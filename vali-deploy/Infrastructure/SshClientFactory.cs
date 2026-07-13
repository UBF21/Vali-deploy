using Renci.SshNet;
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class SshClientFactory : ISshClientFactory
{
    /// <summary>
    /// SSH.NET's BeginExecute/EndExecute puede quedarse esperando para siempre si el comando remoto
    /// deja algún proceso hijo con el file descriptor de salida heredado y abierto (típico de
    /// "docker compose build" con BuildKit, que lanza procesos en segundo plano) — el canal SSH
    /// nunca recibe el EOF final aunque el comando ya haya terminado con éxito del lado del servidor.
    /// Sin este timeout, RunCommandAsync cuelga el pipeline entero sin ningún mensaje.
    /// </summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(15);

    private readonly ISecretResolver _secretResolver;

    public SshClientFactory(ISecretResolver secretResolver) => _secretResolver = secretResolver;

    public async Task<ProcessRunResult> RunCommandAsync(RemoteServer server, string command)
    {
        var client = CreateSshClient(server);
        client.Connect();

        var shellCommand = BuildShellCommand(server.Os, command);
        var sshCommand = client.CreateCommand(shellCommand);

        using var timeoutCts = new CancellationTokenSource();
        var executeTask = Task.Factory.FromAsync(sshCommand.BeginExecute(), sshCommand.EndExecute);
        var timeoutTask = Task.Delay(CommandTimeout, timeoutCts.Token);

        var completedTask = await Task.WhenAny(executeTask, timeoutTask);

        if (completedTask == timeoutTask)
        {
            // No intentamos un Dispose/Disconnect sincrónico acá: el mismo cuelgue de canal que
            // atrapó a executeTask puede atrapar también a Dispose() (ambos dependen de que el
            // servidor mande un EOF que nunca llega). Abandonamos la conexión en background con
            // su propio margen corto, y devolvemos el resultado de timeout sin bloquear al caller.
            _ = CleanUpInBackground(client, sshCommand, executeTask);
            return new ProcessRunResult(-1, "",
                $"El comando no respondió en {(int)CommandTimeout.TotalMinutes} minutos (timeout SSH). Puede haber terminado igual del lado del servidor — revisá manualmente si hace falta.");
        }

        timeoutCts.Cancel();
        var result = await executeTask;
        client.Disconnect();
        sshCommand.Dispose();
        client.Dispose();

        return new ProcessRunResult(sshCommand.ExitStatus, result, sshCommand.Error);
    }

    /// <summary>
    /// Best-effort: le da al canal colgado hasta 30s más para cerrar solo antes de forzar la
    /// desconexión. Cualquier excepción acá se descarta a propósito — ya devolvimos el resultado
    /// de timeout al caller, esto es solo housekeeping de la conexión abandonada.
    /// </summary>
    private static async Task CleanUpInBackground(SshClient client, SshCommand sshCommand, Task executeTask)
    {
        await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(30)));

        try { client.Disconnect(); } catch { /* best-effort */ }
        try { sshCommand.Dispose(); } catch { /* best-effort */ }
        try { client.Dispose(); } catch { /* best-effort */ }
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
