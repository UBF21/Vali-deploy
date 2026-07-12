# Barra de progreso agregada del Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar una fila "Pipeline" al `Progress` de Spectre.Console en `PipelineExecutionView.cs` que muestre steps-completados/total como barra + porcentaje, además de las filas por-step ya existentes.

**Architecture:** Cambio contenido en un único archivo de Presentation (`PipelineExecutionView.cs`), sin tocar Domain ni Application — el total de steps (`steps.Count`) ya está disponible localmente y el runner ya reporta un evento por step completado (`IProgress<StepResult>`), que es el único hook necesario para incrementar la fila agregada.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-10-pipeline-progress-bar-design.md`

---

### Task 1: Fila "Pipeline" en `PipelineExecutionView`

**Files:**
- Modify: `vali-deploy/Presentation/PipelineExecutionView.cs`

Sin test nuevo — mismo criterio que el resto de `Presentation/`: `ProgressTask`/`ProgressContext` de Spectre.Console no son mockeables sin acoplarse a la librería, y no hay tests existentes para este archivo (confirmado: no hay ningún `PipelineExecutionViewTests.cs` en `vali-deploy.Tests/`).

- [ ] **Step 1: Agregar columnas de barra/porcentaje**

En `RunAsync` (línea 13-15), agregar `ProgressBarColumn` y `PercentageColumn` al set de columnas:

```csharp
await AnsiConsole.Progress()
    .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn(), new ElapsedTimeColumn())
    .StartAsync(async ctx => result = await RunPipelineWithProgressAsync(pipelineRunner, steps, context, ctx));
```

- [ ] **Step 2: Agregar la task "Pipeline" antes de las tasks por-step**

En `RunPipelineWithProgressAsync` (línea 21-33), agregar la task agregada primero (para que sea la primera fila renderizada) y pasarla al `Progress<StepResult>`:

```csharp
private static async Task<PipelineResult> RunPipelineWithProgressAsync(
    IPipelineRunner pipelineRunner,
    List<DeployStep> steps,
    StepExecutionContext context,
    ProgressContext ctx)
{
    var pipelineTask = ctx.AddTask("Pipeline", autoStart: true, maxValue: steps.Count);

    var tasks = steps.ToDictionary(s => s, s => ctx.AddTask(s.Name, autoStart: false));
    tasks[steps[0]].StartTask();

    var progress = new Progress<StepResult>(stepResult => OnStepCompleted(stepResult, steps, tasks, pipelineTask));

    return await pipelineRunner.RunAsync(steps, context, progress);
}
```

- [ ] **Step 3: Incrementar la task "Pipeline" en cada step completado**

En `OnStepCompleted` (línea 35-42), agregar el parámetro `pipelineTask` y el incremento:

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

- [ ] **Step 4: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, mismo número de tests que antes del cambio (no se agregan tests nuevos en este task) — confirmar que no bajó el conteo por una regresión de compilación en otro archivo.

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Presentation/PipelineExecutionView.cs
git commit -m "feat(presentation): agregar barra de progreso agregada del pipeline"
```

---

### Task 2: Verificación manual

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Correr el CLI y ejecutar un pipeline con 2+ steps**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Elegir un subproyecto con un pipeline de al menos 2 steps configurados para algún entorno (o el flujo "Generate Microsoft publish" si arma un pipeline local de varios steps) y ejecutarlo.

- [ ] **Step 2: Verificar la fila "Pipeline"**

Confirmar:
- Aparece una primera fila "Pipeline" arriba de las filas por-step, con barra + porcentaje.
- El porcentaje avanza en incrementos de `1/total_steps` cada vez que un step individual completa (✅, ⚠ o ❌), no de forma continua ni interpolada.
- Al terminar todos los steps exitosamente, la fila "Pipeline" llega a 100%.

- [ ] **Step 3: Verificar el caso de corte anticipado**

Si es posible (o describir el comportamiento esperado sin poder reproducirlo): correr un pipeline donde un step falle sin `ContinueOnFailure`. La fila "Pipeline" debería quedar por debajo de 100% (reflejando que no todos los steps llegaron a correr), no saltar a 100% artificialmente.

- [ ] **Step 4: Verificar que las filas por-step no rompieron nada visualmente**

Confirmar que la descripción, spinner y tiempo transcurrido de cada step siguen viéndose igual que antes — la única adición visual esperada es la barra/porcentaje binario (0%→100% al completarse) en cada fila por-step, más la fila "Pipeline" nueva.

Si cualquiera de estos pasos falla, corregir el código en Task 1 y volver a compilar antes de continuar.

---

## Self-review

**Cobertura de la spec:** el único requisito funcional (% = steps completados / total, no interpolado) se cumple por construcción — `pipelineTask.Increment(1)` solo se dispara desde `OnStepCompleted`, que el runner solo invoca post-completado de un step (confirmado en la exploración de `PipelineRunner.cs`). La ubicación "primera fila" y la ausencia de traducción están explícitas en los Steps 1-2 y en la decisión de la spec.

**Consistencia de tipos:** `pipelineTask` es un `ProgressTask` normal (mismo tipo que las tasks del diccionario `tasks`), sin tipos nuevos — no hay superficie de inconsistencia entre Task 1 y su único consumidor (el propio archivo).

**Sin placeholders:** el único step de código tiene el archivo completo relevante, sin TBD.
