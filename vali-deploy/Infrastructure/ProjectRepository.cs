using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Models;

namespace vali_deploy.Infrastructure;

/// <summary>
/// INTEROP: este archivo (deploy_config.json) también es leído/escrito por
/// vali_deploy.Managers.ProjectManager (forma legacy: el JSON raíz ES un
/// Dictionary&lt;string, Project&gt;). Load() debe ser seguro sin importar cuál de las
/// dos clases toca el archivo primero — ver ProjectManager.ParseProjectsLeniently para
/// el mismo heurístico aplicado en espejo. Sin esto, Load() sobre un archivo legacy-flat
/// deserializaba silenciosamente como DeployConfig vacío (Projects.Count == 0), y un Save()
/// posterior (p.ej. desde EnvironmentMenu) destruía todos los proyectos reales en disco.
/// </summary>
public class ProjectRepository : IProjectRepository
{
    private readonly string _configPath;

    public ProjectRepository(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "deploy_config.json");
    }

    public DeployConfig Load()
    {
        var folderPath = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(folderPath);

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return ParseConfigLeniently(json);
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    /// <summary>
    /// Deserializa DeployConfig tolerando ambas formas del archivo:
    /// - Forma DeployConfig (propia): { "Projects": {...}, "Environments": [...] }.
    /// - Forma legacy flat (escrita por ProjectManager): el documento raíz ES el
    ///   Dictionary&lt;string, Project&gt; directamente — se envuelve en un DeployConfig
    ///   con Environments vacío en vez de deserializarse (silenciosamente) como config vacía.
    ///
    /// Se exige la presencia de AMBAS propiedades "Projects" y "Environments" en la raíz para
    /// clasificar el documento como forma DeployConfig, en espejo con
    /// ProjectManager.ParseProjectsLeniently (mismo heurístico, misma justificación).
    /// </summary>
    internal static DeployConfig ParseConfigLeniently(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("Projects", out _) &&
            root.TryGetProperty("Environments", out _))
        {
            return JsonSerializer.Deserialize<DeployConfig>(json) ?? new DeployConfig { Projects = GetDefaultProjects() };
        }

        // Forma legacy flat: el documento raíz es directamente el diccionario de proyectos.
        var projects = JsonSerializer.Deserialize<Dictionary<string, Project>>(json) ?? new Dictionary<string, Project>();
        return new DeployConfig { Projects = projects, Environments = new List<DeployEnvironment>() };
    }

    public void Save(DeployConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private static Dictionary<string, Project> GetDefaultProjects() => new()
    {
        ["Project 1"] = new Project { Path = @"\Projects\Path", SubProjects = new List<SubProject>() }
    };
}
