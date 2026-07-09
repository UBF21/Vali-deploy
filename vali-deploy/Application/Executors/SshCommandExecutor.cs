using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class SshCommandExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public SshCommandExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.SshCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            stopwatch.Stop();
            return StepResultFactory.NoServer(step, context, stopwatch.Elapsed);
        }

        if (!step.Args.TryGetValue("Command", out var command))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"Command\"].");
        }

        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, stopwatch.Elapsed);
    }
}
