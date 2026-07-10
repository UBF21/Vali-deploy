using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Presentation;

public static class DeployHistoryView
{
    private const int MaxRunsShown = 30;
    private const string AllProjectsOption = "[grey]Todos los proyectos[/]";
    private const string BackOption = "[seagreen1]Back to Main Menu[/]";

    public static Task ShowAsync(IDeployHistoryRepository repository, IReadOnlyList<string> projectNames)
    {
        AnsiConsole.Clear();
        ShellRenderer.DrawHeader(new Dictionary<string, Project>(), breadcrumb: "Deploy History");

        var filter = PromptProjectFilter(projectNames);
        var result = repository.GetRecent(MaxRunsShown, filter);

        if (result.Runs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No hay runs registrados todavía.[/]");
            PauseForUserInput();
            return Task.CompletedTask;
        }

        RenderTable(result.Runs);
        WarnIfCorruptedLinesSkipped(result.SkippedCorruptedLines);

        var entry = PromptRunSelection(result.Runs);
        if (entry != null)
        {
            ShowDetail(entry);
        }

        return Task.CompletedTask;
    }

    private static string? PromptProjectFilter(IReadOnlyList<string> projectNames)
    {
        var projectFilter = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Filtrar por proyecto:")
                .AddChoices(new[] { AllProjectsOption }.Concat(projectNames)));

        return projectFilter == AllProjectsOption ? null : projectFilter;
    }

    private static void WarnIfCorruptedLinesSkipped(int skippedCorruptedLines)
    {
        if (skippedCorruptedLines > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{skippedCorruptedLines} línea(s) ilegible(s) del índice fueron omitidas.[/]");
        }
    }

    private static DeployRunSummary? PromptRunSelection(IReadOnlyList<DeployRunSummary> runs)
    {
        var choices = runs.Select(DescribeRun).Append(BackOption).ToList();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Elegí un run para ver el detalle:")
                .AddChoices(choices));

        return selected == BackOption ? null : runs[choices.IndexOf(selected)];
    }

    private static void RenderTable(IReadOnlyList<DeployRunSummary> runs)
    {
        var table = new Table().AddColumns("Fecha", "Proyecto", "Subproyecto", "Entorno", "Estado", "Duración");

        foreach (var run in runs)
        {
            table.AddRow(
                run.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                run.ProjectName,
                run.SubProjectName,
                run.EnvironmentName,
                run.Success ? "[green]OK[/]" : "[red]FALLÓ[/]",
                run.TotalDuration.ToString(@"mm\:ss"));
        }

        AnsiConsole.Write(table);
    }

    private static string DescribeRun(DeployRunSummary run) =>
        $"{run.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {run.ProjectName}/{run.SubProjectName} · {run.EnvironmentName}";

    private static void ShowDetail(DeployRunSummary entry)
    {
        AnsiConsole.Clear();

        if (!File.Exists(entry.LogFilePath))
        {
            AnsiConsole.Write(new Panel("[red]El archivo de log de este run ya no existe en disco.[/]")
                .Header($"{entry.ProjectName}/{entry.SubProjectName} · {entry.EnvironmentName}"));
            PauseForUserInput();
            return;
        }

        var content = File.ReadAllText(entry.LogFilePath);
        AnsiConsole.Write(new Panel(content.EscapeMarkup())
            .Header($"{entry.ProjectName}/{entry.SubProjectName} · {entry.EnvironmentName} · {entry.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"));

        PauseForUserInput();
    }

    private static void PauseForUserInput()
    {
        AnsiConsole.MarkupLine("[grey]Presioná una tecla para continuar...[/]");
        Console.ReadKey(true);
    }
}
