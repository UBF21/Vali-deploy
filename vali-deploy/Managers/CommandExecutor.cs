using System.Diagnostics;
using Spectre.Console;

namespace vali_deploy.Managers;

public static class CommandExecutor
{
    public static async Task RunCommandsAsync(string projectPath)
    {
        if (!Directory.Exists(projectPath))
        {
            AnsiConsole.MarkupLine($"[red]:cross_mark: The project route does not exist: {Markup.Escape(projectPath)}[/]");
            return;
        }

        AnsiConsole.MarkupLine($" :open_file_folder: Switching to directory {Markup.Escape(projectPath)}");

        Directory.SetCurrentDirectory(projectPath);

        var commands = new List<string>
        {
            OperatingSystem.IsWindows() ? "rmdir /s /q bin && rmdir /s /q obj" : "rm -rf bin; rm -rf obj",
            "dotnet clean",
            "dotnet build",
            "dotnet publish -c Release"
        };

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Executing build commands...", async ctx =>
            {
                foreach (var cmd in commands)
                {
                    await ExecuteCommandAsync(cmd, projectPath);
                }
            });

        // Busca el directorio "publish" dentro de "bin/Release" de forma recursiva
        string releaseFolder = Path.Combine(projectPath, "bin", "Release");
        string? publishFolder = Directory.EnumerateDirectories(releaseFolder, "publish", SearchOption.AllDirectories).FirstOrDefault();
        if (string.IsNullOrEmpty(publishFolder) || !Directory.Exists(publishFolder))
        {
            AnsiConsole.MarkupLine("[red] :cross_mark: Publication folder not found.[/]");
            return;
        }

        AnsiConsole.MarkupLine($" :open_file_folder: Publication folder found: {Markup.Escape(publishFolder)}");

        Directory.SetCurrentDirectory(publishFolder);

        bool isWebApiProject = IsWebApiProject(projectPath);
        
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Star)
            .StartAsync("Clearing the publication directory...", ctx =>
            {
                if (isWebApiProject)
                {
                    AnsiConsole.MarkupLine("[yellow]:information: This is a Web API project. Cleaning up appsettings...[/]");

                    List<string> appSettingsFiles = new List<string>
                    {
                        Path.Combine(publishFolder, "appsettings.json"),
                        Path.Combine(publishFolder, "appsettings.Development.json")
                    };

                    foreach (var file in appSettingsFiles)
                    {
                        if (File.Exists(file))
                        {
                            File.Delete(file);
                            AnsiConsole.MarkupLine($"[green]:check_mark: Deleted: {Markup.Escape(file)}[/]");
                        }
                    }

                    string wwwRootPath = Path.Combine(publishFolder, "wwwroot");
                    if (Directory.Exists(wwwRootPath))
                    {
                        Directory.Delete(wwwRootPath, true);
                        AnsiConsole.MarkupLine($"[green]:check_mark: Deleted: {Markup.Escape(wwwRootPath)}[/]");
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]:information: This is not a Web API project. Skipping cleanup of appsettings.[/]");
                }

                return Task.CompletedTask;
            });
    }

    private static async Task ExecuteCommandAsync(string command, string workingDirectory)
    {
        AnsiConsole.MarkupLine($" :racing_car: Running:  {Markup.Escape(command)}");

        var startInfo = CreateProcessStartInfo(command, workingDirectory);

        var process = new Process { StartInfo = startInfo };

        process.Start();
        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrEmpty(output))
            AnsiConsole.MarkupLine($"[springgreen2_1]{Markup.Escape(output)}[/]");
        if (!string.IsNullOrEmpty(error))
            AnsiConsole.MarkupLine($"[red]:warning: Error: {Markup.Escape(error)}[/]");
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Operating system is not supported.");
        }
        
        ProcessStartInfo processStartInfo =  new ProcessStartInfo()
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        
        return processStartInfo;
        
        
    }
    
    private static bool IsWebApiProject(string projectPath)
    {
        // Verificar si el proyecto es una Web API buscando el archivo Program.cs o Startup.cs
        string programCsPath = Path.Combine(projectPath, "Program.cs");
        string startupCsPath = Path.Combine(projectPath, "Startup.cs");

        // También puedes verificar si el archivo .csproj contiene <OutputType>Exe</OutputType> o <OutputType>Library</OutputType>
        string? csprojPath = Directory.GetFiles(projectPath, "*.csproj").FirstOrDefault();
        if (!string.IsNullOrEmpty(csprojPath))
        {
            string csprojContent = File.ReadAllText(csprojPath);
            if (csprojContent.Contains("<OutputType>Exe</OutputType>"))
            {
                return false; // No es una Web API
            }
        }

        // Si existe Program.cs o Startup.cs, es probable que sea una Web API
        return File.Exists(programCsPath) || File.Exists(startupCsPath);
    }
    
}