using vali_deploy.Domain;

namespace vali_deploy.Application;

public static class EnvironmentsTreeBuilder
{
    public static List<EnvironmentTreeNode> Build(DeployConfig config)
    {
        return config.Environments
            .Select(environment => BuildEnvironmentNode(environment, config.Projects))
            .ToList();
    }

    private static EnvironmentTreeNode BuildEnvironmentNode(DeployEnvironment environment, Dictionary<string, Project> projects)
    {
        var projectNodes = projects
            .Select(kvp => BuildProjectNode(kvp.Key, kvp.Value, environment.Name))
            .Where(node => node != null)
            .Select(node => node!)
            .ToList();

        return new EnvironmentTreeNode { EnvironmentName = environment.Name, Projects = projectNodes };
    }

    private static ProjectTreeNode? BuildProjectNode(string projectName, Project project, string environmentName)
    {
        var matchingSubProjects = project.SubProjects
            .Where(sp => sp.PipelinesByEnvironment.ContainsKey(environmentName))
            .Select(sp => sp.Name)
            .ToList();

        if (matchingSubProjects.Count == 0)
        {
            return null;
        }

        return new ProjectTreeNode
        {
            ProjectName = projectName,
            SubProjectNames = project.SubProjects.Count == 1 ? new List<string>() : matchingSubProjects
        };
    }
}
