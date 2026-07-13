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
        Assert.Equal("upload /tmp/proj/compose.yml → /opt/app/compose.yml", result.Command);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/compose.yml", "/opt/app/compose.yml"), Times.Once);
    }

    [Fact]
    public async Task Resolves_relative_LocalPath_against_ProjectPath()
    {
        var expectedLocalPath = Path.Combine("/tmp/proj", "compose.yml");
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), expectedLocalPath, "/opt/app/compose.yml"))
            .Returns(Task.CompletedTask);

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), expectedLocalPath, "/opt/app/compose.yml"), Times.Once);
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
        Assert.Equal("upload /tmp/proj/compose.yml → /opt/app/compose.yml", result.Command);
    }

    [Fact]
    public async Task Fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new CopyToRemoteExecutor(new Mock<ISshClientFactory>().Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "DEV" }
        };
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, context);

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }

    [Fact]
    public async Task ExecuteAsync_falls_back_to_context_LastArtifactPath_when_LocalPath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/sub-20260101.zip", "/opt/app/sub.zip"))
            .Returns(Task.CompletedTask);

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy zip",
            Args = { ["RemotePath"] = "/opt/app/sub.zip" }
        };
        var context = Context();
        context.LastArtifactPath = "/tmp/proj/sub-20260101.zip";

        var result = await executor.ExecuteAsync(step, context);

        Assert.True(result.Success);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/sub-20260101.zip", "/opt/app/sub.zip"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_LocalPath_arg_missing_and_no_LastArtifactPath_in_context()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'copy compose' (CopyToRemote) requiere Args[\"LocalPath\"] o un step anterior que produzca un artifact (ej. ZipPublishOutput).", ex.Message);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_throws_clear_error_when_RemotePath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml" }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'copy compose' (CopyToRemote) requiere Args[\"RemotePath\"].", ex.Message);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
