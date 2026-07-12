# Escenarios de deploy Docker (build remoto sin registry) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar un escenario nuevo de deploy Docker Compose que buildea directo en el servidor remoto vía `git pull` (sin Docker registry intermedio), completar la rama existente "push a registry" para que el wizard pida los datos del `DockerRegistry` inline, y permitir insertar un comando en una posición específica del pipeline (no solo al final).

**Architecture:** Un `StepType` nuevo (`DockerComposeBuild`) + su executor (mismo patrón que `DockerComposeUpExecutor`, corre `docker compose build` por SSH). Un método nuevo en `PipelineTemplateFactory` (`CreateDockerComposeRemoteBuildTemplate`) que arma un pipeline de 3 steps sin `CopyToRemote` ni `DockerImagePrune`. `MenuManager` y `PipelineEditorMenu` ganan la misma sub-pregunta ("¿cómo se buildea la imagen?") y la misma resolución de `DockerRegistry` inline — dos archivos distintos, cambios en paralelo posibles una vez que la factory esté lista.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, xUnit 2.6.6 + Moq (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-12-escenarios-deploy-docker-design.md`

---

### Task 1: `StepType.DockerComposeBuild`

**Independiente — sin dependencias. Bloquea Task 2 y Task 3.**

**Files:**
- Modify: `vali-deploy/Domain/StepType.cs`

Sin test — es un enum, se ejercita indirectamente por los tests de Task 2 y Task 3.

- [ ] **Step 1: Agregar el valor al enum**

Reemplazar el archivo completo:

```csharp
namespace vali_deploy.Domain;

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

- [ ] **Step 2: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Domain/StepType.cs
git commit -m "feat(domain): agregar StepType.DockerComposeBuild"
```

---

### Task 2: `DockerComposeBuildExecutor` + registro + tests

**Depends on:** Task 1
**Independiente de Task 3 — archivos distintos, se puede hacer en paralelo con Task 3 una vez Task 1 esté commiteado.**

**Files:**
- Create: `vali-deploy/Application/Executors/DockerComposeBuildExecutor.cs`
- Modify: `vali-deploy/CompositionRoot.cs:39-42`
- Test: `vali-deploy.Tests/Application/Executors/DockerComposeExecutorsTests.cs` (archivo existente, se agregan 3 tests)

- [ ] **Step 1: Agregar los 3 tests nuevos a `DockerComposeExecutorsTests.cs`**

Insertar `Build_runs_docker_compose_build_on_remote` inmediatamente después de `Down_runs_docker_compose_down_on_remote` (antes de `Pull_fails_fast_when_environment_has_no_remote_server`):

```csharp
    [Fact]
    public async Task Build_runs_docker_compose_build_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" build"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposeBuildExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposeBuild, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeBuild), Context());
        Assert.True(result.Success);
    }
```

Insertar `Build_fails_fast_when_environment_has_no_remote_server` inmediatamente después de `Down_fails_fast_when_environment_has_no_remote_server` (antes de `Pull_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing`):

```csharp
    [Fact]
    public async Task Build_fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new DockerComposeBuildExecutor(new Mock<ISshClientFactory>().Object);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeBuild), ContextWithoutServer());

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }
```

Insertar `Build_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing` al final de la clase, inmediatamente después de `Down_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing` (antes de la llave de cierre de la clase):

```csharp
    [Fact]
    public async Task Build_ExecuteAsync_throws_clear_error_when_ComposeFilePath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new DockerComposeBuildExecutor(sshFactory.Object);
        var step = ComposeStepWithoutArgs(StepType.DockerComposeBuild);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'DockerComposeBuild' (DockerComposeBuild) requiere Args[\"ComposeFilePath\"].", ex.Message);
        sshFactory.Verify(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), It.IsAny<string>()), Times.Never);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan por compilación**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerComposeExecutorsTests`
Expected: FAIL — `DockerComposeBuildExecutor` no existe todavía (error de compilación).

- [ ] **Step 3: Crear `DockerComposeBuildExecutor.cs`**

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

- [ ] **Step 4: Registrar el executor en `CompositionRoot.cs`**

Reemplazar (líneas 39-42):

```csharp
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
```

por:

```csharp
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeBuildExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerComposeExecutorsTests`
Expected: PASS, 12/12 (9 existentes + 3 nuevos).

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Application/Executors/DockerComposeBuildExecutor.cs vali-deploy/CompositionRoot.cs vali-deploy.Tests/Application/Executors/DockerComposeExecutorsTests.cs
git commit -m "feat(application): agregar DockerComposeBuildExecutor para build remoto sin registry"
```

---

### Task 3: `PipelineTemplateFactory.CreateDockerComposeRemoteBuildTemplate`

**Depends on:** Task 1
**Independiente de Task 2 — archivos distintos, se puede hacer en paralelo con Task 2 una vez Task 1 esté commiteado. Bloquea Task 4a y Task 4b.**

**Files:**
- Modify: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Test: `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`

- [ ] **Step 1: Agregar los 3 tests nuevos a `PipelineTemplateFactoryTests.cs`**

Insertar inmediatamente después de `DockerCompose_template_builds_RegistryTag_with_host_for_generic_registry` (antes de `LocalPublish_template_is_a_single_ZipPublishOutput_step`):

```csharp
    [Fact]
    public void CreateDockerComposeRemoteBuildTemplate_follows_step_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath: "/opt/shop-api", composeFileName: "docker-compose.yml");

        Assert.Equal(new[]
        {
            StepType.SshCommand, StepType.DockerComposeBuild, StepType.DockerComposeUp
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void CreateDockerComposeRemoteBuildTemplate_builds_git_pull_command_with_remoteDeployPath()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath: "/opt/shop-api", composeFileName: "docker-compose.yml");
        var gitStep = steps.Single(s => s.Type == StepType.SshCommand);

        Assert.Equal("cd /opt/shop-api && git pull", gitStep.Args["Command"]);
    }

    [Fact]
    public void CreateDockerComposeRemoteBuildTemplate_sets_ComposeFilePath_using_remoteDeployPath_and_composeFileName()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath: "/opt/shop-api", composeFileName: "docker-compose.yml");
        var buildStep = steps.Single(s => s.Type == StepType.DockerComposeBuild);
        var upStep = steps.Single(s => s.Type == StepType.DockerComposeUp);

        Assert.Equal("/opt/shop-api/docker-compose.yml", buildStep.Args["ComposeFilePath"]);
        Assert.Equal("/opt/shop-api/docker-compose.yml", upStep.Args["ComposeFilePath"]);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan por compilación**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: FAIL — `CreateDockerComposeRemoteBuildTemplate` no existe todavía.

- [ ] **Step 3: Agregar el método a `PipelineTemplateFactory.cs`**

Insertar inmediatamente después del cierre de `CreateDockerComposeTemplate` (antes del método privado `BuildRegistryTag`):

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

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: PASS, 18/18 (15 existentes + 3 nuevos).

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "feat(application): agregar CreateDockerComposeRemoteBuildTemplate"
```

---

### Task 4a: Sub-pregunta Docker Compose + `DockerRegistry` inline en `MenuManager`

**Depends on:** Task 3
**Independiente de Task 4b — archivos distintos, se puede hacer en paralelo con Task 4b una vez Task 3 esté commiteado.**

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`

Sin test — Presentation/Managers no testeable en este repo (criterio ya establecido en ciclos previos de esta sesión).

- [ ] **Step 1: Reemplazar `PromptSubProjectsAsync`**

Reemplazar el método completo:

```csharp
    private static async Task<List<SubProject>> PromptSubProjectsAsync(string projectPath, string projectName, List<DeployEnvironment> environments)
    {
        const string subProjectPath = ".";

        string? dockerfilePath = PromptDockerfilePath(projectPath, subProjectPath);
        var (pipelinesByEnvironment, dockerRegistry) = PromptPipelinesForSubProject(projectName, projectName, environments);

        var subProject = new SubProject
        {
            Name = projectName,
            Path = subProjectPath,
            DockerfilePath = dockerfilePath,
            PipelinesByEnvironment = pipelinesByEnvironment,
            DockerRegistry = dockerRegistry
        };

        return await Task.FromResult(new List<SubProject> { subProject });
    }
```

- [ ] **Step 2: Reemplazar `PromptPipelinesForSubProject` y agregar `ResolveDockerRegistry`**

Reemplazar el método completo `PromptPipelinesForSubProject`:

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

    /// <summary>
    /// Pide los datos de un DockerRegistry (host, usuario, token) — misma redacción que ya usa el
    /// menú legacy "Push to registry" (línea ~901), para no tener dos formas distintas de preguntar
    /// lo mismo en el mismo CLI.
    /// </summary>
    private static DockerRegistry ResolveDockerRegistry()
    {
        var username = AnsiConsole.Ask<string>("Usuario del registry (ej. tu usuario de Docker Hub):");
        var host = AnsiConsole.Ask("Host del registry (vacío = Docker Hub):", "");
        var hasToken = AnsiConsole.Confirm("¿Vas a autenticarte con un token vía variable de entorno?");
        string? tokenEnvVar = hasToken
            ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con el token:")
            : null;

        return new DockerRegistry { Host = host, Username = username, TokenEnvVar = tokenEnvVar };
    }
```

- [ ] **Step 3: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: si Task 4b todavía no se hizo, compila igual sin errores (Task 4a no depende de cambios en `PipelineEditorMenu.cs`, solo de la factory de Task 3, ya commiteada). Debe compilar limpio en cualquier orden.

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(managers): agregar sub-pregunta de build remoto vs registry y DockerRegistry inline al wizard"
```

---

### Task 4b: Sub-pregunta Docker Compose + insertar-en-posición en `PipelineEditorMenu`

**Depends on:** Task 3
**Independiente de Task 4a — archivos distintos, se puede hacer en paralelo con Task 4a una vez Task 3 esté commiteado.**

**Files:**
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

Sin test — mismo criterio que Task 4a.

- [ ] **Step 1: Reemplazar el bloque de creación de pipeline en `StartAsync`**

Reemplazar (dentro de `StartAsync`, todo el `if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName)) { ... }` actual, líneas 37-70):

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

            var isDockerCompose = template == "Docker Compose";

            if (isDockerCompose)
            {
                var composeFileName = AnsiConsole.Ask("Nombre del archivo docker-compose:", "docker-compose.yml");

                var dockerMode = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("¿Cómo se buildea la imagen?")
                        .AddChoices("Build directo en el servidor (sin registry)", "Push a un registry"));

                var isRemoteBuild = dockerMode == "Build directo en el servidor (sin registry)";

                var confirmMessage = isRemoteBuild
                    ? $"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con build directo en el servidor, path remoto '{remoteDeployPath}' y archivo '{composeFileName}'?"
                    : $"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con push a registry, path remoto '{remoteDeployPath}' y archivo '{composeFileName}'?";

                var confirmed = AnsiConsole.Confirm(confirmMessage, true);
                if (!confirmed)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
                    return;
                }

                var factory = new PipelineTemplateFactory();
                if (isRemoteBuild)
                {
                    configSubProject.PipelinesByEnvironment[environmentName] = factory.CreateDockerComposeRemoteBuildTemplate(remoteDeployPath, composeFileName);
                }
                else
                {
                    configSubProject.DockerRegistry ??= ResolveDockerRegistry();
                    configSubProject.PipelinesByEnvironment[environmentName] = factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, remoteDeployPath, composeFileName, configSubProject.DockerRegistry);
                }
            }
            else
            {
                var confirmed = AnsiConsole.Confirm($"¿Crear el pipeline de '{configSubProject.Name}' en '{environmentName}' con la plantilla '{template}' y path remoto '{remoteDeployPath}'?", true);
                if (!confirmed)
                {
                    AnsiConsole.MarkupLine("[yellow]Cancelado. No se creó ningún pipeline.[/]");
                    return;
                }

                var factory = new PipelineTemplateFactory();
                configSubProject.PipelinesByEnvironment[environmentName] = factory.CreatePublishZipTemplate(projectName, configSubProject.Name, remoteDeployPath, configSubProject.OmitFiles);
            }

            repository.Save(config);
        }
```

- [ ] **Step 2: Agregar `ResolveDockerRegistry` como método privado de la clase**

Insertar antes del cierre de la clase `PipelineEditorMenu` (después de `EditStepArgs`):

```csharp
    /// <summary>
    /// Pide los datos de un DockerRegistry (host, usuario, token) — misma redacción que ya usa el
    /// menú legacy "Push to registry" en MenuManager.cs, para no tener dos formas distintas de
    /// preguntar lo mismo en el mismo CLI.
    /// </summary>
    private static DockerRegistry ResolveDockerRegistry()
    {
        var username = AnsiConsole.Ask<string>("Usuario del registry (ej. tu usuario de Docker Hub):");
        var host = AnsiConsole.Ask("Host del registry (vacío = Docker Hub):", "");
        var hasToken = AnsiConsole.Confirm("¿Vas a autenticarte con un token vía variable de entorno?");
        string? tokenEnvVar = hasToken
            ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con el token:")
            : null;

        return new DockerRegistry { Host = host, Username = username, TokenEnvVar = tokenEnvVar };
    }
```

- [ ] **Step 3: Insertar en posición — reemplazar `case "Insert RawCommand"` en `EditStepsAsync`**

Reemplazar:

```csharp
                case "Insert RawCommand":
                    var command = AnsiConsole.Ask<string>("Comando a insertar:");
                    steps.Add(new DeployStep { Type = StepType.RawCommand, Name = command, Args = { ["Command"] = command } });
                    repository.Save(config);
                    break;
```

por:

```csharp
                case "Insert RawCommand":
                    var command = AnsiConsole.Ask<string>("Comando a insertar:");
                    var newStep = new DeployStep { Type = StepType.RawCommand, Name = command, Args = { ["Command"] = command } };
                    var insertIndex = PromptInsertPosition(steps);
                    steps.Insert(insertIndex, newStep);
                    repository.Save(config);
                    break;
```

- [ ] **Step 4: Agregar `PromptInsertPosition`**

Insertar como método privado nuevo, antes de `EditStepArgs`:

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

- [ ] **Step 5: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: compila limpio en cualquier orden respecto a Task 4a (mismo criterio que Task 4a Step 3).

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "feat(presentation): agregar sub-pregunta de build remoto vs registry e insertar-en-posicion al pipeline editor"
```

---

### Task 5: Build final + verificación manual

**Depends on:** Task 1, Task 2, Task 3, Task 4a, Task 4b (todos commiteados)

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Build y test suite completos**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, 168/168 (153 previos + 12 de `DockerComposeExecutorsTests` menos 9 ya contados = +3 netos + 18 de `PipelineTemplateFactoryTests` menos 15 ya contados = +3 netos → 153 + 3 + 3 = 159; verificar el conteo exacto real en el output de `dotnet test`, no asumir el número — este plan puede tener un off-by-N si algún test previo cambió de nombre en un ciclo anterior).

- [ ] **Step 2: Verificar el escenario "build en el servidor" con datos reales**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Para el subproyecto `acity-caf-api-migracion-audiencia` (o uno de prueba), entrar a "Pipeline Editor", elegir un ambiente sin pipeline, plantilla "Docker Compose", nombre de archivo `docker-compose.yml`, y en "¿Cómo se buildea la imagen?" elegir "Build directo en el servidor (sin registry)". Confirmar que el pipeline resultante tiene exactamente 3 steps: "Actualizar código", "Compose build", "Compose up" — sin "Copiar compose.yml" ni "Limpiar imágenes viejas".

- [ ] **Step 3: Verificar la rama "Push a un registry"**

Repetir para otro ambiente, eligiendo "Push a un registry". Confirmar que pide Usuario/Host/Token del registry, y que el pipeline resultante es el de siempre (Checkout, Build imagen, Push a registry, Copiar compose.yml, Compose pull, Compose up, Limpiar imágenes viejas).

- [ ] **Step 4: Verificar que no vuelve a preguntar el registry dentro de la misma corrida**

Si en el wizard fusionado de "Add Project" el mismo subproyecto elige "Push a un registry" para dos ambientes distintos en la misma corrida, confirmar que solo pregunta los datos del registry una vez.

- [ ] **Step 5: Verificar insertar-en-posición**

En "Pipeline Editor" → "Insert RawCommand", confirmar que aparece la lista "Antes de 'X'" por cada step existente más "Al final", y que el comando queda insertado en la posición elegida (no siempre al final).

Si cualquiera de estos pasos falla, corregir el código en el task correspondiente y volver a compilar/testear antes de continuar.

---

## Self-review

**Cobertura de la spec:** las 5 secciones del spec (StepType nuevo, executor nuevo, plantilla de build remoto, sub-pregunta + DockerRegistry inline, insertar en posición) están cubiertas por Task 1, Task 2, Task 3, Task 4a+4b, y Task 4b respectivamente.

**Consistencia de tipos:** `CreateDockerComposeRemoteBuildTemplate(string remoteDeployPath, string composeFileName)` se llama con los mismos 2 argumentos posicionales en Task 3 (tests), Task 4a (`MenuManager`) y Task 4b (`PipelineEditorMenu`). `ResolveDockerRegistry()` sin parámetros, mismo nombre y forma en ambos archivos de Task 4a/4b (implementaciones separadas ya que son clases `static` distintas sin herencia compartida — duplicación aceptada, ver nota abajo). `DockerComposeBuildExecutor` implementa `IStepExecutor` con `Handles => StepType.DockerComposeBuild`, coherente entre Task 2 (executor) y Task 3 (steps que lo referencian).

**Duplicación aceptada:** `ResolveDockerRegistry()` queda duplicado entre `MenuManager.cs` y `PipelineEditorMenu.cs` (mismo cuerpo, dos métodos privados en clases `static` distintas). No se extrae a un helper compartido en este ciclo — ambas clases ya duplican otros patrones similares entre sí (ver `ResolveDefaultRemoteDeployPath` que sí es compartido porque vive en `PipelineTemplateFactory`, una clase de Application sin estado de Presentation). Extraer esto a un helper de Presentation compartido es una mejora válida pero fuera de alcance de este ciclo — no lo pide la spec.

**Sin placeholders:** todos los steps tienen código completo, sin TBD.
