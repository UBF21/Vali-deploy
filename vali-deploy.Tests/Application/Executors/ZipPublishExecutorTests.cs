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
}
