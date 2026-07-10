using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerImagePruneExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerImagePrune()
    {
        var executor = new DockerImagePruneExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerImagePrune, executor.Handles);
    }

    [Fact]
    public async Task Prunes_dangling_images_filtered_by_project_name()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker image prune -f --filter \"label=project=proj-sub\"",
                "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "Total reclaimed space: 0B", ""));

        var executor = new DockerImagePruneExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerImagePrune, Name = "prune",
            Args = { ["ImageNameFilter"] = "proj-sub" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
