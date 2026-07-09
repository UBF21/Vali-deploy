using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class GitCheckoutExecutorTests
{
    private static StepExecutionContext Context(string? defaultBranch = "main") => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA", DefaultBranch = defaultBranch }
    };

    [Fact]
    public void Handles_GitCheckout()
    {
        var executor = new GitCheckoutExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.GitCheckout, executor.Handles);
    }

    [Fact]
    public async Task Checks_out_branch_from_Args_and_pulls_by_default()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout develop", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "Already up to date.", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout", Args = { ["Branch"] = "develop" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", "/tmp/proj", null), Times.Once);
    }

    [Fact]
    public async Task Falls_back_to_environment_DefaultBranch_when_Args_Branch_missing()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout" };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git checkout main", "/tmp/proj", null), Times.Once);
    }

    [Fact]
    public async Task Does_not_pull_when_SyncBeforeBuild_is_false()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["SyncBeforeBuild"] = "false" }
        };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", It.IsAny<string>(), null), Times.Never);
    }

    [Fact]
    public async Task Fails_fast_with_clear_message_when_no_branch_available()
    {
        var executor = new GitCheckoutExecutor(new Mock<IProcessRunner>().Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout" };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: null));

        Assert.False(result.Success);
        Assert.Contains("rama", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
