namespace vali_deploy.Infrastructure;

public interface IInteractiveProcessLauncher
{
    Task<int> RunInheritingConsoleAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null);
}
