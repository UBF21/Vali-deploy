# Design Spec — Despliegue remoto SSH + pipeline configurable (Vali-Deploy)

**Fecha**: 2026-07-08
**Estado**: Aprobado, pendiente de plan de implementación

## Contexto

Vali-Deploy es un CLI .NET 7 (Spectre.Console) que hoy automatiza build/publish/Docker de proyectos .NET, todo en la máquina local. Auditoría completa en `C:\Users\fmontenegro\Documents\segundo-cerebro-ingenieria\00_Sistemas\2026-07-08-sistema-vali-deploy.md`.

Objetivo: extender el CLI para desplegar a servidores remotos (Windows y Linux) vía SSH, con foco en Docker (vía `docker compose` en el servidor) y en el flujo clásico de `dotnet publish` + comprimido + subida, permitiendo definir una secuencia de pasos configurable por proyecto en vez de los comandos hardcodeados actuales. Se resuelve junto con esto la deuda técnica detectada en la auditoría: falta de verificación de exit code entre pasos, credenciales en texto plano, ausencia de logging persistente/reintentos, y `MenuManager.cs` como God Class.

## Decisiones de alcance (de la fase de clarificación)

- **Auth SSH**: exclusivamente clave pública/privada (no password). La ruta a la clave privada y el nombre de la env var con la passphrase se guardan en config; el valor de la passphrase nunca se persiste.
- **Pipeline**: modelo híbrido — pasos tipados y validados por su propio executor, más un tipo `RawCommand` como escape hatch para casos no cubiertos.
- **Dónde corre cada paso**: build/publish/docker build siempre local; el pipeline agrega pasos que copian artefactos (scp/sftp) y ejecutan comandos en el remoto (SSH) — no hay build remoto en esta fase.
- **SO remoto**: Windows y Linux soportados desde el inicio; `RemoteServer.Os` determina el shell usado para armar comandos remotos (PowerShell vs bash).
- **Deploy con Docker**: se prioriza `docker compose` en el remoto sobre `docker run` con args reconstruidos — el pipeline publica una imagen a un registry (Docker Hub u otro) y el servidor remoto hace `docker compose pull && docker compose up -d` sobre un `compose.yml` copiado por SSH. Incluye limpieza de imágenes viejas post-deploy (`DockerImagePrune`, acotado por defecto a las imágenes del propio proyecto).
- **Deploy clásico (no-Docker)**: se mantiene y formaliza el flujo `dotnet publish → zip → subir → extraer → reiniciar servicio/IIS pool` que hoy existe parcialmente (`ZipPublishOutput`).
- **Plantillas**: al crear el pipeline de un `SubProject` se ofrecen dos puntos de partida (Docker Compose / Publish+Zip) generados por `PipelineTemplateFactory`; el usuario los edita libremente después (agregar, quitar, reordenar pasos, insertar `RawCommand`).
- **`GitCheckout` y sincronización de cambios**: es el mismo `StepType` en ambas plantillas (Docker Compose y Publish/Zip necesitan definir sobre qué rama local se buildea/empaqueta). El flag de si sincroniza con el remoto antes de continuar (`Args["SyncBeforeBuild"]`, bool, default `true`) se configura **una sola vez al armar/editar el paso** en `PipelineEditorMenu` — no se pregunta en cada ejecución del pipeline — y aplica igual sin importar el tipo de plantilla, porque es una propiedad del paso `GitCheckout`, no del pipeline. Copy profesional en la UI del editor: *"Sincronizar la rama con el remoto antes de continuar"* (evitar wording informal tipo "jalar cambios").
- **Credenciales**: solo referencias a variables de entorno en el JSON de config (`PassphraseEnvVar`, `DockerRegistryTokenEnvVar`) — nunca el valor. Resolución falla explícito si la env var no existe.
- **Multi-entorno**: `DeployEnvironment` (DEV/QA/PROD) es una entidad de primer nivel, reutilizable entre proyectos, con su propio `RemoteServer` y `DefaultBranch`. Cada `SubProject` tiene un pipeline por entorno (`PipelinesByEnvironment`) — la navegación del CLI permite entrar por entorno ("ver todo lo que corre en QA") o por proyecto ("ver a qué entornos despliega esta API"). Se llama `DeployEnvironment` y no `Environment` para no colisionar con `System.Environment` (usado por `EnvVarSecretResolver`).
- **Deuda técnica**: se resuelve completa junto con el feature (exit code check, credenciales, logging, y el refactor de `MenuManager.cs`), no se pospone.
- **UI**: se reemplaza el output disperso de `AnsiConsole.MarkupLine` por una vista centralizada de progreso (`AnsiConsole.Progress`) + tabla resumen al final.

## Arquitectura

Reestructuración en capas (Clean Architecture, alineado a los estándares globales del usuario) dentro del mismo proyecto `vali-deploy` (no se separa en múltiples `.csproj` — el alcance no lo justifica):

```
vali-deploy/
├── Domain/
│   ├── Project.cs, SubProject.cs           (existentes, movidos)
│   ├── DeployEnvironment.cs, RemoteServer.cs (nuevo)
│   ├── DeployStep.cs + StepType (enum)      (nuevo)
│   └── PipelineResult.cs, StepResult.cs     (nuevo)
├── Application/
│   ├── IPipelineRunner.cs / PipelineRunner.cs
│   ├── IStepExecutor.cs
│   ├── Executors/
│   │   ├── LocalCommandExecutor.cs
│   │   ├── DockerBuildExecutor.cs
│   │   ├── DockerPushExecutor.cs
│   │   ├── DockerSaveExecutor.cs / DockerLoadExecutor.cs
│   │   ├── DockerImagePruneExecutor.cs
│   │   ├── DockerComposePullExecutor.cs / DockerComposeUpExecutor.cs / DockerComposeDownExecutor.cs
│   │   ├── ZipPublishExecutor.cs            (lógica existente, movida)
│   │   ├── CopyToRemoteExecutor.cs          (sftp)
│   │   ├── SshCommandExecutor.cs
│   │   └── RawCommandExecutor.cs
│   ├── PipelineTemplateFactory.cs
│   └── ISecretResolver.cs / EnvVarSecretResolver.cs
├── Infrastructure/
│   ├── ProcessRunner.cs                     (adaptado de CommandExecutor actual)
│   ├── SshClientFactory.cs                  (wrapper sobre SSH.NET / Renci.SshNet)
│   ├── ProjectRepository.cs                 (adaptado de ProjectManager: persistencia JSON)
│   └── PipelineLogger.cs                    (archivo + consola)
├── Presentation/
│   ├── MenuManager.cs                       (adelgazado)
│   ├── ChartManager.cs
│   ├── EnvironmentMenu.cs                   (nuevo: navegación por DEV/QA/PROD → proyectos con pipeline en ese entorno)
│   ├── PipelineEditorMenu.cs                (nuevo: alta/edición/reorden de pasos)
│   └── PipelineExecutionView.cs             (nuevo: Progress + tabla resumen)
└── Utils/                                    (igual que hoy)
```

## Modelo de dominio

```csharp
enum StepType {
    GitCheckout, LocalCommand, DockerBuild, DockerPush, DockerSave, DockerLoad,
    DockerImagePrune, DockerComposePull, DockerComposeUp, DockerComposeDown,
    ZipPublishOutput, CopyToRemote, SshCommand, RawCommand
}

enum RemoteOs { Windows, Linux }

class DeployStep {
    StepType Type;
    string Name;                       // etiqueta legible en logs/UI
    Dictionary<string,string> Args;    // validado por el IStepExecutor correspondiente
    bool ContinueOnFailure = false;    // default: un fallo corta el pipeline
    int RetryCount = 0;                // reintentos con backoff (1s, 3s, 5s) — pensado para pasos de red
}

// GitCheckoutExecutor lee de Args (definido una vez en PipelineEditorMenu, no por prompt en cada corrida):
//   Args["Branch"]           = rama a checkear (default: DeployEnvironment.DefaultBranch)
//   Args["SyncBeforeBuild"]  = "true"/"false" — si corre `git pull` sobre esa rama antes de continuar (default: "true")

class RemoteServer {
    string Host;
    int Port = 22;
    string User;
    RemoteOs Os;
    string PrivateKeyPath;
    string? PassphraseEnvVar;          // nombre de la env var, nunca el valor
}

// Nueva entidad de primer nivel: entorno reutilizable entre proyectos (DEV, QA, PROD, ...)
// Nombrada DeployEnvironment (no "Environment") para no colisionar con System.Environment
class DeployEnvironment {
    string Name;                       // "DEV", "QA", "PROD" — clave usada por SubProject.PipelinesByEnvironment
    RemoteServer? Server;              // null = entorno sin deploy remoto (ej. DEV que solo hace build/publish local)
    string? DefaultBranch;             // ej: "main" en PROD, "develop" en QA
}

// SubProject existente se extiende:
class SubProject {
    // ...campos actuales se migran a Args de sus DeployStep correspondientes
    Dictionary<string, List<DeployStep>> PipelinesByEnvironment; // key = DeployEnvironment.Name
    string? DockerRegistryTokenEnvVar; // reemplaza el DockerHubUser en texto plano
}
```

`Environments: List<DeployEnvironment>` se persiste a nivel raíz de `deploy_config.json`, junto a `Projects` (no anidado dentro de cada proyecto) — así QA/DEV/PROD se definen una vez y los reutiliza cualquier `SubProject`. La referencia por nombre (string) en `PipelinesByEnvironment` se valida al editar/ejecutar el pipeline, no por FK real (config JSON plano).

`Args` como `Dictionary<string,string>` evita convertidores JSON custom (se mantiene `System.Text.Json` puro) — cada `IStepExecutor` valida sus propias claves al ejecutar, no en deserialización.

## Ejecución, errores y logging

`PipelineRunner.RunAsync(List<DeployStep>, DeployEnvironment, IProgress<StepResult>)` (el `DeployEnvironment` trae el `RemoteServer?` y el `DefaultBranch` que necesitan los executores de SSH/GitCheckout):

```
foreach step in pipeline:
    emit StepResult.Started(step)
    result = await executors[step.Type].ExecuteAsync(step, context)
    log.Write(step, result)             // archivo + evento a la UI vía IProgress
    if result.ExitCode != 0:
        if step.RetryCount > 0 y quedan reintentos → retry con backoff, vuelve a intentar el mismo paso
        else if step.ContinueOnFailure → emit Warning, continúa con el siguiente paso
        else → emit Failed, corta el pipeline, PipelineResult.Success = false
return PipelineResult { Success, List<StepResult> }
```

Corrige la falencia detectada en la auditoría (`RunCommandsAsync` no verificaba exit code) — acá cortar en el primer fallo es el comportamiento por defecto.

**Logging**: `PipelineLogger` escribe cada corrida a `%USERPROFILE%\Documents\vali-deploy\logs\{proyecto}-{subproyecto}-{timestamp}.log` en texto plano (sin dependencia nueva tipo Serilog — no se justifica para el alcance de este CLI), en paralelo al output que ve el usuario en consola.

**Credenciales**: `EnvVarSecretResolver.Resolve(envVarName)` lee `Environment.GetEnvironmentVariable`. Si la variable referenciada no existe, falla explícito con mensaje claro (no hay fallback silencioso ni valor default).

**SSH/SFTP**: `SshClientFactory` envuelve `SSH.NET` (`Renci.SshNet`, único paquete NuGet nuevo del proyecto). Abre conexión con `PrivateKeyFile` + passphrase resuelta; expone `RunCommandAsync` (ajusta el comando a `bash -c` o `powershell -Command` según `RemoteServer.Os`) y `UploadFileAsync` (sftp).

## UI de ejecución

`PipelineExecutionView` reemplaza el output disperso de `AnsiConsole.MarkupLine`:
- `AnsiConsole.Progress()` con una task por `DeployStep`: nombre, spinner mientras corre, ✅/❌ + duración al terminar
- Output (stdout/stderr) del comando en un panel debajo de la barra activa
- Contador de intento visible si el paso tiene `RetryCount > 0` (`Intento 2/3...`)
- Colores: verde = ok, amarillo = warning (`ContinueOnFailure` con fallo), rojo = falló y cortó el pipeline
- Al finalizar: tabla resumen (`Spectre.Console.Table`) — columnas Paso | Estado | Duración | Exit Code

`PipelineRunner` no depende de Spectre — expone `IProgress<StepResult>`, y `PipelineExecutionView` es quien se suscribe y dibuja (mantiene la capa Application libre de dependencias de presentación).

## Plantillas de pipeline

`PipelineTemplateFactory` genera el punto de partida al asignar un `SubProject` a un `DeployEnvironment` (`PipelinesByEnvironment[env.Name] = template`). Si el proyecto es un repo git, ambas plantillas arrancan con `GitCheckout` usando `DeployEnvironment.DefaultBranch`:

- **Docker Compose**: `GitCheckout → DockerBuild → DockerPush → CopyToRemote(compose.yml) → DockerComposePull → DockerComposeUp → DockerImagePrune`
- **Publish/Zip**: `GitCheckout → LocalCommand(clean) → LocalCommand(dotnet publish) → ZipPublishOutput → CopyToRemote(zip) → SshCommand(extract) → SshCommand(restart servicio/IIS pool)`

Ambas son editables en `PipelineEditorMenu` — agregar, quitar, reordenar pasos, o insertar `RawCommand` en cualquier punto. Como la rama vive en `DeployEnvironment.DefaultBranch` y el pipeline vive en `SubProject.PipelinesByEnvironment[env]`, un mismo proyecto desplegado a QA (rama `develop`) y PROD (rama `main`) tiene dos pipelines independientes sin duplicar la definición del servidor remoto — `DeployEnvironment` se define una sola vez y la reutilizan todos los proyectos que apuntan a QA o PROD.

### Variante sin registry: `docker save`/`docker load`

No es un sistema aparte — es una variante de la plantilla **Docker Compose** para servidores remotos sin acceso a un registry (Docker Hub u otro privado). Se arma reemplazando `DockerPush` + `DockerComposePull` por `DockerSave → CopyToRemote(tar) → DockerLoad`, transfiriendo la imagen directo por SFTP en vez de publicarla:

- **Docker Compose (sin registry)**: `GitCheckout → DockerBuild → DockerSave → CopyToRemote(compose.yml) → CopyToRemote(image.tar) → DockerLoad → DockerComposeUp → DockerImagePrune`

`DockerSaveExecutor` corre `docker save -o <tar> <image:tag>` localmente; `DockerLoadExecutor` corre `docker load -i <tar>` en el remoto vía SSH (no confundir con `CopyToRemoteExecutor`, que solo transfiere el archivo). `PipelineTemplateFactory` no genera esta variante por defecto — el usuario la arma manualmente en `PipelineEditorMenu` a partir de la plantilla Docker Compose cuando el `RemoteServer` de su `DeployEnvironment` no tiene salida a un registry.

## Testing

- Nuevo proyecto `vali-deploy.Tests` (xUnit) agregado a `vali-deploy.sln`
- Unit tests sobre `Application/`: `PipelineRunner` con `IStepExecutor` mockeados (corte en fallo, retry con backoff, `ContinueOnFailure`); cada `Executor` individual con `ProcessRunner`/`SshClientFactory` mockeados
- Sin integration tests contra un servidor SSH real en esta fase — se documenta como prueba manual en el README; agregar Testcontainers para esto es sobre-ingeniería para el alcance actual
- Foco de cobertura en `Application/` (lógica con bugs potenciales reales), no en `Presentation/` (menús Spectre)

## Plan de migración de `MenuManager.cs`

Incremental, no big-bang — el CLI debe seguir funcionando en cada paso:

1. Mover `Project`/`SubProject` a `Domain/` sin cambiar comportamiento
2. Extraer `ProcessRunner` de `CommandExecutor` actual hacia `Infrastructure/` (mismo código, nueva ubicación)
3. Construir `DeployStep`/`StepType`/executores — código 100% nuevo, no toca el flujo existente
4. Reescribir las pantallas de `MenuManager` que hoy arman comandos Docker a mano (`ExecuteCommandSubProject`) para construir `DeployStep[]` y delegar a `PipelineRunner` — este paso es el que reduce las líneas de `MenuManager.cs`
5. El menú de publish/zip clásico sigue funcionando igual para `SubProject` sin ningún `DeployEnvironment` asignado todavía — no rompe el uso actual del CLI

## Deuda técnica pendiente (fuera de este spec)

- Los 720 archivos de `bin/`/`obj/` commiteados requieren `git rm -r --cached` manual del usuario (bloqueado por hook local `destructive-guard.sh`, ver notas en el documento de auditoría del vault)
- Verificación de integridad del mecanismo de auto-actualización (`UpdaterManager`, descarga sin checksum/firma) — anotado en la auditoría, no forma parte de este feature
