using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerComposeDownExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerComposeDownExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerComposeDown;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            return StepResultFactory.NoServer(step, context, stopwatch.Elapsed);
        }

        if (!step.Args.TryGetValue("ComposeFilePath", out var composeFilePath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"ComposeFilePath\"].");
        }

        var command = $"docker compose -f \"{composeFilePath}\" down";
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, command, stopwatch.Elapsed);
    }
}
