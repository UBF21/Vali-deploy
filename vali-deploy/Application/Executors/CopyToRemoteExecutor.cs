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
            stopwatch.Stop();
            return StepResultFactory.NoServer(step, context, stopwatch.Elapsed);
        }

        var localPath = step.Args.GetValueOrDefault("LocalPath");
        if (string.IsNullOrEmpty(localPath))
        {
            localPath = context.LastArtifactPath;
        }
        if (string.IsNullOrEmpty(localPath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"LocalPath\"] o un step anterior que produzca un artifact (ej. ZipPublishOutput).");
        }
        if (!Path.IsPathRooted(localPath))
        {
            localPath = Path.Combine(context.ProjectPath, localPath);
        }

        if (!step.Args.TryGetValue("RemotePath", out var remotePath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"RemotePath\"].");
        }

        try
        {
            await _sshClientFactory.UploadFileAsync(context.Environment.Server, localPath, remotePath);
            stopwatch.Stop();
            return new StepResult { Step = step, Success = true, ExitCode = 0, Command = $"upload {localPath} → {remotePath}", Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1, Error = ex.Message,
                Command = $"upload {localPath} → {remotePath}", Duration = stopwatch.Elapsed
            };
        }
    }
}
