namespace vali_deploy.Domain;

public class RemoteServer
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    public RemoteOs Os { get; set; }
    public string PrivateKeyPath { get; set; } = "";
    public string? PassphraseEnvVar { get; set; }
}
