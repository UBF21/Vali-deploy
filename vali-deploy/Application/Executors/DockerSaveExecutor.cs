using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerSaveExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerSaveExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerSave;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var outputTarPath = step.Args["OutputTarPath"];

        var run = await _processRunner.RunAsync($"docker save -o \"{outputTarPath}\" {imageTag}", context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
