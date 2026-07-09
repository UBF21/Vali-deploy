using Renci.SshNet;
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class SshClientFactory : ISshClientFactory
{
    private readonly ISecretResolver _secretResolver;

    public SshClientFactory(ISecretResolver secretResolver) => _secretResolver = secretResolver;

    public async Task<ProcessRunResult> RunCommandAsync(RemoteServer server, string command)
    {
        using var client = CreateSshClient(server);
        client.Connect();

        var shellCommand = server.Os == RemoteOs.Windows
            ? $"powershell -Command \"{command}\""
            : $"bash -c \"{command}\"";

        using var sshCommand = client.CreateCommand(shellCommand);
        var result = await Task.Factory.FromAsync(sshCommand.BeginExecute(), sshCommand.EndExecute);

        client.Disconnect();

        return new ProcessRunResult(sshCommand.ExitStatus, result, sshCommand.Error);
    }

    public async Task UploadFileAsync(RemoteServer server, string localPath, string remotePath)
    {
        using var client = new SftpClient(BuildConnectionInfo(server));
        client.Connect();

        await using var fileStream = File.OpenRead(localPath);
        await Task.Run(() => client.UploadFile(fileStream, remotePath));

        client.Disconnect();
    }

    private SshClient CreateSshClient(RemoteServer server) => new(BuildConnectionInfo(server));

    private ConnectionInfo BuildConnectionInfo(RemoteServer server)
    {
        var passphrase = server.PassphraseEnvVar != null ? _secretResolver.Resolve(server.PassphraseEnvVar) : null;
        var keyFile = passphrase != null
            ? new PrivateKeyFile(server.PrivateKeyPath, passphrase)
            : new PrivateKeyFile(server.PrivateKeyPath);

        return new ConnectionInfo(server.Host, server.Port, server.User, new PrivateKeyAuthenticationMethod(server.User, keyFile));
    }
}
