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

        var shellCommand = BuildShellCommand(server.Os, command);

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

    /// <summary>
    /// Builds the shell invocation string used to run <paramref name="command"/> on the remote host.
    /// Escapes embedded double quotes (and, for bash, backslashes) so a command that itself contains
    /// quotes - e.g. <c>docker exec app sh -c "echo hi"</c> - does not close the outer quoting early
    /// and get re-parsed as separate tokens by the remote shell.
    /// </summary>
    /// <remarks>
    /// Public (rather than private) so the escaping logic is directly unit-testable as a pure string
    /// function, without mocking SSH.NET.
    /// The PowerShell branch only handles the straightforward case of embedded straight double quotes
    /// via the backtick escape (`"). More elaborate PowerShell quoting scenarios (nested single quotes,
    /// $()-subexpressions, a command ending in a backslash right before the closing quote, etc.) are not
    /// exhaustively covered and may need further hardening if such payloads are ever constructed by a
    /// later executor.
    /// </remarks>
    public static string BuildShellCommand(RemoteOs os, string command)
    {
        return os == RemoteOs.Windows
            ? $"powershell -Command \"{command.Replace("\"", "`\"")}\""
            : $"bash -c \"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
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
