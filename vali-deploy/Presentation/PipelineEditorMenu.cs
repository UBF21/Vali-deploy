using Spectre.Console;
using vali_deploy.Application;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;
using vali_deploy.Models;

namespace vali_deploy.Presentation;

public static class PipelineEditorMenu
{
    public static async Task StartAsync(IProjectRepository repository, string projectName, SubProject subProject)
    {
        var config = repository.Load();

        if (config.Environments.Count == 0)
        {
            AnsiConsole.MarkupLine("[red]No hay DeployEnvironments creados todavía. Andá a 'Manage Environments' primero.[/]");
            return;
        }

        var environmentName = AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title("Elegí el entorno:").AddChoices(config.Environments.Select(e => e.Name)));
        var environment = config.Environments.First(e => e.Name == environmentName);

        // subProject es el parámetro provisto por el caller; config.Projects[...] viene de un repository.Load()
        // recién hecho, así que es un grafo de objetos DISTINTO. Resolvemos la instancia que realmente
        // pertenece al grafo de config y operamos siempre sobre ella, para que cualquier mutación posterior
        // (alta de template o edición de steps) sea visible cuando se llame a repository.Save(config).
        var configSubProject = config.Projects[projectName].SubProjects.First(s => s.Name == subProject.Name);

        if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName))
        {
            var template = AnsiConsole.Prompt(
                new SelectionPrompt<string>().Title("Plantilla inicial:").AddChoices("Docker Compose", "Publish/Zip"));

            var factory = new PipelineTemplateFactory();
            configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment)
                : factory.CreatePublishZipTemplate(projectName, configSubProject.Name);

            repository.Save(config);
        }

        await EditStepsAsync(repository, config, configSubProject, environmentName);
    }

    private static async Task EditStepsAsync(IProjectRepository repository, Domain.DeployConfig config, SubProject subProject, string environmentName)
    {
        while (true)
        {
            var steps = subProject.PipelinesByEnvironment[environmentName];
            AnsiConsole.Clear();
            var table = new Table().AddColumns("#", "Step");
            foreach (var row in steps.Select((s, i) => new[] { (i + 1).ToString(), s.Name }))
            {
                table.AddRow(row.Select(c => (Spectre.Console.Rendering.IRenderable)new Markup(c)).ToArray());
            }
            AnsiConsole.Write(table);

            var action = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Pipeline de {subProject.Name} en {environmentName}:")
                    .AddChoices("Insert RawCommand", "Remove Step", "Back"));

            switch (action)
            {
                case "Insert RawCommand":
                    var command = AnsiConsole.Ask<string>("Comando a insertar:");
                    steps.Add(new DeployStep { Type = StepType.RawCommand, Name = command, Args = { ["Command"] = command } });
                    repository.Save(config);
                    break;
                case "Remove Step":
                    var toRemove = AnsiConsole.Prompt(
                        new SelectionPrompt<DeployStep>().Title("Quitar cuál paso?").UseConverter(s => s.Name).AddChoices(steps));
                    steps.Remove(toRemove);
                    repository.Save(config);
                    break;
                case "Back":
                    return;
            }

            await Task.CompletedTask;
        }
    }
}
