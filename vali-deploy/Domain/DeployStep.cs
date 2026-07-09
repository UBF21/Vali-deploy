namespace vali_deploy.Domain;

public class DeployStep
{
    public StepType Type { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
    public bool ContinueOnFailure { get; set; } = false;
    public int RetryCount { get; set; } = 0;
}
