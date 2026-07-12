# Cancelar registro de Entorno / creación de Pipeline — Design Spec

**Fecha:** 2026-07-11
**Contexto:** bug reportado por el usuario mientras intentaba configurar un entorno para verificar manualmente el Ciclo 5 (barra de progreso del pipeline). Independiente de ese ciclo, es un fix de UX/seguridad de datos.

## Problema

Dos flujos de wizard en `Presentation/` no tienen forma de cancelar a mitad de camino:

1. **`EnvironmentMenu.AddEnvironment`** (`vali-deploy/Presentation/EnvironmentMenu.cs:36-64`): hasta 8 prompts encadenados (nombre, rama, host, puerto, usuario, SO, ruta de clave, passphrase, remote deploy path) sin ninguna opción de "Cancelar". La única salida es Ctrl+C (mata el proceso completo). No hay `Save()` intermedio — el único `repository.Save(config)` está al final (línea 63) — así que no hay corrupción de datos, pero tampoco forma de decir "me equivoqué, no guardes esto" sin cerrar la app.
2. **`PipelineEditorMenu.StartAsync`** (`vali-deploy/Presentation/PipelineEditorMenu.cs:10-44`): la selección de entorno (línea 20-21) y de plantilla (línea 32-33) no tienen opción de cancelar, y **peor**: `repository.Save(config)` (línea 40) se ejecuta apenas se elige la plantilla, antes de que el usuario defina un solo step. Si el usuario toca la plantilla equivocada, ya quedó persistida en `deploy_config.json`.

## Alcance

Agregar cancelación a ambos flujos, siguiendo la convención ya establecida en el repo (`MenuManager.AddProjectAsync` — sentinel `'done'` en `TextPrompt` sin persistir hasta el final; `MenuManager.RemoveProject` — choice `"[seagreen1]Cancel[/]"` appendeado a un `SelectionPrompt`/`MultiSelectionPrompt`). No se introduce ningún mecanismo nuevo de cancelación — se reusa lo que ya existe en el codebase.

Fuera de alcance: `EditStepsAsync`/`EditStepArgs` en `PipelineEditorMenu.cs` — ya tienen salida limpia ("Back"/"Volver") y cada acción (Insert/Edit/Remove step) es unitaria y reversible por el usuario desde el mismo menú (borrar el step que agregó por error), no wizard largo sin retorno. No se toca ese código.

## Diseño

### 1. `EnvironmentMenu.AddEnvironment`

No hace falta cancelar cada prompt individual (no hay persistencia intermedia que evitar). Se agrega un **resumen + confirmación final** antes del único `Save()`:

```csharp
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
        environment.RemoteDeployPath = AnsiConsole.Confirm("¿El path de deploy remoto no sigue la convención /opt/{proyecto}-{subproyecto}?")
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
    table.AddRow("Nombre", environment.Name);
    table.AddRow("Servidor remoto", environment.Server is null ? "No" : "Sí");
    if (environment.Server is not null)
    {
        table.AddRow("Rama por defecto", environment.DefaultBranch ?? "");
        table.AddRow("Host", environment.Server.Host);
        table.AddRow("Puerto", environment.Server.Port.ToString());
        table.AddRow("Usuario SSH", environment.Server.User);
        table.AddRow("SO remoto", environment.Server.Os.ToString());
        table.AddRow("Ruta clave privada", environment.Server.PrivateKeyPath);
        table.AddRow("Passphrase env var", environment.Server.PassphraseEnvVar ?? "[grey](sin passphrase)[/]");
        table.AddRow("Remote deploy path", environment.RemoteDeployPath ?? "[grey](convención por defecto)[/]");
    }
    AnsiConsole.Write(table);

    return AnsiConsole.Confirm("¿Guardar este entorno?", true);
}
```

`ConfirmEnvironmentSummary` no accede a `Server.Port`/`Os` si `Server` es `null` — coherente con que solo se construye cuando `hasRemoteServer == true`.

### 2. `PipelineEditorMenu.StartAsync`

Dos cambios:

**(a) Cancelar en selección de entorno** — appendear `"[seagreen1]Cancelar[/]"` al `SelectionPrompt` (misma convención de `MenuManager.RemoveProject`):

```csharp
var environmentName = AnsiConsole.Prompt(
    new SelectionPrompt<string>().Title("Elegí el entorno:")
        .AddChoices(config.Environments.Select(e => e.Name).Append("[seagreen1]Cancelar[/]")));

if (environmentName == "[seagreen1]Cancelar[/]")
{
    return;
}
```

**(b) Diferir el `Save()` de la creación por plantilla hasta confirmar** — appendear `"Cancelar"` al `SelectionPrompt` de plantilla, y agregar un `Confirm` antes de guardar:

```csharp
if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName))
{
    var template = AnsiConsole.Prompt(
        new SelectionPrompt<string>().Title("Plantilla inicial:").AddChoices("Docker Compose", "Publish/Zip", "Cancelar"));

    if (template == "Cancelar")
    {
        return;
    }

    var confirmed = AnsiConsole.Confirm($"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con la plantilla '{template}'?", true);
    if (!confirmed)
    {
        AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
        return;
    }

    var factory = new PipelineTemplateFactory();
    configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
        ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment, configSubProject.DockerRegistry)
        : factory.CreatePublishZipTemplate(projectName, configSubProject.Name, configSubProject.OmitFiles);

    repository.Save(config);
}

await EditStepsAsync(repository, config, configSubProject, environmentName);
```

Nota: con el `return` temprano en (a) y (b), si el usuario cancela, `StartAsync` termina sin llegar a `EditStepsAsync` — vuelve al menú que lo invocó (`MenuManager`), consistente con el resto del repo (cancelar = volver al nivel anterior, no quedarse en un estado intermedio).

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| Usuario cancela en la confirmación final de `AddEnvironment` | Nada se agrega a `config.Environments`, `repository.Save` no se llama. Vuelve al loop de `StartAsync` (menú "Manage Environments"). |
| Usuario elige "Cancelar" en selección de entorno de `PipelineEditorMenu` | `StartAsync` retorna de inmediato, sin tocar `config`. |
| Usuario elige "Cancelar" en selección de plantilla, o responde "No" a la confirmación | No se agrega entrada a `PipelinesByEnvironment`, `repository.Save` no se llama para ese cambio. |
| Entorno ya tiene pipeline configurado (`ContainsKey(environmentName) == true`) | Sin cambios — ese branch no pasa por la plantilla, va directo a `EditStepsAsync` (comportamiento preexistente, no tocado). |

## Testing

Sin tests — mismo criterio que el resto de `Presentation/`: dependen de `AnsiConsole.Prompt`/`Confirm`/`Ask` (I/O de consola de Spectre.Console), no mockeables sin acoplarse a la librería, y no hay tests existentes para ninguno de los dos archivos. Verificación manual por el usuario.

## Decisiones registradas

- Se reusan los dos patrones de cancelación ya existentes en el repo (sentinel de texto en `AddProjectAsync`, choice "Cancel"/"Volver" appendeado en `RemoveProject`/`EditStepArgs`) — no se inventa un tercer mecanismo.
- En `AddEnvironment` no se cancela prompt-por-prompt (no hace falta, no hay persistencia intermedia) — se resuelve con un resumen + confirmación final, que es lo mínimo necesario para que "me equivoqué en el medio" tenga una salida sin matar el proceso.
- En `PipelineEditorMenu`, el fix crítico es diferir el `Save()` de la plantilla hasta la confirmación explícita — antes del fix, elegir la plantilla y persistirla eran la misma acción atómica sin posibilidad de arrepentirse.
- Cancelar siempre vuelve al nivel de menú anterior (no dejar al usuario "colgado" en un estado intermedio del wizard).
