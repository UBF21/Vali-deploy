using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployEnvironmentTests
{
    [Fact]
    public void Environment_without_server_means_no_remote_deploy()
    {
        var dev = new DeployEnvironment { Name = "DEV" };

        Assert.Null(dev.Server);
        Assert.Null(dev.DefaultBranch);
    }

    [Fact]
    public void Environment_with_server_carries_default_branch_for_prod()
    {
        var prod = new DeployEnvironment
        {
            Name = "PROD",
            DefaultBranch = "main",
            Server = new RemoteServer
            {
                Host = "prod.example.com",
                User = "deploy",
                Os = RemoteOs.Linux,
                PrivateKeyPath = "/home/deploy/.ssh/id_rsa"
            }
        };

        Assert.Equal("main", prod.DefaultBranch);
        Assert.NotNull(prod.Server);
        Assert.Equal(RemoteOs.Linux, prod.Server!.Os);
    }
}
