# Barra de progreso agregada del Pipeline — Design Spec

**Fecha:** 2026-07-10
**Contexto:** feature independiente, posterior al Ciclo 4 (selector de idioma, ya cerrado en `main`). Origen: screenshot de la doc oficial de Spectre.Console mostrando el widget `Progress` con `ProgressBarColumn`/`PercentageColumn`.

## Problema

`Presentation/PipelineExecutionView.cs` hoy muestra una fila de `Progress` por cada `DeployStep` (descripción + spinner + tiempo transcurrido), pero ninguna noción de avance global del pipeline. Para un pipeline de 6 steps, el usuario no tiene forma de saber "voy por la mitad" sin contar manualmente cuántas filas ya tienen ✅.

## Alcance

Agregar una fila adicional "Pipeline" al mismo `Progress` de Spectre.Console, que muestre una barra + porcentaje de **steps completados sobre el total del pipeline** — no un porcentaje falso por step (los comandos externos como `dotnet build`/`docker build` no reportan avance incremental real, así que interpolar sería inventar datos). Confirmado con el usuario en la sesión anterior.

Fuera de alcance: progreso parcial dentro de un step individual (stdout parsing, ETA, etc.) — no hay señal real disponible para eso (`IPipelineRunner` solo reporta post-completado, ver Arquitectura).

## Arquitectura

### Datos disponibles (confirmado por exploración de código, sin cambios de dominio)

- `PipelineExecutionView.RunAsync`/`RunPipelineWithProgressAsync` ya reciben `List<DeployStep> steps` — `steps.Count` es el total, disponible antes de arrancar.
- `IPipelineRunner.RunAsync` reporta vía `IProgress<StepResult>` **una sola vez por step, siempre después de completarlo** (`Application/PipelineRunner.cs`, con retries ya resueltos internamente). No hay reporte al arrancar el pipeline ni al arrancar cada step. Si el pipeline corta por una falla sin `ContinueOnFailure`, los steps restantes simplemente no generan `Report` — la fila "Pipeline" se queda por debajo de 100%, lo cual es correcto (refleja que no todos los steps llegaron a correr).
- No existe (ni hace falta) ningún campo de "total" en `PipelineResult`/`StepResult` — el total ya lo tiene la vista localmente.

### Cambios en `Presentation/PipelineExecutionView.cs`

**Columnas** (`RunAsync`, línea 13-15): Spectre.Console aplica el mismo set de columnas a **todas** las tasks del `Progress` (no hay columnas por-task). Se agrega `ProgressBarColumn` y `PercentageColumn` al set existente:

```csharp
.Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn(), new ElapsedTimeColumn())
```

Efecto secundario aceptado: las filas por-step (que van de `Value=0` a `Value=100` en un solo salto al completarse, `MaxValue` default = 100) también van a mostrar una barra/porcentaje binario (0% mientras corren, 100% al terminar). No es un bug — es consistente con que hoy tampoco hay progreso parcial dentro de un step.

**Fila "Pipeline" — ubicación:** se agrega **primero**, antes de crear el diccionario de tasks por step (`RunPipelineWithProgressAsync`, línea 27), para que Spectre la renderice como primera fila (Spectre preserva el orden de `AddTask`). Actúa como resumen/header por encima del detalle por step.

```csharp
var pipelineTask = ctx.AddTask("Pipeline", autoStart: true, maxValue: steps.Count);

var tasks = steps.ToDictionary(s => s, s => ctx.AddTask(s.Name, autoStart: false));
tasks[steps[0]].StartTask();
```

**Incremento:** en `OnStepCompleted`, además de la actualización existente por-step, `pipelineTask.Increment(1)`. Requiere pasar `pipelineTask` a `OnStepCompleted` (nuevo parámetro) y al `Progress<StepResult>` lambda que lo invoca.

```csharp
private static void OnStepCompleted(StepResult stepResult, List<DeployStep> steps, Dictionary<DeployStep, ProgressTask> tasks, ProgressTask pipelineTask)
{
    var task = tasks[stepResult.Step];
    task.Value = 100;
    task.Description = DescribeStepStatus(stepResult);

    pipelineTask.Increment(1);

    StartNextTask(stepResult, steps, tasks);
}
```

**Label — sin traducción:** `"Pipeline"` queda hardcodeado, igual que el resto de este archivo (`"Paso"`, `"Estado"`, `"Duración"`, etc.). `PipelineExecutionView.cs` no es uno de los archivos tocados por el Translator del Ciclo 4 — esa traducción es asimétrica y deliberadamente acotada a `MenuManager.cs`. Agregar traducción acá rompería ese límite ya decidido explícitamente por el usuario.

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| Pipeline corta por falla sin `ContinueOnFailure` | La fila "Pipeline" queda en `completados/total < 100%` — no se fuerza a 100%, refleja que no todos los steps corrieron. |
| Pipeline de 1 solo step (caso `RunLocalPipelineAsync`, efímero) | `maxValue: 1` — la fila "Pipeline" salta de 0% a 100% en un solo incremento, igual que la fila del step. Redundante visualmente pero no incorrecto; no se agrega lógica especial para ocultarla con 1 step. |

## Testing

Sin tests — mismo criterio que Ciclo 2/3: la capa `Presentation` basada en Spectre.Console no se testea en este repo (`ProgressTask`/`ProgressContext` no son mockeables sin acoplarse a la librería). Verificación manual por el usuario (`dotnet run`, correr un pipeline con 2+ steps, confirmar visualmente).

## Decisiones registradas

- El % es steps-completados/total, nunca un progreso interpolado dentro de un step — confirmado explícitamente con el usuario antes de escribir este spec.
- Fila "Pipeline" primera (arriba), no última — actúa como resumen por encima del detalle.
- Sin traducción — respeta el límite ya decidido en Ciclo 4 (solo `MenuManager.cs` pasa por `Translator`).
- Columnas de `Progress` son globales a todas las tasks (limitación de Spectre.Console, no del diseño) — se acepta que las filas por-step también muestren barra/porcentaje binario en vez de intentar un layout mixto no soportado por la librería.
