using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Models;

namespace vali_deploy.Infrastructure;

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
            return JsonSerializer.Deserialize<DeployConfig>(json) ?? new DeployConfig { Projects = GetDefaultProjects() };
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
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
