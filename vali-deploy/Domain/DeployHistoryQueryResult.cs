namespace vali_deploy.Domain;

public class DeployHistoryQueryResult
{
    public IReadOnlyList<DeployRunSummary> Runs { get; set; } = new List<DeployRunSummary>();
    public int SkippedCorruptedLines { get; set; }
}
