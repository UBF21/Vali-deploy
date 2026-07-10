# Selector de idioma (Inglés/Español) — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar un selector de idioma (Inglés/Español) al menú principal del CLI que traduzca los `Title`/`AddChoices` estáticos de los 22 prompts hoy hardcodeados en inglés en `MenuManager.cs`, persistiendo la elección en `deploy_config.json`.

**Architecture:** Un `Translator` estático (Presentation) mantiene el idioma actual en memoria y expone `T(string)` — traduce si hay una entrada en un diccionario interno en-es y el idioma actual es español, si no devuelve el string sin cambios (seguro para aplicar incluso sobre listas mixtas de opciones estáticas + datos dinámicos). Cada `SelectionPrompt<string>`/`MultiSelectionPrompt<string>` en alcance agrega `.UseConverter(Translator.T)` para las choices y envuelve su `.Title(...)` con `Translator.T(...)` — el valor que el código recibe de vuelta (usado en los `switch`/`if`) no cambia, solo lo que se muestra.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, xUnit 2.6.6 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-10-language-toggle-design.md`

---

### Task 1: `DeployConfig.Language` + `Translator` (con diccionario completo)

**Files:**
- Modify: `vali-deploy/Domain/DeployConfig.cs`
- Create: `vali-deploy/Presentation/Translator.cs`
- Test: `vali-deploy.Tests/Presentation/TranslatorTests.cs`

- [ ] **Step 1: Escribir los tests de `Translator`**

```csharp
using vali_deploy.Presentation;

namespace vali_deploy.Tests.Presentation;

public class TranslatorTests
{
    [Fact]
    public void T_returns_english_unchanged_when_current_language_is_english()
    {
        Translator.SetLanguage("en");

        Assert.Equal("Add Project", Translator.T("Add Project"));
    }

    [Fact]
    public void T_returns_translation_when_current_language_is_spanish_and_key_exists()
    {
        Translator.SetLanguage("es");

        Assert.Equal("Agregar Proyecto", Translator.T("Add Project"));

        Translator.SetLanguage("en");
    }

    [Fact]
    public void T_returns_original_text_unchanged_when_spanish_and_key_not_found()
    {
        Translator.SetLanguage("es");

        Assert.Equal("MyDynamicProjectName", Translator.T("MyDynamicProjectName"));

        Translator.SetLanguage("en");
    }

    [Fact]
    public void SetLanguage_changes_behavior_of_subsequent_T_calls()
    {
        Translator.SetLanguage("en");
        Assert.Equal("Show Projects", Translator.T("Show Projects"));

        Translator.SetLanguage("es");
        Assert.Equal("Ver Proyectos", Translator.T("Show Projects"));

        Translator.SetLanguage("en");
        Assert.Equal("Show Projects", Translator.T("Show Projects"));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter TranslatorTests`
Expected: FAIL (no existe `vali_deploy.Presentation.Translator`, error de compilación)

- [ ] **Step 3: Agregar `Language` a `DeployConfig`**

En `vali-deploy/Domain/DeployConfig.cs`, reemplazar el archivo completo:

```csharp
namespace vali_deploy.Domain;

public class DeployConfig
{
    public Dictionary<string, Project> Projects { get; set; } = new();
    public List<DeployEnvironment> Environments { get; set; } = new();
    public string Language { get; set; } = "en";
}
```

- [ ] **Step 4: Crear `Translator.cs` con el diccionario completo**

```csharp
namespace vali_deploy.Presentation;

public static class Translator
{
    private static string _currentLanguage = "en";

    private static readonly Dictionary<string, string> EnToEs = new()
    {
        // Menú principal
        ["What do you want to do?"] = "¿Qué querés hacer?",
        ["Add Project"] = "Agregar Proyecto",
        ["Remove Project"] = "Eliminar Proyecto",
        ["Show Projects"] = "Ver Proyectos",
        ["Configure Publish File Omissions"] = "Configurar Archivos Omitidos de Publish",
        ["Remove Subprojects"] = "Eliminar Subproyectos",
        ["Manage Docker Projects"] = "Gestionar Proyectos Docker",
        ["Manage Publish Arguments"] = "Gestionar Argumentos de Publish",
        ["Manage Environments"] = "Gestionar Entornos",
        ["View Deploy History"] = "Ver Historial de Deploys",
        ["View Environments Tree"] = "Ver Árbol de Entornos",
        ["[seagreen1]Exit[/]"] = "[seagreen1]Salir[/]",

        // Navegación reutilizada
        ["[seagreen1]Back to Main Menu[/]"] = "[seagreen1]Volver al Menú Principal[/]",
        ["[seagreen1]Back to Projects Menu[/]"] = "[seagreen1]Volver al Menú de Proyectos[/]",
        ["[seagreen1]Back to Projects[/]"] = "[seagreen1]Volver a Proyectos[/]",
        ["[seagreen1]Back to Subprojects[/]"] = "[seagreen1]Volver a Subproyectos[/]",
        ["[seagreen1]Back[/]"] = "[seagreen1]Volver[/]",
        ["[seagreen1]Cancel[/]"] = "[seagreen1]Cancelar[/]",

        // Remover subproyectos
        ["Select projects to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los proyectos a eliminar (barra espaciadora para seleccionar, Enter para confirmar)",
        ["Select a project to remove subprojects from"] = "Elegí un proyecto para eliminarle subproyectos",
        ["Select subprojects to remove from project '{0}' (use spacebar to select, Enter to confirm)"] =
            "Elegí los subproyectos a eliminar del proyecto '{0}' (barra espaciadora para seleccionar, Enter para confirmar)",

        // Show Projects
        ["Select a project"] = "Elegí un proyecto",
        ["Select a subproject for project '{0}'"] = "Elegí un subproyecto del proyecto '{0}'",

        // Omitir archivos de publish
        ["Select a project to configure publish file omissions"] =
            "Elegí un proyecto para configurar archivos omitidos de publish",
        ["Select a subproject for project '{0}' to manage files to omit"] =
            "Elegí un subproyecto del proyecto '{0}' para gestionar archivos a omitir",
        ["Add file to omit"] = "Agregar archivo a omitir",
        ["Remove file from omit list"] = "Quitar archivo de la lista de omitidos",
        ["Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)"] =
            "Elegí los archivos a quitar de la lista de omitidos (barra espaciadora para seleccionar, Enter para confirmar)",

        // Ejecutar comando de subproyecto
        ["What do you want to do with subproject '{0}'?"] = "¿Qué querés hacer con el subproyecto '{0}'?",
        ["Generate Microsoft publish"] = "Generar publish de Microsoft",
        ["Edit Pipeline"] = "Editar Pipeline",
        ["Push to registry"] = "Subir al registry",

        // Proyectos/subproyectos Docker
        ["Select a project with Docker subprojects"] = "Elegí un proyecto con subproyectos Docker",
        ["Select a Docker subproject in '{0}'"] = "Elegí un subproyecto Docker en '{0}'",
        ["Add Docker Arg"] = "Agregar Argumento Docker",
        ["Remove Docker Args"] = "Quitar Argumentos Docker",
        ["Select argument type:"] = "Elegí el tipo de argumento:",
        ["Build Arg"] = "Argumento de Build",
        ["Run Arg"] = "Argumento de Run",
        ["Select argument type to remove:"] = "Elegí el tipo de argumento a quitar:",
        ["Build Args"] = "Argumentos de Build",
        ["Run Args"] = "Argumentos de Run",
        ["Select build args to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los argumentos de build a quitar (barra espaciadora para seleccionar, Enter para confirmar)",
        ["Select run args to remove (use spacebar to select, Enter to confirm)"] =
            "Elegí los argumentos de run a quitar (barra espaciadora para seleccionar, Enter para confirmar)",

        // Argumentos de publish
        ["Select a project to manage publish arguments"] = "Elegí un proyecto para gestionar argumentos de publish",
        ["Select a subproject in '{0}' to manage publish arguments"] =
            "Elegí un subproyecto en '{0}' para gestionar argumentos de publish",
        ["Add Publish Arg"] = "Agregar Argumento de Publish",
        ["Remove Publish Args"] = "Quitar Argumentos de Publish",
        ["Toggle Zip Publish Output"] = "Alternar Salida Zip de Publish",
        ["Select publish args to remove (use space-bar to select, Enter to confirm)"] =
            "Elegí los argumentos de publish a quitar (barra espaciadora para seleccionar, Enter para confirmar)"
    };

    public static void SetLanguage(string language) => _currentLanguage = language;

    public static string T(string english) =>
        _currentLanguage == "es" && EnToEs.TryGetValue(english, out var translated) ? translated : english;
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter TranslatorTests`
Expected: PASS (4/4)

Correr también la suite completa: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` — debería seguir en 144/144 (140 previos + 4 nuevos).

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Domain/DeployConfig.cs vali-deploy/Presentation/Translator.cs vali-deploy.Tests/Presentation/TranslatorTests.cs
git commit -m "feat(presentation): agregar Translator y DeployConfig.Language para el selector de idioma"
```

---

### Task 2: Wiring — cargar idioma al arrancar + nuevo menú "Language / Idioma"

**Depends on:** Task 1

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs:27-30` (StartAsync), `:38-40` (switch), `:114-120` (choices)

No hay test nuevo en este task — mismo criterio ya establecido: la capa Presentation/Manager basada en Spectre.Console no se testea en este repo.

- [ ] **Step 1: Cargar el idioma al arrancar**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar las líneas 27-30:

```csharp
    public static async Task StartAsync()
    {
        var config = _repository.Load();
        _projects = config.Projects;
        Presentation.Translator.SetLanguage(config.Language);
```

- [ ] **Step 2: Agregar el `case` en el switch de `StartAsync()`**

En `vali-deploy/Managers/MenuManager.cs`, dentro del `switch (option)`, entre `case "View Environments Tree":` y `case "[seagreen1]Exit[/]":`:

```csharp
                case "View Environments Tree":
                    await Presentation.EnvironmentsTreeView.ShowAsync(Application.EnvironmentsTreeBuilder.Build(_repository.Load()));
                    break;
                case "Language / Idioma":
                    ShowLanguageMenu();
                    break;
                case "[seagreen1]Exit[/]":
```

- [ ] **Step 3: Agregar la opción a `GetMainMenuOption()`**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar `GetMainMenuOption()` completo:

```csharp
    private static string GetMainMenuOption()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("What do you want to do?"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices("Add Project", "Remove Project", "Show Projects", "Configure Publish File Omissions",
                    "Remove Subprojects", "Manage Docker Projects", "Manage Publish Arguments", "Manage Environments",
                    "View Deploy History", "View Environments Tree", "Language / Idioma", "[seagreen1]Exit[/]")
        );
    }
```

- [ ] **Step 4: Crear `ShowLanguageMenu()`**

En `vali-deploy/Managers/MenuManager.cs`, agregar el siguiente método privado (por ejemplo, después de `GetMainMenuOption()`):

```csharp
    /// <summary>
    /// Muestra el selector de idioma (Inglés/Español), persiste la elección en deploy_config.json
    /// y actualiza <see cref="Presentation.Translator"/> para que tenga efecto inmediato en la sesión actual.
    /// </summary>
    private static void ShowLanguageMenu()
    {
        var selected = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Language / Idioma")
                .AddChoices("English", "Español")
        );

        var languageCode = selected == "Español" ? "es" : "en";

        var config = _repository.Load();
        config.Language = languageCode;
        _repository.Save(config);

        Presentation.Translator.SetLanguage(languageCode);
    }
```

- [ ] **Step 5: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, 144/144 (sin tests nuevos en este task, cuenta igual que al final de Task 1).

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): cargar idioma al arrancar y agregar menu Language/Idioma"
```

---

### Task 3: Traducir menú principal + flujos "Remove Subprojects" y "Show Projects"

**Depends on:** Task 2

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs` (`RemoveProject`, `PromptProjectSelectionForSubprojectRemoval`, `PromptMultipleSubProjectSelection`, `PromptProjectSelection`, `PromptSubProjectSelection`)

Nota: `GetMainMenuOption()` ya se tradujo en el Task 2 (Step 3) porque compartía el mismo edit que agregaba la opción de idioma — no se repite acá.

No hay test nuevo — mismo criterio de siempre.

- [ ] **Step 1: `RemoveProject` — traducir el título del multi-select**

Reemplazar:

```csharp
        var projectsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select projects to remove (use spacebar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
```

Por:

```csharp
        var projectsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select projects to remove (use spacebar to select, Enter to confirm)"))
                .UseConverter(Presentation.Translator.T)
                .NotRequired()
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
```

- [ ] **Step 2: `PromptProjectSelectionForSubprojectRemoval`**

Reemplazar:

```csharp
    private static string PromptProjectSelectionForSubprojectRemoval()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to remove subprojects from")
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

Por:

```csharp
    private static string PromptProjectSelectionForSubprojectRemoval()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select a project to remove subprojects from"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

- [ ] **Step 3: `PromptMultipleSubProjectSelection` — título con plantilla**

Reemplazar:

```csharp
        var selectedSubProjects = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title(
                    $"Select subprojects to remove from project '{projectName}' (use spacebar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Cancel[/]"))
        );
```

Por:

```csharp
        var selectedSubProjects = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title(string.Format(
                    Presentation.Translator.T("Select subprojects to remove from project '{0}' (use spacebar to select, Enter to confirm)"),
                    projectName))
                .UseConverter(Presentation.Translator.T)
                .NotRequired()
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Cancel[/]"))
        );
```

- [ ] **Step 4: `PromptProjectSelection`**

Reemplazar:

```csharp
    private static string PromptProjectSelection()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project")
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

Por:

```csharp
    private static string PromptProjectSelection()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select a project"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

- [ ] **Step 5: `PromptSubProjectSelection` — título con plantilla**

Reemplazar:

```csharp
    private static string PromptSubProjectSelection(Project project, string projectName)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a subproject for project '{projectName}'")
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects Menu[/]"))
        );
    }
```

Por:

```csharp
    private static string PromptSubProjectSelection(Project project, string projectName)
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(string.Format(Presentation.Translator.T("Select a subproject for project '{0}'"), projectName))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects Menu[/]"))
        );
    }
```

- [ ] **Step 6: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln` → 0 errores.
Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` → 144/144.

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): traducir menu principal y flujos Remove Subprojects / Show Projects"
```

---

### Task 4: Traducir flujo "omit files" + `ExecuteCommandSubProject`

**Depends on:** Task 3

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs` (`PromptProjectForOmitFilesFromPublish`, `SelectSubProjectAsync`, `PromptFileManagementAction`, `RemoveFileToOmitFromPublishAsync`, `ExecuteCommandSubProject`)

- [ ] **Step 1: `PromptProjectForOmitFilesFromPublish`**

Reemplazar:

```csharp
    private static string PromptProjectForOmitFilesFromPublish()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select a project to configure publish file omissions")
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

Por:

```csharp
    private static string PromptProjectForOmitFilesFromPublish()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select a project to configure publish file omissions"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
        );
    }
```

- [ ] **Step 2: `SelectSubProjectAsync` — título con plantilla**

Reemplazar:

```csharp
        var subProjectName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"Select a subproject for project '{projectName}' to manage files to omit")
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
        );
```

Por:

```csharp
        var subProjectName = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(string.Format(
                    Presentation.Translator.T("Select a subproject for project '{0}' to manage files to omit"),
                    projectName))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
        );
```

- [ ] **Step 3: `PromptFileManagementAction`**

Reemplazar:

```csharp
    private static string PromptFileManagementAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add file to omit", "Remove file from omit list", "[seagreen1]Back to Subprojects[/]")
        );
    }
```

Por:

```csharp
    private static string PromptFileManagementAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("What do you want to do?"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices("Add file to omit", "Remove file from omit list", "[seagreen1]Back to Subprojects[/]")
        );
    }
```

- [ ] **Step 4: `RemoveFileToOmitFromPublishAsync`**

Reemplazar:

```csharp
            var filesToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.OmitFiles.Append("[seagreen1]Cancel[/]"))
            );
```

Por:

```csharp
            var filesToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)"))
                    .UseConverter(Presentation.Translator.T)
                    .NotRequired()
                    .AddChoices(subProject.OmitFiles.Append("[seagreen1]Cancel[/]"))
            );
```

- [ ] **Step 5: `ExecuteCommandSubProject` — título con plantilla**

Reemplazar:

```csharp
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title($"What do you want to do with subproject '{subProject.Name}'?")
                .AddChoices(choices)
        );
```

Por:

```csharp
        var action = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(string.Format(
                    Presentation.Translator.T("What do you want to do with subproject '{0}'?"),
                    subProject.Name))
                .UseConverter(Presentation.Translator.T)
                .AddChoices(choices)
        );
```

- [ ] **Step 6: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln` → 0 errores.
Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` → 144/144.

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): traducir flujo de omit-files y menu de ExecuteCommandSubProject"
```

---

### Task 5: Traducir menús de Docker

**Depends on:** Task 4

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs` (selección de proyecto/subproyecto Docker, `PromptDockerArgsAction`, selección de tipo de argumento en `AddDockerArgAsync`, `RemoveDockerArgsAsync`)

- [ ] **Step 1: Selección de proyecto con subproyectos Docker**

Reemplazar:

```csharp
            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a project with Docker subprojects")
                    .AddChoices(dockerProjects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
            );
```

Por:

```csharp
            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select a project with Docker subprojects"))
                    .UseConverter(Presentation.Translator.T)
                    .AddChoices(dockerProjects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
            );
```

- [ ] **Step 2: Selección de subproyecto Docker — título con plantilla**

Reemplazar:

```csharp
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a Docker subproject in '{projectName}'")
                    .AddChoices(dockerSubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
            );
```

Por:

```csharp
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(string.Format(Presentation.Translator.T("Select a Docker subproject in '{0}'"), projectName))
                    .UseConverter(Presentation.Translator.T)
                    .AddChoices(dockerSubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
            );
```

- [ ] **Step 3: `PromptDockerArgsAction`**

Reemplazar:

```csharp
    private static string PromptDockerArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Docker Arg", "Remove Docker Args", "[seagreen1]Back to Subprojects[/]")
        );
    }
```

Por:

```csharp
    private static string PromptDockerArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("What do you want to do?"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices("Add Docker Arg", "Remove Docker Args", "[seagreen1]Back to Subprojects[/]")
        );
    }
```

- [ ] **Step 4: `AddDockerArgAsync` — selección de tipo de argumento**

Reemplazar:

```csharp
            var type = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select argument type:")
                    .AddChoices("Build Arg", "Run Arg", "[seagreen1]Back[/]")
            );
```

Por:

```csharp
            var type = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select argument type:"))
                    .UseConverter(Presentation.Translator.T)
                    .AddChoices("Build Arg", "Run Arg", "[seagreen1]Back[/]")
            );
```

- [ ] **Step 5: `RemoveDockerArgsAsync` — selección de tipo a quitar**

Reemplazar:

```csharp
        var type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("Select argument type to remove:")
                .AddChoices("Build Args", "Run Args", "[seagreen1]Cancel[/]")
        );
```

Por:

```csharp
        var type = AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select argument type to remove:"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices("Build Args", "Run Args", "[seagreen1]Cancel[/]")
        );
```

- [ ] **Step 6: `RemoveDockerArgsAsync` — multi-select de build args**

Reemplazar:

```csharp
            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select build args to remove (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.DockerBuildArgs.Append("[seagreen1]Cancel[/]"))
            );
```

Por:

```csharp
            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select build args to remove (use spacebar to select, Enter to confirm)"))
                    .UseConverter(Presentation.Translator.T)
                    .NotRequired()
                    .AddChoices(subProject.DockerBuildArgs.Append("[seagreen1]Cancel[/]"))
            );
```

- [ ] **Step 7: `RemoveDockerArgsAsync` — multi-select de run args**

Reemplazar:

```csharp
            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title("Select run args to remove (use spacebar to select, Enter to confirm)")
                    .NotRequired()
                    .AddChoices(subProject.DockerRunArgs.Append("[seagreen1]Cancel[/]"))
            );
```

Por:

```csharp
            var argsToRemove = AnsiConsole.Prompt(
                new MultiSelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select run args to remove (use spacebar to select, Enter to confirm)"))
                    .UseConverter(Presentation.Translator.T)
                    .NotRequired()
                    .AddChoices(subProject.DockerRunArgs.Append("[seagreen1]Cancel[/]"))
            );
```

- [ ] **Step 8: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln` → 0 errores.
Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` → 144/144.

- [ ] **Step 9: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): traducir menus de gestion de Docker"
```

---

### Task 6: Traducir menús de argumentos de publish

**Depends on:** Task 5

**Files:**
- Modify: `vali-deploy/Managers/MenuManager.cs` (selección de proyecto/subproyecto para publish args, `PromptPublishArgsAction`, `RemovePublishArgsAsync`)

- [ ] **Step 1: Selección de proyecto para publish args**

Reemplazar:

```csharp
            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title("Select a project to manage publish arguments")
                    .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
            );
```

Por:

```csharp
            var projectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(Presentation.Translator.T("Select a project to manage publish arguments"))
                    .UseConverter(Presentation.Translator.T)
                    .AddChoices(_projects.Keys.Append("[seagreen1]Back to Main Menu[/]"))
            );
```

- [ ] **Step 2: Selección de subproyecto para publish args — título con plantilla**

Reemplazar:

```csharp
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title($"Select a subproject in '{projectName}' to manage publish arguments")
                    .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
            );
```

Por:

```csharp
            var subProjectName = AnsiConsole.Prompt(
                new SelectionPrompt<string>()
                    .Title(string.Format(
                        Presentation.Translator.T("Select a subproject in '{0}' to manage publish arguments"),
                        projectName))
                    .UseConverter(Presentation.Translator.T)
                    .AddChoices(project.SubProjects.Select(sp => sp.Name).Append("[seagreen1]Back to Projects[/]"))
            );
```

- [ ] **Step 3: `PromptPublishArgsAction`**

Reemplazar:

```csharp
    private static string PromptPublishArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Publish Arg", "Remove Publish Args", "Toggle Zip Publish Output",
                    "[seagreen1]Back to Subprojects[/]")
        );
    }
```

Por:

```csharp
    private static string PromptPublishArgsAction()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title(Presentation.Translator.T("What do you want to do?"))
                .UseConverter(Presentation.Translator.T)
                .AddChoices("Add Publish Arg", "Remove Publish Args", "Toggle Zip Publish Output",
                    "[seagreen1]Back to Subprojects[/]")
        );
    }
```

- [ ] **Step 4: `RemovePublishArgsAsync`**

Reemplazar:

```csharp
        var argsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title("Select publish args to remove (use space-bar to select, Enter to confirm)")
                .NotRequired()
                .AddChoices(subProject.PublishArgs.Append("[seagreen1]Cancel[/]"))
        );
```

Por:

```csharp
        var argsToRemove = AnsiConsole.Prompt(
            new MultiSelectionPrompt<string>()
                .Title(Presentation.Translator.T("Select publish args to remove (use space-bar to select, Enter to confirm)"))
                .UseConverter(Presentation.Translator.T)
                .NotRequired()
                .AddChoices(subProject.PublishArgs.Append("[seagreen1]Cancel[/]"))
        );
```

- [ ] **Step 5: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln` → 0 errores.
Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` → 144/144.

- [ ] **Step 6: Commit**

```bash
git add vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): traducir menus de argumentos de publish"
```

---

### Task 7: Verificación manual

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Correr el CLI**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

- [ ] **Step 2: Verificar default en inglés**

Confirmar que el menú principal aparece en inglés (comportamiento sin cambios respecto a antes de esta feature) — a menos que ya hayas elegido español en una corrida anterior.

- [ ] **Step 3: Cambiar a español**

Elegir "Language / Idioma" → "Español". Confirmar que el menú principal se re-dibuja (la próxima vez que se muestre) en español: "Agregar Proyecto", "Ver Proyectos", etc.

- [ ] **Step 4: Recorrer 2-3 submenús traducidos**

Entrar a "Ver Proyectos" (Show Projects) y a "Gestionar Proyectos Docker" (Manage Docker Projects). Confirmar que los títulos y opciones están en español, y que las opciones "Volver a..."/"Cancelar" funcionan igual que antes (el routing no debería haberse roto).

- [ ] **Step 5: Verificar que lo fuera de alcance sigue en español fijo**

Entrar a "Ver Historial de Deploys" (View Deploy History) o "Ver Árbol de Entornos" — deberían seguir en español, sin importar si el idioma está en inglés o español (no cambian, están fuera de alcance).

- [ ] **Step 6: Verificar persistencia**

Salir del CLI y volver a correrlo. Confirmar que el menú principal arranca directamente en español (el idioma persistió en `deploy_config.json`).

- [ ] **Step 7: Volver a inglés**

Elegir "Language / Idioma" → "English". Confirmar que el menú principal vuelve a inglés.

Si cualquiera de estos pasos falla, corregir el código correspondiente en el task de origen y volver a correr `dotnet test` antes de continuar.

---

## Self-review

**Cobertura de la spec:** los 22 prompts inventariados en la spec están cubiertos uno a uno entre Tasks 3-6 (6 en Task 3, 5 en Task 4, 7 en Task 5, 4 en Task 6 = 22). El prompt nuevo (Language/Idioma) está en Task 2. `DeployConfig.Language` y `Translator` con el diccionario completo están en Task 1. Los 4 archivos fuera de alcance no aparecen en ningún task — correcto.

**Consistencia de tipos:** `Translator.T(string)` y `Translator.SetLanguage(string)` se usan con la misma firma en todos los tasks 2-6, sin variar nombres.

**Sin placeholders:** todos los steps tienen código completo (diccionario íntegro en Task 1, cada reemplazo de método completo en Tasks 3-6), sin TBD.
