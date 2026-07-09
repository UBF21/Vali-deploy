using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerBuildExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerBuildExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerBuild;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = BuildCommand(step, context);

        var run = await _processRunner.RunAsync(command, context.ProjectPath, new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" });
        stopwatch.Stop();

        return BuildResult(step, run, stopwatch.Elapsed);
    }

    private static string BuildCommand(DeployStep step, StepExecutionContext context)
    {
        var dockerfile = step.Args["Dockerfile"];
        var imageTag = step.Args["ImageTag"];
        var buildArgs = step.Args.GetValueOrDefault("BuildArgs");
        var buildArgsSuffix = string.IsNullOrWhiteSpace(buildArgs) ? "" : $" {buildArgs}";
        var projectLabel = $"{context.ProjectName.ToLower()}-{context.SubProjectName.ToLower()}";

        return $"docker build -f \"{dockerfile}\" -t {imageTag}{buildArgsSuffix} --label project={projectLabel} \"{context.ProjectPath}\"";
    }

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = run.StdOut,
        Error = run.StdErr,
        Duration = duration
    };
}
