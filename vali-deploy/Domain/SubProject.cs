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
    /// Campo legacy (pre-DockerRegistry): username de Docker Hub en texto plano. Todavía lo usa el flujo
    /// "Push to Docker Hub" de <see cref="Managers.MenuManager"/> hasta que se migre a
    /// <see cref="DockerRegistry"/> (ver Task 8 del plan de deuda técnica) — hasta entonces, coexiste con
    /// <see cref="DockerRegistry"/> y <see cref="Infrastructure.ProjectRepository.Load"/> lo migra a este
    /// último solo la primera vez que lo encuentra seteado, sin pisarlo si ya migró.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DockerHubUser { get; set; }
}
