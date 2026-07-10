using Spectre.Console;
using vali_deploy.Domain;

namespace vali_deploy.Presentation;

public static class EnvironmentsTreeView
{
    public static Task ShowAsync(IReadOnlyList<EnvironmentTreeNode> environments)
    {
        AnsiConsole.Clear();
        ShellRenderer.DrawHeader(new Dictionary<string, Project>(), breadcrumb: "Environments Tree");

        if (environments.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No hay entornos configurados. Andá a 'Manage Environments' para agregar uno.[/]");
            PauseForUserInput();
            return Task.CompletedTask;
        }

        foreach (var environmentNode in environments)
        {
            AnsiConsole.Write(BuildTree(environmentNode));
        }

        PauseForUserInput();
        return Task.CompletedTask;
    }

    private static Tree BuildTree(EnvironmentTreeNode environmentNode)
    {
        var label = environmentNode.Projects.Count == 0
            ? $"{Markup.Escape(environmentNode.EnvironmentName)} [grey](sin proyectos)[/]"
            : Markup.Escape(environmentNode.EnvironmentName);

        var tree = new Tree($"[yellow]{label}[/]");

        foreach (var projectNode in environmentNode.Projects)
        {
            if (projectNode.SubProjectNames.Count == 0)
            {
                tree.AddNode(Markup.Escape(projectNode.ProjectName));
                continue;
            }

            var branch = tree.AddNode($"[green]{Markup.Escape(projectNode.ProjectName)}[/]");
            foreach (var subProjectName in projectNode.SubProjectNames)
            {
                branch.AddNode(Markup.Escape(subProjectName));
            }
        }

        return tree;
    }

    private static void PauseForUserInput()
    {
        AnsiConsole.MarkupLine("[grey]Presioná una tecla para continuar...[/]");
        Console.ReadKey(true);
    }
}
