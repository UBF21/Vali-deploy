using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Presentation;

public static class EnvironmentMenu
{
    public static async Task StartAsync(IProjectRepository repository)
    {
        while (true)
        {
            var config = repository.Load();
            AnsiConsole.Clear();
            ShellRenderer.DrawHeader(config.Projects, breadcrumb: "Entornos");

            var option = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Manage Environments[/]")
                    .AddChoices(config.Environments.Select(e => e.Name)
                        .Append("[green]Add Environment[/]")
                        .Append("[seagreen1]Back to Main Menu[/]")));

            if (option == "[seagreen1]Back to Main Menu[/]") return;

            if (option == "[green]Add Environment[/]")
            {
                AddEnvironment(repository, config);
                continue;
            }

            await Task.CompletedTask;
        }
    }

    private static void AddEnvironment(IProjectRepository repository, Domain.DeployConfig config)
    {
        var name = AnsiConsole.Ask<string>("Nombre del entorno (ej. QA, PROD):");
        var hasRemoteServer = AnsiConsole.Confirm("¿Este entorno despliega a un servidor remoto por SSH?");

        var environment = new DeployEnvironment { Name = name };

        if (hasRemoteServer)
        {
            environment.DefaultBranch = AnsiConsole.Ask<string>("Rama por defecto (ej. main):");
            environment.Server = new RemoteServer
            {
                Host = AnsiConsole.Ask<string>("Host:"),
                Port = AnsiConsole.Ask("Puerto:", 22),
                User = AnsiConsole.Ask<string>("Usuario SSH:"),
                Os = AnsiConsole.Prompt(new SelectionPrompt<RemoteOs>().Title("Sistema operativo remoto:").AddChoices(RemoteOs.Windows, RemoteOs.Linux)),
                PrivateKeyPath = AnsiConsole.Ask<string>("Ruta a la clave privada SSH:"),
                PassphraseEnvVar = AnsiConsole.Confirm("¿La clave tiene passphrase?")
                    ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con la passphrase:")
                    : null
            };
            environment.RemoteDeployPath = AnsiConsole.Confirm(
                "¿Querés fijar un path de deploy remoto por defecto para este ambiente? (si decís que no, se usa la convención /opt/{proyecto}-{subproyecto}, que igual podés sobreescribir después por cada subproyecto)", false)
                ? AnsiConsole.Ask<string>("Path de deploy remoto (ej. /srv/apps/legacy-name):")
                : null;
        }

        if (!ConfirmEnvironmentSummary(environment))
        {
            AnsiConsole.MarkupLine("[yellow]Cancelado. No se guardó ningún cambio.[/]");
            return;
        }

        config.Environments.Add(environment);
        repository.Save(config);
    }

    private static bool ConfirmEnvironmentSummary(DeployEnvironment environment)
    {
        var table = new Table().AddColumns("Campo", "Valor");
        table.AddRow("Nombre", environment.Name.EscapeMarkup());
        table.AddRow("Servidor remoto", environment.Server is null ? "No" : "Sí");
        if (environment.Server is not null)
        {
            table.AddRow("Rama por defecto", (environment.DefaultBranch ?? "").EscapeMarkup());
            table.AddRow("Host", environment.Server.Host.EscapeMarkup());
            table.AddRow("Puerto", environment.Server.Port.ToString());
            table.AddRow("Usuario SSH", environment.Server.User.EscapeMarkup());
            table.AddRow("SO remoto", environment.Server.Os.ToString());
            table.AddRow("Ruta clave privada", environment.Server.PrivateKeyPath.EscapeMarkup());
            table.AddRow("Passphrase env var", environment.Server.PassphraseEnvVar is null ? "[grey](sin passphrase)[/]" : environment.Server.PassphraseEnvVar.EscapeMarkup());
            table.AddRow("Remote deploy path", environment.RemoteDeployPath is null ? "[grey](convención por defecto)[/]" : environment.RemoteDeployPath.EscapeMarkup());
        }
        AnsiConsole.Write(table);

        return AnsiConsole.Confirm("¿Guardar este entorno?", true);
    }
}
