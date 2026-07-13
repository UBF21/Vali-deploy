# Mostrar el comando ejecutado en cada step — Design Spec

**Fecha:** 2026-07-12
**Contexto:** el usuario pidió ver qué comando se ejecuta en cada paso del pipeline, no solo el nombre/estado. Complementa el fix de esta misma sesión que ya muestra el output/error de un step fallido — ahora se agrega también el comando que lo produjo, tanto para diagnóstico como para entender qué hace cada step sin tener que leer el código.

## Alcance

Agregar `Command` a `StepResult`, poblado por cada uno de los 16 `IStepExecutor`, y mostrarlo en la tabla resumen de `PipelineExecutionView` (`RenderSummaryTable`) junto al resto de columnas.

## Diseño

### 1. `Domain/StepResult.cs`

```csharp
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

### 2. `Application/Executors/StepResultFactory.cs` — gana el parámetro `command`

```csharp
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

`NoServer` no gana `command` — no llegó a ejecutarse nada.

### 3. Regla de asignación por categoría de executor (de la exploración de los 16 archivos)

**Categoría A — comando único, ya extraído a una variable antes de ejecutar** (`DockerBuildExecutor`, `DockerImagePruneExecutor`): pasar esa misma variable al `StepResult`/`FromProcessResult`.

**Categoría B — comando único armado inline dentro de la llamada** (`DockerSaveExecutor`, `DockerComposeDownExecutor`, `DockerComposePullExecutor`, `DockerComposeUpExecutor`, `DockerComposeBuildExecutor`, `DockerLoadExecutor`): extraer a una variable local antes de la llamada, para no repetir la interpolación, y pasarla también a `FromProcessResult`.

**Categoría C — comando literal de `Args["Command"]`** (`LocalCommandExecutor`, `RawCommandExecutor`, `SshCommandExecutor`): pasar esa misma variable `command` ya existente.

**Categoría D — `IInteractiveProcessLauncher`, no `IProcessRunner`** (`DockerRunExecutor`): no pasa por `StepResultFactory` (arma el `StepResult` a mano) — agregar `Command = command` al objeto ya construido.

**Categoría E — múltiples comandos condicionales en secuencia** (`GitCheckoutExecutor`: checkout + pull opcional; `DockerPushExecutor`: login opcional + tag + push): igual que ya se hace con `Output` (que concatena `checkoutResult.StdOut + pullResult.StdOut`), `Command` concatena con `" && "` los comandos que realmente llegaron a ejecutarse hasta el punto de retorno — nunca comandos que no corrieron.

**Categoría F — loop de N comandos** (`ZipPublishExecutor`: hasta 4 comandos de limpieza/build/publish): mismo patrón, `Command` = `string.Join(" && ", comandosEjecutadosHastaAhora)`. En los casos de retorno temprano sin haber corrido ningún comando (`PathNotFoundResult`), `Command` queda `""` (default).

**Categoría G — sin comando de shell** (`CopyToRemoteExecutor`: usa `UploadFileAsync`, transferencia de archivo, no ejecución remota): `Command` se completa con una descripción de la operación, no un comando real: `$"upload {localPath} → {remotePath}"`. No es un comando ejecutable, es informativo — se documenta así para que no se confunda con los demás.

### 4. `Presentation/PipelineExecutionView.cs` — nueva columna

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

`Markup.Escape` porque `Command` es texto libre (interpola paths/args del usuario) que puede contener `[`/`]`.

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| Step que no llega a ejecutar ningún comando (ej. `ZipPublishExecutor` con `context.ProjectPath` inexistente) | `Command` queda `""`, la tabla muestra `(sin comando)`. |
| Step con múltiples comandos donde el primero falla | `Command` solo incluye el/los comando(s) que realmente corrieron antes del fallo, nunca los que no llegaron a ejecutarse. |

## Testing

Para cada executor de las categorías A-D y G, agregar/actualizar un assert en su test existente confirmando `result.Command` (ej. `Assert.Equal("docker build -f ... ", result.Command)`). Para categorías E-F (`GitCheckoutExecutor`, `DockerPushExecutor`, `ZipPublishExecutor`), agregar un test nuevo por escenario de concatenación (ej. "checkout + pull concatenados", "solo checkout cuando SyncBeforeBuild=false").

Sin test para `PipelineExecutionView.cs` (Presentation/Spectre.Console, criterio ya establecido en el repo).

## Decisiones registradas

- `Command` es siempre texto informativo, no necesariamente "pegable" en una terminal (ej. la descripción de `CopyToRemoteExecutor` no es un comando real).
- Se concatena con `" && "` en los casos multi-comando, reflejando que se ejecutaron secuencialmente y cada uno dependía del éxito del anterior (mismo significado semántico que ese operador en shell).
- `NoServer` no lleva comando — no hay nada que reportar cuando el step nunca llegó a intentar ejecutar algo.
