using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class CopyToRemoteExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public CopyToRemoteExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.CopyToRemote;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            return NoServerResult(step, context, stopwatch);
        }

        var localPath = step.Args["LocalPath"];
        var remotePath = step.Args["RemotePath"];

        try
        {
            await _sshClientFactory.UploadFileAsync(context.Environment.Server, localPath, remotePath);
            stopwatch.Stop();
            return new StepResult { Step = step, Success = true, ExitCode = 0, Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1, Error = ex.Message, Duration = stopwatch.Elapsed
            };
        }
    }

    private static StepResult NoServerResult(DeployStep step, StepExecutionContext context, Stopwatch stopwatch)
    {
        stopwatch.Stop();
        return new StepResult
        {
            Step = step, Success = false, ExitCode = -1,
            Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
            Duration = stopwatch.Elapsed
        };
    }
}
