using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerLoadExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerLoadExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerLoad;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            stopwatch.Stop();
            return StepResultFactory.NoServer(step, context, stopwatch.Elapsed);
        }

        if (!step.Args.TryGetValue("RemoteTarPath", out var remoteTarPath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"RemoteTarPath\"].");
        }

        var command = $"docker load -i \"{remoteTarPath}\"";
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, command, stopwatch.Elapsed);
    }
}
