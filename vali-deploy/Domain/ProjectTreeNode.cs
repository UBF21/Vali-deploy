namespace vali_deploy.Domain;

public class ProjectTreeNode
{
    public string ProjectName { get; set; } = "";
    public List<string> SubProjectNames { get; set; } = new();
}
