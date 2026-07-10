using System.Text.Json;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

/// <summary>
/// HISTORIAL: hasta la Tarea 30, este archivo (deploy_config.json) también era leído/escrito
/// por la clase legacy (ya retirada) que persistía proyectos con el JSON raíz siendo
/// directamente un Dictionary&lt;string, Project&gt; (sin envolver en {"Projects", "Environments"}).
/// Esa clase y esta escribían formas de JSON incompatibles sobre el mismo archivo, lo cual
/// causaba pérdida de datos. Load() conserva el parseo tolerante de esa forma legacy-flat
/// (ver <see cref="ParseConfigLeniently"/>) por si algún usuario todavía tiene un
/// deploy_config.json en esa forma antigua en disco — sin esto, Load() lo deserializaba
/// silenciosamente como DeployConfig vacío (Projects.Count == 0), y un Save() posterior
/// (p.ej. desde EnvironmentMenu) destruía todos los proyectos reales en disco.
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
            var config = ParseConfigLeniently(json);
            MigrateDockerHubUserToRegistry(config);
            return config;
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    /// <summary>
    /// Migra SubProject.DockerHubUser (campo legacy en texto plano) a SubProject.DockerRegistry la primera vez
    /// que se carga un deploy_config.json escrito por una versión anterior del CLI. No persiste el resultado acá
    /// — el próximo Save() (de cualquier flujo) ya escribe la forma nueva.
    /// </summary>
    internal static void MigrateDockerHubUserToRegistry(DeployConfig config)
    {
        foreach (var project in config.Projects.Values)
        {
            foreach (var subProject in project.SubProjects)
            {
                if (subProject.DockerRegistry == null && !string.IsNullOrEmpty(subProject.DockerHubUser))
                {
                    subProject.DockerRegistry = new DockerRegistry { Host = "", Username = subProject.DockerHubUser };
                    subProject.DockerHubUser = null;
                }
            }
        }
    }

    /// <summary>
    /// Deserializa DeployConfig tolerando ambas formas del archivo:
    /// - Forma DeployConfig (propia): { "Projects": {...}, "Environments": [...] }.
    /// - Forma legacy flat (escrita por la clase legacy retirada en la Tarea 30): el documento
    ///   raíz ES el Dictionary&lt;string, Project&gt; directamente — se envuelve en un DeployConfig
    ///   con Environments vacío en vez de deserializarse (silenciosamente) como config vacía.
    ///
    /// Se exige la presencia de AMBAS propiedades "Projects" y "Environments" en la raíz para
    /// clasificar el documento como forma DeployConfig — evita falsos positivos si un proyecto
    /// legacy se llamara literalmente "Projects" en la forma flat.
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
