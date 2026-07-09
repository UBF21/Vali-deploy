using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;
using vali_deploy.Managers;
using vali_deploy.Models;

namespace vali_deploy.Tests.Managers;

/// <summary>
/// Cubre la compatibilidad de ProjectManager con la forma de JSON que
/// vali_deploy.Infrastructure.ProjectRepository escribe/lee en el mismo archivo
/// (deploy_config.json). Ver comentario de clase en ProjectManager.cs para el contexto
/// completo del bug de pérdida de datos que estas pruebas cierran.
///
/// ProjectManager es una clase estática con una ruta de archivo hardcodeada
/// (%USERPROFILE%\Documents\vali-deploy\deploy_config.json), así que no es seguro
/// probarla de punta a punta contra el filesystem real sin arriesgar el archivo real
/// del usuario. En cambio, se prueban directamente los dos métodos internos puros que
/// contienen toda la lógica de transformación de JSON (ParseProjectsLeniently /
/// BuildCompatibleJson) — no tocan disco — y, para las pruebas de interoperabilidad,
/// se combinan con la clase ProjectRepository real (que sí admite una ruta inyectada)
/// sobre archivos temporales.
/// </summary>
public class ProjectManagerTests
{
    private static Dictionary<string, Project> SampleProjects() => new()
    {
        ["real-project-a"] = new Project { Path = @"C:\src\a", SubProjects = new List<SubProject> { new() { Name = "Api", Path = "Api" } } },
        ["real-project-b"] = new Project { Path = @"C:\src\b", SubProjects = new List<SubProject>() }
    };

    // ---------------------------------------------------------------
    // Escenario A: leer un archivo DeployConfig-shaped (escrito por
    // ProjectRepository/EnvironmentMenu/PipelineEditorMenu) ya no debe tirar
    // JsonException ni caer en la rama de "recrear con defaults".
    // ---------------------------------------------------------------

    [Fact]
    public void ParseProjectsLeniently_reads_DeployConfig_shaped_json_with_real_projects()
    {
        var config = new DeployConfig
        {
            Projects = SampleProjects(),
            Environments = new List<DeployEnvironment> { new() { Name = "QA", DefaultBranch = "develop" } }
        };
        string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

        var result = ProjectManager.ParseProjectsLeniently(json);

        Assert.Equal(2, result.Count);
        Assert.True(result.ContainsKey("real-project-a"));
        Assert.Equal(@"C:\src\a", result["real-project-a"].Path);
        Assert.Single(result["real-project-a"].SubProjects);
        Assert.True(result.ContainsKey("real-project-b"));
    }

    [Fact]
    public void ParseProjectsLeniently_does_not_fall_back_to_dummy_defaults_for_DeployConfig_shape()
    {
        var config = new DeployConfig { Projects = SampleProjects(), Environments = new List<DeployEnvironment>() };
        string json = JsonSerializer.Serialize(config);

        var result = ProjectManager.ParseProjectsLeniently(json);

        // Los defaults dummy de ProjectManager se llaman "Project 1", "Project 2", "Project 3".
        Assert.DoesNotContain("Project 1", result.Keys);
        Assert.DoesNotContain("Project 2", result.Keys);
        Assert.DoesNotContain("Project 3", result.Keys);
    }

    // ---------------------------------------------------------------
    // Compatibilidad hacia atrás: la forma legacy flat sigue funcionando igual que antes.
    // ---------------------------------------------------------------

    [Fact]
    public void ParseProjectsLeniently_reads_legacy_flat_shape_json()
    {
        var projects = SampleProjects();
        string json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });

        var result = ProjectManager.ParseProjectsLeniently(json);

        Assert.Equal(2, result.Count);
        Assert.Equal(@"C:\src\b", result["real-project-b"].Path);
    }

    // ---------------------------------------------------------------
    // Escenario B: guardar no debe destruir un arreglo "Environments" existente.
    // ---------------------------------------------------------------

    [Fact]
    public void BuildCompatibleJson_preserves_existing_Environments_array()
    {
        var existingConfig = new DeployConfig
        {
            Projects = new Dictionary<string, Project> { ["old"] = new Project { Path = "old-path" } },
            Environments = new List<DeployEnvironment>
            {
                new() { Name = "Prod", DefaultBranch = "main" },
                new() { Name = "QA", DefaultBranch = "develop" }
            }
        };
        string existingJson = JsonSerializer.Serialize(existingConfig);

        string result = ProjectManager.BuildCompatibleJson(SampleProjects(), existingJson);

        using var document = JsonDocument.Parse(result);
        var environments = document.RootElement.GetProperty("Environments");
        Assert.Equal(2, environments.GetArrayLength());
        Assert.Equal("Prod", environments[0].GetProperty("Name").GetString());
        Assert.Equal("QA", environments[1].GetProperty("Name").GetString());

        var projectsNode = document.RootElement.GetProperty("Projects");
        Assert.True(projectsNode.TryGetProperty("real-project-a", out _));
        Assert.False(projectsNode.TryGetProperty("old", out _), "SaveConfig replaces the Projects node with the caller's dictionary, it does not merge it.");
    }

    [Fact]
    public void BuildCompatibleJson_with_no_prior_file_produces_empty_Environments_array()
    {
        string result = ProjectManager.BuildCompatibleJson(SampleProjects(), existingJson: null);

        using var document = JsonDocument.Parse(result);
        var environments = document.RootElement.GetProperty("Environments");
        Assert.Equal(JsonValueKind.Array, environments.ValueKind);
        Assert.Equal(0, environments.GetArrayLength());
    }

    [Fact]
    public void BuildCompatibleJson_upgrades_legacy_flat_existing_file_to_DeployConfig_shape()
    {
        // Un archivo legacy existente no tiene "Environments" en absoluto.
        string legacyExistingJson = JsonSerializer.Serialize(new Dictionary<string, Project>
        {
            ["old"] = new Project { Path = "old-path" }
        });

        string result = ProjectManager.BuildCompatibleJson(SampleProjects(), legacyExistingJson);

        using var document = JsonDocument.Parse(result);
        Assert.True(document.RootElement.TryGetProperty("Projects", out _));
        Assert.True(document.RootElement.TryGetProperty("Environments", out var environments));
        Assert.Equal(0, environments.GetArrayLength());
    }

    [Fact]
    public void BuildCompatibleJson_treats_corrupt_existing_json_as_no_Environments_to_preserve()
    {
        string result = ProjectManager.BuildCompatibleJson(SampleProjects(), existingJson: "{ not valid json");

        using var document = JsonDocument.Parse(result);
        var environments = document.RootElement.GetProperty("Environments");
        Assert.Equal(0, environments.GetArrayLength());
    }

    // ---------------------------------------------------------------
    // Interop cruzado: ProjectManager <-> ProjectRepository sobre el mismo archivo
    // (usando un path temporal inyectado en ProjectRepository, nunca el path real
    // hardcodeado de ProjectManager).
    // ---------------------------------------------------------------

    private static string NewTempConfigPath() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "deploy_config.json");

    [Fact]
    public void SaveConfig_output_is_loadable_by_ProjectRepository_without_data_loss()
    {
        string tempPath = NewTempConfigPath();
        string json = ProjectManager.BuildCompatibleJson(SampleProjects(), existingJson: null);
        File.WriteAllText(tempPath, json);

        var repository = new ProjectRepository(tempPath);
        DeployConfig loaded = repository.Load();

        Assert.Equal(2, loaded.Projects.Count);
        Assert.True(loaded.Projects.ContainsKey("real-project-a"));
        Assert.Empty(loaded.Environments);
    }

    [Fact]
    public void ProjectRepository_output_with_Environments_is_readable_by_ParseProjectsLeniently_without_data_loss()
    {
        string tempPath = NewTempConfigPath();
        var repository = new ProjectRepository(tempPath);
        var config = repository.Load(); // crea defaults la primera vez
        config.Projects = SampleProjects();
        config.Environments.Add(new DeployEnvironment { Name = "QA", DefaultBranch = "develop" });
        repository.Save(config);

        string rawJson = File.ReadAllText(tempPath);
        var parsedProjects = ProjectManager.ParseProjectsLeniently(rawJson);

        Assert.Equal(2, parsedProjects.Count);
        Assert.True(parsedProjects.ContainsKey("real-project-a"));
        Assert.True(parsedProjects.ContainsKey("real-project-b"));
    }

    [Fact]
    public void Full_interop_chain_ProjectManager_save_then_ProjectRepository_adds_environment_then_ProjectManager_reload_keeps_projects()
    {
        // Reproduce la secuencia real del bug reportado:
        // 1) ProjectManager guarda proyectos (p.ej. "Add Project" en el menú legacy).
        // 2) ProjectRepository (EnvironmentMenu) agrega un DeployEnvironment y guarda.
        // 3) ProjectManager vuelve a leer (p.ej. UpdateProjectsAndChart) y NO debe perder nada.
        string tempPath = NewTempConfigPath();

        string savedByProjectManager = ProjectManager.BuildCompatibleJson(SampleProjects(), existingJson: null);
        File.WriteAllText(tempPath, savedByProjectManager);

        var repository = new ProjectRepository(tempPath);
        var configFromRepository = repository.Load();
        configFromRepository.Environments.Add(new DeployEnvironment { Name = "Prod", DefaultBranch = "main" });
        repository.Save(configFromRepository);

        string rawJsonAfterEnvironmentSave = File.ReadAllText(tempPath);
        var reloadedByProjectManager = ProjectManager.ParseProjectsLeniently(rawJsonAfterEnvironmentSave);

        Assert.Equal(2, reloadedByProjectManager.Count);
        Assert.True(reloadedByProjectManager.ContainsKey("real-project-a"));
        Assert.True(reloadedByProjectManager.ContainsKey("real-project-b"));
    }
}
