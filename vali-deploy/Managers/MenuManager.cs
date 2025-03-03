using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;

namespace vali_deploy.Managers;

public static class MenuManager
{
    private static Dictionary<string, Project> _projects = new();
    private static BarChart _barChart = new();

    public static async Task StartAsync()
    {
        _projects = ProjectManager.LoadOrCreateConfig();
        _barChart = ChartManager.CreateBarChart(_projects);

        bool running = true;

        while (running)
        {
            DisplayMainMenu();
            var option = GetMainMenuOption();

            switch (option)
            {
                case "Add Project":
                    await AddProjectAsync();
                    UpdateProjectsAndChart();
                    break;

                case "Remove Project":
                    RemoveProject();
                    UpdateProjectsAndChart();
                    PauseForUserInput("Remove Project");
                    break;

                case "Show Projects":
                    await ShowProjectsAsync();
                    break;

                case "Manage Project Files to omit":
                    await ManageProjectFilesToOmitAsync();
                    UpdateProjectsAndChart();
                    break;

                case "Remove Subprojects":
                    await RemoveSubprojectsAsync();
                    UpdateProjectsAndChart();
                    break;
                case "Manage Docker Projects":
                    await ManageDockerProjectsAsync();
                    UpdateProjectsAndChart();
                    break;
                case "[chartreuse3_1]Exit[/]":
                    running = false;
                    AnsiConsole.MarkupLine("[yellow] Leaving...[/]");
                    Environment.Exit(0);
                    break;
            }
        }
    }

    // Métodos de la interfaz principal
    private static void DisplayMainMenu()
    {
        AnsiConsole.Clear();
        var currentVersion = Util.GetCurrentVersion();

        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule("[red] Developed by [yellow]Felipe Rafael M.M[/] [/]"));
        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule($"[bold grey] Version: {currentVersion}[/]").RightJustified());
        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();

        var gridHeader = new Grid()
            .AddColumn(new GridColumn().RightAligned())
            .AddColumn(new GridColumn().LeftAligned())
            .AddRow(new FigletText("Vali-Deploy").LeftJustified().Color(Color.Yellow), _barChart);

        AnsiConsole.Write(gridHeader);
        AnsiConsole.WriteLine();
    }

    private static string GetMainMenuOption()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Project", "Remove Project", "Show Projects", "Manage Project Files to omit",
                    "Remove Subprojects", "Manage Docker Projects", "[chartreuse3_1]Exit[/]")
        );
    }

    private static void UpdateProjectsAndChart()
    {
        _projects = ProjectManager.LoadOrCreateConfig();
        _barChart = ChartManager.CreateBarChart(_projects);
    }

    // Gestión de proyectos
    private static async Task AddProjectAsync()
    {
        string? projectName = PromptProjectName();
        if (projectName == null) return;

        string? projectPath = PromptProjectPath();
        if (projectPath == null) return;

        var subProjects = await PromptSubProjectsAsync(projectPath);
        if (subProjects == null) return;

        ProjectManager.AddProject(projectName, new Project { Path = projectPath, SubProjects = subProjects });
        AnsiConsole.MarkupLine($"[green]Project '{Markup.Escape(projectName)}' added successfully![/]");
    }

    private static string? PromptProjectName()
    {
        while (true)
        {
            var name = AnsiConsole.Ask<string>("Enter the project name (or type 'done' to cancel):");
            if (name.ToLower() == "done") return null;
            if (!string.IsNullOrWhiteSpace(name)) return name;
            AnsiConsole.MarkupLine("[red]Project name cannot be empty.[/]");
        }
    }

    private static string? PromptProjectPath()
    {
        while (true)
        {
            var path = AnsiConsole.Ask<string>("Enter the project path (or type 'done' to return to main menu):");
            if (path.ToLower() == "done") return null;
            if (Directory.Exists(path)) return path;
            AnsiConsole.MarkupLine($"[red]:cross_mark: The project path does not exist: {Markup.Escape(path)} [/]");
            AnsiConsole.MarkupLine("Please enter a valid path.");
        }
    }

    private static async Task<List<SubProject>?> PromptSubProjectsAsync(string projectPath)
    {
        var subProjects = new List<SubProject>();
        bool addMoreSubProjects = true;

        while (addMoreSubProjects)
        {
            var subProjectName =
                AnsiConsole.Ask<string>("Enter the subproject name (or type 'done' to return to main menu):");
            if (subProjectName.ToLower() == "done")
            {
                if (subProjects.Count == 0)
                {
                    AnsiConsole.MarkupLine(
                        "[red]:warning: You must add at least one subproject. Returning to main menu without saving...[/]");
                    return null;
                }

                addMoreSubProjects = false;
                continue;
            }

            string? subProjectPath = PromptSubProjectPath(projectPath);
            if (subProjectPath == null) continue;

            string? dockerfilePath =
                AnsiConsole.Ask<string>("Enter the Dockerfile path (relative to subproject path, or 'skip' to omit):");
            if (dockerfilePath.ToLower() == "skip")
            {
                dockerfilePath = null;
            }
            else if (!string.IsNullOrEmpty(dockerfilePath))
            {
                string fullDockerfilePath = Path.Combine(projectPath, subProjectPath, dockerfilePath);
                if (!File.Exists(fullDockerfilePath))
                {
                    AnsiConsole.MarkupLine(
                        $"[yellow]:warning: Dockerfile not found at {Markup.Escape(fullDockerfilePath)}. Proceeding without Docker.[/]");
                    dockerfilePath = null;
                }
            }

            subProjects.Add(new SubProject
            {
                Name = subProjectName,
                Path = subProjectPath,
                DockerfilePath = dockerfilePath
            });

            AnsiConsole.MarkupLine($"[green]Subproject '{Markup.Escape(subProjectName)}' added.[/]");
        }

        return await Task.FromResult(subProjects.Count > 0 ? subProjects : null);
    }

    private static string? PromptSubProjectPath(string projectPath)
    {
        while (true)
        {
            var subPath = AnsiConsole.Ask<string>("Enter the subproject path (or type 'done' to skip):");
            if (subPath.ToLower() == "done") return null;
            var fullPath = Path.Combine(projectPath, subPath);
            if (Directory.Exists(fullPath)) return subPath;
            AnsiConsole.MarkupLine(
                $"[red]:cross_mark: The subproject path does not exist: {Markup.Escape(subPath)} [/]");
            AnsiConsole.MarkupLine("Please enter a valid path.");
        }
    }

    private static void RemoveProject()
    {
        if (_projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
            return;
        }

        var projectToRemove = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to remove")
                .AddChoices(_projects.Keys)
        );
        ProjectManager.RemoveProject(projectToRemove);
    }

    // Nueva funcionalidad para eliminar subproyectos
    private static async Task RemoveSubprojectsAsync()
    {
        while (true)
        {
            if (_projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                PauseForUserInput();
                await Task.CompletedTask;
                return;
            }

            var projectName = PromptProjectSelectionForSubprojectRemoval();
            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            var project = _projects[projectName];
            if (project.SubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]:warning: No subprojects found for project '{Markup.Escape(projectName)}'.[/]");
                PauseForUserInput();
                continue;
            }

            var subProjectsToRemove = PromptMultipleSubProjectSelection(project, projectName);
            if (subProjectsToRemove == null || !subProjectsToRemove.Any()) continue;

            foreach (var subProjectName in subProjectsToRemove)
            {
                var subProject = project.SubProjects.FirstOrDefault(sp => sp.Name == subProjectName);
                if (subProject != null)
                {
                    project.SubProjects.Remove(subProject);
                    AnsiConsole.MarkupLine(
                        $"[green]Subproject '{Markup.Escape(subProjectName)}' removed from project '{Markup.Escape(projectName)}'.[/]");
                }
            }

            ProjectManager.SaveConfig(_projects);
            PauseForUserInput();
            break;
        }
    }

    private static string PromptProjectSelectionForSubprojectRemoval()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to remove subprojects from")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    private static List<string>? PromptMultipleSubProjectSelection(Project project, string projectName)
    {
        var selectedSubProjects = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title(
                    $"Select subprojects to remove from project '{projectName}' (use spacebar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Cancel[/]"))
        );

        if (selectedSubProjects.Contains("[chartreuse3_1]Cancel[/]") || selectedSubProjects.Count == 0)
            return null;

        return selectedSubProjects;
    }

    // Mostrar proyectos y subproyectos
    private static async Task ShowProjectsAsync()
    {
        while (true)
        {
            if (_projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                PauseForUserInput();
                return;
            }

            var projectName = PromptProjectSelection();
            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            if (await ShowSubProjectsAsync(_projects[projectName], projectName))
                break;
        }
    }

    private static string PromptProjectSelection()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    private static async Task<bool> ShowSubProjectsAsync(Project project, string projectName)
    {
        while (true)
        {
            if (project.SubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]:warning: No subprojects found for project '{Markup.Escape(projectName)}'.[/]");
                PauseForUserInput();
                return false;
            }

            if (project.SubProjects.Count == 1)
            {
                await ExecuteCommandSubProject(project, project.SubProjects.First(), projectName);
                return true;
            }

            var subProjectName = PromptSubProjectSelection(project, projectName);
            if (subProjectName == "[chartreuse3_1]Back to Projects Menu[/]") return false;

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

    private static string PromptSubProjectSelection(Project project, string projectName)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a subproject for project '{projectName}'")
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects Menu[/]"))
        );
    }

    // Gestión de archivos a omitir
    private static async Task ManageProjectFilesToOmitAsync()
    {
        while (true)
        {
            if (_projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                return;
            }

            var projectName = PromptProjectForOmitFiles();
            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            await ManageSubProjectFilesAsync(_projects[projectName], projectName);
        }
    }

    private static string PromptProjectForOmitFiles()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to manage files to omit")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    private static async Task ManageSubProjectFilesAsync(Project project, string projectName)
    {
        while (true)
        {
            var subProject = await SelectSubProjectAsync(project, projectName);
            if (subProject == null) return;

            await ManageFilesForSubProjectAsync(subProject, projectName);
        }
    }

    private static async Task<SubProject?> SelectSubProjectAsync(Project project, string projectName)
    {
        if (project.SubProjects.Count == 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]:warning: No subprojects found for project '{Markup.Escape(projectName)}'.[/]");
            await Task.CompletedTask;
            return null;
        }

        var subProjectName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a subproject for project '{projectName}' to manage files to omit")
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects[/]"))
        );

        if (subProjectName == "[chartreuse3_1]Back to Projects[/]") return null;

        var foundSubProject = project.SubProjects.FirstOrDefault(sp => sp.Name == subProjectName);
        if (foundSubProject == null)
        {
            AnsiConsole.MarkupLine("[red]:cross_mark: Subproject not found.[/]");
        }

        return foundSubProject;
    }

    private static async Task ManageFilesForSubProjectAsync(SubProject subProject, string projectName)
    {
        bool managingFiles = true;
        while (managingFiles)
        {
            AnsiConsole.Clear();
            DisplayOmitFiles(subProject, projectName);
            var action = PromptFileManagementAction();

            switch (action)
            {
                case "Add file to omit":
                    await AddFileToOmitAsync(subProject);
                    break;

                case "Remove file from omit list":
                    await RemoveFileFromOmitAsync(subProject);
                    break;

                case "[chartreuse3_1]Back to Subprojects[/]":
                    managingFiles = false;
                    AnsiConsole.Clear(); // Limpiar el árbol
                    DisplayMainMenu();
                    break; // Solo limpiar y salir, dejando la pantalla en blanco (o con el encabezado si se reinicia el flujo)
            }
        }
    }

    private static void DisplayOmitFiles(SubProject subProject, string projectName)
    {
        var tree = new Tree($"[yellow]{Markup.Escape(projectName)}[/]");
        var subProjectNode = tree.AddNode($"[green]{Markup.Escape(subProject.Name)}[/]");
        var publishNode = subProjectNode.AddNode("[blue]publish[/]");

        var padder = new Padder(tree).PadLeft(2);
        var panel = new Panel(padder).Header("structure");

        if (subProject.OmitFiles.Count == 0)
        {
            publishNode.AddNode("[grey]No files specified[/]");
        }
        else
        {
            foreach (var file in subProject.OmitFiles)
            {
                publishNode.AddNode($"[white]{Markup.Escape(file)}[/]");
            }
        }

        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    private static string PromptFileManagementAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add file to omit", "Remove file from omit list", "[chartreuse3_1]Back to Subprojects[/]")
        );
    }

    private static Task AddFileToOmitAsync(SubProject subProject)
    {
        bool addingFiles = true;
        while (addingFiles)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[yellow]Adding a file to omit [/]");
            AnsiConsole.WriteLine();

            var fileToAdd =
                AnsiConsole.Ask<string>("Enter the file name to omit (e.g., 'example.txt') or 'done' to return:");
            if (fileToAdd.ToLower() == "done")
            {
                addingFiles = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(fileToAdd))
            {
                AnsiConsole.MarkupLine("[red]File name cannot be empty.[/]");
                PauseForUserInput();
            }
            else if (subProject.OmitFiles.Contains(fileToAdd))
            {
                AnsiConsole.MarkupLine("[red]This file is already in the omit list.[/]");
                PauseForUserInput();
            }
            else
            {
                subProject.OmitFiles.Add(fileToAdd);
                ProjectManager.SaveConfig(_projects);
                AnsiConsole.MarkupLine($"[green]File '{Markup.Escape(fileToAdd)}' added to omit list.[/]");
                PauseForUserInput();
            }
        }

        return Task.CompletedTask;
    }

    private static Task RemoveFileFromOmitAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        if (subProject.OmitFiles.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No files to remove.[/]");
        }
        else
        {
            var filesToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.OmitFiles.Append("[chartreuse3_1]Cancel[/]"))
            );

            if (!filesToRemove.Contains("[chartreuse3_1]Cancel[/]") && filesToRemove.Count > 0)
            {
                foreach (var file in filesToRemove)
                {
                    subProject.OmitFiles.Remove(file);
                    AnsiConsole.MarkupLine($"[green]File '{Markup.Escape(file)}' removed from omit list.[/]");
                }

                ProjectManager.SaveConfig(_projects);
            }
        }

        return Task.CompletedTask;
    }

    // Ejecución de subproyectos
    private static async Task ExecuteCommandSubProject(Project project, SubProject? subProject, string projectName)
    {
        if (subProject == null) return;

        string subProjectPathFull = Path.Combine(project.Path, subProject.Path);
        string imageTag = $"{projectName.ToLower()}-{subProject.Name.ToLower()}:latest";

        var choices = new List<string> { "Generate Microsoft publish", "[chartreuse3_1]Back to Subprojects[/]" };
        if (!string.IsNullOrEmpty(subProject.DockerfilePath))
        {
            choices.Insert(1, "Docker Build");
            choices.Insert(2, "Docker Run");
            choices.Insert(3, "Push to Docker Hub");
        }

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"What do you want to do with subproject '{subProject.Name}'?")
                .AddChoices(choices)
        );

        switch (action)
        {
            case "Generate Microsoft publish":
                AnsiConsole.MarkupLine($"[green]Running normal publish for subproject '{Markup.Escape(subProject.Name)}' in project '{Markup.Escape(projectName)}'...[/]");
                await CommandExecutor.RunCommandsAsync(projectName, subProject.Name, subProjectPathFull, subProject);
                PauseForUserInput();
                break;

            case "Docker Build":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    string dockerfileFullPath = Path.Combine(subProjectPathFull, subProject.DockerfilePath);
                    AnsiConsole.MarkupLine(
                        $"[green]Building Docker image for subproject '{Markup.Escape(subProject.Name)}'...[/]");
                    string buildArgs = subProject.DockerBuildArgs != null && subProject.DockerBuildArgs.Count > 0
                        ? " " + string.Join(" ", subProject.DockerBuildArgs)
                        : "";
                    string buildCommand =
                        $"docker build -f \"{dockerfileFullPath}\" -t {imageTag}{buildArgs} \"{subProjectPathFull}\"";
                    int buildResult =
                        await CommandExecutor.ExecuteDockerCommandAsync(buildCommand); // Asumiendo que lo agregarás
                    if (buildResult == 0)
                        AnsiConsole.MarkupLine($"[green]Docker image '{imageTag}' built successfully![/]");
                    else
                        AnsiConsole.MarkupLine("[red]Docker build failed. Check the output above.[/]");
                    PauseForUserInput();
                }

                break;

            case "Docker Run":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    AnsiConsole.MarkupLine(
                        $"[green]Running Docker container for subproject '{Markup.Escape(subProject.Name)}'...[/]");
                    string runArgs = subProject.DockerRunArgs != null && subProject.DockerRunArgs.Count > 0
                        ? " " + string.Join(" ", subProject.DockerRunArgs)
                        : "";
                    string runCommand = $"docker run -it --rm{runArgs} {imageTag}";
                    int runResult = await CommandExecutor.ExecuteDockerCommandAsync(runCommand);
                    AnsiConsole.MarkupLine(runResult == 0
                        ? $"[green]Container ran successfully![/]"
                        : "[red]Docker run failed. Check the output above.[/]");
                    PauseForUserInput();
                }

                break;

            case "Push to Docker Hub":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    string? dockerHubUser = subProject.DockerHubUser;
                    
                    if (string.IsNullOrEmpty(dockerHubUser))
                    {
                        dockerHubUser = AnsiConsole.Ask<string>("Enter your Docker Hub username (this will be saved):");
                        subProject.DockerHubUser = dockerHubUser;
                        ProjectManager.SaveConfig(_projects);
                    }

                    string dockerHubTag = $"{dockerHubUser}/{imageTag}";
                    AnsiConsole.MarkupLine($"[yellow]Tagging image '{imageTag}' as '{dockerHubTag}'...[/]");
                    string tagCommand = $"docker tag {imageTag} {dockerHubTag}";
                    await CommandExecutor.ExecuteDockerCommandAsync(tagCommand);

                    AnsiConsole.MarkupLine($"[yellow]Pushing to Docker Hub as '{dockerHubTag}'...[/]");
                    string pushCommand = $"docker push {dockerHubTag}";
                    int pushResult = await CommandExecutor.ExecuteDockerCommandAsync(pushCommand);
                    AnsiConsole.MarkupLine(pushResult == 0
                        ? $"[green]Image pushed to Docker Hub successfully![/]"
                        : "[red]Push to Docker Hub failed. Check credentials or network.[/]");
                    PauseForUserInput();
                }

                break;

            case "[chartreuse3_1]Back to Subprojects[/]":
                return; // Vuelve al menú de subproyectos
        }
    }

    private static async Task ManageDockerProjectsAsync()
    {
        while (true)
        {
            var dockerProjects = _projects
                .Where(p => p.Value.SubProjects.Any(sp => !string.IsNullOrEmpty(sp.DockerfilePath)))
                .ToDictionary(p => p.Key, p => p.Value);

            if (dockerProjects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects with Dockerfiles found.[/]");
                await Task.Delay(2000);
                return;
            }

            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a project with Docker subprojects")
                    .AddChoices(dockerProjects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
            );

            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            await ManageDockerSubProjectsAsync(dockerProjects[projectName], projectName);
        }
    }

    private static async Task ManageDockerSubProjectsAsync(Project project, string projectName)
    {
        while (true)
        {
            var dockerSubProjects = project.SubProjects
                .Where(sp => !string.IsNullOrEmpty(sp.DockerfilePath))
                .ToList();

            if (dockerSubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine($"[yellow]:warning: No subprojects with Dockerfiles in '{Markup.Escape(projectName)}'.[/]");
                await Task.Delay(2000);
                return;
            }

            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a Docker subproject in '{projectName}'")
                    .AddChoices(dockerSubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects[/]"))
            );

            if (subProjectName == "[chartreuse3_1]Back to Projects[/]") return;

            var subProject = dockerSubProjects.First(sp => sp.Name == subProjectName);

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Manage Docker settings for '{subProjectName}'")
                    .AddChoices("Set Docker Build Args", "Set Docker Run Args", "[chartreuse3_1]Back to Subprojects[/]")
            );

            switch (action)
            {
                case "Set Docker Build Args":
                    subProject.DockerBuildArgs = PromptDockerArgs("build", subProject.DockerBuildArgs);
                    ProjectManager.SaveConfig(_projects);
                    AnsiConsole.MarkupLine(
                        $"[green]Docker build args updated for '{Markup.Escape(subProjectName)}'.[/]");
                    break;

                case "Set Docker Run Args":
                    subProject.DockerRunArgs = PromptDockerArgs("run", subProject.DockerRunArgs);
                    ProjectManager.SaveConfig(_projects);
                    AnsiConsole.MarkupLine($"[green]Docker run args updated for '{Markup.Escape(subProjectName)}'.[/]");
                    break;

                case "[chartreuse3_1]Back to Subprojects[/]":
                    return;
            }

            await Task.Delay(1500);
        }
    }

    private static List<string> PromptDockerArgs(string type, List<string>? currentArgs)
    {
        var args = currentArgs != null ? new List<string>(currentArgs) : new List<string>();
        AnsiConsole.MarkupLine($"[yellow]Current {type} args: {(args.Count > 0 ? string.Join(" ", args) : "None")}[/]");

        bool adding = true;
        while (adding)
        {
            var arg = AnsiConsole.Ask<string>(
                $"Enter a {type} arg (e.g., '-p 8080:80' or '--build-arg ENV=prod', or 'done' to finish, 'clear' to reset):");
            if (arg.ToLower() == "done")
            {
                adding = false;
            }
            else if (arg.ToLower() == "clear")
            {
                args.Clear();
                AnsiConsole.MarkupLine($"[green]{type} args cleared.[/]");
            }
            else if (!string.IsNullOrWhiteSpace(arg))
            {
                args.Add(arg);
                AnsiConsole.MarkupLine($"[green]Added: {Markup.Escape(arg)}[/]");
            }
        }

        return args;
    }

    // Utilidad
    private static void PauseForUserInput(string context = "")
    {
        AnsiConsole.MarkupLine(context == "Remove Project"
            ? ":hand_with_fingers_splayed: Press any key to continue..."
            : "Press any key to continue...");
        Console.ReadKey(true);
    }
}