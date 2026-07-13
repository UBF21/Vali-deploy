# Limpieza de navegación del menú (Ciclo B) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** "Edit Pipeline" deja de estar escondido detrás de una condición siempre-verdadera, se agrega cancelar a los flujos que no lo tenían, se saca "Remove Subprojects" del menú (ya no tiene sentido con 1:1 Proyecto↔SubProyecto), se colapsan 3 selectores de subproyecto redundantes, y se limpian breadcrumbs/header duplicados.

**Architecture:** Todo el alcance vive en 3 archivos de Presentation/Managers. `MenuManager.cs` concentra 5 de los 6 cambios (mismo archivo, un único task secuencial). `PipelineEditorMenu.cs` y `ShellRenderer.cs` son cambios independientes y chicos, en paralelo con el de `MenuManager.cs`.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-12-limpieza-navegacion-menu-design.md`

---

### Task 1: `MenuManager.cs` — bug crítico, cancelar, sacar Remove Subprojects, colapsar selectores, breadcrumbs

**Independiente de Task 2 y 3 (archivos distintos), pero es un ÚNICO task secuencial internamente — no paralelizable dentro de sí mismo, es el mismo archivo con 6 cambios dispersos.**

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`

Sin test — Presentation/Managers no testeable en este repo (criterio ya establecido en todos los ciclos de esta sesión).

- [ ] **Step 1: Fix del bug crítico — `ExecuteCommandSubProject` siempre muestra el menú de acciones**

Reemplazar el método completo (buscar `private static async Task ExecuteCommandSubProject`):

```csharp
    private static async Task ExecuteCommandSubProject(Project project, SubProject? subProject, string projectName)
    {
        if (subProject == null) return;

        var repository = CompositionRoot.CreateProjectRepository();
        var config = repository.Load();
        var configSubProject = config.Projects[projectName].SubProjects.First(s => s.Name == subProject.Name);

        string subProjectPathFull = Path.Combine(project.Path, subProject.Path);
        string imageTag = $"{projectName.ToLower()}-{subProject.Name.ToLower()}:latest";

        var choices = new List<string>();
        if (configSubProject.PipelinesByEnvironment.Count > 0)
        {
            choices.Add("Run Pipeline");
        }
        choices.Add("Generate Microsoft publish");
        if (!string.IsNullOrEmpty(subProject.DockerfilePath))
        {
            choices.AddRange(_dockerActions);
        }
        choices.Add("Edit Pipeline");
        choices.Add("[seagreen1]Back to Projects[/]");

        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Translated("What do you want to do?")
                .AddChoices(choices)
        );

        if (action == "Generate Microsoft publish" || _dockerActions.Contains(action))
        {
            AnsiConsole.Clear();
            Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: projectName);
        }

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
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    var buildArgs = subProject.DockerBuildArgs is { Count: > 0 } ? string.Join(" ", subProject.DockerBuildArgs) : "";
                    var steps = new Application.PipelineTemplateFactory().CreateLocalDockerBuildTemplate(subProject.DockerfilePath, imageTag, buildArgs);
                    await RunLocalPipelineAsync(project, subProject, projectName, steps);
                }

                break;

            case "Docker Run":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    var runArgs = subProject.DockerRunArgs is { Count: > 0 } ? string.Join(" ", subProject.DockerRunArgs) : "";
                    var steps = new Application.PipelineTemplateFactory().CreateLocalDockerRunTemplate(imageTag, runArgs);
                    await RunLocalPipelineAsync(project, subProject, projectName, steps);
                }

                break;

            case "Push to registry":
                if (!string.IsNullOrEmpty(subProject.DockerfilePath))
                {
                    if (subProject.DockerRegistry == null || string.IsNullOrEmpty(subProject.DockerRegistry.Username))
                    {
                        var registry = ResolveDockerRegistry();
                        if (registry == null)
                        {
                            AnsiConsole.MarkupLine("[yellow]Cancelado. No se configuró el registry.[/]");
                            break;
                        }

                        subProject.DockerRegistry = registry;
                        PersistProjects();
                    }

                    var steps = new Application.PipelineTemplateFactory().CreateLocalDockerPushTemplate(imageTag, subProject.DockerRegistry);
                    await RunLocalPipelineAsync(project, subProject, projectName, steps);
                }

                break;

            case "[seagreen1]Back to Projects[/]":
                return;
        }
    }
```

Cambios respecto al original: (a) el `if (configSubProject.PipelinesByEnvironment.Count > 0) { ...; return; }` que desviaba automáticamente ANTES de construir el menú desaparece — ahora `PipelinesByEnvironment.Count > 0` solo decide si aparece el choice `"Run Pipeline"`; (b) el prompt ya no nombra "subproject" (`"What do you want to do?"` en vez de `"What do you want to do with subproject '{0}'?"`); (c) `"[seagreen1]Back to Subprojects[/]"` → `"[seagreen1]Back to Projects[/]"`; (d) la rama `"Push to registry"` usa el `ResolveDockerRegistry()` compartido (nullable, cancelable — ver Step 3) en vez de las 3 preguntas inline.

- [ ] **Step 2: `ExecuteSubProjectPipelineAsync` — breadcrumb sin nivel de subproyecto**

Buscar `private static async Task ExecuteSubProjectPipelineAsync` y reemplazar las 2 líneas de breadcrumb:

```csharp
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name}");
```

(primera aparición, justo después de `AnsiConsole.Clear();` al principio del método) por:

```csharp
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: projectName);
```

y

```csharp
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name} · {environmentName}");
```

(segunda aparición, después de resolver `environmentName`) por:

```csharp
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {environmentName}");
```

- [ ] **Step 3: `ResolveDockerRegistry` — nullable, con cancelar**

Buscar `private static DockerRegistry ResolveDockerRegistry` (el método privado compartido, distinto de la copia de `PipelineEditorMenu.cs`) y reemplazarlo completo:

```csharp
    /// <summary>
    /// Pide los datos de un DockerRegistry (host, usuario, token) — misma redacción que ya usa el
    /// menú legacy "Push to registry". Devuelve null si el usuario decide no configurarlo ahora.
    /// </summary>
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

- [ ] **Step 4: `PromptPipelinesForSubProject` — cancelar por ambiente**

Buscar `private static (Dictionary<string, List<Domain.DeployStep>> Pipelines, DockerRegistry? DockerRegistry) PromptPipelinesForSubProject` y reemplazar el método completo:

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
                new SelectionPrompt<string>().Title($"Plantilla inicial para '{environmentName}':")
                    .AddChoices("Docker Compose", "Publish/Zip", "[seagreen1]Cancelar este ambiente[/]"));

            if (template == "[seagreen1]Cancelar este ambiente[/]")
            {
                continue;
            }

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
                    if (dockerRegistry == null)
                    {
                        AnsiConsole.MarkupLine($"[yellow]Cancelado. No se configuró el registry para '{environmentName}'.[/]");
                        continue;
                    }

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

- [ ] **Step 5: Sacar "Remove Subprojects" del menú principal**

En `GetMainMenuOption()`, quitar `"Remove Subprojects", ` de la lista de `AddChoices(...)`.

En el switch de `StartAsync()`, quitar el bloque:

```csharp
                case "Remove Subprojects":
                    await RemoveSubprojectsAsync();
                    UpdateProjectsAndChart();
                    break;
```

(el texto exacto puede variar levemente — buscar el `case` que invoca `RemoveSubprojectsAsync()` y borrarlo completo).

Borrar los 3 métodos que quedan sin ningún caller: `RemoveSubprojectsAsync`, `PromptProjectSelectionForSubprojectRemoval`, `PromptMultipleSubProjectSelection` (buscar cada `private static` de esos 3 nombres y borrar el método completo, incluido su comentario XML doc si lo tiene).

- [ ] **Step 6: Colapsar `SelectSubProjectAsync` cuando hay 1 subproyecto**

Buscar `private static async Task<SubProject?> SelectSubProjectAsync` y reemplazar el método completo:

```csharp
    private static async Task<SubProject?> SelectSubProjectAsync(Project project, string projectName)
    {
        if (project.SubProjects.Count == 0)
        {
            AnsiConsole.MarkupLine(
                $"[yellow]:warning: No subprojects found for project '{Markup.Escape(projectName)}'.[/]");
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

        var foundSubProject = project.SubProjects.FirstOrDefault(sp => sp.Name == subProjectName);
        if (foundSubProject == null)
        {
            AnsiConsole.MarkupLine("[red]:cross_mark: Subproject not found.[/]");
        }

        return foundSubProject;
    }
```

- [ ] **Step 7: Colapsar `ManageDockerSubProjectsAsync` cuando hay 1 subproyecto con Dockerfile**

Buscar `private static async Task ManageDockerSubProjectsAsync` y reemplazar el método completo:

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
                AnsiConsole.MarkupLine(
                    $"[yellow]:warning: No subprojects with Dockerfiles in '{Markup.Escape(projectName)}'.[/]");
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

- [ ] **Step 8: Colapsar `ManagePublishSubProjectsAsync` cuando hay 1 subproyecto**

Buscar `private static async Task ManagePublishSubProjectsAsync` y reemplazar el método completo:

```csharp
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

            SubProject subProject;
            if (project.SubProjects.Count == 1)
            {
                subProject = project.SubProjects[0];
            }
            else
            {
                var subProjectName = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .TranslatedFormat("Select a subproject in '{0}' to manage publish arguments", projectName)
                        .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
                );

                if (subProjectName == "[seagreen1]Back to Projects[/]") return;
                subProject = project.SubProjects.First(sp => sp.Name == subProjectName);
            }

            await ManagePublishArgsAsync(subProject, projectName);
        }
    }
```

- [ ] **Step 9: Limpiar el resto de los breadcrumbs `"{projectName} · {subProject.Name}"` duplicados**

Correr `grep -n '{projectName} · {subProject.Name}' vali-deploy/Managers/MenuManager.cs` (o el patrón equivalente con `subProject.Name`/`{0}` según el método) para encontrar TODAS las apariciones restantes que no se tocaron en los Steps 1-2 (deberían quedar unas 6-8, en métodos como `DisplayOmitFilesFromPublish`, `DisplayDockerArgs`, `ManageDockerArgsAsync`, `DisplayPublishArgs`, `ManagePublishArgsAsync`, etc.). Para cada una, reemplazar `$"{projectName} · {subProject.Name}"` por `projectName` a secas (son siempre el mismo valor, ver spec sección 5). No tocar breadcrumbs que ya incluyan un tercer nivel real (ej. nombre de ambiente, nombre de un Docker arg específico) — esos conservan su nivel adicional, solo se saca la parte redundante del subproyecto.

- [ ] **Step 10: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

- [ ] **Step 11: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "fix(managers): mostrar siempre Edit Pipeline, agregar cancelar, sacar Remove Subprojects y colapsar selectores redundantes"
```

---

### Task 2: `PipelineEditorMenu.cs` — `ResolveDockerRegistry` nullable + su call site

**Depends on:** ninguno
**Independiente de Task 1 y 3 — archivo distinto, en paralelo.**

**Files:**
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

Sin test — mismo criterio que Task 1.

- [ ] **Step 1: `ResolveDockerRegistry` nullable con Confirm inicial**

Buscar `private static DockerRegistry ResolveDockerRegistry` en este archivo y reemplazarlo completo:

```csharp
    /// <summary>
    /// Pide los datos de un DockerRegistry (host, usuario, token) — misma redacción que ya usa el
    /// menú legacy "Push to registry" en MenuManager.cs. Devuelve null si el usuario decide no
    /// configurarlo ahora.
    /// </summary>
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

- [ ] **Step 2: El call site maneja el `null`**

Buscar la línea `configSubProject.DockerRegistry ??= ResolveDockerRegistry();` dentro de `StartAsync` (rama `"Push a un registry"` de la sub-pregunta Docker Compose) y reemplazar el bloque que la contiene por:

```csharp
                configSubProject.DockerRegistry ??= ResolveDockerRegistry();
                if (configSubProject.DockerRegistry == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelado. No se configuró el registry.[/]");
                    return;
                }

                configSubProject.PipelinesByEnvironment[environmentName] = factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, remoteDeployPath, composeFileName!, configSubProject.DockerRegistry);
```

(mantener sin cambios el resto del bloque `if (isDockerCompose) { ... }` — solo agregar el chequeo de `null` inmediatamente después de la línea `??=` existente, antes de usar `configSubProject.DockerRegistry` para crear el pipeline).

- [ ] **Step 3: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: si Task 1/3 todavía no terminaron, puede haber errores en otros archivos — confirmá que NINGUNO está en `PipelineEditorMenu.cs`.

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "fix(presentation): permitir cancelar la configuracion de DockerRegistry en el pipeline editor"
```

---

### Task 3: `ShellRenderer.cs` — sacar el contador de subproyectos duplicado

**Depends on:** ninguno
**Independiente de Task 1 y 2 — archivo distinto y trivial, en paralelo.**

**Files:**
- Modify: `vali-deploy/Presentation/ShellRenderer.cs`

Sin test — mismo criterio que Task 1.

- [ ] **Step 1: Sacar `subProjectCount` del header**

Reemplazar:

```csharp
        var currentVersion = Util.GetCurrentVersion();
        var subProjectCount = projects.Values.Sum(p => p.SubProjects.Count);

        var status = breadcrumb is null
            ? $"{projects.Count} proyectos · {subProjectCount} subproyectos"
            : Markup.Escape(breadcrumb);
```

por:

```csharp
        var currentVersion = Util.GetCurrentVersion();

        var status = breadcrumb is null
            ? $"{projects.Count} proyectos"
            : Markup.Escape(breadcrumb);
```

- [ ] **Step 2: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores (este archivo no depende de nada de Task 1/2).

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/ShellRenderer.cs
git commit -m "fix(presentation): sacar el contador de subproyectos duplicado del header"
```

---

### Task 4: Build final + verificación manual

**Depends on:** Task 1, 2, 3 (todos commiteados)

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Build y test suite completos**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, 162/162 (sin tests nuevos en este ciclo, Presentation/Managers no testeable).

- [ ] **Step 2: Verificar el fix del bug crítico**

`dotnet run` → "Show Projects" → elegir un proyecto con pipeline ya configurado. Confirmar que aparece el menú de acciones con **"Run Pipeline"** y **"Edit Pipeline"** ambos visibles (antes, solo se podía ejecutar, nunca editar).

- [ ] **Step 3: Verificar cancelar en el wizard de alta**

"Add Project" → crear un proyecto de prueba → en la selección de plantilla de un ambiente, elegir "Cancelar este ambiente". Confirmar que no rompe el flujo y el subproyecto se crea igual (sin pipeline para ese ambiente, o vacío si se canceló el único elegido).

- [ ] **Step 4: Verificar que "Remove Subprojects" ya no aparece**

Menú principal → confirmar que la opción no está en la lista.

- [ ] **Step 5: Verificar los selectores colapsados**

"Configure Publish File Omissions" y "Manage Docker Projects" sobre un proyecto con 1 solo subproyecto (el caso normal) → confirmar que NO pregunta "elegí el subproyecto", salta directo.

- [ ] **Step 6: Verificar breadcrumbs**

Recorrer 2-3 pantallas distintas (Show Projects, Manage Docker Projects, Configure Publish File Omissions) y confirmar que ningún breadcrumb muestra el nombre del proyecto duplicado (`"X · X"`), y que el header del menú principal dice `"N proyectos"` sin el conteo de subproyectos al lado.

Si cualquiera de estos pasos falla, corregir el código en el task correspondiente y volver a compilar antes de continuar.

---

## Self-review

**Cobertura de la spec:** las 5 secciones de la spec (bug crítico, cancelar, sacar Remove Subprojects, colapsar selectores, breadcrumbs/header) están cubiertas por Task 1 Steps 1/4 (bug+cancelar wizard), Step 3 (ResolveDockerRegistry MenuManager), Step 5 (sacar Remove Subprojects), Steps 6-8 (colapsar 3 selectores), Steps 2/9 (breadcrumbs); Task 2 (ResolveDockerRegistry PipelineEditorMenu); Task 3 (header).

**Consistencia de tipos:** `ResolveDockerRegistry()` tiene la misma firma nueva (`DockerRegistry?`, sin parámetros, `Confirm` inicial) en ambos archivos (Task 1 Step 3, Task 2 Step 1) — duplicación intencional entre dos clases `static` sin herencia compartida, ya aceptada como decisión en el ciclo anterior de "escenarios de deploy Docker".

**Riesgo de coordinación:** Task 1 es grande (11 steps, mismo archivo) — debe ejecutarlo un único subagente de punta a punta, sin paralelizarse internamente. Task 2 y 3 pueden despacharse en paralelo con Task 1 (archivos distintos, sin overlap). Dado un incidente real de coordinación de git entre agentes paralelos en el ciclo anterior de esta misma sesión (commits pisados y recuperados vía reflog), cada task debe hacer `git add` con paths explícitos de SUS archivos únicamente antes de cada commit, nunca `git add -A`/`git add .`.

**Sin placeholders:** todos los steps tienen código completo. El Step 9 de Task 1 (breadcrumbs restantes) usa una instrucción de "buscar con grep y reemplazar el patrón" en vez de listar cada line number exacto — es la única ambigüedad intencional, porque el patrón es mecánico y repetitivo (mismo find-and-replace en ~6-8 lugares) y los line numbers exactos no se relevaron uno por uno en la exploración previa.
