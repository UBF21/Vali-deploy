using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class ProjectRepositoryTests
{
    private static string NewTempConfigPath() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "deploy_config.json");

    private static Dictionary<string, Project> SampleProjects() => new()
    {
        ["real-project-a"] = new Project { Path = @"C:\src\a", SubProjects = new List<SubProject> { new() { Name = "Api", Path = "Api" } } },
        ["real-project-b"] = new Project { Path = @"C:\src\b", SubProjects = new List<SubProject>() }
    };

    [Fact]
    public void Load_creates_default_config_when_file_does_not_exist()
    {
        var repository = new ProjectRepository(NewTempConfigPath());

        var config = repository.Load();

        Assert.NotEmpty(config.Projects);
        Assert.Empty(config.Environments);
    }

    [Fact]
    public void Save_then_load_roundtrips_environments_and_projects()
    {
        var configPath = NewTempConfigPath();
        var repository = new ProjectRepository(configPath);
        var config = repository.Load();
        config.Environments.Add(new DeployEnvironment { Name = "QA", DefaultBranch = "develop" });
        config.Projects["demo"] = new Project { Path = "/tmp/demo", SubProjects = new List<SubProject>() };

        repository.Save(config);
        var reloaded = repository.Load();

        Assert.Single(reloaded.Environments);
        Assert.Equal("QA", reloaded.Environments[0].Name);
        Assert.True(reloaded.Projects.ContainsKey("demo"));
    }

    // ---------------------------------------------------------------
    // Regresión histórica (Tarea 30 retiró la clase legacy que causaba esto): Load() debe
    // seguir siendo seguro ante un archivo legacy-flat preexistente en disco, sin importar
    // qué versión anterior del código lo haya escrito. Ver comentario de clase en ProjectRepository.cs.
    // ---------------------------------------------------------------

    [Fact]
    public void Load_reads_legacy_flat_shape_json_with_real_projects()
    {
        var configPath = NewTempConfigPath();
        string legacyFlatJson = JsonSerializer.Serialize(SampleProjects(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, legacyFlatJson);

        var repository = new ProjectRepository(configPath);
        var config = repository.Load();

        Assert.Equal(2, config.Projects.Count);
        Assert.True(config.Projects.ContainsKey("real-project-a"));
        Assert.Equal(@"C:\src\a", config.Projects["real-project-a"].Path);
        Assert.True(config.Projects.ContainsKey("real-project-b"));
        Assert.Empty(config.Environments);
    }

    [Fact]
    public void Load_then_Save_on_dangerous_order_does_not_wipe_legacy_projects()
    {
        // Reproduce exactamente la secuencia peligrosa reportada: un archivo legacy-flat
        // con proyectos reales ya en disco (escrito por la clase legacy retirada en la Tarea 30
        // en algún momento anterior, o a mano), y "Manage Environments" es LO PRIMERO que toca el archivo
        // en esta ejecución — ProjectRepository nunca lo vio en forma DeployConfig antes.
        var configPath = NewTempConfigPath();
        string legacyFlatJson = JsonSerializer.Serialize(SampleProjects(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(configPath, legacyFlatJson);

        var repository = new ProjectRepository(configPath);

        // 1) Load() es la primera operación que ProjectRepository ejecuta sobre este archivo.
        var config = repository.Load();
        Assert.Equal(2, config.Projects.Count); // Si esto falla, el bug de Scenario B sigue abierto.

        // 2) El usuario agrega un DeployEnvironment desde "Manage Environments" y se guarda.
        config.Environments.Add(new DeployEnvironment { Name = "Prod", DefaultBranch = "main" });
        repository.Save(config);

        // 3) Releer el archivo: los proyectos reales deben seguir ahí, no haber sido
        // reemplazados por un DeployConfig vacío.
        var reloaded = repository.Load();
        Assert.Equal(2, reloaded.Projects.Count);
        Assert.True(reloaded.Projects.ContainsKey("real-project-a"));
        Assert.True(reloaded.Projects.ContainsKey("real-project-b"));
        Assert.Single(reloaded.Environments);
        Assert.Equal("Prod", reloaded.Environments[0].Name);
    }

    [Fact]
    public void Load_still_reads_DeployConfig_shape_correctly_regression_guard()
    {
        var configPath = NewTempConfigPath();
        var originalConfig = new DeployConfig
        {
            Projects = SampleProjects(),
            Environments = new List<DeployEnvironment> { new() { Name = "QA", DefaultBranch = "develop" } }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(originalConfig, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var config = repository.Load();

        Assert.Equal(2, config.Projects.Count);
        Assert.True(config.Projects.ContainsKey("real-project-a"));
        Assert.Single(config.Environments);
        Assert.Equal("QA", config.Environments[0].Name);
    }

    [Fact]
    public void Load_migrates_legacy_DockerHubUser_to_DockerRegistry()
    {
        var configPath = NewTempConfigPath();
        var legacyConfig = new DeployConfig
        {
            Projects = new Dictionary<string, Project>
            {
                ["proj"] = new Project
                {
                    Path = "/tmp/proj",
                    SubProjects = new List<SubProject> { new() { Name = "api", DockerHubUser = "myuser" } }
                }
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(legacyConfig, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var config = repository.Load();

        var subProject = config.Projects["proj"].SubProjects[0];
        Assert.NotNull(subProject.DockerRegistry);
        Assert.Equal("", subProject.DockerRegistry!.Host);
        Assert.Equal("myuser", subProject.DockerRegistry.Username);
        Assert.Null(subProject.DockerHubUser);
    }

    [Fact]
    public void Load_does_not_overwrite_existing_DockerRegistry_with_stale_DockerHubUser()
    {
        var configPath = NewTempConfigPath();
        var config = new DeployConfig
        {
            Projects = new Dictionary<string, Project>
            {
                ["proj"] = new Project
                {
                    Path = "/tmp/proj",
                    SubProjects = new List<SubProject>
                    {
                        new()
                        {
                            Name = "api",
                            DockerRegistry = new DockerRegistry { Host = "ghcr.io", Username = "already-configured" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var reloaded = repository.Load();

        Assert.Equal("already-configured", reloaded.Projects["proj"].SubProjects[0].DockerRegistry!.Username);
    }

    [Fact]
    public void Load_does_not_null_out_DockerHubUser_when_DockerRegistry_already_migrated()
    {
        // Reproduce el escenario donde MenuManager (flujo legacy "Push to Docker Hub", hasta Task 8)
        // repuebla DockerHubUser después de una migración previa — Load() no debe volver a pisarlo,
        // porque MigrateDockerHubUserToRegistry ya no actúa cuando DockerRegistry != null.
        var configPath = NewTempConfigPath();
        var config = new DeployConfig
        {
            Projects = new Dictionary<string, Project>
            {
                ["proj"] = new Project
                {
                    Path = "/tmp/proj",
                    SubProjects = new List<SubProject>
                    {
                        new()
                        {
                            Name = "api",
                            DockerRegistry = new DockerRegistry { Host = "", Username = "old-migrated-user" },
                            DockerHubUser = "user-set-again-by-legacy-flow"
                        }
                    }
                }
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var reloaded = repository.Load();

        Assert.Equal("user-set-again-by-legacy-flow", reloaded.Projects["proj"].SubProjects[0].DockerHubUser);
    }
}
