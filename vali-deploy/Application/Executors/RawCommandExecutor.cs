using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class RawCommandExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public RawCommandExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.RawCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = step.Args["Command"];
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = run.ExitCode == 0,
            ExitCode = run.ExitCode,
            Output = run.StdOut,
            Error = run.StdErr,
            Duration = stopwatch.Elapsed
        };
    }
}
