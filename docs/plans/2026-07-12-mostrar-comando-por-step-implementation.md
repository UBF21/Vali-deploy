# Mostrar el comando ejecutado en cada step Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cada `StepResult` lleva el comando que efectivamente se ejecutó, y `PipelineExecutionView` lo muestra en una columna nueva de la tabla resumen.

**Architecture:** `StepResult.Command` (nuevo campo) + `StepResultFactory.FromProcessResult` gana un parámetro `command`. Los 16 `IStepExecutor` se agrupan en 5 categorías por cómo construyen su comando (ver spec); cada categoría es un task independiente por archivo, todos bloqueados solo por Task 1 (el campo en `StepResult`/`StepResultFactory`), y entre sí son independientes (archivos distintos) — se pueden despachar en paralelo.

**Tech Stack:** .NET 7, xUnit 2.6.6 + Moq.

**Spec:** `docs/specs/2026-07-12-mostrar-comando-por-step-design.md`

---

### Task 1: `StepResult.Command` + `StepResultFactory`

**Bloquea todos los demás tasks. Sin dependencias.**

**Files:**
- Modify: `vali-deploy/Domain/StepResult.cs`
- Modify: `vali-deploy/Application/Executors/StepResultFactory.cs`

- [ ] **Step 1: Agregar `Command` a `StepResult`**

```csharp
namespace vali_deploy.Domain;

public class StepResult
{
    public DeployStep Step { get; set; } = new();
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public string Command { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int AttemptNumber { get; set; } = 1;
    public bool WasSkippedDueToContinueOnFailure { get; set; } = false;
}
```

- [ ] **Step 2: `StepResultFactory.FromProcessResult` gana el parámetro `command`**

```csharp
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Application.Executors;

internal static class StepResultFactory
{
    public static StepResult NoServer(DeployStep step, StepExecutionContext context, TimeSpan duration) =>
        new()
        {
            Step = step, Success = false, ExitCode = -1,
            Error = $"El DeployEnvironment '{context.Environment.Name}' no tiene RemoteServer configurado.",
            Duration = duration
        };

    public static StepResult FromProcessResult(DeployStep step, ProcessRunResult run, string command, TimeSpan duration) =>
        new()
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Command = command, Duration = duration
        };
}
```

Esto rompe la compilación de TODOS los executores que llaman `FromProcessResult` con 3 argumentos (los de Categoría B/C, Task 2 y 3) — es esperado, cada task lo arregla en su propio archivo.

- [ ] **Step 3: Compilar (va a fallar, es esperado)**

Run: `dotnet build vali-deploy.sln`
Expected: FAIL — varios executores llaman `FromProcessResult` con la firma vieja (3 args). Confirmá que los ÚNICOS errores son "no overload for method 'FromProcessResult' takes 3 arguments" en los archivos de Categoría B/C (ver Task 2/3) — si hay CUALQUIER otro error, algo salió mal en este task.

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Domain/StepResult.cs vali-deploy/Application/Executors/StepResultFactory.cs
git commit -m "feat(domain): agregar StepResult.Command para registrar el comando ejecutado por step"
```

---

### Task 2: Categoría A+B — executores que usan `IProcessRunner`/`ISshClientFactory` con comando construido

**Depends on:** Task 1
**Independiente de Task 3, 4, 5, 6 — se puede despachar en paralelo una vez Task 1 esté commiteado.**

**Files:**
- Modify: `vali-deploy/Application/Executors/DockerBuildExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerImagePruneExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerSaveExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerComposeDownExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerComposePullExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerComposeUpExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerComposeBuildExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerLoadExecutor.cs`
- Test: `vali-deploy.Tests/Application/Executors/*` (los archivos de test correspondientes a cada uno de los 8 executores de arriba — buscarlos por nombre, ej. `DockerBuildExecutorTests.cs`, y `DockerComposeExecutorsTests.cs` para los 3 de Compose que comparten archivo)

- [ ] **Step 1: `DockerBuildExecutor.cs`** — el comando ya está en la variable `command` (línea 18). Agregar `Command = command` al `BuildResult`:

```csharp
    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string command, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = run.StdOut,
        Error = run.StdErr,
        Command = command,
        Duration = duration
    };
```

Y actualizar la única llamada (línea 23): `return BuildResult(step, run, command, stopwatch.Elapsed);`

- [ ] **Step 2: `DockerImagePruneExecutor.cs`** — comando ya en variable `command` (línea 20). Agregar `Command = command,` al `new StepResult { ... }` (líneas 24-28).

- [ ] **Step 3: `DockerSaveExecutor.cs`** — comando armado inline en la llamada (línea 21). Extraer a variable:

```csharp
        var command = $"docker save -o \"{outputTarPath}\" {imageTag}";
        var run = await _processRunner.RunAsync(command, context.ProjectPath);
        stopwatch.Stop();

        return new StepResult
        {
            Step = step, Success = run.ExitCode == 0, ExitCode = run.ExitCode,
            Output = run.StdOut, Error = run.StdErr, Command = command, Duration = stopwatch.Elapsed
        };
```

- [ ] **Step 4-8: Los 5 executores basados en `ISshClientFactory.RunCommandAsync` + `StepResultFactory.FromProcessResult`** (`DockerComposeDownExecutor`, `DockerComposePullExecutor`, `DockerComposeUpExecutor`, `DockerComposeBuildExecutor`, `DockerLoadExecutor`) — mismo patrón en los 5: extraer el comando interpolado a una variable antes de la llamada, y pasarla también a `FromProcessResult`. Ejemplo con `DockerComposeUpExecutor.cs`:

```csharp
        var command = $"docker compose -f \"{composeFilePath}\" up -d";
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, command, stopwatch.Elapsed);
```

Aplicar el mismo cambio (extraer variable `command`, pasarla a `FromProcessResult`) en:
- `DockerComposeDownExecutor.cs`: `$"docker compose -f \"{composeFilePath}\" down"`
- `DockerComposePullExecutor.cs`: `$"docker compose -f \"{composeFilePath}\" pull"`
- `DockerComposeBuildExecutor.cs`: `$"docker compose -f \"{composeFilePath}\" build"`
- `DockerLoadExecutor.cs`: `$"docker load -i \"{remoteTarPath}\""`

- [ ] **Step 9: Actualizar tests** — para cada uno de los 8 executores, agregar `Assert.Equal("<comando esperado>", result.Command);` al test que ya verifica éxito (ej. en `DockerComposeExecutorsTests.cs`, el test `Up_runs_docker_compose_up_detached_on_remote` ya construye el `result` — agregarle el assert de `Command` ahí, no un test nuevo separado). Localizar el archivo de test exacto de cada executor con `Grep` antes de editar (algunos comparten archivo, como los 3 de Compose que están en `DockerComposeExecutorsTests.cs`).

- [ ] **Step 10: Compilar y correr los tests de este grupo**

Run: `dotnet build vali-deploy.sln` — es esperado que SIGA fallando si Task 3/4/5 todavía no terminaron (otros executores con la firma vieja). Confirmá que no hay errores en NINGUNO de los 8 archivos de este task.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "DockerBuildExecutorTests|DockerImagePruneExecutorTests|DockerSaveExecutorTests|DockerComposeExecutorsTests|DockerLoadExecutorTests"` (ajustar los nombres exactos de clase de test según lo que encuentres — no asumir, verificar con Grep primero).
Expected: PASS.

- [ ] **Step 11: Commit**

```bash
git add vali-deploy/Application/Executors/DockerBuildExecutor.cs vali-deploy/Application/Executors/DockerImagePruneExecutor.cs vali-deploy/Application/Executors/DockerSaveExecutor.cs vali-deploy/Application/Executors/DockerComposeDownExecutor.cs vali-deploy/Application/Executors/DockerComposePullExecutor.cs vali-deploy/Application/Executors/DockerComposeUpExecutor.cs vali-deploy/Application/Executors/DockerComposeBuildExecutor.cs vali-deploy/Application/Executors/DockerLoadExecutor.cs vali-deploy.Tests/Application/Executors/
git commit -m "feat(application): registrar Command en executores Docker/Compose basados en IProcessRunner/ISshClientFactory"
```

(Ajustar el `git add` de la carpeta de tests a los archivos puntuales tocados si preferís no usar el wildcard de carpeta.)

---

### Task 3: Categoría C — `LocalCommandExecutor`, `RawCommandExecutor`, `SshCommandExecutor`

**Depends on:** Task 1
**Independiente de Task 2, 4, 5, 6.**

**Files:**
- Modify: `vali-deploy/Application/Executors/LocalCommandExecutor.cs`
- Modify: `vali-deploy/Application/Executors/RawCommandExecutor.cs`
- Modify: `vali-deploy/Application/Executors/SshCommandExecutor.cs`
- Test: los archivos de test correspondientes (buscar con Grep)

- [ ] **Step 1: `LocalCommandExecutor.cs` y `RawCommandExecutor.cs`** — ambos ya tienen `command` como variable local (de `Args["Command"]`). Agregar `Command = command,` al `new StepResult { ... }` en ambos (mismo bloque en los dos archivos, líneas 27-35 aprox.).

- [ ] **Step 2: `SshCommandExecutor.cs`** — ya tiene `command` como variable local (línea 25, de `Args["Command"]`). Cambiar la llamada final:

```csharp
        var run = await _sshClientFactory.RunCommandAsync(context.Environment.Server, command);
        stopwatch.Stop();

        return StepResultFactory.FromProcessResult(step, run, command, stopwatch.Elapsed);
```

- [ ] **Step 3: Actualizar tests** de los 3 executores con `Assert.Equal(command, result.Command)` (o el valor literal usado en cada test).

- [ ] **Step 4: Correr los tests de este grupo**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "LocalCommandExecutorTests|RawCommandExecutorTests|SshCommandExecutorTests"` (ajustar nombres exactos tras Grep).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/LocalCommandExecutor.cs vali-deploy/Application/Executors/RawCommandExecutor.cs vali-deploy/Application/Executors/SshCommandExecutor.cs vali-deploy.Tests/Application/Executors/
git commit -m "feat(application): registrar Command en LocalCommand/RawCommand/SshCommand executors"
```

---

### Task 4: Categoría D+G — `DockerRunExecutor`, `CopyToRemoteExecutor`

**Depends on:** Task 1
**Independiente de Task 2, 3, 5, 6.**

**Files:**
- Modify: `vali-deploy/Application/Executors/DockerRunExecutor.cs`
- Modify: `vali-deploy/Application/Executors/CopyToRemoteExecutor.cs`
- Test: los archivos de test correspondientes

- [ ] **Step 1: `DockerRunExecutor.cs`** — comando ya en variable `command` (línea 27). Agregar `Command = command,` al `new StepResult { ... }` (líneas 32-38).

- [ ] **Step 2: `CopyToRemoteExecutor.cs`** — no hay comando real, es una descripción de la operación. Agregar en AMBOS puntos de retorno (éxito línea 48, error líneas 53-56):

```csharp
        try
        {
            await _sshClientFactory.UploadFileAsync(context.Environment.Server, localPath, remotePath);
            stopwatch.Stop();
            return new StepResult { Step = step, Success = true, ExitCode = 0, Command = $"upload {localPath} → {remotePath}", Duration = stopwatch.Elapsed };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new StepResult
            {
                Step = step, Success = false, ExitCode = -1, Error = ex.Message,
                Command = $"upload {localPath} → {remotePath}", Duration = stopwatch.Elapsed
            };
        }
```

- [ ] **Step 3: Actualizar tests** de ambos executores con el assert de `Command` correspondiente.

- [ ] **Step 4: Correr los tests de este grupo**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "DockerRunExecutorTests|CopyToRemoteExecutorTests"` (ajustar nombres exactos tras Grep).
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Application/Executors/DockerRunExecutor.cs vali-deploy/Application/Executors/CopyToRemoteExecutor.cs vali-deploy.Tests/Application/Executors/
git commit -m "feat(application): registrar Command en DockerRun y CopyToRemote executors"
```

---

### Task 5: Categoría E+F — `GitCheckoutExecutor`, `DockerPushExecutor`, `ZipPublishExecutor` (multi-comando)

**Depends on:** Task 1
**Independiente de Task 2, 3, 4, 6. Es el más delicado — requiere concatenar solo los comandos que realmente corrieron.**

**Files:**
- Modify: `vali-deploy/Application/Executors/GitCheckoutExecutor.cs`
- Modify: `vali-deploy/Application/Executors/DockerPushExecutor.cs`
- Modify: `vali-deploy/Application/Executors/ZipPublishExecutor.cs`
- Test: los archivos de test correspondientes

- [ ] **Step 1: `GitCheckoutExecutor.cs`** — `BuildResult` gana un parámetro `command`, y cada uno de los 3 call-sites (líneas 38, 44, 50) pasa el/los comando(s) que realmente corrieron:

```csharp
        var checkoutCommand = $"git checkout {branch}";
        var checkoutResult = await _processRunner.RunAsync(checkoutCommand, context.ProjectPath);

        if (checkoutResult.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, checkoutResult, checkoutResult.StdOut, checkoutCommand, stopwatch.Elapsed);
        }

        if (!ShouldSyncBeforeBuild(step))
        {
            stopwatch.Stop();
            return BuildResult(step, checkoutResult, checkoutResult.StdOut, checkoutCommand, stopwatch.Elapsed);
        }

        const string pullCommand = "git pull";
        var pullResult = await _processRunner.RunAsync(pullCommand, context.ProjectPath);
        stopwatch.Stop();

        return BuildResult(step, pullResult, checkoutResult.StdOut + pullResult.StdOut, $"{checkoutCommand} && {pullCommand}", stopwatch.Elapsed);
```

```csharp
    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, string command, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Command = command,
        Duration = duration
    };
```

`MissingBranchResult`/`InvalidBranchResult` no ejecutaron ningún comando — no les toca `Command` (queda `""` default).

- [ ] **Step 2: `DockerPushExecutor.cs`** — `BuildResult` gana parámetro `command`; los 3 call-sites (líneas 31, 39, 45) pasan el comando acumulado hasta ese punto:

```csharp
        var loginCommand = await TryLoginAsync(step, context, extraEnv);
        if (loginCommand.Run != null && loginCommand.Run.ExitCode != 0)
        {
            stopwatch.Stop();
            return BuildResult(step, loginCommand.Run, loginCommand.Run.StdOut, loginCommand.Command ?? "", stopwatch.Elapsed);
        }

        var tagCommand = $"docker tag {imageTag} {registryTag}";
        var tagRun = await _processRunner.RunAsync(tagCommand, context.ProjectPath, extraEnv);

        if (tagRun.ExitCode != 0)
        {
            stopwatch.Stop();
            var commandSoFar = loginCommand.Command != null ? $"{loginCommand.Command} && {tagCommand}" : tagCommand;
            return BuildResult(step, tagRun, tagRun.StdOut, commandSoFar, stopwatch.Elapsed);
        }

        var pushCommand = $"docker push {registryTag}";
        var pushRun = await _processRunner.RunAsync(pushCommand, context.ProjectPath, extraEnv);
        stopwatch.Stop();

        var fullCommand = loginCommand.Command != null ? $"{loginCommand.Command} && {tagCommand} && {pushCommand}" : $"{tagCommand} && {pushCommand}";
        return BuildResult(step, pushRun, tagRun.StdOut + pushRun.StdOut, fullCommand, stopwatch.Elapsed);
    }

    private async Task<(ProcessRunResult? Run, string? Command)> TryLoginAsync(DeployStep step, StepExecutionContext context, IDictionary<string, string> extraEnv)
    {
        var registryHost = step.Args.GetValueOrDefault("RegistryHost", "");
        var registryUsername = step.Args.GetValueOrDefault("RegistryUsername", "");
        var registryTokenEnvVar = step.Args.GetValueOrDefault("RegistryTokenEnvVar", "");

        if (string.IsNullOrEmpty(registryTokenEnvVar))
        {
            return (null, null);
        }

        var token = _secretResolver.Resolve(registryTokenEnvVar);
        var loginCommand = string.IsNullOrEmpty(registryHost)
            ? $"docker login -u {registryUsername} --password-stdin"
            : $"docker login {registryHost} -u {registryUsername} --password-stdin";

        var run = await _processRunner.RunAsync(loginCommand, context.ProjectPath, extraEnv, token);
        return (run, loginCommand);
    }

    private static StepResult BuildResult(DeployStep step, ProcessRunResult run, string output, string command, TimeSpan duration) => new()
    {
        Step = step,
        Success = run.ExitCode == 0,
        ExitCode = run.ExitCode,
        Output = output,
        Error = run.StdErr,
        Command = command,
        Duration = duration
    };
```

`TryLoginAsync` cambia de firma (devuelve tupla en vez de `ProcessRunResult?`) — actualizar el nombre de la variable en `ExecuteAsync` de `loginRun` a `loginCommand` (tupla) como en el ejemplo de arriba, y todos sus usos.

- [ ] **Step 3: `ZipPublishExecutor.cs`** — trackear los comandos ejecutados en una lista, y pasarlos a cada `StepResult` de retorno:

```csharp
        var combinedOutput = new StringBuilder();
        var executedCommands = new List<string>();

        foreach (var command in BuildCommands(step))
        {
            executedCommands.Add(command);
            var run = await _processRunner.RunAsync(command, context.ProjectPath);
            combinedOutput.AppendLine(run.StdOut);

            if (run.ExitCode != 0)
            {
                stopwatch.Stop();
                return FailureResult(step, run, combinedOutput.ToString(), string.Join(" && ", executedCommands), stopwatch.Elapsed);
            }
        }

        var publishFolder = FindPublishFolder(context.ProjectPath);
        if (publishFolder == null)
        {
            stopwatch.Stop();
            return PublishFolderNotFoundResult(step, combinedOutput.ToString(), string.Join(" && ", executedCommands), stopwatch.Elapsed);
        }

        var omitFiles = ParseOmitFiles(step);
        var zipPath = CreateZip(publishFolder, context.SubProjectName, omitFiles);
        combinedOutput.AppendLine($"Comprimido en: {zipPath}");
        context.LastArtifactPath = zipPath;

        stopwatch.Stop();
        return SuccessResult(step, combinedOutput.ToString(), string.Join(" && ", executedCommands), stopwatch.Elapsed);
```

```csharp
    private static StepResult PublishFolderNotFoundResult(DeployStep step, string output, string command, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = -1,
        Output = output, Error = "No se encontró la carpeta 'publish' dentro de bin/Release tras el build.",
        Command = command, Duration = duration
    };

    private static StepResult FailureResult(DeployStep step, ProcessRunResult run, string output, string command, TimeSpan duration) => new()
    {
        Step = step, Success = false, ExitCode = run.ExitCode, Output = output, Error = run.StdErr, Command = command, Duration = duration
    };

    private static StepResult SuccessResult(DeployStep step, string output, string command, TimeSpan duration) => new()
    {
        Step = step, Success = true, ExitCode = 0, Output = output, Command = command, Duration = duration
    };
```

`PathNotFoundResult` no cambia — nunca llegó a ejecutar ningún comando.

- [ ] **Step 4: Actualizar tests** de los 3 executores — agregar asserts de `Command` cubriendo al menos: caso "solo checkout" (sin pull) y "checkout + pull" para `GitCheckoutExecutor`; caso "sin login" y "con login" para `DockerPushExecutor`; caso "falla en el 2do comando de limpieza" (solo ese comando en `Command`) para `ZipPublishExecutor`.

- [ ] **Step 5: Correr los tests de este grupo**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter "GitCheckoutExecutorTests|DockerPushExecutorTests|ZipPublishExecutorTests"` (ajustar nombres exactos tras Grep).
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Application/Executors/GitCheckoutExecutor.cs vali-deploy/Application/Executors/DockerPushExecutor.cs vali-deploy/Application/Executors/ZipPublishExecutor.cs vali-deploy.Tests/Application/Executors/
git commit -m "feat(application): registrar Command concatenado en executores multi-comando"
```

---

### Task 6: `PipelineExecutionView.cs` — columna nueva en la tabla resumen

**Depends on:** Task 1 (solo necesita que `StepResult.Command` exista)
**Independiente de Task 2, 3, 4, 5.**

**Files:**
- Modify: `vali-deploy/Presentation/PipelineExecutionView.cs`

Sin test — Presentation/Spectre.Console no testeable en este repo (criterio ya establecido).

- [ ] **Step 1: Agregar la columna "Comando" a `RenderSummaryTable`**

```csharp
    private static void RenderSummaryTable(PipelineResult result)
    {
        var table = new Table().AddColumns("Paso", "Comando", "Estado", "Duración", "Exit Code");

        foreach (var stepResult in result.Steps)
        {
            table.AddRow(
                stepResult.Step.Name,
                string.IsNullOrEmpty(stepResult.Command) ? "[grey](sin comando)[/]" : Markup.Escape(stepResult.Command),
                DescribeEstado(stepResult),
                stepResult.Duration.ToString(@"mm\:ss"),
                stepResult.ExitCode.ToString());
        }

        AnsiConsole.Write(table);
        RenderFailureDetails(result);
    }
```

- [ ] **Step 2: Compilar (debería estar todo verde si los Tasks 2-5 ya commitearon)**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/PipelineExecutionView.cs
git commit -m "feat(presentation): mostrar el comando ejecutado en cada step de la tabla resumen"
```

---

### Task 7: Build final + verificación manual

**Depends on:** Task 1, 2, 3, 4, 5, 6 (todos commiteados)

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Build y test suite completos**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, todos los tests (160 previos + los nuevos asserts/tests de Command — no se agregan tests completamente nuevos salvo los de concatenación de Task 5, así que el conteo sube solo por esos).

- [ ] **Step 2: Correr un pipeline real y verificar la columna nueva**

`dotnet run` → correr cualquier pipeline con al menos 2-3 steps de tipos distintos (ej. SshCommand + DockerComposeBuild + DockerComposeUp). Confirmar que la tabla resumen muestra una columna "Comando" con el comando real de cada step, legible y sin errores de markup.

Si cualquiera de estos pasos falla, corregir el código en el task correspondiente y volver a compilar/testear antes de continuar.

---

## Self-review

**Cobertura de la spec:** las 7 categorías (A-G) del spec están cubiertas 1:1 por Task 2 (A+B), Task 3 (C), Task 4 (D+G), Task 5 (E+F). La columna nueva está en Task 6.

**Consistencia de tipos:** `StepResultFactory.FromProcessResult` tiene la misma firma nueva (`command: string` como 3er parámetro, antes de `duration`) usada consistentemente en Task 2 y Task 3. `BuildResult` en `GitCheckoutExecutor`/`DockerPushExecutor` (Task 5) gana el mismo patrón de parámetro `command` al final, antes de `duration`.

**Riesgo de coordinación:** Task 2, 3, 4, 5, 6 tocan archivos completamente distintos entre sí (ninguno se pisa), pero TODOS dependen de que Task 1 esté commiteado primero (cambia la firma de `FromProcessResult`, usada por Task 2 y 3). Ejecutar Task 1 solo, confirmar commit, y recién ahí despachar 2-6 en paralelo.

**Sin placeholders:** todos los steps tienen código completo. Los nombres exactos de clases de test se dejan como "verificar con Grep" en vez de asumir, porque no se relevaron en la exploración previa — es la única ambigüedad intencional del plan, resuelta por el propio subagente al ejecutar.
