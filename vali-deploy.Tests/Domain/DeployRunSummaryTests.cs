using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployRunSummaryTests
{
    [Fact]
    public void Default_RunId_is_generated_and_unique_per_instance()
    {
        var first = new DeployRunSummary();
        var second = new DeployRunSummary();

        Assert.NotEmpty(first.RunId);
        Assert.NotEqual(first.RunId, second.RunId);
    }
}
