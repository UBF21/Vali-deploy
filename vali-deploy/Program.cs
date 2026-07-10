using Spectre.Console;
using vali_deploy;
using vali_deploy.Managers;
using vali_deploy.Presentation;
using vali_deploy.Utils;

async Task LaunchShellAsync()
{
    var config = CompositionRoot.CreateProjectRepository().Load();
    SplashScreen.ShowAndWait(config);
    await MenuManager.StartAsync();
}

try
{
    string jsonUrl = Constants.UrlVersion;
    string currentVersion = Util.GetCurrentVersion();

    // Consulta el JSON y obtiene la información de actualización (si existe)
    var updateInfo = await UpdaterManager.GetUpdateInfoAsync(jsonUrl, currentVersion);

    if (updateInfo != null)
    {
        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new FigletText("!New version available!").Centered().Color(Color.Yellow));
        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule($" NEW : {updateInfo.Version}").RightJustified());
        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel($"{updateInfo.ReleaseNotes}")
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Aquamarine3)
            .Header($"Release - {Markup.Escape(updateInfo.ReleaseDate)}\t"));
        AnsiConsole.WriteLine();
        
        bool userWantsUpdate = AnsiConsole.Confirm("[yellow]Do you want to upgrade now?[/]");
        if (userWantsUpdate)
        {
            string osIdentifier = Util.GetOsIdentifier();
            if (updateInfo.Downloads.TryGetValue(osIdentifier, out string? downloadUrl))
            {
                if (downloadUrl != null)
                {
                    updateInfo.Checksums.TryGetValue(osIdentifier, out string? expectedChecksum);
                    await UpdaterManager.DownloadAndInstallAsync(downloadUrl, updateInfo.Version, expectedChecksum);
                }
                UpdaterManager.LaunchNewVersionAndExit();
            }
            else
            {
                AnsiConsole.MarkupLine("[red]No download available for your operating system.[/]");
                UpdaterManager.DeleteOldVersions();
                await LaunchShellAsync();
            }
        }
        else
        {
            UpdaterManager.DeleteOldVersions();
            await LaunchShellAsync();
        }
    }
    else
    {
        UpdaterManager.DeleteOldVersions();
        await LaunchShellAsync();
    }
}
catch (Exception ex)
{
    // Manejar errores inesperados
    AnsiConsole.MarkupLine($"[red] :cross_mark: Fatal error: {Markup.Escape(ex.Message)}[/]");
    if (ex.StackTrace != null) AnsiConsole.MarkupLine($"[red] :books: StackTrace: {Markup.Escape(ex.StackTrace)}[/]");
    AnsiConsole.MarkupLine(" :hand_with_fingers_splayed: Press any key to continue...");
    Console.ReadKey();
}