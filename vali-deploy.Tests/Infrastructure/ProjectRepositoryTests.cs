using vali_deploy.Domain;
using vali_deploy.Infrastructure;
using vali_deploy.Models;

namespace vali_deploy.Tests.Infrastructure;

public class ProjectRepositoryTests
{
    private static string NewTempConfigPath() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "deploy_config.json");

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
}
