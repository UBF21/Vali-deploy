namespace vali_deploy.Domain;

public class Project
{
    public string Path { get; set; } = "";
    public List<SubProject> SubProjects { get; set; } = new();
}