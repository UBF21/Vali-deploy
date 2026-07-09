using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class PipelineResultTests
{
    [Fact]
    public void Pipeline_succeeds_when_all_steps_succeed()
    {
        var results = new List<StepResult>
        {
            new() { Step = new DeployStep { Name = "clean" }, ExitCode = 0, Success = true },
            new() { Step = new DeployStep { Name = "build" }, ExitCode = 0, Success = true }
        };

        var pipelineResult = new PipelineResult { Steps = results, Success = results.All(r => r.Success) };

        Assert.True(pipelineResult.Success);
    }

    [Fact]
    public void Pipeline_fails_when_any_step_fails()
    {
        var results = new List<StepResult>
        {
            new() { Step = new DeployStep { Name = "clean" }, ExitCode = 0, Success = true },
            new() { Step = new DeployStep { Name = "build" }, ExitCode = 1, Success = false }
        };

        var pipelineResult = new PipelineResult { Steps = results, Success = results.All(r => r.Success) };

        Assert.False(pipelineResult.Success);
    }
}
