# Despliegue remoto SSH + pipeline configurable — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extender Vali-Deploy con despliegue remoto SSH (Windows/Linux) y un pipeline de pasos configurable por `SubProject`/`DeployEnvironment`, migrando `MenuManager.cs` (1406 líneas, God Class) hacia Clean Architecture sin romper el CLI existente en ningún paso intermedio.

**Architecture:** Domain (`DeployStep`/`StepType`, `DeployEnvironment`, `RemoteServer`, resultados) → Application (`IStepExecutor` por tipo de paso, `PipelineRunner` con retry/`ContinueOnFailure`, `PipelineTemplateFactory`) → Infrastructure (`ProcessRunner` unificado, `SshClientFactory` sobre SSH.NET, `PipelineLogger`, `ProjectRepository`) → Presentation (`PipelineExecutionView`, `EnvironmentMenu`, `PipelineEditorMenu`, `MenuManager` adelgazado). Migración incremental: cada tarea deja el CLI compilando y funcionando: código nuevo convive con el viejo hasta la Tarea 31, que es la única que reemplaza comportamiento real en `MenuManager`.

**Tech Stack:** .NET 7.0, Spectre.Console 0.49.1, System.Text.Json, xUnit + Moq (nuevos), SSH.NET (`Renci.SshNet`, único paquete NuGet nuevo).

**Spec de referencia:** `docs/superpowers/specs/2026-07-08-ssh-deploy-pipeline-design.md`

**Mapa del código actual** (para no releer los archivos originales durante la ejecución):
- `Program.cs`: 3 puntos de entrada a `MenuManager.StartAsync()` (L41, L47, L53) tras el flujo de auto-actualización.
- `Managers/MenuManager.cs` (1406 líneas): estática, sin DI, estado en `_projects`/`_barChart` (L13-14). Método objetivo: `ExecuteCommandSubProject` (L707-807) — arma comandos Docker a mano (`docker build` L746-747, `docker run` L765, `docker tag`/`docker push` L789/793) y publish (L730-735 vía `CommandExecutor.RunCommandsAsync`).
- `Managers/CommandExecutor.cs` (263 líneas): `RunCommandsAsync` (L10) **no verifica exit code**; `ExecuteDockerCommandAsync` (L147) sí retorna `process.ExitCode` (L174). `ExecuteCommandAsync` privado (L128) es el que corre cada comando sin chequear resultado. `CreateProcessStartInfo` (L177) decide `cmd.exe /c` vs `/bin/bash -c` vía `OperatingSystem.IsWindows()` — misma lógica duplicada inline en `ExecuteDockerCommandAsync` L149-159.
- `Managers/ProjectManager.cs` (143 líneas, namespace en bloque no file-scoped): `ConfigPath` = `%USERPROFILE%\Documents\vali-deploy\deploy_config.json` (L9-13). `LoadOrCreateConfig` (L18), `SaveConfig` (L54, solo usa `WriteIndented = true`), `AddProject` (L64), `RemoveProject` (L80).
- `Models/Project.cs`: `Path`, `SubProjects`. `Models/SubProject.cs`: `Name`, `Path`, `OmitFiles`, `DockerfilePath?`, `DockerRunArgs?`, `DockerBuildArgs?`, `DockerHubUser?` (credencial en texto plano, L11), `PublishArgs?`, `ZipPublishOutput` (default `true`).
- `vali-deploy.csproj`: `net7.0`, únicas deps `Spectre.Console`/`Spectre.Console.Cli` 0.49.1. `vali-deploy.sln`: un solo proyecto, GUID `{737A7AF3-CCFD-45E1-8F84-2C5236821EEE}`.
- No existe ningún test ni proyecto de test hoy.

---

## Task 1: Crear proyecto de tests `vali-deploy.Tests`

**Files:**
- Create: `vali-deploy.Tests/vali-deploy.Tests.csproj`
- Create: `vali-deploy.Tests/GlobalUsings.cs`
- Create: `vali-deploy.Tests/Sanity/SanityTest.cs`
- Modify: `vali-deploy.sln`

- [ ] **Step 1: Crear el csproj del proyecto de tests**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>vali_deploy.Tests</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.6.6" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.6" />
    <PackageReference Include="Moq" Version="4.20.70" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\vali-deploy\vali-deploy.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Crear GlobalUsings.cs**

```csharp
global using Xunit;
global using Moq;
```

- [ ] **Step 3: Crear un test de humo para confirmar que el proyecto compila y corre**

```csharp
namespace vali_deploy.Tests.Sanity;

public class SanityTest
{
    [Fact]
    public void True_is_true()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 4: Agregar el proyecto a la solución**

Run: `dotnet sln vali-deploy.sln add vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: `Project(s) added to the solution.` y `vali-deploy.sln` gana un bloque `Project(...)` nuevo con GUID generado automáticamente.

- [ ] **Step 5: Restaurar y correr**

Run: `dotnet test vali-deploy.sln`
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0` (el sanity test).

- [ ] **Step 6: Commit**

```bash
git add vali-deploy.Tests vali-deploy.sln
git commit -m "chore(tests): agregar proyecto vali-deploy.Tests (xUnit + Moq)"
```

---

## Task 2: Domain — `StepType`, `DeployStep`

**Files:**
- Create: `vali-deploy/Domain/StepType.cs`
- Create: `vali-deploy/Domain/DeployStep.cs`
- Test: `vali-deploy.Tests/Domain/DeployStepTests.cs`

- [ ] **Step 1: Escribir el test primero**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployStepTests
{
    [Fact]
    public void New_step_has_no_retries_and_stops_pipeline_on_failure_by_default()
    {
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "clean" };

        Assert.Equal(0, step.RetryCount);
        Assert.False(step.ContinueOnFailure);
        Assert.Empty(step.Args);
    }

    [Fact]
    public void All_step_types_from_spec_exist()
    {
        var expected = new[]
        {
            "GitCheckout", "LocalCommand", "DockerBuild", "DockerPush", "DockerSave", "DockerLoad",
            "DockerImagePrune", "DockerComposePull", "DockerComposeUp", "DockerComposeDown",
            "ZipPublishOutput", "CopyToRemote", "SshCommand", "RawCommand"
        };

        var actual = Enum.GetNames<StepType>();

        Assert.Equal(expected.OrderBy(x => x), actual.OrderBy(x => x));
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter DeployStepTests`
Expected: FAIL — `vali_deploy.Domain` no existe todavía (CS0234).

- [ ] **Step 3: Crear `StepType.cs`**

```csharp
namespace vali_deploy.Domain;

public enum StepType
{
    GitCheckout,
    LocalCommand,
    DockerBuild,
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

- [ ] **Step 4: Crear `DeployStep.cs`**

```csharp
namespace vali_deploy.Domain;

public class DeployStep
{
    public StepType Type { get; set; }
    public string Name { get; set; } = "";
    public Dictionary<string, string> Args { get; set; } = new();
    public bool ContinueOnFailure { get; set; } = false;
    public int RetryCount { get; set; } = 0;
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter DeployStepTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Domain/StepType.cs vali-deploy/Domain/DeployStep.cs vali-deploy.Tests/Domain/DeployStepTests.cs
git commit -m "feat(domain): agregar StepType y DeployStep"
```

---

## Task 3: Domain — `RemoteOs`, `RemoteServer`

**Files:**
- Create: `vali-deploy/Domain/RemoteOs.cs`
- Create: `vali-deploy/Domain/RemoteServer.cs`
- Test: `vali-deploy.Tests/Domain/RemoteServerTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class RemoteServerTests
{
    [Fact]
    public void Default_port_is_22_and_passphrase_env_var_is_optional()
    {
        var server = new RemoteServer
        {
            Host = "192.168.1.10",
            User = "deploy",
            Os = RemoteOs.Linux,
            PrivateKeyPath = "/home/deploy/.ssh/id_rsa"
        };

        Assert.Equal(22, server.Port);
        Assert.Null(server.PassphraseEnvVar);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter RemoteServerTests`
Expected: FAIL — tipo `RemoteServer` no existe (CS0246).

- [ ] **Step 3: Crear `RemoteOs.cs`**

```csharp
namespace vali_deploy.Domain;

public enum RemoteOs
{
    Windows,
    Linux
}
```

- [ ] **Step 4: Crear `RemoteServer.cs`**

```csharp
namespace vali_deploy.Domain;

public class RemoteServer
{
    public string Host { get; set; } = "";
    public int Port { get; set; } = 22;
    public string User { get; set; } = "";
    public RemoteOs Os { get; set; }
    public string PrivateKeyPath { get; set; } = "";
    public string? PassphraseEnvVar { get; set; }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter RemoteServerTests`
Expected: `Passed!  - Failed: 0, Passed: 1, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Domain/RemoteOs.cs vali-deploy/Domain/RemoteServer.cs vali-deploy.Tests/Domain/RemoteServerTests.cs
git commit -m "feat(domain): agregar RemoteOs y RemoteServer"
```

---

## Task 4: Domain — `DeployEnvironment`

**Files:**
- Create: `vali-deploy/Domain/DeployEnvironment.cs`
- Test: `vali-deploy.Tests/Domain/DeployEnvironmentTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployEnvironmentTests
{
    [Fact]
    public void Environment_without_server_means_no_remote_deploy()
    {
        var dev = new DeployEnvironment { Name = "DEV" };

        Assert.Null(dev.Server);
        Assert.Null(dev.DefaultBranch);
    }

    [Fact]
    public void Environment_with_server_carries_default_branch_for_prod()
    {
        var prod = new DeployEnvironment
        {
            Name = "PROD",
            DefaultBranch = "main",
            Server = new RemoteServer
            {
                Host = "prod.example.com",
                User = "deploy",
                Os = RemoteOs.Linux,
                PrivateKeyPath = "/home/deploy/.ssh/id_rsa"
            }
        };

        Assert.Equal("main", prod.DefaultBranch);
        Assert.NotNull(prod.Server);
        Assert.Equal(RemoteOs.Linux, prod.Server!.Os);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter DeployEnvironmentTests`
Expected: FAIL — `DeployEnvironment` no existe (CS0246).

- [ ] **Step 3: Crear `DeployEnvironment.cs`**

```csharp
namespace vali_deploy.Domain;

public class DeployEnvironment
{
    public string Name { get; set; } = "";
    public RemoteServer? Server { get; set; }
    public string? DefaultBranch { get; set; }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter DeployEnvironmentTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Domain/DeployEnvironment.cs vali-deploy.Tests/Domain/DeployEnvironmentTests.cs
git commit -m "feat(domain): agregar DeployEnvironment"
```

---

## Task 5: Domain — `StepResult`, `PipelineResult`

**Files:**
- Create: `vali-deploy/Domain/StepResult.cs`
- Create: `vali-deploy/Domain/PipelineResult.cs`
- Test: `vali-deploy.Tests/Domain/PipelineResultTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class PipelineResultTests
{
    [Fact]
    public void Pipeline_succeeds_when_all_steps_succeed()
    {
        var results = new List<StepResult>
        {
            new() { Step = new DeployStep { Name = "clean" }, ExitCode = 0, Success = true },
            new() { Step = new DeployStep { Name = "build" }, ExitCode = 0, Success = true }
        };

        var pipelineResult = new PipelineResult { Steps = results, Success = results.All(r => r.Success) };

        Assert.True(pipelineResult.Success);
    }

    [Fact]
    public void Pipeline_fails_when_any_step_fails()
    {
        var results = new List<StepResult>
        {
            new() { Step = new DeployStep { Name = "clean" }, ExitCode = 0, Success = true },
            new() { Step = new DeployStep { Name = "build" }, ExitCode = 1, Success = false }
        };

        var pipelineResult = new PipelineResult { Steps = results, Success = results.All(r => r.Success) };

        Assert.False(pipelineResult.Success);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter PipelineResultTests`
Expected: FAIL — `StepResult`/`PipelineResult` no existen (CS0246).

- [ ] **Step 3: Crear `StepResult.cs`**

```csharp
namespace vali_deploy.Domain;

public class StepResult
{
    public DeployStep Step { get; set; } = new();
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public bool WasSkippedDueToContinueOnFailure { get; set; } = false;
}
```

- [ ] **Step 4: Crear `PipelineResult.cs`**

```csharp
namespace vali_deploy.Domain;

public class PipelineResult
{
    public bool Success { get; set; }
    public List<StepResult> Steps { get; set; } = new();
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter PipelineResultTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Domain/StepResult.cs vali-deploy/Domain/PipelineResult.cs vali-deploy.Tests/Domain/PipelineResultTests.cs
git commit -m "feat(domain): agregar StepResult y PipelineResult"
```

---

## Task 6: Domain — extender `SubProject` con `PipelinesByEnvironment`

**Files:**
- Modify: `vali-deploy/Models/SubProject.cs`
- Test: `vali-deploy.Tests/Models/SubProjectTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;
using vali_deploy.Models;

namespace vali_deploy.Tests.Models;

public class SubProjectTests
{
    [Fact]
    public void New_subproject_has_no_pipelines_and_no_registry_token_configured()
    {
        var subProject = new SubProject { Name = "api", Path = "src/api" };

        Assert.Empty(subProject.PipelinesByEnvironment);
        Assert.Null(subProject.DockerRegistryTokenEnvVar);
    }

    [Fact]
    public void Pipeline_can_be_assigned_per_environment_name()
    {
        var subProject = new SubProject { Name = "api", Path = "src/api" };
        subProject.PipelinesByEnvironment["QA"] = new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "checkout" }
        };

        Assert.Single(subProject.PipelinesByEnvironment["QA"]);
        Assert.Equal(StepType.GitCheckout, subProject.PipelinesByEnvironment["QA"][0].Type);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter SubProjectTests`
Expected: FAIL — `SubProject` no tiene `PipelinesByEnvironment` ni `DockerRegistryTokenEnvVar` (CS1061).

- [ ] **Step 3: Modificar `SubProject.cs`**

Archivo actual completo (14 líneas, namespace en bloque con indentación de 4 espacios extra — se preserva el estilo del archivo original para minimizar el diff):

```csharp
    namespace vali_deploy.Models;

    public class SubProject
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public List<string> OmitFiles { get; set; } = new();
        public string? DockerfilePath { get; set; }
        public List<string>? DockerRunArgs { get; set; }
        public List<string>? DockerBuildArgs { get; set; }
        public string? DockerHubUser { get; set; }
        public List<string>? PublishArgs { get; set; }
        public bool ZipPublishOutput { get; set; } = true;
        public Dictionary<string, List<vali_deploy.Domain.DeployStep>> PipelinesByEnvironment { get; set; } = new();
        public string? DockerRegistryTokenEnvVar { get; set; }
    }
```

Nota: `DockerHubUser` **se mantiene** en este paso — no se borra ni se migra todavía, para no romper `ExecuteCommandSubProject` (Task 31 es la que reemplaza su uso). `DockerRegistryTokenEnvVar` es el reemplazo planeado, conviven ambos campos hasta el refactor final del menú.

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter SubProjectTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: Correr toda la suite para confirmar que no rompió nada existente**

Run: `dotnet build vali-deploy.sln && dotnet test vali-deploy.sln`
Expected: build exitoso, todos los tests pasan.

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Models/SubProject.cs vali-deploy.Tests/Models/SubProjectTests.cs
git commit -m "feat(domain): extender SubProject con PipelinesByEnvironment y DockerRegistryTokenEnvVar"
```

---

## Task 7: Application — `ISecretResolver` + `EnvVarSecretResolver`

**Files:**
- Create: `vali-deploy/Application/ISecretResolver.cs`
- Create: `vali-deploy/Infrastructure/EnvVarSecretResolver.cs`
- Test: `vali-deploy.Tests/Infrastructure/EnvVarSecretResolverTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class EnvVarSecretResolverTests
{
    [Fact]
    public void Resolve_returns_value_when_env_var_exists()
    {
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_SECRET", "s3cr3t");
        var resolver = new EnvVarSecretResolver();

        var value = resolver.Resolve("VALI_DEPLOY_TEST_SECRET");

        Assert.Equal("s3cr3t", value);
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_SECRET", null);
    }

    [Fact]
    public void Resolve_throws_explicit_error_when_env_var_missing()
    {
        Environment.SetEnvironmentVariable("VALI_DEPLOY_TEST_MISSING", null);
        var resolver = new EnvVarSecretResolver();

        var ex = Assert.Throws<InvalidOperationException>(() => resolver.Resolve("VALI_DEPLOY_TEST_MISSING"));
        Assert.Contains("VALI_DEPLOY_TEST_MISSING", ex.Message);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter EnvVarSecretResolverTests`
Expected: FAIL — `EnvVarSecretResolver` no existe (CS0246).

- [ ] **Step 3: Crear `ISecretResolver.cs`**

```csharp
namespace vali_deploy.Application;

public interface ISecretResolver
{
    string Resolve(string envVarName);
}
```

- [ ] **Step 4: Crear `EnvVarSecretResolver.cs`**

```csharp
using vali_deploy.Application;

namespace vali_deploy.Infrastructure;

public class EnvVarSecretResolver : ISecretResolver
{
    public string Resolve(string envVarName)
    {
        var value = Environment.GetEnvironmentVariable(envVarName);

        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException(
                $"La variable de entorno '{envVarName}' no está definida o está vacía. " +
                "Configurala antes de correr el pipeline.");
        }

        return value;
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter EnvVarSecretResolverTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Application/ISecretResolver.cs vali-deploy/Infrastructure/EnvVarSecretResolver.cs vali-deploy.Tests/Infrastructure/EnvVarSecretResolverTests.cs
git commit -m "feat(infra): agregar EnvVarSecretResolver"
```

---

## Task 8: Infrastructure — `ProcessRunner` (unifica `CreateProcessStartInfo` + verifica exit code)

**Files:**
- Create: `vali-deploy/Infrastructure/IProcessRunner.cs`
- Create: `vali-deploy/Infrastructure/ProcessRunner.cs`
- Test: `vali-deploy.Tests/Infrastructure/ProcessRunnerTests.cs`

Este componente reemplaza la lógica duplicada de `CommandExecutor.CreateProcessStartInfo` (L177-196) + el inline de `ExecuteDockerCommandAsync` (L149-159): un único punto que decide `cmd.exe /c` vs `/bin/bash -c` y **siempre** retorna exit code + stdout/stderr — corrige la falencia de `RunCommandsAsync`/`ExecuteCommandAsync` que hoy no lo verifica. `CommandExecutor.cs` no se toca ni se borra en esta tarea (sigue siendo el código que corre el menú viejo); `ProcessRunner` es la base sobre la que se construyen los executores nuevos.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_returns_exit_code_zero_and_captures_stdout_on_success()
    {
        var runner = new ProcessRunner();
        var command = OperatingSystem.IsWindows() ? "echo hola" : "echo hola";

        var result = await runner.RunAsync(command, Directory.GetCurrentDirectory());

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hola", result.StdOut);
    }

    [Fact]
    public async Task RunAsync_returns_nonzero_exit_code_on_failure()
    {
        var runner = new ProcessRunner();
        var command = OperatingSystem.IsWindows() ? "exit 3" : "exit 3";

        var result = await runner.RunAsync(command, Directory.GetCurrentDirectory());

        Assert.Equal(3, result.ExitCode);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter ProcessRunnerTests`
Expected: FAIL — `ProcessRunner` no existe (CS0246).

- [ ] **Step 3: Crear `IProcessRunner.cs`**

```csharp
namespace vali_deploy.Infrastructure;

public record ProcessRunResult(int ExitCode, string StdOut, string StdErr);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null);
}
```

- [ ] **Step 4: Crear `ProcessRunner.cs`**

```csharp
using System.Diagnostics;
using System.Text;

namespace vali_deploy.Infrastructure;

public class ProcessRunner : IProcessRunner
{
    public async Task<ProcessRunResult> RunAsync(string command, string workingDirectory, IDictionary<string, string>? extraEnvVars = null)
    {
        var startInfo = CreateProcessStartInfo(command, workingDirectory);

        if (extraEnvVars != null)
        {
            foreach (var (key, value) in extraEnvVars)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var stdOut = new StringBuilder();
        var stdErr = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync();

        return new ProcessRunResult(process.ExitCode, stdOut.ToString(), stdErr.ToString());
    }

    private static ProcessStartInfo CreateProcessStartInfo(string command, string workingDirectory)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException("Sistema operativo no soportado para ejecutar comandos locales.");
        }

        return new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/bash",
            Arguments = OperatingSystem.IsWindows() ? $"/c {command}" : $"-c \"{command}\"",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter ProcessRunnerTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Infrastructure/IProcessRunner.cs vali-deploy/Infrastructure/ProcessRunner.cs vali-deploy.Tests/Infrastructure/ProcessRunnerTests.cs
git commit -m "feat(infra): agregar ProcessRunner unificado con verificación de exit code"
```

---

## Task 9: Application — `IStepExecutor` + `StepExecutionContext`

**Files:**
- Create: `vali-deploy/Application/StepExecutionContext.cs`
- Create: `vali-deploy/Application/IStepExecutor.cs`

No lleva test propio (es una interfaz + un DTO sin lógica) — se verifica indirectamente en cada tarea de executor concreto (Tasks 11-21).

- [ ] **Step 1: Crear `StepExecutionContext.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public class StepExecutionContext
{
    public required string ProjectName { get; init; }
    public required string SubProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public required DeployEnvironment Environment { get; init; }
}
```

- [ ] **Step 2: Crear `IStepExecutor.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public interface IStepExecutor
{
    StepType Handles { get; }
    Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context);
}
```

- [ ] **Step 3: Confirmar que compila**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Application/StepExecutionContext.cs vali-deploy/Application/IStepExecutor.cs
git commit -m "feat(application): agregar IStepExecutor y StepExecutionContext"
```

---

## Task 10: Application — `IPipelineRunner` / `PipelineRunner` (retry + `ContinueOnFailure`)

**Files:**
- Create: `vali-deploy/Application/IPipelineRunner.cs`
- Create: `vali-deploy/Application/PipelineRunner.cs`
- Test: `vali-deploy.Tests/Application/PipelineRunnerTests.cs`

Implementa el algoritmo del spec (sección "Ejecución, errores y logging"): por cada `DeployStep`, ejecuta con el `IStepExecutor` correspondiente a `step.Type`; si falla, reintenta según `RetryCount` con backoff (1s, 3s, 5s), si se agotan los reintentos y `ContinueOnFailure` es true continúa con warning, si no corta el pipeline.

- [ ] **Step 1: Escribir los tests (usando Moq sobre `IStepExecutor`)**

```csharp
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class PipelineRunnerTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj",
        SubProjectName = "sub",
        ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public async Task Pipeline_succeeds_when_all_steps_succeed()
    {
        var executor = new Mock<IStepExecutor>();
        executor.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        executor.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { executor.Object });
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "clean" } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.True(result.Success);
        Assert.Single(result.Steps);
    }

    [Fact]
    public async Task Pipeline_stops_at_first_failure_by_default()
    {
        var failing = new Mock<IStepExecutor>();
        failing.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        failing.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = false, ExitCode = 1 });

        var neverCalled = new Mock<IStepExecutor>();
        neverCalled.Setup(e => e.Handles).Returns(StepType.DockerBuild);
        neverCalled.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { failing.Object, neverCalled.Object });
        var steps = new List<DeployStep>
        {
            new() { Type = StepType.LocalCommand, Name = "clean" },
            new() { Type = StepType.DockerBuild, Name = "build" }
        };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Single(result.Steps);
        neverCalled.Verify(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()), Times.Never);
    }

    [Fact]
    public async Task Pipeline_continues_after_failure_when_ContinueOnFailure_is_true()
    {
        var failing = new Mock<IStepExecutor>();
        failing.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        failing.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = false, ExitCode = 1 });

        var next = new Mock<IStepExecutor>();
        next.Setup(e => e.Handles).Returns(StepType.DockerBuild);
        next.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) => new StepResult { Step = s, Success = true, ExitCode = 0 });

        var runner = new PipelineRunner(new[] { failing.Object, next.Object });
        var steps = new List<DeployStep>
        {
            new() { Type = StepType.LocalCommand, Name = "clean", ContinueOnFailure = true },
            new() { Type = StepType.DockerBuild, Name = "build" }
        };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Equal(2, result.Steps.Count);
        next.Verify(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()), Times.Once);
    }

    [Fact]
    public async Task Step_retries_until_RetryCount_exhausted_then_fails()
    {
        var callCount = 0;
        var flaky = new Mock<IStepExecutor>();
        flaky.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        flaky.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) =>
            {
                callCount++;
                return new StepResult { Step = s, Success = false, ExitCode = 1 };
            });

        var runner = new PipelineRunner(new[] { flaky.Object }, retryDelayProvider: _ => TimeSpan.Zero);
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "flaky", RetryCount = 2 } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.False(result.Success);
        Assert.Equal(3, callCount); // intento inicial + 2 reintentos
    }

    [Fact]
    public async Task Step_succeeds_on_retry_without_exhausting_all_attempts()
    {
        var callCount = 0;
        var flaky = new Mock<IStepExecutor>();
        flaky.Setup(e => e.Handles).Returns(StepType.LocalCommand);
        flaky.Setup(e => e.ExecuteAsync(It.IsAny<DeployStep>(), It.IsAny<StepExecutionContext>()))
            .ReturnsAsync((DeployStep s, StepExecutionContext _) =>
            {
                callCount++;
                return new StepResult { Step = s, Success = callCount >= 2, ExitCode = callCount >= 2 ? 0 : 1 };
            });

        var runner = new PipelineRunner(new[] { flaky.Object }, retryDelayProvider: _ => TimeSpan.Zero);
        var steps = new List<DeployStep> { new() { Type = StepType.LocalCommand, Name = "flaky", RetryCount = 3 } };

        var result = await runner.RunAsync(steps, Context(), progress: null);

        Assert.True(result.Success);
        Assert.Equal(2, callCount);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter PipelineRunnerTests`
Expected: FAIL — `PipelineRunner`/`IPipelineRunner` no existen (CS0246).

- [ ] **Step 3: Crear `IPipelineRunner.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public interface IPipelineRunner
{
    Task<PipelineResult> RunAsync(List<DeployStep> pipeline, StepExecutionContext context, IProgress<StepResult>? progress);
}
```

- [ ] **Step 4: Crear `PipelineRunner.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineRunner : IPipelineRunner
{
    private static readonly TimeSpan[] DefaultBackoff = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5) };

    private readonly Dictionary<StepType, IStepExecutor> _executors;
    private readonly Func<int, TimeSpan> _retryDelayProvider;

    public PipelineRunner(IEnumerable<IStepExecutor> executors, Func<int, TimeSpan>? retryDelayProvider = null)
    {
        _executors = executors.ToDictionary(e => e.Handles);
        _retryDelayProvider = retryDelayProvider ?? (attempt => DefaultBackoff[Math.Min(attempt, DefaultBackoff.Length - 1)]);
    }

    public async Task<PipelineResult> RunAsync(List<DeployStep> pipeline, StepExecutionContext context, IProgress<StepResult>? progress)
    {
        var stepResults = new List<StepResult>();

        foreach (var step in pipeline)
        {
            if (!_executors.TryGetValue(step.Type, out var executor))
            {
                throw new InvalidOperationException($"No hay IStepExecutor registrado para StepType.{step.Type}.");
            }

            var result = await ExecuteWithRetryAsync(executor, step, context);
            stepResults.Add(result);
            progress?.Report(result);

            if (!result.Success && !step.ContinueOnFailure)
            {
                return new PipelineResult { Success = false, Steps = stepResults };
            }
        }

        return new PipelineResult { Success = stepResults.All(r => r.Success), Steps = stepResults };
    }

    private async Task<StepResult> ExecuteWithRetryAsync(IStepExecutor executor, DeployStep step, StepExecutionContext context)
    {
        StepResult result;
        var attempt = 1;

        while (true)
        {
            result = await executor.ExecuteAsync(step, context);
            result.AttemptNumber = attempt;

            if (result.Success || attempt > step.RetryCount)
            {
                return result;
            }

            await Task.Delay(_retryDelayProvider(attempt - 1));
            attempt++;
        }
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter PipelineRunnerTests`
Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Application/IPipelineRunner.cs vali-deploy/Application/PipelineRunner.cs vali-deploy.Tests/Application/PipelineRunnerTests.cs
git commit -m "feat(application): agregar PipelineRunner con retry y ContinueOnFailure"
```

---

## Task 11: Executor — `LocalCommandExecutor` y `RawCommandExecutor`

**Files:**
- Create: `vali-deploy/Application/Executors/LocalCommandExecutor.cs`
- Create: `vali-deploy/Application/Executors/RawCommandExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/LocalCommandExecutorTests.cs`
- Test: `vali-deploy.Tests/Application/Executors/RawCommandExecutorTests.cs`

Ambos son el patrón más simple: delegan a `IProcessRunner.RunAsync` sobre `step.Args["Command"]`. Se agrupan en una tarea porque son casi idénticos — `RawCommand` es el escape hatch mencionado en el spec, mismo comportamiento que `LocalCommand`, solo se diferencian por `StepType` para que quede explícito en el pipeline cuál es cuál.

- [ ] **Step 1: Escribir el test de `LocalCommandExecutor`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class LocalCommandExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj",
        SubProjectName = "sub",
        ProjectPath = Directory.GetCurrentDirectory(),
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_LocalCommand()
    {
        var executor = new LocalCommandExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.LocalCommand, executor.Handles);
    }

    [Fact]
    public async Task ExecuteAsync_runs_Args_Command_in_ProjectPath_and_reports_success_on_exit_zero()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("dotnet build", Context().ProjectPath, null))
            .ReturnsAsync(new ProcessRunResult(0, "Build succeeded", ""));

        var executor = new LocalCommandExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "build", Args = { ["Command"] = "dotnet build" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Build succeeded", result.Output);
    }

    [Fact]
    public async Task ExecuteAsync_reports_failure_on_nonzero_exit()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync(It.IsAny<string>(), It.IsAny<string>(), null))
            .ReturnsAsync(new ProcessRunResult(1, "", "error CS0000"));

        var executor = new LocalCommandExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.LocalCommand, Name = "build", Args = { ["Command"] = "dotnet build" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("error CS0000", result.Error);
    }
}
```

- [ ] **Step 2: Escribir el test de `RawCommandExecutor`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class RawCommandExecutorTests
{
    [Fact]
    public void Handles_RawCommand()
    {
        var executor = new RawCommandExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.RawCommand, executor.Handles);
    }

    [Fact]
    public async Task ExecuteAsync_runs_Args_Command_verbatim()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("echo custom", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "custom", ""));

        var executor = new RawCommandExecutor(processRunner.Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "QA" }
        };
        var step = new DeployStep { Type = StepType.RawCommand, Name = "custom", Args = { ["Command"] = "echo custom" } };

        var result = await executor.ExecuteAsync(step, context);

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 3: Correr y verificar que ambos fallan**

Run: `dotnet test --filter "LocalCommandExecutorTests|RawCommandExecutorTests"`
Expected: FAIL — los executores no existen (CS0246).

- [ ] **Step 4: Crear `LocalCommandExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class LocalCommandExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public LocalCommandExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.LocalCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = step.Args["Command"];
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = run.ExitCode == 0,
            ExitCode = run.ExitCode,
            Output = run.StdOut,
            Error = run.StdErr,
            Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 5: Crear `RawCommandExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class RawCommandExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public RawCommandExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.RawCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var command = step.Args["Command"];
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = run.ExitCode == 0,
            ExitCode = run.ExitCode,
            Output = run.StdOut,
            Error = run.StdErr,
            Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 6: Correr y verificar que pasan**

Run: `dotnet test --filter "LocalCommandExecutorTests|RawCommandExecutorTests"`
Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Application/Executors/LocalCommandExecutor.cs vali-deploy/Application/Executors/RawCommandExecutor.cs vali-deploy.Tests/Application/Executors/LocalCommandExecutorTests.cs vali-deploy.Tests/Application/Executors/RawCommandExecutorTests.cs
git commit -m "feat(application): agregar LocalCommandExecutor y RawCommandExecutor"
```

---

## Task 12: Executor — `GitCheckoutExecutor` (con `SyncBeforeBuild`)

**Files:**
- Create: `vali-deploy/Application/Executors/GitCheckoutExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/GitCheckoutExecutorTests.cs`

Implementa la decisión del spec: `Args["Branch"]` (default `context.Environment.DefaultBranch`), `Args["SyncBeforeBuild"]` (`"true"`/`"false"`, default `"true"`) controla si corre `git pull` después de `git checkout`.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class GitCheckoutExecutorTests
{
    private static StepExecutionContext Context(string? defaultBranch = "main") => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA", DefaultBranch = defaultBranch }
    };

    [Fact]
    public void Handles_GitCheckout()
    {
        var executor = new GitCheckoutExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.GitCheckout, executor.Handles);
    }

    [Fact]
    public async Task Checks_out_branch_from_Args_and_pulls_by_default()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout develop", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "Already up to date.", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout", Args = { ["Branch"] = "develop" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", "/tmp/proj", null), Times.Once);
    }

    [Fact]
    public async Task Falls_back_to_environment_DefaultBranch_when_Args_Branch_missing()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner.Setup(p => p.RunAsync("git pull", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout" };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git checkout main", "/tmp/proj", null), Times.Once);
    }

    [Fact]
    public async Task Does_not_pull_when_SyncBeforeBuild_is_false()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner.Setup(p => p.RunAsync("git checkout main", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new GitCheckoutExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.GitCheckout, Name = "checkout",
            Args = { ["SyncBeforeBuild"] = "false" }
        };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: "main"));

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("git pull", It.IsAny<string>(), null), Times.Never);
    }

    [Fact]
    public async Task Fails_fast_with_clear_message_when_no_branch_available()
    {
        var executor = new GitCheckoutExecutor(new Mock<IProcessRunner>().Object);
        var step = new DeployStep { Type = StepType.GitCheckout, Name = "checkout" };

        var result = await executor.ExecuteAsync(step, Context(defaultBranch: null));

        Assert.False(result.Success);
        Assert.Contains("rama", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter GitCheckoutExecutorTests`
Expected: FAIL — `GitCheckoutExecutor` no existe (CS0246).

- [ ] **Step 3: Crear `GitCheckoutExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class GitCheckoutExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public GitCheckoutExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.GitCheckout;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var branch = step.Args.GetValueOrDefault("Branch") ?? context.Environment.DefaultBranch;

        if (string.IsNullOrWhiteSpace(branch))
        {
            return new StepResult
            {
                Step = step,
                Success = false,
                ExitCode = -1,
                Error = "No se definió rama para GitCheckout: falta Args[\"Branch\"] y el DeployEnvironment no tiene DefaultBranch.",
                Duration = stopwatch.Elapsed
            };
        }

        var checkoutResult = await _processRunner.RunAsync($"git checkout {branch}", context.ProjectPath);

        if (checkoutResult.ExitCode != 0)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = checkoutResult.ExitCode,
                Output = checkoutResult.StdOut, Error = checkoutResult.StdErr, Duration = stopwatch.Elapsed
            };
        }

        var syncBeforeBuild = step.Args.GetValueOrDefault("SyncBeforeBuild", "true") == "true";

        if (!syncBeforeBuild)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = true, ExitCode = 0,
                Output = checkoutResult.StdOut, Duration = stopwatch.Elapsed
            };
        }

        var pullResult = await _processRunner.RunAsync("git pull", context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step,
            Success = pullResult.ExitCode == 0,
            ExitCode = pullResult.ExitCode,
            Output = checkoutResult.StdOut + pullResult.StdOut,
            Error = pullResult.StdErr,
            Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter GitCheckoutExecutorTests`
Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/GitCheckoutExecutor.cs vali-deploy.Tests/Application/Executors/GitCheckoutExecutorTests.cs
git commit -m "feat(application): agregar GitCheckoutExecutor con flag SyncBeforeBuild"
```

---

## Task 13: Executor — `DockerBuildExecutor` y `DockerPushExecutor`

**Files:**
- Create: `vali-deploy/Application/Executors/DockerBuildExecutor.cs`
- Create: `vali-deploy/Application/Executors/DockerPushExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerBuildExecutorTests.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerPushExecutorTests.cs`

`DockerBuildExecutor` reemplaza la lógica hoy inline en `MenuManager.ExecuteCommandSubProject` L737-755 (comando armado en L746-747). Args esperados: `Dockerfile`, `ImageTag`, `BuildArgs` (opcional, string ya unido con espacios — igual que `subProject.DockerBuildArgs` hoy). `DockerPushExecutor` reemplaza L776-802 (sin la parte de pedir/guardar `DockerHubUser` — eso se resuelve en la Tarea 31 al migrar el menú); Args: `ImageTag`, `RegistryTag`.

- [ ] **Step 1: Escribir el test de `DockerBuildExecutor`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerBuildExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerBuild()
    {
        var executor = new DockerBuildExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerBuild, executor.Handles);
    }

    [Fact]
    public async Task Builds_image_with_dockerfile_and_tag_from_Args()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker build -f \"/tmp/proj/Dockerfile\" -t proj-sub:latest \"/tmp/proj\"",
                "/tmp/proj",
                It.Is<IDictionary<string, string>>(d => d["DOCKER_BUILDKIT"] == "1")))
            .ReturnsAsync(new ProcessRunResult(0, "Successfully built", ""));

        var executor = new DockerBuildExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerBuild, Name = "build image",
            Args = { ["Dockerfile"] = "/tmp/proj/Dockerfile", ["ImageTag"] = "proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Appends_BuildArgs_when_present()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker build -f \"/tmp/proj/Dockerfile\" -t proj-sub:latest --build-arg KEY=VALUE \"/tmp/proj\"",
                "/tmp/proj",
                It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerBuildExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerBuild, Name = "build image",
            Args =
            {
                ["Dockerfile"] = "/tmp/proj/Dockerfile",
                ["ImageTag"] = "proj-sub:latest",
                ["BuildArgs"] = "--build-arg KEY=VALUE"
            }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Escribir el test de `DockerPushExecutor`**

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
        var executor = new DockerPushExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerPush, executor.Handles);
    }

    [Fact]
    public async Task Tags_then_pushes_image_to_registry()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));
        processRunner
            .Setup(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new ProcessRunResult(0, "pushed", ""));

        var executor = new DockerPushExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        processRunner.Verify(p => p.RunAsync("docker tag proj-sub:latest myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>()), Times.Once);
        processRunner.Verify(p => p.RunAsync("docker push myuser/proj-sub:latest", "/tmp/proj", It.IsAny<IDictionary<string, string>>()), Times.Once);
    }

    [Fact]
    public async Task Stops_at_tag_failure_without_attempting_push()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker tag")), "/tmp/proj", It.IsAny<IDictionary<string, string>>()))
            .ReturnsAsync(new ProcessRunResult(1, "", "no such image"));

        var executor = new DockerPushExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerPush, Name = "push",
            Args = { ["ImageTag"] = "proj-sub:latest", ["RegistryTag"] = "myuser/proj-sub:latest" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        processRunner.Verify(p => p.RunAsync(It.Is<string>(c => c.StartsWith("docker push")), It.IsAny<string>(), It.IsAny<IDictionary<string, string>>()), Times.Never);
    }
}
```

- [ ] **Step 3: Correr y verificar que ambos fallan**

Run: `dotnet test --filter "DockerBuildExecutorTests|DockerPushExecutorTests"`
Expected: FAIL — no existen los tipos (CS0246).

- [ ] **Step 4: Crear `DockerBuildExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerBuildExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerBuildExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerBuild;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var dockerfile = step.Args["Dockerfile"];
        var imageTag = step.Args["ImageTag"];
        var buildArgs = step.Args.GetValueOrDefault("BuildArgs");
        var buildArgsSuffix = string.IsNullOrWhiteSpace(buildArgs) ? "" : $" {buildArgs}";

        var command = $"docker build -f \"{dockerfile}\" -t {imageTag}{buildArgsSuffix} \"{context.ProjectPath}\"";
        var run = await _processRunner.RunAsync(command, context.ProjectPath, new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" });
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 5: Crear `DockerPushExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerPushExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerPushExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerPush;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var registryTag = step.Args["RegistryTag"];
        var extraEnv = new Dictionary<string, string> { ["DOCKER_BUILDKIT"] = "1" };

        var tagRun = await _processRunner.RunAsync($"docker tag {imageTag} {registryTag}", context.ProjectPath, extraEnv);

        if (tagRun.ExitCode != 0)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = tagRun.ExitCode,
                Output = tagRun.StdOut, Error = tagRun.StdErr, Duration = stopwatch.Elapsed
            };
        }

        var pushRun = await _processRunner.RunAsync($"docker push {registryTag}", context.ProjectPath, extraEnv);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = pushRun.ExitCode == 0, ExitCode = pushRun.ExitCode,
            Output = tagRun.StdOut + pushRun.StdOut, Error = pushRun.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 6: Correr y verificar que pasan**

Run: `dotnet test --filter "DockerBuildExecutorTests|DockerPushExecutorTests"`
Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Application/Executors/DockerBuildExecutor.cs vali-deploy/Application/Executors/DockerPushExecutor.cs vali-deploy.Tests/Application/Executors/DockerBuildExecutorTests.cs vali-deploy.Tests/Application/Executors/DockerPushExecutorTests.cs
git commit -m "feat(application): agregar DockerBuildExecutor y DockerPushExecutor"
```

---

## Task 14: Executor — `DockerSaveExecutor` (variante sin registry)

**Files:**
- Create: `vali-deploy/Application/Executors/DockerSaveExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerSaveExecutorTests.cs`

Corre `docker save -o <tar> <image:tag>` localmente — Args: `ImageTag`, `OutputTarPath`. `DockerLoadExecutor` (que corre en el remoto vía SSH) se implementa en la Tarea 17 junto con `SshClientFactory`, porque depende de esa infraestructura.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerSaveExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerSave()
    {
        var executor = new DockerSaveExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerSave, executor.Handles);
    }

    [Fact]
    public async Task Saves_image_to_tar_file()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync("docker save -o \"/tmp/proj/image.tar\" proj-sub:latest", "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerSaveExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerSave, Name = "save",
            Args = { ["ImageTag"] = "proj-sub:latest", ["OutputTarPath"] = "/tmp/proj/image.tar" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter DockerSaveExecutorTests`
Expected: FAIL — `DockerSaveExecutor` no existe (CS0246).

- [ ] **Step 3: Crear `DockerSaveExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerSaveExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerSaveExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerSave;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageTag = step.Args["ImageTag"];
        var outputTarPath = step.Args["OutputTarPath"];

        var run = await _processRunner.RunAsync($"docker save -o \"{outputTarPath}\" {imageTag}", context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter DockerSaveExecutorTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/DockerSaveExecutor.cs vali-deploy.Tests/Application/Executors/DockerSaveExecutorTests.cs
git commit -m "feat(application): agregar DockerSaveExecutor"
```

---

## Task 15: Executor — `DockerImagePruneExecutor`

**Files:**
- Create: `vali-deploy/Application/Executors/DockerImagePruneExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerImagePruneExecutorTests.cs`

Args: `ImageNameFilter` (acota la limpieza a las imágenes del propio proyecto, según el spec — "acotado por defecto a las imágenes del propio proyecto").

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerImagePruneExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_DockerImagePrune()
    {
        var executor = new DockerImagePruneExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.DockerImagePrune, executor.Handles);
    }

    [Fact]
    public async Task Prunes_dangling_images_filtered_by_project_name()
    {
        var processRunner = new Mock<IProcessRunner>();
        processRunner
            .Setup(p => p.RunAsync(
                "docker image prune -f --filter \"label=project=proj-sub\"",
                "/tmp/proj", null))
            .ReturnsAsync(new ProcessRunResult(0, "Total reclaimed space: 0B", ""));

        var executor = new DockerImagePruneExecutor(processRunner.Object);
        var step = new DeployStep
        {
            Type = StepType.DockerImagePrune, Name = "prune",
            Args = { ["ImageNameFilter"] = "proj-sub" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter DockerImagePruneExecutorTests`
Expected: FAIL — `DockerImagePruneExecutor` no existe (CS0246).

- [ ] **Step 3: Crear `DockerImagePruneExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerImagePruneExecutor : IStepExecutor
{
    private readonly IProcessRunner _processRunner;

    public DockerImagePruneExecutor(IProcessRunner processRunner) => _processRunner = processRunner;

    public StepType Handles => StepType.DockerImagePrune;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var imageNameFilter = step.Args["ImageNameFilter"];

        var command = $"docker image prune -f --filter \"label=project={imageNameFilter}\"";
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter DockerImagePruneExecutorTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/DockerImagePruneExecutor.cs vali-deploy.Tests/Application/Executors/DockerImagePruneExecutorTests.cs
git commit -m "feat(application): agregar DockerImagePruneExecutor"
```

---

## Task 16: Executor — `ZipPublishExecutor` (migra lógica existente de `CommandExecutor.RunCommandsAsync`)

**Files:**
- Create: `vali-deploy/Application/Executors/ZipPublishExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs`

Reemplaza el flujo de `CommandExecutor.RunCommandsAsync` (L10-125): clean → `dotnet build` → `dotnet publish`, corriendo cada comando con `ProcessRunner` (que **sí** verifica exit code entre pasos, a diferencia del original) y cortando en el primer fallo. La limpieza de `OmitFiles` y el zip condicional (L60-125 del original) se extraen a un helper privado dentro del mismo executor — no se tocan archivos de UI (`OpenFileExplorer`) porque eso es responsabilidad de Presentation, no de Application; ese detalle se resuelve en la Tarea 31.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class ZipPublishExecutorTests
{
    private static StepExecutionContext Context(string path) => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = path,
        Environment = new DeployEnvironment { Name = "QA" }
    };

    [Fact]
    public void Handles_ZipPublishOutput()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        Assert.Equal(StepType.ZipPublishOutput, executor.Handles);
    }

    [Fact]
    public async Task Runs_clean_build_and_publish_in_order_and_stops_on_first_failure()
    {
        var tempDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var processRunner = new Mock<IProcessRunner>();
            var callOrder = new List<string>();

            processRunner
                .Setup(p => p.RunAsync(It.IsAny<string>(), tempDir, null))
                .Callback<string, string, IDictionary<string, string>?>((cmd, _, _) => callOrder.Add(cmd))
                .ReturnsAsync((string cmd, string _, IDictionary<string, string>? _) =>
                    cmd.Contains("build") ? new ProcessRunResult(1, "", "build failed") : new ProcessRunResult(0, "", ""));

            var executor = new ZipPublishExecutor(processRunner.Object);
            var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

            var result = await executor.ExecuteAsync(step, Context(tempDir));

            Assert.False(result.Success);
            Assert.DoesNotContain(callOrder, c => c.Contains("publish"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Fails_fast_when_project_path_does_not_exist()
    {
        var executor = new ZipPublishExecutor(new Mock<IProcessRunner>().Object);
        var step = new DeployStep { Type = StepType.ZipPublishOutput, Name = "publish" };

        var result = await executor.ExecuteAsync(step, Context("/no/existe/este/path"));

        Assert.False(result.Success);
        Assert.Contains("no existe", result.Error, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter ZipPublishExecutorTests`
Expected: FAIL — `ZipPublishExecutor` no existe (CS0246).

- [ ] **Step 3: Crear `ZipPublishExecutor.cs`**

```csharp
using System.Diagnostics;
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
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1,
                Error = $"El path del proyecto no existe: {context.ProjectPath}", Duration = stopwatch.Elapsed
            };
        }

        var publishArgs = step.Args.GetValueOrDefault("PublishArgs", "");
        var cleanCommand = OperatingSystem.IsWindows() ? "rmdir /s /q bin && rmdir /s /q obj" : "rm -rf bin; rm -rf obj";
        var commands = new[]
        {
            cleanCommand,
            "dotnet clean",
            "dotnet build",
            $"dotnet publish -c Release {publishArgs}".TrimEnd()
        };

        var combinedOutput = new System.Text.StringBuilder();

        foreach (var command in commands)
        {
            var run = await _processRunner.RunAsync(command, context.ProjectPath);
            combinedOutput.AppendLine(run.StdOut);

            if (run.ExitCode != 0)
            {
                stopwatch.Stop();
                return new StepResult
                {
                    Step = step, Success = false, ExitCode = run.ExitCode,
                    Output = combinedOutput.ToString(), Error = run.StdErr, Duration = stopwatch.Elapsed
                };
            }
        }

        stopwatch.Stop();
        return new StepResult
        {
            Step = step, Success = true, ExitCode = 0,
            Output = combinedOutput.ToString(), Duration = stopwatch.Elapsed
        };
    }
}
```

Nota de alcance: la limpieza de `OmitFiles` y el zipeo del output (`CommandExecutor.cs` L60-125 del original) quedan **fuera** de esta tarea — se migran en la Tarea 31 como parte del `PipelineTemplateFactory`/`PipelineEditorMenu`, porque dependen de `SubProject.OmitFiles`/`ZipPublishOutput` que hoy vive en el modelo y requiere decidir si se modela como Args de este mismo step o como un `StepType` separado (`ZipOmitAndCompress`). Esa decisión de diseño no estaba resuelta en el spec y se marca explícitamente como pendiente para no inventarla en este plan.

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter ZipPublishExecutorTests`
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/ZipPublishExecutor.cs vali-deploy.Tests/Application/Executors/ZipPublishExecutorTests.cs
git commit -m "feat(application): agregar ZipPublishExecutor (build+publish con exit code verificado)"
```

---

## Task 17: Infrastructure — `SshClientFactory` (SSH.NET) + Executor — `SshCommandExecutor` y `DockerLoadExecutor`

**Files:**
- Modify: `vali-deploy/vali-deploy.csproj` (agregar `Renci.SshNet`)
- Create: `vali-deploy/Infrastructure/ISshClientFactory.cs`
- Create: `vali-deploy/Infrastructure/SshClientFactory.cs`
- Create: `vali-deploy/Application/Executors/SshCommandExecutor.cs`
- Create: `vali-deploy/Application/Executors/DockerLoadExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/SshCommandExecutorTests.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerLoadExecutorTests.cs`

Envuelve `Renci.SshNet` detrás de una interfaz para poder mockear en tests (el spec explícitamente descarta integration tests contra un servidor SSH real). `RunCommandAsync` ajusta el comando a `bash -c` o `powershell -Command` según `RemoteServer.Os` (spec, sección SSH/SFTP).

- [ ] **Step 1: Agregar el paquete NuGet**

Run: `dotnet add vali-deploy/vali-deploy.csproj package Renci.SshNet --version 2023.0.0`
Expected: `PackageReference for package 'Renci.SshNet' version '2023.0.0' added to file '...\vali-deploy.csproj'.`

- [ ] **Step 2: Escribir el test de `SshCommandExecutor`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class SshCommandExecutorTests
{
    private static StepExecutionContext ContextWithServer(RemoteOs os) => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = os, PrivateKeyPath = "/key" }
        }
    };

    [Fact]
    public void Handles_SshCommand()
    {
        var executor = new SshCommandExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.SshCommand, executor.Handles);
    }

    [Fact]
    public async Task Runs_command_on_remote_server_from_environment()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "systemctl restart myapp"))
            .ReturnsAsync(new ProcessRunResult(0, "restarted", ""));

        var executor = new SshCommandExecutor(sshFactory.Object);
        var step = new DeployStep { Type = StepType.SshCommand, Name = "restart", Args = { ["Command"] = "systemctl restart myapp" } };

        var result = await executor.ExecuteAsync(step, ContextWithServer(RemoteOs.Linux));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Fails_fast_when_environment_has_no_remote_server()
    {
        var executor = new SshCommandExecutor(new Mock<ISshClientFactory>().Object);
        var context = new StepExecutionContext
        {
            ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
            Environment = new DeployEnvironment { Name = "DEV" }
        };
        var step = new DeployStep { Type = StepType.SshCommand, Name = "restart", Args = { ["Command"] = "echo hi" } };

        var result = await executor.ExecuteAsync(step, context);

        Assert.False(result.Success);
        Assert.Contains("RemoteServer", result.Error);
    }
}
```

- [ ] **Step 3: Escribir el test de `DockerLoadExecutor`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerLoadExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = RemoteOs.Linux, PrivateKeyPath = "/key" }
        }
    };

    [Fact]
    public void Handles_DockerLoad()
    {
        var executor = new DockerLoadExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.DockerLoad, executor.Handles);
    }

    [Fact]
    public async Task Loads_tar_on_remote_via_ssh()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker load -i \"/opt/app/image.tar\""))
            .ReturnsAsync(new ProcessRunResult(0, "Loaded image", ""));

        var executor = new DockerLoadExecutor(sshFactory.Object);
        var step = new DeployStep { Type = StepType.DockerLoad, Name = "load", Args = { ["RemoteTarPath"] = "/opt/app/image.tar" } };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
    }
}
```

- [ ] **Step 4: Correr y verificar que todo falla**

Run: `dotnet test --filter "SshCommandExecutorTests|DockerLoadExecutorTests"`
Expected: FAIL — `ISshClientFactory` y los executores no existen (CS0246).

- [ ] **Step 5: Crear `ISshClientFactory.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface ISshClientFactory
{
    Task<ProcessRunResult> RunCommandAsync(RemoteServer server, string command);
    Task UploadFileAsync(RemoteServer server, string localPath, string remotePath);
}
```

- [ ] **Step 6: Crear `SshClientFactory.cs`**

```csharp
using Renci.SshNet;
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class SshClientFactory : ISshClientFactory
{
    private readonly ISecretResolver _secretResolver;

    public SshClientFactory(ISecretResolver secretResolver) => _secretResolver = secretResolver;

    public async Task<ProcessRunResult> RunCommandAsync(RemoteServer server, string command)
    {
        using var client = CreateSshClient(server);
        client.Connect();

        var shellCommand = server.Os == RemoteOs.Windows
            ? $"powershell -Command \"{command}\""
            : $"bash -c \"{command}\"";

        using var sshCommand = client.CreateCommand(shellCommand);
        var result = await Task.Factory.FromAsync(sshCommand.BeginExecute(), sshCommand.EndExecute);

        client.Disconnect();

        return new ProcessRunResult(sshCommand.ExitStatus, result, sshCommand.Error);
    }

    public async Task UploadFileAsync(RemoteServer server, string localPath, string remotePath)
    {
        using var client = new SftpClient(BuildConnectionInfo(server));
        client.Connect();

        await using var fileStream = File.OpenRead(localPath);
        await Task.Run(() => client.UploadFile(fileStream, remotePath));

        client.Disconnect();
    }

    private SshClient CreateSshClient(RemoteServer server) => new(BuildConnectionInfo(server));

    private ConnectionInfo BuildConnectionInfo(RemoteServer server)
    {
        var passphrase = server.PassphraseEnvVar != null ? _secretResolver.Resolve(server.PassphraseEnvVar) : null;
        var keyFile = passphrase != null
            ? new PrivateKeyFile(server.PrivateKeyPath, passphrase)
            : new PrivateKeyFile(server.PrivateKeyPath);

        return new ConnectionInfo(server.Host, server.Port, server.User, new PrivateKeyAuthenticationMethod(server.User, keyFile));
    }
}
```

- [ ] **Step 7: Crear `SshCommandExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class SshCommandExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public SshCommandExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.SshCommand;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1,
                Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
                Duration = stopwatch.Elapsed
            };
        }

        var command = step.Args["Command"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 8: Crear `DockerLoadExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerLoadExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerLoadExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerLoad;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1,
                Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
                Duration = stopwatch.Elapsed
            };
        }

        var remoteTarPath = step.Args["RemoteTarPath"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, $"docker load -i \"{remoteTarPath}\"");
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 9: Correr y verificar que todo pasa**

Run: `dotnet test --filter "SshCommandExecutorTests|DockerLoadExecutorTests"`
Expected: `Passed!  - Failed: 0, Passed: 5, Skipped: 0`

- [ ] **Step 10: Commit**

```bash
git add vali-deploy/vali-deploy.csproj vali-deploy/Infrastructure/ISshClientFactory.cs vali-deploy/Infrastructure/SshClientFactory.cs vali-deploy/Application/Executors/SshCommandExecutor.cs vali-deploy/Application/Executors/DockerLoadExecutor.cs vali-deploy.Tests/Application/Executors/SshCommandExecutorTests.cs vali-deploy.Tests/Application/Executors/DockerLoadExecutorTests.cs
git commit -m "feat(infra): agregar SshClientFactory (SSH.NET), SshCommandExecutor y DockerLoadExecutor"
```

---

## Task 18: Executor — `CopyToRemoteExecutor` (SFTP)

**Files:**
- Create: `vali-deploy/Application/Executors/CopyToRemoteExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/CopyToRemoteExecutorTests.cs`

Args: `LocalPath`, `RemotePath`. Usa `ISshClientFactory.UploadFileAsync` (Task 17).

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class CopyToRemoteExecutorTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = RemoteOs.Linux, PrivateKeyPath = "/key" }
        }
    };

    [Fact]
    public void Handles_CopyToRemote()
    {
        var executor = new CopyToRemoteExecutor(new Mock<ISshClientFactory>().Object);
        Assert.Equal(StepType.CopyToRemote, executor.Handles);
    }

    [Fact]
    public async Task Uploads_local_file_to_remote_path()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/compose.yml", "/opt/app/compose.yml"))
            .Returns(Task.CompletedTask);

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.True(result.Success);
        sshFactory.Verify(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), "/tmp/proj/compose.yml", "/opt/app/compose.yml"), Times.Once);
    }

    [Fact]
    public async Task Reports_failure_when_upload_throws()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.UploadFileAsync(It.IsAny<RemoteServer>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("connection reset"));

        var executor = new CopyToRemoteExecutor(sshFactory.Object);
        var step = new DeployStep
        {
            Type = StepType.CopyToRemote, Name = "copy compose",
            Args = { ["LocalPath"] = "/tmp/proj/compose.yml", ["RemotePath"] = "/opt/app/compose.yml" }
        };

        var result = await executor.ExecuteAsync(step, Context());

        Assert.False(result.Success);
        Assert.Contains("connection reset", result.Error);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter CopyToRemoteExecutorTests`
Expected: FAIL — `CopyToRemoteExecutor` no existe (CS0246).

- [ ] **Step 3: Crear `CopyToRemoteExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class CopyToRemoteExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public CopyToRemoteExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.CopyToRemote;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();

        if (context.Environment.Server == null)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1,
                Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
                Duration = stopwatch.Elapsed
            };
        }

        var localPath = step.Args["LocalPath"];
        var remotePath = step.Args["RemotePath"];

        try
        {
            await _sshClientFactory.UploadFileAsync(context.Environment.Server, localPath, remotePath);
            stopwatch.Stop();
            return new StepResult { Step = step, Success = true, ExitCode = 0, Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1, Error = ex.Message, Duration = stopwatch.Elapsed
            };
        }
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter CopyToRemoteExecutorTests`
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/CopyToRemoteExecutor.cs vali-deploy.Tests/Application/Executors/CopyToRemoteExecutorTests.cs
git commit -m "feat(application): agregar CopyToRemoteExecutor (SFTP)"
```

---

## Task 19: Executors — `DockerComposePullExecutor`, `DockerComposeUpExecutor`, `DockerComposeDownExecutor`

**Files:**
- Create: `vali-deploy/Application/Executors/DockerComposePullExecutor.cs`
- Create: `vali-deploy/Application/Executors/DockerComposeUpExecutor.cs`
- Create: `vali-deploy/Application/Executors/DockerComposeDownExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/DockerComposeExecutorsTests.cs`

Los tres son idénticos salvo el subcomando (`pull`/`up -d`/`down`) y corren en el remoto vía `ISshClientFactory` (a diferencia de `DockerBuild`/`DockerSave` que corren local) — Args: `ComposeFilePath` (path remoto del `compose.yml` ya copiado por `CopyToRemote`).

- [ ] **Step 1: Escribir los tests (los 3 executores en un solo archivo por ser triviales y compartir forma)**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Application.Executors;

public class DockerComposeExecutorsTests
{
    private static StepExecutionContext Context() => new()
    {
        ProjectName = "proj", SubProjectName = "sub", ProjectPath = "/tmp/proj",
        Environment = new DeployEnvironment
        {
            Name = "PROD",
            Server = new RemoteServer { Host = "prod.example.com", User = "deploy", Os = RemoteOs.Linux, PrivateKeyPath = "/key" }
        }
    };

    private static DeployStep ComposeStep(StepType type) => new()
    {
        Type = type, Name = type.ToString(), Args = { ["ComposeFilePath"] = "/opt/app/compose.yml" }
    };

    [Fact]
    public async Task Pull_runs_docker_compose_pull_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" pull"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposePullExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposePull, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposePull), Context());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Up_runs_docker_compose_up_detached_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" up -d"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposeUpExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposeUp, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeUp), Context());
        Assert.True(result.Success);
    }

    [Fact]
    public async Task Down_runs_docker_compose_down_on_remote()
    {
        var sshFactory = new Mock<ISshClientFactory>();
        sshFactory
            .Setup(f => f.RunCommandAsync(It.IsAny<RemoteServer>(), "docker compose -f \"/opt/app/compose.yml\" down"))
            .ReturnsAsync(new ProcessRunResult(0, "", ""));

        var executor = new DockerComposeDownExecutor(sshFactory.Object);
        Assert.Equal(StepType.DockerComposeDown, executor.Handles);

        var result = await executor.ExecuteAsync(ComposeStep(StepType.DockerComposeDown), Context());
        Assert.True(result.Success);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter DockerComposeExecutorsTests`
Expected: FAIL — los tres tipos no existen (CS0246).

- [ ] **Step 3: Crear `DockerComposePullExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerComposePullExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerComposePullExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerComposePull;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var composeFilePath = step.Args["ComposeFilePath"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server!, $"docker compose -f \"{composeFilePath}\" pull");
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 4: Crear `DockerComposeUpExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerComposeUpExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerComposeUpExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerComposeUp;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var composeFilePath = step.Args["ComposeFilePath"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server!, $"docker compose -f \"{composeFilePath}\" up -d");
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 5: Crear `DockerComposeDownExecutor.cs`**

```csharp
using System.Diagnostics;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

public class DockerComposeDownExecutor : IStepExecutor
{
    private readonly ISshClientFactory _sshClientFactory;

    public DockerComposeDownExecutor(ISshClientFactory sshClientFactory) => _sshClientFactory = sshClientFactory;

    public StepType Handles => StepType.DockerComposeDown;

    public async Task<StepResult> ExecuteAsync(DeployStep step, StepExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        var composeFilePath = step.Args["ComposeFilePath"];
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server!, $"docker compose -f \"{composeFilePath}\" down");
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Duration = stopwatch.Elapsed
        };
    }
}
```

- [ ] **Step 6: Correr y verificar que pasan**

Run: `dotnet test --filter DockerComposeExecutorsTests`
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Application/Executors/DockerComposePullExecutor.cs vali-deploy/Application/Executors/DockerComposeUpExecutor.cs vali-deploy/Application/Executors/DockerComposeDownExecutor.cs vali-deploy.Tests/Application/Executors/DockerComposeExecutorsTests.cs
git commit -m "feat(application): agregar executores DockerComposePull/Up/Down"
```

---

## Task 20: Application — `PipelineTemplateFactory`

**Files:**
- Create: `vali-deploy/Application/PipelineTemplateFactory.cs`
- Test: `vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs`

Genera las dos plantillas del spec (Docker Compose y Publish/Zip), ambas arrancando con `GitCheckout` cuando el proyecto es un repo git.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class PipelineTemplateFactoryTests
{
    [Fact]
    public void DockerCompose_template_follows_spec_order()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "shop", subProjectName: "api");

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

        var steps = factory.CreatePublishZipTemplate(projectName: "shop", subProjectName: "api");

        Assert.Equal(new[]
        {
            StepType.GitCheckout, StepType.LocalCommand, StepType.LocalCommand, StepType.ZipPublishOutput,
            StepType.CopyToRemote, StepType.SshCommand, StepType.SshCommand
        }, steps.Select(s => s.Type));
    }

    [Fact]
    public void DockerCompose_template_sets_ImageTag_using_project_and_subproject_name()
    {
        var factory = new PipelineTemplateFactory();

        var steps = factory.CreateDockerComposeTemplate(projectName: "Shop", subProjectName: "Api");
        var buildStep = steps.Single(s => s.Type == StepType.DockerBuild);

        Assert.Equal("shop-api:latest", buildStep.Args["ImageTag"]);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter PipelineTemplateFactoryTests`
Expected: FAIL — `PipelineTemplateFactory` no existe (CS0246).

- [ ] **Step 3: Crear `PipelineTemplateFactory.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public class PipelineTemplateFactory
{
    public List<DeployStep> CreateDockerComposeTemplate(string projectName, string subProjectName)
    {
        var imageTag = $"{projectName.ToLower()}-{subProjectName.ToLower()}:latest";

        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.DockerBuild, Name = "Build imagen", Args = { ["ImageTag"] = imageTag, ["Dockerfile"] = "Dockerfile" } },
            new() { Type = StepType.DockerPush, Name = "Push a registry", Args = { ["ImageTag"] = imageTag } },
            new() { Type = StepType.CopyToRemote, Name = "Copiar compose.yml", Args = { ["LocalPath"] = "compose.yml" } },
            new() { Type = StepType.DockerComposePull, Name = "Compose pull" },
            new() { Type = StepType.DockerComposeUp, Name = "Compose up" },
            new() { Type = StepType.DockerImagePrune, Name = "Limpiar imágenes viejas", Args = { ["ImageNameFilter"] = $"{projectName.ToLower()}-{subProjectName.ToLower()}" } }
        };
    }

    public List<DeployStep> CreatePublishZipTemplate(string projectName, string subProjectName)
    {
        return new List<DeployStep>
        {
            new() { Type = StepType.GitCheckout, Name = "Checkout" },
            new() { Type = StepType.LocalCommand, Name = "Limpiar bin/obj", Args = { ["Command"] = OperatingSystem.IsWindows() ? "rmdir /s /q bin && rmdir /s /q obj" : "rm -rf bin obj" } },
            new() { Type = StepType.LocalCommand, Name = "dotnet publish", Args = { ["Command"] = "dotnet publish -c Release" } },
            new() { Type = StepType.ZipPublishOutput, Name = "Comprimir output" },
            new() { Type = StepType.CopyToRemote, Name = "Copiar zip al remoto" },
            new() { Type = StepType.SshCommand, Name = "Extraer zip", Args = { ["Command"] = "" } },
            new() { Type = StepType.SshCommand, Name = "Reiniciar servicio/IIS pool", Args = { ["Command"] = "" } }
        };
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test --filter PipelineTemplateFactoryTests`
Expected: `Passed!  - Failed: 0, Passed: 3, Skipped: 0`

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/PipelineTemplateFactory.cs vali-deploy.Tests/Application/PipelineTemplateFactoryTests.cs
git commit -m "feat(application): agregar PipelineTemplateFactory (plantillas Docker Compose y Publish/Zip)"
```

---

## Task 21: Infrastructure — `PipelineLogger`

**Files:**
- Create: `vali-deploy/Infrastructure/IPipelineLogger.cs`
- Create: `vali-deploy/Infrastructure/PipelineLogger.cs`
- Test: `vali-deploy.Tests/Infrastructure/PipelineLoggerTests.cs`

Escribe cada corrida a `%USERPROFILE%\Documents\vali-deploy\logs\{proyecto}-{subproyecto}-{timestamp}.log` (spec, sección Logging), en paralelo al output de consola.

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class PipelineLoggerTests
{
    [Fact]
    public void WriteStep_appends_step_result_to_the_run_log_file()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("proj", "sub");

            logger.WriteStep(new StepResult
            {
                Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0, Output = "ok"
            });

            var logFile = Directory.GetFiles(tempLogsDir).Single();
            var content = File.ReadAllText(logFile);

            Assert.Contains("build", content);
            Assert.Contains("ExitCode: 0", content);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void StartRun_creates_file_named_with_project_subproject_and_timestamp()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("shop", "api");

            var logFile = Directory.GetFiles(tempLogsDir).Single();
            Assert.StartsWith("shop-api-", Path.GetFileName(logFile));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter PipelineLoggerTests`
Expected: FAIL — `PipelineLogger` no existe (CS0246).

- [ ] **Step 3: Crear `IPipelineLogger.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IPipelineLogger
{
    void StartRun(string projectName, string subProjectName);
    void WriteStep(StepResult stepResult);
}
```

- [ ] **Step 4: Crear `PipelineLogger.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class PipelineLogger : IPipelineLogger
{
    private readonly string _logsDirectory;
    private string? _currentLogFilePath;

    public PipelineLogger(string? logsDirectory = null)
    {
        _logsDirectory = logsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "logs");
    }

    public void StartRun(string projectName, string subProjectName)
    {
        Directory.CreateDirectory(_logsDirectory);
        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        _currentLogFilePath = Path.Combine(_logsDirectory, $"{projectName}-{subProjectName}-{timestamp}.log");
        File.WriteAllText(_currentLogFilePath, $"=== Pipeline run: {projectName}/{subProjectName} — {DateTime.UtcNow:O} ===\n");
    }

    public void WriteStep(StepResult stepResult)
    {
        if (_currentLogFilePath == null)
        {
            throw new InvalidOperationException("StartRun debe llamarse antes de WriteStep.");
        }

        var line = $"[{DateTime.UtcNow:O}] {stepResult.Step.Name} — Success: {stepResult.Success} — ExitCode: {stepResult.ExitCode} — Duration: {stepResult.Duration}\n{stepResult.Output}\n{stepResult.Error}\n";
        File.AppendAllText(_currentLogFilePath, line);
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test --filter PipelineLoggerTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Infrastructure/IPipelineLogger.cs vali-deploy/Infrastructure/PipelineLogger.cs vali-deploy.Tests/Infrastructure/PipelineLoggerTests.cs
git commit -m "feat(infra): agregar PipelineLogger"
```

---

## Task 22: Infrastructure — `ProjectRepository` (adapta `ProjectManager`, agrega `Environments` a nivel raíz)

**Files:**
- Create: `vali-deploy/Domain/DeployConfig.cs`
- Create: `vali-deploy/Infrastructure/IProjectRepository.cs`
- Create: `vali-deploy/Infrastructure/ProjectRepository.cs`
- Test: `vali-deploy.Tests/Infrastructure/ProjectRepositoryTests.cs`

Reemplaza `ProjectManager.cs` (no lo borra todavía — Task 31 es la que apaga el uso viejo). Persiste `DeployConfig { Projects, Environments }` en vez de solo `Dictionary<string, Project>`, para que `Environments: List<DeployEnvironment>` viva a nivel raíz según el spec ("no anidado dentro de cada proyecto").

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;
using vali_deploy.Infrastructure;
using vali_deploy.Models;

namespace vali_deploy.Tests.Infrastructure;

public class ProjectRepositoryTests
{
    private static string NewTempConfigPath() => Path.Combine(Directory.CreateTempSubdirectory().FullName, "deploy_config.json");

    [Fact]
    public void Load_creates_default_config_when_file_does_not_exist()
    {
        var repository = new ProjectRepository(NewTempConfigPath());

        var config = repository.Load();

        Assert.NotEmpty(config.Projects);
        Assert.Empty(config.Environments);
    }

    [Fact]
    public void Save_then_load_roundtrips_environments_and_projects()
    {
        var configPath = NewTempConfigPath();
        var repository = new ProjectRepository(configPath);
        var config = repository.Load();
        config.Environments.Add(new DeployEnvironment { Name = "QA", DefaultBranch = "develop" });
        config.Projects["demo"] = new Project { Path = "/tmp/demo", SubProjects = new List<SubProject>() };

        repository.Save(config);
        var reloaded = repository.Load();

        Assert.Single(reloaded.Environments);
        Assert.Equal("QA", reloaded.Environments[0].Name);
        Assert.True(reloaded.Projects.ContainsKey("demo"));
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test --filter ProjectRepositoryTests`
Expected: FAIL — `DeployConfig`/`ProjectRepository` no existen (CS0246).

- [ ] **Step 3: Crear `DeployConfig.cs`**

```csharp
using vali_deploy.Models;

namespace vali_deploy.Domain;

public class DeployConfig
{
    public Dictionary<string, Project> Projects { get; set; } = new();
    public List<DeployEnvironment> Environments { get; set; } = new();
}
```

- [ ] **Step 4: Crear `IProjectRepository.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IProjectRepository
{
    DeployConfig Load();
    void Save(DeployConfig config);
}
```

- [ ] **Step 5: Crear `ProjectRepository.cs`**

```csharp
using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Models;

namespace vali_deploy.Infrastructure;

public class ProjectRepository : IProjectRepository
{
    private readonly string _configPath;

    public ProjectRepository(string? configPath = null)
    {
        _configPath = configPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "deploy_config.json");
    }

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
            return JsonSerializer.Deserialize<DeployConfig>(json) ?? new DeployConfig { Projects = GetDefaultProjects() };
        }
        catch (JsonException)
        {
            var defaultConfig = new DeployConfig { Projects = GetDefaultProjects() };
            Save(defaultConfig);
            return defaultConfig;
        }
    }

    public void Save(DeployConfig config)
    {
        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_configPath, json);
    }

    private static Dictionary<string, Project> GetDefaultProjects() => new()
    {
        ["Project 1"] = new Project { Path = @"\Projects\Path", SubProjects = new List<SubProject>() }
    };
}
```

- [ ] **Step 6: Correr y verificar que pasa**

Run: `dotnet test --filter ProjectRepositoryTests`
Expected: `Passed!  - Failed: 0, Passed: 2, Skipped: 0`

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Domain/DeployConfig.cs vali-deploy/Infrastructure/IProjectRepository.cs vali-deploy/Infrastructure/ProjectRepository.cs vali-deploy.Tests/Infrastructure/ProjectRepositoryTests.cs
git commit -m "feat(infra): agregar ProjectRepository con Environments a nivel raíz de DeployConfig"
```

---

## Task 23: Composition root — wiring en `Program.cs`

**Files:**
- Create: `vali-deploy/CompositionRoot.cs`
- Modify: `vali-deploy/Program.cs`

Registra manualmente (sin contenedor DI — no se justifica el paquete nuevo para un CLI de este tamaño) todos los `IStepExecutor` y arma el `PipelineRunner`. Este es el primer punto donde el código nuevo se conecta a `Program.cs`, pero **todavía no reemplaza** `MenuManager.StartAsync()` — solo dejamos disponible la infraestructura para que la Tarea 24 en adelante (Presentation) la consuma.

- [ ] **Step 1: Crear `CompositionRoot.cs`**

```csharp
using vali_deploy.Application;
using vali_deploy.Application.Executors;
using vali_deploy.Infrastructure;

namespace vali_deploy;

public static class CompositionRoot
{
    public static IPipelineRunner CreatePipelineRunner()
    {
        var processRunner = new ProcessRunner();
        var secretResolver = new EnvVarSecretResolver();
        var sshClientFactory = new SshClientFactory(secretResolver);

        IStepExecutor[] executors =
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

        return new PipelineRunner(executors);
    }

    public static IProjectRepository CreateProjectRepository() => new ProjectRepository();

    public static IPipelineLogger CreatePipelineLogger() => new PipelineLogger();
}
```

- [ ] **Step 2: Confirmar que compila (sin tocar `Program.cs` todavía — se conecta en Task 24)**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.` — los 14 `StepType` tienen executor registrado; si faltara alguno el `Dictionary` de `PipelineRunner` lo detectaría recién en runtime (`InvalidOperationException`), no en compilación, así que confirmar visualmente que la lista cubre los 14 valores del enum antes de continuar.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/CompositionRoot.cs
git commit -m "feat(composition-root): registrar todos los IStepExecutor y armar PipelineRunner"
```

---

## Task 24: Presentation — `PipelineExecutionView` (Progress + tabla resumen)

**Files:**
- Create: `vali-deploy/Presentation/PipelineExecutionView.cs`

Sin test (es puramente Spectre.Console — el spec explícitamente excluye `Presentation/` del foco de cobertura). Se verifica manualmente en la Tarea 31, cuando queda conectada al menú real.

- [ ] **Step 1: Crear `PipelineExecutionView.cs`**

```csharp
using Spectre.Console;
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Presentation;

public class PipelineExecutionView
{
    public async Task<PipelineResult> RunAsync(IPipelineRunner pipelineRunner, List<DeployStep> steps, StepExecutionContext context)
    {
        PipelineResult? result = null;

        await AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new SpinnerColumn(), new ElapsedTimeColumn())
            .StartAsync(async ctx =>
            {
                var tasks = steps.ToDictionary(s => s, s => ctx.AddTask(s.Name, autoStart: false));
                var current = tasks[steps[0]];
                current.StartTask();

                var progress = new Progress<StepResult>(stepResult =>
                {
                    var task = tasks[stepResult.Step];
                    task.Value = 100;
                    task.Description = stepResult.Success
                        ? $"[green]✅ {stepResult.Step.Name}[/]"
                        : stepResult.WasSkippedDueToContinueOnFailure
                            ? $"[yellow]⚠ {stepResult.Step.Name}[/]"
                            : $"[red]❌ {stepResult.Step.Name}[/]";

                    var nextIndex = steps.IndexOf(stepResult.Step) + 1;
                    if (nextIndex < steps.Count)
                    {
                        tasks[steps[nextIndex]].StartTask();
                    }
                });

                result = await pipelineRunner.RunAsync(steps, context, progress);
            });

        RenderSummaryTable(result!);
        return result!;
    }

    private static void RenderSummaryTable(PipelineResult result)
    {
        var table = new Table().AddColumns("Paso", "Estado", "Duración", "Exit Code");

        foreach (var stepResult in result.Steps)
        {
            var estado = stepResult.Success ? "[green]OK[/]" : stepResult.WasSkippedDueToContinueOnFailure ? "[yellow]WARNING[/]" : "[red]FALLÓ[/]";
            table.AddRow(stepResult.Step.Name, estado, stepResult.Duration.ToString(@"mm\:ss"), stepResult.ExitCode.ToString());
        }

        AnsiConsole.Write(table);
    }
}
```

- [ ] **Step 2: Confirmar que compila**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/PipelineExecutionView.cs
git commit -m "feat(presentation): agregar PipelineExecutionView (Progress + tabla resumen)"
```

---

## Task 25: Presentation — `EnvironmentMenu` (alta/edición de `DeployEnvironment`)

**Files:**
- Create: `vali-deploy/Presentation/EnvironmentMenu.cs`
- Modify: `vali-deploy/Managers/MenuManager.cs:102-114` (agregar opción "Manage Environments" a `GetMainMenuOption`/`StartAsync`)

Primera integración real con el menú existente: agrega una opción nueva sin tocar ninguna de las 8 existentes.

- [ ] **Step 1: Crear `EnvironmentMenu.cs`**

```csharp
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
            var option = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Manage Environments[/]")
                    .AddChoices(config.Environments.Select(e => e.Name)
                        .Append("[green]Add Environment[/]")
                        .Append("[chartreuse3_1]Back to Main Menu[/]")));

            if (option == "[chartreuse3_1]Back to Main Menu[/]") return;

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
        }

        config.Environments.Add(environment);
        repository.Save(config);
    }
}
```

- [ ] **Step 2: Conectar la opción al menú principal — modificar `GetMainMenuOption` en `MenuManager.cs`**

Ubicar el método `GetMainMenuOption` (L102-114 en el archivo actual) y agregar una choice nueva antes de la de salir, más el `case` correspondiente en el `switch` de `StartAsync` (L20-75). Como el contenido exacto de esas líneas puede haber cambiado levemente por las tareas previas, el patrón a aplicar es:

```csharp
// En GetMainMenuOption(), agregar a la lista de .AddChoices(...):
"Manage Environments",

// En el switch de StartAsync(), agregar un case nuevo:
case "Manage Environments":
    await Presentation.EnvironmentMenu.StartAsync(CompositionRoot.CreateProjectRepository());
    break;
```

- [ ] **Step 3: Confirmar que compila y el menú corre**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`
Expected: el CLI arranca, "Manage Environments" aparece en el menú principal, permite crear un entorno DEV/QA/PROD y volver sin errores. Probar manualmente (no hay test automatizado — es Presentation).

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Presentation/EnvironmentMenu.cs vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): agregar EnvironmentMenu para alta de DeployEnvironment"
```

---

## Task 26: Presentation — `PipelineEditorMenu` (alta/edición/reorden de pasos)

**Files:**
- Create: `vali-deploy/Presentation/PipelineEditorMenu.cs`

Permite asignar un `DeployEnvironment` a un `SubProject` (dispara `PipelineTemplateFactory`), y editar la lista de `DeployStep` resultante: agregar, quitar, reordenar, insertar `RawCommand`.

- [ ] **Step 1: Crear `PipelineEditorMenu.cs`**

```csharp
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

        if (!subProject.PipelinesByEnvironment.ContainsKey(environmentName))
        {
            var template = AnsiConsole.Prompt(
                new SelectionPrompt<string>().Title("Plantilla inicial:").AddChoices("Docker Compose", "Publish/Zip"));

            var factory = new PipelineTemplateFactory();
            subProject.PipelinesByEnvironment[environmentName] = template == "Docker Compose"
                ? factory.CreateDockerComposeTemplate(projectName, subProject.Name)
                : factory.CreatePublishZipTemplate(projectName, subProject.Name);

            config.Projects[projectName].SubProjects.First(s => s.Name == subProject.Name).PipelinesByEnvironment = subProject.PipelinesByEnvironment;
            repository.Save(config);
        }

        await EditStepsAsync(repository, config, projectName, subProject, environmentName);
    }

    private static async Task EditStepsAsync(IProjectRepository repository, Domain.DeployConfig config, string projectName, SubProject subProject, string environmentName)
    {
        while (true)
        {
            var steps = subProject.PipelinesByEnvironment[environmentName];
            AnsiConsole.Clear();
            AnsiConsole.Write(new Table().AddColumns("#", "Step").AddRows(
                steps.Select((s, i) => new[] { (i + 1).ToString(), s.Name })
                    .Select(r => r.Select(c => (Spectre.Console.Rendering.IRenderable)new Markup(c)).ToArray())));

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
        }
    }
}
```

- [ ] **Step 2: Confirmar que compila**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "feat(presentation): agregar PipelineEditorMenu"
```

---

## Task 27: Presentation — conectar `PipelineEditorMenu` al flujo de subproyectos existente

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs` (agregar opción dentro del submenú de subproyecto, cerca de `ShowSubProjectsAsync` L406-421)

Agrega la entrada al editor de pipelines desde donde hoy se navega a `ExecuteCommandSubProject` — todavía **sin reemplazar** `ExecuteCommandSubProject` (eso es la Tarea 31). Esta tarea solo deja el editor accesible desde el menú para poder armar pipelines antes de correrlos.

- [ ] **Step 1: Ubicar `ShowSubProjectsAsync` y agregar la opción "Edit Pipeline"**

Patrón a aplicar en el submenú que hoy ofrece ejecutar un subproyecto (alrededor de L406-444 en el archivo actual):

```csharp
// Agregar a las choices del SelectionPrompt existente en ese método:
"Edit Pipeline",

// case nuevo en el switch correspondiente:
case "Edit Pipeline":
    await Presentation.PipelineEditorMenu.StartAsync(CompositionRoot.CreateProjectRepository(), projectName, subProject);
    break;
```

- [ ] **Step 2: Probar manualmente el flujo completo**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`
Expected: desde un subproyecto existente, "Edit Pipeline" pide elegir entorno, ofrece plantilla inicial la primera vez, y permite insertar/quitar pasos en corridas subsiguientes.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): conectar PipelineEditorMenu al menú de subproyectos"
```

---

## Task 28: Migración — reemplazar `ExecuteCommandSubProject` para correr con `PipelineRunner`

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs:707-807` (`ExecuteCommandSubProject`)

**Esta es la única tarea que cambia comportamiento real observable por el usuario.** Reemplaza el cuerpo de `ExecuteCommandSubProject` (que hoy arma comandos Docker/publish a mano) para que, si el `SubProject` tiene un pipeline asignado al entorno elegido, lo corra vía `PipelineExecutionView.RunAsync` en vez del código viejo. Si no tiene ningún pipeline (`PipelinesByEnvironment` vacío), cae al comportamiento actual sin cambios — así el flujo de publish/zip clásico para subproyectos sin `DeployEnvironment` asignado sigue funcionando igual, tal como pide el spec ("El menú de publish/zip clásico sigue funcionando igual para SubProject sin ningún DeployEnvironment asignado todavía").

- [ ] **Step 1: Leer el estado actual exacto del método antes de editar**

Run: `grep -n "private static async Task ExecuteCommandSubProject" -A 5 vali-deploy/Managers/MenuManager.cs`
Expected: confirma la línea de inicio exacta (puede haber corrido unos números de línea por las tareas 25/27 que ya tocaron este archivo).

- [ ] **Step 2: Envolver el inicio del método con el chequeo de pipeline configurado**

Insertar al principio del cuerpo de `ExecuteCommandSubProject` (antes de la lógica existente que arma `imageTag` y el menú dinámico):

```csharp
private static async Task ExecuteCommandSubProject(Project project, SubProject? subProject, string projectName)
{
    if (subProject != null && subProject.PipelinesByEnvironment.Count > 0)
    {
        await ExecuteSubProjectPipelineAsync(project, subProject, projectName);
        return;
    }

    // ... resto del método existente sin cambios (imageTag, menú Docker/publish/run a mano) ...
}

private static async Task ExecuteSubProjectPipelineAsync(Project project, SubProject subProject, string projectName)
{
    var repository = CompositionRoot.CreateProjectRepository();
    var config = repository.Load();

    var environmentName = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("Elegí el entorno a desplegar:")
            .AddChoices(subProject.PipelinesByEnvironment.Keys));

    var environment = config.Environments.First(e => e.Name == environmentName);
    var steps = subProject.PipelinesByEnvironment[environmentName];
    var subProjectPathFull = Path.Combine(project.Path, subProject.Path);

    var context = new Application.StepExecutionContext
    {
        ProjectName = projectName,
        SubProjectName = subProject.Name,
        ProjectPath = subProjectPathFull,
        Environment = environment
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

    PauseForUserInput(result.Success ? "Pipeline completado con éxito." : "Pipeline falló, revisá el detalle arriba.");
}
```

- [ ] **Step 3: Confirmar que compila**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`

- [ ] **Step 4: Probar manualmente ambos caminos**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`
Expected:
1. Subproyecto **sin** pipeline asignado → comportamiento idéntico al actual (menú Docker/publish a mano).
2. Subproyecto **con** pipeline asignado (armado en la Tarea 27) → corre `PipelineExecutionView` con barra de progreso y tabla resumen al final.

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): ExecuteCommandSubProject corre PipelineRunner cuando el subproyecto tiene pipeline asignado"
```

---

## Task 29: Migración incremental — mover `Project`/`SubProject` a `Domain/` y adelgazar `MenuManager`

**Files:**
- Modify: `vali-deploy/Models/Project.cs` → mover a `vali-deploy/Domain/Project.cs`
- Modify: `vali-deploy/Models/SubProject.cs` → mover a `vali-deploy/Domain/SubProject.cs`
- Modify: todos los archivos que referencian `vali_deploy.Models.Project`/`SubProject` (actualizar `using`)

Paso 1 del "Plan de migración de MenuManager.cs" del spec — cambio mecánico de namespace, sin tocar comportamiento. Se hace al final (no al principio) porque cambiar el namespace de estos dos tipos antes hubiera obligado a tocar el `using` en cada tarea anterior; hacerlo ahora es un solo cambio localizado.

- [ ] **Step 1: Mover y renombrar namespace de `Project.cs`**

```csharp
namespace vali_deploy.Domain;

public class Project
{
    public string Path { get; set; } = "";
    public List<SubProject> SubProjects { get; set; } = new();
}
```

Run: `git mv vali-deploy/Models/Project.cs vali-deploy/Domain/Project.cs`

- [ ] **Step 2: Mover y renombrar namespace de `SubProject.cs`**

```csharp
namespace vali_deploy.Domain;

public class SubProject
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public List<string> OmitFiles { get; set; } = new();
    public string? DockerfilePath { get; set; }
    public List<string>? DockerRunArgs { get; set; }
    public List<string>? DockerBuildArgs { get; set; }
    public string? DockerHubUser { get; set; }
    public List<string>? PublishArgs { get; set; }
    public bool ZipPublishOutput { get; set; } = true;
    public Dictionary<string, List<DeployStep>> PipelinesByEnvironment { get; set; } = new();
    public string? DockerRegistryTokenEnvVar { get; set; }
}
```

Run: `git mv vali-deploy/Models/SubProject.cs vali-deploy/Domain/SubProject.cs`

- [ ] **Step 3: Actualizar todos los `using vali_deploy.Models;` a `using vali_deploy.Domain;`**

Run: `grep -rl "using vali_deploy.Models;" vali-deploy/ vali-deploy.Tests/`
Expected: lista de archivos afectados (`MenuManager.cs`, `ProjectManager.cs`, `ChartManager.cs`, `DeployConfig.cs`, `ProjectRepository.cs`, tests de `SubProjectTests.cs`/`ProjectRepositoryTests.cs`, y cualquier otro reportado). Reemplazar en cada uno `using vali_deploy.Models;` por `using vali_deploy.Domain;` (si el archivo ya tiene `using vali_deploy.Domain;`, solo borrar la línea de `Models`). También actualizar `DeployConfig.cs` (Task 22), que hoy tiene `using vali_deploy.Models;` para `Project` — pasa a no necesitarlo porque `Project` ya vive en `vali_deploy.Domain`, mismo namespace del archivo.

- [ ] **Step 4: Confirmar que compila y toda la suite pasa**

Run: `dotnet build vali-deploy.sln && dotnet test vali-deploy.sln`
Expected: `Build succeeded.` y todos los tests en verde (ningún test debería fallar por este cambio — es solo namespace).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(domain): mover Project y SubProject a Domain/ (paso 1 del plan de migración de MenuManager)"
```

---

## Task 30: Limpieza — retirar `ProjectManager.cs` y `Models/` vacíos, documentar deuda restante

**Files:**
- Delete: `vali-deploy/Managers/ProjectManager.cs` (reemplazado por `ProjectRepository`)
- Modify: `vali-deploy/Managers/MenuManager.cs` (reemplazar todas las llamadas `ProjectManager.*` por `ProjectRepository`/`CompositionRoot`)
- Modify: `CLAUDE.md` (actualizar sección "Módulos principales" y "NO hacer")

Último paso del plan de migración: apaga el código viejo de persistencia ahora que `ExecuteCommandSubProject` (Task 28) y el resto del menú ya pueden usar `IProjectRepository`. Se hace al final porque `ProjectManager.SaveConfig` se llama **13 veces** en `MenuManager.cs` (según el mapeo inicial) — hasta que todo lo demás está migrado y probado, retirarlo de una sola vez es el punto de menor riesgo.

- [ ] **Step 1: Reemplazar cada llamada a `ProjectManager` por el repositorio nuevo**

Patrón de reemplazo (aplicar en cada una de las ~17 ubicaciones reportadas: `LoadOrCreateConfig` en L22/L118, `AddProject` en L137, `RemoveProject` en L276, `SaveConfig` en las 13 ubicaciones restantes):

```csharp
// Antes:
_projects = ProjectManager.LoadOrCreateConfig();
// ...
ProjectManager.SaveConfig(_projects);

// Después:
var repository = CompositionRoot.CreateProjectRepository();
var config = repository.Load();
_projects = config.Projects;
// ...
config.Projects = _projects;
repository.Save(config);
```

Nota: como `_projects` es un campo estático mutado en 13+ lugares distintos, el reemplazo más seguro es introducir un helper privado `LoadConfig()`/`PersistProjects()` dentro de `MenuManager` que envuelva `repository.Load()`/`repository.Save()` preservando `Environments` (para no pisarlos al guardar solo `_projects`), en vez de repetir el patrón anterior 17 veces. Escribir ese helper como parte de este step:

```csharp
private static Infrastructure.IProjectRepository _repository = CompositionRoot.CreateProjectRepository();

private static void PersistProjects()
{
    var config = _repository.Load();
    config.Projects = _projects;
    _repository.Save(config);
}
```

Y reemplazar cada `ProjectManager.SaveConfig(_projects);` por `PersistProjects();`, cada `ProjectManager.LoadOrCreateConfig()` por `_repository.Load().Projects`, `ProjectManager.AddProject(name, project)` por la lógica equivalente sobre `_repository.Load()`/`.Projects.TryAdd(...)`/`_repository.Save(...)`, y `ProjectManager.RemoveProject(name)` de forma análoga.

- [ ] **Step 2: Borrar `ProjectManager.cs`**

Run: `git rm vali-deploy/Managers/ProjectManager.cs`

- [ ] **Step 3: Confirmar que compila (no debe quedar ninguna referencia a `ProjectManager`)**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.` Si falla con `CS0103: The name 'ProjectManager' does not exist`, todavía queda una llamada sin migrar — volver al Step 1.

Run: `grep -rn "ProjectManager\." vali-deploy/`
Expected: sin resultados.

- [ ] **Step 4: Correr toda la suite y probar el CLI manualmente**

Run: `dotnet test vali-deploy.sln`
Expected: todos los tests en verde.

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`
Expected: alta de proyecto, alta de subproyecto, remove, show projects, y el flujo de pipeline (Tasks 25-28) funcionan igual que antes de esta tarea — el único cambio es de dónde lee/escribe la config, no el comportamiento visible.

- [ ] **Step 5: Actualizar `CLAUDE.md`**

En la sección "Módulos principales", reemplazar la línea de `Managers/ProjectManager.cs` por:

```markdown
- `Infrastructure/ProjectRepository.cs` — CRUD de `Project`/`SubProject` y `DeployEnvironment`, persistencia de `DeployConfig` (Projects + Environments) en `deploy_config.json` (System.Text.Json)
```

Y en "NO hacer", tachar/actualizar el ítem sobre `RunCommandsAsync` no verificando exit code (ya no aplica — `ProcessRunner` sí lo verifica) y el de credenciales en texto plano (parcialmente resuelto: `DockerRegistryTokenEnvVar` es el reemplazo, pero `DockerHubUser` sigue existiendo en `SubProject` hasta que el flujo Docker viejo de `ExecuteCommandSubProject` se retire por completo — documentar esto como deuda explícita restante).

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(infra): retirar ProjectManager en favor de ProjectRepository, actualizar CLAUDE.md"
```

---

## Self-Review (aplicado por el autor del plan antes de entregar)

**1. Cobertura del spec:**
- Auth SSH clave pública/privada + passphrase por env var → Task 3 (`RemoteServer`), Task 17 (`SshClientFactory`).
- Modelo híbrido pasos tipados + `RawCommand` → Task 2 (enum completo con los 14 `StepType`), Task 11 (`RawCommandExecutor`).
- SO remoto Windows/Linux → Task 3 (`RemoteOs`), Task 17 (`SshClientFactory` arma `powershell`/`bash` según `Os`).
- Docker Compose vía registry → Tasks 13, 19.
- Variante DockerSave/DockerLoad sin registry → Tasks 14, 17.
- Publish/Zip clásico → Task 16 (`ZipPublishExecutor`, con nota explícita de alcance sobre `OmitFiles`/zip pendiente de decisión de diseño).
- Plantillas editables → Tasks 20, 26.
- Credenciales solo por referencia a env var → Task 7 (`EnvVarSecretResolver`), usado por `SshClientFactory` (Task 17).
- `DeployEnvironment` multi-entorno de primer nivel → Task 4, Task 22 (`Environments` a nivel raíz de `DeployConfig`).
- Deuda técnica (exit code, credenciales, logging, God Class) → Task 8 (`ProcessRunner` verifica exit code), Task 21 (`PipelineLogger`), Tasks 29-30 (migración de `MenuManager`).
- UI `AnsiConsole.Progress` + tabla resumen → Task 24.
- Testing xUnit con mocks, sin integration tests SSH real → Task 1 (setup), todas las tareas de executor mockean `IProcessRunner`/`ISshClientFactory`, ninguna abre una conexión SSH real.
- Migración incremental de `MenuManager` sin romper el CLI → Tasks 25, 27, 28 (agregan sin romper), Tasks 29-30 (al final, cuando ya no hay riesgo).
- Flag `SyncBeforeBuild` unificado en `GitCheckout` (corrección aplicada post-brainstorming) → Task 12.
- DockerSave/DockerLoad como variante del template, no sistema aparte → Task 14 (ejecutor local), Task 17 (ejecutor remoto), no se creó ningún `PipelineTemplateFactory` method nuevo para esto — se deja como armado manual en `PipelineEditorMenu` (Task 26), tal como dice el spec actualizado.

**2. Placeholders:** ninguno — cada step con código trae la implementación completa, cada test trae asserts concretos. La única excepción documentada explícitamente es el alcance de `OmitFiles`/zip en Task 16, marcado como decisión de diseño pendiente (no un placeholder de "TODO", sino un límite de alcance justificado).

**3. Consistencia de tipos:** `IStepExecutor.Handles`/`ExecuteAsync(DeployStep, StepExecutionContext)` se usa idéntico en las 14 tareas de executor. `ProcessRunResult(int ExitCode, string StdOut, string StdErr)` (Task 8) se referencia igual en `ISshClientFactory.RunCommandAsync` (Task 17) para reutilizar el mismo tipo de retorno entre ejecución local y remota. `IProjectRepository.Load()/Save(DeployConfig)` (Task 22) es lo que consume `PipelineEditorMenu`/`EnvironmentMenu`/`MenuManager` en las Tasks 25-30, sin variantes de firma.

---

Plan completo y guardado en `docs/plans/2026-07-08-ssh-deploy-pipeline-implementation.md`. Dos opciones de ejecución:

1. **Subagent-Driven (recomendado)** — un subagente fresco por tarea, review entre tareas, iteración rápida.
2. **Inline Execution** — ejecución en esta sesión con `executing-plans`, por lotes con checkpoints.
