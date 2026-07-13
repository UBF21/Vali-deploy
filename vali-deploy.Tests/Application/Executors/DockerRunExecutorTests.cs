using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerRunExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerRun()
    {
        var executor = new DockerRunExecutor(new Mock<IInteractiveProcessLauncher>().Object);
        Assert.Equal(StepType.DockerRun, executor.Handles);
    }

    [Fact]
    public async Task Records_the_docker_run_command_on_the_result()
    {
        var launcher = new Mock<IInteractiveProcessLauncher>();
        launcher
            .Setup(l => l.RunInheritingConsoleAsync(
                "docker run -it --rm --name proj-sub proj-sub:latest",
                "/tmp/proj",
                It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(0);

        var executor = new DockerRunExecutor(launcher.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerRun, Name = "run image",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RunArgs"] = "--name proj-sub" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        Assert.Equal("docker run -it --rm --name proj-sub proj-sub:latest", result.Command);
    }
}
