# Vista de árbol por Entorno — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar una pantalla nueva de solo lectura ("View Environments Tree") al menú principal del CLI que muestre, como árbol, qué proyectos/subproyectos tienen un pipeline configurado para cada entorno (QA, DEV, etc.).

**Architecture:** Toda la lógica de agrupación (qué proyecto/subproyecto matchea qué entorno, cuándo colapsar el nivel de subproyecto) vive en una función pura y testeable (`EnvironmentsTreeBuilder.Build`) que no toca Spectre.Console ni hace I/O — recibe el `DeployConfig` ya cargado y devuelve una lista de nodos de dominio. La capa de presentación (`EnvironmentsTreeView`) solo itera esa lista ya armada y la dibuja con el componente `Tree` de Spectre.Console, sin lógica propia de negocio.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1, xUnit 2.6.6 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-10-environments-tree-design.md`

---

### Task 1: `EnvironmentTreeNode`/`ProjectTreeNode` (Domain) + `EnvironmentsTreeBuilder` (Application)

**Files:**
- Create: `vali-deploy/Domain/EnvironmentTreeNode.cs`
- Create: `vali-deploy/Domain/ProjectTreeNode.cs`
- Create: `vali-deploy/Application/EnvironmentsTreeBuilder.cs`
- Test: `vali-deploy.Tests/Application/EnvironmentsTreeBuilderTests.cs`

Los dos tipos de `Domain/` son POCOs sin comportamiento propio (listas vacías por defecto) — no llevan test dedicado, se ejercitan a través de los tests de `EnvironmentsTreeBuilder`.

- [ ] **Step 1: Escribir los tests de `EnvironmentsTreeBuilder`**

```csharp
using vali_deploy.Application;
using vali_deploy.Domain;

namespace vali_deploy.Tests.Application;

public class EnvironmentsTreeBuilderTests
{
    private static DeployConfig ConfigWith(List<DeployEnvironment> environments, Dictionary<string, Project> projects) =>
        new() { Environments = environments, Projects = projects };

    [Fact]
    public void Build_environment_with_no_matching_projects_returns_empty_projects_list()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject> { new() { Name = "api", PipelinesByEnvironment = new() } }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        Assert.Single(result);
        Assert.Equal("QA", result[0].EnvironmentName);
        Assert.Empty(result[0].Projects);
    }

    [Fact]
    public void Build_collapses_single_subproject_project_to_a_leaf_with_no_subproject_names()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal("shop", projectNode.ProjectName);
        Assert.Empty(projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_keeps_project_as_branch_when_multiple_subprojects_even_if_only_one_matches()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal("shop", projectNode.ProjectName);
        Assert.Equal(new[] { "api" }, projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_lists_all_matching_subprojects_when_multiple_match_in_SubProjects_order()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var projectNode = Assert.Single(result[0].Projects);
        Assert.Equal(new[] { "api", "worker" }, projectNode.SubProjectNames);
    }

    [Fact]
    public void Build_excludes_subproject_with_no_pipeline_in_any_environment()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" }, new() { Name = "DEV" } },
            projects: new Dictionary<string, Project>
            {
                ["shop"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "api", PipelinesByEnvironment = new() { ["DEV"] = new List<DeployStep>() } },
                        new() { Name = "worker", PipelinesByEnvironment = new() }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var qaNode = result.Single(e => e.EnvironmentName == "QA");
        var devNode = result.Single(e => e.EnvironmentName == "DEV");

        Assert.Empty(qaNode.Projects);
        var shopUnderDev = Assert.Single(devNode.Projects);
        Assert.Equal(new[] { "api" }, shopUnderDev.SubProjectNames);
    }

    [Fact]
    public void Build_keeps_environments_independent_from_each_other()
    {
        var config = ConfigWith(
            environments: new List<DeployEnvironment> { new() { Name = "QA" }, new() { Name = "DEV" } },
            projects: new Dictionary<string, Project>
            {
                ["app-qa"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "app-qa", PipelinesByEnvironment = new() { ["QA"] = new List<DeployStep>() } }
                    }
                },
                ["app-dev"] = new Project
                {
                    SubProjects = new List<SubProject>
                    {
                        new() { Name = "app-dev", PipelinesByEnvironment = new() { ["DEV"] = new List<DeployStep>() } }
                    }
                }
            });

        var result = EnvironmentsTreeBuilder.Build(config);

        var qaNode = result.Single(e => e.EnvironmentName == "QA");
        var devNode = result.Single(e => e.EnvironmentName == "DEV");

        Assert.Equal("app-qa", Assert.Single(qaNode.Projects).ProjectName);
        Assert.Equal("app-dev", Assert.Single(devNode.Projects).ProjectName);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter EnvironmentsTreeBuilderTests`
Expected: FAIL (no existen `EnvironmentsTreeBuilder`, `EnvironmentTreeNode` ni `ProjectTreeNode`, error de compilación)

- [ ] **Step 3: Crear `EnvironmentTreeNode`**

```csharp
namespace vali_deploy.Domain;

public class EnvironmentTreeNode
{
    public string EnvironmentName { get; set; } = "";
    public List<ProjectTreeNode> Projects { get; set; } = new();
}
```

- [ ] **Step 4: Crear `ProjectTreeNode`**

```csharp
namespace vali_deploy.Domain;

public class ProjectTreeNode
{
    public string ProjectName { get; set; } = "";
    public List<string> SubProjectNames { get; set; } = new();
}
```

- [ ] **Step 5: Crear `EnvironmentsTreeBuilder`**

```csharp
using vali_deploy.Domain;

namespace vali_deploy.Application;

public static class EnvironmentsTreeBuilder
{
    public static List<EnvironmentTreeNode> Build(DeployConfig config)
    {
        return config.Environments
            .Select(environment => BuildEnvironmentNode(environment, config.Projects))
            .ToList();
    }

    private static EnvironmentTreeNode BuildEnvironmentNode(DeployEnvironment environment, Dictionary<string, Project> projects)
    {
        var projectNodes = projects
            .Select(kvp => BuildProjectNode(kvp.Key, kvp.Value, environment.Name))
            .Where(node => node != null)
            .Select(node => node!)
            .ToList();

        return new EnvironmentTreeNode { EnvironmentName = environment.Name, Projects = projectNodes };
    }

    private static ProjectTreeNode? BuildProjectNode(string projectName, Project project, string environmentName)
    {
        var matchingSubProjects = project.SubProjects
            .Where(sp => sp.PipelinesByEnvironment.ContainsKey(environmentName))
            .Select(sp => sp.Name)
            .ToList();

        if (matchingSubProjects.Count == 0)
        {
            return null;
        }

        return new ProjectTreeNode
        {
            ProjectName = projectName,
            SubProjectNames = project.SubProjects.Count == 1 ? new List<string>() : matchingSubProjects
        };
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj --filter EnvironmentsTreeBuilderTests`
Expected: PASS (6/6)

Correr también la suite completa para confirmar que no hay regresiones: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj` — debería seguir en 140/140 (134 previos + 6 nuevos).

- [ ] **Step 7: Commit**

```bash
git add vali-deploy/Domain/EnvironmentTreeNode.cs vali-deploy/Domain/ProjectTreeNode.cs vali-deploy/Application/EnvironmentsTreeBuilder.cs vali-deploy.Tests/Application/EnvironmentsTreeBuilderTests.cs
git commit -m "feat(application): agregar EnvironmentsTreeBuilder para agrupar proyectos por entorno"
```

---

### Task 2: `EnvironmentsTreeView` (Presentation) + wiring en `MenuManager`

**Depends on:** Task 1

**Files:**
- Create: `vali-deploy/Presentation/EnvironmentsTreeView.cs`
- Modify: `vali-deploy/Managers/MenuManager.cs:71-77` (switch), `:114-116` (choices)

No hay test nuevo en este task — mismo criterio que `DeployHistoryView`: la capa Presentation/Manager basada en Spectre.Console no se testea en este repo.

- [ ] **Step 1: Crear `Presentation/EnvironmentsTreeView.cs`**

```csharp
using Spectre.Console;
using vali_deploy.Domain;

namespace vali_deploy.Presentation;

public static class EnvironmentsTreeView
{
    public static Task ShowAsync(IReadOnlyList<EnvironmentTreeNode> environments)
    {
        AnsiConsole.Clear();
        ShellRenderer.DrawHeader(new Dictionary<string, Project>(), breadcrumb: "Environments Tree");

        if (environments.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No hay entornos configurados. Andá a 'Manage Environments' para agregar uno.[/]");
            PauseForUserInput();
            return Task.CompletedTask;
        }

        foreach (var environmentNode in environments)
        {
            AnsiConsole.Write(BuildTree(environmentNode));
        }

        PauseForUserInput();
        return Task.CompletedTask;
    }

    private static Tree BuildTree(EnvironmentTreeNode environmentNode)
    {
        var label = environmentNode.Projects.Count == 0
            ? $"{Markup.Escape(environmentNode.EnvironmentName)} [grey](sin proyectos)[/]"
            : Markup.Escape(environmentNode.EnvironmentName);

        var tree = new Tree($"[yellow]{label}[/]");

        foreach (var projectNode in environmentNode.Projects)
        {
            if (projectNode.SubProjectNames.Count == 0)
            {
                tree.AddNode(Markup.Escape(projectNode.ProjectName));
                continue;
            }

            var branch = tree.AddNode($"[green]{Markup.Escape(projectNode.ProjectName)}[/]");
            foreach (var subProjectName in projectNode.SubProjectNames)
            {
                branch.AddNode(Markup.Escape(subProjectName));
            }
        }

        return tree;
    }

    private static void PauseForUserInput()
    {
        AnsiConsole.MarkupLine("[grey]Presioná una tecla para continuar...[/]");
        Console.ReadKey(true);
    }
}
```

- [ ] **Step 2: Agregar la opción al menú principal**

En `vali-deploy/Managers/MenuManager.cs`, reemplazar `GetMainMenuOption()` (líneas 109-118):

```csharp
    private static string GetMainMenuOption()
    {
        return AnsiConsole.Prompt(
            new SelectionPrompt<string>()
                .Title("What do you want to do?")
                .AddChoices("Add Project", "Remove Project", "Show Projects", "Configure Publish File Omissions",
                    "Remove Subprojects", "Manage Docker Projects", "Manage Publish Arguments", "Manage Environments",
                    "View Deploy History", "View Environments Tree", "[seagreen1]Exit[/]")
        );
    }
```

- [ ] **Step 3: Agregar el `case` en el switch de `StartAsync()`**

En `vali-deploy/Managers/MenuManager.cs`, entre el `case "View Deploy History":` y el `case "[seagreen1]Exit[/]":` (líneas 74-77):

```csharp
                case "View Deploy History":
                    await Presentation.DeployHistoryView.ShowAsync(CompositionRoot.CreateDeployHistoryRepository(), _projects.Keys.ToList());
                    break;
                case "View Environments Tree":
                    await Presentation.EnvironmentsTreeView.ShowAsync(Application.EnvironmentsTreeBuilder.Build(_repository.Load()));
                    break;
                case "[seagreen1]Exit[/]":
```

- [ ] **Step 4: Compilar y correr toda la suite**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

Run: `dotnet test vali-deploy.Tests/vali-deploy.Tests.csproj`
Expected: PASS, 140/140 (134 previos + 6 de `EnvironmentsTreeBuilderTests`).

- [ ] **Step 5: Commit**

```bash
git add vali-deploy/Presentation/EnvironmentsTreeView.cs vali-deploy/Managers/MenuManager.cs
git commit -m "feat(presentation): agregar menu View Environments Tree"
```

---

### Task 3: Verificación manual

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Correr el CLI**

Run: `dotnet run --project vali-deploy/vali-deploy.csproj`

- [ ] **Step 2: Verificar el árbol con datos reales**

Desde el menú principal, elegir "View Environments Tree". Confirmar:
- Aparece una raíz por cada entorno configurado en "Manage Environments".
- Los proyectos con un solo subproyecto aparecen como una hoja simple (nombre del proyecto, sin nivel extra).
- Los proyectos con más de un subproyecto aparecen como rama, con los subproyectos que tienen pipeline en ese entorno como hojas debajo.
- Un entorno sin proyectos matcheando muestra el indicador "(sin proyectos)".

- [ ] **Step 3: Verificar que "Local" no aparece**

Confirmar que ninguna raíz del árbol se llama "Local" — solo deben verse los entornos reales de "Manage Environments".

- [ ] **Step 4: Verificar el caso sin entornos**

Si es posible probarlo (o describir el comportamiento esperado sin poder reproducirlo): con `Manage Environments` vacío, "View Environments Tree" debería mostrar el mensaje "No hay entornos configurados..." sin crashear.

Si cualquiera de estos pasos falla, corregir el código correspondiente en el task de origen y volver a correr `dotnet test` antes de continuar.

---

## Self-review

**Cobertura de la spec:** las 5 secciones de reglas de jerarquía (raíz=entorno, colapso 1 subproyecto, no-colapso >1, entorno vacío, subproyecto huérfano) están cubiertas por los 6 tests de Task 1. El wiring en MenuManager y el entorno "Local" excluido (nunca se agrega a `config.Environments`, así que `EnvironmentsTreeBuilder.Build` nunca lo ve) están cubiertos por Task 2 y verificados manualmente en Task 3.

**Consistencia de tipos:** `EnvironmentTreeNode`/`ProjectTreeNode` (Task 1) se usan sin cambios en `EnvironmentsTreeView` (Task 2) — mismos nombres de propiedad (`EnvironmentName`, `Projects`, `ProjectName`, `SubProjectNames`) en ambos.

**Sin placeholders:** todos los steps tienen código completo, sin TBD.
