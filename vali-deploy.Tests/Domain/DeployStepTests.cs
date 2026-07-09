using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployStepTests
{
    [Fact]
    public void New_step_has_no_retries_and_stops_pipeline_on_failure_by_default()
    {
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "clean" };

        Assert.Equal(0, step.RetryCount);
        Assert.False(step.ContinueOnFailure);
        Assert.Empty(step.Args);
    }

    [Fact]
    public void All_step_types_from_spec_exist()
    {
        var expected = new[]
        {
            "GitCheckout", "LocalCommand", "DockerBuild", "DockerPush", "DockerSave", "DockerLoad",
            "DockerImagePrune", "DockerComposePull", "DockerComposeUp", "DockerComposeDown",
            "ZipPublishOutput", "CopyToRemote", "SshCommand", "RawCommand"
        };

        var actual = Enum.GetNames<StepType>();

        Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
    }
}
