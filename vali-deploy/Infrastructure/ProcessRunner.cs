using System.Diagnostics;
using System.Text;

namespace vali_deploy.Infrastructure;

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null)
    {
        var startInfo = CreateProcessStartInfo(command, workingDirectory);

        if (extraEnvVars != null)
        {
            foreach (var (key, value) in extraEnvVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Sistema operativo no soportado para ejecutar comandos locales.");
        }

        return new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
