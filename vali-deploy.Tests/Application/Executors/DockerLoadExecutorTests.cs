using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerLoadExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = RemoteOs.Linux, PrivateKeyPath = "/key" }
        }
    };

    [Fact]
    public void Handles_DockerLoad()
    {
        var executor = new DockerLoadExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.DockerLoad, executor.Handles);
    }

    [Fact]
    public async Task Loads_tar_on_remote_via_ssh()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker load -i \"/opt/app/image.tar\""))
            .ReturnsAsync(new ProcessRunResult(0, "Loaded image", ""));

        var executor = new DockerLoadExecutor(sshFactory.Object);
        var step = new DeployStep { Type = StepType.DockerLoad, Name = "load", Args = { ["RemoteTarPath"] = "/opt/app/image.tar" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        Assert.Equal("docker load -i \"/opt/app/image.tar\"", result.Command);
    }

    [Fact]
    public async Task Fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new DockerLoadExecutor(new Mock<ISshClientFactory>().Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "DEV" }
        };
        var step = new DeployStep { Type = StepType.DockerLoad, Name = "load", Args = { ["RemoteTarPath"] = "/opt/app/image.tar" } };

        var result = await executor.ExecuteAsync(step, context);

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_throws_clear_error_when_RemoteTarPath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new DockerLoadExecutor(sshFactory.Object);
        var step = new DeployStep { Type = StepType.DockerLoad, Name = "load" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'load' (DockerLoad) requiere Args[\"RemoteTarPath\"].", ex.Message);
        sshFactory.Verify(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), It.IsAny<string>()), Times.Never);
    }
}
