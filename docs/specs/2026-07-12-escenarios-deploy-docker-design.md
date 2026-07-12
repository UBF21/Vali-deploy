# Escenarios de deploy Docker (build remoto sin registry) + insertar en posición — Design Spec

**Fecha:** 2026-07-12
**Contexto:** el usuario, mientras armaba su primer deploy real (`acity-caf-api-migracion-audiencia`, ambiente DEV de un solo servidor sin Docker registry propio), detectó que la plantilla "Docker Compose" actual asume siempre build local → push a un registry → pull remoto → up remoto. Sin registry configurado, el `docker compose pull` remoto no tiene de dónde traer la imagen — ese escenario simplemente no funciona hoy. También pidió poder insertar comandos custom en una posición específica del pipeline (no solo al final), y confirmó que el menú legacy ("Manage Docker Projects"/"Manage Publish Arguments", 100% local sin SSH) no es su flujo real y no hace falta tocarlo.

## Alcance

1. La elección de plantilla "Docker Compose" se bifurca en una sub-pregunta: **build directo en el servidor remoto** (sin registry, vía `git pull` remoto) o **push a un registry** (flujo actual, build local → push → pull remoto).
2. La rama "push a un registry" pasa a pedir los datos del `DockerRegistry` (Host, Usuario, variable de entorno del token) **dentro del wizard**, en vez de depender del menú legacy que el usuario no usa — hoy esa rama queda funcionalmente incompleta si no se pasa por ahí.
3. Nuevo `StepType.DockerComposeBuild` (+ executor), para correr `docker compose build` en el servidor remoto.
4. `PipelineEditorMenu` → "Insert RawCommand" pregunta antes de cuál step insertar (o "Al final"), en vez de agregar siempre al final.

Fuera de alcance (confirmado con el usuario): plantilla Publish/Zip (ya cubre "solo descomprimir sin Docker/reinicio" dejando los steps SSH vacíos o borrándolos vía "Remove Step") y el menú legacy Docker/Publish 100% local — no se tocan.

## Diseño

### 1. `Domain/StepType.cs` — nuevo tipo

```csharp
public enum StepType
{
    GitCheckout,
    LocalCommand,
    DockerBuild,
    DockerRun,
    DockerPush,
    DockerSave,
    DockerLoad,
    DockerImagePrune,
    DockerComposePull,
    DockerComposeBuild,
    DockerComposeUp,
    DockerComposeDown,
    ZipPublishOutput,
    CopyToRemote,
    SshCommand,
    RawCommand
}
```

### 2. `Application/Executors/DockerComposeBuildExecutor.cs` (nuevo) — mismo patrón que `DockerComposeUpExecutor`

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerComposeBuildExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerComposeBuildExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerComposeBuild;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            return StepResultFactory.NoServer(step, context, stopwatch.Elapsed);
        }

        if (!step.Args.TryGetValue("ComposeFilePath", out var composeFilePath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"ComposeFilePath\"].");
        }

        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, $"docker compose -f \"{composeFilePath}\" build");
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, stopwatch.Elapsed);
    }
}
```

Registrar en `CompositionRoot.cs:39-42` (`BuildExecutors`), junto al resto de executores basados en `ISshClientFactory`:

```csharp
new DockerComposePullExecutor(sshClientFactory),
new DockerComposeBuildExecutor(sshClientFactory),
new DockerComposeUpExecutor(sshClientFactory),
new DockerComposeDownExecutor(sshClientFactory)
```

### 3. `Application/PipelineTemplateFactory.cs` — nuevo método `CreateDockerComposeRemoteBuildTemplate`

`CreateDockerComposeTemplate` (existente, la rama "con registry") **no cambia** — sigue igual. Se agrega un método nuevo, independiente, para la rama "build en remoto":

```csharp
public List<DeployStep> CreateDockerComposeRemoteBuildTemplate(string remoteDeployPath, string composeFileName)
{
    var remoteComposeFilePath = $"{remoteDeployPath}/{composeFileName}";

    return new List<DeployStep>
    {
        new() { Type = StepType.SshCommand, Name = "Actualizar código", Args = { ["Command"] = $"cd {remoteDeployPath} && git pull" } },
        new() { Type = StepType.DockerComposeBuild, Name = "Compose build", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
        new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } }
    };
}
```

**Decisión explícita: sin `CopyToRemote` ni `DockerImagePrune` en esta plantilla.**
- `CopyToRemote` del compose file no hace falta — `git pull` ya lo trae (está en el repo). Precondición fuera de alcance del CLI: el repo debe estar ya clonado en `{remoteDeployPath}` en el servidor, con su propio acceso git (deploy key del servidor, no la clave SSH que usa el CLI para conectarse).
- `DockerImagePrune` se omite a propósito: su `ImageNameFilter` asume la convención `{proyecto}-{subproyecto}` que usan las imágenes tageadas por el CLI (`DockerBuildExecutor`). Un `docker compose build` corrido directo sobre el `docker-compose.yml` del usuario (sin `image:` explícito, como es el caso real acá) nombra las imágenes con la convención propia de Docker Compose (basada en el nombre de carpeta + servicio), que no necesariamente coincide — filtrar con el patrón equivocado podría no limpiar nada, o en el peor caso, limpiar de más. Se prefiere no incluir el step antes que adivinar mal. Si hace falta limpieza de imágenes viejas en este escenario, se agrega a mano después vía "Insert RawCommand" con el comando correcto para ese `docker-compose.yml` puntual.

### 4. Wizard — sub-pregunta al elegir "Docker Compose"

**`MenuManager.PromptPipelinesForSubProject`** (wizard fusionado de alta de subproyecto) y **`PipelineEditorMenu.StartAsync`** (alta de pipeline para un ambiente nuevo de un subproyecto existente) ganan la misma sub-pregunta cuando el template elegido es "Docker Compose":

```csharp
var dockerMode = AnsiConsole.Prompt(
    new SelectionPrompt<string>()
        .Title("¿Cómo se buildea la imagen?")
        .AddChoices("Build directo en el servidor (sin registry)", "Push a un registry"));
```

- Si `"Build directo en el servidor (sin registry)"` → `factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath, composeFileName)`. No se pide `DockerRegistry`.
- Si `"Push a un registry"` → se mantiene la plantilla actual (`CreateDockerComposeTemplate`), pero antes de generarla se resuelve el `DockerRegistry`:

```csharp
private static DockerRegistry ResolveDockerRegistry()
{
    var host = AnsiConsole.Ask("Host del registry (vacío = Docker Hub):", "");
    var username = AnsiConsole.Ask<string>("Usuario del registry:");
    var hasToken = AnsiConsole.Confirm("¿El registry necesita token/password vía variable de entorno?", true);
    var tokenEnvVar = hasToken ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con el token:") : null;

    return new DockerRegistry { Host = host, Username = username, TokenEnvVar = tokenEnvVar };
}
```

`ResolveDockerRegistry` siempre pregunta — la decisión de "no volver a preguntar si ya hay uno" vive enteramente en el `??=` de cada caller (`dockerRegistry ??= ResolveDockerRegistry()` en `MenuManager`, `configSubProject.DockerRegistry ??= ResolveDockerRegistry()` en `PipelineEditorMenu`), que ya corta la evaluación del lado derecho si el de la izquierda no es `null`. Un parámetro `existing` dentro del método sería redundante con esa guarda — se descarta esa versión.

**Cambio de forma en `MenuManager.PromptPipelinesForSubProject`:** como ahora puede resolver un `DockerRegistry` que hay que guardar en el `SubProject` (no solo en `PipelinesByEnvironment`), el método pasa a devolver una tupla:

```csharp
private static (Dictionary<string, List<Domain.DeployStep>> Pipelines, DockerRegistry? DockerRegistry) PromptPipelinesForSubProject(string projectName, string subProjectName, List<DeployEnvironment> environments)
{
    var environmentNames = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title($"¿A qué ambiente(s) apunta '{subProjectName}'? (barra espaciadora para elegir, Enter para confirmar)")
            .AddChoices(environments.Select(e => e.Name)));

    var pipelines = new Dictionary<string, List<Domain.DeployStep>>();
    DockerRegistry? dockerRegistry = null;
    var factory = new Application.PipelineTemplateFactory();

    foreach (var environmentName in environmentNames)
    {
        var environment = environments.First(e => e.Name == environmentName);

        var template = AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title($"Plantilla inicial para '{environmentName}':").AddChoices("Docker Compose", "Publish/Zip"));

        var defaultRemotePath = Application.PipelineTemplateFactory.ResolveDefaultRemoteDeployPath(projectName, subProjectName, environment);
        var remoteDeployPath = AnsiConsole.Ask("Path remoto de deploy:", defaultRemotePath);

        if (template == "Docker Compose")
        {
            var composeFileName = AnsiConsole.Ask("Nombre del archivo docker-compose:", "docker-compose.yml");

            var dockerMode = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("¿Cómo se buildea la imagen?")
                    .AddChoices("Build directo en el servidor (sin registry)", "Push a un registry"));

            if (dockerMode == "Build directo en el servidor (sin registry)")
            {
                pipelines[environmentName] = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath, composeFileName);
            }
            else
            {
                dockerRegistry ??= ResolveDockerRegistry();
                pipelines[environmentName] = factory.CreateDockerComposeTemplate(projectName, subProjectName, remoteDeployPath, composeFileName, dockerRegistry);
            }
        }
        else
        {
            pipelines[environmentName] = factory.CreatePublishZipTemplate(projectName, subProjectName, remoteDeployPath);
        }
    }

    return (pipelines, dockerRegistry);
}
```

`PromptSubProjectsAsync` (caller) asigna el `DockerRegistry` devuelto al `SubProject` nuevo:

```csharp
var (pipelinesByEnvironment, dockerRegistry) = PromptPipelinesForSubProject(projectName, projectName, environments);

var subProject = new SubProject
{
    Name = projectName,
    Path = subProjectPath,
    DockerfilePath = dockerfilePath,
    PipelinesByEnvironment = pipelinesByEnvironment,
    DockerRegistry = dockerRegistry
};
```

**`PipelineEditorMenu.StartAsync`** ya tiene acceso directo a `configSubProject`, así que no necesita tupla — resuelve y asigna en el momento:

```csharp
if (isDockerCompose)
{
    var composeFileName = AnsiConsole.Ask("Nombre del archivo docker-compose:", "docker-compose.yml");

    var dockerMode = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("¿Cómo se buildea la imagen?")
            .AddChoices("Build directo en el servidor (sin registry)", "Push a un registry"));

    if (dockerMode == "Build directo en el servidor (sin registry)")
    {
        var confirmed = AnsiConsole.Confirm($"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con build directo en el servidor, path remoto '{remoteDeployPath}' y archivo '{composeFileName}'?", true);
        if (!confirmed)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
            return;
        }

        var factory = new PipelineTemplateFactory();
        configSubProject.PipelinesByEnvironment[environmentName] = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath, composeFileName);
    }
    else
    {
        configSubProject.DockerRegistry ??= ResolveDockerRegistry();

        var confirmed = AnsiConsole.Confirm($"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con push a registry, path remoto '{remoteDeployPath}' y archivo '{composeFileName}'?", true);
        if (!confirmed)
        {
            AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
            return;
        }

        var factory = new PipelineTemplateFactory();
        configSubProject.PipelinesByEnvironment[environmentName] = factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, remoteDeployPath, composeFileName, configSubProject.DockerRegistry);
    }
}
else
{
    // rama Publish/Zip, sin cambios respecto a lo que ya existe
}
```

Nota: el spec muestra la forma final del bloque; el plan de implementación va a mostrar el diff exacto contra el código actual de `StartAsync` (que hoy tiene un único branch `isDockerCompose ? ... : ...`, ver commit `cb809e9`).

### 5. `PipelineEditorMenu.EditStepsAsync` — insertar en posición

Reemplaza el `case "Insert RawCommand"` actual (que hace `steps.Add(...)`, siempre al final):

```csharp
case "Insert RawCommand":
    var command = AnsiConsole.Ask<string>("Comando a insertar:");
    var newStep = new DeployStep { Type = StepType.RawCommand, Name = command, Args = { ["Command"] = command } };
    var insertIndex = PromptInsertPosition(steps);
    steps.Insert(insertIndex, newStep);
    repository.Save(config);
    break;
```

Nuevo método privado:

```csharp
private static int PromptInsertPosition(List<DeployStep> steps)
{
    if (steps.Count == 0)
    {
        return 0;
    }

    var choices = steps.Select(s => $"Antes de '{s.Name}'").Append("Al final").ToList();
    var choice = AnsiConsole.Prompt(new SelectionPrompt<string>().Title("¿Dónde insertar?").AddChoices(choices));

    return choice == "Al final" ? steps.Count : choices.IndexOf(choice);
}
```

`choices.IndexOf(choice)` funciona porque las opciones "Antes de 'X'" están en el mismo orden que `steps`, así que su índice en `choices` coincide con la posición en `steps` donde hay que insertar (insertar en la posición `i` empuja el step que estaba en `i` un lugar más adelante, que es exactamente "antes de ese step").

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| Usuario elige "Build directo en el servidor" pero el repo no está clonado en el servidor | Fuera de alcance del CLI detectarlo de antemano — el step "Actualizar código" (`git pull`) va a fallar con el error real de git (ej. "not a git repository"), visible en el resultado del step como cualquier otro fallo de `SshCommand`. |
| Usuario elige "Push a un registry" para un SubProyecto que ya tiene `DockerRegistry` seteado (desde el menú legacy) | No se vuelve a preguntar — el `??=` en el caller corta antes de invocar `ResolveDockerRegistry`. |
| Mismo SubProyecto, dos ambientes, ambos "Push a un registry", en la misma corrida del wizard fusionado | Solo se pregunta una vez (primera vez que se resuelve `dockerRegistry` en el loop de `PromptPipelinesForSubProject`); el segundo ambiente reusa el mismo objeto en memoria. |
| Insertar en pipeline vacío (0 steps) | `PromptInsertPosition` devuelve `0` sin preguntar — no tiene sentido preguntar "antes de cuál" si no hay ningún step. |

## Testing

**`vali-deploy.Tests/Application/Executors/DockerComposeExecutorsTests.cs`** (existente — cubre Pull/Up/Down en una sola clase, se agregan 3 tests de `Build` con el mismo patrón exacto, reusando los helpers `Context()`/`ContextWithoutServer()`/`ComposeStep()`/`ComposeStepWithoutArgs()` ya definidos ahí):
- `Build_runs_docker_compose_build_on_remote` — mismo shape que `Up_runs_docker_compose_up_detached_on_remote`, verificando el comando `docker compose -f "/opt/app/compose.yml" build` y `Assert.Equal(StepType.DockerComposeBuild, executor.Handles);` inline (no hay un `Handles_X` separado en este archivo, se verifica dentro del primer test de cada executor).
- `Build_fails_fast_when_environment_has_no_remote_server`.
- `Build_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing`.

**`vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`**:
- `CreateDockerComposeRemoteBuildTemplate_follows_step_order` — `SshCommand, DockerComposeBuild, DockerComposeUp`, sin `CopyToRemote` ni `DockerImagePrune`.
- `CreateDockerComposeRemoteBuildTemplate_builds_git_pull_command_with_remoteDeployPath`.
- `CreateDockerComposeRemoteBuildTemplate_sets_ComposeFilePath_using_remoteDeployPath_and_composeFileName`.

Sin tests para `MenuManager.cs`/`PipelineEditorMenu.cs` — mismo criterio que el resto de `Presentation/Managers` en este repo.

## Decisiones registradas

- `CreateDockerComposeTemplate` (rama "con registry") no cambia de forma — solo gana un caller que ahora sí completa el `DockerRegistry` antes de invocarla.
- `DockerImagePrune` se omite deliberadamente en la plantilla "build en remoto" — el riesgo de un filtro de nombre de imagen incorrecto (por la convención de nombrado propia de `docker compose build` sin `image:` explícito) pesa más que la conveniencia de limpiar automáticamente.
- El `DockerRegistry` resuelto en el wizard se guarda en `SubProject.DockerRegistry` (mismo campo que ya usa el menú legacy) — no se duplica el dato, ambos sistemas comparten esa única fuente de verdad aunque el usuario solo pase por el wizard nuevo.
- La precondición de "repo ya clonado en el servidor con su propio acceso git" queda documentada pero no verificada por el CLI — verificarlo requeriría una llamada SSH extra antes de armar el pipeline, que no aporta lo suficiente para el esfuerzo en este ciclo.
- Menú legacy ("Manage Docker Projects"/"Manage Publish Arguments") y plantilla Publish/Zip: sin cambios, confirmado explícitamente con el usuario que no son su flujo real.
