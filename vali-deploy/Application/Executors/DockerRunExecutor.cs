using System.Diagnostics;
using vali_deploy.Domain;

namespace vali_deploy.Application.Executors;

/// <summary>
/// Ejecuta "docker run -it --rm" heredando la consola del proceso padre (sin redirigir stdin/stdout/stderr),
/// para que el usuario pueda interactuar con el contenedor. A diferencia del resto de los IStepExecutor,
/// no depende de IProcessRunner: esa abstracción captura la salida como texto para loguearla, lo cual es
/// incompatible con una sesión interactiva real. La salida de este step nunca queda en el log del pipeline.
/// </summary>
public class DockerRunExecutor : IStepExecutor
{
    public StepType Handles => StepType.DockerRun;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var runArgs = step.Args.GetValueOrDefault("RunArgs", "");
        var runArgsSuffix = string.IsNullOrWhiteSpace(runArgs) ? "" : $" {runArgs}";
        var command = $"docker run -it --rm{runArgsSuffix} {imageTag}";

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = context.ProjectPath,
            UseShellExecute = false
        };
        startInfo.Environment["DOCKER_BUILDKIT"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            Output = "(sesión interactiva — salida no capturada)",
            Duration = stopwatch.Elapsed
        };
    }
}
