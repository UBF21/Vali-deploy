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

        var loginCommand = await TryLoginAsync(step, context, extraEnv);
        if (loginCommand.Run != null && loginCommand.Run.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, loginCommand.Run, loginCommand.Run.StdOut, loginCommand.Command ?? "", stopwatch.Elapsed);
        }

        var tagCommand = $"docker tag {imageTag} {registryTag}";
        var tagRun = await _processRunner.RunAsync(tagCommand, context.ProjectPath, extraEnv);

        if (tagRun.ExitCode != 0)
        {
            stopwatch.Stop();
            var commandSoFar = loginCommand.Command != null ? $"{loginCommand.Command} && {tagCommand}" : tagCommand;
            return BuildResult(step, tagRun, tagRun.StdOut, commandSoFar, stopwatch.Elapsed);
        }

        var pushCommand = $"docker push {registryTag}";
        var pushRun = await _processRunner.RunAsync(pushCommand, context.ProjectPath, extraEnv);
        stopwatch.Stop();

        var fullCommand = loginCommand.Command != null ? $"{loginCommand.Command} && {tagCommand} && {pushCommand}" : $"{tagCommand} && {pushCommand}";
        return BuildResult(step, pushRun, tagRun.StdOut + pushRun.StdOut, fullCommand, stopwatch.Elapsed);
    }

    private async Task<(ProcessRunResult? Run, string? Command)> TryLoginAsync(DeployStep step, StepExecutionContext context, IDictionary<string, string> extraEnv)
    {
        var registryHost = step.Args.GetValueOrDefault("RegistryHost", "");
        var registryUsername = step.Args.GetValueOrDefault("RegistryUsername", "");
        var registryTokenEnvVar = step.Args.GetValueOrDefault("RegistryTokenEnvVar", "");

        if (string.IsNullOrEmpty(registryTokenEnvVar))
        {
            return (null, null);
        }

        var token = _secretResolver.Resolve(registryTokenEnvVar);
        var loginCommand = string.IsNullOrEmpty(registryHost)
            ? $"docker login -u {registryUsername} --password-stdin"
            : $"docker login {registryHost} -u {registryUsername} --password-stdin";

        var run = await _processRunner.RunAsync(loginCommand, context.ProjectPath, extraEnv, token);
        return (run, loginCommand);
    }

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, string command, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Command = command,
        Duration = duration
    };
}
