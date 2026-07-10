using System.Text.Json.Serialization;

namespace vali_deploy.Domain;

public class SubProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> OmitFiles { get; set; } = new();
    public string? DockerfilePath { get; set; }
    public List<string>? DockerRunArgs { get; set; }
    public List<string>? DockerBuildArgs { get; set; }
    public DockerRegistry? DockerRegistry { get; set; }
    public List<string>? PublishArgs { get; set; }
    public bool ZipPublishOutput { get; set; } = true;
    public Dictionary<string, List<DeployStep>> PipelinesByEnvironment { get; set; } = new();

    /// <summary>
    /// Campo legacy (pre-DockerRegistry): username de Docker Hub en texto plano. Ningún flujo de la
    /// aplicación lo lee ni lo escribe — existe solo para que <see cref="Infrastructure.ProjectRepository.Load"/>
    /// pueda migrarlo a <see cref="DockerRegistry"/> la primera vez que se carga un deploy_config.json viejo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DockerHubUser { get; set; }
}
