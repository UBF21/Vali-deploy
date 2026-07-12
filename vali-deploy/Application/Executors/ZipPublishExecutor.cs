using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Text;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class ZipPublishExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public ZipPublishExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.ZipPublishOutput;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Directory.Exists(context.ProjectPath))
        {
            stopwatch.Stop();
            return PathNotFoundResult(step, context.ProjectPath, stopwatch.Elapsed);
        }

        var combinedOutput = new StringBuilder();

        foreach (var command in BuildCommands(step))
        {
            var run = await _processRunner.RunAsync(command, context.ProjectPath);
            combinedOutput.AppendLine(run.StdOut);

            if (run.ExitCode != 0)
            {
                stopwatch.Stop();
                return FailureResult(step, run, combinedOutput.ToString(), stopwatch.Elapsed);
            }
        }

        var publishFolder = FindPublishFolder(context.ProjectPath);
        if (publishFolder == null)
        {
            stopwatch.Stop();
            return PublishFolderNotFoundResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
        }

        var omitFiles = ParseOmitFiles(step);
        var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
        combinedOutput.AppendLine($"Comprimido en: {zipPath}");
        context.LastArtifactPath = zipPath;

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
    }

    private static string[] BuildCommands(DeployStep step)
    {
        var publishArgs = step.Args.GetValueOrDefault("PublishArgs", "");

        return CleanCommands()
            .Append("dotnet clean")
            .Append("dotnet build")
            .Append($"dotnet publish -c Release {publishArgs}".TrimEnd())
            .ToArray();
    }

    private static IEnumerable<string> CleanCommands()
    {
        if (OperatingSystem.IsWindows())
        {
            // "bin" y "obj" van como comandos independientes -no encadenados con "&"/"&&"- para que el loop
            // de ExecuteAsync verifique el exit code de cada uno por separado. Un "&"/"&&" combinado deja el
            // exit code final en manos del último comando, enmascarando un fallo real del primero (p.ej. un
            // archivo bloqueado dentro de "bin") si el segundo ("obj") termina en éxito.
            return new[] { "if exist bin rmdir /s /q bin", "if exist obj rmdir /s /q obj" };
        }

        // rm -rf no falla si el path no existe (no-op, exit 0) y, a diferencia de rmdir en Windows, un
        // error real (p.ej. permisos) sí se propaga como exit code no-cero de este único comando -no hay
        // aquí el mismo riesgo de enmascaramiento-, por eso no hace falta separarlo en dos.
        return new[] { "rm -rf bin; rm -rf obj" };
    }

    private static string? FindPublishFolder(string projectPath)
    {
        var releaseFolder = Path.Combine(projectPath, "bin", "Release");
        if (!Directory.Exists(releaseFolder)) return null;

        return Directory.EnumerateDirectories(releaseFolder, "publish", SearchOption.AllDirectories)
            .OrderBy(dir => dir.Count(c => c == Path.DirectorySeparatorChar))
            .FirstOrDefault();
    }

    private static List<string> ParseOmitFiles(DeployStep step)
    {
        var raw = step.Args.GetValueOrDefault("OmitFiles", "");
        return string.IsNullOrEmpty(raw) ? new List<string>() : raw.Split('|').ToList();
    }

    private static string CreateZip(string publishFolder, string subProjectName, List<string> omitFiles)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var parentFolder = Directory.GetParent(publishFolder)?.FullName ?? publishFolder;
        var zipPath = Path.Combine(parentFolder, $"{subProjectName}-{timestamp}.zip");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(publishFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(publishFolder, filePath).Replace('\\', '/');
            if (omitFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase)) continue;
            zip.CreateEntryFromFile(filePath, relativePath);
        }

        return zipPath;
    }

    private static StepResult PathNotFoundResult(DeployStep step, string path, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = -1,
        Error = $"El path del proyecto no existe: {path}", Duration = duration
    };

    private static StepResult PublishFolderNotFoundResult(DeployStep step, string output, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = -1,
        Output = output, Error = "No se encontró la carpeta 'publish' dentro de bin/Release tras el build.",
        Duration = duration
    };

    private static StepResult FailureResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = run.ExitCode, Output = output, Error = run.StdErr, Duration = duration
    };

    private static StepResult SuccessResult(DeployStep step, string output, TimeSpan duration) => new()
    {
        Step = step, Success = true, ExitCode = 0, Output = output, Duration = duration
    };
}
