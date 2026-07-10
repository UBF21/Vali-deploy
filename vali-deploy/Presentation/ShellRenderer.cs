using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Utils;

namespace vali_deploy.Presentation;

/// <summary>
/// Dibuja la franja de header compartida por el menú raíz y los submenús (EnvironmentMenu,
/// PipelineEditorMenu): marca + versión a la izquierda, resumen global o breadcrumb a la derecha.
/// No hace AnsiConsole.Clear() — eso queda a cargo del caller, para no acoplar el renderer a cuándo
/// debe limpiarse la pantalla. No fija anchos: Grid/Rule se re-miden contra el ancho de consola
/// vigente en cada llamada.
/// </summary>
public static class ShellRenderer
{
    public static void DrawHeader(IReadOnlyDictionary<string, Project> projects, string? breadcrumb = null)
    {
        var currentVersion = Util.GetCurrentVersion();
        var subProjectCount = projects.Values.Sum(p => p.SubProjects.Count);

        var status = breadcrumb is null
            ? $"{projects.Count} proyectos · {subProjectCount} subproyectos"
            : Markup.Escape(breadcrumb);

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().RightAligned())
            .AddRow(
                new Markup($"[bold {ShellPalette.BrandTag}]Vali-Deploy[/] [{ShellPalette.MutedTag}]v{currentVersion}[/]"),
                new Markup($"[{ShellPalette.MutedTag}]{status}[/]"));

        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule().RuleStyle(new Style(foreground: ShellPalette.Muted)));
        AnsiConsole.WriteLine();
    }
}
