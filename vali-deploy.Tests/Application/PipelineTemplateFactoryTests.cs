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

        var steps = factory.CreateDockerComposeTemplate(projectName: "shop", subProjectName: "api", environment: Environment());

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

        var steps = factory.CreatePublishZipTemplate(projectName: "shop", subProjectName: "api");

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

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment());
        var buildStep = steps.Single(s => s.Type == StepType.DockerBuild);

        Assert.Equal("shop-api:latest", buildStep.Args["ImageTag"]);
    }

    [Fact]
    public void DockerCompose_template_uses_opt_convention_for_remote_path_by_default()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment());
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);
        var pullStep = steps.Single(s => s.Type == StepType.DockerComposePull);

        Assert.Equal("/opt/shop-api/compose.yml", copyStep.Args["RemotePath"]);
        Assert.Equal("/opt/shop-api/compose.yml", pullStep.Args["ComposeFilePath"]);
    }

    [Fact]
    public void DockerCompose_template_uses_environment_RemoteDeployPath_override_when_set()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment("/srv/apps/legacy-name"));
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);
        var upStep = steps.Single(s => s.Type == StepType.DockerComposeUp);

        Assert.Equal("/srv/apps/legacy-name/compose.yml", copyStep.Args["RemotePath"]);
        Assert.Equal("/srv/apps/legacy-name/compose.yml", upStep.Args["ComposeFilePath"]);
    }

    [Fact]
    public void DockerCompose_template_falls_back_to_bare_imageTag_when_no_registry_configured()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment());
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_for_docker_hub_when_host_is_empty()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment(), dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("myuser/shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_with_host_for_generic_registry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment(), dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("ghcr.io/myorg/shop-api:latest", pushStep.Args["RegistryTag"]);
        Assert.Equal("ghcr.io", pushStep.Args["RegistryHost"]);
        Assert.Equal("myorg", pushStep.Args["RegistryUsername"]);
        Assert.Equal("GHCR_TOKEN", pushStep.Args["RegistryTokenEnvVar"]);
    }
}
