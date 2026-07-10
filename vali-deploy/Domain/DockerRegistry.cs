namespace vali_deploy.Domain;

public class DockerRegistry
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string? TokenEnvVar { get; set; }
}
