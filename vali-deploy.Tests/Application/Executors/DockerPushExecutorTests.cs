using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerPushExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerPush()
    {
        var executor = new DockerPushExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerPush, executor.Handles);
    }

    [Fact]
    public async Task Tags_then_pushes_image_to_registry()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var executor = new DockerPushExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
        processRunner.Verify(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
    }

    [Fact]
    public async Task Stops_at_tag_failure_without_attempting_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(1, "", "no such image"));

        var executor = new DockerPushExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), null), Times.Never);
    }
}
