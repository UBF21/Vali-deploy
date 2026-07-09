using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_returns_exit_code_zero_and_captures_stdout_on_success()
    {
        var runner = new ProcessRunner();
        var command = OperatingSystem.IsWindows() ? "echo hola" : "echo hola";

        var result = await runner.RunAsync(command, Directory.GetCurrentDirectory());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hola", result.StdOut);
    }

    [Fact]
    public async Task RunAsync_returns_nonzero_exit_code_on_failure()
    {
        var runner = new ProcessRunner();
        var command = OperatingSystem.IsWindows() ? "exit 3" : "exit 3";

        var result = await runner.RunAsync(command, Directory.GetCurrentDirectory());

        Assert.Equal(3, result.ExitCode);
    }
}
