using System.Diagnostics;
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

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
    }

    private static string[] BuildCommands(DeployStep step)
    {
        var publishArgs = step.Args.GetValueOrDefault("PublishArgs", "");
        var cleanCommand = OperatingSystem.IsWindows()
            ? "(if exist bin rmdir /s /q bin) & (if exist obj rmdir /s /q obj)"
            : "rm -rf bin; rm -rf obj";

        return new[]
        {
            cleanCommand,
            "dotnet clean",
            "dotnet build",
            $"dotnet publish -c Release {publishArgs}".TrimEnd()
        };
    }

    private static StepResult PathNotFoundResult(DeployStep step, string path, TimeSpan duration) => new()
    {
        Step = step,
        Success = false,
        ExitCode = -1,
        Error = $"El path del proyecto no existe: {path}",
        Duration = duration
    };

    private static StepResult FailureResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step,
        Success = false,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Duration = duration
    };

    private static StepResult SuccessResult(DeployStep step, string output, TimeSpan duration) => new()
    {
        Step = step,
        Success = true,
        ExitCode = 0,
        Output = output,
        Duration = duration
    };
}
