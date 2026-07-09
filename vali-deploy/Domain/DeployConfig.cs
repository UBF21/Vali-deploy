namespace vali_deploy.Domain;

public class DeployConfig
{
    public Dictionary<string, Project> Projects { get; set; } = new();
    public List<DeployEnvironment> Environments { get; set; } = new();
}
