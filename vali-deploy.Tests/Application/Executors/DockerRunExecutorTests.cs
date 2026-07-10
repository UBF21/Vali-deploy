using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerRunExecutorTests
{
    [Fact]
    public void Handles_DockerRun()
    {
        var executor = new DockerRunExecutor(new Mock<IInteractiveProcessLauncher>().Object);
        Assert.Equal(StepType.DockerRun, executor.Handles);
    }
}
