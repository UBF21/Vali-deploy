using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;

namespace vali_deploy.Managers;

/// <summary>
/// Manages the main menu and user interactions for the Vali-Deploy application.
/// Provides functionality to add, remove, and manage projects, subprojects, Docker configurations, and publish arguments.
/// </summary>
public static class MenuManager
{
    private static Dictionary<string, Project> _projects = new();
    private static BarChart _barChart = new();

    /// <summary>
    /// Starts the main menu loop of the application, allowing users to interact with project management features.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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
                    break;

                case "Show Projects":
                    await ShowProjectsAsync();
                    break;

                case "Configure Publish File Omissions":
                    await ManageProjectFilesToOmitFromPublishAsync();
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
                case "Manage Publish Arguments":
                    await ManagePublishArgumentsAsync();
                    UpdateProjectsAndChart();
                    break;
                case "Manage Environments":
                    await Presentation.EnvironmentMenu.StartAsync(CompositionRoot.CreateProjectRepository());
                    break;
                case "[chartreuse3_1]Exit[/]":
                    running = false;
                    AnsiConsole.MarkupLine("[yellow] Leaving...[/]");
                    Environment.Exit(0);
                    break;
            }
        }
    }

    /// <summary>
    /// Displays the main menu header, including the application title, version, and project statistics bar chart.
    /// </summary>
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

    /// <summary>
    /// Prompts the user to select an option from the main menu.
    /// </summary>
    /// <returns>The selected menu option as a string.</returns>
    private static string GetMainMenuOption()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Project", "Remove Project", "Show Projects", "Configure Publish File Omissions",
                    "Remove Subprojects", "Manage Docker Projects", "Manage Publish Arguments", "Manage Environments",
                    "[chartreuse3_1]Exit[/]")
        );
    }

    /// <summary>
    /// Updates the projects dictionary and bar chart with the latest data from the configuration.
    /// </summary>
    private static void UpdateProjectsAndChart()
    {
        _projects = ProjectManager.LoadOrCreateConfig();
        _barChart = ChartManager.CreateBarChart(_projects);
    }

    /// <summary>
    /// Prompts the user to add a new project, including its path and subprojects, and saves it to the configuration.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Prompts the user to enter a project name.
    /// </summary>
    /// <returns>The project name as a string, or null if the user cancels.</returns>
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

    /// <summary>
    /// Prompts the user to enter a project path and validates its existence.
    /// </summary>
    /// <returns>The project path as a string, or null if the user cancels.</returns>
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

    /// <summary>
    /// Prompts the user to add subprojects to a project, including their paths and optional Dockerfile paths.
    /// </summary>
    /// <param name="projectPath">The path of the parent project.</param>
    /// <returns>A task that resolves to a list of subprojects, or null if the user cancels without adding any subprojects.</returns>
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

    /// <summary>
    /// Prompts the user to enter a subproject path and validates its existence.
    /// </summary>
    /// <param name="projectPath">The path of the parent project.</param>
    /// <returns>The subproject path as a string, or null if the user cancels.</returns>
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

    /// <summary>
    /// Allows the user to remove one or more projects from the configuration.
    /// </summary>
    private static void RemoveProject()
    {
        if (_projects.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
            PauseForUserInput();
            return;
        }

        var projectsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select projects to remove (use spacebar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );

        if (projectsToRemove.Contains("[chartreuse3_1]Back to Main Menu[/]") || projectsToRemove.Count == 0)
        {
            return;
        }

        foreach (var projectName in projectsToRemove)
        {
            ProjectManager.RemoveProject(projectName);
            AnsiConsole.MarkupLine($"[green]Project '{Markup.Escape(projectName)}' removed successfully.[/]");
        }
    }

    /// <summary>
    /// Allows the user to remove subprojects from a selected project.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Prompts the user to select a project for subproject removal.
    /// </summary>
    /// <returns>The selected project name, or "[chartreuse3_1]Back to Main Menu[/]" if the user cancels.</returns>
    private static string PromptProjectSelectionForSubprojectRemoval()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to remove subprojects from")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    /// <summary>
    /// Prompts the user to select multiple subprojects to remove from a project.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A list of subproject names to remove, or null if the user cancels.</returns>
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

    /// <summary>
    /// Displays a list of projects and allows the user to select one to view its subprojects.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Prompts the user to select a project from the list of available projects.
    /// </summary>
    /// <returns>The selected project name, or "[chartreuse3_1]Back to Main Menu[/]" if the user cancels.</returns>
    private static string PromptProjectSelection()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    /// <summary>
    /// Displays the subprojects of a selected project and allows the user to execute commands on a subproject.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A task that resolves to true if a subproject command was executed, false otherwise.</returns>
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

    /// <summary>
    /// Prompts the user to select a subproject from a project.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>The selected subproject name, or "[chartreuse3_1]Back to Projects Menu[/]" if the user cancels.</returns>
    private static string PromptSubProjectSelection(Project project, string projectName)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a subproject for project '{projectName}'")
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects Menu[/]"))
        );
    }

    /// <summary>
    /// Allows the user to manage files to omit from the publish output for a selected project and subproject.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ManageProjectFilesToOmitFromPublishAsync()
    {
        while (true)
        {
            if (_projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                return;
            }

            var projectName = PromptProjectForOmitFilesFromPublish();
            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            await ManageSubProjectFilesFromPublishAsync(_projects[projectName], projectName);
        }
    }

    /// <summary>
    /// Prompts the user to select a project for managing publish file omissions.
    /// </summary>
    /// <returns>The selected project name, or "[chartreuse3_1]Back to Main Menu[/]" if the user cancels.</returns>
    private static string PromptProjectForOmitFilesFromPublish()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to configure publish file omissions")
                .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
        );
    }

    /// <summary>
    /// Manages the subprojects of a selected project for configuring publish file omissions.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ManageSubProjectFilesFromPublishAsync(Project project, string projectName)
    {
        while (true)
        {
            var subProject = await SelectSubProjectAsync(project, projectName);
            if (subProject == null) return;

            await ConfigurePublishFileOmissionsForSubProjectAsync(subProject, projectName);
        }
    }

    /// <summary>
    /// Prompts the user to select a subproject for managing publish file omissions.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A task that resolves to the selected subproject, or null if the user cancels.</returns>
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

    /// <summary>
    /// Configures the files to omit from the publish output for a selected subproject.
    /// </summary>
    /// <param name="subProject">The subproject to configure.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ConfigurePublishFileOmissionsForSubProjectAsync(SubProject subProject, string projectName)
    {
        bool managingFiles = true;
        while (managingFiles)
        {
            AnsiConsole.Clear();
            DisplayOmitFilesFromPublish(subProject, projectName);
            var action = PromptFileManagementAction();

            switch (action)
            {
                case "Add file to omit":
                    await AddFileToOmitFromPublishAsync(subProject);
                    break;

                case "Remove file from omit list":
                    await RemoveFileToOmitFromPublishAsync(subProject);
                    break;

                case "[chartreuse3_1]Back to Subprojects[/]":
                    managingFiles = false;
                    AnsiConsole.Clear();
                    DisplayMainMenu();
                    break;
            }
        }
    }

    /// <summary>
    /// Displays the files to omit from the publish output in a tree structure.
    /// </summary>
    /// <param name="subProject">The subproject containing the omit files.</param>
    /// <param name="projectName">The name of the parent project.</param>
    private static void DisplayOmitFilesFromPublish(SubProject subProject, string projectName)
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

    /// <summary>
    /// Prompts the user to select an action for managing publish file omissions.
    /// </summary>
    /// <returns>The selected action as a string.</returns>
    private static string PromptFileManagementAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add file to omit", "Remove file from omit list", "[chartreuse3_1]Back to Subprojects[/]")
        );
    }

    /// <summary>
    /// Allows the user to add files to the list of files to omit from the publish output.
    /// </summary>
    /// <param name="subProject">The subproject to add omit files to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task AddFileToOmitFromPublishAsync(SubProject subProject)
    {
        bool addingFiles = true;
        bool firstFileAdded = false;

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
        while (addingFiles)
        {
            if (firstFileAdded)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
            }

            var fileToAdd = AnsiConsole.Ask<string>("Enter the file name to omit (e.g., 'example.txt'): ");
            if (fileToAdd.ToLower() == "done")
            {
                addingFiles = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(fileToAdd))
            {
                AnsiConsole.MarkupLine("[red]File name cannot be empty.[/]");
            }
            else if (subProject.OmitFiles.Contains(fileToAdd))
            {
                AnsiConsole.MarkupLine("[red]This file is already in the omit list.[/]");
            }
            else
            {
                subProject.OmitFiles.Add(fileToAdd);
                ProjectManager.SaveConfig(_projects);
                AnsiConsole.MarkupLine($"[green]File '{Markup.Escape(fileToAdd)}' added to omit list.[/]");
                firstFileAdded = true; // Marca que ya se agregó el primer archivo
                await Task.Delay(1000); // Pausa de 1.5 segundos para mostrar el mensaje
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Allows the user to remove files from the list of files to omit from the publish output.
    /// </summary>
    /// <param name="subProject">The subproject to remove omit files from.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static Task RemoveFileToOmitFromPublishAsync(SubProject subProject)
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

    /// <summary>
    /// Executes a command on a selected subproject, such as generating a Microsoft publish or Docker-related actions.
    /// </summary>
    /// <param name="project">The parent project of the subproject.</param>
    /// <param name="subProject">The subproject to execute the command on.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ExecuteCommandSubProject(Project project, SubProject? subProject, string projectName)
    {
        if (subProject == null) return;

        string subProjectPathFull = Path.Combine(project.Path, subProject.Path);
        string imageTag = $"{projectName.ToLower()}-{subProject.Name.ToLower()}:latest";

        var choices = new List<string> { "Generate Microsoft publish", "Edit Pipeline", "[chartreuse3_1]Back to Subprojects[/]" };
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
                AnsiConsole.MarkupLine(
                    $"[green]Running normal publish for subproject '{Markup.Escape(subProject.Name)}' in project '{Markup.Escape(projectName)}'...[/]");
                await CommandExecutor.RunCommandsAsync(projectName, subProject.Name, subProjectPathFull, subProject);
                PauseForUserInput();
                break;

            case "Edit Pipeline":
                await Presentation.PipelineEditorMenu.StartAsync(CompositionRoot.CreateProjectRepository(), projectName, subProject);
                break;

            case "Docker Build":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    string dockerfileFullPath = Path.Combine(subProjectPathFull, subProject.DockerfilePath);
                    AnsiConsole.MarkupLine(
                        $"[green]Building Docker image for subproject '{Markup.Escape(subProject.Name)}'...[/]");
                    string buildArgs = subProject.DockerBuildArgs is { Count: > 0 }
                        ? " " + string.Join(" ", subProject.DockerBuildArgs)
                        : "";
                    string buildCommand =
                        $"docker build -f \"{dockerfileFullPath}\" -t {imageTag}{buildArgs} \"{subProjectPathFull}\"";
                    int buildResult = await CommandExecutor.ExecuteDockerCommandAsync(buildCommand);
                    AnsiConsole.MarkupLine(buildResult == 0
                        ? $"[green]Docker image '{imageTag}' built successfully![/]"
                        : "[red]Docker build failed. Check the output above.[/]");
                    PauseForUserInput();
                }

                break;

            case "Docker Run":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    AnsiConsole.MarkupLine(
                        $"[green]Running Docker container for subproject '{Markup.Escape(subProject.Name)}'...[/]");
                    string runArgs = subProject.DockerRunArgs is { Count: > 0 }
                        ? " " + string.Join(" ", subProject.DockerRunArgs)
                        : "";
                    string runCommand = $"docker run -it --rm{runArgs} {imageTag}";
                    int runResult = await CommandExecutor.ExecuteDockerCommandAsync(runCommand);
                    if (runResult == 0)
                        AnsiConsole.MarkupLine($"[green]Container ran successfully![/]");
                    else
                        AnsiConsole.MarkupLine("[red]Docker run failed. Check the output above.[/]");
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
                    if (pushResult == 0)
                        AnsiConsole.MarkupLine($"[green]Image pushed to Docker Hub successfully![/]");
                    else
                        AnsiConsole.MarkupLine("[red]Push to Docker Hub failed. Check credentials or network.[/]");
                    PauseForUserInput();
                }

                break;

            case "[chartreuse3_1]Back to Subprojects[/]":
                return;
        }
    }

    /// <summary>
    /// Manages Docker projects by allowing the user to configure Docker arguments for subprojects with Dockerfiles.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
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

    /// <summary>
    /// Manages Docker subprojects for a selected project by allowing the user to configure Docker arguments.
    /// </summary>
    /// <param name="project">The project containing the Docker subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ManageDockerSubProjectsAsync(Project project, string projectName)
    {
        while (true)
        {
            var dockerSubProjects = project.SubProjects
                .Where(sp => !string.IsNullOrEmpty(sp.DockerfilePath))
                .ToList();

            if (dockerSubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]:warning: No subprojects with Dockerfiles in '{Markup.Escape(projectName)}'.[/]");
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
            await ManageDockerArgsAsync(subProject, projectName);
        }
    }

    /// <summary>
    /// Manages Docker build and run arguments for a selected subproject.
    /// </summary>
    /// <param name="subProject">The subproject to manage Docker arguments for.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private static async Task ManageDockerArgsAsync(SubProject subProject, string projectName)
    {
        bool managingArgs = true;
        while (managingArgs)
        {
            AnsiConsole.Clear();
            DisplayDockerArgs(subProject, projectName);
            var action = PromptDockerArgsAction();

            switch (action)
            {
                case "Add Docker Arg":
                    await AddDockerArgAsync(subProject);
                    break;

                case "Remove Docker Args":
                    await RemoveDockerArgsAsync(subProject);
                    break;

                case "[chartreuse3_1]Back to Subprojects[/]":
                    managingArgs = false;
                    AnsiConsole.Clear();
                    DisplayMainMenu();
                    break;
            }
        }
    }

    /// <summary>
    /// Displays the Docker build and run arguments for a subproject in a tree structure.
    /// </summary>
    /// <param name="subProject">The subproject containing the Docker arguments to display.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <remarks>
    /// This method creates a visual tree representation of the subproject's Docker arguments, showing both build and run arguments.
    /// If no arguments are specified, a placeholder message is displayed. The tree is rendered within a panel after displaying the main menu.
    /// </remarks>
    private static void DisplayDockerArgs(SubProject subProject, string projectName)
    {
        var tree = new Tree($"[yellow]{Markup.Escape(projectName)}[/]");
        var subProjectNode = tree.AddNode($"[green]{Markup.Escape(subProject.Name)}[/]");
        var buildArgsNode = subProjectNode.AddNode("[blue]Build Args[/]");
        var runArgsNode = subProjectNode.AddNode("[blue]Run Args[/]");

        var padder = new Padder(tree).PadLeft(2);
        var panel = new Panel(padder).Header("Docker Arguments");

        if (subProject.DockerBuildArgs == null || subProject.DockerBuildArgs.Count == 0)
        {
            buildArgsNode.AddNode("[grey]No build args specified[/]");
        }
        else
        {
            foreach (var arg in subProject.DockerBuildArgs)
            {
                buildArgsNode.AddNode($"[white]{Markup.Escape(arg)}[/]");
            }
        }

        if (subProject.DockerRunArgs == null || subProject.DockerRunArgs.Count == 0)
        {
            runArgsNode.AddNode("[grey]No run args specified[/]");
        }
        else
        {
            foreach (var arg in subProject.DockerRunArgs)
            {
                runArgsNode.AddNode($"[white]{Markup.Escape(arg)}[/]");
            }
        }

        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing Docker arguments.
    /// </summary>
    /// <returns>The selected action as a string, such as "Add Docker Arg", "Remove Docker Args", or "[chartreuse3_1]Back to Subprojects[/]".</returns>
    private static string PromptDockerArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Docker Arg", "Remove Docker Args", "[chartreuse3_1]Back to Subprojects[/]")
        );
    }

    /// <summary>
    /// Allows the user to add Docker build or run arguments to a subproject.
    /// </summary>
    /// <param name="subProject">The subproject to add Docker arguments to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method prompts the user to select the type of argument (Build Arg or Run Arg) and then allows them to add arguments one by one.
    /// The user can type 'done' to finish adding arguments of a specific type. Duplicate arguments are not allowed, and the configuration is saved after each addition.
    /// A brief delay is added after each argument addition to display the confirmation message.
    /// </remarks>
    private static async Task AddDockerArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        while (addingArgs)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[yellow]Adding a Docker argument[/]");
            AnsiConsole.WriteLine();

            var type = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select argument type:")
                    .AddChoices("Build Arg", "Run Arg", "[chartreuse3_1]Back[/]")
            );

            if (type == "[chartreuse3_1]Back[/]")
            {
                addingArgs = false;
                continue;
            }

            AnsiConsole.MarkupLine($"[yellow]Adding {type.ToLower()}s (type 'done' to finish)[/]");
            bool addingTypeArgs = true;
            bool firstArgAdded = false; // Bandera para saber si ya se agregó el primer argumento
            while (addingTypeArgs)
            {
                if (firstArgAdded)
                {
                    AnsiConsole.Clear(); // Limpia la pantalla después del primer argumento
                    AnsiConsole.MarkupLine($"[yellow]Adding {type.ToLower()}s (type 'done' to finish)[/]");
                }

                var arg = AnsiConsole.Ask<string>(
                    $"Enter {type.ToLower()} (e.g., '-p 8080:80' or '--build-arg ENV=prod'): ");
                if (arg.ToLower() == "done")
                {
                    addingTypeArgs = false;
                    continue; // Vuelve a la selección de tipo
                }

                if (string.IsNullOrWhiteSpace(arg))
                {
                    AnsiConsole.MarkupLine("[red]Argument cannot be empty.[/]");
                }
                else
                {
                    if (type == "Build Arg")
                    {
                        subProject.DockerBuildArgs ??= new List<string>();
                        if (subProject.DockerBuildArgs.Contains(arg))
                        {
                            AnsiConsole.MarkupLine("[red]This build arg is already in the list.[/]");
                        }
                        else
                        {
                            subProject.DockerBuildArgs.Add(arg);
                            AnsiConsole.MarkupLine($"[green]Build arg '{Markup.Escape(arg)}' added.[/]");
                            ProjectManager.SaveConfig(_projects);
                            firstArgAdded = true; // Marca que ya se agregó el primer argumento
                            await Task.Delay(1000); // Pausa de 1.5 segundos para mostrar el mensaje
                        }
                    }
                    else if (type == "Run Arg")
                    {
                        subProject.DockerRunArgs ??= new List<string>();
                        if (subProject.DockerRunArgs.Contains(arg))
                        {
                            AnsiConsole.MarkupLine("[red]This run arg is already in the list.[/]");
                        }
                        else
                        {
                            subProject.DockerRunArgs.Add(arg);
                            AnsiConsole.MarkupLine($"[green]Run arg '{Markup.Escape(arg)}' added.[/]");
                            ProjectManager.SaveConfig(_projects);
                            firstArgAdded = true; // Marca que ya se agregó el primer argumento
                            await Task.Delay(1500); // Pausa de 1.5 segundos para mostrar el mensaje
                        }
                    }
                }
            }
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// Allows the user to remove Docker build or run arguments from a subproject using multiple selection.
    /// </summary>
    /// <param name="subProject">The subproject to remove Docker arguments from.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method prompts the user to select the type of arguments to remove (Build Args or Run Args) and then allows them to select multiple arguments to remove.
    /// If no arguments of the selected type exist, a warning is displayed. The configuration is saved after removing the selected arguments.
    /// </remarks>
    private static Task RemoveDockerArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        var type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select argument type to remove:")
                .AddChoices("Build Args", "Run Args", "[chartreuse3_1]Cancel[/]")
        );

        if (type == "[chartreuse3_1]Cancel[/]") return Task.CompletedTask;

        if (type == "Build Args")
        {
            if (subProject.DockerBuildArgs == null || subProject.DockerBuildArgs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No build args to remove.[/]");
                PauseForUserInput();
                return Task.CompletedTask;
            }

            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select build args to remove (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.DockerBuildArgs.Append("[chartreuse3_1]Cancel[/]"))
            );

            if (!argsToRemove.Contains("[chartreuse3_1]Cancel[/]") && argsToRemove.Count > 0)
            {
                foreach (var arg in argsToRemove)
                {
                    subProject.DockerBuildArgs.Remove(arg);
                    AnsiConsole.MarkupLine($"[green]Build arg '{Markup.Escape(arg)}' removed.[/]");
                }

                ProjectManager.SaveConfig(_projects);
            }
        }
        else if (type == "Run Args")
        {
            if (subProject.DockerRunArgs == null || subProject.DockerRunArgs.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]No run args to remove.[/]");
                PauseForUserInput();
                return Task.CompletedTask;
            }

            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select run args to remove (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.DockerRunArgs.Append("[chartreuse3_1]Cancel[/]"))
            );

            if (!argsToRemove.Contains("[chartreuse3_1]Cancel[/]") && argsToRemove.Count > 0)
            {
                foreach (var arg in argsToRemove)
                {
                    subProject.DockerRunArgs.Remove(arg);
                    AnsiConsole.MarkupLine($"[green]Run arg '{Markup.Escape(arg)}' removed.[/]");
                }

                ProjectManager.SaveConfig(_projects);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Manages publish arguments for projects by allowing the user to select a project and configure its subprojects' publish arguments.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method displays a list of available projects and allows the user to select one to manage its subprojects' publish arguments.
    /// If no projects are found, a warning is displayed, and the method exits after a brief delay.
    /// </remarks>
    private static async Task ManagePublishArgumentsAsync()
    {
        while (true)
        {
            if (_projects.Count == 0)
            {
                AnsiConsole.MarkupLine("[yellow]:warning: No projects found.[/]");
                await Task.Delay(2000);
                return;
            }

            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a project to manage publish arguments")
                    .AddChoices(_projects.Keys.Append("[chartreuse3_1]Back to Main Menu[/]"))
            );

            if (projectName == "[chartreuse3_1]Back to Main Menu[/]") return;

            await ManagePublishSubProjectsAsync(_projects[projectName], projectName);
        }
    }

    /// <summary>
    /// Manages the subprojects of a selected project for configuring publish arguments.
    /// </summary>
    /// <param name="project">The project containing the subprojects.</param>
    /// <param name="projectName">The name of the project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method displays a list of subprojects for the selected project and allows the user to select one to manage its publish arguments.
    /// If no subprojects are found, a warning is displayed, and the method exits after a brief delay.
    /// </remarks>
    private static async Task ManagePublishSubProjectsAsync(Project project, string projectName)
    {
        while (true)
        {
            if (project.SubProjects.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[yellow]:warning: No subprojects found in '{Markup.Escape(projectName)}'.[/]");
                await Task.Delay(2000);
                return;
            }

            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a subproject in '{projectName}' to manage publish arguments")
                    .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[chartreuse3_1]Back to Projects[/]"))
            );

            if (subProjectName == "[chartreuse3_1]Back to Projects[/]") return;

            var subProject = project.SubProjects.First(sp => sp.Name == subProjectName);
            await ManagePublishArgsAsync(subProject, projectName);
        }
    }

    /// <summary>
    /// Manages the publish arguments for a selected subproject, including adding, removing, and toggling zip output.
    /// </summary>
    /// <param name="subProject">The subproject to manage publish arguments for.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method provides a menu for the user to add or remove publish arguments, or toggle the zip publish output option.
    /// The configuration is saved after each change, and a brief delay is added after toggling the zip option to display the confirmation message.
    /// </remarks>
    private static async Task ManagePublishArgsAsync(SubProject subProject, string projectName)
    {
        bool managingArgs = true;
        while (managingArgs)
        {
            AnsiConsole.Clear();
            DisplayPublishArgs(subProject, projectName);
            var action = PromptPublishArgsAction();

            switch (action)
            {
                case "Add Publish Arg":
                    await AddPublishArgAsync(subProject);
                    break;

                case "Remove Publish Args":
                    await RemovePublishArgsAsync(subProject);
                    break;

                case "Toggle Zip Publish Output":
                    subProject.ZipPublishOutput = !subProject.ZipPublishOutput;
                    ProjectManager.SaveConfig(_projects);
                    AnsiConsole.MarkupLine(
                        $"[green]Zip Publish Output {(subProject.ZipPublishOutput ? "enabled" : "disabled")}.[/]");
                    await Task.Delay(1500); // Pausa breve para mostrar el mensaje
                    break;

                case "[chartreuse3_1]Back to Subprojects[/]":
                    managingArgs = false;
                    AnsiConsole.Clear();
                    DisplayMainMenu();
                    break;
            }
        }
    }

    /// <summary>
    /// Displays the publish arguments for a subproject in a tree structure.
    /// </summary>
    /// <param name="subProject">The subproject containing the publish arguments to display.</param>
    /// <param name="projectName">The name of the parent project.</param>
    /// <remarks>
    /// This method creates a visual tree representation of the subproject's publish arguments.
    /// If no arguments are specified, a placeholder message is displayed. The tree is rendered within a panel after displaying the main menu.
    /// </remarks>
    private static void DisplayPublishArgs(SubProject subProject, string projectName)
    {
        var tree = new Tree($"[yellow]{Markup.Escape(projectName)}[/]");
        var subProjectNode = tree.AddNode($"[green]{Markup.Escape(subProject.Name)}[/]");
        var publishArgsNode = subProjectNode.AddNode("[blue]Publish Args[/]");
        // var zipNode = subProjectNode.AddNode($"[blue]Zip Publish Output: {(subProject.ZipPublishOutput ? "[green]Enabled[/]" : "[red]Disabled[/]")}[/]");

        var padder = new Padder(tree).PadLeft(2);
        var panel = new Panel(padder).Header("Publish Arguments");

        if (subProject.PublishArgs == null || subProject.PublishArgs.Count == 0)
        {
            publishArgsNode.AddNode("[grey]No publish args specified[/]");
        }
        else
        {
            foreach (var arg in subProject.PublishArgs)
            {
                publishArgsNode.AddNode($"[white]{Markup.Escape(arg)}[/]");
            }
        }

        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing publish arguments.
    /// </summary>
    /// <returns>The selected action as a string, such as "Add Publish Arg", "Remove Publish Args", "Toggle Zip Publish Output", or "[chartreuse3_1]Back to Subprojects[/]".</returns>
    private static string PromptPublishArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Publish Arg", "Remove Publish Args", "Toggle Zip Publish Output",
                    "[chartreuse3_1]Back to Subprojects[/]")
        );
    }

    /// <summary>
    /// Allows the user to add publish arguments to a subproject.
    /// </summary>
    /// <param name="subProject">The subproject to add publish arguments to.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method prompts the user to enter publish arguments one by one, allowing them to type 'done' to finish.
    /// Duplicate arguments are not allowed, and the configuration is saved after each addition.
    /// A brief delay is added after each argument addition to display the confirmation message.
    /// </remarks>
    private static async Task AddPublishArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        bool firstArgAdded = false;

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
        while (addingArgs)
        {
            if (firstArgAdded)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
            }

            var arg = AnsiConsole.Ask<string>(
                "Enter publish arg (e.g., '--no-restore' or '-p:EnvironmentName=Production'): ");
            if (arg.ToLower() == "done")
            {
                addingArgs = false;
                continue;
            }

            if (string.IsNullOrWhiteSpace(arg))
            {
                AnsiConsole.MarkupLine("[red]Argument cannot be empty.[/]");
            }
            else if (subProject.PublishArgs != null && subProject.PublishArgs.Contains(arg))
            {
                AnsiConsole.MarkupLine("[red]This publish arg is already in the list.[/]");
            }
            else
            {
                subProject.PublishArgs ??= new List<string>();
                subProject.PublishArgs.Add(arg);
                ProjectManager.SaveConfig(_projects);
                AnsiConsole.MarkupLine($"[green]Publish arg '{Markup.Escape(arg)}' added.[/]");
                firstArgAdded = true;
                await Task.Delay(1500); // Pausa de 1.5 segundos
            }
        }
    }

    /// <summary>
    /// Allows the user to remove publish arguments from a subproject using multiple selection.
    /// </summary>
    /// <param name="subProject">The subproject to remove publish arguments from.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    /// This method displays a list of existing publish arguments and allows the user to select multiple arguments to remove.
    /// If no arguments exist, a warning is displayed. The configuration is saved after removing the selected arguments, and a brief delay is added to display the confirmation messages.
    /// </remarks>
    private static async Task RemovePublishArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        if (subProject.PublishArgs == null || subProject.PublishArgs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No publish args to remove.[/]");
            await Task.Delay(1500);
            return;
        }

        var argsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select publish args to remove (use space-bar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(subProject.PublishArgs.Append("[chartreuse3_1]Cancel[/]"))
        );

        if (!argsToRemove.Contains("[chartreuse3_1]Cancel[/]") && argsToRemove.Count > 0)
        {
            foreach (var arg in argsToRemove)
            {
                subProject.PublishArgs.Remove(arg);
                AnsiConsole.MarkupLine($"[green]Publish arg '{Markup.Escape(arg)}' removed.[/]");
            }

            ProjectManager.SaveConfig(_projects);
        }

        await Task.Delay(1500);
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