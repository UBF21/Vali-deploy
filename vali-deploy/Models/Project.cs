namespace vali_deploy.Models;

public class Project
{
    public string Path { get; set; } = "";
    public List<SubProject> SubProjects { get; set; } = new();
}