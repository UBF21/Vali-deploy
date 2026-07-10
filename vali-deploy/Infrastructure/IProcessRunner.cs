namespace vali_deploy.Infrastructure;

public record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null, string? stdInput = null);
}
