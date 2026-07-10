# Selector de idioma (Inglés/Español) para las opciones de menú — Design Spec

**Fecha:** 2026-07-10
**Contexto:** feature independiente, posterior a los ciclos "historial de deploys" y "árbol por entorno", ambos ya cerrados en `main`.

## Problema

El CLI mezcla idiomas de forma inconsistente: el grueso de `MenuManager.cs` (menú principal y submenús de Docker/publish/proyectos) está hardcodeado en inglés, mientras que 4 pantallas más nuevas (`EnvironmentMenu`, `PipelineEditorMenu`, `DeployHistoryView`, `EnvironmentsTreeView`) y una línea puntual dentro de `MenuManager.cs` (selección de entorno a desplegar) están hardcodeadas en español. No existe ningún selector de idioma ni mecanismo de traducción.

## Alcance

Se agrega un selector de idioma (Inglés/Español) que traduce **únicamente** los `Title`/`AddChoices` estáticos de los `SelectionPrompt<string>`/`MultiSelectionPrompt<string>` que hoy están hardcodeados en inglés dentro de `vali-deploy/Managers/MenuManager.cs`. De los 23 `SelectionPrompt`/`MultiSelectionPrompt` que existen hoy en ese archivo, 22 están en inglés y se modifican (inventariados abajo); el restante (selección de entorno a desplegar, línea 925) ya está en español y queda fuera de alcance. Se suma además un prompt nuevo para el propio selector de idioma (sin traducir, bilingüe fijo). El idioma se persiste en `deploy_config.json` y se aplica a toda la sesión desde que se carga la config.

**Explícitamente fuera de alcance** (quedan siempre en español, sin importar el idioma elegido):
- Los 4 archivos ya hardcodeados en español: `Presentation/EnvironmentMenu.cs`, `Presentation/PipelineEditorMenu.cs`, `Presentation/DeployHistoryView.cs`, `Presentation/EnvironmentsTreeView.cs`.
- La selección de entorno a desplegar dentro de `MenuManager.ExecuteSubProjectPipelineAsync` ("Elegí el entorno a desplegar:"), ya hardcodeada en español.
- Cualquier mensaje de éxito/error/status (`AnsiConsole.MarkupLine(...)`) — solo se traducen `Title`/`AddChoices`, no el resto del output.
- Nombres dinámicos (proyectos, subproyectos, entornos, archivos, argumentos) — nunca se traducen, solo pasan a través del mecanismo sin cambios (ver Arquitectura).

## Arquitectura

### Persistencia

**`Domain/DeployConfig.cs`** — nuevo campo:
```csharp
public string Language { get; set; } = "en";
```
Valores válidos: `"en"` (default, sin cambio de comportamiento para instalaciones existentes) o `"es"`.

### Mecanismo de traducción

**`Presentation/Translator.cs`** (nuevo, estático):

```csharp
public static class Translator
{
    private static string _currentLanguage = "en";

    private static readonly Dictionary<string, string> EnToEs = new()
    {
        // ver diccionario completo más abajo
    };

    public static void SetLanguage(string language) => _currentLanguage = language;

    public static string T(string english) =>
        _currentLanguage == "es" && EnToEs.TryGetValue(english, out var translated) ? translated : english;
}
```

- `T(string)` es **seguro de aplicar a cualquier string**, incluidos nombres dinámicos (proyectos, subproyectos, archivos) — si la clave no está en el diccionario, devuelve el string sin cambios. Esto es lo que permite aplicar `.UseConverter(Translator.T)` a un `SelectionPrompt<string>` que mezcla opciones estáticas ("Add Project") con datos dinámicos (nombres de proyecto) sin tener que separar ambos casos.
- `.UseConverter(Translator.T)` traduce cada opción **al mostrarla**, pero el valor que `AnsiConsole.Prompt` devuelve al código (y que los `switch`/`if` comparan) sigue siendo el string en inglés original — cero cambios en la lógica de routing existente de `MenuManager.cs`.
- `Title` no pasa por `UseConverter` (ese método solo afecta cómo se muestran las opciones/choices) — cada `.Title("...")` se envuelve directo: `.Title(Translator.T("..."))`.
- Los 4 títulos con datos interpolados (`$"Select a subproject for project '{projectName}'"`, etc.) se guardan en el diccionario como plantilla con `{0}` y se aplican con `string.Format`: `.Title(string.Format(Translator.T("Select a subproject for project '{0}'"), projectName))`.

### Carga y cambio de idioma

- **Al arrancar** (`MenuManager.StartAsync`): después de cargar `_projects = config.Projects`, se llama `Presentation.Translator.SetLanguage(config.Language)`.
- **Nuevo menú "Language / Idioma"**: entrada en el menú principal. Sus dos opciones (`"English"`, `"Español"`) son labels bilingües fijos que **no** pasan por `Translator.T` (son los nombres de los idiomas en sí, no texto a traducir). Al elegir uno: `config.Language = "en"` o `"es"`, `_repository.Save(config)`, `Translator.SetLanguage(config.Language)` — efecto inmediato en la sesión actual, sin reiniciar el CLI.

## Diccionario completo (Inglés → Español)

Claves con `{0}` son plantillas para `string.Format`. Las etiquetas con markup Spectre (`[seagreen1]...[/]`) se guardan con el markup incluido, ya que la clave debe matchear el literal exacto del código.

### Menú principal

| Inglés (clave) | Español |
|---|---|
| `What do you want to do?` | `¿Qué querés hacer?` |
| `Add Project` | `Agregar Proyecto` |
| `Remove Project` | `Eliminar Proyecto` |
| `Show Projects` | `Ver Proyectos` |
| `Configure Publish File Omissions` | `Configurar Archivos Omitidos de Publish` |
| `Remove Subprojects` | `Eliminar Subproyectos` |
| `Manage Docker Projects` | `Gestionar Proyectos Docker` |
| `Manage Publish Arguments` | `Gestionar Argumentos de Publish` |
| `Manage Environments` | `Gestionar Entornos` |
| `View Deploy History` | `Ver Historial de Deploys` |
| `View Environments Tree` | `Ver Árbol de Entornos` |
| `[seagreen1]Exit[/]` | `[seagreen1]Salir[/]` |

### Navegación (reutilizados en varios menús)

| Inglés (clave) | Español |
|---|---|
| `[seagreen1]Back to Main Menu[/]` | `[seagreen1]Volver al Menú Principal[/]` |
| `[seagreen1]Back to Projects Menu[/]` | `[seagreen1]Volver al Menú de Proyectos[/]` |
| `[seagreen1]Back to Projects[/]` | `[seagreen1]Volver a Proyectos[/]` |
| `[seagreen1]Back to Subprojects[/]` | `[seagreen1]Volver a Subproyectos[/]` |
| `[seagreen1]Back[/]` | `[seagreen1]Volver[/]` |
| `[seagreen1]Cancel[/]` | `[seagreen1]Cancelar[/]` |

### Remover subproyectos

| Inglés (clave) | Español |
|---|---|
| `Select projects to remove (use spacebar to select, Enter to confirm)` | `Elegí los proyectos a eliminar (barra espaciadora para seleccionar, Enter para confirmar)` |
| `Select a project to remove subprojects from` | `Elegí un proyecto para eliminarle subproyectos` |
| `Select subprojects to remove from project '{0}' (use spacebar to select, Enter to confirm)` | `Elegí los subproyectos a eliminar del proyecto '{0}' (barra espaciadora para seleccionar, Enter para confirmar)` |

### Show Projects

| Inglés (clave) | Español |
|---|---|
| `Select a project` | `Elegí un proyecto` |
| `Select a subproject for project '{0}'` | `Elegí un subproyecto del proyecto '{0}'` |

### Omitir archivos de publish

| Inglés (clave) | Español |
|---|---|
| `Select a project to configure publish file omissions` | `Elegí un proyecto para configurar archivos omitidos de publish` |
| `Select a subproject for project '{0}' to manage files to omit` | `Elegí un subproyecto del proyecto '{0}' para gestionar archivos a omitir` |
| `Add file to omit` | `Agregar archivo a omitir` |
| `Remove file from omit list` | `Quitar archivo de la lista de omitidos` |
| `Select files to remove 'from' omit list (use spacebar to select, Enter to confirm)` | `Elegí los archivos a quitar de la lista de omitidos (barra espaciadora para seleccionar, Enter para confirmar)` |

### Ejecutar comando de subproyecto

| Inglés (clave) | Español |
|---|---|
| `What do you want to do with subproject '{0}'?` | `¿Qué querés hacer con el subproyecto '{0}'?` |
| `Generate Microsoft publish` | `Generar publish de Microsoft` |
| `Edit Pipeline` | `Editar Pipeline` |
| `Push to registry` | `Subir al registry` |

`Docker Build` y `Docker Run` (de `_dockerActions`, línea 14) se dejan **sin traducir** — son nombres de subcomandos reales de Docker (`docker build`, `docker run`), no descripciones; traducirlos generaría confusión sobre qué comando ejecutan.

### Proyectos/subproyectos Docker

| Inglés (clave) | Español |
|---|---|
| `Select a project with Docker subprojects` | `Elegí un proyecto con subproyectos Docker` |
| `Select a Docker subproject in '{0}'` | `Elegí un subproyecto Docker en '{0}'` |
| `Add Docker Arg` | `Agregar Argumento Docker` |
| `Remove Docker Args` | `Quitar Argumentos Docker` |
| `Select argument type:` | `Elegí el tipo de argumento:` |
| `Build Arg` | `Argumento de Build` |
| `Run Arg` | `Argumento de Run` |
| `Select argument type to remove:` | `Elegí el tipo de argumento a quitar:` |
| `Build Args` | `Argumentos de Build` |
| `Run Args` | `Argumentos de Run` |
| `Select build args to remove (use spacebar to select, Enter to confirm)` | `Elegí los argumentos de build a quitar (barra espaciadora para seleccionar, Enter para confirmar)` |
| `Select run args to remove (use spacebar to select, Enter to confirm)` | `Elegí los argumentos de run a quitar (barra espaciadora para seleccionar, Enter para confirmar)` |

### Argumentos de publish

| Inglés (clave) | Español |
|---|---|
| `Select a project to manage publish arguments` | `Elegí un proyecto para gestionar argumentos de publish` |
| `Select a subproject in '{0}' to manage publish arguments` | `Elegí un subproyecto en '{0}' para gestionar argumentos de publish` |
| `Add Publish Arg` | `Agregar Argumento de Publish` |
| `Remove Publish Args` | `Quitar Argumentos de Publish` |
| `Toggle Zip Publish Output` | `Alternar Salida Zip de Publish` |
| `Select publish args to remove (use space-bar to select, Enter to confirm)` | `Elegí los argumentos de publish a quitar (barra espaciadora para seleccionar, Enter para confirmar)` |

Nota: `What do you want to do?` aparece 4 veces en el código (menú principal, omit-files, Docker args, publish args) con el mismo texto exacto — una sola entrada en el diccionario cubre las 4.

## Inventario de los 22 prompts a modificar (todos en `vali-deploy/Managers/MenuManager.cs`) + 1 nuevo

1. `GetMainMenuOption` — menú principal
2. Selección múltiple de proyectos a eliminar
3. Selección de proyecto (para eliminar subproyectos)
4. Selección múltiple de subproyectos a eliminar
5. `PromptProjectSelection` (Show Projects)
6. `PromptSubProjectSelection` (Show Projects)
7. Selección de proyecto (config. de omit-files)
8. Selección de subproyecto (config. de omit-files)
9. `PromptFileManagementAction`
10. Selección múltiple de archivos a quitar del omit-list
11. Menú de acción de `ExecuteCommandSubProject`
12. Selección de proyecto con subproyectos Docker
13. Selección de subproyecto Docker
14. `PromptDockerArgsAction`
15. Selección de tipo de argumento Docker (agregar)
16. Selección de tipo de argumento Docker (quitar)
17. Selección múltiple de build args a quitar
18. Selección múltiple de run args a quitar
19. Selección de proyecto (publish args)
20. Selección de subproyecto (publish args)
21. `PromptPublishArgsAction`
22. Selección múltiple de publish args a quitar

**Prompt nuevo (no es una modificación, se crea desde cero):**

23. Menú "Language / Idioma" — no traducido, bilingüe fijo (ver "Carga y cambio de idioma")

## Manejo de errores

| Caso | Comportamiento |
|---|---|
| `deploy_config.json` de una instalación existente, sin campo `Language` | `System.Text.Json` deserializa el campo faltante al default de la propiedad (`"en"`) — comportamiento idéntico al actual, sin migración necesaria. |
| Clave no encontrada en el diccionario (nombre dinámico, o string fuera de alcance) | `Translator.T` devuelve el string sin cambios — nunca lanza excepción, nunca muestra texto vacío. |
| Valor de `Language` inválido en el JSON (corrupción manual) | Cualquier valor distinto de `"es"` se trata como inglés (`_currentLanguage == "es"` es la única condición que activa traducción) — no hay excepción, degrada a inglés silenciosamente. |

## Testing

**`TranslatorTests.cs`** (nuevo):
- `T` devuelve el texto en inglés sin cambios cuando el idioma actual es `"en"` (default).
- `T` devuelve la traducción cuando el idioma es `"es"` y la clave existe en el diccionario.
- `T` devuelve el texto original sin cambios cuando el idioma es `"es"` pero la clave no existe (ej. un nombre de proyecto dinámico) — no lanza excepción.
- `SetLanguage` cambia el comportamiento de llamadas posteriores a `T` (probar `en` → `es` → `en` en la misma prueba, ya que `_currentLanguage` es estado estático compartido).

Sin tests para el wiring en `MenuManager.cs` (agregar `.UseConverter`/envolver `.Title`) — mismo criterio ya establecido en los ciclos anteriores de este repo: la capa Presentation/Manager basada en prompts de Spectre.Console no se testea directamente, solo la lógica pura que la alimenta (acá, `Translator`).

## Decisiones registradas

- Diccionario **hardcodeado en código** (`Dictionary<string,string>` estático), no un archivo `.resx`/JSON externo — proporcional a 2 idiomas y ~50 entradas fijas; introducir un sistema de resource files sería sobre-ingeniería para este alcance.
- `Docker Build`/`Docker Run` quedan sin traducir por ser nombres de subcomandos reales, no descripciones — el resto de las opciones estáticas sí se traduce.
- El toggle es **asimétrico**: solo normaliza lo que hoy está en inglés. Las 4 pantallas ya hardcodeadas en español (y la línea de selección de entorno dentro de `MenuManager.cs`) no cambian con el selector — decisión explícita del usuario para no tocar código recién shippeado.
- Cambio de idioma tiene efecto inmediato en la sesión activa (no requiere reiniciar el CLI) porque `Translator` es estado en memoria, actualizado en el mismo momento en que se persiste a `deploy_config.json`.
