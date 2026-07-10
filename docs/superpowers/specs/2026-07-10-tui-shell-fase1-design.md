# Design Spec — TUI Shell Persistente, Fase 1 (Vali-Deploy)

**Fecha**: 2026-07-10
**Estado**: Aprobado, pendiente de plan de implementación

## Contexto

Vali-Deploy es un CLI .NET 7 (Spectre.Console) cuyo menú principal (`Managers/MenuManager.cs`) hoy hace `AnsiConsole.Clear()` y redibuja todo (Rule + FigletText + BarChart + `SelectionPrompt`) en cada vuelta del loop. No hay header fijo, ni splash de arranque propio, ni una identidad visual consistente entre el menú raíz y los submenús (`Presentation/EnvironmentMenu.cs`, `Presentation/PipelineEditorMenu.cs`, y los flujos Docker que viven dentro de `MenuManager`).

Objetivo de esta Fase 1: introducir un shell visual coherente — splash de arranque + header persistente + paleta de color propia — reutilizable desde el menú raíz y desde los submenús existentes, sin todavía resolver el redibujado sin parpadeo (eso es explícitamente una fase futura).

## Decisiones de alcance (de la fase de clarificación)

- **Reemplaza** el loop raíz de `MenuManager.StartAsync()` / `DisplayMainMenu()`.
- **El header persistente también se muestra dentro de los submenús existentes** (`EnvironmentMenu`, `PipelineEditorMenu`, los flujos Docker de `MenuManager`) — no se limita al menú raíz. Esas pantallas se adaptan en esta fase para invocar el header compartido en vez de dibujar su propio encabezado (o ninguno).
- **Navegación**: se mantiene `SelectionPrompt<string>` de Spectre.Console, que ya soporta ↑↓ + Enter de forma nativa — no se introduce un loop de teclado custom ni se cambia la librería de UI.
- **Sin atajo de teclado global para salir**: el pie de ayuda del shell dice `↑ ↓ navegar · Enter seleccionar`. Salir sigue siendo la opción "Exit" dentro de la lista, como hoy. Un atajo tipo `Q` global requeriría interceptar teclado por fuera de `SelectionPrompt` — complejidad que no se justifica en esta fase.
- **Sin concepto de "proyecto/entorno activo" de sesión**: el dominio actual no tiene esa noción (cada acción elige un proyecto puntualmente). El header muestra siempre un resumen global (cantidad de proyectos/subproyectos, versión); al entrar a un submenú de un proyecto puntual, se agrega el nombre de ese proyecto como breadcrumb mientras dura esa pantalla, y desaparece al volver al menú raíz.
- **Redibujado sin parpadeo real (Spectre `Live` + `Layout`) queda fuera de alcance** — se elige persistencia semántica (ver "Enfoque técnico") en vez de persistencia visual real, como base de menor riesgo para esta fase.
- **Responsive / redimensionable por tamaño de terminal**: ningún renderable del shell usa anchos fijos en caracteres. Header, splash y listas se construyen con las primitivas de Spectre que ya se auto-ajustan al ancho de consola (`Rule`, `Panel` sin `Width` explícito, `Grid`/`Table` con columnas relativas) — igual que ya hace el código actual (`AnsiConsole.Write(new Rule())` no fija ancho). Como el shell redibuja completo en cada vuelta del loop (ver "Enfoque técnico"), cada pantalla se re-mide contra el ancho de consola vigente en ese momento; no hay contrato de refresco en vivo mientras el usuario está parado esperando input en un `SelectionPrompt` — un resize de la ventana se refleja recién en el próximo redibujado (siguiente pantalla o vuelta de loop), igual que el comportamiento actual de Spectre.Console.

## Enfoque técnico: persistencia semántica

Se descarta `AnsiConsole.Live()` + `Layout` (persistencia visual real, sin parpadeo) para esta fase por complejidad: requeriría manejar teclas fuera de `SelectionPrompt` en varias pantallas y adaptar `EnvironmentMenu`/`PipelineEditorMenu` a esa API.

En su lugar, `ShellRenderer` centraliza el dibujo de header en un solo lugar reutilizable. Cada pantalla (root menu, submenús) sigue llamando `AnsiConsole.Clear()` y redibujando completo — pero delegando el header a `ShellRenderer` en vez de tener lógica de dibujo duplicada/ausente, lo que da consistencia visual entre pantallas sin resolver el flicker. Queda documentado como candidato de Fase 2.

## Componentes nuevos

```
vali-deploy/Presentation/
├── ShellRenderer.cs   (nuevo)
└── SplashScreen.cs    (nuevo)
```

### `ShellRenderer`

```csharp
public static class ShellRenderer
{
    // Dibuja la franja de header: marca + versión + resumen global
    // (proyectos/subproyectos), y opcionalmente un breadcrumb.
    // No hace Clear() — lo hace el caller antes, para no acoplar
    // ShellRenderer a cuándo debe limpiarse la pantalla.
    public static void DrawHeader(string? breadcrumb = null);
}
```

- Reemplaza el cuerpo actual de `MenuManager.DisplayMainMenu()` (Rule + FigletText + Grid con BarChart) por una llamada a `ShellRenderer.DrawHeader()`.
- `EnvironmentMenu.StartAsync`, `PipelineEditorMenu` y las pantallas Docker de `MenuManager` agregan `AnsiConsole.Clear(); ShellRenderer.DrawHeader(breadcrumb: nombreDelProyectoOPantalla);` al inicio de su loop de renderizado, antes de su contenido específico.
- Usa `Panel`/`Rule`/`Grid` sin anchos fijos (ver "Responsive" arriba) — se re-mide contra `AnsiConsole.Profile.Width` en cada llamada, igual que el resto del código Spectre existente.
- Colores tomados de una paleta Forest centralizada (ver abajo), no hardcodeados por pantalla.

### `SplashScreen`

```csharp
public static class SplashScreen
{
    // Se muestra una vez al arrancar: FigletText("Vali-Deploy") centrado
    // + panel de resumen (proyectos, subproyectos, último deploy si existe)
    // + espera una tecla antes de continuar.
    public static void ShowAndWait(DeployConfig config);
}
```

- Se invoca desde `Program.cs`, después del check de `UpdaterManager` (si no hay actualización pendiente o el usuario la rechaza) y antes de `MenuManager.StartAsync()`.
- Métricas del resumen: `config.Projects.Count`, suma de `SubProjects` de todos los proyectos. "Último deploy" es opcional — si no hay dato disponible en el modelo actual, esa línea del panel se omite (no se introduce tracking nuevo de "último deploy" en esta fase; es contenido best-effort con lo que ya existe).
- El panel usa `Panel` sin ancho fijo, centrado con `Align`, siguiendo la misma regla de responsive del header.

## Paleta — Forest

Verdes desaturados sobre fondo casi negro, en vez de verde brillante puro ("Matrix"):

- Texto base: gris-verde suave (aprox. `Color.Grey78` / equivalente desaturado — se ajusta al implementar contra los valores reales de `Spectre.Console.Color`, no hay una constante "Forest" nativa en Spectre)
- Marca/títulos/selección activa: verde suave (aprox. `Color.SeaGreen1`/`Color.PaleGreen1` — a validar visualmente en terminal real durante implementación, distinto de `Color.Chartreuse3_1`/`Color.Lime` que usa el código actual para el ítem "Exit")
- Error: rojo apagado (no rojo puro)
- Warning: ámbar apagado (no amarillo puro)
- Éxito puntual (ej. paso de pipeline OK): mismo verde de marca

Se centraliza en una clase estática (`Utils/Constants.cs` o un nuevo `Presentation/ShellPalette.cs`) para que `ShellRenderer`, `SplashScreen` y — donde tenga sentido — `PipelineExecutionView` usen los mismos valores en vez de colores sueltos por archivo.

## Header — layout

Franja compacta de una sección (evolución directa del header actual, no una reescritura): marca + versión a la izquierda, resumen global (o breadcrumb si aplica) a la derecha, separador (`Rule`), seguido de la lista de opciones navegable (`SelectionPrompt`) de esa pantalla. Reemplaza el patrón actual de `Rule` + `Rule` + `FigletText` + `Rule` + `Grid` (4-5 elementos sueltos en `DisplayMainMenu`) por una sola llamada a `ShellRenderer.DrawHeader()`.

## Fuera de alcance (Fase 1)

- Redibujado sin parpadeo real (`AnsiConsole.Live()` + `Layout`)
- Atajo de teclado global (`Q` para salir desde cualquier pantalla)
- Concepto de "proyecto/entorno activo" persistente en sesión
- Navegación horizontal por tabs (se evaluó en brainstorming y se descartó a favor de lista vertical, que ya cubre `SelectionPrompt`)
- Tracking de "último deploy" como dato nuevo — el splash lo muestra solo si ya existe en el modelo actual

## Testing

El repo no tiene tests (confirmado en `CLAUDE.md` del proyecto). Verificación manual: `dotnet run --project vali-deploy/vali-deploy.csproj`, recorrer splash → menú raíz → al menos un submenú migrado (`Manage Environments`), y repetir con la ventana de terminal en al menos dos anchos distintos (ej. 80 columnas y 120+ columnas) para confirmar que header y splash no rompen layout ni truncan contenido de forma abrupta.

## Deuda técnica pendiente (fuera de este spec)

- Persistencia visual real sin flicker (`Live`+`Layout`) — candidato de Fase 2, no se aborda acá
- Los submenús migrados (`EnvironmentMenu`, `PipelineEditorMenu`, flujos Docker) mantienen su lógica interna intacta — solo se les agrega la llamada a `ShellRenderer.DrawHeader()`; cualquier refactor más profundo de esas pantallas queda fuera de este spec
