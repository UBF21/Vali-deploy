using Spectre.Console;
using vali_deploy.Managers;
using vali_deploy.Utils;

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
        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();
        
        bool userWantsUpdate = AnsiConsole.Confirm("[yellow]Do you want to upgrade now?[/]");
        if (userWantsUpdate)
        {
            string osIdentifier = Util.GetOsIdentifier();
            if (updateInfo.Downloads.TryGetValue(osIdentifier, out string? downloadUrl))
            {
                if (downloadUrl != null) await UpdaterManager.DownloadAndInstallAsync(downloadUrl,updateInfo.Version);
                UpdaterManager.LaunchNewVersionAndExit();
            }
            else
            {
                AnsiConsole.MarkupLine("[red]No download available for your operating system.[/]");
                UpdaterManager.DeleteOldVersions();
                await MenuManager.StartAsync();
            }
        }
        else
        {
            UpdaterManager.DeleteOldVersions();
            await MenuManager.StartAsync();
        }
    }
    else
    {
        UpdaterManager.DeleteOldVersions();
        await MenuManager.StartAsync();
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