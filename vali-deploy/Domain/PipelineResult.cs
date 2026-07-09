namespace vali_deploy.Domain;

public class PipelineResult
{
    public bool Success { get; set; }
    public List<StepResult> Steps { get; set; } = new();
}
