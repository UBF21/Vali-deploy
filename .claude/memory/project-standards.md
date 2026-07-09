# Estándares del Proyecto — Vali-Deploy
Generado: 2026-07-08

## Tamaño
- Líneas por archivo: 300 (nota: `Managers/MenuManager.cs` ya supera esto — 700+ líneas, ver deuda técnica)
- Líneas por función: 30
- Parámetros por función: 3 (más → usar objeto)
- Longitud de línea: 120 caracteres

## Complejidad
- Ciclomática máxima: 10
- Cognitiva máxima: 15
- Anidamiento máximo: 3 niveles

## C# / .NET
- `dynamic`/`object` sin tipar: prohibido sin justificación
- Nullable reference types: habilitado (ya está `<Nullable>enable</Nullable>` en el .csproj — respetar)
- Null checks explícitos: sí

## Tests
- Cobertura mínima: 80% (aspiracional — hoy el proyecto tiene 0% de cobertura, no hay tests)
- Tipos requeridos: Unit + Integration
- Tests E2E: solo flujos críticos (publish + docker build/push)

## Naming
- Clases/métodos: PascalCase
- Variables locales/parámetros: camelCase
- Archivos: PascalCase (convención C# estándar, coincide con el repo)
- Constantes: SCREAMING_SNAKE_CASE o PascalCase (C# admite ambos; el repo usa PascalCase en `Constants.cs`, mantener consistencia)

## Arquitectura
- Dirección de dependencias: infra (CommandExecutor, UpdaterManager) → orquestación (MenuManager) → dominio (Models)
- Lógica de negocio en: hoy vive mezclada en `MenuManager` — a separar en capa de aplicación si el proyecto crece (ver deuda técnica)
- Orden de imports: BCL → paquetes externos (Spectre.Console) → internos (`vali_deploy.*`)

## PRs / Commits
- LOC máximas por PR: 400
- Formato: Conventional Commits
- Un commit por unidad lógica de cambio

## Comentarios
- Cuándo comentar: solo WHY no obvio
- XML doc (`///`) requerido: solo en métodos públicos de `Managers/*` que se usen como API interna del CLI

## Seguridad
- Secrets en código: prohibido siempre
- Credenciales (SSH, Docker Hub) en `deploy_config.json` en texto plano: prohibido — usar mecanismo separado (ver deuda técnica)
- Validación de inputs: obligatoria en boundary (prompts de usuario vía Spectre.Console)

## Notas del proyecto
- `bin/`/`obj/` están commiteados a git (720 archivos) con DLLs que no coinciden con las dependencias actuales del `.csproj` — pendiente de limpieza (requiere confirmación del usuario antes de tocar historial).
- `CommandExecutor.RunCommandsAsync` no verifica exit code entre comandos — un paso fallido no detiene la secuencia.
- No hay soporte SSH ni despliegue remoto — todo corre en la máquina local (en evaluación, ver `/flows/onboard` → brainstorming de despliegue SSH+Docker).
