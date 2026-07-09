using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class CopyToRemoteExecutorTests
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
    public void Handles_CopyToRemote()
    {
        var executor = new CopyToRemoteExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.CopyToRemote, executor.Handles);
    }

    [Fact]
    public async Task Uploads_local_file_to_remote_path()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/compose.yml", "/opt/app/compose.yml"))
            .Returns(Task.CompletedTask);

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/compose.yml", "/opt/app/compose.yml"), Times.Once);
    }

    [Fact]
    public async Task Reports_failure_when_upload_throws()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("connection reset"));

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Contains("connection reset", result.Error);
    }
}
