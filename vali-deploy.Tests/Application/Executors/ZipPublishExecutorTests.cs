using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class ZipPublishExecutorTests
{
    private static StepExecutionContext Context(string path) => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = path,
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_ZipPublishOutput()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.ZipPublishOutput, executor.Handles);
    }

    [Fact]
    public async Task Runs_clean_build_and_publish_in_order_and_stops_on_first_failure()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null))
                .Callback<string, string, IDictionary<string, string>?>((cmd, _, _) => callOrder.Add(cmd))
                .ReturnsAsync((string cmd, string _, IDictionary<string, string>? _) =>
                    cmd.Contains("build") ? new ProcessRunResult(1, "", "build failed") : new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.False(result.Success);
            Assert.DoesNotContain(callOrder, c => c.Contains("publish"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_fast_when_project_path_does_not_exist()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

        var result = await executor.ExecuteAsync(step, Context("/no/existe/este/path"));

        Assert.False(result.Success);
        Assert.Contains("no existe", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Proceeds_past_clean_step_on_fresh_checkout_without_bin_or_obj()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null))
                .Callback<string, string, IDictionary<string, string>?>((cmd, _, _) => callOrder.Add(cmd))
                .ReturnsAsync(new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.True(result.Success);
            Assert.Contains(callOrder, c => c.Contains("dotnet clean"));
            Assert.Contains(callOrder, c => c.Contains("dotnet build"));
            Assert.Contains(callOrder, c => c.Contains("dotnet publish"));

            if (OperatingSystem.IsWindows())
            {
                // rmdir encadenado con && falla con exit code != 0 cuando bin/obj no existen todavía
                // (checkout fresco): el comando de limpieza debe usar "if exist" + "&" simple, no "&&".
                Assert.DoesNotContain("&&", callOrder[0]);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_rmdir_chained_with_double_ampersand_fails_when_bin_and_obj_are_missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var runner = new ProcessRunner();

            var result = await runner.RunAsync("rmdir /s /q bin && rmdir /s /q obj", tempDir);

            Assert.NotEqual(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Windows_conditional_rmdir_succeeds_when_bin_and_obj_are_missing()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var runner = new ProcessRunner();

            var result = await runner.RunAsync(
                "(if exist bin rmdir /s /q bin) & (if exist obj rmdir /s /q obj)", tempDir);

            Assert.Equal(0, result.ExitCode);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
