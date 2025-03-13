using System.Diagnostics;
using System.IO.Compression;
using Spectre.Console;
using vali_deploy.Models;

namespace vali_deploy.Managers;

public static class CommandExecutor
{
    public static async Task RunCommandsAsync(string projectName, string subProjectName, string projectPath,
        SubProject? subProject)
    {
        if (!Directory.Exists(projectPath))
        {
            AnsiConsole.MarkupLine(
                $"[red]:cross_mark: The project route does not exist: {Markup.Escape(projectPath)}[/]");
            return;
        }

        AnsiConsole.MarkupLine($" :open_file_folder: Switching to directory {Markup.Escape(projectPath)}");

        Directory.SetCurrentDirectory(projectPath);
        
        string publishArgs = subProject?.PublishArgs is { Count: > 0 }
            ? " " + string.Join(" ", subProject.PublishArgs)
            : "";
        
        var commands = new List<string>
        {
            OperatingSystem.IsWindows() ? "rmdir /s /q bin && rmdir /s /q obj" : "rm -rf bin; rm -rf obj",
            "dotnet clean",
            "dotnet build",
            $"dotnet publish -c Release {publishArgs}"
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
        string? publishFolder = Directory.EnumerateDirectories(releaseFolder, "publish", SearchOption.AllDirectories)
            .FirstOrDefault();
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
            .StartAsync("Clearing the publication directory...", async ctx =>
            {
                // Procesar los archivos y carpetas a omitir desde OmitFiles
                if (subProject != null && subProject.OmitFiles.Any())
                {
                    AnsiConsole.MarkupLine("[yellow]:information: Cleaning up omitted files and folders...[/]");
                    foreach (var item in subProject.OmitFiles)
                    {
                        string fullPath = Path.Combine(publishFolder, item);

                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                            AnsiConsole.MarkupLine($"[green]:check_mark: Deleted file: {Markup.Escape(fullPath)}[/]");
                        }
                        else if (Directory.Exists(fullPath))
                        {
                            Directory.Delete(fullPath, true); // Elimina la carpeta y su contenido
                            AnsiConsole.MarkupLine($"[green]:check_mark: Deleted folder: {Markup.Escape(fullPath)}[/]");
                        }
                        else
                        {
                            AnsiConsole.MarkupLine(
                                $"[yellow]:warning: Item not found in publish folder: {Markup.Escape(fullPath)}[/]");
                        }
                    }
                }
                else
                {
                    AnsiConsole.MarkupLine("[yellow]:information: No files or folders to omit. Skipping cleanup.[/]");
                }

                await Task.CompletedTask;
            });

        if (subProject is { ZipPublishOutput: true })
        {
            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dqpb)
                .SpinnerStyle(Style.Parse("green"))
                .StartAsync("Packaging in progress...", async ctx =>
                {
                    // Crear el archivo ZIP con el nombre deseado
                    string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                    string zipFileName = $"publish_{projectName}_{subProjectName}_{timestamp}.zip";
                    // Guardamos el ZIP en el directorio padre de la carpeta publish
                    string parentFolder = Directory.GetParent(publishFolder)?.FullName ?? publishFolder;
                    string zipFilePath = Path.Combine(parentFolder, zipFileName);

                    try
                    {
                        ZipFile.CreateFromDirectory(publishFolder, zipFilePath);
                        AnsiConsole.MarkupLine(
                            $"[green]:check_mark: Packaging complete: {Markup.Escape(zipFilePath)}[/]");
                        OpenFileExplorer(parentFolder);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Packaging error: {Markup.Escape(ex.Message)}[/]");
                    }

                    await Task.CompletedTask;
                });
        }
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

    public static async Task<int> ExecuteDockerCommandAsync(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows()
                ? $"/c set DOCKER_BUILDKIT=1 && {command}"
                : $"-c \"DOCKER_BUILDKIT=1 {command}\"",
            RedirectStandardOutput = true, // Necesario para leer la salida estándar
            RedirectStandardError = true, // Para leer errores
            UseShellExecute = false, // Requerido para redirigir salida
            CreateNoWindow = true // Evita mostrar ventana en GUI
        };

        using var process = new Process();
        process.StartInfo = startInfo;
        process.Start();

        string output = await process.StandardOutput.ReadToEndAsync();
        string error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (!string.IsNullOrEmpty(output))
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(output)}[/]");
        if (!string.IsNullOrEmpty(error))
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(error)}[/]");

        return process.ExitCode;
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Operating system is not supported.");
        }

        ProcessStartInfo processStartInfo = new ProcessStartInfo()
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

    private static void OpenFileExplorer(string folderPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // En Windows, usar explorer.exe para abrir la carpeta
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                // En macOS, usar 'open' para abrir Finder
                Process.Start(new ProcessStartInfo
                {
                    FileName = "open",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                // En Linux, intentar con xdg-open (común en la mayoría de distribuciones)
                Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-open",
                    Arguments = $"\"{folderPath}\"",
                    UseShellExecute = true
                });
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]:warning: Unsupported operating system. Cannot open file explorer.[/]");
            }
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]Error opening file explorer: {Markup.Escape(ex.Message)}[/]");
        }
    }
}