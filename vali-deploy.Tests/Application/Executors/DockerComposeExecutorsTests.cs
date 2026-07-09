using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerComposeExecutorsTests
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

    private static StepExecutionContext ContextWithoutServer() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "DEV" }
    };

    private static DeployStep ComposeStep(StepType type) => new()
    {
        Type = type, Name = type.ToString(), Args = { ["ComposeFilePath"] = "/opt/app/compose.yml" }
    };

    private static DeployStep ComposeStepWithoutArgs(StepType type) => new()
    {
        Type = type, Name = type.ToString()
    };

    [Fact]
    public async Task Pull_runs_docker_compose_pull_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" pull"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposePullExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposePull, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposePull), Context());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Up_runs_docker_compose_up_detached_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" up -d"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposeUpExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposeUp, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeUp), Context());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Down_runs_docker_compose_down_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" down"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposeDownExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposeDown, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeDown), Context());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Pull_fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new DockerComposePullExecutor(new Mock<ISshClientFactory>().Object);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposePull), ContextWithoutServer());

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }

    [Fact]
    public async Task Up_fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new DockerComposeUpExecutor(new Mock<ISshClientFactory>().Object);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeUp), ContextWithoutServer());

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }

    [Fact]
    public async Task Down_fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new DockerComposeDownExecutor(new Mock<ISshClientFactory>().Object);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeDown), ContextWithoutServer());

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }

    [Fact]
    public async Task Pull_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new DockerComposePullExecutor(sshFactory.Object);
        var step = ComposeStepWithoutArgs(StepType.DockerComposePull);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'DockerComposePull' (DockerComposePull) requiere Args[\"ComposeFilePath\"].", ex.Message);
        sshFactory.Verify(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Up_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new DockerComposeUpExecutor(sshFactory.Object);
        var step = ComposeStepWithoutArgs(StepType.DockerComposeUp);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'DockerComposeUp' (DockerComposeUp) requiere Args[\"ComposeFilePath\"].", ex.Message);
        sshFactory.Verify(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Down_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new DockerComposeDownExecutor(sshFactory.Object);
        var step = ComposeStepWithoutArgs(StepType.DockerComposeDown);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'DockerComposeDown' (DockerComposeDown) requiere Args[\"ComposeFilePath\"].", ex.Message);
        sshFactory.Verify(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), It.IsAny<string>()), Times.Never);
    }
}
