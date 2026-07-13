using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class LocalCommandExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public LocalCommandExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.LocalCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!step.Args.TryGetValue("Command", out var command))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"Command\"].");
        }

        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = run.ExitCode == 0,
            ExitCode = run.ExitCode,
            Output = run.StdOut,
            Error = run.StdErr,
            Command = command,
            Duration = stopwatch.Elapsed
        };
    }
}
