# Vali-Deploy

CLI de consola (.NET 7, Spectre.Console) que automatiza build, publish y empaquetado Docker de proyectos .NET.

## Estándares de Desarrollo

> Generado por `/flows/onboard` — 2026-07-08. Mantener sincronizado con `.claude/memory/project-standards.md`.

### Tamaño
- Líneas por archivo: 300 (`Managers/MenuManager.cs` ya lo supera — deuda técnica conocida)
- Líneas por función: 30
- Parámetros por función: 3
- Longitud de línea: 120 caracteres

### Complejidad
- Ciclomática máxima: 10
- Cognitiva máxima: 15
- Anidamiento máximo: 3 niveles

### C# / .NET
- Nullable reference types: habilitado
- Null checks explícitos: sí

### Tests
- Cobertura mínima: 80% (aspiracional — hoy 0%, no hay tests)
- Tipos requeridos: Unit + Integration
- E2E: solo flujos críticos (publish + docker build/push)

### Naming
- Clases/métodos: PascalCase
- Variables/parámetros: camelCase
- Archivos: PascalCase

### Arquitectura
- Dependencias: infra (CommandExecutor, UpdaterManager) → orquestación (MenuManager) → dominio (Models)
- Orden de imports: BCL → paquetes externos → internos (`vali_deploy.*`)

### PRs / Commits
- LOC máximas por PR: 400
- Formato: Conventional Commits

### Seguridad
- Secrets en código: prohibido siempre
- Credenciales en `deploy_config.json` en texto plano: prohibido (deuda técnica conocida, ver `DockerHubUser`)
- Validación de inputs: obligatoria en boundary