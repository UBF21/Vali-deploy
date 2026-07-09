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
    }
}
