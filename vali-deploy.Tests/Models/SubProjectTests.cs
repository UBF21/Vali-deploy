using vali_deploy.Domain;
using vali_deploy.Models;

namespace vali_deploy.Tests.Models;

public class SubProjectTests
{
    [Fact]
    public void New_subproject_has_no_pipelines_and_no_registry_token_configured()
    {
        var subProject = new SubProject { Name = "api", Path = "src/api" };

        Assert.Empty(subProject.PipelinesByEnvironment);
        Assert.Null(subProject.DockerRegistryTokenEnvVar);
    }

    [Fact]
    public void Pipeline_can_be_assigned_per_environment_name()
    {
        var subProject = new SubProject { Name = "api", Path = "src/api" };
        subProject.PipelinesByEnvironment["QA"] = new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "checkout" }
        };

        Assert.Single(subProject.PipelinesByEnvironment["QA"]);
        Assert.Equal(StepType.GitCheckout, subProject.PipelinesByEnvironment["QA"][0].Type);
    }
}
