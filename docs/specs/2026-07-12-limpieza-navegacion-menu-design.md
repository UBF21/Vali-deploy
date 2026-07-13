# Limpieza de navegación del menú (Ciclo B) — Design Spec

**Fecha:** 2026-07-12
**Contexto:** acumulado de quejas concretas del usuario durante esta sesión: "Edit Pipeline" no se encuentra desde "Show Projects", muchas pantallas no tienen forma de cancelar/salir sin cerrar la app entera, "eliminar subproyecto" sigue apareciendo cuando ya no tiene sentido (desde este ciclo, todo Proyecto tiene exactamente 1 SubProyecto con su mismo nombre, auto-generado — ver Ciclo de "auto-completar el subproyecto único"), y varias pantallas muestran breadcrumbs/contadores duplicados (`"MiProyecto · MiProyecto"`, `"N proyectos · N subproyectos"` con el mismo N dos veces).

Investigación previa (mapeo completo, con line numbers): confirmó un **bug crítico** no reportado explícitamente por el usuario pero que explica su queja #1 — "Edit Pipeline" es hoy prácticamente inalcanzable desde el flujo normal.

## Alcance

1. **Bug crítico**: `ExecuteCommandSubProject` desvía automáticamente a "ejecutar pipeline" en vez de mostrar el menú de acciones (que incluye "Edit Pipeline"), porque la condición que decide el desvío (`PipelinesByEnvironment.Count > 0`) es hoy SIEMPRE verdadera.
2. **Cancelar en 5 flujos sin ninguna salida** (ni siquiera por texto mágico): wizard de alta de proyecto (`PromptPipelinesForSubProject`), y las 3 copias de `ResolveDockerRegistry` (MenuManager x2, PipelineEditorMenu x1).
3. **Sacar "Remove Subprojects" del menú principal** — con 1:1 Proyecto↔SubProyecto, "Remove Project" ya cubre el caso completo; "Remove Subprojects" deja proyectos huérfanos sin sentido claro.
4. **Colapsar 2 selectores de subproyecto redundantes** (`SelectSubProjectAsync` en Omit Files, `ManageDockerSubProjectsAsync`) para que, cuando hay 1 solo subproyecto, no pregunten — mismo patrón que ya usa `ShowSubProjectsAsync`.
5. **Limpiar breadcrumbs y header duplicados**: `"{projectName} · {subProject.Name}"` → solo `projectName` (son siempre el mismo valor); `"N proyectos · N subproyectos"` → solo `"N proyectos"`.

Fuera de alcance (decisión ya tomada en un ciclo anterior de esta sesión, no se reabre acá): el modelo de dominio sigue teniendo `SubProject` como entidad interna — no se colapsa a nivel de datos, solo se limpia lo que el usuario ve.

## Diseño

### 1. Fix del bug crítico — `ExecuteCommandSubProject` (`MenuManager.cs:858-954`)

El menú de acciones (líneas 880-953) deja de estar condicionado a `PipelinesByEnvironment.Count == 0` — se muestra siempre. Se agrega una opción nueva `"Run Pipeline"` que es la que antes se disparaba automáticamente:

```csharp
private static async Task ExecuteCommandSubProject(Project project, SubProject? subProject, string projectName)
{
    if (subProject == null) return;

    var repository = CompositionRoot.CreateProjectRepository();
    var config = repository.Load();
    var configSubProject = config.Projects[projectName].SubProjects.First(s => s.Name == subProject.Name);

    var choices = new List<string> { "Generate Microsoft publish", "Edit Pipeline", "[seagreen1]Back to Projects[/]" };
    if (configSubProject.PipelinesByEnvironment.Count > 0)
    {
        choices.Insert(0, "Run Pipeline");
    }
    if (!string.IsNullOrEmpty(subProject.DockerfilePath))
    {
        choices.InsertRange(choices.IndexOf("Generate Microsoft publish"), _dockerActions);
    }

    AnsiConsole.Clear();
    Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: projectName);

    var action = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Translated("What do you want to do?")
            .AddChoices(choices)
    );

    switch (action)
    {
        case "Run Pipeline":
            await ExecuteSubProjectPipelineAsync(project, configSubProject, projectName, config);
            break;

        case "Generate Microsoft publish":
            await RunLocalPipelineAsync(project, subProject, projectName,
                new Application.PipelineTemplateFactory().CreateLocalPublishTemplate(subProject.OmitFiles));
            break;

        case "Edit Pipeline":
            await Presentation.PipelineEditorMenu.StartAsync(CompositionRoot.CreateProjectRepository(), projectName, subProject);
            break;

        case "Docker Build":
            /* ...sin cambios respecto al código actual... */
            break;

        case "Docker Run":
            /* ...sin cambios... */
            break;

        case "Push to registry":
            /* ...sin cambios de lógica, pero ver sección 2 para el cambio de ResolveDockerRegistry... */
            break;

        case "[seagreen1]Back to Projects[/]":
            return;
    }
}
```

Cambios puntuales respecto al código actual:
- El breadcrumb `$"{projectName} · {subProject.Name}"` (línea 895 original) pasa a ser solo `projectName` — ver sección 5.
- El texto del prompt (línea 888 original, `"What do you want to do with subproject '{0}'?"`) pasa a `"What do you want to do?"` sin nombrar "subproject" — el usuario ya sabe en qué proyecto está por el breadcrumb.
- El choice de salida cambia de `"[seagreen1]Back to Subprojects[/]"` a `"[seagreen1]Back to Projects[/]"` — no hay "Subprojects" como nivel de navegación separado.
- `AnsiConsole.Clear()` + `DrawHeader` ahora se hacen SIEMPRE antes del prompt de acción (antes solo se hacía condicionalmente en algunas ramas, línea 892-896 original) — consistencia visual.

**`ShowSubProjectsAsync`** (`MenuManager.cs:553-584`) no cambia su lógica de invocación — sigue llamando a `ExecuteCommandSubProject` cuando `Count == 1` (que es siempre). El fix vive enteramente dentro de `ExecuteCommandSubProject`.

### 2. Cancelar en los 5 flujos sin salida

**`PromptPipelinesForSubProject`** (`MenuManager.cs:326-373`, wizard de alta de proyecto): se agrega un choice `"Cancelar (no asociar más ambientes)"` al final del `MultiSelectionPrompt` de ambientes — pero como ya es obligatorio elegir ≥1 (decisión de un ciclo anterior, no se reabre), la salida real está en el loop `foreach`: si el usuario cancela DENTRO de la configuración de un ambiente puntual (plantilla, path remoto, modo Docker), se salta ese ambiente sin agregarlo a `pipelines`, en vez de forzarlo a completar todo. Se agrega un choice `"Cancelar este ambiente"` a la selección de plantilla:

```csharp
var template = AnsiConsole.Prompt(
    new SelectionPrompt<string>().Title($"Plantilla inicial para '{environmentName}':")
        .AddChoices("Docker Compose", "Publish/Zip", "[seagreen1]Cancelar este ambiente[/]"));

if (template == "[seagreen1]Cancelar este ambiente[/]")
{
    continue; // no agrega pipelines[environmentName], sigue con el próximo ambiente elegido
}
```

Si el usuario cancela TODOS los ambientes que había elegido, `pipelines` queda vacío — `PromptSubProjectsAsync` (caller) deja el `SubProject` con `PipelinesByEnvironment` vacío, consistente con el estado que ya manejaba el código antes de este ciclo (subproyecto sin pipeline, editable después vía "Edit Pipeline").

**`ResolveDockerRegistry`** (3 copias: `MenuManager.cs:380-390`, `MenuManager.cs:934-939` dentro de `ExecuteCommandSubProject`, `PipelineEditorMenu.cs:219-229`): cambia de `DockerRegistry` (no-nullable) a `DockerRegistry?`, con un `Confirm` inicial:

```csharp
private static DockerRegistry? ResolveDockerRegistry()
{
    if (!AnsiConsole.Confirm("¿Configurar el registry ahora?", true))
    {
        return null;
    }

    var username = AnsiConsole.Ask<string>("Usuario del registry (ej. tu usuario de Docker Hub):");
    var host = AnsiConsole.Ask("Host del registry (vacío = Docker Hub):", "");
    var hasToken = AnsiConsole.Confirm("¿Vas a autenticarte con un token vía variable de entorno?");
    string? tokenEnvVar = hasToken
        ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con el token:")
        : null;

    return new DockerRegistry { Host = host, Username = username, TokenEnvVar = tokenEnvVar };
}
```

Todos los callers ya usan `??=`/chequeo de `null` — si `ResolveDockerRegistry()` devuelve `null`, el caller debe tratarlo como "no se pudo armar el pipeline con registry ahora" (mismo patrón que cancelar plantilla): en `PromptPipelinesForSubProject` y `PipelineEditorMenu`, si es `null`, no se crea el pipeline para ese ambiente (mensaje `"Cancelado. No se configuró el registry."` + no persistir). En la rama legacy `"Push to registry"` de `ExecuteCommandSubProject`, si es `null`, se cancela esa acción puntual y vuelve al menú.

### 3. Sacar "Remove Subprojects" del menú principal

`MenuManager.cs:124-126` (`GetMainMenuOption`): eliminar `"Remove Subprojects"` de `AddChoices(...)`. `MenuManager.cs:62-63` (switch de `StartAsync`): eliminar el `case "Remove Subprojects":`. `RemoveSubprojectsAsync`, `PromptProjectSelectionForSubprojectRemoval`, `PromptMultipleSubProjectSelection` (líneas 428-508) quedan sin ningún caller — se eliminan del archivo (código muerto, no una feature flag ni nada que valga la pena preservar).

### 4. Colapsar selectores de subproyecto redundantes

**`SelectSubProjectAsync`** (`MenuManager.cs:659-684`, usado por Omit Files): agregar el mismo atajo que ya usa `ShowSubProjectsAsync`:

```csharp
private static async Task<SubProject?> SelectSubProjectAsync(Project project, string projectName)
{
    if (project.SubProjects.Count == 0)
    {
        AnsiConsole.MarkupLine($"[yellow]:warning: No subprojects found for project '{Markup.Escape(projectName)}'.[/]");
        await Task.CompletedTask;
        return null;
    }

    if (project.SubProjects.Count == 1)
    {
        return project.SubProjects[0];
    }

    var subProjectName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .TranslatedFormat("Select a subproject for project '{0}' to manage files to omit", projectName)
            .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
    );

    if (subProjectName == "[seagreen1]Back to Projects[/]") return null;

    return project.SubProjects.FirstOrDefault(sp => sp.Name == subProjectName);
}
```

**`ManageDockerSubProjectsAsync`** (`MenuManager.cs:1093-1120`): mismo atajo, pero sobre `dockerSubProjects` (ya filtrado por `DockerfilePath` no vacío) en vez de `project.SubProjects`:

```csharp
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

        SubProject subProject;
        if (dockerSubProjects.Count == 1)
        {
            subProject = dockerSubProjects[0];
        }
        else
        {
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .TranslatedFormat("Select a Docker subproject in '{0}'", projectName)
                    .AddChoices(dockerSubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
            );

            if (subProjectName == "[seagreen1]Back to Projects[/]") return;
            subProject = dockerSubProjects.First(sp => sp.Name == subProjectName);
        }

        await ManageDockerArgsAsync(subProject, projectName);
    }
}
```

**`ManagePublishSubProjectsAsync`** (línea aproximada 1434-1457, mismo patrón que `ManageDockerSubProjectsAsync` pero sin filtrar por Dockerfile): mismo atajo aplicado directo sobre `project.SubProjects.Count == 1`.

Nota: en los 3 casos, cuando hay más de 1 resultado (multi-subproyecto real, caso raro pero posible si en el futuro se reactiva esa capacidad), el comportamiento actual con selector + cancelar se mantiene sin cambios — el atajo solo aplica al caso de 1 solo elemento.

### 5. Breadcrumbs y header sin duplicación

**Breadcrumbs `"{projectName} · {subProject.Name}"`** (`MenuManager.cs` líneas 746, 895/909 vía `ExecuteCommandSubProject`, 1009, 1027, 1199, 1233, 1257, 1571, 1578, 1622): reemplazar todas las apariciones por solo `projectName`. Estos breadcrumbs son siempre dentro de un contexto donde `subProject.Name == projectName` (auto-generado), así que no se pierde información.

Caso especial: `ExecuteSubProjectPipelineAsync` (línea 1027 original) tiene un breadcrumb de 3 niveles `"{projectName} · {subProject.Name} · {environmentName}"` — pasa a `"{projectName} · {environmentName}"` (se saca el nivel de subproyecto, se conserva el de ambiente, que sí aporta información real).

**`ShellRenderer.DrawHeader`** (`ShellRenderer.cs:19-22`):

```csharp
public static void DrawHeader(IReadOnlyDictionary<string, Project> projects, string? breadcrumb = null)
{
    var currentVersion = Util.GetCurrentVersion();

    var status = breadcrumb is null
        ? $"{projects.Count} proyectos"
        : Markup.Escape(breadcrumb);

    /* ...resto sin cambios... */
}
```

Se elimina `subProjectCount` (ya no se usa en ningún lado).

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| `ResolveDockerRegistry` cancelado (usuario responde "No" al confirm inicial) | Ninguna de las 3 llamadas persiste nada — el caller trata `null` como "no se armó el pipeline/no se hizo push a registry", mensaje claro, vuelve al menú anterior sin guardar. |
| `PromptPipelinesForSubProject`: usuario cancela TODOS los ambientes elegidos | `SubProject` se crea igual (ya pasó por nombre/path/Dockerfile), pero con `PipelinesByEnvironment` vacío — mismo estado que un subproyecto recién creado antes de este ciclo, editable después vía "Edit Pipeline". |
| Proyecto con 1 subproyecto entra a Omit Files / Manage Docker Projects | Salta directo al subproyecto único, sin preguntar — igual que ya hace `ShowSubProjectsAsync`. |
| `RemoveSubprojectsAsync` y sus 2 helpers privados | Eliminados del código — cualquier referencia externa (no debería haber ninguna fuera del switch principal) rompería la compilación, lo cual es la señal correcta de que había un caller no contemplado. |

## Testing

Sin tests — todo el alcance es `Managers/MenuManager.cs`, `Presentation/PipelineEditorMenu.cs`, `Presentation/ShellRenderer.cs`, capa Presentation/Managers basada en Spectre.Console, criterio ya establecido en todos los ciclos de esta sesión (no testeable sin acoplarse a la librería).

## Decisiones registradas

- El modelo de dominio (`SubProject` como entidad separada) no se toca — decisión ya cerrada en el ciclo de "auto-completar subproyecto único" de esta misma sesión. Este ciclo es puramente de superficie (UI/navegación).
- "Remove Subprojects" se elimina en vez de "arreglarse" — no hay forma sensata de que "eliminar el único subproyecto de un proyecto" signifique algo distinto de "eliminar el proyecto", que ya existe como opción separada y correcta.
- El atajo "saltar selector si hay 1 solo elemento" se aplica de forma consistente en los 3 lugares que lo necesitaban, replicando el patrón que `ShowSubProjectsAsync` ya usaba desde antes — no se inventa un mecanismo nuevo.
- `ExecuteCommandSubProject` pasa a mostrar SIEMPRE el menú de acciones — la ejecución directa del pipeline (que antes pasaba automáticamente) ahora es una opción explícita ("Run Pipeline") en vez de un atajo implícito basado en una condición que ya no discrimina nada (siempre verdadera).
