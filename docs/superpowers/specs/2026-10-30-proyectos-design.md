# Proyectos — diseño

Fecha: 2026-10-30. Rama: `feat/proyectos` (a crear).

## Intención y por qué

El cliente (Municipio de Carmelo, Uruguay) pidió el 2026-10-21 separar dos conceptos que hoy viven mezclados en la Tarea:

- la **tarea operativa** del equipo (spec `2026-08-01-modulo-tareas-design.md`): algo concreto que alguien toma y termina;
- el **proyecto** (obra, programa, iniciativa): lo que el spec `2026-09-08-tareas-clasificadores-design.md` le agregó a la Tarea con los clasificadores zona / dimensión temática / organismo responsable / origen de financiamiento.

Se crea la entidad **Proyecto**, y la Tarea vuelve a su simplicidad inicial. El cliente no sabe todavía qué más quiere de los Proyectos. Por eso Martin eligió UNA funcionalidad extra para esta entrega: **imputar gastos a un proyecto y compararlos contra un presupuesto opcional**. Para un municipio, saber cuánto costó cada obra es lo de más valor, y aprovecha el módulo de Finanzas que ya existe.

## Objetivos

1. **Entidad Proyecto** con:
   - título (obligatorio) y descripción (opcional);
   - estado `Pendiente → En curso → Terminado / Cancelado`, con las mismas transiciones válidas que la Tarea; quien tiene permiso lo cambia a mano;
   - prioridad y fecha límite (opcional), igual que la Tarea;
   - notas, igual que la Tarea;
   - los cuatro clasificadores opcionales: **zona, dimensión temática, organismo responsable, origen de financiamiento**;
   - **presupuesto opcional** (monto en pesos).
2. **Sin responsable** (decisión de Martin): un proyecto no se toma ni se suelta, y no guarda quién lo tomó ni quién lo cerró. Las operaciones (alta, edición, cambio de estado, notas) quedan en `LogAuditoria`, como hoy.
3. **La Tarea vuelve a la forma del spec 2026-08-01**, con dos agregados:
   - **expediente opcional** (`DocumentoAdministrativoId`), que se conserva tal cual;
   - **proyecto opcional** (`ProyectoId`). Una tarea puede existir sin proyecto.
   - Se quitan de la Tarea zona, dimensión temática, organismo responsable y origen de financiamiento.
4. **Gastos imputados**: el Gasto tiene un campo opcional **Proyecto**. Un gasto va a un solo proyecto o a ninguno; nunca se reparte entre varios.
5. **Ficha del proyecto**: datos, tareas del proyecto, gastos imputados, **total gastado** y, si hay presupuesto, **presupuesto, disponible y sobregiro** (el sobregiro en rojo). Sin presupuesto se muestra solo el total gastado.
6. **Permisos propios** de Proyectos, separados de `tareas.gestionar` y `tareas.administrar`.
7. **Reporte por clasificadores** (el reporte-matriz del spec 2026-09-08): los ejes zona / dimensión / organismo / origen pasan a contar **proyectos**. El eje **expediente se elimina** del reporte (decisión de Martin): la ficha de cada expediente ya lista sus tareas vinculadas.

## No-objetivos

- **Migrar tareas a proyectos**: NO. Las tareas actuales siguen siendo tareas, y Proyectos arranca vacío (Martin: "nadie dijo que las tareas actuales pasen a ser proyectos").
- Proyecto ↔ expediente: por ahora no se asocian.
- Avance del proyecto calculado por tareas: no (Martin: no van a cargar al principio todas las tareas que requiere el proyecto).
- Responsable o asignación de usuarios al proyecto: no.
- Materiales de stock imputados al proyecto: no, por ahora.
- Bloquear la carga de un gasto que supera el presupuesto: no. El sobregiro solo se muestra.
- Ficha PDF del proyecto: no, en esta entrega.

## Anti-objetivos

- Que una tarea existente pierda su expediente, sus notas, su estado o su trazabilidad (tomada/cerrada).
- Que los permisos de Tareas den acceso a Proyectos, o al revés.
- Que el total gastado de un proyecto cuente gastos anulados (`Gasto.Activo = false`).
- Correr la migración en producción sin un backup verificado previo: la migración **borra datos cargados** (los cuatro clasificadores de las tareas).

## Restricciones

- Arquitectura existente: Domain / Application / Infrastructure (EF Core + PostgreSQL) / Api (minimal API + JWT) / ApiClient / Presentation (Avalonia, UI v2). El desktop habla solo con la API.
- Sin acceso al servidor post-instalación: todo control vive en el cliente o en la DB.
- Formato es-UY en montos (`FormatoEsUy`), convenciones de grillas y formularios del proyecto (`DataGridColumnasAutoBehavior`, `ComandoDeGuardado`).
- Las vistas Avalonia no se auto-inicializan: hay que cargar datos vía `DataContextChanged`.
- Un gate de permisos en la UI necesita un test en `Presentation.UiTests` con la View real y un usuario de permisos mixtos (no Admin).
- Toda la UI nueva sigue el sistema visual UI v2.

## Diseño decidido

1. **Migración de datos**:
   - Se quitan de `Tarea` las columnas `ZonaId`, `DimensionTematicaId`, `OrganismoResponsableId` y `OrigenFinanciamientoId`, y sus valores se **borran** (decisión de Martin). `DocumentoAdministrativoId` se queda.
   - Se agrega `Tarea.ProyectoId` (nullable, todas en null) y `Gasto.ProyectoId` (nullable, todos en null).
   - Las tablas maestras (Zona, DimensionTematica, OrganismoResponsable, OrigenFinanciamiento) no se tocan: pasan a usarlas los proyectos.
   - Requisito de despliegue: backup `pg_dump` verificado antes de aplicar la migración en el VPS.
2. **Estados del proyecto**: se reusan las transiciones válidas de la Tarea (Pendiente→En curso, En curso→Terminado, Pendiente/En curso→Cancelado). El cambio de estado lo hace cualquiera con el permiso de gestión; cada cambio se audita con "estado anterior → nuevo".
   - *(Decisión del agente.)* A diferencia de la Tarea, Terminado y Cancelado se pueden **reabrir** a En curso, con `proyectos.gestionar`. Sin esto, un proyecto cerrado por error no podría recibir más gastos.
   - *(Decisión del agente.)* El proyecto guarda `FechaCreacion` y `FechaFin`. `FechaFin` se sella al pasar a Terminado o Cancelado y se limpia al reabrir. Así el reporte-matriz conserva su criterio de fecha Creación/Cierre.
3. **Gasto ↔ Proyecto**:
   - El selector de proyecto en el formulario de gasto (alta y edición, contado y crédito) ofrece solo proyectos **Pendiente o En curso**.
   - Un gasto ya imputado a un proyecto que después se terminó o se canceló conserva su imputación y la muestra.
   - El ingreso por factura (`IngresoPorFacturaService`) también permite elegir el proyecto, porque genera un gasto.
   - Total gastado = suma de `MontoTotal` de los gastos activos imputados. Es el monto comprometido, no lo pagado; el mismo criterio que el "gastado" del Control POA.
   - *(Decisión del agente.)* El proyecto se puede cambiar o quitar en cualquier gasto **activo**, incluso en uno que viene de una factura con movimientos de stock: es una clasificación que no mueve plata ni stock. Va por la edición de gasto existente y se audita. Un gasto anulado no se edita.
   - *(Decisión del agente.)* El proyecto **no** agrega una columna a la grilla de gastos ni al PDF de gastos, porque a 1366 px la grilla ya no entra (ver la sesión del 2026-10-27). Se ve en el formulario de gasto y en la ficha del proyecto.
4. **Permisos**: `proyectos.ver` (ver lista y ficha) y `proyectos.gestionar` (alta, edición, estado, notas, presupuesto). Imputar un gasto a un proyecto lo hace quien registra gastos (`finanzas.gastos`): no requiere un permiso de proyectos.
   - Ficha sin `finanzas.ver` (decisión de Martin): se ven el total gastado, el presupuesto, el disponible y el sobregiro, pero **no** la lista de gastos. El endpoint no devuelve la lista a quien no tiene `finanzas.ver` (no alcanza con ocultarla en la UI), y el gate de UI lleva su test con la View real y un usuario de permisos mixtos.
   - *(Decisión del agente.)* Mismo criterio que `tareas.gestionar`: Admin y Operador tienen los dos permisos, tanto los Operadores existentes (backfill en la migración) como los nuevos (`PermisosInicialesOperador`). El reporte-matriz sigue controlado por `reportes.ver`.
5. **Tarea ↔ Proyecto**: el formulario de tarea tiene un selector opcional de proyecto. La ficha del proyecto lista sus tareas y permite crear una tarea ya vinculada. Desvincular = dejar el proyecto vacío en la tarea.

## Lo que queda a criterio del builder

- Nombres de tablas, DTOs, endpoints y pantallas, siguiendo las convenciones existentes (`TareasEndpoints`, `TareaApiClient`, etc.).
- Si el estado y la prioridad del Proyecto reusan los enums `EstadoTarea`/`PrioridadTarea` o tienen los suyos.
- Ubicación del módulo Proyectos en el sidebar.
- Cómo queda el panel de clasificación (`ClasificacionTareaPanelViewModel`): se mueve a Proyectos sin el buscador de expedientes, y en la Tarea queda solo el expediente.
