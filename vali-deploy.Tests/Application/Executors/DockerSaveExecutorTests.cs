using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerSaveExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerSave()
    {
        var executor = new DockerSaveExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerSave, executor.Handles);
    }

    [Fact]
    public async Task Saves_image_to_tar_file()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker save -o \"/tmp/proj/image.tar\" proj-sub:latest", "/tmp/proj", null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerSaveExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerSave, Name = "save",
            Args = { ["ImageTag"] = "proj-sub:latest", ["OutputTarPath"] = "/tmp/proj/image.tar" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        Assert.Equal("docker save -o \"/tmp/proj/image.tar\" proj-sub:latest", result.Command);
    }
}
