# Asociación obligatoria Proyecto/SubProyecto ↔ Ambiente Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ningún SubProyecto puede existir sin al menos un Ambiente asociado (gate al crear Proyecto + wizard fusionado de alta), el path remoto de deploy se resuelve y confirma por SubProyecto+Ambiente (no un único override por Ambiente), y se corrige un bug preexistente donde la plantilla Publish/Zip generaba un step "Copiar zip al remoto" que explotaba en runtime por falta de `LocalPath`/`RemotePath`.

**Architecture:** Cuatro cambios en capas distintas, con dependencia de orden pero sin overlap de archivos entre sí: (1) `Application/Executors` gana un mecanismo de "artifact entre steps" vía una propiedad mutable en `StepExecutionContext`, consumida por `CopyToRemoteExecutor` y producida por `ZipPublishExecutor`; (2) `PipelineTemplateFactory` deja de calcular el path remoto internamente desde `DeployEnvironment` y lo recibe ya resuelto, ganando un helper estático puro para la convención por defecto; (3) `MenuManager` agrega el gate y el wizard fusionado de alta; (4) `PipelineEditorMenu` agrega el mismo prompt de path remoto para consistencia. (3) y (4) dependen de la firma nueva de (2) pero no se tocan entre sí — se pueden implementar en paralelo una vez (2) esté commiteado. (1) es independiente de todo el resto y se puede hacer en paralelo con (2).

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, xUnit 2.6.6 + Moq (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-11-asociacion-proyecto-ambiente-design.md`

---

### Task 1: Artifact entre steps (`StepExecutionContext` + `ZipPublishExecutor` + `CopyToRemoteExecutor`)

**Independiente — sin dependencias de otros tasks, se puede hacer en paralelo con Task 2.**

**Files:**
- Modify: `vali-deploy/Application/StepExecutionContext.cs`
- Modify: `vali-deploy/Application/Executors/ZipPublishExecutor.cs`
- Modify: `vali-deploy/Application/Executors/CopyToRemoteExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/CopyToRemoteExecutorTests.cs`
- Test: `vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs`

- [ ] **Step 1: Escribir los tests nuevos/modificados de `CopyToRemoteExecutorTests`**

Reemplazar el test `ExecuteAsync_throws_clear_error_when_LocalPath_arg_missing` (líneas 90-105 del archivo actual) por estos dos tests:

```csharp
    [Fact]
    public async Task ExecuteAsync_falls_back_to_context_LastArtifactPath_when_LocalPath_arg_missing()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/sub-20260101.zip", "/opt/app/sub.zip"))
            .Returns(Task.CompletedTask);

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy zip",
            Args = { ["RemotePath"] = "/opt/app/sub.zip" }
        };
        var context = Context();
        context.LastArtifactPath = "/tmp/proj/sub-20260101.zip";

        var result = await executor.ExecuteAsync(step, context);

        Assert.True(result.Success);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/sub-20260101.zip", "/opt/app/sub.zip"), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_throws_when_LocalPath_arg_missing_and_no_LastArtifactPath_in_context()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => executor.ExecuteAsync(step, Context()));

        Assert.Equal("El paso 'copy compose' (CopyToRemote) requiere Args[\"LocalPath\"] o un step anterior que produzca un artifact (ej. ZipPublishOutput).", ex.Message);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
```

El test `ExecuteAsync_throws_clear_error_when_RemotePath_arg_missing` (líneas 107-122) queda sin cambios.

- [ ] **Step 2: Escribir los tests nuevos de `ZipPublishExecutorTests`**

Agregar al final de la clase, antes de la última llave de cierre:

```csharp
    [Fact]
    public async Task Sets_context_LastArtifactPath_to_the_created_zip_path_on_success()
    {
        var publishFolder = CreateFakePublishFolder(out var projectPath);
        var processRunner = SuccessfulBuildRunner(projectPath);

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };
        var context = Context(projectPath, "sub");

        var result = await executor.ExecuteAsync(step, context);

        Assert.True(result.Success);
        var zipFiles = Directory.EnumerateFiles(Path.GetDirectoryName(publishFolder)!, "sub-*.zip").ToList();
        Assert.Single(zipFiles);
        Assert.Equal(zipFiles[0], context.LastArtifactPath);
    }

    [Fact]
    public async Task Does_not_set_context_LastArtifactPath_when_build_fails()
    {
        var _ = CreateFakePublishFolder(out var projectPath);
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), projectPath, null, null))
            .ReturnsAsync(new ProcessRunResult(1, "", "build error"));

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };
        var context = Context(projectPath);

        var result = await executor.ExecuteAsync(step, context);

        Assert.False(result.Success);
        Assert.Null(context.LastArtifactPath);
    }
```

- [ ] **Step 3: Correr los tests y verificar que fallan por compilación**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "CopyToRemoteExecutorTests|ZipPublishExecutorTests"`
Expected: FAIL — `context.LastArtifactPath` no existe todavía en `StepExecutionContext` (error de compilación).

- [ ] **Step 4: Agregar `LastArtifactPath` a `StepExecutionContext`**

Reemplazar el archivo completo `vali-deploy/Application/StepExecutionContext.cs`:

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public class StepExecutionContext
{
    public required string ProjectName { get; init; }
    public required string SubProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required DeployEnvironment Environment { get; init; }
    public string? LastArtifactPath { get; set; }
}
```

- [ ] **Step 5: `ZipPublishExecutor` escribe `context.LastArtifactPath` al terminar con éxito**

En `vali-deploy/Application/Executors/ZipPublishExecutor.cs`, reemplazar las líneas 49-54:

```csharp
        var omitFiles = ParseOmitFiles(step);
        var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
        combinedOutput.AppendLine($"Comprimido en: {zipPath}");

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
```

por:

```csharp
        var omitFiles = ParseOmitFiles(step);
        var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
        combinedOutput.AppendLine($"Comprimido en: {zipPath}");
        context.LastArtifactPath = zipPath;

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
```

- [ ] **Step 6: `CopyToRemoteExecutor` cae a `context.LastArtifactPath` cuando falta `Args["LocalPath"]`**

En `vali-deploy/Application/Executors/CopyToRemoteExecutor.cs`, reemplazar las líneas 25-33:

```csharp
        if (!step.Args.TryGetValue("LocalPath", out var localPath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"LocalPath\"].");
        }

        if (!step.Args.TryGetValue("RemotePath", out var remotePath))
        {
            throw new InvalidOperationException($"El paso '{step.Name}' ({step.Type}) requiere Args[\"RemotePath\"].");
        }
```

por:

```csharp
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

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "CopyToRemoteExecutorTests|ZipPublishExecutorTests"`
Expected: PASS, todos los tests de ambas clases (las existentes sin tocar siguen pasando, más los 3 nuevos/renombrados).

Correr también la suite completa: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` — no debe haber regresiones en otros archivos.

- [ ] **Step 8: Commit**

```bash
git add vali-deploy/Application/StepExecutionContext.cs vali-deploy/Application/Executors/ZipPublishExecutor.cs vali-deploy/Application/Executors/CopyToRemoteExecutor.cs vali-deploy.Tests/Application/Executors/CopyToRemoteExecutorTests.cs vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs
git commit -m "fix(application): propagar el path del artifact entre steps para CopyToRemote"
```

---

### Task 2: `PipelineTemplateFactory` — path remoto explícito

**Independiente de Task 1. Task 3 y Task 4 dependen de este task (firma nueva de la factory).**

**Files:**
- Modify: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Test: `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`

- [ ] **Step 1: Reemplazar `PipelineTemplateFactoryTests.cs` completo**

```csharp
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class PipelineTemplateFactoryTests
{
    private static DeployEnvironment Environment(string? remoteDeployPath = null) =>
        new() { Name = "PROD", RemoteDeployPath = remoteDeployPath };

    [Fact]
    public void DockerCompose_template_follows_spec_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "shop", subProjectName: "api", remoteDeployPath: "/opt/shop-api");

        Assert.Equal(new[]
        {
            StepType.GitCheckout, StepType.DockerBuild, StepType.DockerPush, StepType.CopyToRemote,
            StepType.DockerComposePull, StepType.DockerComposeUp, StepType.DockerImagePrune
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void PublishZip_template_follows_spec_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreatePublishZipTemplate(projectName: "shop", subProjectName: "api", remoteDeployPath: "/opt/shop-api");

        Assert.Equal(new[]
        {
            StepType.GitCheckout, StepType.ZipPublishOutput,
            StepType.CopyToRemote, StepType.SshCommand, StepType.SshCommand
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void DockerCompose_template_sets_ImageTag_using_project_and_subproject_name()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var buildStep = steps.Single(s => s.Type == StepType.DockerBuild);

        Assert.Equal("shop-api:latest", buildStep.Args["ImageTag"]);
    }

    [Fact]
    public void DockerCompose_template_uses_the_given_remoteDeployPath_verbatim()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/srv/apps/legacy-name");
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);
        var pullStep = steps.Single(s => s.Type == StepType.DockerComposePull);
        var upStep = steps.Single(s => s.Type == StepType.DockerComposeUp);

        Assert.Equal("/srv/apps/legacy-name/compose.yml", copyStep.Args["RemotePath"]);
        Assert.Equal("/srv/apps/legacy-name/compose.yml", pullStep.Args["ComposeFilePath"]);
        Assert.Equal("/srv/apps/legacy-name/compose.yml", upStep.Args["ComposeFilePath"]);
    }

    [Fact]
    public void PublishZip_template_sets_RemotePath_on_CopyToRemote_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreatePublishZipTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var copyStep = steps.Single(s => s.Type == StepType.CopyToRemote);

        Assert.Equal("/opt/shop-api/api.zip", copyStep.Args["RemotePath"]);
    }

    [Fact]
    public void ResolveDefaultRemoteDeployPath_uses_opt_convention_when_environment_has_no_override()
    {
        var path = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath("Shop", "Api", Environment());

        Assert.Equal("/opt/shop-api", path);
    }

    [Fact]
    public void ResolveDefaultRemoteDeployPath_uses_environment_RemoteDeployPath_when_set()
    {
        var path = PipelineTemplateFactory.ResolveDefaultRemoteDeployPath("Shop", "Api", Environment("/srv/apps/legacy-name"));

        Assert.Equal("/srv/apps/legacy-name", path);
    }

    [Fact]
    public void DockerCompose_template_falls_back_to_bare_imageTag_when_no_registry_configured()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api");
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_for_docker_hub_when_host_is_empty()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api", dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("myuser/shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_with_host_for_generic_registry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", remoteDeployPath: "/opt/shop-api", dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("ghcr.io/myorg/shop-api:latest", pushStep.Args["RegistryTag"]);
        Assert.Equal("ghcr.io", pushStep.Args["RegistryHost"]);
        Assert.Equal("myorg", pushStep.Args["RegistryUsername"]);
        Assert.Equal("GHCR_TOKEN", pushStep.Args["RegistryTokenEnvVar"]);
    }

    [Fact]
    public void LocalPublish_template_is_a_single_ZipPublishOutput_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalPublishTemplate(omitFiles: new List<string>());

        Assert.Single(steps);
        Assert.Equal(StepType.ZipPublishOutput, steps[0].Type);
        Assert.Equal("", steps[0].Args["OmitFiles"]);
    }

    [Fact]
    public void LocalPublish_template_encodes_OmitFiles_pipe_delimited()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalPublishTemplate(omitFiles: new List<string> { "a.txt", "b.txt" });

        Assert.Equal("a.txt|b.txt", steps[0].Args["OmitFiles"]);
    }

    [Fact]
    public void LocalDockerBuild_template_is_a_single_DockerBuild_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalDockerBuildTemplate(dockerfilePath: "Dockerfile", imageTag: "shop-api:latest", buildArgs: "--build-arg X=1");

        Assert.Single(steps);
        Assert.Equal(StepType.DockerBuild, steps[0].Type);
        Assert.Equal("Dockerfile", steps[0].Args["Dockerfile"]);
        Assert.Equal("shop-api:latest", steps[0].Args["ImageTag"]);
        Assert.Equal("--build-arg X=1", steps[0].Args["BuildArgs"]);
    }

    [Fact]
    public void LocalDockerPush_template_builds_RegistryTag_from_DockerRegistry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateLocalDockerPushTemplate(imageTag: "shop-api:latest", dockerRegistry: registry);

        Assert.Single(steps);
        Assert.Equal(StepType.DockerPush, steps[0].Type);
        Assert.Equal("myuser/shop-api:latest", steps[0].Args["RegistryTag"]);
    }

    [Fact]
    public void LocalDockerRun_template_is_a_single_DockerRun_step()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateLocalDockerRunTemplate(imageTag: "shop-api:latest", runArgs: "-p 8080:80");

        Assert.Single(steps);
        Assert.Equal(StepType.DockerRun, steps[0].Type);
        Assert.Equal("shop-api:latest", steps[0].Args["ImageTag"]);
        Assert.Equal("-p 8080:80", steps[0].Args["RunArgs"]);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan por compilación**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: FAIL — `CreateDockerComposeTemplate`/`CreatePublishZipTemplate` todavía no aceptan `remoteDeployPath: string`, y `ResolveDefaultRemoteDeployPath` no existe (error de compilación).

- [ ] **Step 3: Reemplazar `PipelineTemplateFactory.cs` completo**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineTemplateFactory
{
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, string remoteDeployPath, DockerRegistry? dockerRegistry = null)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
        var remoteComposeFilePath = $"{remoteDeployPath}/compose.yml";
        var registryTag = BuildRegistryTag(dockerRegistry, imageTag);

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.DockerBuild, Name = "Build imagen", Args = { ["ImageTag"] = imageTag, ["Dockerfile"] = "Dockerfile" } },
            new()
            {
                Type = StepType.DockerPush, Name = "Push a registry",
                Args =
                {
                    ["ImageTag"] = imageTag,
                    ["RegistryTag"] = registryTag,
                    ["RegistryHost"] = dockerRegistry?.Host ?? "",
                    ["RegistryUsername"] = dockerRegistry?.Username ?? "",
                    ["RegistryTokenEnvVar"] = dockerRegistry?.TokenEnvVar ?? ""
                }
            },
            new() { Type = StepType.CopyToRemote, Name = "Copiar compose.yml", Args = { ["LocalPath"] = "compose.yml", ["RemotePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposePull, Name = "Compose pull", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerImagePrune, Name = "Limpiar imágenes viejas", Args = { ["ImageNameFilter"] = $"{projectName.ToLower()}-{subProjectName.ToLower()}" } }
        };
    }

    private static string BuildRegistryTag(DockerRegistry? registry, string imageTag)
    {
        if (registry == null || string.IsNullOrEmpty(registry.Username)) return imageTag;
        var prefix = string.IsNullOrEmpty(registry.Host) ? registry.Username : $"{registry.Host}/{registry.Username}";
        return $"{prefix}/{imageTag}";
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

    public List<DeployStep> CreateLocalPublishTemplate(List<string> omitFiles) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.ZipPublishOutput,
                Name = "Build, publish y comprimir output",
                Args = { ["OmitFiles"] = omitFiles.Count > 0 ? string.Join("|", omitFiles) : "" }
            }
        };

    public List<DeployStep> CreateLocalDockerBuildTemplate(string dockerfilePath, string imageTag, string? buildArgs) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerBuild,
                Name = "Build imagen",
                Args = { ["Dockerfile"] = dockerfilePath, ["ImageTag"] = imageTag, ["BuildArgs"] = buildArgs ?? "" }
            }
        };

    public List<DeployStep> CreateLocalDockerPushTemplate(string imageTag, DockerRegistry? dockerRegistry) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerPush,
                Name = "Push a registry",
                Args =
                {
                    ["ImageTag"] = imageTag,
                    ["RegistryTag"] = BuildRegistryTag(dockerRegistry, imageTag),
                    ["RegistryHost"] = dockerRegistry?.Host ?? "",
                    ["RegistryUsername"] = dockerRegistry?.Username ?? "",
                    ["RegistryTokenEnvVar"] = dockerRegistry?.TokenEnvVar ?? ""
                }
            }
        };

    public List<DeployStep> CreateLocalDockerRunTemplate(string imageTag, string? runArgs) =>
        new()
        {
            new DeployStep
            {
                Type = StepType.DockerRun,
                Name = "Run contenedor",
                Args = { ["ImageTag"] = imageTag, ["RunArgs"] = runArgs ?? "" }
            }
        };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: PASS, 17/17.

Esto rompe la compilación de `MenuManager.cs` y `PipelineEditorMenu.cs` (todavía llaman a la firma vieja) — es esperado, Task 3 y Task 4 los actualizan. No correr `dotnet build vali-deploy.sln` completo todavía en este task.

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "refactor(application): PipelineTemplateFactory recibe el path remoto ya resuelto"
```

---

### Task 3: Gate + wizard fusionado en `MenuManager`

**Depends on:** Task 2 (firma nueva de `PipelineTemplateFactory`)
**Independiente de Task 4 — sin overlap de archivos, se puede hacer en paralelo con Task 4 una vez Task 2 esté commiteado.**

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`

Sin test nuevo — `MenuManager` es Presentation/Manager basado en Spectre.Console, no testeable en este repo (mismo criterio que el resto de ciclos de esta sesión).

- [ ] **Step 1: Agregar el gate en `AddProjectAsync`**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar el método completo (líneas 214-227):

```csharp
    private static async Task AddProjectAsync()
    {
        string? projectName = PromptProjectName();
        if (projectName == null) return;

        string? projectPath = PromptProjectPath();
        if (projectPath == null) return;

        var subProjects = await PromptSubProjectsAsync(projectPath);
        if (subProjects == null) return;

        AddProjectToConfig(projectName, new Project { Path = projectPath, SubProjects = subProjects });
        AnsiConsole.MarkupLine($"[green]Project '{Markup.Escape(projectName)}' added successfully![/]");
    }
```

por:

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

- [ ] **Step 2: Cambiar la firma de `PromptSubProjectsAsync` y agregar el wizard fusionado**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar el método completo (líneas 260-317, incluido el comentario XML doc que lo precede):

```csharp
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
```

por:

```csharp
    /// <summary>
    /// Prompts the user to add subprojects to a project, including their paths, optional Dockerfile paths,
    /// y el/los ambiente(s) a los que apunta cada uno (con su pipeline inicial ya armado).
    /// </summary>
    /// <param name="projectPath">The path of the parent project.</param>
    /// <param name="projectName">The name of the parent project (usado para armar las plantillas de pipeline).</param>
    /// <param name="environments">Ambientes disponibles en la config, para el selector de cada subproyecto.</param>
    /// <returns>A task that resolves to a list of subprojects, or null if the user cancels without adding any subprojects.</returns>
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

    /// <summary>
    /// Pide a qué ambiente(s) apunta un subproyecto nuevo (selección obligatoria, al menos 1) y arma
    /// un pipeline inicial por cada uno (plantilla + path remoto confirmado), todo en memoria — no
    /// persiste nada acá, el caller (<see cref="PromptSubProjectsAsync"/>) recién guarda al final.
    /// </summary>
    private static Dictionary<string, List<Domain.DeployStep>> PromptPipelinesForSubProject(string projectName, string subProjectName, List<DeployEnvironment> environments)
    {
        var environmentNames = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title($"¿A qué ambiente(s) apunta '{subProjectName}'? (barra espaciadora para elegir, Enter para confirmar)")
                .AddChoices(environments.Select(e => e.Name)));

        var pipelines = new Dictionary<string, List<Domain.DeployStep>>();
        var factory = new Application.PipelineTemplateFactory();

        foreach (var environmentName in environmentNames)
        {
            var environment = environments.First(e => e.Name == environmentName);

            var template = AnsiConsole.Prompt(
                new SelectionPrompt<string>().Title($"Plantilla inicial para '{environmentName}':").AddChoices("Docker Compose", "Publish/Zip"));

            var defaultRemotePath = Application.PipelineTemplateFactory.ResolveDefaultRemoteDeployPath(projectName, subProjectName, environment);
            var remoteDeployPath = AnsiConsole.Ask("Path remoto de deploy:", defaultRemotePath);

            pipelines[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, subProjectName, remoteDeployPath)
                : factory.CreatePublishZipTemplate(projectName, subProjectName, remoteDeployPath);
        }

        return pipelines;
    }
```

- [ ] **Step 3: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: si Task 4 todavía no se hizo, la build va a fallar en `PipelineEditorMenu.cs` (sigue llamando a la firma vieja de la factory) — **eso es esperado si Task 3 y Task 4 corren en paralelo**. Si Task 4 ya está commiteado, debe compilar sin errores.

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(managers): agregar gate de ambiente obligatorio y wizard fusionado de subproyecto"
```

---

### Task 4: Path remoto en `PipelineEditorMenu`

**Depends on:** Task 2 (firma nueva de `PipelineTemplateFactory`)
**Independiente de Task 3 — sin overlap de archivos, se puede hacer en paralelo con Task 3 una vez Task 2 esté commiteado.**

**Files:**
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

Sin test nuevo — mismo criterio que Task 3.

- [ ] **Step 1: Agregar el prompt de path remoto antes de la confirmación**

En `vali-deploy/Presentation/PipelineEditorMenu.cs`, reemplazar el bloque (dentro de `StartAsync`, el `if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName))`):

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
```

por:

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

- [ ] **Step 2: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: si Task 3 todavía no se hizo, la build va a fallar en `MenuManager.cs` (sigue llamando a `PromptSubProjectsAsync(projectPath)` con la firma vieja) — **eso es esperado si Task 3 y Task 4 corren en paralelo**. Si Task 3 ya está commiteado, debe compilar sin errores.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "feat(presentation): agregar prompt de path remoto por subproyecto en el pipeline editor"
```

---

### Task 5: Build final + verificación manual

**Depends on:** Task 1, Task 2, Task 3, Task 4 (todos commiteados)

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Build y test suite completos**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores (con Task 3 y Task 4 ambos commiteados, ya no hay firmas desincronizadas).

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, todos los tests (147 previos - 1 renombrado + 2 nuevos de CopyToRemote + 2 nuevos de ZipPublish + 2 nuevos de PipelineTemplateFactory − 2 tests reemplazados de PipelineTemplateFactory = verificar el conteo final exacto en el output, no debe haber ningún FAIL).

- [ ] **Step 2: Correr el CLI y verificar el gate**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Si `deploy_config.json` ya tiene ambientes de sesiones anteriores, vaciar `Environments` temporalmente (backup del archivo primero) o usar un perfil limpio para probar el caso "sin ambientes". Confirmar: "Add Project" con `Environments` vacío muestra el mensaje y entra directo a "Manage Environments"; si se sale sin crear ninguno, cancela el alta de proyecto.

- [ ] **Step 3: Verificar el wizard fusionado**

Con al menos un ambiente configurado, crear un proyecto nuevo con un subproyecto. Confirmar:
- Se pide a qué ambiente(s) apunta el subproyecto (multiselección, no se puede confirmar con 0 elegidos).
- Para cada ambiente elegido, se pide plantilla y path remoto (con el default `/opt/{proyecto}-{subproyecto}` precargado, editable).
- Al terminar, el proyecto queda guardado con el pipeline ya armado — entrar a "Pipeline Editor" para ese subproyecto/ambiente debe ir directo a la lista de steps (no volver a pedir plantilla).

- [ ] **Step 4: Verificar el path remoto en `PipelineEditorMenu`**

Agregar un ambiente nuevo a un subproyecto ya existente vía "Pipeline Editor". Confirmar que pide el path remoto (con el default correcto) antes de la confirmación final.

- [ ] **Step 5: Verificar que Publish/Zip generó un pipeline ejecutable**

Para un subproyecto con plantilla Publish/Zip, revisar (vía "Pipeline Editor" → ver los steps, o inspeccionando `deploy_config.json`) que el step "Copiar zip al remoto" tiene `RemotePath` seteado. Si es posible correr el pipeline completo contra un servidor de prueba, confirmar que "Copiar zip al remoto" no explota por falta de `LocalPath` (usa el zip que generó el step anterior).

Si cualquiera de estos pasos falla, corregir el código en el task correspondiente y volver a compilar/testear antes de continuar.

---

## Self-review

**Cobertura de la spec:** las 4 secciones del spec (gate, wizard fusionado, path remoto por SubProyecto+Ambiente, fix de LocalPath) están cubiertas 1:1 por Task 3, Task 3, Task 2+3+4, y Task 1 respectivamente.

**Consistencia de tipos:** `PromptPipelinesForSubProject` devuelve `Dictionary<string, List<Domain.DeployStep>>`, mismo tipo que `SubProject.PipelinesByEnvironment` (`Domain/SubProject.cs:16`). `PipelineTemplateFactory.ResolveDefaultRemoteDeployPath` se llama igual (mismos 3 parámetros: `projectName`, `subProjectName`, `environment`) en Task 3 (`MenuManager`) y Task 4 (`PipelineEditorMenu`). `CreateDockerComposeTemplate`/`CreatePublishZipTemplate` tienen la misma firma nueva (`remoteDeployPath: string` como 3er parámetro posicional) usada consistentemente en Task 2 (tests), Task 3 y Task 4.

**Sin placeholders:** todos los steps tienen código completo (archivos completos donde el volumen de cambios lo justifica, diffs puntuales donde el cambio es acotado), sin TBD.

**Nota de paralelismo:** Task 3 y Task 4 individualmente dejan el build roto si se commitean antes de que el otro también esté hecho (ambos dependen de la firma nueva de Task 2, pero se pisan entre sí solo a nivel "build completo", no a nivel de archivo — no hay conflicto de merge posible). Task 5 es el punto de sincronización que valida que ambos ya están commiteados y el build vuelve a estar verde.
