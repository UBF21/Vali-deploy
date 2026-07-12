# Asociación obligatoria Proyecto/SubProyecto ↔ Ambiente — Design Spec

**Fecha:** 2026-07-11
**Contexto:** el usuario detectó que "Add Project" no pide ambiente — la asociación SubProyecto↔Ambiente hoy es implícita (solo existe si alguien entra después a "Pipeline Editor" y arma un pipeline). Este ciclo (Ciclo A de dos, ver decisión de alcance abajo) cierra ese gap en el flujo de datos. El rediseño visual del menú principal (Ciclo B) queda para un ciclo aparte, sin dependencias con este.

## Premisa

Todo SubProyecto debe quedar asociado a al menos un Ambiente desde el momento en que se crea — no puede existir "flotando" sin destino de deploy. Como precondición, no puede haber Proyectos si no hay ningún Ambiente configurado todavía.

## Alcance

1. **Gate**: no se puede crear un Proyecto si `config.Environments` está vacío — se redirige a crear un ambiente primero.
2. **Wizard fusionado**: al dar de alta un SubProyecto, se elige a qué ambiente(s) apunta y se arma ahí mismo un pipeline inicial (plantilla + path remoto) para cada uno — no queda para después.
3. **Path remoto por SubProyecto+Ambiente**: reemplaza el único override global (`DeployEnvironment.RemoteDeployPath`, que aplicaba igual a todos los subproyectos de un ambiente) por una confirmación/override puntual en el momento de asociar cada SubProyecto a cada Ambiente.
4. **Fix de `LocalPath` en la plantilla Publish/Zip**: hallazgo durante el diseño — ese template genera un step "Copiar zip al remoto" con Args vacíos, que hoy explota en runtime (`CopyToRemoteExecutor` requiere `LocalPath`/`RemotePath`, ninguno seteado). El nombre del zip es timestamped (generado en runtime), así que no hay forma de conocerlo en tiempo de diseño del pipeline — se resuelve pasando el artifact entre steps vía el `StepExecutionContext` compartido.

Fuera de alcance: comandos SSH de "Extraer zip"/"Reiniciar servicio" (dependen del SO/servicio puntual, se completan a mano vía "Edit Step Args", ya existente). Rediseño visual del menú principal (Ciclo B, spec aparte).

## Diseño

### 1. Gate en `MenuManager.AddProjectAsync`

```csharp
private static async Task AddProjectAsync()
{
    var config = _repository.Load();
    if (config.Environments.Count == 0)
    {
        AnsiConsole.MarkupLine("[yellow]Necesitás al menos un ambiente configurado antes de crear un proyecto.[/]");
        await Presentation.EnvironmentMenu.StartAsync(_repository);

        config = _repository.Load();
        if (config.Environments.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Todavía no hay ningún ambiente. Cancelando alta de proyecto.[/]");
            return;
        }
    }

    string? projectName = PromptProjectName();
    if (projectName == null) return;

    string? projectPath = PromptProjectPath();
    if (projectPath == null) return;

    var subProjects = await PromptSubProjectsAsync(projectPath, projectName, config.Environments);
    if (subProjects == null) return;

    AddProjectToConfig(projectName, new Project { Path = projectPath, SubProjects = subProjects });
    AnsiConsole.MarkupLine($"[green]Project '{Markup.Escape(projectName)}' added successfully![/]");
}
```

Reusa `EnvironmentMenu.StartAsync` completo (con su cancelación, ya arreglada esta sesión) en vez de duplicar un mini-flujo de alta de ambiente. Si el usuario entra y sale sin crear ninguno, se aborta el alta de proyecto sin persistir nada — consistente con "sin guardado parcial".

### 2. Wizard fusionado en `PromptSubProjectsAsync`

Cambia de firma — gana `projectName` (necesario para las plantillas de pipeline) y `environments` (para el `MultiSelectionPrompt`):

```csharp
private static async Task<List<SubProject>?> PromptSubProjectsAsync(string projectPath, string projectName, List<DeployEnvironment> environments)
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

        var pipelinesByEnvironment = PromptPipelinesForSubProject(projectName, subProjectName, environments);

        subProjects.Add(new SubProject
        {
            Name = subProjectName,
            Path = subProjectPath,
            DockerfilePath = dockerfilePath,
            PipelinesByEnvironment = pipelinesByEnvironment
        });
        AnsiConsole.MarkupLine($"[green]Subproject '{Markup.Escape(subProjectName)}' added.[/]");
    }

    return await Task.FromResult(subProjects.Count > 0 ? subProjects : null);
}

private static Dictionary<string, List<DeployStep>> PromptPipelinesForSubProject(string projectName, string subProjectName, List<DeployEnvironment> environments)
{
    var environmentNames = AnsiConsole.Prompt(
        new MultiSelectionPrompt<string>()
            .Title($"¿A qué ambiente(s) apunta '{subProjectName}'? (barra espaciadora para elegir, Enter para confirmar)")
            .AddChoices(environments.Select(e => e.Name)));

    var pipelines = new Dictionary<string, List<DeployStep>>();
    var factory = new PipelineTemplateFactory();

    foreach (var environmentName in environmentNames)
    {
        var environment = environments.First(e => e.Name == environmentName);

        var template = AnsiConsole.Prompt(
            new SelectionPrompt<string>().Title($"Plantilla inicial para '{environmentName}':").AddChoices("Docker Compose", "Publish/Zip"));

        var defaultRemotePath = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath(projectName, subProjectName, environment);
        var remoteDeployPath = AnsiConsole.Ask("Path remoto de deploy:", defaultRemotePath);

        pipelines[environmentName] = template == "Docker Compose"
            ? factory.CreateDockerComposeTemplate(projectName, subProjectName, remoteDeployPath)
            : factory.CreatePublishZipTemplate(projectName, subProjectName, remoteDeployPath);
    }

    return pipelines;
}
```

**Sin "Cancelar" dentro del loop por-ambiente**: a diferencia de `PipelineEditorMenu` (que persiste sobre un SubProyecto ya existente), acá todo el SubProyecto —incluidos sus pipelines— vive en memoria hasta que termina `AddProjectAsync` completo. Si el usuario elige mal una plantilla, no hay nada guardado todavía que deshacer — lo corrige después con "Pipeline Editor" (ya cancelable) una vez creado el proyecto. `MultiSelectionPrompt` sin `.NotRequired()` ya exige ≥1 ambiente por default en Spectre.Console (mismo criterio que la decisión de "obligatorio").

`DockerRegistry`/`OmitFiles` no se pasan a las plantillas acá (el SubProyecto es nuevo, esos campos todavía no existen) — mismo comportamiento que ya tiene hoy el flujo standalone de `PipelineEditorMenu` para un SubProyecto que arma su primer pipeline.

### 3. Path remoto por SubProyecto+Ambiente — refactor de `PipelineTemplateFactory`

`CreateDockerComposeTemplate`/`CreatePublishZipTemplate` dejan de recibir `DeployEnvironment` y de calcular el path remoto internamente — lo reciben ya resuelto:

```csharp
public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, string remoteDeployPath, DockerRegistry? dockerRegistry = null)
{
    var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
    var remoteComposeFilePath = $"{remoteDeployPath}/compose.yml";
    var registryTag = BuildRegistryTag(dockerRegistry, imageTag);
    // ... resto sin cambios, usa remoteComposeFilePath donde antes usaba la variable calculada inline
}

public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName, string remoteDeployPath, List<string>? omitFiles = null)
{
    var omitFilesArg = omitFiles is { Count: > 0 } ? string.Join("|", omitFiles) : "";
    var remoteZipPath = $"{remoteDeployPath}/{subProjectName.ToLower()}.zip";

    return new List<DeployStep>
    {
        new() { Type = StepType.GitCheckout, Name = "Checkout" },
        new() { Type = StepType.ZipPublishOutput, Name = "Build, publish y comprimir output", Args = { ["OmitFiles"] = omitFilesArg } },
        new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto", Args = { ["RemotePath"] = remoteZipPath } },
        new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
        new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
    };
}

public static string ResolveDefaultRemoteDeployPath(string projectName, string subProjectName, DeployEnvironment environment) =>
    environment.RemoteDeployPath ?? $"/opt/{projectName.ToLower()}-{subProjectName.ToLower()}";
```

`ResolveDefaultRemoteDeployPath` extrae la convención que hoy vive inline en `CreateDockerComposeTemplate` — mismo comportamiento (`environment.RemoteDeployPath` sigue funcionando como default a nivel ambiente, ahora es solo el punto de partida que el usuario puede aceptar o sobreescribir por subproyecto, ya no un override ciego).

**`PipelineEditorMenu.StartAsync`** (el flujo standalone para asociar un ambiente nuevo a un SubProyecto ya existente) se actualiza para pedir el mismo path remoto, con el mismo default, antes de la confirmación que ya agregamos esta sesión:

```csharp
if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName))
{
    var template = AnsiConsole.Prompt(
        new SelectionPrompt<string>().Title("Plantilla inicial:").AddChoices("Docker Compose", "Publish/Zip", "Cancelar"));

    if (template == "Cancelar")
    {
        return;
    }

    var defaultRemotePath = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath(projectName, configSubProject.Name, environment);
    var remoteDeployPath = AnsiConsole.Ask("Path remoto de deploy:", defaultRemotePath);

    var confirmed = AnsiConsole.Confirm($"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con la plantilla '{template}' y path remoto '{remoteDeployPath}'?", true);
    if (!confirmed)
    {
        AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
        return;
    }

    var factory = new PipelineTemplateFactory();
    configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
        ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, remoteDeployPath, configSubProject.DockerRegistry)
        : factory.CreatePublishZipTemplate(projectName, configSubProject.Name, remoteDeployPath, configSubProject.OmitFiles);

    repository.Save(config);
}
```

### 4. Fix `LocalPath` — artifact entre steps vía `StepExecutionContext`

`PipelineRunner.RunAsync` (`Application/PipelineRunner.cs:18-46`) ya pasa la MISMA instancia de `StepExecutionContext` por referencia a todos los steps de una corrida (confirmado leyendo el código — no hay que crear ningún mecanismo nuevo de propagación, alcanza con una propiedad mutable):

```csharp
// Application/StepExecutionContext.cs
public class StepExecutionContext
{
    public required string ProjectName { get; init; }
    public required string SubProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required DeployEnvironment Environment { get; init; }
    public string? LastArtifactPath { get; set; }
}
```

```csharp
// Application/Executors/ZipPublishExecutor.cs — al final de ExecuteAsync, antes de SuccessResult:
var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
combinedOutput.AppendLine($"Comprimido en: {zipPath}");
context.LastArtifactPath = zipPath;

stopwatch.Stop();
return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
```

```csharp
// Application/Executors/CopyToRemoteExecutor.cs — reemplaza la resolución de LocalPath:
var localPath = step.Args.GetValueOrDefault("LocalPath");
if (string.IsNullOrEmpty(localPath))
{
    localPath = context.LastArtifactPath;
}
if (string.IsNullOrEmpty(localPath))
{
    throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"LocalPath\"] o un step anterior que produzca un artifact (ej. ZipPublishOutput).");
}

if (!step.Args.TryGetValue("RemotePath", out var remotePath))
{
    throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"RemotePath\"].");
}
```

`RemotePath` sigue siendo estrictamente requerido en Args (no tiene fallback — con el fix de la sección 3, la plantilla Publish/Zip ya lo setea siempre). Solo `LocalPath` gana el fallback, porque es el único caso real de "no se puede saber en tiempo de diseño".

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| `config.Environments` vacío al intentar "Add Project" | Redirige a `EnvironmentMenu.StartAsync`; si vuelve sin crear ninguno, cancela el alta sin persistir nada. |
| Usuario no selecciona ningún ambiente en el `MultiSelectionPrompt` del wizard fusionado | No aplica — Spectre.Console no permite confirmar la selección con 0 elementos cuando no se llamó `.NotRequired()`. |
| `CopyToRemote` sin `Args["LocalPath"]` y sin `context.LastArtifactPath` (pipeline mal armado a mano, ej. alguien borra el step `ZipPublishOutput` previo) | Excepción clara indicando que falta el Arg o un step previo que genere el artifact — mismo patrón que la excepción ya existente de `RemotePath`. |
| Pipeline con `CopyToRemote` seguido de OTRO `CopyToRemote` sin `LocalPath` propio (ej. copiar dos archivos distintos) | El segundo hereda `context.LastArtifactPath` del ÚLTIMO artifact producido (no necesariamente el que el usuario esperaba) — limitación conocida y aceptada: si un pipeline necesita copiar múltiples archivos con distinto origen, cada `CopyToRemote` debe declarar su propio `LocalPath` explícito; el fallback es solo para el caso de 1 artifact → 1 copia, que es el único que generan las plantillas hoy. |

## Testing

**`PipelineTemplateFactoryTests.cs`** (modificar tests existentes + agregar nuevos):
- Los tests `DockerCompose_template_uses_opt_convention_for_remote_path_by_default` y `DockerCompose_template_uses_environment_RemoteDeployPath_override_when_set` ya no aplican tal cual (la factory ya no lee `DeployEnvironment`) — se reemplazan por tests que pasan `remoteDeployPath` directo y verifican que se usa tal cual en `RemotePath`/`ComposeFilePath`/nuevo `RemotePath` de Publish/Zip.
- Todos los demás tests existentes de `CreateDockerComposeTemplate`/`CreatePublishZipTemplate` se actualizan solo en la firma de la llamada (pasar un string de path en vez de `Environment(...)`), sin cambiar sus asserts.
- Nuevo: `ResolveDefaultRemoteDeployPath_uses_opt_convention_when_environment_has_no_override`.
- Nuevo: `ResolveDefaultRemoteDeployPath_uses_environment_RemoteDeployPath_when_set`.
- Nuevo: `PublishZip_template_sets_RemotePath_on_CopyToRemote_step`.

**`CopyToRemoteExecutorTests.cs`**:
- Modificar `ExecuteAsync_throws_clear_error_when_LocalPath_arg_missing`: el mensaje de excepción esperado cambia (incluye la mención al fallback).
- Nuevo: `ExecuteAsync_falls_back_to_context_LastArtifactPath_when_LocalPath_arg_missing` — `Args` sin `LocalPath`, `context.LastArtifactPath` seteado, verifica que `UploadFileAsync` se llama con ese path.
- Nuevo: `ExecuteAsync_throws_when_LocalPath_arg_missing_and_no_LastArtifactPath_in_context` — mismo caso que el test existente pero renombrado/clarificado, con `context.LastArtifactPath == null`.

**`ZipPublishExecutorTests.cs`**:
- Nuevo: `Sets_context_LastArtifactPath_to_the_created_zip_path_on_success` — reusa `CreateFakePublishFolder`, verifica `context.LastArtifactPath` después de `ExecuteAsync` exitoso.
- Nuevo (opcional, cubre la regla de "no se pisa en fallo"): `Does_not_set_context_LastArtifactPath_when_build_fails`.

Sin tests para `MenuManager.cs`/`EnvironmentMenu.cs`/`PipelineEditorMenu.cs` — mismo criterio que el resto de `Presentation/`Managers basados en Spectre.Console.

## Decisiones registradas

- Granularidad de asociación: el SubProyecto (no el Proyecto) es la unidad que se asocia a Ambiente(s) — coherente con que `PipelinesByEnvironment` ya vive en `SubProject`.
- El gate es un bloqueo duro con redirect automático, no una advertencia opcional.
- Selección de ambiente(s) obligatoria (≥1) al crear un SubProyecto — sin salida "ninguno por ahora".
- El override de path remoto pasa de ser a nivel Ambiente (afecta a todos los subproyectos por igual) a nivel SubProyecto+Ambiente (uno por combinación) — `DeployEnvironment.RemoteDeployPath` no se elimina, pasa a ser solo el valor default sugerido en el prompt, ya no un override ciego aplicado en el factory.
- Se extiende el arreglo de path remoto también a Publish/Zip (no solo Docker Compose) — el pedido original era genérico, no específico de una plantilla.
- Se expande el alcance para arreglar también `LocalPath` (bug preexistente, no causado por este cambio, pero bloqueante para que Publish/Zip funcione de punta a punta) vía un mecanismo de "artifact entre steps" en `StepExecutionContext` — decisión explícita del usuario de ampliar el alcance en vez de dejarlo como deuda técnica aparte.
- Los comandos SSH de extracción/reinicio del template Publish/Zip siguen sin autocompletar — dependen del SO/servicio específico, no hay convención segura para inventarlos.
- Rediseño visual del menú principal queda fuera de este spec — es un ciclo aparte (Ciclo B), sin dependencias de datos con este.
