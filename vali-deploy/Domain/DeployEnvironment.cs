namespace vali_deploy.Domain;

public class DeployEnvironment
{
    public string Name { get; set; } = "";
    public RemoteServer? Server { get; set; }
    public string? DefaultBranch { get; set; }
    public string? RemoteDeployPath { get; set; }
}
