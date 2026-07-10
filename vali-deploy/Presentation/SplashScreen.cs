using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Utils;

namespace vali_deploy.Presentation;

/// <summary>
/// Pantalla de arranque: se muestra una vez, antes de entrar al shell (MenuManager.StartAsync).
/// FigletText grande centrado (a diferencia de ShellRenderer.DrawHeader, que usa texto simple y
/// se repite en cada pantalla — el Figlet queda reservado exclusivamente para acá). Sin anchos
/// fijos: Table/Align se centran y re-miden contra el ancho de consola vigente.
/// </summary>
public static class SplashScreen
{
    public static void ShowAndWait(DeployConfig config)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(Align.Center(new FigletText("Vali-Deploy").Color(ShellPalette.Brand)));
        AnsiConsole.Write(Align.Center(new Markup($"[{ShellPalette.MutedTag}]v{Util.GetCurrentVersion()}[/]")));
        AnsiConsole.WriteLine();

        var subProjectCount = config.Projects.Values.Sum(p => p.SubProjects.Count);
        var summary = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ShellPalette.Muted)
            .HideHeaders()
            .AddColumn("clave")
            .AddColumn("valor");
        summary.AddRow($"[{ShellPalette.MutedTag}]Proyectos[/]", $"{config.Projects.Count}");
        summary.AddRow($"[{ShellPalette.MutedTag}]Subproyectos[/]", $"{subProjectCount}");

        AnsiConsole.Write(Align.Center(summary));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(Align.Center(new Markup($"[{ShellPalette.MutedTag}]Presione una tecla para continuar…[/]")));
        Console.ReadKey(true);
    }
}
