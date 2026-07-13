using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerImagePruneExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerImagePruneExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerImagePrune;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageNameFilter = step.Args["ImageNameFilter"];

        var command = $"docker image prune -f --filter \"label=project={imageNameFilter}\"";
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Command = command, Duration = stopwatch.Elapsed
        };
    }
}
