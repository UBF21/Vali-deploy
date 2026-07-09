using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface ISshClientFactory
{
    Task<ProcessRunResult> RunCommandAsync(RemoteServer server, string command);
    Task UploadFileAsync(RemoteServer server, string localPath, string remotePath);
}
