# Vista de árbol por Entorno — Design Spec

**Fecha:** 2026-07-10
**Contexto:** feature independiente, posterior al Ciclo 2 (historial de deploys consultable, ya cerrado en `main`).

## Problema

Hoy no hay forma de ver de un vistazo qué proyectos/subproyectos están configurados para cada entorno (QA, DEV, Prod, etc.). "Show Projects" en el menú principal no sirve para esto — es un wizard de selección (proyecto → subproyecto → ejecutar acción), no una vista de consulta. Para saber qué hay en QA hoy, el usuario tiene que entrar subproyecto por subproyecto y mirar sus `PipelinesByEnvironment`.

## Alcance

Pantalla nueva, de solo lectura, accesible desde el menú principal, que muestra la jerarquía **Entorno → Proyecto (→ Subproyecto si aplica)** como un árbol. No reemplaza ni modifica "Show Projects". No permite seleccionar ni ejecutar nada — se dibuja, se espera una tecla, se vuelve al menú.

Fuera de alcance: el entorno especial `"Local"` (reservado en memoria para acciones sin deploy remoto, nunca persistido) no aparece en este árbol — solo entornos reales de `config.Environments`.

## Jerarquía y reglas

- **Raíz = Entorno.** Un nodo raíz por cada `DeployEnvironment` en `config.Environments`, en el mismo orden que están en la config (sin ordenar alfabéticamente, igual que `EnvironmentMenu`).
- **Bajo cada entorno, los proyectos que tienen al menos un subproyecto con pipeline configurado para ese entorno** (`subProject.PipelinesByEnvironment.ContainsKey(environment.Name)`).
  - Si el proyecto tiene **1 solo subproyecto** en total (`project.SubProjects.Count == 1` — mismo criterio estructural que ya usa `MenuManager.ShowSubProjectsAsync` para auto-seleccionar sin preguntar), se muestra como **una sola hoja con el nombre del proyecto**, sin nivel extra de subproyecto.
  - Si el proyecto tiene **más de un subproyecto en total**, se muestra como **rama con el nombre del proyecto**, y debajo una hoja por cada subproyecto que matchea ese entorno (aunque sea solo uno de varios — la forma del árbol para un proyecto dado es consistente entre entornos, no cambia según cuántos subproyectos matcheen en cada uno).
  - Un proyecto con **0 subproyectos matcheando** ese entorno no aparece bajo ese entorno.
- **Un entorno sin ningún proyecto matcheando** se muestra igual, como raíz vacía con un indicador (`"{entorno} (sin proyectos)"`), para que el usuario sepa que el entorno existe pero nada apunta ahí todavía.
- **Un subproyecto sin pipeline configurado en ningún entorno** no aparece en ningún lado del árbol (no hay una sección "huérfanos" — está fuera de alcance).

## Arquitectura

### Domain

**`Domain/EnvironmentTreeNode.cs`** (nuevo):
```csharp
public class EnvironmentTreeNode
{
    public string EnvironmentName { get; set; } = "";
    public List<ProjectTreeNode> Projects { get; set; } = new();
}
```

**`Domain/ProjectTreeNode.cs`** (nuevo):
```csharp
public class ProjectTreeNode
{
    public string ProjectName { get; set; } = "";
    public List<string> SubProjectNames { get; set; } = new();
}
```

`SubProjectNames` vacío es la señal de "colapsado" (proyecto con un solo subproyecto) — la capa de presentación lo interpreta así, no hay un flag booleano separado.

### Application

**`Application/EnvironmentsTreeBuilder.cs`** (nuevo) — lógica pura, sin I/O, sin dependencia de Spectre.Console:

```csharp
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

Nota: `Project` (confirmado en `Domain/Project.cs`) no tiene una propiedad `Name` propia — el nombre vive únicamente como key del `Dictionary<string, Project>` en `DeployConfig.Projects`. El código de arriba ya refleja esto: `BuildEnvironmentNode` itera el diccionario completo (no `.Values`) y pasa `kvp.Key` como nombre a `BuildProjectNode`.

### Presentation

**`Presentation/EnvironmentsTreeView.cs`** (nuevo, clase estática — mismo patrón que `DeployHistoryView`/`EnvironmentMenu`):

```csharp
public static Task ShowAsync(IReadOnlyList<EnvironmentTreeNode> environments)
```

- Si `environments` está vacío (no hay ningún entorno configurado) → mensaje `"No hay entornos configurados. Andá a 'Manage Environments' para agregar uno."`, pausa, vuelve.
- Si no, por cada `EnvironmentTreeNode`: crea un `Tree` raíz (`"{EnvironmentName} [grey](sin proyectos)[/]"` si `Projects.Count == 0`, si no solo `"{EnvironmentName}"`).
  - Por cada `ProjectTreeNode`: si `SubProjectNames.Count == 0` → `tree.AddNode(ProjectName)` (hoja). Si no → `var branch = tree.AddNode(ProjectName)`, y por cada nombre en `SubProjectNames` → `branch.AddNode(nombre)`.
  - `AnsiConsole.Write(tree)` por cada entorno, en orden.
- Pausa con tecla al final, vuelve.

### MenuManager

- Nueva opción `"View Environments Tree"` en `GetMainMenuOption()`, junto a `"View Deploy History"`.
- Nuevo `case "View Environments Tree":` en el switch de `StartAsync()`, que hace `var config = _repository.Load();` y llama `await Presentation.EnvironmentsTreeView.ShowAsync(Application.EnvironmentsTreeBuilder.Build(config));`.

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| `config.Environments` vacío | Mensaje "No hay entornos configurados..." en la vista, sin excepción. |
| Entorno sin proyectos matcheando | Raíz vacía con indicador `(sin proyectos)`, no se omite. |
| Proyecto sin subproyectos (lista vacía) | `BuildProjectNode` devuelve `null` (ningún subproyecto puede matchear), el proyecto no aparece — comportamiento consistente con "0 subproyectos matcheando". |

## Testing

**`EnvironmentsTreeBuilderTests.cs`** (nuevo):
- Entorno sin proyectos matcheando → `EnvironmentTreeNode.Projects` vacío.
- Proyecto con 1 subproyecto matcheando → `ProjectTreeNode.SubProjectNames` vacío (colapsado).
- Proyecto con >1 subproyectos pero solo 1 matcheando el entorno → `SubProjectNames` con ese único nombre (no colapsa).
- Proyecto con >1 subproyectos y varios matcheando → `SubProjectNames` con todos los que matchean, en el orden de `project.SubProjects`.
- Subproyecto sin pipeline en ningún entorno → no aparece en el `ProjectTreeNode` de ningún entorno.
- Dos entornos distintos con distintos proyectos matcheando cada uno → cada `EnvironmentTreeNode` solo lista lo que le corresponde (no hay fuga entre entornos).

Sin tests para `EnvironmentsTreeView` ni el wiring en `MenuManager` — mismo criterio que el Ciclo 2 (capa Presentation/Manager basada en Spectre.Console no se testea en este repo).

## Decisiones registradas

- El entorno `"Local"` (en memoria, no persistido) queda fuera del árbol — no es un entorno de negocio real.
- Colapso de subproyecto a nivel de proyecto es estructural (`project.SubProjects.Count == 1`), no depende de cuántos matchean el entorno actual — la forma del árbol de un proyecto es consistente entre todos los entornos.
- Subproyectos sin ningún pipeline configurado no tienen una sección propia ("huérfanos") — quedan simplemente invisibles en esta vista, que es sobre "qué hay desplegado dónde", no un inventario completo de subproyectos.
- Vista puramente de lectura — no hay selección ni acción disponible desde acá, a diferencia de "Show Projects".
