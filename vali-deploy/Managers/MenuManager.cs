using Spectre.Console;
using vali_deploy.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace vali_deploy.Managers;

public static class MenuManager
{
    public static async Task StartAsync()
    {
        var projects = ProjectManager.LoadOrCreateConfig();
        var barChart = ChartManager.CreateBarChart(projects);

        bool running = true;

        while (running)
        {
            AnsiConsole.Clear();  // Limpia todo lo que Spectre.Console imprimió
            
            AnsiConsole.Write( new Rule());
            AnsiConsole.Write(new Rule("[red] Developed by [yellow]Felipe Rafael M.M[/] [/]"));
            AnsiConsole.Write( new Rule());
            AnsiConsole.WriteLine();


            // Mostrar el título y el gráfico de barras
            var gridHeader = new Grid();
            gridHeader.AddColumn(new GridColumn().RightAligned());
            gridHeader.AddColumn(new GridColumn().LeftAligned());

            gridHeader.AddRow(new FigletText("Vali-Deploy").LeftJustified().Color(Color.Yellow), barChart);
            AnsiConsole.Write(gridHeader);

            AnsiConsole.WriteLine();
            
            // Menú principal
            var option = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("What do you want to do?")
                    .AddChoices(new[] { "Add Project", "Remove Project", "Show Projects", "[chartreuse3_1]Exit[/]" })
                
            );
            

            
            switch (option)
            {
                case "Add Project":
                    await AddProjectAsync();
                    projects = ProjectManager.LoadOrCreateConfig();
                    barChart = ChartManager.CreateBarChart(projects);
                    break;

                case "Remove Project":
                    RemoveProject();
                    projects = ProjectManager.LoadOrCreateConfig();
                    barChart = ChartManager.CreateBarChart(projects);
                    break;

                case "Show Projects":
                    await ShowProjectsAsync();
                    break;

                case "[chartreuse3_1]Exit[/]":
                    running = false;
                    AnsiConsole.MarkupLine("[yellow]Leaving...[/]");
                    break;
            }

            if (option == "Remove Project")
            {
                AnsiConsole.MarkupLine(":hand_with_fingers_splayed: Press any key to continue...");
                Console.ReadKey(true);
            }
        }
    }

    private static Task AddProjectAsync()
    {
        // Solicitar el nombre del proyecto
        string projectName;
        while (true)
        {
            projectName = AnsiConsole.Ask<string>("Enter the project name (or type 'done' to cancel):");
            if (projectName.ToLower() == "done") return Task.CompletedTask;
            if (!string.IsNullOrWhiteSpace(projectName)) break;
            AnsiConsole.MarkupLine("[red]Project name cannot be empty.[/]");
        }

        // Solicitar la ruta del proyecto y validar que exista
        string projectPath;
        while (true)
        {
            projectPath = AnsiConsole.Ask<string>("Enter the project path:");
            if (projectPath.ToLower() == "done") return Task.CompletedTask;
            if (Directory.Exists(projectPath)) break; // La ruta es válida, salir del bucle
            
            AnsiConsole.MarkupLine($"[red]:cross_mark: The project path does not exist: {projectPath}[/]");
            AnsiConsole.MarkupLine("Please enter a valid path.");
        }

        // Pedir al usuario que ingrese los subproyectos (APIs)
        var subProjects = new List<SubProject>();
        bool addMoreSubProjects = true;

        while (addMoreSubProjects)
        {
            var subProjectName = AnsiConsole.Ask<string>("Enter the subproject name (or type 'done' to finish):");

            if (subProjectName.ToLower() == "done")
            {
                if (subProjects.Count == 0)
                {
                    AnsiConsole.MarkupLine("[red]:warning: You must add at least one subproject.[/]");
                    continue;
                }

                addMoreSubProjects = false;
            }
            else
            {
                // Solicitar la ruta del subproyecto y validar que exista
                string subProjectPath;
                while (true)
                {
                    subProjectPath = AnsiConsole.Ask<string>("Enter the subproject path:");
                    string fullPathSubProject = Path.Combine(projectPath, subProjectPath);

                    if (Directory.Exists(fullPathSubProject))
                    {
                        break; // La ruta es válida, salir del bucle
                    }

                    AnsiConsole.MarkupLine(
                        $"[red]:cross_mark: The subproject path does not exist: {subProjectPath}[/]");
                    AnsiConsole.MarkupLine("Please enter a valid path.");
                }

                // Agregar el subproyecto a la lista
                subProjects.Add(new SubProject { Name = subProjectName, Path = subProjectPath });
                AnsiConsole.MarkupLine($"[green]Subproject '{subProjectName}' added.[/]");
            }
        }

        // Agregar el proyecto con los subproyectos
        ProjectManager.AddProject(projectName, new Project { Path = projectPath, SubProjects = subProjects });
        AnsiConsole.MarkupLine($"[green]Project '{projectName}' added successfully![/]");
        return Task.CompletedTask;
    }

    private static void RemoveProject()
    {
        var projects = ProjectManager.LoadOrCreateConfig();
        if (projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
            return;
        }

        var projectToRemove = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to remove")
                .AddChoices(projects.Keys)
        );
        ProjectManager.RemoveProject(projectToRemove);
    }

    private static async Task ShowProjectsAsync()
    {
        while (true) // Bucle para mantener al usuario en el menú de proyectos
        {
            var projects = ProjectManager.LoadOrCreateConfig();
            if (projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                AnsiConsole.MarkupLine("Press any key to return to the main menu...");
                Console.ReadKey();
                return; // Regresar al menú principal
            }

            // Mostrar la lista de proyectos con la opción "Back"
            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a project")
                    .AddChoices(projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
            );

            if (projectName == "[chartreuse3_1]Back to Main Menu[/]")
            {
                return;
            }

            // Entrar al menú de subproyectos del proyecto seleccionado
            bool exitToMainMenu = await ShowSubProjectsAsync(projects[projectName], projectName);
            if (exitToMainMenu)
            {
                break; // Salir del bucle y regresar al menú principal
            }
        }
    }

    private static async Task<bool> ShowSubProjectsAsync(Project project, string projectName)
    {
        while (true) // Bucle para mantener al usuario en el menú de subproyectos
        {
            if (project.SubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]:warning: No subprojects found for project '{projectName}'.[/]");
                AnsiConsole.MarkupLine("Press any key to return to the projects menu...");
                Console.ReadKey();
                return false; // Regresar al menú de proyectos
            }

            // Si hay exactamente un subproyecto, ejecutarlo automáticamente
            if (project.SubProjects.Count == 1)
            {
                var subProject = project.SubProjects.First();
                await ExecuteCommandSubProject(project, subProject, projectName);
                return true; // Regresar al menú principal después de ejecutar
            }

            // Mostrar la lista de subproyectos con la opción "Back"
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a subproject for project '{projectName}'")
                    .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects Menu[/]"))
            );

            if (subProjectName == "[chartreuse3_1]Back to Projects Menu[/]")
            {
                return false; // Regresar al menú de proyectos
            }

            // Obtener el subproyecto seleccionado
            var selectedSubProject = project.SubProjects.FirstOrDefault(sp => sp.Name == subProjectName);
            if (selectedSubProject == null)
            {
                AnsiConsole.MarkupLine("[red]:cross_mark: Subproject not found.[/]");
                continue;
            }

            await ExecuteCommandSubProject(project, selectedSubProject, projectName);
            return true;
        }
    }

    private static async Task ExecuteCommandSubProject(Project project, SubProject subProject, string projectName)
    {
        string subProjectPathFull = Path.Combine(project.Path, subProject.Path);
        AnsiConsole.MarkupLine(
            $"[green]Running publish for subproject '{subProject.Name}' in project '{projectName}'...[/]");
    
        await CommandExecutor.RunCommandsAsync(subProjectPathFull);

        AnsiConsole.MarkupLine("Press any key to continue...");
        await Task.Run(() => Console.ReadKey(true));
        await StartAsync();
    }
}