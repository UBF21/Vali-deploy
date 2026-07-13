namespace vali_deploy.Domain;

public class StepResult
{
    public DeployStep Step { get; set; } = new();
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public string Command { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public bool WasSkippedDueToContinueOnFailure { get; set; } = false;
}
