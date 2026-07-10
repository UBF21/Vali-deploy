using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DockerRegistryTests
{
    [Fact]
    public void Empty_host_means_docker_hub()
    {
        var registry = new DockerRegistry { Username = "myuser" };

        Assert.Equal("", registry.Host);
        Assert.Null(registry.TokenEnvVar);
    }

    [Fact]
    public void Host_set_means_generic_registry()
    {
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        Assert.Equal("ghcr.io", registry.Host);
        Assert.Equal("GHCR_TOKEN", registry.TokenEnvVar);
    }
}
