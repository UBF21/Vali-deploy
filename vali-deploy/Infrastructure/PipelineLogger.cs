using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class PipelineLogger : IPipelineLogger
{
    private readonly string _logsDirectory;
    private string? _currentLogFilePath;

    public PipelineLogger(string? logsDirectory = null)
    {
        _logsDirectory = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "logs");
    }

    public void StartRun(string projectName, string subProjectName)
    {
        Directory.CreateDirectory(_logsDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        _currentLogFilePath = Path.Combine(_logsDirectory, $"{projectName}-{subProjectName}-{timestamp}.log");
        File.WriteAllText(_currentLogFilePath, $"=== Pipeline run: {projectName}/{subProjectName} — {DateTime.UtcNow:O} ===\n");
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
}
