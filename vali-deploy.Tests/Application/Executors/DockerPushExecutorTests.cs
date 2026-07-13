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
        var executor = new DockerPushExecutor(new Mock<IProcessRunner>().Object, new Mock<ISecretResolver>().Object);
        Assert.Equal(StepType.DockerPush, executor.Handles);
    }

    [Fact]
    public async Task Tags_then_pushes_image_to_registry_without_login_when_no_TokenEnvVar()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var executor = new DockerPushExecutor(processRunner.Object, new Mock<ISecretResolver>().Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker login")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
        processRunner.Verify(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
        processRunner.Verify(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
        Assert.Equal("docker tag proj-sub:latest myuser/proj-sub:latest && docker push myuser/proj-sub:latest", result.Command);
    }

    [Fact]
    public async Task Logs_in_with_resolved_token_before_tag_and_push_when_TokenEnvVar_is_set()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker login ghcr.io -u myorg --password-stdin", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), "resolved-token"))
            .ReturnsAsync(new ProcessRunResult(0, "Login Succeeded", ""));
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var secretResolver = new Mock<ISecretResolver>();
        secretResolver.Setup(s => s.Resolve("GHCR_TOKEN")).Returns("resolved-token");

        var executor = new DockerPushExecutor(processRunner.Object, secretResolver.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args =
            {
                ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "ghcr.io/myorg/proj-sub:latest",
                ["RegistryHost"] = "ghcr.io", ["RegistryUsername"] = "myorg", ["RegistryTokenEnvVar"] = "GHCR_TOKEN"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("docker login ghcr.io -u myorg --password-stdin", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), "resolved-token"), Times.Once);
        Assert.Equal(
            "docker login ghcr.io -u myorg --password-stdin && docker tag proj-sub:latest ghcr.io/myorg/proj-sub:latest && docker push ghcr.io/myorg/proj-sub:latest",
            result.Command);
    }

    [Fact]
    public async Task Stops_at_login_failure_without_attempting_tag_or_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker login")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "unauthorized"));

        var secretResolver = new Mock<ISecretResolver>();
        secretResolver.Setup(s => s.Resolve("GHCR_TOKEN")).Returns("bad-token");

        var executor = new DockerPushExecutor(processRunner.Object, secretResolver.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args =
            {
                ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "ghcr.io/myorg/proj-sub:latest",
                ["RegistryHost"] = "ghcr.io", ["RegistryUsername"] = "myorg", ["RegistryTokenEnvVar"] = "GHCR_TOKEN"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
        Assert.Equal("docker login ghcr.io -u myorg --password-stdin", result.Command);
    }

    [Fact]
    public async Task Stops_at_tag_failure_without_attempting_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(1, "", "no such image"));

        var executor = new DockerPushExecutor(processRunner.Object, new Mock<ISecretResolver>().Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
        Assert.Equal("docker tag proj-sub:latest myuser/proj-sub:latest", result.Command);
    }
}
