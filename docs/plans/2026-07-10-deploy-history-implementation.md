# Historial de deploys consultable — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar un menú "View Deploy History" al CLI que liste los runs de pipeline pasados (proyecto, subproyecto, entorno, éxito/fallo, duración) y permita ver el detalle (log crudo) de uno elegido, sin tener que abrir archivos a mano.

**Architecture:** `PipelineLogger` sigue escribiendo un `.log` de texto plano por run (sin cambios de formato), pero ahora también appendea una línea JSON por run a un índice nuevo `deploy-history.jsonl` en la misma carpeta de logs. Un `DeployHistoryRepository` nuevo lee ese índice (tolerando líneas corruptas) para alimentar una vista `DeployHistoryView` (listado + drill-down) colgada del menú principal de `MenuManager`. El historial arranca desde cero: los `.log` que ya existen en disco no entran al índice.

**Nota de implementación respecto a la spec:** `IDeployHistoryRepository.GetRecent` devuelve `DeployHistoryQueryResult` (no `IReadOnlyList<DeployRunSummary>` pelado) para poder exponer `SkippedCorruptedLines` sin un parámetro `out`. `DeployHistoryView` es una clase estática (`ShowAsync`/`ShowDetailAsync`), siguiendo el mismo patrón que `Presentation/EnvironmentMenu.cs` — no `PipelineExecutionView` (que se instancia porque lo llaman dos call sites distintos con estado por-invocación). Ambos detalles ya están reflejados en la spec actualizada.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, System.Text.Json (BCL), xUnit 2.6.6 + Moq 4.20.70 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-10-deploy-history-design.md`

---

### Task 1: `DeployRunSummary` — value object de dominio

**Files:**
- Create: `vali-deploy/Domain/DeployRunSummary.cs`
- Test: `vali-deploy.Tests/Domain/DeployRunSummaryTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Tests.Domain;

public class DeployRunSummaryTests
{
    [Fact]
    public void Default_RunId_is_generated_and_unique_per_instance()
    {
        var first = new DeployRunSummary();
        var second = new DeployRunSummary();

        Assert.NotEmpty(first.RunId);
        Assert.NotEqual(first.RunId, second.RunId);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DeployRunSummaryTests`
Expected: FAIL (no existe `vali_deploy.Domain.DeployRunSummary`, error de compilación)

- [ ] **Step 3: Crear el value object**

```csharp
namespace vali_deploy.Domain;

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

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DeployRunSummaryTests`
Expected: PASS (1/1)

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Domain/DeployRunSummary.cs vali-deploy.Tests/Domain/DeployRunSummaryTests.cs
git commit -m "feat(domain): agregar DeployRunSummary como value object"
```

---

### Task 2: `Constants.DefaultLogsDirectory()` + reusar en `PipelineLogger`

**Files:**
- Modify: `vali-deploy/Utils/Constants.cs`
- Modify: `vali-deploy/Infrastructure/PipelineLogger.cs:10-14`
- Test: `vali-deploy.Tests/Utils/ConstantsTests.cs`

- [ ] **Step 1: Escribir el test**

```csharp
using vali_deploy.Utils;

namespace vali_deploy.Tests.Utils;

public class ConstantsTests
{
    [Fact]
    public void DefaultLogsDirectory_ends_with_expected_relative_path()
    {
        var path = Constants.DefaultLogsDirectory();

        Assert.EndsWith(Path.Combine("vali-deploy", "logs"), path);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ConstantsTests`
Expected: FAIL (no existe `Constants.DefaultLogsDirectory`, error de compilación)

- [ ] **Step 3: Agregar el método a `Constants.cs`**

En `vali-deploy/Utils/Constants.cs`, agregar dentro de la clase (después de `UrlVersion`):

```csharp
    public static string DefaultLogsDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "vali-deploy", "logs");
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter ConstantsTests`
Expected: PASS (1/1)

- [ ] **Step 5: Reusar el método en `PipelineLogger`**

En `vali-deploy/Infrastructure/PipelineLogger.cs`, reemplazar el constructor (líneas 10-14):

```csharp
    public PipelineLogger(string? logsDirectory = null)
    {
        _logsDirectory = logsDirectory ?? Utils.Constants.DefaultLogsDirectory();
    }
```

- [ ] **Step 6: Correr toda la suite de `PipelineLoggerTests` y verificar que sigue en verde**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineLoggerTests`
Expected: PASS (4/4) — todos los tests existentes pasan un `tempLogsDir` explícito, así que el cambio de default no los afecta.

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Utils/Constants.cs vali-deploy/Infrastructure/PipelineLogger.cs vali-deploy.Tests/Utils/ConstantsTests.cs
git commit -m "refactor(infrastructure): extraer Constants.DefaultLogsDirectory y reusarla en PipelineLogger"
```

---

### Task 3: `IPipelineLogger`/`PipelineLogger` — entorno en `StartRun` + `FinishRun`

**Depends on:** Task 2

**Files:**
- Modify: `vali-deploy/Infrastructure/IPipelineLogger.cs`
- Modify: `vali-deploy/Infrastructure/PipelineLogger.cs`
- Modify: `vali-deploy.Tests/Infrastructure/PipelineLoggerTests.cs`

- [ ] **Step 1: Actualizar los tests existentes a la nueva firma y agregar los tests nuevos**

Reemplazar el contenido completo de `vali-deploy.Tests/Infrastructure/PipelineLoggerTests.cs`:

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
            logger.StartRun("proj", "sub", "Local");

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
            logger.StartRun("shop", "api", "Prod");

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            Assert.StartsWith("shop-api-", Path.GetFileName(logFile));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void WriteStep_throws_when_called_before_StartRun()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            Assert.Throws<InvalidOperationException>(() =>
                logger.WriteStep(new StepResult { Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0 }));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void WriteStep_appends_multiple_steps_without_overwriting_previous_ones()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("proj", "sub", "Local");

            logger.WriteStep(new StepResult { Step = new DeployStep { Name = "clean" }, Success = true, ExitCode = 0 });
            logger.WriteStep(new StepResult { Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0 });

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            var content = File.ReadAllText(logFile);

            Assert.Contains("clean", content);
            Assert.Contains("build", content);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_throws_when_called_before_StartRun()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            Assert.Throws<InvalidOperationException>(() => logger.FinishRun(new PipelineResult { Success = true }));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_appends_footer_to_log_and_a_json_line_to_the_history_index()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);
            logger.StartRun("shop", "api", "Prod");

            var stepResult = new StepResult
            {
                Step = new DeployStep { Name = "build" }, Success = true, ExitCode = 0, Duration = TimeSpan.FromSeconds(5)
            };
            logger.WriteStep(stepResult);

            var pipelineResult = new PipelineResult { Success = true, Steps = new List<StepResult> { stepResult } };
            logger.FinishRun(pipelineResult);

            var logFile = Directory.GetFiles(tempLogsDir).Single(f => f.EndsWith(".log"));
            Assert.Contains("Run finalizado", File.ReadAllText(logFile));

            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            var line = File.ReadAllLines(indexFile).Single();
            var summary = System.Text.Json.JsonSerializer.Deserialize<DeployRunSummary>(line)!;

            Assert.Equal("shop", summary.ProjectName);
            Assert.Equal("api", summary.SubProjectName);
            Assert.Equal("Prod", summary.EnvironmentName);
            Assert.True(summary.Success);
            Assert.Equal(TimeSpan.FromSeconds(5), summary.TotalDuration);
            Assert.Equal(logFile, summary.LogFilePath);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void FinishRun_on_two_consecutive_runs_appends_two_lines_to_the_same_index_file()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var logger = new PipelineLogger(tempLogsDir);

            logger.StartRun("proj", "sub", "Local");
            logger.FinishRun(new PipelineResult { Success = true, Steps = new List<StepResult>() });

            logger.StartRun("proj", "sub", "Local");
            logger.FinishRun(new PipelineResult { Success = false, Steps = new List<StepResult>() });

            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            var lines = File.ReadAllLines(indexFile);

            Assert.Equal(2, lines.Length);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineLoggerTests`
Expected: FAIL con error de compilación (`StartRun` no acepta 3 argumentos, `FinishRun` no existe)

- [ ] **Step 3: Actualizar `IPipelineLogger.cs`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IPipelineLogger
{
    void StartRun(string projectName, string subProjectName, string environmentName);
    void WriteStep(StepResult stepResult);
    void FinishRun(PipelineResult result);
}
```

- [ ] **Step 4: Actualizar `PipelineLogger.cs`**

```csharp
using System.Text.Json;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class PipelineLogger : IPipelineLogger
{
    private readonly string _logsDirectory;
    private string? _currentLogFilePath;
    private string? _currentProjectName;
    private string? _currentSubProjectName;
    private string? _currentEnvironmentName;
    private DateTime _currentStartedAtUtc;

    public PipelineLogger(string? logsDirectory = null)
    {
        _logsDirectory = logsDirectory ?? Utils.Constants.DefaultLogsDirectory();
    }

    public void StartRun(string projectName, string subProjectName, string environmentName)
    {
        Directory.CreateDirectory(_logsDirectory);

        _currentProjectName = projectName;
        _currentSubProjectName = subProjectName;
        _currentEnvironmentName = environmentName;
        _currentStartedAtUtc = DateTime.UtcNow;

        var timestamp = _currentStartedAtUtc.ToString("yyyyMMdd-HHmmss");
        _currentLogFilePath = Path.Combine(_logsDirectory, $"{projectName}-{subProjectName}-{timestamp}.log");
        File.WriteAllText(_currentLogFilePath, $"=== Pipeline run: {projectName}/{subProjectName} ({environmentName}) — {_currentStartedAtUtc:O} ===\n");
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

    public void FinishRun(PipelineResult result)
    {
        if (_currentLogFilePath == null)
        {
            throw new InvalidOperationException("StartRun debe llamarse antes de FinishRun.");
        }

        File.AppendAllText(_currentLogFilePath, $"=== Run finalizado — Success: {result.Success} — {DateTime.UtcNow:O} ===\n");

        var summary = new DeployRunSummary
        {
            ProjectName = _currentProjectName!,
            SubProjectName = _currentSubProjectName!,
            EnvironmentName = _currentEnvironmentName!,
            StartedAtUtc = _currentStartedAtUtc,
            Success = result.Success,
            TotalDuration = result.Steps.Aggregate(TimeSpan.Zero, (total, step) => total + step.Duration),
            LogFilePath = _currentLogFilePath
        };

        var indexFilePath = Path.Combine(_logsDirectory, "deploy-history.jsonl");
        File.AppendAllText(indexFilePath, JsonSerializer.Serialize(summary) + "\n");
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter PipelineLoggerTests`
Expected: PASS (7/7)

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Infrastructure/IPipelineLogger.cs vali-deploy/Infrastructure/PipelineLogger.cs vali-deploy.Tests/Infrastructure/PipelineLoggerTests.cs
git commit -m "feat(infrastructure): PipelineLogger registra entorno y cierra runs con FinishRun (indice deploy-history.jsonl)"
```

---

### Task 4: `IDeployHistoryRepository` / `DeployHistoryRepository`

**Depends on:** Task 3

**Files:**
- Create: `vali-deploy/Domain/DeployHistoryQueryResult.cs`
- Create: `vali-deploy/Infrastructure/IDeployHistoryRepository.cs`
- Create: `vali-deploy/Infrastructure/DeployHistoryRepository.cs`
- Test: `vali-deploy.Tests/Infrastructure/DeployHistoryRepositoryTests.cs`

- [ ] **Step 1: Escribir los tests**

```csharp
using System.Text.Json;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Tests.Infrastructure;

public class DeployHistoryRepositoryTests
{
    private static string WriteIndex(string logsDir, params DeployRunSummary[] summaries)
    {
        var indexFile = Path.Combine(logsDir, "deploy-history.jsonl");
        File.WriteAllLines(indexFile, summaries.Select(s => JsonSerializer.Serialize(s)));
        return indexFile;
    }

    [Fact]
    public void GetRecent_on_missing_index_file_returns_empty_result()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var repository = new DeployHistoryRepository(tempLogsDir);

            var result = repository.GetRecent(30);

            Assert.Empty(result.Runs);
            Assert.Equal(0, result.SkippedCorruptedLines);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_orders_runs_by_StartedAtUtc_descending()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var older = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) };
            var newer = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc) };
            WriteIndex(tempLogsDir, older, newer);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30);

            Assert.Equal(new[] { newer.RunId, older.RunId }, result.Runs.Select(r => r.RunId));
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_filters_by_project_name()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var shop = new DeployRunSummary { ProjectName = "shop", SubProjectName = "api", StartedAtUtc = DateTime.UtcNow };
            var billing = new DeployRunSummary { ProjectName = "billing", SubProjectName = "worker", StartedAtUtc = DateTime.UtcNow };
            WriteIndex(tempLogsDir, shop, billing);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30, projectFilter: "shop");

            Assert.Single(result.Runs);
            Assert.Equal("shop", result.Runs[0].ProjectName);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_respects_the_count_limit()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var summaries = Enumerable.Range(0, 5)
                .Select(i => new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = DateTime.UtcNow.AddMinutes(i) })
                .ToArray();
            WriteIndex(tempLogsDir, summaries);

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(2);

            Assert.Equal(2, result.Runs.Count);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }

    [Fact]
    public void GetRecent_skips_corrupted_lines_without_losing_valid_ones()
    {
        var tempLogsDir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var valid = new DeployRunSummary { ProjectName = "p", SubProjectName = "s", StartedAtUtc = DateTime.UtcNow };
            var indexFile = Path.Combine(tempLogsDir, "deploy-history.jsonl");
            File.WriteAllLines(indexFile, new[] { "{ esto no es json valido", JsonSerializer.Serialize(valid) });

            var repository = new DeployHistoryRepository(tempLogsDir);
            var result = repository.GetRecent(30);

            Assert.Single(result.Runs);
            Assert.Equal(1, result.SkippedCorruptedLines);
        }
        finally
        {
            Directory.Delete(tempLogsDir, recursive: true);
        }
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DeployHistoryRepositoryTests`
Expected: FAIL (no existen `DeployHistoryRepository` ni `DeployHistoryQueryResult`, error de compilación)

- [ ] **Step 3: Crear `DeployHistoryQueryResult`**

```csharp
namespace vali_deploy.Domain;

public class DeployHistoryQueryResult
{
    public IReadOnlyList<DeployRunSummary> Runs { get; set; } = new List<DeployRunSummary>();
    public int SkippedCorruptedLines { get; set; }
}
```

- [ ] **Step 4: Crear `IDeployHistoryRepository`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public interface IDeployHistoryRepository
{
    DeployHistoryQueryResult GetRecent(int count, string? projectFilter = null);
}
```

- [ ] **Step 5: Crear `DeployHistoryRepository`**

```csharp
using System.Text.Json;
using vali_deploy.Domain;

namespace vali_deploy.Infrastructure;

public class DeployHistoryRepository : IDeployHistoryRepository
{
    private readonly string _indexFilePath;

    public DeployHistoryRepository(string? logsDirectory = null)
    {
        var directory = logsDirectory ?? Utils.Constants.DefaultLogsDirectory();
        _indexFilePath = Path.Combine(directory, "deploy-history.jsonl");
    }

    public DeployHistoryQueryResult GetRecent(int count, string? projectFilter = null)
    {
        if (!File.Exists(_indexFilePath))
        {
            return new DeployHistoryQueryResult();
        }

        var runs = new List<DeployRunSummary>();
        var skipped = 0;

        foreach (var line in File.ReadAllLines(_indexFilePath))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var summary = JsonSerializer.Deserialize<DeployRunSummary>(line);
                if (summary == null)
                {
                    skipped++;
                    continue;
                }

                runs.Add(summary);
            }
            catch (JsonException)
            {
                skipped++;
            }
        }

        var filtered = projectFilter == null
            ? runs
            : runs.Where(r => r.ProjectName == projectFilter).ToList();

        var ordered = filtered.OrderByDescending(r => r.StartedAtUtc).Take(count).ToList();

        return new DeployHistoryQueryResult { Runs = ordered, SkippedCorruptedLines = skipped };
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter DeployHistoryRepositoryTests`
Expected: PASS (5/5)

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Domain/DeployHistoryQueryResult.cs vali-deploy/Infrastructure/IDeployHistoryRepository.cs vali-deploy/Infrastructure/DeployHistoryRepository.cs vali-deploy.Tests/Infrastructure/DeployHistoryRepositoryTests.cs
git commit -m "feat(infrastructure): agregar DeployHistoryRepository para leer el indice de runs"
```

---

### Task 5: `DeployHistoryView` + wiring en `MenuManager`/`CompositionRoot`

**Depends on:** Task 4

**Files:**
- Create: `vali-deploy/Presentation/DeployHistoryView.cs`
- Modify: `vali-deploy/CompositionRoot.cs:47`
- Modify: `vali-deploy/Managers/MenuManager.cs:71-73` (switch), `:111-113` (choices), `:887` (`RunLocalPipelineAsync`), `:945` (`ExecuteSubProjectPipelineAsync`)

No hay test nuevo en este task — es consistente con que la capa Presentation/Manager basada en `SelectionPrompt`/`AnsiConsole` de Spectre.Console no tiene tests en el repo hoy (`PipelineExecutionView`, `EnvironmentMenu` tampoco los tienen).

- [ ] **Step 1: Agregar el factory a `CompositionRoot.cs`**

En `vali-deploy/CompositionRoot.cs`, después de la línea `public static IPipelineLogger CreatePipelineLogger() => new PipelineLogger();`:

```csharp
    public static IDeployHistoryRepository CreateDeployHistoryRepository() => new DeployHistoryRepository();
```

- [ ] **Step 2: Crear `Presentation/DeployHistoryView.cs`**

```csharp
using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Infrastructure;

namespace vali_deploy.Presentation;

public static class DeployHistoryView
{
    private const int MaxRunsShown = 30;
    private const string AllProjectsOption = "[grey]Todos los proyectos[/]";
    private const string BackOption = "[seagreen1]Back to Main Menu[/]";

    public static Task ShowAsync(IDeployHistoryRepository repository, IReadOnlyList<string> projectNames)
    {
        AnsiConsole.Clear();
        ShellRenderer.DrawHeader(new Dictionary<string, Project>(), breadcrumb: "Deploy History");

        var projectFilter = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Filtrar por proyecto:")
                .AddChoices(new[] { AllProjectsOption }.Concat(projectNames)));

        var filter = projectFilter == AllProjectsOption ? null : projectFilter;
        var result = repository.GetRecent(MaxRunsShown, filter);

        if (result.Runs.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No hay runs registrados todavía.[/]");
            PauseForUserInput();
            return Task.CompletedTask;
        }

        RenderTable(result.Runs);

        if (result.SkippedCorruptedLines > 0)
        {
            AnsiConsole.MarkupLine($"[yellow]{result.SkippedCorruptedLines} línea(s) ilegible(s) del índice fueron omitidas.[/]");
        }

        var choices = result.Runs
            .Select(DescribeRun)
            .Append(BackOption)
            .ToList();

        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Elegí un run para ver el detalle:")
                .AddChoices(choices));

        if (selected == BackOption)
        {
            return Task.CompletedTask;
        }

        var entry = result.Runs[choices.IndexOf(selected)];
        ShowDetail(entry);
        return Task.CompletedTask;
    }

    private static void RenderTable(IReadOnlyList<DeployRunSummary> runs)
    {
        var table = new Table().AddColumns("Fecha", "Proyecto", "Subproyecto", "Entorno", "Estado", "Duración");

        foreach (var run in runs)
        {
            table.AddRow(
                run.StartedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                run.ProjectName,
                run.SubProjectName,
                run.EnvironmentName,
                run.Success ? "[green]OK[/]" : "[red]FALLÓ[/]",
                run.TotalDuration.ToString(@"mm\:ss"));
        }

        AnsiConsole.Write(table);
    }

    private static string DescribeRun(DeployRunSummary run) =>
        $"{run.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm} · {run.ProjectName}/{run.SubProjectName} · {run.EnvironmentName}";

    private static void ShowDetail(DeployRunSummary entry)
    {
        AnsiConsole.Clear();

        if (!File.Exists(entry.LogFilePath))
        {
            AnsiConsole.Write(new Panel("[red]El archivo de log de este run ya no existe en disco.[/]")
                .Header($"{entry.ProjectName}/{entry.SubProjectName} · {entry.EnvironmentName}"));
            PauseForUserInput();
            return;
        }

        var content = File.ReadAllText(entry.LogFilePath);
        AnsiConsole.Write(new Panel(content.EscapeMarkup())
            .Header($"{entry.ProjectName}/{entry.SubProjectName} · {entry.EnvironmentName} · {entry.StartedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}"));

        PauseForUserInput();
    }

    private static void PauseForUserInput()
    {
        AnsiConsole.MarkupLine("[grey]Presioná una tecla para continuar...[/]");
        Console.ReadKey(true);
    }
}
```

- [ ] **Step 3: Agregar la opción al menú principal**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar `GetMainMenuOption()` (líneas 106-115):

```csharp
    private static string GetMainMenuOption()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Project", "Remove Project", "Show Projects", "Configure Publish File Omissions",
                    "Remove Subprojects", "Manage Docker Projects", "Manage Publish Arguments", "Manage Environments",
                    "View Deploy History", "[seagreen1]Exit[/]")
        );
    }
```

- [ ] **Step 4: Agregar el `case` en el switch de `StartAsync()`**

En `vali-deploy/Managers/MenuManager.cs`, entre el `case "Manage Environments":` y el `case "[seagreen1]Exit[/]":` (líneas 71-74):

```csharp
                case "Manage Environments":
                    await Presentation.EnvironmentMenu.StartAsync(CompositionRoot.CreateProjectRepository());
                    break;
                case "View Deploy History":
                    await Presentation.DeployHistoryView.ShowAsync(CompositionRoot.CreateDeployHistoryRepository(), _projects.Keys.ToList());
                    break;
                case "[seagreen1]Exit[/]":
```

- [ ] **Step 5: Pasar el entorno y cerrar el run en `RunLocalPipelineAsync`**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar las líneas 885-897:

```csharp
        var pipelineRunner = CompositionRoot.CreatePipelineRunner();
        var logger = CompositionRoot.CreatePipelineLogger();
        logger.StartRun(projectName, subProject.Name, LocalEnvironment.Name);

        var view = new Presentation.PipelineExecutionView();
        var result = await view.RunAsync(pipelineRunner, steps, context);

        foreach (var stepResult in result.Steps)
        {
            logger.WriteStep(stepResult);
        }

        logger.FinishRun(result);

        PauseForUserInput(result.Success ? "Ejecución completada con éxito." : "La ejecución falló, revisá el detalle arriba.");
```

- [ ] **Step 6: Pasar el entorno y cerrar el run en `ExecuteSubProjectPipelineAsync`**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar las líneas 943-955:

```csharp
        var pipelineRunner = CompositionRoot.CreatePipelineRunner();
        var logger = CompositionRoot.CreatePipelineLogger();
        logger.StartRun(projectName, subProject.Name, environmentName);

        var view = new Presentation.PipelineExecutionView();
        var result = await view.RunAsync(pipelineRunner, steps, context);

        foreach (var stepResult in result.Steps)
        {
            logger.WriteStep(stepResult);
        }

        logger.FinishRun(result);

        PauseForUserInput(result.Success ? "Pipeline completado con éxito." : "Pipeline falló, revisá el detalle arriba.");
```

- [ ] **Step 7: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS (todos los tests existentes + los agregados en Tasks 1-4).

- [ ] **Step 8: Commit**

```bash
git add vali-deploy/CompositionRoot.cs vali-deploy/Presentation/DeployHistoryView.cs vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): agregar menu View Deploy History con listado y detalle de runs"
```

---

### Task 6: Verificación manual

**Files:** ninguno (solo verificación, no genera commit propio salvo que aparezca un bug a corregir)

- [ ] **Step 1: Correr el CLI**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

- [ ] **Step 2: Generar al menos 2 runs**

Ejecutar un pipeline local (Docker Build o "Generate Microsoft publish") sobre algún subproyecto configurado, y —si hay algún entorno remoto configurado— un pipeline vía "Execute Pipeline" contra un entorno. Si no hay proyectos configurados en esta máquina, crear uno de prueba con un solo step `LocalCommand` (`echo hola`) primero.

- [ ] **Step 3: Verificar el listado**

Desde el menú principal, elegir "View Deploy History" → "Todos los proyectos". Confirmar que aparecen los runs generados en el Step 2, con fecha, entorno y estado correctos, más recientes primero.

- [ ] **Step 4: Verificar el filtro por proyecto**

Repetir "View Deploy History" eligiendo un proyecto puntual. Confirmar que solo aparecen los runs de ese proyecto.

- [ ] **Step 5: Verificar el drill-down**

Elegir un run de la lista. Confirmar que se muestra el contenido completo del `.log` correspondiente (mismo texto que abrir el archivo en `~/Documents/vali-deploy/logs/` a mano).

- [ ] **Step 6: Verificar el caso sin runs**

Con un proyecto que nunca corrió un pipeline, filtrar el historial por ese proyecto y confirmar el mensaje "No hay runs registrados todavía." (sin excepción, sin crash).

Si cualquiera de estos pasos falla, corregir el código correspondiente en el task de origen y volver a correr `dotnet test` antes de continuar.
