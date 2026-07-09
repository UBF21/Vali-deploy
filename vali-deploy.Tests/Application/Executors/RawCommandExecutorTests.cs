using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class RawCommandExecutorTests
{
    [Fact]
    public void Handles_RawCommand()
    {
        var executor = new RawCommandExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.RawCommand, executor.Handles);
    }

    [Fact]
    public async Task ExecuteAsync_runs_Args_Command_verbatim()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("echo custom", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "custom", ""));

        var executor = new RawCommandExecutor(processRunner.Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "QA" }
        };
        var step = new DeployStep { Type = StepType.RawCommand, Name = "custom", Args = { ["Command"] = "echo custom" } };

        var result = await executor.ExecuteAsync(step, context);

        Assert.True(result.Success);
    }
}
