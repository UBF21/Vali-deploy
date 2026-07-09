namespace vali_deploy.Domain;

public class SubProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> OmitFiles { get; set; } = new();
    public string? DockerfilePath { get; set; }
    public List<string>? DockerRunArgs { get; set; }
    public List<string>? DockerBuildArgs { get; set; }
    public string? DockerHubUser { get; set; }
    public List<string>? PublishArgs { get; set; }
    public bool ZipPublishOutput { get; set; } = true;
    public Dictionary<string, List<DeployStep>> PipelinesByEnvironment { get; set; } = new();
    public string? DockerRegistryTokenEnvVar { get; set; }
}