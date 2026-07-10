namespace vali_deploy.Domain;

public class DeployRunSummary
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectName { get; set; } = "";
    public string SubProjectName { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public bool Success { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public string LogFilePath { get; set; } = "";
}
