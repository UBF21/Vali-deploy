using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class EnvironmentsTreeBuilderTests
{
    private static DeployConfig ConfigWith(List<DeployEnvironment> environments, Dictionary<string, Project> projects) =>
        new() { Environments = environments, Projects = projects };

    [Fact]
    public void Build_environment_with_no_matching_projects_returns_empty_projects_list()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject> { new() { Name = "api", PipelinesByEnvironment = new() } }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        Assert.Single(result);
        Assert.Equal("QA", result[0].EnvironmentName);
        Assert.Empty(result[0].Projects);
    }

    [Fact]
    public void Build_collapses_single_subproject_project_to_a_leaf_with_no_subproject_names()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal("shop", projectNode.ProjectName);
        Assert.Empty(projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_keeps_project_as_branch_when_multiple_subprojects_even_if_only_one_matches()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal("shop", projectNode.ProjectName);
        Assert.Equal(new[] { "api" }, projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_lists_all_matching_subprojects_when_multiple_match_in_SubProjects_order()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal(new[] { "api", "worker" }, projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_excludes_subproject_with_no_pipeline_in_any_environment()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" }, new() { Name = "DEV" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["DEV"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var qaNode = result.Single(e => e.EnvironmentName == "QA");
        var devNode = result.Single(e => e.EnvironmentName == "DEV");

        Assert.Empty(qaNode.Projects);
        var shopUnderDev = Assert.Single(devNode.Projects);
        Assert.Equal(new[] { "api" }, shopUnderDev.SubProjectNames);
    }

    [Fact]
    public void Build_keeps_environments_independent_from_each_other()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" }, new() { Name = "DEV" } },
            projects: new Dictionary<string, Project>
            {
                ["app-qa"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "app-qa", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                },
                ["app-dev"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "app-dev", PipelinesByEnvironment = new() { ["DEV"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var qaNode = result.Single(e => e.EnvironmentName == "QA");
        var devNode = result.Single(e => e.EnvironmentName == "DEV");

        Assert.Equal("app-qa", Assert.Single(qaNode.Projects).ProjectName);
        Assert.Equal("app-dev", Assert.Single(devNode.Projects).ProjectName);
    }
}
