# Proyectos

Objective: Proyecto entity with classifiers, optional budget and imputed expenses; Tarea back to its original shape. Branch: `feat/proyectos`. Delivery: merge --ff-only to main (project convention). Runner: `dotnet test` per project. Plan: `docs/superpowers/plans/2026-10-30-proyectos.md`. Spec: `docs/superpowers/specs/2026-10-30-proyectos-design.md`.

## Specs

- S1: Proyecto entity per spec, objective 1. User: "Lo que vamos a sacar de proyectos es la pertenencia, no se adjudica un usuario al proyecto, solo se registran las operaciones como se hace hoy en día para auditar."
- S2: Proyecto has no expediente. User: "Por ahora no vamos a asociar proyectos y expedientes."
- S3: Tarea keeps expediente and may exist without a project. User: "Las tareas pueden existir sin proyectos, así como también las tareas pueden pertenecer a expedientes."
- S4: Existing tasks are not migrated. User: "nadie dijo que las tareas actuales ahora pasen a ser proyectos, de hecho se dijo lo contrario"
- S5: The 4 classifiers of existing tasks are deleted. User: "se borran"
- S6: Expenses imputed to a project. User: "vamos solo con 1 por ahora" (idea 1: gastos imputados al proyecto)
- S7: Budget. User: "Que lleve un presupuesto si" / "que el presupuesto sea opcional"
- S8: States changed by hand. User: "si asi esta bien" (same states, manual change)
- S9: Without finanzas.ver: totals visible, expense list hidden. User: "si esta bien asi"
- S10: Remove the expediente axis from the matrix report. User: "si sacalo"
- S11: No progress by tasks. User: "el 3 no tiene mucho sentido porque no van a cargar al principio todas las tareas que requiera el proyecto"

## Tasks

| ID | Specs | Route | Status | Commit |
|---|---|---|---|---|
| T1 Dominio | S1,S7,S8 | delegated (SDD, user rule) | pending | |
| T2 Permisos | S1,S9 | delegated | pending | |
| T3 Persistencia + migración aditiva | S1,S3,S6 | delegated | pending | |
| T4 ProyectoService | S1,S7,S8,S9 | delegated | pending | |
| T5 API + ApiClient | S1,S9 | delegated | pending | |
| T6 Tarea↔Proyecto | S3 | delegated | pending | |
| T7 Gasto↔Proyecto | S6 | delegated | pending | |
| T8 Reporte → proyectos | S10 | delegated | pending | |
| T9 Quitar clasificadores de Tarea (high risk item 1) | S3,S4,S5 | delegated | pending | |
| T10 Desktop Proyectos | S1,S7,S8,S9 | delegated | pending | |
| T11 Selector en gasto/factura | S6 | delegated | pending | |
| T12 Suites + GATE orgánico | all | delegated | pending | |

Route evidence: the user's personal rule "SIEMPRE ejecutar planes con subagentes (subagent-driven)".

## Log

- L1 (2026-10-30) user: "vamos con los proyectos"
- L2 design approved by user ("aprobado"), spec commit c25a2330.
- L3 plan written: docs/superpowers/plans/2026-10-30-proyectos.md. Awaiting user review.
