# CLAUDE.md — Vali-Deploy

## Stack
- **Runtime/Lenguaje**: .NET 7.0 (`global.json` fija SDK 7.0.0, rollForward latestMinor)
- **Tipo**: CLI de consola (self-contained, multi-RID: `osx-x64;osx-arm64;linux-x64;win-x64`)
- **UI de consola**: Spectre.Console + Spectre.Console.Cli (0.49.1) — único paquete NuGet del proyecto
- **Serialización**: System.Text.Json (BCL, sin Newtonsoft)
- **Testing**: no hay tests en el repo
- **Auto-actualización**: `UpdaterManager` consulta un JSON de versión hosteado en Netlify (`Constants.UrlVersion`) y descarga/reemplaza el binario

## Comandos clave
```bash
# Desarrollo
dotnet run --project vali-deploy/vali-deploy.csproj

# Build
dotnet build vali-deploy.sln

# Publish (self-contained, ejemplo win-x64)
dotnet publish vali-deploy/vali-deploy.csproj -r win-x64 -c Release --self-contained true
```

## Convenciones del proyecto
- **Nombres**: PascalCase para clases/métodos, camelCase para variables locales (convención estándar C#)
- **Estructura**: por capa (`Managers/`, `Models/`, `Utils/`) — no por feature
- **Commits**: sin convención detectada, adoptar Conventional Commits (`feat/fix/refactor/chore`)
- **Branches**: no verificado

## Arquitectura de alto nivel
CLI que automatiza build/publish de proyectos .NET (incluye Web APIs) y opcionalmente build/run/push de imágenes Docker — todo en la **máquina local** del usuario. `Program.cs` maneja auto-actualización y delega a `MenuManager.StartAsync()`, que orquesta un menú interactivo (Spectre.Console) sobre `Project`/`SubProject` persistidos en `%USERPROFILE%\Documents\vali-deploy\deploy_config.json`.

## Módulos principales
- `Managers/MenuManager.cs` — orquesta el menú interactivo completo (700+ líneas); punto de entrada de casi toda la lógica de negocio, incluida la construcción de comandos Docker
- `Managers/ProjectManager.cs` — CRUD de `Project`/`SubProject`, persistencia en `deploy_config.json` (System.Text.Json)
- `Managers/CommandExecutor.cs` — ejecuta comandos locales vía `Process` (`cmd.exe /c` en Windows, `/bin/bash -c` en Unix); build/publish/zip y comandos Docker
- `Managers/UpdaterManager.cs` — auto-actualización del propio CLI contra un JSON remoto en Netlify
- `Managers/ChartManager.cs` — gráfico de barras (Spectre) con conteo de subproyectos
- `Models/Project.cs`, `Models/SubProject.cs` — modelo de dominio (proyecto → lista de subproyectos con flags de publish/Docker)
- `Utils/Constants.cs`, `Utils/Util.cs` — constantes y helpers (detección de OS/arch, comparación de versiones)

## NO hacer
- [ ] No commitear más binarios a `bin/`/`obj/` — ya hay 720 archivos de build trackeados en git, incluyendo DLLs que no corresponden a las dependencias actuales del `.csproj` (EPPlus.Core, PromptPlus, System.Data.SqlClient). Ver sección "Deuda técnica conocida".
- [ ] No asumir que `RunCommandsAsync` (`CommandExecutor.cs`) verifica el exit code entre pasos — hoy no lo hace; un `dotnet build` fallido no detiene el `dotnet publish` siguiente.
- [ ] No guardar credenciales nuevas en `deploy_config.json` en texto plano (ya ocurre con `DockerHubUser`) — si se agrega soporte SSH, las credenciales van fuera del JSON de config (ver flow de despliegue SSH en curso).
- [ ] No commitear `.env` ni archivos con credenciales

## Contexto de negocio
Herramienta CLI de uso personal/publicado en GitHub (`UBF21/Vali-deploy`) para automatizar build, publish y empaquetado Docker de proyectos .NET, evitando repetir comandos manuales. En evaluación para extenderse con despliegue remoto vía SSH a servidores Windows/Linux con secuencias de comandos configurables.
