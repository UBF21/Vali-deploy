using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class SshClientFactoryTests
{
    // One literal double-quote character, used to compose the expected strings below without
    // drowning the assertions in hard-to-eyeball backslash-escaping.
    private const string Q = "\"";

    [Fact]
    public void BuildShellCommand_wraps_simple_linux_command_in_bash_dash_c()
    {
        var result = SshClientFactory.BuildShellCommand(RemoteOs.Linux, "dotnet --version");

        Assert.Equal($"bash -c {Q}dotnet --version{Q}", result);
    }

    [Fact]
    public void BuildShellCommand_escapes_embedded_double_quotes_for_linux()
    {
        var command = $"docker exec app sh -c {Q}echo hi{Q}";

        var result = SshClientFactory.BuildShellCommand(RemoteOs.Linux, command);

        Assert.Equal($"bash -c {Q}docker exec app sh -c \\{Q}echo hi\\{Q}{Q}", result);
    }

    [Fact]
    public void BuildShellCommand_wraps_simple_windows_command_in_powershell_dash_command()
    {
        var result = SshClientFactory.BuildShellCommand(RemoteOs.Windows, "Get-Process");

        Assert.Equal($"powershell -Command {Q}Get-Process{Q}", result);
    }

    [Fact]
    public void BuildShellCommand_escapes_embedded_double_quotes_for_windows_with_backtick()
    {
        var command = $"Write-Host {Q}hi{Q}";

        var result = SshClientFactory.BuildShellCommand(RemoteOs.Windows, command);

        Assert.Equal($"powershell -Command {Q}Write-Host `{Q}hi`{Q}{Q}", result);
    }
}
