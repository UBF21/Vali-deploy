# Cancelar registro de Entorno / creación de Pipeline — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Agregar cancelación segura a `EnvironmentMenu.AddEnvironment` (resumen + confirmación final antes de persistir) y a `PipelineEditorMenu.StartAsync` (cancelar selección de entorno, y diferir el `Save()` de la plantilla hasta confirmar), reusando los patrones de cancelación ya existentes en el repo.

**Architecture:** Dos archivos independientes en `Presentation/`, sin dependencia entre sí — se pueden implementar como dos tasks en paralelo.

**Tech Stack:** .NET 7, Spectre.Console 0.49.1 (sin paquetes nuevos).

**Spec:** `docs/specs/2026-07-11-cancelar-registro-entorno-pipeline-design.md`

---

### Task 1: Confirmación final en `EnvironmentMenu.AddEnvironment`

**Files:**
- Modify: `vali-deploy/Presentation/EnvironmentMenu.cs`

Sin test — Presentation/Spectre.Console no testeable en este repo (criterio ya establecido en ciclos previos).

- [ ] **Step 1: Agregar `ConfirmEnvironmentSummary` y el gate antes de `Save`**

Reemplazar `AddEnvironment` completo por la versión de la spec (sección 1), que agrega el método privado `ConfirmEnvironmentSummary(DeployEnvironment environment)` (tabla resumen + `AnsiConsole.Confirm("¿Guardar este entorno?", true)`) y el `if (!ConfirmEnvironmentSummary(environment)) { ...; return; }` antes de `config.Environments.Add(environment); repository.Save(config);`. Código exacto en la spec.

- [ ] **Step 2: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

- [ ] **Step 3: Commit**

```bash
git add vali-deploy/Presentation/EnvironmentMenu.cs
git commit -m "feat(presentation): agregar confirmacion final antes de guardar un entorno nuevo"
```

---

### Task 2: Cancelar en `PipelineEditorMenu.StartAsync`

**Files:**
- Modify: `vali-deploy/Presentation/PipelineEditorMenu.cs`

Sin test — mismo criterio que Task 1.

- [ ] **Step 1: Cancelar en selección de entorno**

En `StartAsync`, reemplazar el `SelectionPrompt` de entorno (línea 20-21) por la versión de la spec (sección 2a): appendear `"[seagreen1]Cancelar[/]"` a los choices, y si se elige, `return` inmediato antes de resolver `environment`/`configSubProject`.

- [ ] **Step 2: Diferir el `Save()` de la plantilla hasta confirmar**

Reemplazar el bloque `if (!configSubProject.PipelinesByEnvironment.ContainsKey(environmentName)) { ... }` (línea 30-41) por la versión de la spec (sección 2b): agregar `"Cancelar"` al `SelectionPrompt` de plantilla con `return` si se elige; agregar `AnsiConsole.Confirm(...)` antes de crear la entrada en `PipelinesByEnvironment` y llamar `repository.Save(config)`; si el usuario responde que no, mensaje de cancelado y `return` sin persistir.

- [ ] **Step 3: Compilar**

Run: `dotnet build vali-deploy.sln`
Expected: Build succeeded, 0 errores.

- [ ] **Step 4: Commit**

```bash
git add vali-deploy/Presentation/PipelineEditorMenu.cs
git commit -m "feat(presentation): agregar cancelacion en seleccion de entorno y plantilla del pipeline editor"
```

---

### Task 3: Verificación manual

**Files:** ninguno (solo verificación)

- [ ] **Step 1: Verificar cancelación en "Add Environment"**

`dotnet run` → Manage Environments → Add Environment → completar algunos campos (aunque sea con datos de prueba) → en la confirmación final, responder "No". Confirmar que no aparece un entorno nuevo en la lista de "Manage Environments" y que `deploy_config.json` no cambió.

- [ ] **Step 2: Verificar guardado normal sigue funcionando**

Repetir el flujo de Add Environment completo, esta vez confirmando "Sí". Confirmar que el entorno nuevo aparece en la lista.

- [ ] **Step 3: Verificar cancelación en selección de entorno del Pipeline Editor**

Entrar a configurar el pipeline de un subproyecto (desde "Show Projects" → subproyecto → gestionar pipeline, o el flujo equivalente) y elegir "Cancelar" en la selección de entorno. Confirmar que vuelve al menú anterior sin errores.

- [ ] **Step 4: Verificar cancelación en selección/confirmación de plantilla**

Para un subproyecto sin pipeline configurado todavía en un entorno dado: elegir ese entorno, y en la plantilla elegir "Cancelar" (o elegir una plantilla y responder "No" a la confirmación). Confirmar que NO se creó ninguna entrada en `PipelinesByEnvironment` para ese entorno (por ejemplo, volviendo a entrar al Pipeline Editor para ese mismo subproyecto/entorno y viendo que vuelve a pedir la plantilla, en vez de ir directo a la lista de steps).

Si cualquiera de estos pasos falla, corregir el código en el task correspondiente y volver a compilar antes de continuar.

---

## Self-review

**Cobertura de la spec:** los dos problemas descritos (falta de cancelación en `AddEnvironment`, persistencia prematura en `PipelineEditorMenu`) están cubiertos uno-a-uno por Task 1 y Task 2 respectivamente. La regla "cancelar vuelve al menú anterior sin estado intermedio" se verifica en Task 3 Steps 1, 3 y 4.

**Consistencia de tipos:** ningún tipo nuevo — se reusan `DeployEnvironment`, `RemoteServer`, `SubProject` ya existentes, sin cambios de forma.

**Sin placeholders:** el código de cada step está completo en la spec referenciada, sin TBD.
