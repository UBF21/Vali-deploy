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
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1,
                Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
                Duration = stopwatch.Elapsed
            };
        }

        var remoteTarPath = step.Args["RemoteTarPath"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, $"docker load -i \"{remoteTarPath}\"");
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
