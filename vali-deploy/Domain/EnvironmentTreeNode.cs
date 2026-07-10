namespace vali_deploy.Domain;

public class EnvironmentTreeNode
{
    public string EnvironmentName { get; set; } = "";
    public List<ProjectTreeNode> Projects { get; set; } = new();
}
