using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerBuildExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerBuild()
    {
        var executor = new DockerBuildExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerBuild, executor.Handles);
    }

    [Fact]
    public async Task Builds_image_with_dockerfile_and_tag_from_Args()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker build -f \"/tmp/proj/Dockerfile\" -t proj-sub:latest \"/tmp/proj\"",
                "/tmp/proj",
                It.Is<IDictionary<string, string>>(d => d["DOCKER_BUILDKIT"] == "1")))
            .ReturnsAsync(new ProcessRunResult(0, "Successfully built", ""));

        var executor = new DockerBuildExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerBuild, Name = "build image",
            Args = { ["Dockerfile"] = "/tmp/proj/Dockerfile", ["ImageTag"] = "proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Appends_BuildArgs_when_present()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker build -f \"/tmp/proj/Dockerfile\" -t proj-sub:latest --build-arg KEY=VALUE \"/tmp/proj\"",
                "/tmp/proj",
                It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerBuildExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerBuild, Name = "build image",
            Args =
            {
                ["Dockerfile"] = "/tmp/proj/Dockerfile",
                ["ImageTag"] = "proj-sub:latest",
                ["BuildArgs"] = "--build-arg KEY=VALUE"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
