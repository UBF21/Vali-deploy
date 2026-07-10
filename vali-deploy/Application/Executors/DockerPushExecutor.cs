using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerPushExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;
    private readonly ISecretResolver _secretResolver;

    public DockerPushExecutor(IProcessRunner processRunner, ISecretResolver secretResolver)
    {
        _processRunner = processRunner;
        _secretResolver = secretResolver;
    }

    public StepType Handles => StepType.DockerPush;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var registryTag = step.Args["RegistryTag"];
        var extraEnv = new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" };

        var loginRun = await TryLoginAsync(step, context, extraEnv);
        if (loginRun != null && loginRun.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, loginRun, loginRun.StdOut, stopwatch.Elapsed);
        }

        var tagRun = await _processRunner.RunAsync($"docker tag {imageTag} {registryTag}", context.ProjectPath, extraEnv);

        if (tagRun.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, tagRun, tagRun.StdOut, stopwatch.Elapsed);
        }

        var pushRun = await _processRunner.RunAsync($"docker push {registryTag}", context.ProjectPath, extraEnv);
        stopwatch.Stop();

        return BuildResult(step, pushRun, tagRun.StdOut + pushRun.StdOut, stopwatch.Elapsed);
    }

    private async Task<ProcessRunResult?> TryLoginAsync(DeployStep step, StepExecutionContext context, IDictionary<string, string> extraEnv)
    {
        var registryHost = step.Args.GetValueOrDefault("RegistryHost", "");
        var registryUsername = step.Args.GetValueOrDefault("RegistryUsername", "");
        var registryTokenEnvVar = step.Args.GetValueOrDefault("RegistryTokenEnvVar", "");

        if (string.IsNullOrEmpty(registryTokenEnvVar))
        {
            return null;
        }

        var token = _secretResolver.Resolve(registryTokenEnvVar);
        var loginCommand = string.IsNullOrEmpty(registryHost)
            ? $"docker login -u {registryUsername} --password-stdin"
            : $"docker login {registryHost} -u {registryUsername} --password-stdin";

        return await _processRunner.RunAsync(loginCommand, context.ProjectPath, extraEnv, token);
    }

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Duration = duration
    };
}
