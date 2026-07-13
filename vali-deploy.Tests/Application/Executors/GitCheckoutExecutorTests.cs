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
        processRunner.Setup(p => p.RunAsync("git checkout develop", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "Already up to date.", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout", Args = { ["Branch"] = "develop" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", "/tmp/proj", null, null), Times.Once);
        Assert.Equal("git checkout develop && git pull", result.Command);
    }

    [Fact]
    public async Task Falls_back_to_environment_DefaultBranch_when_Args_Branch_missing()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout" };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git checkout main", "/tmp/proj", null, null), Times.Once);
    }

    [Fact]
    public async Task Does_not_pull_when_SyncBeforeBuild_is_false()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["SyncBeforeBuild"] = "false" }
        };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", It.IsAny<string>(), null, null), Times.Never);
        Assert.Equal("git checkout main", result.Command);
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

    [Fact]
    public async Task Rejects_branch_with_shell_metacharacters_without_calling_ProcessRunner()
    {
        var processRunner = new Mock<IProcessRunner>();
        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["Branch"] = "main; rm -rf /" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Contains("rama", result.Error, StringComparison.OrdinalIgnoreCase);
        processRunner.Verify(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), null), Times.Never);
    }

    [Fact]
    public async Task Rejects_branch_with_leading_dash_without_calling_ProcessRunner()
    {
        var processRunner = new Mock<IProcessRunner>();
        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["Branch"] = "--force" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Contains("rama", result.Error, StringComparison.OrdinalIgnoreCase);
        processRunner.Verify(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), null), Times.Never);
    }

    [Fact]
    public async Task Captures_checkout_stderr_when_SyncBeforeBuild_is_false()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", "warning: some message"));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["SyncBeforeBuild"] = "false" }
        };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        Assert.Equal("warning: some message", result.Error);
    }

    [Fact]
    public async Task Accepts_valid_branch_name_with_typical_git_ref_characters()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout feature/my-branch_v2.1", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["Branch"] = "feature/my-branch_v2.1" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git checkout feature/my-branch_v2.1", "/tmp/proj", null, null), Times.Once);
    }
}
