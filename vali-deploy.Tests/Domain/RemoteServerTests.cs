using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class RemoteServerTests
{
    [Fact]
    public void Default_port_is_22_and_passphrase_env_var_is_optional()
    {
        var server = new RemoteServer
        {
            Host = "192.168.1.10",
            User = "deploy",
            Os = RemoteOs.Linux,
            PrivateKeyPath = "/home/deploy/.ssh/id_rsa"
        };

        Assert.Equal(22, server.Port);
        Assert.Null(server.PassphraseEnvVar);
    }
}
