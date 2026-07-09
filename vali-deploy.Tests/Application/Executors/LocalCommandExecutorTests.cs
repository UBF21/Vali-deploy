using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class LocalCommandExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj",
        SubProjectName = "sub",
        ProjectPath = Directory.GetCurrentDirectory(),
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_LocalCommand()
    {
        var executor = new LocalCommandExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.LocalCommand, executor.Handles);
    }

    [Fact]
    public async Task ExecuteAsync_runs_Args_Command_in_ProjectPath_and_reports_success_on_exit_zero()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("dotnet build", Context().ProjectPath, null))
            .ReturnsAsync(new ProcessRunResult(0, "Build succeeded", ""));

        var executor = new LocalCommandExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "build", Args = { ["Command"] = "dotnet build" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_reports_failure_on_nonzero_exit()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), null))
            .ReturnsAsync(new ProcessRunResult(1, "", "error CS0000"));

        var executor = new LocalCommandExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "build", Args = { ["Command"] = "dotnet build" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error CS0000", result.Error);
    }
}
