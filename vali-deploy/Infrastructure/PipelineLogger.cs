using System.Text.Json;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class PipelineLogger : IPipelineLogger
{
    private readonly string _logsDirectory;
    private string? _currentLogFilePath;
    private string? _currentProjectName;
    private string? _currentSubProjectName;
    private string? _currentEnvironmentName;
    private DateTime _currentStartedAtUtc;

    public PipelineLogger(string? logsDirectory = null)
    {
        _logsDirectory = logsDirectory ?? Utils.Constants.DefaultLogsDirectory();
    }

    public void StartRun(string projectName, string subProjectName, string environmentName)
    {
        Directory.CreateDirectory(_logsDirectory);

        _currentProjectName = projectName;
        _currentSubProjectName = subProjectName;
        _currentEnvironmentName = environmentName;
        _currentStartedAtUtc = DateTime.UtcNow;

        var timestamp = _currentStartedAtUtc.ToString("yyyyMMdd-HHmmss");
        _currentLogFilePath = Path.Combine(_logsDirectory, $"{projectName}-{subProjectName}-{timestamp}.log");
        File.WriteAllText(_currentLogFilePath, $"=== Pipeline run: {projectName}/{subProjectName} ({environmentName}) — {_currentStartedAtUtc:O} ===\n");
    }

    public void WriteStep(StepResult stepResult)
    {
        if (_currentLogFilePath == null)
        {
            throw new InvalidOperationException("StartRun debe llamarse antes de WriteStep.");
        }

        var line = $"[{DateTime.UtcNow:O}] {stepResult.Step.Name} — Success: {stepResult.Success} — ExitCode: {stepResult.ExitCode} — Duration: {stepResult.Duration}\n{stepResult.Output}\n{stepResult.Error}\n";
        File.AppendAllText(_currentLogFilePath, line);
    }

    public void FinishRun(PipelineResult result)
    {
        if (_currentLogFilePath == null)
        {
            throw new InvalidOperationException("StartRun debe llamarse antes de FinishRun.");
        }

        File.AppendAllText(_currentLogFilePath, $"=== Run finalizado — Success: {result.Success} — {DateTime.UtcNow:O} ===\n");

        var summary = new DeployRunSummary
        {
            ProjectName = _currentProjectName!,
            SubProjectName = _currentSubProjectName!,
            EnvironmentName = _currentEnvironmentName!,
            StartedAtUtc = _currentStartedAtUtc,
            Success = result.Success,
            TotalDuration = result.Steps.Aggregate(TimeSpan.Zero, (total, step) => total + step.Duration),
            LogFilePath = _currentLogFilePath
        };

        var indexFilePath = Path.Combine(_logsDirectory, "deploy-history.jsonl");
        File.AppendAllText(indexFilePath, JsonSerializer.Serialize(summary) + "\n");
    }
}
