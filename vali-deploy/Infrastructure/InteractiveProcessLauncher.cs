using System.Diagnostics;

namespace vali_deploy.Infrastructure;

/// <summary>
/// Arranca un proceso heredando la consola del proceso padre (sin redirigir stdin/stdout/stderr), para
/// sesiones interactivas reales (ej. "docker run -it"). A diferencia de IProcessRunner, no captura salida
/// como texto — es intencional, la salida queda en la terminal del usuario, no en ningún log.
/// </summary>
public class InteractiveProcessLauncher : IInteractiveProcessLauncher
{
    public async Task<int> RunInheritingConsoleAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };

        if (extraEnvVars != null)
        {
            foreach (var (key, value) in extraEnvVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }
}
