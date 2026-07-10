# Cierre de deuda técnica — Ciclo 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar 5 items de deuda técnica documentados en `CLAUDE.md`: unificar la ejecución Docker/publish (legacy `CommandExecutor` → pipeline con verificación de exit code), generalizar el registry Docker (sale `DockerHubUser` en texto plano), verificar integridad del updater (GitHub Releases + SHA256), y terminar `ZipPublishExecutor` (compresión real + `OmitFiles`).

**Architecture:** Todo el trabajo nuevo se apoya en la abstracción `IStepExecutor`/`PipelineRunner` ya existente — no se introduce ningún sistema de ejecución paralelo. El menú ad-hoc de `MenuManager.ExecuteCommandSubProject` conserva sus mismas opciones visibles ("Generate Microsoft publish", "Docker Build", "Docker Run", "Push to registry") pero construye pipelines efímeros de 1 step (no persistidos en `PipelinesByEnvironment`, no visibles desde "Edit Pipeline") contra un `DeployEnvironment` reservado `"Local"` construido en memoria — nunca se escribe a `deploy_config.json`, por lo que no hace falta filtrarlo de ningún menú.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, xUnit 2.6.6 + Moq 4.20.70 (test framework ya instalado, sin paquetes nuevos).

**Spec:** `docs/superpowers/specs/2026-07-10-cli-tech-debt-cycle1-design.md`

---

### Task 1: `DockerRegistry` — value object de dominio

**Files:**
- Create: `vali-deploy/Domain/DockerRegistry.cs`
- Test: `vali-deploy.Tests/Domain/DockerRegistryTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DockerRegistryTests
{
    [Fact]
    public void Empty_host_means_docker_hub()
    {
        var registry = new DockerRegistry { Username = "myuser" };

        Assert.Equal("", registry.Host);
        Assert.Null(registry.TokenEnvVar);
    }

    [Fact]
    public void Host_set_means_generic_registry()
    {
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        Assert.Equal("ghcr.io", registry.Host);
        Assert.Equal("GHCR_TOKEN", registry.TokenEnvVar);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerRegistryTests`
Expected: FAIL (no existe `vali_deploy.Domain.DockerRegistry`, error de compilación)

- [ ] **Step 3: Crear el value object**

```csharp
namespace vali_deploy.Domain;

public class DockerRegistry
{
    public string Host { get; set; } = "";
    public string Username { get; set; } = "";
    public string? TokenEnvVar { get; set; }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerRegistryTests`
Expected: PASS (2/2)

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Domain/DockerRegistry.cs vali-deploy.Tests/Domain/DockerRegistryTests.cs
git commit -m "feat(domain): agregar DockerRegistry como value object"
```

---

### Task 2: Migrar `SubProject.DockerHubUser` → `DockerRegistry`

**Depends on:** Task 1

**Files:**
- Modify: `vali-deploy/Domain/SubProject.cs`
- Modify: `vali-deploy/Infrastructure/ProjectRepository.cs`
- Modify: `vali-deploy.Tests/Infrastructure/ProjectRepositoryTests.cs`

- [ ] **Step 1: Escribir los tests de migración**

Agregar al final de la clase `ProjectRepositoryTests` (antes del `}` de cierre), en `vali-deploy.Tests/Infrastructure/ProjectRepositoryTests.cs`:

```csharp

    [Fact]
    public void Load_migrates_legacy_DockerHubUser_to_DockerRegistry()
    {
        var configPath = NewTempConfigPath();
        var legacyConfig = new DeployConfig
        {
            Projects = new Dictionary<string, Project>
            {
                ["proj"] = new Project
                {
                    Path = "/tmp/proj",
                    SubProjects = new List<SubProject> { new() { Name = "api", DockerHubUser = "myuser" } }
                }
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(legacyConfig, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var config = repository.Load();

        var subProject = config.Projects["proj"].SubProjects[0];
        Assert.NotNull(subProject.DockerRegistry);
        Assert.Equal("", subProject.DockerRegistry!.Host);
        Assert.Equal("myuser", subProject.DockerRegistry.Username);
        Assert.Null(subProject.DockerHubUser);
    }

    [Fact]
    public void Load_does_not_overwrite_existing_DockerRegistry_with_stale_DockerHubUser()
    {
        var configPath = NewTempConfigPath();
        var config = new DeployConfig
        {
            Projects = new Dictionary<string, Project>
            {
                ["proj"] = new Project
                {
                    Path = "/tmp/proj",
                    SubProjects = new List<SubProject>
                    {
                        new()
                        {
                            Name = "api",
                            DockerRegistry = new DockerRegistry { Host = "ghcr.io", Username = "already-configured" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

        var repository = new ProjectRepository(configPath);
        var reloaded = repository.Load();

        Assert.Equal("already-configured", reloaded.Projects["proj"].SubProjects[0].DockerRegistry!.Username);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ProjectRepositoryTests`
Expected: FAIL (no existe `SubProject.DockerRegistry` todavía)

- [ ] **Step 3: Reemplazar `DockerHubUser`/`DockerRegistryTokenEnvVar` por `DockerRegistry` en `SubProject.cs`**

Reemplazar el contenido completo de `vali-deploy/Domain/SubProject.cs`:

```csharp
using System.Text.Json.Serialization;

namespace vali_deploy.Domain;

public class SubProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> OmitFiles { get; set; } = new();
    public string? DockerfilePath { get; set; }
    public List<string>? DockerRunArgs { get; set; }
    public List<string>? DockerBuildArgs { get; set; }
    public DockerRegistry? DockerRegistry { get; set; }
    public List<string>? PublishArgs { get; set; }
    public bool ZipPublishOutput { get; set; } = true;
    public Dictionary<string, List<DeployStep>> PipelinesByEnvironment { get; set; } = new();

    /// <summary>
    /// Campo legacy (pre-DockerRegistry): username de Docker Hub en texto plano. Ningún flujo de la
    /// aplicación lo lee ni lo escribe — existe solo para que <see cref="Infrastructure.ProjectRepository.Load"/>
    /// pueda migrarlo a <see cref="DockerRegistry"/> la primera vez que se carga un deploy_config.json viejo.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DockerHubUser { get; set; }
}
```

- [ ] **Step 4: Agregar la migración en `ProjectRepository.Load()`**

En `vali-deploy/Infrastructure/ProjectRepository.cs`, reemplazar:

```csharp
    public DeployConfig Load()
    {
        var folderPath = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(folderPath);

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            return ParseConfigLeniently(json);
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
    }
```

por:

```csharp
    public DeployConfig Load()
    {
        var folderPath = Path.GetDirectoryName(_configPath)!;
        Directory.CreateDirectory(folderPath);

        if (!File.Exists(_configPath))
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }

        try
        {
            var json = File.ReadAllText(_configPath);
            var config = ParseConfigLeniently(json);
            MigrateDockerHubUserToRegistry(config);
            return config;
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    /// <summary>
    /// Migra SubProject.DockerHubUser (campo legacy en texto plano) a SubProject.DockerRegistry la primera vez
    /// que se carga un deploy_config.json escrito por una versión anterior del CLI. No persiste el resultado acá
    /// — el próximo Save() (de cualquier flujo) ya escribe la forma nueva.
    /// </summary>
    internal static void MigrateDockerHubUserToRegistry(DeployConfig config)
    {
        foreach (var project in config.Projects.Values)
        {
            foreach (var subProject in project.SubProjects)
            {
                if (subProject.DockerRegistry == null && !string.IsNullOrEmpty(subProject.DockerHubUser))
                {
                    subProject.DockerRegistry = new DockerRegistry { Host = "", Username = subProject.DockerHubUser };
                }

                subProject.DockerHubUser = null;
            }
        }
    }
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ProjectRepositoryTests`
Expected: PASS (7/7 — 5 preexistentes + 2 nuevos)

- [ ] **Step 6: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.` — este step va a marcar errores de compilación en `MenuManager.cs` porque todavía referencia `subProject.DockerHubUser` como propiedad de escritura simple; esos call-sites se corrigen en la Task 8. Por ahora, si el build falla únicamente en `MenuManager.cs` con el mensaje de que `DockerHubUser` ya no es asignable de la forma en que se usaba (sigue existiendo la propiedad, así que en realidad esto compila igual — `DockerHubUser` sigue siendo un `string?` legítimo). Confirmá que compila.

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Domain/SubProject.cs vali-deploy/Infrastructure/ProjectRepository.cs vali-deploy.Tests/Infrastructure/ProjectRepositoryTests.cs
git commit -m "feat(domain): migrar SubProject.DockerHubUser a DockerRegistry con migracion automatica"
```

---

### Task 3: Soporte de stdin en `IProcessRunner`/`ProcessRunner`

**Files:**
- Modify: `vali-deploy/Infrastructure/IProcessRunner.cs`
- Modify: `vali-deploy/Infrastructure/ProcessRunner.cs`
- Modify: `vali-deploy.Tests/Infrastructure/ProcessRunnerTests.cs`

**Por qué:** `docker login --password-stdin` necesita pasar el token por stdin en vez de como argumento de línea de comando (que quedaría visible en la lista de procesos del SO). `IProcessRunner.RunAsync` hoy no soporta escribir a stdin del proceso hijo.

- [ ] **Step 1: Escribir el test**

Agregar al final de `vali-deploy.Tests/Infrastructure/ProcessRunnerTests.cs` (antes del `}` de cierre):

```csharp

    [Fact]
    public async Task RunAsync_pipes_stdInput_to_the_process()
    {
        var runner = new ProcessRunner();
        var command = OperatingSystem.IsWindows() ? "findstr ." : "cat";

        var result = await runner.RunAsync(command, Directory.GetCurrentDirectory(), stdInput: "secreto123");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("secreto123", result.StdOut);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ProcessRunnerTests`
Expected: FAIL (no existe el parámetro `stdInput`, error de compilación)

- [ ] **Step 3: Extender `IProcessRunner`**

Reemplazar `vali-deploy/Infrastructure/IProcessRunner.cs` completo:

```csharp
namespace vali_deploy.Infrastructure;

public record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null, string? stdInput = null);
}
```

- [ ] **Step 4: Implementar el envío de stdin en `ProcessRunner`**

Reemplazar el método `RunAsync` en `vali-deploy/Infrastructure/ProcessRunner.cs`:

```csharp
    public async Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null, string? stdInput = null)
    {
        var startInfo = CreateProcessStartInfo(command, workingDirectory);

        if (extraEnvVars != null)
        {
            foreach (var (key, value) in extraEnvVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        if (stdInput != null)
        {
            startInfo.RedirectStandardInput = true;
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (stdInput != null)
        {
            await process.StandardInput.WriteAsync(stdInput);
            process.StandardInput.Close();
        }

        await process.WaitForExitAsync();

        return new ProcessRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }
```

- [ ] **Step 5: Correr el test y verificar que pasa**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ProcessRunnerTests`
Expected: PASS (3/3)

- [ ] **Step 6: Build completo (los mocks existentes de `IProcessRunner` deben seguir compilando)**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.` — los `Setup(p => p.RunAsync(cmd, path, It.IsAny<IDictionary<string,string>>()))` existentes en `DockerPushExecutorTests.cs` siguen compilando porque `stdInput` tiene default `null` y el compilador lo materializa en la expresión.

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Infrastructure/IProcessRunner.cs vali-deploy/Infrastructure/ProcessRunner.cs vali-deploy.Tests/Infrastructure/ProcessRunnerTests.cs
git commit -m "feat(infrastructure): agregar soporte de stdin a IProcessRunner"
```

---

### Task 4: `DockerPushExecutor` — login automático antes de tag/push

**Depends on:** Task 1, Task 3

**Files:**
- Modify: `vali-deploy/Application/Executors/DockerPushExecutor.cs`
- Modify: `vali-deploy/CompositionRoot.cs`
- Modify: `vali-deploy.Tests/CompositionRootTests.cs`
- Modify: `vali-deploy.Tests/Application/Executors/DockerPushExecutorTests.cs`

- [ ] **Step 1: Reemplazar `DockerPushExecutorTests.cs` completo con los tests nuevos**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerPushExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerPush()
    {
        var executor = new DockerPushExecutor(new Mock<IProcessRunner>().Object, new Mock<ISecretResolver>().Object);
        Assert.Equal(StepType.DockerPush, executor.Handles);
    }

    [Fact]
    public async Task Tags_then_pushes_image_to_registry_without_login_when_no_TokenEnvVar()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var executor = new DockerPushExecutor(processRunner.Object, new Mock<ISecretResolver>().Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker login")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
        processRunner.Verify(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
        processRunner.Verify(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null), Times.Once);
    }

    [Fact]
    public async Task Logs_in_with_resolved_token_before_tag_and_push_when_TokenEnvVar_is_set()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker login ghcr.io -u myorg --password-stdin", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), "resolved-token"))
            .ReturnsAsync(new ProcessRunResult(0, "Login Succeeded", ""));
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var secretResolver = new Mock<ISecretResolver>();
        secretResolver.Setup(s => s.Resolve("GHCR_TOKEN")).Returns("resolved-token");

        var executor = new DockerPushExecutor(processRunner.Object, secretResolver.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args =
            {
                ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "ghcr.io/myorg/proj-sub:latest",
                ["RegistryHost"] = "ghcr.io", ["RegistryUsername"] = "myorg", ["RegistryTokenEnvVar"] = "GHCR_TOKEN"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("docker login ghcr.io -u myorg --password-stdin", "/tmp/proj", It.IsAny<IDictionary<string, string>>(), "resolved-token"), Times.Once);
    }

    [Fact]
    public async Task Stops_at_login_failure_without_attempting_tag_or_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker login")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "unauthorized"));

        var secretResolver = new Mock<ISecretResolver>();
        secretResolver.Setup(s => s.Resolve("GHCR_TOKEN")).Returns("bad-token");

        var executor = new DockerPushExecutor(processRunner.Object, secretResolver.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args =
            {
                ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "ghcr.io/myorg/proj-sub:latest",
                ["RegistryHost"] = "ghcr.io", ["RegistryUsername"] = "myorg", ["RegistryTokenEnvVar"] = "GHCR_TOKEN"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Stops_at_tag_failure_without_attempting_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>(), null))
            .ReturnsAsync(new ProcessRunResult(1, "", "no such image"));

        var executor = new DockerPushExecutor(processRunner.Object, new Mock<ISecretResolver>().Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<string>()), Times.Never);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerPushExecutorTests`
Expected: FAIL (constructor de `DockerPushExecutor` no acepta `ISecretResolver`, error de compilación)

- [ ] **Step 3: Reemplazar `DockerPushExecutor.cs` completo**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerPushExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;
    private readonly ISecretResolver _secretResolver;

    public DockerPushExecutor(IProcessRunner processRunner, ISecretResolver secretResolver)
    {
        _processRunner = processRunner;
        _secretResolver = secretResolver;
    }

    public StepType Handles => StepType.DockerPush;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var registryTag = step.Args["RegistryTag"];
        var registryHost = step.Args.GetValueOrDefault("RegistryHost", "");
        var registryUsername = step.Args.GetValueOrDefault("RegistryUsername", "");
        var registryTokenEnvVar = step.Args.GetValueOrDefault("RegistryTokenEnvVar", "");
        var extraEnv = new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" };

        if (!string.IsNullOrEmpty(registryTokenEnvVar))
        {
            var token = _secretResolver.Resolve(registryTokenEnvVar);
            var loginCommand = string.IsNullOrEmpty(registryHost)
                ? $"docker login -u {registryUsername} --password-stdin"
                : $"docker login {registryHost} -u {registryUsername} --password-stdin";

            var loginRun = await _processRunner.RunAsync(loginCommand, context.ProjectPath, extraEnv, token);
            if (loginRun.ExitCode != 0)
            {
                stopwatch.Stop();
                return BuildResult(step, loginRun, loginRun.StdOut, stopwatch.Elapsed);
            }
        }

        var tagRun = await _processRunner.RunAsync($"docker tag {imageTag} {registryTag}", context.ProjectPath, extraEnv);

        if (tagRun.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, tagRun, tagRun.StdOut, stopwatch.Elapsed);
        }

        var pushRun = await _processRunner.RunAsync($"docker push {registryTag}", context.ProjectPath, extraEnv);
        stopwatch.Stop();

        return BuildResult(step, pushRun, tagRun.StdOut + pushRun.StdOut, stopwatch.Elapsed);
    }

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Duration = duration
    };
}
```

- [ ] **Step 4: Actualizar `CompositionRoot.cs` para inyectar `ISecretResolver` en `DockerPushExecutor`**

Reemplazar en `vali-deploy/CompositionRoot.cs`:

```csharp
    public static IPipelineRunner CreatePipelineRunner()
    {
        var processRunner = new ProcessRunner();
        var secretResolver = new EnvVarSecretResolver();
        var sshClientFactory = new SshClientFactory(secretResolver);

        return new PipelineRunner(BuildExecutors(processRunner, sshClientFactory));
    }

    /// <summary>
    /// Builds the full set of <see cref="IStepExecutor"/> instances that back <see cref="CreatePipelineRunner"/>.
    /// Extracted as its own seam (rather than inlined in <see cref="CreatePipelineRunner"/>) so tests can assert
    /// registration completeness against every <see cref="vali_deploy.Domain.StepType"/> value without needing
    /// real infrastructure (a live process runner or SSH connection) or introspecting a private dictionary.
    /// </summary>
    public static IStepExecutor[] BuildExecutors(IProcessRunner processRunner, ISshClientFactory sshClientFactory) =>
        new IStepExecutor[]
        {
            new LocalCommandExecutor(processRunner),
            new RawCommandExecutor(processRunner),
            new GitCheckoutExecutor(processRunner),
            new DockerBuildExecutor(processRunner),
            new DockerPushExecutor(processRunner),
            new DockerSaveExecutor(processRunner),
            new DockerImagePruneExecutor(processRunner),
            new ZipPublishExecutor(processRunner),
            new SshCommandExecutor(sshClientFactory),
            new DockerLoadExecutor(sshClientFactory),
            new CopyToRemoteExecutor(sshClientFactory),
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
        };
```

por:

```csharp
    public static IPipelineRunner CreatePipelineRunner()
    {
        var processRunner = new ProcessRunner();
        var secretResolver = new EnvVarSecretResolver();
        var sshClientFactory = new SshClientFactory(secretResolver);

        return new PipelineRunner(BuildExecutors(processRunner, sshClientFactory, secretResolver));
    }

    /// <summary>
    /// Builds the full set of <see cref="IStepExecutor"/> instances that back <see cref="CreatePipelineRunner"/>.
    /// Extracted as its own seam (rather than inlined in <see cref="CreatePipelineRunner"/>) so tests can assert
    /// registration completeness against every <see cref="vali_deploy.Domain.StepType"/> value without needing
    /// real infrastructure (a live process runner or SSH connection) or introspecting a private dictionary.
    /// </summary>
    public static IStepExecutor[] BuildExecutors(IProcessRunner processRunner, ISshClientFactory sshClientFactory, ISecretResolver secretResolver) =>
        new IStepExecutor[]
        {
            new LocalCommandExecutor(processRunner),
            new RawCommandExecutor(processRunner),
            new GitCheckoutExecutor(processRunner),
            new DockerBuildExecutor(processRunner),
            new DockerPushExecutor(processRunner, secretResolver),
            new DockerSaveExecutor(processRunner),
            new DockerImagePruneExecutor(processRunner),
            new ZipPublishExecutor(processRunner),
            new SshCommandExecutor(sshClientFactory),
            new DockerLoadExecutor(sshClientFactory),
            new CopyToRemoteExecutor(sshClientFactory),
            new DockerComposePullExecutor(sshClientFactory),
            new DockerComposeUpExecutor(sshClientFactory),
            new DockerComposeDownExecutor(sshClientFactory)
        };
```

- [ ] **Step 5: Actualizar `CompositionRootTests.cs`**

Reemplazar:

```csharp
    private static IStepExecutor[] BuildExecutors() =>
        CompositionRoot.BuildExecutors(new Mock<IProcessRunner>().Object, new Mock<ISshClientFactory>().Object);
```

por:

```csharp
    private static IStepExecutor[] BuildExecutors() =>
        CompositionRoot.BuildExecutors(new Mock<IProcessRunner>().Object, new Mock<ISshClientFactory>().Object, new Mock<ISecretResolver>().Object);
```

Y agregar el `using` correspondiente al tope del archivo si no está: `using vali_deploy.Application;` (necesario para `ISecretResolver`, que vive en `vali_deploy.Application`).

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "DockerPushExecutorTests|CompositionRootTests"`
Expected: PASS (5 de DockerPushExecutorTests + 3 de CompositionRootTests)

- [ ] **Step 7: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 8: Commit**

```bash
git add vali-deploy/Application/Executors/DockerPushExecutor.cs vali-deploy/CompositionRoot.cs vali-deploy.Tests/CompositionRootTests.cs vali-deploy.Tests/Application/Executors/DockerPushExecutorTests.cs
git commit -m "feat(application): login automatico en DockerPushExecutor antes de tag/push"
```

---

### Task 5: `CreateDockerComposeTemplate` — autogenerar `RegistryTag`

**Depends on:** Task 1

**Files:**
- Modify: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`
- Modify: `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`

- [ ] **Step 1: Reemplazar el test que asumía `RegistryTag` vacío y agregar los nuevos**

En `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`, reemplazar:

```csharp
    [Fact]
    public void DockerCompose_template_leaves_RegistryTag_as_empty_placeholder_for_manual_completion()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment());
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("", pushStep.Args["RegistryTag"]);
    }
```

por:

```csharp
    [Fact]
    public void DockerCompose_template_falls_back_to_bare_imageTag_when_no_registry_configured()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment());
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_for_docker_hub_when_host_is_empty()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "", Username = "myuser" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment(), dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("myuser/shop-api:latest", pushStep.Args["RegistryTag"]);
    }

    [Fact]
    public void DockerCompose_template_builds_RegistryTag_with_host_for_generic_registry()
    {
        var factory = new PipelineTemplateFactory();
        var registry = new DockerRegistry { Host = "ghcr.io", Username = "myorg", TokenEnvVar = "GHCR_TOKEN" };

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api", environment: Environment(), dockerRegistry: registry);
        var pushStep = steps.Single(s => s.Type == StepType.DockerPush);

        Assert.Equal("ghcr.io/myorg/shop-api:latest", pushStep.Args["RegistryTag"]);
        Assert.Equal("ghcr.io", pushStep.Args["RegistryHost"]);
        Assert.Equal("myorg", pushStep.Args["RegistryUsername"]);
        Assert.Equal("GHCR_TOKEN", pushStep.Args["RegistryTokenEnvVar"]);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: FAIL (`CreateDockerComposeTemplate` no acepta el parámetro `dockerRegistry`)

- [ ] **Step 3: Actualizar `PipelineTemplateFactory.CreateDockerComposeTemplate`**

Reemplazar en `vali-deploy/Application/PipelineTemplateFactory.cs`:

```csharp
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, DeployEnvironment environment)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
        var remoteDeployPath = environment.RemoteDeployPath ?? $"/opt/{projectName.ToLower()}-{subProjectName.ToLower()}";
        var remoteComposeFilePath = $"{remoteDeployPath}/compose.yml";

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.DockerBuild, Name = "Build imagen", Args = { ["ImageTag"] = imageTag, ["Dockerfile"] = "Dockerfile" } },
            new() { Type = StepType.DockerPush, Name = "Push a registry", Args = { ["ImageTag"] = imageTag, ["RegistryTag"] = "" } },
            new() { Type = StepType.CopyToRemote, Name = "Copiar compose.yml", Args = { ["LocalPath"] = "compose.yml", ["RemotePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposePull, Name = "Compose pull", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up", Args = { ["ComposeFilePath"] = remoteComposeFilePath } },
            new() { Type = StepType.DockerImagePrune, Name = "Limpiar imágenes viejas", Args = { ["ImageNameFilter"] = $"{projectName.ToLower()}-{subProjectName.ToLower()}" } }
        };
    }
```

por:

```csharp
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName, DeployEnvironment environment, DockerRegistry? dockerRegistry = null)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";
        var remoteDeployPath = environment.RemoteDeployPath ?? $"/opt/{projectName.ToLower()}-{subProjectName.ToLower()}";
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
```

- [ ] **Step 4: Actualizar el call-site en `PipelineEditorMenu.cs` (solo la línea de `CreateDockerComposeTemplate` — la de `CreatePublishZipTemplate` se toca recién en la Task 10)**

Reemplazar:

```csharp
            configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment)
                : factory.CreatePublishZipTemplate(projectName, configSubProject.Name);
```

por:

```csharp
            configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment, configSubProject.DockerRegistry)
                : factory.CreatePublishZipTemplate(projectName, configSubProject.Name);
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: PASS (8/8 — 5 preexistentes intactos + 3 nuevos)

- [ ] **Step 6: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy/Presentation/PipelineEditorMenu.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "feat(application): autogenerar RegistryTag en CreateDockerComposeTemplate"
```

---

### Task 6: `StepType.DockerRun` + `DockerRunExecutor`

**Files:**
- Modify: `vali-deploy/Domain/StepType.cs`
- Create: `vali-deploy/Application/Executors/DockerRunExecutor.cs`
- Modify: `vali-deploy/CompositionRoot.cs`
- Create: `vali-deploy.Tests/Application/Executors/DockerRunExecutorTests.cs`

**Por qué `DockerRunExecutor` no sigue el patrón de los demás executors:** `docker run -it --rm` es una sesión interactiva — el usuario queda dentro del contenedor con su propia terminal. `IProcessRunner`/`ProcessRunner` capturan `StdOut`/`StdErr` como texto para poder loguearlos (ver Task 3), lo cual es incompatible con una sesión interactiva real. Por eso este es el único `IStepExecutor` que NO usa `IProcessRunner` — arranca su propio `Process` heredando la consola del proceso padre. Es una excepción intencional y documentada al patrón "todo step es logueable" del resto del pipeline.

- [ ] **Step 1: Agregar `DockerRun` al enum**

Reemplazar `vali-deploy/Domain/StepType.cs` completo:

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
    DockerComposeUp,
    DockerComposeDown,
    ZipPublishOutput,
    CopyToRemote,
    SshCommand,
    RawCommand
}
```

- [ ] **Step 2: Escribir el test (antes de registrar el executor, para verificar el fallo esperado de `CompositionRootTests`)**

```csharp
using vali_deploy.Application.Executors;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application.Executors;

public class DockerRunExecutorTests
{
    // DockerRunExecutor arranca una sesión interactiva real (docker run -it) heredando la consola
    // del proceso padre — no usa IProcessRunner, así que no es mockeable/testeable en aislamiento sin
    // una capa de abstracción de Process que este proyecto no tiene (igual que Presentation/ no tiene
    // tests unitarios). Este test solo verifica el registro correcto del StepType.
    [Fact]
    public void Handles_DockerRun()
    {
        var executor = new DockerRunExecutor();
        Assert.Equal(StepType.DockerRun, executor.Handles);
    }
}
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DockerRunExecutorTests`
Expected: FAIL (no existe `DockerRunExecutor`)

- [ ] **Step 4: Crear `DockerRunExecutor`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;

namespace vali_deploy.Application.Executors;

/// <summary>
/// Ejecuta "docker run -it --rm" heredando la consola del proceso padre (sin redirigir stdin/stdout/stderr),
/// para que el usuario pueda interactuar con el contenedor. A diferencia del resto de los IStepExecutor,
/// no depende de IProcessRunner: esa abstracción captura la salida como texto para loguearla, lo cual es
/// incompatible con una sesión interactiva real. La salida de este step nunca queda en el log del pipeline.
/// </summary>
public class DockerRunExecutor : IStepExecutor
{
    public StepType Handles => StepType.DockerRun;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var runArgs = step.Args.GetValueOrDefault("RunArgs", "");
        var runArgsSuffix = string.IsNullOrWhiteSpace(runArgs) ? "" : $" {runArgs}";
        var command = $"docker run -it --rm{runArgsSuffix} {imageTag}";

        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = context.ProjectPath,
            UseShellExecute = false
        };
        startInfo.Environment["DOCKER_BUILDKIT"] = "1";

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = process.ExitCode == 0,
            ExitCode = process.ExitCode,
            Output = "(sesión interactiva — salida no capturada)",
            Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 5: Correr el test de `DockerRunExecutorTests` y verificar que pasa, y confirmar que `CompositionRootTests` ahora falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "DockerRunExecutorTests|CompositionRootTests"`
Expected: `DockerRunExecutorTests` PASS (1/1). `CompositionRootTests.BuildExecutors_registers_exactly_one_executor_for_every_StepType` FAIL — es el comportamiento esperado: agregamos un `StepType` sin registrar su executor todavía.

- [ ] **Step 6: Registrar `DockerRunExecutor` en `CompositionRoot.BuildExecutors`**

Reemplazar en `vali-deploy/CompositionRoot.cs`:

```csharp
            new DockerBuildExecutor(processRunner),
            new DockerPushExecutor(processRunner, secretResolver),
```

por:

```csharp
            new DockerBuildExecutor(processRunner),
            new DockerRunExecutor(),
            new DockerPushExecutor(processRunner, secretResolver),
```

- [ ] **Step 7: Correr todos los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS (todos, sin regresiones)

- [ ] **Step 8: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
git add vali-deploy/Domain/StepType.cs vali-deploy/Application/Executors/DockerRunExecutor.cs vali-deploy/CompositionRoot.cs vali-deploy.Tests/Application/Executors/DockerRunExecutorTests.cs
git commit -m "feat(application): agregar StepType.DockerRun con consola heredada"
```

---

### Task 7: `PipelineTemplateFactory` — templates locales (Build/Push/Run/Publish)

**Depends on:** Task 5, Task 6

**Files:**
- Modify: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Modify: `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`

**Por qué:** estos templates reemplazan al `CommandExecutor` legacy para las 4 acciones locales del menú ad-hoc (Task 8). A diferencia de `CreateDockerComposeTemplate`/`CreatePublishZipTemplate`, no incluyen `GitCheckout` (construyen desde el working copy en disco tal cual está, igual que el comportamiento legacy) y son de un solo step — no se persisten en `PipelinesByEnvironment`, se ejecutan una vez y se descartan.

- [ ] **Step 1: Escribir los tests**

Agregar al final de `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs` (antes del `}` de cierre):

```csharp

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
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: FAIL (no existen los 4 métodos nuevos)

- [ ] **Step 3: Agregar los 4 métodos a `PipelineTemplateFactory.cs`**

Agregar al final de la clase `PipelineTemplateFactory` (antes del `}` de cierre, después de `CreatePublishZipTemplate` y `BuildRegistryTag`):

```csharp

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
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: PASS (13/13)

- [ ] **Step 5: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "feat(application): agregar templates locales de un solo step a PipelineTemplateFactory"
```

---

### Task 8: Migrar `MenuManager.ExecuteCommandSubProject` al pipeline

**Depends on:** Task 2, Task 7

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`

**Nota de scope:** el menú visible NO cambia de estructura (sigue mostrando "Generate Microsoft publish", "Docker Build", "Docker Run", "Push to registry" — renombrado desde "Push to Docker Hub" porque ahora soporta cualquier registry). Lo que cambia es la implementación detrás de cada opción: en vez de `CommandExecutor` (sin verificación de exit code), ahora arma un pipeline efímero de 1 step y lo corre vía `PipelineRunner`/`PipelineExecutionView` contra un `DeployEnvironment` reservado `"Local"` construido en memoria (nunca se persiste a `deploy_config.json`).

- [ ] **Step 1: Agregar el campo estático `LocalEnvironment` junto a `_dockerActions`**

Reemplazar:

```csharp
    private static Dictionary<string, Project> _projects = new();
    private static readonly Infrastructure.IProjectRepository _repository = CompositionRoot.CreateProjectRepository();
    private static readonly string[] _dockerActions = { "Docker Build", "Docker Run", "Push to Docker Hub" };
```

por:

```csharp
    private static Dictionary<string, Project> _projects = new();
    private static readonly Infrastructure.IProjectRepository _repository = CompositionRoot.CreateProjectRepository();
    private static readonly string[] _dockerActions = { "Docker Build", "Docker Run", "Push to registry" };

    /// <summary>
    /// DeployEnvironment reservado para acciones locales sin deploy remoto (Docker Build/Run/Push local,
    /// "Generate Microsoft publish"). Se construye en memoria y nunca se persiste a deploy_config.json —
    /// no aparece en "Manage Environments" ni en ningún otro menú porque nunca entra a config.Environments.
    /// </summary>
    private static readonly Domain.DeployEnvironment LocalEnvironment = new() { Name = "Local" };
```

- [ ] **Step 2: Agregar el helper `RunLocalPipelineAsync`**

Agregar como nuevo método privado, inmediatamente después de `ExecuteCommandSubProject` (antes del doc-comment de `ExecuteSubProjectPipelineAsync`):

```csharp

    /// <summary>
    /// Ejecuta un pipeline efímero de 1 step (Docker Build/Run/Push local, o publish local) contra
    /// <see cref="LocalEnvironment"/>. A diferencia de <see cref="ExecuteSubProjectPipelineAsync"/>, no
    /// persiste nada en PipelinesByEnvironment — el pipeline se descarta después de correr.
    /// </summary>
    private static async Task RunLocalPipelineAsync(Project project, SubProject subProject, string projectName, List<Domain.DeployStep> steps)
    {
        var subProjectPathFull = Path.Combine(project.Path, subProject.Path);
        var context = new Application.StepExecutionContext
        {
            ProjectName = projectName,
            SubProjectName = subProject.Name,
            ProjectPath = subProjectPathFull,
            Environment = LocalEnvironment
        };

        var pipelineRunner = CompositionRoot.CreatePipelineRunner();
        var logger = CompositionRoot.CreatePipelineLogger();
        logger.StartRun(projectName, subProject.Name);

        var view = new Presentation.PipelineExecutionView();
        var result = await view.RunAsync(pipelineRunner, steps, context);

        foreach (var stepResult in result.Steps)
        {
            logger.WriteStep(stepResult);
        }

        PauseForUserInput(result.Success ? "Ejecución completada con éxito." : "La ejecución falló, revisá el detalle arriba.");
    }
```

- [ ] **Step 3: Reemplazar el `switch (action)` completo de `ExecuteCommandSubProject`**

Reemplazar:

```csharp
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
                        PersistProjects();
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

            case "[seagreen1]Back to Subprojects[/]":
                return;
        }
    }
```

por:

```csharp
        switch (action)
        {
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
                        var username = AnsiConsole.Ask<string>("Usuario del registry (ej. tu usuario de Docker Hub):");
                        var host = AnsiConsole.Ask("Host del registry (vacío = Docker Hub):", "");
                        var hasToken = AnsiConsole.Confirm("¿Vas a autenticarte con un token vía variable de entorno?");
                        string? tokenEnvVar = hasToken
                            ? AnsiConsole.Ask<string>("Nombre de la variable de entorno con el token:")
                            : null;

                        subProject.DockerRegistry = new DockerRegistry { Host = host, Username = username, TokenEnvVar = tokenEnvVar };
                        PersistProjects();
                    }

                    var steps = new Application.PipelineTemplateFactory().CreateLocalDockerPushTemplate(imageTag, subProject.DockerRegistry);
                    await RunLocalPipelineAsync(project, subProject, projectName, steps);
                }

                break;

            case "[seagreen1]Back to Subprojects[/]":
                return;
        }
    }
```

- [ ] **Step 4: Actualizar el `if` de refresco de header (ya no hace falta — `RunLocalPipelineAsync`/`PipelineExecutionView` dibujan su propio contenido, y el `Clear()+DrawHeader` previo al `switch` sigue siendo válido tal cual)**

Confirmá que este bloque, ya existente antes del `switch`, sigue igual (no requiere cambios — sigue cubriendo las 4 acciones de ejecución, ahora incluyendo la renombrada "Push to registry" porque `_dockerActions` ya la contiene desde el Step 1):

```csharp
        if (action == "Generate Microsoft publish" || _dockerActions.Contains(action))
        {
            AnsiConsole.Clear();
            Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name}");
        }
```

- [ ] **Step 5: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.` (esperable que falle si `CommandExecutor.cs` todavía existe y algo más lo referencia fuera de este archivo — no debería, se borra recién en la Task 9, y este archivo ya no lo usa)

- [ ] **Step 6: Correr todos los tests**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS (sin regresiones — `MenuManager.cs`/`Managers/` no tiene tests unitarios, ver nota de convención al inicio del plan original de TUI shell)

- [ ] **Step 7: Verificar que no quedan referencias a `CommandExecutor` en `MenuManager.cs`**

Run: `grep -n "CommandExecutor" vali-deploy/Managers/MenuManager.cs`
Expected: sin salida (0 ocurrencias)

- [ ] **Step 8: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "refactor(presentation): migrar ExecuteCommandSubProject al pipeline (retira CommandExecutor)"
```

---

### Task 9: Borrar `CommandExecutor.cs`

**Depends on:** Task 8

**Files:**
- Delete: `vali-deploy/Managers/CommandExecutor.cs`

- [ ] **Step 1: Confirmar que no queda ningún caller en todo el proyecto**

Run: `grep -rn "CommandExecutor" vali-deploy --include=*.cs`
Expected: sin salida (0 ocurrencias en código fuente — puede haber matches en `vali-deploy/bin/`/`obj/`, que no cuentan)

- [ ] **Step 2: Borrar el archivo**

```bash
git rm vali-deploy/Managers/CommandExecutor.cs
```

- [ ] **Step 3: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git commit -m "chore(presentation): borrar CommandExecutor legacy (sin callers)"
```

---

### Task 10: `ZipPublishExecutor` — compresión real + `OmitFiles`

**Files:**
- Modify: `vali-deploy/Application/Executors/ZipPublishExecutor.cs`
- Modify: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`
- Modify: `vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs`

- [ ] **Step 1: Reemplazar `ZipPublishExecutorTests.cs` completo**

```csharp
using System.IO.Compression;
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class ZipPublishExecutorTests
{
    private static string CreateFakePublishFolder(out string projectPath)
    {
        projectPath = Directory.CreateTempSubdirectory().FullName;
        var publishFolder = Path.Combine(projectPath, "bin", "Release", "net7.0", "publish");
        Directory.CreateDirectory(publishFolder);
        File.WriteAllText(Path.Combine(publishFolder, "app.dll"), "dummy");
        File.WriteAllText(Path.Combine(publishFolder, "app.pdb"), "dummy");
        return publishFolder;
    }

    private static StepExecutionContext Context(string projectPath, string subProjectName = "sub") => new()
    {
        ProjectName = "proj", SubProjectName = subProjectName, ProjectPath = projectPath,
        Environment = new DeployEnvironment { Name = "Local" }
    };

    private static Mock<IProcessRunner> SuccessfulBuildRunner(string projectPath)
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), projectPath, null, null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        return processRunner;
    }

    [Fact]
    public void Handles_ZipPublishOutput()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.ZipPublishOutput, executor.Handles);
    }

    [Fact]
    public async Task Creates_zip_alongside_publish_folder_without_deleting_it()
    {
        var publishFolder = CreateFakePublishFolder(out var projectPath);
        var processRunner = SuccessfulBuildRunner(projectPath);

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };

        var result = await executor.ExecuteAsync(step, Context(projectPath, "sub"));

        Assert.True(result.Success);
        Assert.True(Directory.Exists(publishFolder));
        Assert.Equal(2, Directory.EnumerateFiles(publishFolder).Count());
        var zipFiles = Directory.EnumerateFiles(Path.GetDirectoryName(publishFolder)!, "sub-*.zip").ToList();
        Assert.Single(zipFiles);
    }

    [Fact]
    public async Task Excludes_OmitFiles_from_the_zip_but_not_from_the_publish_folder()
    {
        var publishFolder = CreateFakePublishFolder(out var projectPath);
        var processRunner = SuccessfulBuildRunner(projectPath);

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip", Args = { ["OmitFiles"] = "app.pdb" } };

        await executor.ExecuteAsync(step, Context(projectPath, "sub"));

        Assert.True(File.Exists(Path.Combine(publishFolder, "app.pdb")));

        var zipPath = Directory.EnumerateFiles(Path.GetDirectoryName(publishFolder)!, "sub-*.zip").Single();
        using var zip = ZipFile.OpenRead(zipPath);
        Assert.Contains(zip.Entries, e => e.Name == "app.dll");
        Assert.DoesNotContain(zip.Entries, e => e.Name == "app.pdb");
    }

    [Fact]
    public async Task Returns_failure_when_a_build_command_fails()
    {
        var _ = CreateFakePublishFolder(out var projectPath);
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.IsAny<string>(), projectPath, null, null))
            .ReturnsAsync(new ProcessRunResult(1, "", "build error"));

        var executor = new ZipPublishExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "zip" };

        var result = await executor.ExecuteAsync(step, Context(projectPath));

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ZipPublishExecutorTests`
Expected: FAIL (`ExecuteAsync` no comprime nada todavía, no se crea ningún `.zip`)

- [ ] **Step 3: Reemplazar `ZipPublishExecutor.cs` completo**

```csharp
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Text;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class ZipPublishExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public ZipPublishExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.ZipPublishOutput;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (!Directory.Exists(context.ProjectPath))
        {
            stopwatch.Stop();
            return PathNotFoundResult(step, context.ProjectPath, stopwatch.Elapsed);
        }

        var combinedOutput = new StringBuilder();

        foreach (var command in BuildCommands(step))
        {
            var run = await _processRunner.RunAsync(command, context.ProjectPath);
            combinedOutput.AppendLine(run.StdOut);

            if (run.ExitCode != 0)
            {
                stopwatch.Stop();
                return FailureResult(step, run, combinedOutput.ToString(), stopwatch.Elapsed);
            }
        }

        var publishFolder = FindPublishFolder(context.ProjectPath);
        if (publishFolder == null)
        {
            stopwatch.Stop();
            return PublishFolderNotFoundResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
        }

        var omitFiles = ParseOmitFiles(step);
        var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
        combinedOutput.AppendLine($"Comprimido en: {zipPath}");

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), stopwatch.Elapsed);
    }

    private static string[] BuildCommands(DeployStep step)
    {
        var publishArgs = step.Args.GetValueOrDefault("PublishArgs", "");

        return CleanCommands()
            .Append("dotnet clean")
            .Append("dotnet build")
            .Append($"dotnet publish -c Release {publishArgs}".TrimEnd())
            .ToArray();
    }

    private static IEnumerable<string> CleanCommands()
    {
        if (OperatingSystem.IsWindows())
        {
            return new[] { "if exist bin rmdir /s /q bin", "if exist obj rmdir /s /q obj" };
        }

        return new[] { "rm -rf bin; rm -rf obj" };
    }

    private static string? FindPublishFolder(string projectPath)
    {
        var releaseFolder = Path.Combine(projectPath, "bin", "Release");
        if (!Directory.Exists(releaseFolder)) return null;

        return Directory.EnumerateDirectories(releaseFolder, "publish", SearchOption.AllDirectories).FirstOrDefault();
    }

    private static List<string> ParseOmitFiles(DeployStep step)
    {
        var raw = step.Args.GetValueOrDefault("OmitFiles", "");
        return string.IsNullOrEmpty(raw) ? new List<string>() : raw.Split('|').ToList();
    }

    private static string CreateZip(string publishFolder, string subProjectName, List<string> omitFiles)
    {
        var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
        var parentFolder = Directory.GetParent(publishFolder)?.FullName ?? publishFolder;
        var zipPath = Path.Combine(parentFolder, $"{subProjectName}-{timestamp}.zip");

        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var filePath in Directory.EnumerateFiles(publishFolder, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(publishFolder, filePath);
            if (omitFiles.Contains(relativePath, StringComparer.OrdinalIgnoreCase)) continue;
            zip.CreateEntryFromFile(filePath, relativePath.Replace('\\', '/'));
        }

        return zipPath;
    }

    private static StepResult PathNotFoundResult(DeployStep step, string path, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = -1,
        Error = $"El path del proyecto no existe: {path}", Duration = duration
    };

    private static StepResult PublishFolderNotFoundResult(DeployStep step, string output, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = -1,
        Output = output, Error = "No se encontró la carpeta 'publish' dentro de bin/Release tras el build.",
        Duration = duration
    };

    private static StepResult FailureResult(DeployStep step, ProcessRunResult run, string output, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = run.ExitCode, Output = output, Error = run.StdErr, Duration = duration
    };

    private static StepResult SuccessResult(DeployStep step, string output, TimeSpan duration) => new()
    {
        Step = step, Success = true, ExitCode = 0, Output = output, Duration = duration
    };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ZipPublishExecutorTests`
Expected: PASS (4/4)

- [ ] **Step 5: Pasar `OmitFiles` a través de `CreatePublishZipTemplate`**

Reemplazar en `vali-deploy/Application/PipelineTemplateFactory.cs`:

```csharp
    public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName)
    {
        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.ZipPublishOutput, Name = "Build, publish y comprimir output" },
            new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto" },
            new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
            new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
        };
    }
```

por:

```csharp
    public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName, List<string>? omitFiles = null)
    {
        var omitFilesArg = omitFiles is { Count: > 0 } ? string.Join("|", omitFiles) : "";

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.ZipPublishOutput, Name = "Build, publish y comprimir output", Args = { ["OmitFiles"] = omitFilesArg } },
            new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto" },
            new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
            new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
        };
    }
```

- [ ] **Step 6: Actualizar el call-site en `PipelineEditorMenu.cs` (completa el ajuste dejado pendiente en la Task 5)**

Reemplazar:

```csharp
            configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment, configSubProject.DockerRegistry)
                : factory.CreatePublishZipTemplate(projectName, configSubProject.Name);
```

por:

```csharp
            configSubProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, configSubProject.Name, environment, configSubProject.DockerRegistry)
                : factory.CreatePublishZipTemplate(projectName, configSubProject.Name, configSubProject.OmitFiles);
```

- [ ] **Step 7: Actualizar `PipelineTemplateFactoryTests` para el nuevo parámetro (verificar que el test existente sigue pasando sin cambios, ya que el parámetro es opcional)**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineTemplateFactoryTests`
Expected: PASS (13/13 — `PublishZip_template_follows_spec_order` sigue compilando porque `omitFiles` es opcional)

- [ ] **Step 8: Build completo + suite entera**

Run: `dotnet build vali-deploy.sln && dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: build y tests OK, sin regresiones

- [ ] **Step 9: Commit**

```bash
git add vali-deploy/Application/Executors/ZipPublishExecutor.cs vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy/Presentation/PipelineEditorMenu.cs vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "feat(application): completar ZipPublishExecutor con compresion real y OmitFiles"
```

---

### Task 11: `UpdaterManager` — migrar a GitHub Releases API

**Files:**
- Create: `vali-deploy/Models/GitHubRelease.cs`
- Modify: `vali-deploy/Models/UpdateInfo.cs`
- Modify: `vali-deploy/Utils/Constants.cs`
- Modify: `vali-deploy/Managers/UpdaterManager.cs`

**Nota:** `UpdaterManager`/`Program.cs` no tienen tests unitarios en este proyecto (dependen de `HttpClient` real sin abstracción inyectable — mismo patrón que el resto de `Managers/`). Esta task se verifica con build + smoke test manual, no con tests automatizados.

- [ ] **Step 1: Crear el modelo `GitHubRelease`**

```csharp
using System.Text.Json.Serialization;

namespace vali_deploy.Models;

public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = "";

    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    [JsonPropertyName("published_at")]
    public string PublishedAt { get; set; } = "";

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = new();
}

public class GitHubReleaseAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = "";
}
```

- [ ] **Step 2: Agregar `Checksums` a `UpdateInfo`**

Reemplazar `vali-deploy/Models/UpdateInfo.cs` completo:

```csharp
namespace vali_deploy.Models;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public Dictionary<string, string?> Downloads { get; set; } = new();
    public string ReleaseDate { get; set; } = "";
    public string ReleaseNotes { get; set; } = "";
    public Dictionary<string, string?> Checksums { get; set; } = new();
}
```

- [ ] **Step 3: Apuntar `Constants.UrlVersion` a la API de GitHub Releases**

En `vali-deploy/Utils/Constants.cs`, reemplazar la línea:

```csharp
    public const string UrlVersion = "https://vali-deploy.netlify.app/version/updates.json";
```

por:

```csharp
    public const string UrlVersion = "https://api.github.com/repos/UBF21/Vali-deploy/releases/latest";
```

(El nombre de la constante se mantiene igual a propósito — `Program.cs` la referencia como `Constants.UrlVersion` y no necesita ningún otro cambio.)

- [ ] **Step 4: Reemplazar `GetUpdateInfoAsync` para consultar GitHub y mapear a `UpdateInfo`**

En `vali-deploy/Managers/UpdaterManager.cs`, reemplazar:

```csharp
    // Este método solo consulta el JSON y devuelve la información de actualización si existe
    public static async Task<UpdateInfo?> GetUpdateInfoAsync(string url, string currentVersion)
    {
        try
        {
            using HttpClient client = new HttpClient();
            string jsonResponse = await client.GetStringAsync(url);
            var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(jsonResponse,
                options: new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            //updateInfo.Version
            if (updateInfo != null && Util.IsNewerVersion(updateInfo.Version, currentVersion))
            {
                return updateInfo;
            }

            AnsiConsole.MarkupLine("[blue]You already have the latest version.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red] :cross_mark: Error checking for updates: {Markup.Escape(ex.Message)}[/]");
        }

        return null;
    }
```

por:

```csharp
    private static readonly string[] KnownRuntimeIdentifiers = { "win-x64", "osx-x64", "osx-arm64", "linux-x64" };

    // Este método consulta la API de GitHub Releases y devuelve la información de actualización si existe
    public static async Task<UpdateInfo?> GetUpdateInfoAsync(string url, string currentVersion)
    {
        try
        {
            using HttpClient client = new HttpClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vali-Deploy-Updater");

            string jsonResponse = await client.GetStringAsync(url);
            var release = JsonSerializer.Deserialize<GitHubRelease>(jsonResponse,
                options: new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (release == null)
            {
                return null;
            }

            var updateInfo = MapToUpdateInfo(release);
            updateInfo.Checksums = await FetchChecksumsAsync(client, release);

            if (Util.IsNewerVersion(updateInfo.Version, currentVersion))
            {
                return updateInfo;
            }

            AnsiConsole.MarkupLine("[blue]You already have the latest version.[/]");
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red] :cross_mark: Error checking for updates: {Markup.Escape(ex.Message)}[/]");
        }

        return null;
    }

    private static UpdateInfo MapToUpdateInfo(GitHubRelease release)
    {
        var version = release.TagName.TrimStart('v');
        var downloads = new Dictionary<string, string?>();

        foreach (var rid in KnownRuntimeIdentifiers)
        {
            var asset = release.Assets.FirstOrDefault(a =>
                a.Name.Contains(rid, StringComparison.OrdinalIgnoreCase) && a.Name.EndsWith(".zip"));
            if (asset != null)
            {
                downloads[rid] = asset.BrowserDownloadUrl;
            }
        }

        return new UpdateInfo
        {
            Version = version,
            Downloads = downloads,
            ReleaseDate = release.PublishedAt,
            ReleaseNotes = release.Body
        };
    }

    private static async Task<Dictionary<string, string?>> FetchChecksumsAsync(HttpClient client, GitHubRelease release)
    {
        var checksums = new Dictionary<string, string?>();
        var checksumAsset = release.Assets.FirstOrDefault(a => a.Name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase));
        if (checksumAsset == null)
        {
            return checksums;
        }

        var content = await client.GetStringAsync(checksumAsset.BrowserDownloadUrl);

        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) continue;

            var hash = parts[0];
            var fileName = parts[1].TrimStart('*');

            foreach (var rid in KnownRuntimeIdentifiers)
            {
                if (fileName.Contains(rid, StringComparison.OrdinalIgnoreCase))
                {
                    checksums[rid] = hash;
                }
            }
        }

        return checksums;
    }
```

- [ ] **Step 5: Agregar el `using` de `vali_deploy.Models` para `GitHubRelease` (ya está — comparte namespace con `UpdateInfo`)**

Confirmá que el tope de `vali-deploy/Managers/UpdaterManager.cs` sigue teniendo `using vali_deploy.Models;` (ya estaba antes de este cambio, no hace falta agregarlo).

- [ ] **Step 6: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 7: Smoke test manual**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Verificar que el CLI arranca sin excepción no controlada al consultar `GetUpdateInfoAsync` (si no hay releases públicos en `UBF21/Vali-deploy` todavía, el catch existente debe mostrar el mensaje de error de forma controlada y continuar al menú, no crashear).

- [ ] **Step 8: Commit**

```bash
git add vali-deploy/Models/GitHubRelease.cs vali-deploy/Models/UpdateInfo.cs vali-deploy/Utils/Constants.cs vali-deploy/Managers/UpdaterManager.cs
git commit -m "feat(managers): migrar UpdaterManager de Netlify a GitHub Releases API"
```

---

### Task 12: `UpdaterManager` — verificación de checksum SHA256

**Depends on:** Task 11

**Files:**
- Modify: `vali-deploy/Managers/UpdaterManager.cs`
- Modify: `vali-deploy/Program.cs`

- [ ] **Step 1: Agregar el parámetro `expectedSha256` a `DownloadAndInstallAsync` y verificar antes de extraer**

En `vali-deploy/Managers/UpdaterManager.cs`, reemplazar:

```csharp
    // Método que descarga el ZIP, lo extrae y reemplaza el ejecutable actual.
    public static async Task DownloadAndInstallAsync(string downloadUrl, string newVersion)
    {
        // Obtener la carpeta donde está el ejecutable actual.
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string exeDirectory = Path.GetDirectoryName(currentExePath) ?? Environment.CurrentDirectory;

        // Derivar el nombre del ZIP a partir de la URL (se conserva el nombre real del archivo descargado).
        string downloadedZipFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string zipPath = Path.Combine(exeDirectory, downloadedZipFileName);
        // Ruta temporal de extracción.
        string tempExtractPath = Path.Combine(exeDirectory, "TempUpdate");

        // Estado de descarga.
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Downloading new version...", async ctx =>
            {
                using HttpClient client = new HttpClient();
                byte[] data = await client.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(zipPath, data);
            });
        AnsiConsole.MarkupLine("[green]:check_mark: Download completed.[/]");
```

por:

```csharp
    // Método que descarga el ZIP, verifica su checksum, lo extrae y reemplaza el ejecutable actual.
    public static async Task DownloadAndInstallAsync(string downloadUrl, string newVersion, string? expectedSha256 = null)
    {
        // Obtener la carpeta donde está el ejecutable actual.
        string currentExePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string exeDirectory = Path.GetDirectoryName(currentExePath) ?? Environment.CurrentDirectory;

        // Derivar el nombre del ZIP a partir de la URL (se conserva el nombre real del archivo descargado).
        string downloadedZipFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        string zipPath = Path.Combine(exeDirectory, downloadedZipFileName);
        // Ruta temporal de extracción.
        string tempExtractPath = Path.Combine(exeDirectory, "TempUpdate");

        // Estado de descarga.
        byte[] downloadedData = Array.Empty<byte>();
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots12)
            .StartAsync("Downloading new version...", async ctx =>
            {
                using HttpClient client = new HttpClient();
                downloadedData = await client.GetByteArrayAsync(downloadUrl);
                await File.WriteAllBytesAsync(zipPath, downloadedData);
            });
        AnsiConsole.MarkupLine("[green]:check_mark: Download completed.[/]");

        if (!string.IsNullOrEmpty(expectedSha256))
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(downloadedData)).ToLowerInvariant();
            if (!string.Equals(actualHash, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                AnsiConsole.MarkupLine(
                    $"[red]:cross_mark: Checksum verification failed. Expected {expectedSha256}, got {actualHash}. Aborting update.[/]");
                File.Delete(zipPath);
                return;
            }

            AnsiConsole.MarkupLine("[green]:check_mark: Checksum verified.[/]");
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]:warning: No checksum available for this release — skipping integrity verification.[/]");
        }
```

- [ ] **Step 2: Agregar el `using` de criptografía**

En `vali-deploy/Managers/UpdaterManager.cs`, reemplazar el bloque de `using` del tope del archivo:

```csharp
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;
```

por:

```csharp
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Spectre.Console;
using vali_deploy.Models;
using vali_deploy.Utils;
```

- [ ] **Step 3: Pasar el checksum esperado desde `Program.cs`**

En `vali-deploy/Program.cs`, reemplazar:

```csharp
            string osIdentifier = Util.GetOsIdentifier();
            if (updateInfo.Downloads.TryGetValue(osIdentifier, out string? downloadUrl))
            {
                if (downloadUrl != null) await UpdaterManager.DownloadAndInstallAsync(downloadUrl,updateInfo.Version);
                UpdaterManager.LaunchNewVersionAndExit();
            }
```

por:

```csharp
            string osIdentifier = Util.GetOsIdentifier();
            if (updateInfo.Downloads.TryGetValue(osIdentifier, out string? downloadUrl))
            {
                if (downloadUrl != null)
                {
                    updateInfo.Checksums.TryGetValue(osIdentifier, out string? expectedChecksum);
                    await UpdaterManager.DownloadAndInstallAsync(downloadUrl, updateInfo.Version, expectedChecksum);
                }
                UpdaterManager.LaunchNewVersionAndExit();
            }
```

- [ ] **Step 4: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 5: Correr toda la suite (regresión)**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, sin regresiones (esta task no tiene tests propios — `Managers/`/`Program.cs` no se testean, ver nota de la Task 11)

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Managers/UpdaterManager.cs vali-deploy/Program.cs
git commit -m "feat(managers): verificar checksum SHA256 antes de instalar una actualizacion"
```

---

### Task 13: Checklist de release — generar `SHA256SUMS.txt`

**Depends on:** Task 12

**Files:**
- Create: `docs/RELEASE_CHECKLIST.md`

**Por qué:** este paso vive fuera del código del CLI — es algo que hay que hacer a mano (o vía script) cada vez que se publica una versión nueva en GitHub Releases. Si se omite, el updater sigue funcionando pero salta la verificación de integridad (ver Task 12, muestra un warning, no bloquea el update).

- [ ] **Step 1: Crear el checklist**

```markdown
# Checklist de release — Vali-Deploy

Al publicar una versión nueva en GitHub Releases (`https://github.com/UBF21/Vali-deploy/releases`):

1. Generar los binarios self-contained para cada RID:
   ```bash
   dotnet publish vali-deploy/vali-deploy.csproj -r win-x64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r osx-x64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r osx-arm64 -c Release --self-contained true
   dotnet publish vali-deploy/vali-deploy.csproj -r linux-x64 -c Release --self-contained true
   ```

2. Comprimir cada carpeta de publish a `.zip`, nombrados `Vali-Deploy_<version>-<rid>.zip` (ej. `Vali-Deploy_1.2.0-win-x64.zip`) — el nombre debe **contener el RID** (`win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`) para que `UpdaterManager.MapToUpdateInfo` lo pueda mapear.

3. Generar `SHA256SUMS.txt` en la misma carpeta que los `.zip`:

   PowerShell (Windows):
   ```powershell
   Get-ChildItem *.zip | ForEach-Object { "$((Get-FileHash $_.Name -Algorithm SHA256).Hash.ToLower())  $($_.Name)" } | Out-File -Encoding utf8 SHA256SUMS.txt
   ```

   Bash (Linux/macOS):
   ```bash
   sha256sum *.zip > SHA256SUMS.txt
   ```

4. Crear el release en GitHub, subiendo TODOS los `.zip` más `SHA256SUMS.txt` como assets:
   ```bash
   gh release create v<version> *.zip SHA256SUMS.txt --title "v<version>" --notes "<release notes>"
   ```

5. Verificar: correr una versión vieja del CLI y confirmar que detecta la actualización nueva, descarga, valida el checksum sin error ("Checksum verified.") e instala correctamente.

Si se olvida subir `SHA256SUMS.txt`, el updater sigue funcionando pero muestra "No checksum available for this release — skipping integrity verification." y no bloquea el update.
```

- [ ] **Step 2: Commit**

```bash
git add docs/RELEASE_CHECKLIST.md
git commit -m "docs: agregar checklist de release con generacion de SHA256SUMS.txt"
```

---

### Task 14: Verificación final del ciclo completo

**Depends on:** Tasks 1-13 (todas)

- [ ] **Step 1: Build completo de la solución**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`, 0 errores.

- [ ] **Step 2: Suite de tests completa**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, 0 fallos. El conteo total debería ser 104 (baseline previo al ciclo) + los tests nuevos de este plan (aprox. 2 DockerRegistry + 2 ProjectRepository + 1 ProcessRunner + 5 DockerPushExecutor + 1 CompositionRoot sin cambio de conteo + 3 PipelineTemplateFactory de registry + 1 DockerRunExecutor + 5 PipelineTemplateFactory de templates locales + 4 ZipPublishExecutor ≈ 24 tests nuevos).

- [ ] **Step 3: Confirmar que no queda código muerto**

Run: `grep -rn "CommandExecutor\|DockerHubUser\b" vali-deploy --include=*.cs`
Expected: sin ocurrencias de `CommandExecutor`. `DockerHubUser` puede aparecer solo en `SubProject.cs` (el campo legacy de migración, intencional) y en `ProjectRepositoryTests.cs`/`ProjectRepository.cs` (la lógica de migración) — no en ningún otro lugar.

- [ ] **Step 4: Recorrido funcional manual (no automatizable — `Presentation`/`Managers` sin tests unitarios)**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Verificar:
- Un subproyecto sin `PipelinesByEnvironment` configurado: "Generate Microsoft publish" corre, genera el `.zip` junto a la carpeta de publish sin borrarla.
- Si tiene `DockerfilePath`: "Docker Build" corre y verifica exit code (probar con un Dockerfile roto a propósito — debe mostrar el fallo, no seguir adelante silenciosamente).
- "Docker Run" abre una sesión interactiva real dentro del contenedor (confirmar que se puede escribir/ver output en vivo, no que queda colgado ni sin output).
- "Push to registry" (primera vez, sin `DockerRegistry` configurado): pide usuario/host/token, persiste, y el push corre — probar también con Docker Hub (host vacío) y con un registry con host explícito si hay uno de prueba disponible.
- El header persistente (breadcrumb) se mantiene visible durante toda esta ejecución, igual que en el resto del CLI.
