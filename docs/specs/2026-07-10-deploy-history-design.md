# Historial de deploys consultable desde el CLI — Design Spec

**Fecha:** 2026-07-10
**Ciclo:** Ciclo 2 del roadmap de deuda técnica / features de Vali-Deploy (posterior al Ciclo 1, ya mergeado a main)

## Contexto y problema

`PipelineLogger` (`vali-deploy/Infrastructure/PipelineLogger.cs`) escribe un archivo `.log` de texto plano por cada run de pipeline (`~/Documents/vali-deploy/logs/{proyecto}-{subproyecto}-{timestamp}.log`), pero:

- No hay forma de listar o navegar los runs pasados desde el CLI — hay que abrir el archivo a mano.
- El nombre del archivo no incluye el entorno (`Local`, o el nombre elegido en `ExecuteSubProjectPipelineAsync`), ni si el run tuvo éxito o falló.
- No existe ningún índice: para saber el resultado de un run hay que abrir y leer el texto completo.

Dos call sites generan runs hoy, ambos en `MenuManager.cs`:
- `RunLocalPipelineAsync` (línea ~867) — pipeline efímero de 1 step contra un `DeployEnvironment` reservado `"Local"`, no persistido.
- `ExecuteSubProjectPipelineAsync` (línea ~910) — pipeline completo contra un entorno elegido por el usuario.

## Alcance de esta iteración

- Listado + drill-down a detalle de runs pasados, accesible desde una nueva entrada de menú top-level.
- El historial arranca desde cero: solo se listan runs ejecutados después de este cambio. Los `.log` preexistentes no se migran ni se backfillean — quedan en disco, abribles a mano si hace falta, pero fuera del índice nuevo.
- Fuera de alcance (explícitamente no se hace en este ciclo): filtros avanzados (rango de fechas, búsqueda de texto en output), paginación real, retención/pruning de logs viejos, backfill de runs legacy.

## Arquitectura

### Domain

**`Domain/DeployRunSummary.cs`** (nuevo) — registro de un run, es lo que vive en el índice:

```csharp
public class DeployRunSummary
{
    public string RunId { get; set; } = Guid.NewGuid().ToString("N");
    public string ProjectName { get; set; } = "";
    public string SubProjectName { get; set; } = "";
    public string EnvironmentName { get; set; } = "";
    public DateTime StartedAtUtc { get; set; }
    public bool Success { get; set; }
    public TimeSpan TotalDuration { get; set; }
    public string LogFilePath { get; set; } = "";
}
```

`TotalDuration` es la suma de `StepResult.Duration` de todos los steps del run (no wall-clock de `FinishRun - StartRun`, para no depender de que el caller invoque `FinishRun` inmediatamente).

### Infrastructure

**`IPipelineLogger`** — extendido sin romper compatibilidad binaria del flujo actual, pero sí la firma (ambos call sites se actualizan en el mismo cambio):

```csharp
public interface IPipelineLogger
{
    void StartRun(string projectName, string subProjectName, string environmentName);
    void WriteStep(StepResult stepResult);
    void FinishRun(PipelineResult result);
}
```

- `StartRun` gana `environmentName` y lo guarda en un campo privado junto con `projectName`/`subProjectName`/timestamp de inicio (ya trackeados).
- `FinishRun(PipelineResult result)`:
  1. Agrega un footer al `.log` actual (`=== Run finalizado — Success: {result.Success} — {DateTime.UtcNow:O} ===`).
  2. Construye un `DeployRunSummary` con los datos acumulados desde `StartRun` + `result.Success` + `result.Steps.Sum(s => s.Duration)` + `_currentLogFilePath`.
  3. Serializa el summary a una línea JSON y la appendea a `deploy-history.jsonl` (mismo `_logsDirectory`, `File.AppendAllText`).

**`Domain/DeployHistoryQueryResult.cs`** (nuevo) + **`Infrastructure/IDeployHistoryRepository.cs`** + **`DeployHistoryRepository`** (nuevo):

```csharp
public class DeployHistoryQueryResult
{
    public IReadOnlyList<DeployRunSummary> Runs { get; set; } = new List<DeployRunSummary>();
    public int SkippedCorruptedLines { get; set; }
}

public interface IDeployHistoryRepository
{
    DeployHistoryQueryResult GetRecent(int count, string? projectFilter = null);
}
```

`GetRecent` devuelve un objeto de resultado (no una lista pelada) para poder exponer `SkippedCorruptedLines` sin un parámetro `out` — es el mecanismo concreto con el que se cumple el requisito de "no tragarse en silencio" las líneas corruptas (ver Manejo de errores).

- Lee `deploy-history.jsonl` línea por línea.
- Cada línea se deserializa individualmente (no todo el archivo como un solo JSON) — así una línea corrupta no invalida el resto. Las líneas que fallan deserialización (o deserializan a `null`) se cuentan en `SkippedCorruptedLines`; las líneas en blanco se ignoran sin contar como corruptas.
- Ordena por `StartedAtUtc` descendente, filtra por `ProjectName == projectFilter` si no es null, corta a `count`.
- Si el archivo no existe todavía, devuelve `Runs` vacío y `SkippedCorruptedLines = 0` (no excepción — es el estado esperado en un repo/instalación sin runs).

### Presentation

**`Presentation/DeployHistoryView.cs`** (nuevo, clase estática — mismo patrón que `Presentation/EnvironmentMenu.cs`):

- `static Task ShowAsync(IDeployHistoryRepository repository, IReadOnlyList<string> projectNames)`:
  1. `SelectionPrompt`: "Todos los proyectos" + un choice por cada nombre en `projectNames`.
  2. Llama `repository.GetRecent(30, filtro elegido o null)`.
  3. Si la lista viene vacía → `AnsiConsole.MarkupLine("[yellow]No hay runs registrados todavía.[/]")`, return.
  4. Tabla Spectre: columnas Fecha, Proyecto, Subproyecto, Entorno, Estado (✅/❌ con color, mismo patrón que `PipelineExecutionView.DescribeEstado`), Duración.
  5. `SelectionPrompt` para elegir un run de la lista (formateado como `"{Fecha} · {Proyecto}/{SubProyecto} · {Entorno}"`) + opción "Volver".
  6. Si elige un run → `ShowDetailAsync(entry)`.
- `ShowDetailAsync(DeployRunSummary entry)`:
  - Si `File.Exists(entry.LogFilePath)` es falso → panel de advertencia "El archivo de log de este run ya no existe en disco.", return.
  - Si existe → lee el `.log` completo (`File.ReadAllText`) y lo muestra dentro de un `Panel` de Spectre con el header del run como título (`{Proyecto}/{SubProyecto} · {Entorno} · {Fecha}`).

### MenuManager / CompositionRoot

- El cálculo del directorio de logs por default (`Documents/vali-deploy/logs`) se extrae a `Utils/Constants.DefaultLogsDirectory()` (o método equivalente), usado tanto por `PipelineLogger` como por `DeployHistoryRepository`, para que ambos apunten siempre a la misma carpeta sin duplicar la fórmula.
- `CompositionRoot.CreateDeployHistoryRepository()` → `new DeployHistoryRepository()` usando ese mismo default.
- `MenuManager.GetMainMenuOption()` (línea ~111): se agrega `"View Deploy History"` a `AddChoices(...)`, antes de `"[seagreen1]Exit[/]"`.
- Nuevo `case "View Deploy History":` en el `switch (option)` de `MenuManager.StartAsync()` (línea ~38, junto a `"Manage Environments"`) que llama `Presentation.DeployHistoryView.ShowAsync(CompositionRoot.CreateDeployHistoryRepository(), _projects.Keys.ToList())`.
- `RunLocalPipelineAsync` (línea ~887): `logger.StartRun(projectName, subProject.Name, LocalEnvironment.Name)`; después del loop de `WriteStep` (línea ~895), agregar `logger.FinishRun(result)`.
- `ExecuteSubProjectPipelineAsync` (línea ~945): `logger.StartRun(projectName, subProject.Name, environmentName)`; después del loop de `WriteStep` (línea ~953), agregar `logger.FinishRun(result)`.

## Data flow

```
MenuManager.RunLocalPipelineAsync / ExecuteSubProjectPipelineAsync
    → logger.StartRun(proyecto, subproyecto, entorno)   [guarda estado interno]
    → pipelineRunner ejecuta steps
    → logger.WriteStep(stepResult) × N                  [.log crece, texto plano, sin cambios]
    → logger.FinishRun(result)                          [footer en .log + 1 línea en deploy-history.jsonl]

MenuManager → "View Deploy History"
    → DeployHistoryView.ShowAsync
        → IDeployHistoryRepository.GetRecent(30, filtro)  [lee deploy-history.jsonl, parsea, ordena, filtra]
        → tabla + selección
        → ShowDetailAsync(entry)                          [lee entry.LogFilePath, .log crudo en un Panel]
```

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| `deploy-history.jsonl` no existe (0 runs desde este cambio) | `GetRecent` devuelve lista vacía; la vista muestra "No hay runs registrados todavía." |
| Línea corrupta en el JSONL (crash a mitad de un `AppendAllText`, edición manual) | Se descarta esa línea, se cuenta. `DeployHistoryView` muestra un aviso "`{N}` líneas ilegibles omitidas" al pie de la tabla — no se traga en silencio. |
| `.log` referenciado por un `DeployRunSummary` ya no existe en disco (borrado a mano) | `ShowDetailAsync` muestra un panel de advertencia en vez de lanzar excepción de `File.ReadAllText`. |
| Proyecto filtrado no tiene runs | Igual que "0 runs": mensaje claro, no excepción. |

## Testing

- **`PipelineLoggerTests.cs`** (extender):
  - `StartRun` con el nuevo parámetro `environmentName` — los tests existentes se actualizan a la nueva firma.
  - `FinishRun` escribe el footer esperado en el `.log`.
  - `FinishRun` appendea una línea válida a `deploy-history.jsonl` con los campos correctos (proyecto, subproyecto, entorno, éxito, duración total = suma de steps, ruta al log).
  - Dos runs consecutivos generan dos líneas en el mismo `deploy-history.jsonl` (append, no overwrite) — mismo patrón que el test existente `WriteStep_appends_multiple_steps_without_overwriting_previous_ones`.
- **`DeployHistoryRepositoryTests.cs`** (nuevo):
  - `GetRecent` devuelve los runs ordenados por fecha descendente.
  - `GetRecent` respeta el filtro por proyecto.
  - `GetRecent` respeta el límite `count`.
  - `GetRecent` tolera una línea corrupta en medio del archivo sin perder las demás.
  - `GetRecent` sobre archivo inexistente devuelve lista vacía.
- Sin tests nuevos para `DeployHistoryView` ni el cambio en `MenuManager` — consistente con que la capa Presentation/Manager basada en prompts de Spectre.Console no tiene tests en el repo hoy.

## Decisiones registradas (para no re-derivar)

- Se descartó backfill de logs `.log` preexistentes al índice nuevo — el usuario prefirió arrancar limpio antes que sumar parsing de texto legacy.
- Se descartó reemplazar el `.log` de texto plano por JSON estructurado — el texto plano se mantiene por legibilidad directa (abrir en un editor) y porque ya tiene tests cubriéndolo; el JSONL es un índice *adicional*, no un reemplazo.
- El listado no tiene filtros avanzados (rango de fechas, búsqueda de texto) en este ciclo — solo filtro por proyecto y un tope de 30 runs recientes, para evitar over-engineering hasta que el volumen real de uso lo justifique.
