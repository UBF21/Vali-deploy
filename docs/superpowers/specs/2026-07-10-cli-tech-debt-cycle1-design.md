# Cierre de deuda técnica — Ciclo 1

**Fecha:** 2026-07-10
**Estado:** Aprobado, pendiente de plan de implementación

## Contexto

Vali-Deploy tiene dos sistemas de ejecución conviviendo: el pipeline nuevo (`IStepExecutor`/`PipelineRunner`, usado por `PipelinesByEnvironment`), que sí verifica exit code entre pasos, y un flujo ad-hoc legacy (`CommandExecutor.cs`, invocado desde `ExecuteCommandSubProject` en `MenuManager.cs`) para "Generate Microsoft publish" / "Docker Build" / "Docker Run" / "Push to Docker Hub", que no lo verifica. Además hay deuda puntual documentada en `CLAUDE.md`: credenciales Docker Hub en texto plano, updater sin verificación de integridad, y `ZipPublishExecutor`/`OmitFiles` a medio implementar.

Este ciclo agrupa 5 items relacionados porque todos son correcciones/cierres de features ya existentes, sin necesidad de diseño de producto nuevo. Es el primero de 4 ciclos de un roadmap más amplio (los otros 3 — historial de deploys, dry-run, rollback — son features nuevas con spec propio).

## 1. Migración del flujo legacy al pipeline

**Problema:** `CommandExecutor.RunCommandsAsync`/`ExecuteDockerCommandAsync` no verifican exit code entre pasos — un `dotnet build` fallido no detiene el `dotnet publish` siguiente. El pipeline nuevo ya resuelve esto correctamente vía `PipelineRunner.ExecuteWithRetryAsync`.

**Decisión:** en vez de parchear el legacy, se retira y todo pasa por el pipeline.

- Se eliminan del menú ad-hoc de `ExecuteCommandSubProject`: **"Generate Microsoft publish"**, **"Docker Build"**, **"Push to Docker Hub"**.
- **"Docker Run"** es interactivo (`docker run -it --rm`, deja al usuario dentro del contenedor) y no encaja en `IProcessRunner` (captura StdOut/StdErr como texto para el log). Se agrega `StepType.DockerRun` + un executor nuevo que, a diferencia de todos los demás, **no usa `IProcessRunner`** — hereda la consola del proceso padre directamente. Es la única excepción intencional al modelo "todo step es logueable" del pipeline; documentar esto en el código con un comentario explicando por qué.
- Si el `SubProject` no tiene pipeline para el environment activo, se autogenera on-the-fly reusando `PipelineTemplateFactory` (mismo mecanismo que ya dispara "Edit Pipeline" cuando no existe pipeline para un environment). El template autogenerado para acciones locales **no incluye `GitCheckout`** — construye desde el working copy en disco tal cual está, igual que el comportamiento actual del legacy. Esto es una tercera variante de template (además de `CreateDockerComposeTemplate`/`CreatePublishZipTemplate`), ej. `CreateLocalDockerTemplate`.
- Se introduce un `DeployEnvironment` reservado de nombre `"Local"` (`Server = null`), creado automáticamente (sin pedir confirmación) la primera vez que una acción local lo necesita. Se filtra explícitamente de la lista que muestra `EnvironmentMenu.StartAsync` (`config.Environments.Select(e => e.Name)`) — el usuario nunca lo ve, no puede editarlo ni borrarlo desde "Manage Environments". Si en el futuro un `SubProject` con pipeline normal necesita listar sus propios environments disponibles (ej. `PipelineEditorMenu`), ese filtro debe aplicarse ahí también.
- `CommandExecutor.cs` queda sin callers tras esto → se elimina completo (mismo patrón que `ChartManager` en el TUI shell Fase 1).
- Toda ejecución (Build/Push/Publish local o remoto) pasa a mostrarse vía `PipelineExecutionView`, heredando el header persistente ya cableado en el addendum anterior.

## 2. Registry Docker generalizado

**Problema:** `SubProject.DockerHubUser` es texto plano y asume Docker Hub específicamente. `DockerRegistryTokenEnvVar` ya existe en el dominio como reemplazo planeado pero no está cableado a ningún flujo real, y `RegistryTag` se genera vacío en `PipelineTemplateFactory` (hay que completarlo a mano en `PipelineEditorMenu`).

**Decisión:** generalizar a cualquier registry (Docker Hub, ACR, GHCR, privado), no solo Docker Hub.

- Nuevo value object `DockerRegistry { string Host, string Username, string? TokenEnvVar }` en `Domain/`. `Host` vacío = Docker Hub (comportamiento actual).
- `SubProject.DockerHubUser` (string) se reemplaza por `SubProject.DockerRegistry` (objeto). El campo suelto `DockerRegistryTokenEnvVar` se absorbe dentro de `DockerRegistry.TokenEnvVar` y se elimina de `SubProject`.
- **Migración automática de datos:** `ProjectRepository.Load()` detecta `DockerHubUser` presente en el JSON (formato viejo) y lo convierte en memoria a `DockerRegistry { Host = "", Username = DockerHubUser, TokenEnvVar = null }`. Se persiste en el próximo `Save()` — sin pasos manuales para el usuario. El campo viejo desaparece del JSON después de la primera escritura.
- `RegistryTag` autogenerado (en vez de pedirlo vacío): `{Host}/{Username}/{imagen}:{tag}` si `Host` está seteado, `{Username}/{imagen}:{tag}` si `Host` está vacío (Docker Hub, formato actual).
- `DockerPushExecutor` gana un paso previo a `docker tag`/`docker push`: `docker login {Host} -u {Username} --password-stdin`, pasando el token resuelto vía `EnvVarSecretResolver` (falla explícito si la env var no está seteada, mismo comportamiento que hoy con `PassphraseEnvVar`). Si `Host` está vacío, `docker login` sin host apunta a Docker Hub por defecto (comportamiento nativo de Docker).

## 3. Integridad del updater

**Problema:** `UpdaterManager` descarga un binario desde una URL listada en un JSON hosteado a mano en Netlify (`Constants.UrlVersion`) y lo reemplaza sin verificar nada — ni que la descarga esté completa, ni que el archivo sea el esperado.

**Decisión:** mover la fuente de verdad a GitHub Releases (el repo ya vive ahí) y agregar checksum.

- `UpdaterManager.GetUpdateInfoAsync` deja de consultar `Constants.UrlVersion` y pasa a consultar `https://api.github.com/repos/UBF21/Vali-deploy/releases/latest` (JSON nativo de GitHub: `tag_name`, `assets[].browser_download_url`, etc.).
- Cada release debe incluir un asset `SHA256SUMS.txt` (una línea por RID, formato estándar `<hash>  <nombre-archivo>`). Esto es un paso del **flujo de release** (fuera del código del CLI) — se documenta como script/checklist manual, no algo que el CLI genere solo.
- El updater descarga `SHA256SUMS.txt` junto con el binario, calcula el SHA256 del binario descargado, y compara contra la línea correspondiente al RID actual. Si no coincide, aborta el reemplazo y muestra un error — no borra el binario actual en ese caso.
- `Models/UpdateInfo.cs` se adapta a la forma de la respuesta de GitHub Releases (o se mapea a un modelo intermedio si la forma nativa de GitHub es muy distinta a como se usa hoy en `UpdaterManager`/`Program.cs`).

## 4. `ZipPublishExecutor` completo

**Problema:** el step `ZipPublishOutput` hoy solo corre `dotnet clean/build/publish` — no comprime nada a `.zip` pese al nombre, y `SubProject.OmitFiles` no se aplica en ningún lado del pipeline nuevo (sí se aplicaba, indirectamente, en el flujo legacy vía `CommandExecutor`).

**Decisión:**

- Después del `dotnet publish`, `ZipPublishExecutor` comprime el contenido de la carpeta de publish a `{publishPath}/../{subProjectName}-{timestamp}.zip` usando `System.IO.Compression` (BCL, sin dependencias nuevas).
- La carpeta de publish sin comprimir **no se toca ni se borra** — queda disponible al lado del `.zip`, por si algún flujo (ej. Docker build apuntando a esa carpeta) depende de que exista descomprimida.
- `SubProject.OmitFiles` se aplica **solo al armar el `.zip`** (se excluyen esos paths del comprimido) — no se borran físicamente de la carpeta de publish.
- Solo corre esta compresión si `SubProject.ZipPublishOutput == true` (el flag ya existe en el dominio, default `true`).

## Fuera de alcance de este ciclo

- Limpiar los 720 archivos de `bin/`/`obj/` commiteados al repo — bloqueado hoy por un hook local (`destructive-guard.sh`) que impide el `git rm -r --cached` automático. Requiere intervención manual del usuario, se trata aparte.
- Historial de deploys consultable, dry-run/plan mode, rollback — ciclos 2, 3 y 4 de este roadmap, specs propios.

## Riesgos / consideraciones para el plan de implementación

- El "Local" `DeployEnvironment` reservado necesita que `ProjectRepository`/`EnvironmentMenu` lo traten como caso especial (no debería aparecer como borrable ni editable desde "Manage Environments", o si aparece, debe quedar claro que es interno).
- La migración de `DockerHubUser` → `DockerRegistry` debe cubrirse con un test en `vali-deploy.Tests` (carga de un JSON con el campo viejo → verificar que el objeto en memoria queda bien formado) — el proyecto sí tiene tests para `Infrastructure`/`Domain`, a diferencia de `Presentation`.
- El cambio de `UpdaterManager` a la API de GitHub es lo único de este ciclo que depende de un paso externo al código (subir `SHA256SUMS.txt` en cada release) — el plan debe dejarlo documentado como checklist, no como algo que quede "roto" si no se hace.
