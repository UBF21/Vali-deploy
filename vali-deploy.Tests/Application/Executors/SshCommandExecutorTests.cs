using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class SshCommandExecutorTests
{
    private static StepExecutionContext ContextWithServer(RemoteOs os) => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = os, PrivateKeyPath = "/key" }
        }
    };

    [Fact]
    public void Handles_SshCommand()
    {
        var executor = new SshCommandExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.SshCommand, executor.Handles);
    }

    [Fact]
    public async Task Runs_command_on_remote_server_from_environment()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "systemctl restart myapp"))
            .ReturnsAsync(new ProcessRunResult(0, "restarted", ""));

        var executor = new SshCommandExecutor(sshFactory.Object);
        var step = new DeployStep { Type = StepType.SshCommand, Name = "restart", Args = { ["Command"] = "systemctl restart myapp" } };

        var result = await executor.ExecuteAsync(step, ContextWithServer(RemoteOs.Linux));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new SshCommandExecutor(new Mock<ISshClientFactory>().Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "DEV" }
        };
        var step = new DeployStep { Type = StepType.SshCommand, Name = "restart", Args = { ["Command"] = "echo hi" } };

        var result = await executor.ExecuteAsync(step, context);

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }
}
