using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;

namespace vali_deploy.Managers;

public static class UpdaterManager
{
    private static readonly string[] KnownRuntimeIdentifiers = { "win-x64", "osx-x64", "osx-arm64", "linux-x64" };

    // Este método consulta la API de GitHub Releases y devuelve la información de actualización si existe
    public static async Task<UpdateInfo?> GetUpdateInfoAsync(string url, string currentVersion)
    {
        try
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vali-Deploy-Updater");

            string jsonResponse = await client.GetStringAsync(url);
            var release = JsonSerializer.Deserialize<GitHubRelease>(jsonResponse,
                options: new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (release == null)
            {
                return null;
            }

            var updateInfo = MapToUpdateInfo(release);
            updateInfo.Checksums = await FetchChecksumsAsync(client, release);

            if (Util.IsNewerVersion(updateInfo.Version, currentVersion))
            {
                return updateInfo;
            }

            AnsiConsole.MarkupLine("[blue]You already have the latest version.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red] :cross_mark: Error checking for updates: {Markup.Escape(ex.Message)}[/]");
        }

        return null;
    }

    private static UpdateInfo MapToUpdateInfo(GitHubRelease release)
    {
        var version = release.TagName.TrimStart('v');
        var downloads = new Dictionary<string, string?>();

        foreach (var rid in KnownRuntimeIdentifiers)
        {
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip"));
            if (asset != null)
            {
                downloads[rid] = asset.BrowserDownloadUrl;
            }
        }

        return new UpdateInfo
        {
            Version = version,
            Downloads = downloads,
            ReleaseDate = release.PublishedAt,
            ReleaseNotes = release.Body
        };
    }

    private static async Task<Dictionary<string, string?>> FetchChecksumsAsync(HttpClient client, GitHubRelease release)
    {
        var checksums = new Dictionary<string, string?>();
        var checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset == null)
        {
            return checksums;
        }

        var content = await client.GetStringAsync(checksumAsset.BrowserDownloadUrl);

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;

            var hash = parts[0];
            var fileName = parts[1].TrimStart('*');

            var matchedRid = KnownRuntimeIdentifiers.FirstOrDefault(rid =>
                fileName.Contains(rid, StringComparison.OrdinalIgnoreCase));
            if (matchedRid == null) continue;

            checksums[matchedRid] = hash;
        }

        return checksums;
    }

    // Elimina archivos anteriores (versiones antiguas) en la carpeta base de la aplicación.
    public static void DeleteOldVersions()
    {
        // Obtener la ruta base de la aplicación
        string baseDirectory = AppContext.BaseDirectory;
        string? currentFileName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName);

        if (string.IsNullOrEmpty(baseDirectory)) return;

        string[] files = Directory.GetFiles(baseDirectory, "Vali-Deploy_*");
        var oldFiles = files.Where(file =>
            !string.Equals(Path.GetFileName(file), currentFileName, StringComparison.OrdinalIgnoreCase));

        foreach (var file in oldFiles)
        {
            try
            {
                File.Delete(file);
                AnsiConsole.MarkupLine($"[green]:check_mark: Removed previous version: {Markup.Escape(file)}[/]");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(
                    $"[red] :cross_mark: Error when deleting {Markup.Escape(file)}: {Markup.Escape(ex.Message)}[/]");
            }
        }
    }

    // Método que descarga el ZIP, lo extrae y reemplaza el ejecutable actual.
    public static async Task DownloadAndInstallAsync(string downloadUrl, string newVersion)
    {
        // Obtener la carpeta donde está el ejecutable actual.
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string exeDirectory = Path.GetDirectoryName(currentExePath) ?? Environment.CurrentDirectory;

        // Derivar el nombre del ZIP a partir de la URL (se conserva el nombre real del archivo descargado).
        string downloadedZipFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string zipPath = Path.Combine(exeDirectory, downloadedZipFileName);
        // Ruta temporal de extracción.
        string tempExtractPath = Path.Combine(exeDirectory, "TempUpdate");

        // Estado de descarga.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Downloading new version...", async ctx =>
            {
                using HttpClient client = new HttpClient();
                byte[] data = await client.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(zipPath, data);
            });
        AnsiConsole.MarkupLine("[green]:check_mark: Download completed.[/]");

        // Estado de extracción.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Extracting new version...", async ctx =>
            {
                if (Directory.Exists(tempExtractPath))
                    Directory.Delete(tempExtractPath, true);
                Directory.CreateDirectory(tempExtractPath);
                ZipFile.ExtractToDirectory(zipPath, tempExtractPath);
                File.Delete(zipPath);
                await Task.CompletedTask;
            });
        AnsiConsole.MarkupLine("[green]:check_mark: Extraction completed.[/]");

        // Calcular el nombre esperado del nuevo ejecutable según la versión.
        string expectedNewExeFileName;
        if (OperatingSystem.IsWindows())
            expectedNewExeFileName = $"Vali-Deploy_{newVersion}.exe";
        else
            expectedNewExeFileName = $"Vali-Deploy_{newVersion}";

        string newExePath = Path.Combine(tempExtractPath, expectedNewExeFileName);
        AnsiConsole.MarkupLine($"[green]Expected new executable: {Markup.Escape(expectedNewExeFileName)}[/]");
        AnsiConsole.MarkupLine($"[green]Looking for new executable at: {Markup.Escape(newExePath)}[/]");

        if (!File.Exists(newExePath))
        {
            AnsiConsole.MarkupLine("[red]:cross_mark: The new executable was not found in the extracted folder.[/]");
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            AnsiConsole.MarkupLine("[yellow]Removing quarantine attribute and setting execution permissions...[/]");
            await Process.Start("xattr", $"-rd com.apple.quarantine \"{newExePath}\"").WaitForExitAsync();
        }

        // Estado de instalación.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Installing new version...", async ctx =>
            {
                // Reemplazar el ejecutable actual con el nuevo (sobrescribiendo)
                string targetNewExePath = Path.Combine(exeDirectory, expectedNewExeFileName);
                File.Copy(newExePath, targetNewExePath, true);
                Directory.Delete(tempExtractPath, true);
                await Task.CompletedTask;
                Process.Start(new ProcessStartInfo { FileName = targetNewExePath, UseShellExecute = false });
                Environment.Exit(0);
            });

        AnsiConsole.MarkupLine(
            $"[green]:check_mark: New version installed: {Markup.Escape(expectedNewExeFileName)}[/]");
    }

    // Lanza la nueva versión y cierra la aplicación actual
    public static void LaunchNewVersionAndExit()
    {
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        Console.WriteLine("Restarting the application with the new version...");
        Process.Start(currentExePath);
        Environment.Exit(0);
    }
}