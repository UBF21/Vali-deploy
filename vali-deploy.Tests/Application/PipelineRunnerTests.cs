using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class PipelineRunnerTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj",
        SubProjectName = "sub",
        ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public async Task Pipeline_succeeds_when_all_steps_succeed()
    {
        var executor = new Mock<IStepExecutor>();
        executor.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        executor.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { executor.Object });
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "clean" } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.True(result.Success);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task Pipeline_stops_at_first_failure_by_default()
    {
        var failing = new Mock<IStepExecutor>();
        failing.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        failing.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = false, ExitCode = 1 });

        var neverCalled = new Mock<IStepExecutor>();
        neverCalled.Setup(e => e.Handles).Returns(StepType.DockerBuild);
        neverCalled.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { failing.Object, neverCalled.Object });
        var steps = new List<DeployStep>
        {
            new() { Type = StepType.LocalCommand, Name = "clean" },
            new() { Type = StepType.DockerBuild, Name = "build" }
        };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Single(result.Steps);
        neverCalled.Verify(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Pipeline_continues_after_failure_when_ContinueOnFailure_is_true()
    {
        var failing = new Mock<IStepExecutor>();
        failing.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        failing.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = false, ExitCode = 1 });

        var next = new Mock<IStepExecutor>();
        next.Setup(e => e.Handles).Returns(StepType.DockerBuild);
        next.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { failing.Object, next.Object });
        var steps = new List<DeployStep>
        {
            new() { Type = StepType.LocalCommand, Name = "clean", ContinueOnFailure = true },
            new() { Type = StepType.DockerBuild, Name = "build" }
        };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Equal(2, result.Steps.Count);
        next.Verify(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Step_retries_until_RetryCount_exhausted_then_fails()
    {
        var callCount = 0;
        var flaky = new Mock<IStepExecutor>();
        flaky.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        flaky.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) =>
            {
                callCount++;
                return new StepResult { Step = s, Success = false, ExitCode = 1 };
            });

        var runner = new PipelineRunner(new[] { flaky.Object }, retryDelayProvider: _ => TimeSpan.Zero);
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "flaky", RetryCount = 2 } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Equal(3, callCount); // intento inicial + 2 reintentos
    }

    [Fact]
    public async Task Step_succeeds_on_retry_without_exhausting_all_attempts()
    {
        var callCount = 0;
        var flaky = new Mock<IStepExecutor>();
        flaky.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        flaky.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) =>
            {
                callCount++;
                return new StepResult { Step = s, Success = callCount >= 2, ExitCode = callCount >= 2 ? 0 : 1 };
            });

        var runner = new PipelineRunner(new[] { flaky.Object }, retryDelayProvider: _ => TimeSpan.Zero);
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "flaky", RetryCount = 3 } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }
}
