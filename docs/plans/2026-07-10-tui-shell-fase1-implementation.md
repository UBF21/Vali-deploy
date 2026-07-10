# TUI Shell Persistente Fase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reemplazar el header ad-hoc de `MenuManager` (Rule+FigletText+BarChart, redibujado sin consistencia) por un shell visual coherente — `ShellRenderer` (header compacto reutilizable) + `SplashScreen` (pantalla de arranque) + paleta Forest centralizada — usado tanto en el menú raíz como en los submenús existentes (`EnvironmentMenu`, `PipelineEditorMenu`).

**Architecture:** Persistencia semántica (no `Live`+`Layout`): cada pantalla sigue haciendo `AnsiConsole.Clear()` + redibujado completo, pero delega el header a `ShellRenderer.DrawHeader()` en vez de tener lógica de dibujo duplicada. Todos los renderables usan anchos relativos (sin `Width` fijo) para adaptarse al tamaño de terminal vigente en cada redibujado.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1 (ya referenciado, sin paquetes nuevos).

**Spec:** `docs/superpowers/specs/2026-07-10-tui-shell-fase1-design.md`

**Sin tests automatizados en este plan** — el proyecto tiene un suite xUnit (`vali-deploy.Tests/`) pero, por convención ya establecida ahí (ver spec previo `docs/superpowers/specs/2026-07-08-ssh-deploy-pipeline-design.md`, sección Testing: *"Foco de cobertura en Application/, no en Presentation/ (menús Spectre)"*), las pantallas de `Presentation/`/`Managers/` no se cubren con tests unitarios. Cada tarea se verifica con `dotnet build`; la Tarea 8 es verificación manual end-to-end.

---

### Task 1: `ShellPalette` — paleta Forest centralizada

**Files:**
- Create: `vali-deploy/Presentation/ShellPalette.cs`

- [ ] **Step 1: Crear la paleta**

```csharp
using Spectre.Console;

namespace vali_deploy.Presentation;

public static class ShellPalette
{
    public static readonly Color Brand = Color.SeaGreen1;
    public static readonly Color Muted = Color.Grey62;
    public static readonly Color Warning = Color.DarkOrange3;
    public static readonly Color Error = Color.IndianRed1;

    public const string BrandTag = "seagreen1";
    public const string MutedTag = "grey62";
    public const string WarningTag = "darkorange3";
    public const string ErrorTag = "indianred1";
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/ShellPalette.cs
git commit -m "feat(presentation): agregar ShellPalette (paleta Forest centralizada)"
```

---

### Task 2: `ShellRenderer` — header compartido

**Depends on:** Task 1

**Files:**
- Create: `vali-deploy/Presentation/ShellRenderer.cs`

- [ ] **Step 1: Crear el renderer**

```csharp
using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Utils;

namespace vali_deploy.Presentation;

/// <summary>
/// Dibuja la franja de header compartida por el menú raíz y los submenús (EnvironmentMenu,
/// PipelineEditorMenu): marca + versión a la izquierda, resumen global o breadcrumb a la derecha.
/// No hace AnsiConsole.Clear() — eso queda a cargo del caller, para no acoplar el renderer a cuándo
/// debe limpiarse la pantalla. No fija anchos: Grid/Rule se re-miden contra el ancho de consola
/// vigente en cada llamada.
/// </summary>
public static class ShellRenderer
{
    public static void DrawHeader(IReadOnlyDictionary<string, Project> projects, string? breadcrumb = null)
    {
        var currentVersion = Util.GetCurrentVersion();
        var subProjectCount = projects.Values.Sum(p => p.SubProjects.Count);

        var status = breadcrumb is null
            ? $"{projects.Count} proyectos · {subProjectCount} subproyectos"
            : Markup.Escape(breadcrumb);

        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().RightAligned())
            .AddRow(
                new Markup($"[bold {ShellPalette.BrandTag}]Vali-Deploy[/] [{ShellPalette.MutedTag}]v{currentVersion}[/]"),
                new Markup($"[{ShellPalette.MutedTag}]{status}[/]"));

        AnsiConsole.Write(grid);
        AnsiConsole.Write(new Rule().RuleStyle(new Style(foreground: ShellPalette.Muted)));
        AnsiConsole.WriteLine();
    }
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/ShellRenderer.cs
git commit -m "feat(presentation): agregar ShellRenderer (header persistente compartido)"
```

---

### Task 3: `SplashScreen` — pantalla de arranque

**Depends on:** Task 1 (puede correr en paralelo con Task 2 — no depende de `ShellRenderer`)

**Files:**
- Create: `vali-deploy/Presentation/SplashScreen.cs`

- [ ] **Step 1: Crear la splash screen**

```csharp
using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Utils;

namespace vali_deploy.Presentation;

/// <summary>
/// Pantalla de arranque: se muestra una vez, antes de entrar al shell (MenuManager.StartAsync).
/// FigletText grande centrado (a diferencia de ShellRenderer.DrawHeader, que usa texto simple y
/// se repite en cada pantalla — el Figlet queda reservado exclusivamente para acá). Sin anchos
/// fijos: Table/Align se centran y re-miden contra el ancho de consola vigente.
/// </summary>
public static class SplashScreen
{
    public static void ShowAndWait(DeployConfig config)
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(Align.Center(new FigletText("Vali-Deploy").Color(ShellPalette.Brand)));
        AnsiConsole.Write(Align.Center(new Markup($"[{ShellPalette.MutedTag}]v{Util.GetCurrentVersion()}[/]")));
        AnsiConsole.WriteLine();

        var subProjectCount = config.Projects.Values.Sum(p => p.SubProjects.Count);
        var summary = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ShellPalette.Muted)
            .HideHeaders()
            .AddColumn("clave")
            .AddColumn("valor");
        summary.AddRow($"[{ShellPalette.MutedTag}]Proyectos[/]", $"{config.Projects.Count}");
        summary.AddRow($"[{ShellPalette.MutedTag}]Subproyectos[/]", $"{subProjectCount}");

        AnsiConsole.Write(Align.Center(summary));
        AnsiConsole.WriteLine();
        AnsiConsole.Write(Align.Center(new Markup($"[{ShellPalette.MutedTag}]Presione una tecla para continuar…[/]")));
        Console.ReadKey(true);
    }
}
```

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/SplashScreen.cs
git commit -m "feat(presentation): agregar SplashScreen (pantalla de arranque)"
```

---

### Task 4: Migrar `MenuManager.cs` a `ShellRenderer`

**Depends on:** Task 2

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`
- Delete: `vali-deploy/Managers/ChartManager.cs`
- Modify: `vali-deploy/Utils/Util.cs`

`ShellRenderer.DrawHeader` reemplaza el `Grid` con `FigletText` + `BarChart` que hoy arma `DisplayMainMenu()`. Como consecuencia directa, `_barChart`/`ChartManager.CreateBarChart` quedan sin ningún caller — se eliminan en esta misma tarea en vez de dejarlos huérfanos (verificado: `ChartManager`/`BarChart` no se referencian desde ningún otro archivo del proyecto).

- [ ] **Step 1: Quitar el campo `_barChart`**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar:

```csharp
    private static Dictionary<string, Project> _projects = new();
    private static BarChart _barChart = new();
    private static readonly Infrastructure.IProjectRepository _repository = CompositionRoot.CreateProjectRepository();
```

por:

```csharp
    private static Dictionary<string, Project> _projects = new();
    private static readonly Infrastructure.IProjectRepository _repository = CompositionRoot.CreateProjectRepository();
```

- [ ] **Step 2: Quitar el cómputo de `_barChart` en `StartAsync`**

Reemplazar:

```csharp
    public static async Task StartAsync()
    {
        _projects = _repository.Load().Projects;
        _barChart = ChartManager.CreateBarChart(_projects);

        bool running = true;
```

por:

```csharp
    public static async Task StartAsync()
    {
        _projects = _repository.Load().Projects;

        bool running = true;
```

- [ ] **Step 3: Reemplazar `DisplayMainMenu()` por una llamada a `ShellRenderer`, y agregar el overload con breadcrumb**

Reemplazar:

```csharp
    /// <summary>
    /// Displays the main menu header, including the application title, version, and project statistics bar chart.
    /// </summary>
    private static void DisplayMainMenu()
    {
        AnsiConsole.Clear();
        var currentVersion = Util.GetCurrentVersion();

        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule("[red] Developed by [yellow]Felipe Rafael M.M[/] [/]"));
        AnsiConsole.Write(new Rule());
        AnsiConsole.Write(new Rule($"[bold grey] Version: {currentVersion}[/]").RightJustified());
        AnsiConsole.Write(new Rule());
        AnsiConsole.WriteLine();

        var gridHeader = new Grid()
            .AddColumn(new GridColumn().RightAligned())
            .AddColumn(new GridColumn().LeftAligned())
            .AddRow(new FigletText("Vali-Deploy").LeftJustified().Color(Color.Yellow), _barChart);

        AnsiConsole.Write(gridHeader);
        AnsiConsole.WriteLine();
    }
```

por:

```csharp
    /// <summary>
    /// Displays the shell header (branding, version, global summary) via <see cref="Presentation.ShellRenderer"/>.
    /// </summary>
    private static void DisplayMainMenu()
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects);
    }

    /// <summary>
    /// Same as <see cref="DisplayMainMenu()"/>, but shows <paramref name="breadcrumb"/> (e.g. "proyecto · subproyecto")
    /// instead of the global project/subproject summary — used by screens scoped to one project/subproject.
    /// </summary>
    private static void DisplayMainMenu(string breadcrumb)
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb);
    }
```

- [ ] **Step 4: Quitar el cómputo de `_barChart` en `UpdateProjectsAndChart`**

Reemplazar:

```csharp
    private static void UpdateProjectsAndChart()
    {
        _projects = _repository.Load().Projects;
        _barChart = ChartManager.CreateBarChart(_projects);
    }
```

por:

```csharp
    private static void UpdateProjectsAndChart()
    {
        _projects = _repository.Load().Projects;
    }
```

- [ ] **Step 5: Pasar breadcrumb en las 3 pantallas que ya tienen `projectName`/`subProject` en scope**

En `DisplayOmitFilesFromPublish`, reemplazar (el bloque completo incluye el doc-comment del método siguiente para asegurar un match único):

```csharp
        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing publish file omissions.
```

por:

```csharp
        DisplayMainMenu($"{projectName} · {subProject.Name}");
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing publish file omissions.
```

En `DisplayDockerArgs`, reemplazar:

```csharp
        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing Docker arguments.
```

por:

```csharp
        DisplayMainMenu($"{projectName} · {subProject.Name}");
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing Docker arguments.
```

En `DisplayPublishArgs`, reemplazar:

```csharp
        DisplayMainMenu();
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing publish arguments.
```

por:

```csharp
        DisplayMainMenu($"{projectName} · {subProject.Name}");
        AnsiConsole.Write(panel);
        AnsiConsole.WriteLine();
    }

    /// <summary>
    /// Prompts the user to select an action for managing publish arguments.
```

- [ ] **Step 6: Recolorear el acento de la paleta (chartreuse → Forest)**

Este archivo usa el tag de markup `chartreuse3_1` (verde lima brillante, ya hardcodeado como texto literal, no como constante) en 50 lugares — como opciones de menú ("Exit", "Back to...", "Cancel") tanto al definirlas en `AddChoices` como al compararlas en el `switch`/`if`. Como es el mismo literal en ambos lados de cada par, un reemplazo de texto global preserva la correspondencia automáticamente.

Usar el tool de edición con reemplazo global sobre todo el archivo:
- old_string: `chartreuse3_1`
- new_string: `seagreen1` (mismo valor que `ShellPalette.BrandTag`)
- replace_all: true

Verificar que no queda ninguna ocurrencia:

Run: `grep -c chartreuse3_1 vali-deploy/Managers/MenuManager.cs`
Expected: `0`

- [ ] **Step 7: Quitar el `using` de Utils que queda sin uso**

`Util.GetCurrentVersion()` era el único uso de `vali_deploy.Utils` en este archivo (ahora vive dentro de `ShellRenderer`). Reemplazar:

```csharp
using Spectre.Console;
using vali_deploy.Domain;
using vali_deploy.Utils;

namespace vali_deploy.Managers;
```

por:

```csharp
using Spectre.Console;
using vali_deploy.Domain;

namespace vali_deploy.Managers;
```

- [ ] **Step 8: Borrar `ChartManager.cs` (sin callers tras este cambio)**

```bash
rm vali-deploy/Managers/ChartManager.cs
```

- [ ] **Step 9: Borrar `Util.GetRandomColor()` (sin callers tras borrar `ChartManager`)**

En `vali-deploy/Utils/Util.cs`, reemplazar (borra el método completo, incluida la lista de colores):

```csharp
namespace vali_deploy.Utils;

public static class Util
{
    public static Spectre.Console.Color GetRandomColor()
    {
        var colors = new List<Spectre.Console.Color>
        {
            Spectre.Console.Color.Red,
            Spectre.Console.Color.Green,
            Spectre.Console.Color.Blue,
            Spectre.Console.Color.Yellow,
            Spectre.Console.Color.Purple,
            Spectre.Console.Color.Orange1,
            Spectre.Console.Color.Aquamarine1,
            Spectre.Console.Color.Aquamarine3,
            Spectre.Console.Color.Aquamarine1_1,
            Spectre.Console.Color.Blue3,
            Spectre.Console.Color.Blue3_1,
            Spectre.Console.Color.Chartreuse1,
            Spectre.Console.Color.Chartreuse2,
            Spectre.Console.Color.Chartreuse3,
            Spectre.Console.Color.Grey0,
            Spectre.Console.Color.Grey3,
            Spectre.Console.Color.Grey7,
            Spectre.Console.Color.Grey11,
            Spectre.Console.Color.Grey15,
            Spectre.Console.Color.Grey19,
            Spectre.Console.Color.Grey100,
            Spectre.Console.Color.Gold1,
            Spectre.Console.Color.Gold3,
            Spectre.Console.Color.Gold3_1,
            Spectre.Console.Color.Fuchsia,
            Spectre.Console.Color.Honeydew2,
            Spectre.Console.Color.Khaki1,
            Spectre.Console.Color.Khaki3,
            Spectre.Console.Color.HotPink,
            Spectre.Console.Color.HotPink_1,
            Spectre.Console.Color.Navy,
            Spectre.Console.Color.Magenta1,
            Spectre.Console.Color.DarkMagenta_1,
            Spectre.Console.Color.Olive,
            Spectre.Console.Color.Tan,
            Spectre.Console.Color.Plum1,
            Spectre.Console.Color.Plum2,
            Spectre.Console.Color.Plum3
        };

        var random = new Random();
        return colors[random.Next(colors.Count)];
    }

    public static string GetOsIdentifier()
```

por:

```csharp
namespace vali_deploy.Utils;

public static class Util
{
    public static string GetOsIdentifier()
```

- [ ] **Step 10: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.` (sin warnings de símbolos no encontrados — confirma que no quedó ningún caller de `ChartManager`/`_barChart`/`GetRandomColor`)

- [ ] **Step 11: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs vali-deploy/Utils/Util.cs
git rm vali-deploy/Managers/ChartManager.cs
git commit -m "refactor(presentation): migrar MenuManager al header de ShellRenderer, quitar BarChart"
```

---

### Task 5: Migrar `EnvironmentMenu.cs` a `ShellRenderer`

**Depends on:** Task 2 (puede correr en paralelo con Task 4 y Task 6 — archivo distinto)

**Files:**
- Modify: `vali-deploy/Presentation/EnvironmentMenu.cs`

- [ ] **Step 1: Recolorear el acento (chartreuse → Forest)**

Este archivo tiene 2 ocurrencias del tag `chartreuse3_1` (definición en `AddChoices` y comparación en el `if`). Usar el tool de edición con reemplazo global:
- old_string: `chartreuse3_1`
- new_string: `seagreen1`
- replace_all: true

- [ ] **Step 2: Agregar el header persistente al loop principal**

Reemplazar:

```csharp
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
                        .Append("[seagreen1]Back to Main Menu[/]")));
```

por:

```csharp
    public static async Task StartAsync(IProjectRepository repository)
    {
        while (true)
        {
            var config = repository.Load();
            AnsiConsole.Clear();
            ShellRenderer.DrawHeader(config.Projects, breadcrumb: "Entornos");

            var option = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("[bold]Manage Environments[/]")
                    .AddChoices(config.Environments.Select(e => e.Name)
                        .Append("[green]Add Environment[/]")
                        .Append("[seagreen1]Back to Main Menu[/]")));
```

(`EnvironmentMenu` ya está en el namespace `vali_deploy.Presentation`, igual que `ShellRenderer` — no hace falta calificarlo ni agregar un `using` nuevo.)

- [ ] **Step 3: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Presentation/EnvironmentMenu.cs
git commit -m "refactor(presentation): agregar header persistente a EnvironmentMenu"
```

---

### Task 6: Migrar `PipelineEditorMenu.cs` a `ShellRenderer`

**Depends on:** Task 2 (puede correr en paralelo con Task 4 y Task 5 — archivo distinto)

**Files:**
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

- [ ] **Step 1: Agregar el header persistente al loop de edición de steps**

Reemplazar:

```csharp
    private static async Task EditStepsAsync(IProjectRepository repository, Domain.DeployConfig config, SubProject subProject, string environmentName)
    {
        while (true)
        {
            var steps = subProject.PipelinesByEnvironment[environmentName];
            AnsiConsole.Clear();
            var table = new Table().AddColumns("#", "Step");
```

por:

```csharp
    private static async Task EditStepsAsync(IProjectRepository repository, Domain.DeployConfig config, SubProject subProject, string environmentName)
    {
        while (true)
        {
            var steps = subProject.PipelinesByEnvironment[environmentName];
            AnsiConsole.Clear();
            ShellRenderer.DrawHeader(config.Projects, breadcrumb: $"{subProject.Name} · {environmentName}");

            var table = new Table().AddColumns("#", "Step");
```

(`PipelineEditorMenu` ya está en el namespace `vali_deploy.Presentation` — no hace falta calificarlo ni agregar un `using` nuevo. Este archivo no usa el tag `chartreuse3_1` — sus opciones de "volver"/"cancelar" ya son texto plano sin color, no requiere el paso de recoloreo.)

- [ ] **Step 2: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "refactor(presentation): agregar header persistente a PipelineEditorMenu"
```

---

### Task 7: Conectar `SplashScreen` en `Program.cs`

**Depends on:** Task 3 (puede correr en paralelo con Task 4/5/6 — archivo distinto, no depende de `ShellRenderer`)

**Files:**
- Modify: `vali-deploy/Program.cs`

- [ ] **Step 1: Agregar los `using` necesarios y una función local que muestra la splash antes del shell**

Reemplazar:

```csharp
using Spectre.Console;
using vali_deploy.Managers;
using vali_deploy.Utils;

try
{
```

por:

```csharp
using Spectre.Console;
using vali_deploy;
using vali_deploy.Managers;
using vali_deploy.Presentation;
using vali_deploy.Utils;

async Task LaunchShellAsync()
{
    var config = CompositionRoot.CreateProjectRepository().Load();
    SplashScreen.ShowAndWait(config);
    await MenuManager.StartAsync();
}

try
{
```

- [ ] **Step 2: Reemplazar los 3 call-sites de `MenuManager.StartAsync()` por `LaunchShellAsync()`**

Primer call-site (rama sin download disponible para el OS actual), reemplazar:

```csharp
            else
            {
                AnsiConsole.MarkupLine("[red]No download available for your operating system.[/]");
                UpdaterManager.DeleteOldVersions();
                await MenuManager.StartAsync();
            }
```

por:

```csharp
            else
            {
                AnsiConsole.MarkupLine("[red]No download available for your operating system.[/]");
                UpdaterManager.DeleteOldVersions();
                await LaunchShellAsync();
            }
```

Segundo call-site (usuario rechaza la actualización), reemplazar:

```csharp
        else
        {
            UpdaterManager.DeleteOldVersions();
            await MenuManager.StartAsync();
        }
    }
    else
    {
        UpdaterManager.DeleteOldVersions();
        await MenuManager.StartAsync();
    }
```

por:

```csharp
        else
        {
            UpdaterManager.DeleteOldVersions();
            await LaunchShellAsync();
        }
    }
    else
    {
        UpdaterManager.DeleteOldVersions();
        await LaunchShellAsync();
    }
```

(Este único reemplazo cubre a la vez el segundo y el tercer call-site — son las dos ramas consecutivas que cierran, respectivamente, el `if (userWantsUpdate)` y el `if (updateInfo != null)` exteriores; tomarlas juntas como un solo bloque evita ambigüedad de match ya que el contenido interno de ambas ramas es idéntico.)

- [ ] **Step 3: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

Run: `grep -n "MenuManager.StartAsync" vali-deploy/Program.cs`
Expected: sin salida (0 ocurrencias — todas pasaron a `LaunchShellAsync()`)

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Program.cs
git commit -m "feat(presentation): mostrar SplashScreen antes de MenuManager.StartAsync"
```

---

### Task 8: Verificación manual end-to-end

**Depends on:** Tasks 1–7 (todas)

No hay tests automatizados para `Presentation/` en este proyecto (ver nota al inicio del plan) — esta tarea es la verificación real.

- [ ] **Step 1: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`, 0 errores.

- [ ] **Step 2: Recorrido funcional en terminal ancha (≥120 columnas)**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Verificar visualmente:
- Splash aparece primero: FigletText "Vali-Deploy" centrado, versión, panel con conteo de proyectos/subproyectos, "Presione una tecla para continuar…"
- Al presionar una tecla, entra al menú raíz con el nuevo header (marca + versión a la izquierda, "N proyectos · M subproyectos" a la derecha, separador debajo)
- Entrar a "Manage Environments": el header se mantiene, breadcrumb dice "Entornos"
- Volver al menú raíz, entrar a un proyecto con subproyecto → "Configure Publish File Omissions" (o cualquiera de los 3 flujos con árbol): el header muestra breadcrumb `"{proyecto} · {subproyecto}"`
- Si hay un subproyecto con pipeline (`PipelinesByEnvironment`), entrar a "Edit Pipeline": el header muestra breadcrumb `"{subproyecto} · {entorno}"`
- Las opciones "Exit"/"Back to..."/"Cancel" se ven en verde Forest (no en el lima brillante anterior)

- [ ] **Step 3: Repetir el mismo recorrido en terminal angosta (~80 columnas)**

Redimensionar la ventana de terminal a 80 columnas, volver a correr `dotnet run --project vali-deploy/vali-deploy.csproj` y repetir el Step 2.

Verificar: ningún panel ni el `Grid` del header se corta abruptamente ni tira excepción — el contenido se ajusta (wrap/recentrado) al ancho disponible, igual que ya hace el resto del CLI con `Rule`/`Panel` sin ancho fijo.

- [ ] **Step 4: Confirmar que no quedó código muerto**

Run: `grep -rn "ChartManager\|_barChart\|GetRandomColor\|chartreuse3_1" vali-deploy --include=*.cs`
Expected: sin salida (0 ocurrencias en código fuente — puede haber matches dentro de `vali-deploy/bin/`/`obj/`, que no cuentan; si aparecen, son artefactos de build viejos, no código fuente).

---

## Addendum (post-review): gaps de scope encontrados en el review holístico de Tasks 1-8

El review final detectó 2 gaps reales frente al goal del plan ("shell visual coherente... usado tanto en el menú raíz como en los submenús"):

1. **Header parpadea en 7 sub-flujos de edición** — `AnsiConsole.Clear()` sin `ShellRenderer.DrawHeader()` posterior, verificado línea por línea contra el código actual (no son 7 arbitrarios, son estos 7 sitios exactos): `AddFileToOmitFromPublishAsync`, `RemoveFileToOmitFromPublishAsync`, `AddDockerArgAsync`, `RemoveDockerArgsAsync`, `AddPublishArgAsync`, `RemovePublishArgsAsync` (todos en `MenuManager.cs`) y `EditStepArgs` (`PipelineEditorMenu.cs`). Durante esos sub-flujos el header desaparece por completo y solo reaparece cuando el loop padre vuelve a dibujar `DisplayOmitFilesFromPublish`/`DisplayDockerArgs`/`DisplayPublishArgs`.
2. **Pantallas de ejecución real nunca migradas** — `ExecuteSubProjectPipelineAsync` (prompt "Elegí el entorno a desplegar" + `PipelineExecutionView.RunAsync`) y las 3 ramas Docker Build/Docker Run/Push to Docker Hub de `ExecuteCommandSubProject`, ambos en `MenuManager.cs`, ejecutan sin `Clear()`/header propio — heredan lo que haya quedado en pantalla de la selección anterior.

---

### Task 9: Eliminar el parpadeo del header en los 7 sub-flujos de edición

**Depends on:** Task 2 (usa `ShellRenderer.DrawHeader`)

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

En `MenuManager.cs` los 6 sitios usan el campo estático `_projects` (ya en scope de clase, no requiere parámetro nuevo). En `PipelineEditorMenu.cs`, `EditStepArgs` no tiene `config`/breadcrumb en scope — se agregan como parámetros.

- [ ] **Step 1: `AddFileToOmitFromPublishAsync`**

Reemplazar:

```csharp
    private static async Task AddFileToOmitFromPublishAsync(SubProject subProject)
    {
        bool addingFiles = true;
        bool firstFileAdded = false;

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
        while (addingFiles)
        {
            if (firstFileAdded)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
            }
```

por:

```csharp
    private static async Task AddFileToOmitFromPublishAsync(SubProject subProject)
    {
        bool addingFiles = true;
        bool firstFileAdded = false;

        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
        AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
        while (addingFiles)
        {
            if (firstFileAdded)
            {
                AnsiConsole.Clear();
                Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
                AnsiConsole.MarkupLine("[yellow]Adding files to omit (type 'done' to finish)[/]");
            }
```

- [ ] **Step 2: `RemoveFileToOmitFromPublishAsync`**

Reemplazar:

```csharp
    private static Task RemoveFileToOmitFromPublishAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        if (subProject.OmitFiles.Count == 0)
```

por:

```csharp
    private static Task RemoveFileToOmitFromPublishAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
        if (subProject.OmitFiles.Count == 0)
```

- [ ] **Step 3: `AddDockerArgAsync`**

Reemplazar:

```csharp
    private static async Task AddDockerArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        while (addingArgs)
        {
            AnsiConsole.Clear();
            AnsiConsole.MarkupLine("[yellow]Adding a Docker argument[/]");
            AnsiConsole.WriteLine();
```

por:

```csharp
    private static async Task AddDockerArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        while (addingArgs)
        {
            AnsiConsole.Clear();
            Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
            AnsiConsole.MarkupLine("[yellow]Adding a Docker argument[/]");
            AnsiConsole.WriteLine();
```

Reemplazar (el segundo `Clear()` dentro del loop interno, tras el primer argumento):

```csharp
                if (firstArgAdded)
                {
                    AnsiConsole.Clear(); // Limpia la pantalla después del primer argumento
                    AnsiConsole.MarkupLine($"[yellow]Adding {type.ToLower()}s (type 'done' to finish)[/]");
                }
```

por:

```csharp
                if (firstArgAdded)
                {
                    AnsiConsole.Clear(); // Limpia la pantalla después del primer argumento
                    Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
                    AnsiConsole.MarkupLine($"[yellow]Adding {type.ToLower()}s (type 'done' to finish)[/]");
                }
```

- [ ] **Step 4: `RemoveDockerArgsAsync`**

Reemplazar:

```csharp
    private static Task RemoveDockerArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        var type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select argument type to remove:")
```

por:

```csharp
    private static Task RemoveDockerArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
        var type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select argument type to remove:")
```

- [ ] **Step 5: `AddPublishArgAsync`**

Reemplazar:

```csharp
    private static async Task AddPublishArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        bool firstArgAdded = false;

        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
        while (addingArgs)
        {
            if (firstArgAdded)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
            }
```

por:

```csharp
    private static async Task AddPublishArgAsync(SubProject subProject)
    {
        bool addingArgs = true;
        bool firstArgAdded = false;

        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
        AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
        while (addingArgs)
        {
            if (firstArgAdded)
            {
                AnsiConsole.Clear();
                Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
                AnsiConsole.MarkupLine("[yellow]Adding publish args (type 'done' to finish)[/]");
            }
```

- [ ] **Step 6: `RemovePublishArgsAsync`**

Reemplazar:

```csharp
    private static async Task RemovePublishArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        if (subProject.PublishArgs == null || subProject.PublishArgs.Count == 0)
```

por:

```csharp
    private static async Task RemovePublishArgsAsync(SubProject subProject)
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: subProject.Name);
        if (subProject.PublishArgs == null || subProject.PublishArgs.Count == 0)
```

- [ ] **Step 7: `EditStepArgs` — agregar `projects`/`breadcrumb` como parámetros**

En `vali-deploy/Presentation/PipelineEditorMenu.cs`, reemplazar el call-site:

```csharp
                case "Edit Step Args":
                    var toEdit = AnsiConsole.Prompt(
                        new SelectionPrompt<DeployStep>().Title("Editar Args de cuál paso?").UseConverter(s => s.Name).AddChoices(steps));
                    EditStepArgs(toEdit);
                    repository.Save(config);
                    break;
```

por:

```csharp
                case "Edit Step Args":
                    var toEdit = AnsiConsole.Prompt(
                        new SelectionPrompt<DeployStep>().Title("Editar Args de cuál paso?").UseConverter(s => s.Name).AddChoices(steps));
                    EditStepArgs(toEdit, config.Projects, $"{subProject.Name} · {environmentName}");
                    repository.Save(config);
                    break;
```

Reemplazar la firma y el `Clear()` del método:

```csharp
    private static void EditStepArgs(DeployStep step)
    {
        if (step.Args.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Este step no tiene Args definidos.[/]");
            return;
        }

        while (true)
        {
            AnsiConsole.Clear();
            var table = new Table().AddColumns("Key", "Value");
```

por:

```csharp
    private static void EditStepArgs(DeployStep step, IReadOnlyDictionary<string, Domain.Project> projects, string breadcrumb)
    {
        if (step.Args.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]Este step no tiene Args definidos.[/]");
            return;
        }

        while (true)
        {
            AnsiConsole.Clear();
            ShellRenderer.DrawHeader(projects, breadcrumb: breadcrumb);
            var table = new Table().AddColumns("Key", "Value");
```

- [ ] **Step 8: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 9: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "fix(presentation): mostrar header persistente en los 7 sub-flujos de edicion"
```

---

### Task 10: Migrar las pantallas de ejecución real (pipeline + Docker Build/Run/Push) a `ShellRenderer`

**Depends on:** Task 2

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs`

- [ ] **Step 1: `ExecuteSubProjectPipelineAsync` — header antes de "Elegí el entorno" y antes de ejecutar el pipeline**

Reemplazar:

```csharp
    private static async Task ExecuteSubProjectPipelineAsync(Project project, SubProject subProject, string projectName, Domain.DeployConfig config)
    {
        var environmentName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Elegí el entorno a desplegar:")
                .AddChoices(subProject.PipelinesByEnvironment.Keys));

        var environment = config.Environments.First(e => e.Name == environmentName);
        var steps = subProject.PipelinesByEnvironment[environmentName];

        if (steps.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]El pipeline de este entorno no tiene steps. Andá a 'Edit Pipeline' para agregar alguno.[/]");
            PauseForUserInput();
            return;
        }
```

por:

```csharp
    private static async Task ExecuteSubProjectPipelineAsync(Project project, SubProject subProject, string projectName, Domain.DeployConfig config)
    {
        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name}");

        var environmentName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Elegí el entorno a desplegar:")
                .AddChoices(subProject.PipelinesByEnvironment.Keys));

        var environment = config.Environments.First(e => e.Name == environmentName);
        var steps = subProject.PipelinesByEnvironment[environmentName];

        if (steps.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]El pipeline de este entorno no tiene steps. Andá a 'Edit Pipeline' para agregar alguno.[/]");
            PauseForUserInput();
            return;
        }

        AnsiConsole.Clear();
        Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name} · {environmentName}");
```

(El segundo `Clear()`+header, justo antes de ejecutar, deja el breadcrumb con el entorno ya elegido visible durante todo el `PipelineExecutionView.RunAsync` — que solo dibuja la tabla de progreso/resumen debajo, nunca su propio header.)

- [ ] **Step 2: Header antes de las 3 ramas Docker en `ExecuteCommandSubProject`**

Reemplazar:

```csharp
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"What do you want to do with subproject '{subProject.Name}'?")
                .AddChoices(choices)
        );

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
```

por:

```csharp
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"What do you want to do with subproject '{subProject.Name}'?")
                .AddChoices(choices)
        );

        if (action is "Docker Build" or "Docker Run" or "Push to Docker Hub")
        {
            AnsiConsole.Clear();
            Presentation.ShellRenderer.DrawHeader(_projects, breadcrumb: $"{projectName} · {subProject.Name}");
        }

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
```

(Un solo `if` antes del `switch` cubre las 3 ramas Docker sin duplicar el `Clear()`+header en cada `case` — `_projects` es el campo estático de la clase, igual que en el resto de `MenuManager.cs`.)

- [ ] **Step 3: Verificar que compila**

Run: `dotnet build vali-deploy/vali-deploy.csproj`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): agregar header persistente a las pantallas de ejecucion (pipeline y Docker)"
```

---

### Task 11: Verificación manual end-to-end del addendum

**Depends on:** Tasks 9-10

- [ ] **Step 1: Build completo**

Run: `dotnet build vali-deploy.sln`
Expected: `Build succeeded.`, 0 errores.

- [ ] **Step 2: Recorrido funcional de los 7 sub-flujos (Task 9)**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

Entrar a cada uno de los 7 flujos (Add/Remove de omit-files, Add/Remove de Docker args, Add/Remove de publish args, Edit Step Args de un pipeline) y verificar que el header (marca + versión + breadcrumb) permanece visible en todo momento — nunca desaparece entre el `Clear()` de entrada y la primera línea de contenido.

- [ ] **Step 3: Recorrido funcional de las pantallas de ejecución (Task 10)**

Ejecutar un pipeline de un subproyecto con `PipelinesByEnvironment` configurado — verificar que el header aparece antes del prompt "Elegí el entorno a desplegar" y se actualiza con el breadcrumb del entorno elegido antes de que arranque `PipelineExecutionView` (barras de progreso).

Si hay un subproyecto con `DockerfilePath` configurado, ejecutar Docker Build (o Docker Run/Push si hay imagen disponible) y verificar que el header aparece antes del mensaje "Building Docker image...".

- [ ] **Step 4: Confirmar que no quedó código muerto ni breadcrumbs rotos**

Run: `grep -rn "AnsiConsole.Clear" vali-deploy/Managers/MenuManager.cs vali-deploy/Presentation/PipelineEditorMenu.cs --include=*.cs`

Revisar manualmente que cada `Clear()` restante en estos dos archivos tiene un `DrawHeader`/`DisplayMainMenu` inmediatamente después (los únicos `Clear()` sin header esperado son los del loop principal de `MenuManager` que llaman a `DisplayMainMenu()` en la misma línea siguiente, ya cubiertos desde Task 4).
