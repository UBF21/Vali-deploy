using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class PipelineTemplateFactoryTests
{
    private static DeployEnvironment Environment(string? remoteDeployPath = null) =>
        new() { Name = "PROD", RemoteDeployPath = remoteDeployPath };

    [Fact]
    public void DockerCompose_template_follows_spec_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "shop", subProjectName: "api", remoteDeployPath: "/opt/shop-api");

        Assert.Equal(new[]
        {
            StepType.GitCheckout, StepType.DockerBuild, StepType.DockerPush, StepType.CopyToRemote,
            StepType.DockerComposePull, StepType.DockerComposeUp, StepType.DockerImagePrune
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void PublishZip_template_follows_spec_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreatePublishZipTemplate(projectName: "shop", subProjectName: "api", remoteDeployPath: "/opt/shop-api");

        Assert.Equal(new[]
        {
            StepType.GitCheckout, StepType.ZipPublishOutput,
            StepType.CopyToRemote, StepType.SshCommand, StepType.SshCommand
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void DockerCompose_template_sets_ImageTag_using_project_and_subproject_name()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var buildStep = steps.Single(s => s.Type == StepType.DockerBuild);

        Assert.Equal("shop-api:latest", buildStep.Args["ImageTag"]);
    }

    [Fact]
    public void DockerCompose_template_uses_the_given_remoteDeployPath_verbatim()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/srv/apps/legacy-name");
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);
        var pullStep = steps.Single(s => s.Type == StepType.DockerComposePull);
        var upStep = steps.Single(s => s.Type == StepType.DockerComposeUp);

        Assert.Equal("/srv/apps/legacy-name/compose.yml", copyStep.Args["RemotePath"]);
        Assert.Equal("/srv/apps/legacy-name/compose.yml", pullStep.Args["ComposeFilePath"]);
        Assert.Equal("/srv/apps/legacy-name/compose.yml", upStep.Args["ComposeFilePath"]);
    }

    [Fact]
    public void PublishZip_template_sets_RemotePath_on_CopyToRemote_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreatePublishZipTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);

        Assert.Equal("/opt/shop-api/api.zip", copyStep.Args["RemotePath"]);
    }

    [Fact]
    public void ResolveDefaultRemoteDeployPath_uses_opt_convention_when_environment_has_no_override()
    {
        var path = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath("Shop", "Api", Environment());

        Assert.Equal("/opt/shop-api", path);
    }

    [Fact]
    public void ResolveDefaultRemoteDeployPath_uses_environment_RemoteDeployPath_when_set()
    {
        var path = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath("Shop", "Api", Environment("/srv/apps/legacy-name"));

        Assert.Equal("/srv/apps/legacy-name", path);
    }

    [Fact]
    public void DockerCompose_template_falls_back_to_bare_imageTag_when_no_registry_configured()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_for_docker_hub_when_host_is_empty()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api", dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("myuser/shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_with_host_for_generic_registry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api", dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("ghcr.io/myorg/shop-api:latest", pushStep.Args["RegistryTag"]);
        Assert.Equal("ghcr.io", pushStep.Args["RegistryHost"]);
        Assert.Equal("myorg", pushStep.Args["RegistryUsername"]);
        Assert.Equal("GHCR_TOKEN", pushStep.Args["RegistryTokenEnvVar"]);
    }

    [Fact]
    public void LocalPublish_template_is_a_single_ZipPublishOutput_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalPublishTemplate(omitFiles: new List<string>());

        Assert.Single(steps);
        Assert.Equal(StepType.ZipPublishOutput, steps[0].Type);
        Assert.Equal("", steps[0].Args["OmitFiles"]);
    }

    [Fact]
    public void LocalPublish_template_encodes_OmitFiles_pipe_delimited()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalPublishTemplate(omitFiles: new List<string> { "a.txt", "b.txt" });

        Assert.Equal("a.txt|b.txt", steps[0].Args["OmitFiles"]);
    }

    [Fact]
    public void LocalDockerBuild_template_is_a_single_DockerBuild_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalDockerBuildTemplate(dockerfilePath: "Dockerfile", imageTag: "shop-api:latest", buildArgs: "--build-arg X=1");

        Assert.Single(steps);
        Assert.Equal(StepType.DockerBuild, steps[0].Type);
        Assert.Equal("Dockerfile", steps[0].Args["Dockerfile"]);
        Assert.Equal("shop-api:latest", steps[0].Args["ImageTag"]);
        Assert.Equal("--build-arg X=1", steps[0].Args["BuildArgs"]);
    }

    [Fact]
    public void LocalDockerPush_template_builds_RegistryTag_from_DockerRegistry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateLocalDockerPushTemplate(imageTag: "shop-api:latest", dockerRegistry: registry);

        Assert.Single(steps);
        Assert.Equal(StepType.DockerPush, steps[0].Type);
        Assert.Equal("myuser/shop-api:latest", steps[0].Args["RegistryTag"]);
    }

    [Fact]
    public void LocalDockerRun_template_is_a_single_DockerRun_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalDockerRunTemplate(imageTag: "shop-api:latest", runArgs: "-p 8080:80");

        Assert.Single(steps);
        Assert.Equal(StepType.DockerRun, steps[0].Type);
        Assert.Equal("shop-api:latest", steps[0].Args["ImageTag"]);
        Assert.Equal("-p 8080:80", steps[0].Args["RunArgs"]);
    }
}
