# Clasificadores y estadísticas del módulo de Tareas

Fecha: 2026-09-08
Estado: aprobado, pendiente de plan de implementación

## Contexto

El cliente (municipio de Carmelo, Uruguay) pide extender el módulo de Tareas con cinco clasificadores y un reporte estadístico. Pedido textual: poder adjudicar a una tarea un barrio/zona, una "dimensión" (categoría temática: infraestructura, tránsito, etc.), un responsable (municipio, intendencia, organismo nacional), una fuente de financiamiento, y un expediente. Además, estadísticas: tareas hechas por zona, cantidad de terminadas, pendientes, etc.

## Hallazgos previos al diseño

1. **No existe un módulo "Expedientes"**. Expediente es uno de los tres valores del enum `TipoDocumento` (junto con Oficio y Suministro) dentro del módulo Documentos administrativos (`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`).
2. **`FuenteFinanciamiento` ya existe** (`src/StockApp.Domain/Entities/FuenteFinanciamiento.cs`), pero es el catálogo FIGM cerrado de Finanzas (A/B/C/Multas/Excedentes) que alimenta `AsignacionPresupuestal` y `LineaPoa`.
3. **No hay ninguna librería de gráficos en el repo**. Los cinco reportes existentes son todos `DataGrid`.
4. **Tareas (~145 tests) y Documentos (~244 tests) están hoy totalmente aislados**: ninguna entidad del dominio le apunta a `DocumentoAdministrativo` por FK salvo sus propios hijos (`EventoDocumento`, `AdjuntoDocumento`).
5. La plantilla de catálogo simple del repo es `Categoria`: ~15 archivos de producción + ~6 de test por catálogo.

## Alcance

Dentro:
- 4 catálogos nuevos con ABM: `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`.
- 5 campos nullable nuevos en `Tarea` (los 4 catálogos + FK a `DocumentoAdministrativo`).
- Método de reclasificación restringido a Admin.
- Reporte-matriz de tareas con selector de agrupador.
- Sección "Tareas vinculadas" en la ficha de documento administrativo.

Fuera:
- Campo de referente-persona reasignable (la deuda D2 del spec de Documentos sigue abierta y NO se cierra acá).
- Librería de gráficos / dashboard con charts.
- Filtros por clasificador en la lista de tareas.
- Jerarquía Zona → Barrio.
- Clasificadores en Documentos administrativos (solo se agregan a Tareas).

## Decisiones

**D1. Los clasificadores son catálogos administrables, no enums.**
Tablas con ABM, para que el municipio dé de alta/baja valores sin redeploy. Motivo determinante: la restricción documentada de que post-instalación no hay acceso al servidor, así que todo control debe vivir en el cliente o en la base. Esto se aparta de la decisión D6 del spec de Documentos (que dejó `TipoDocumento` como enum fijo) y el apartamiento es deliberado: allá los valores eran tres y estables, acá son abiertos y los define el municipio.

**D2. Cuatro tablas reales con UI unificada** (alternativas descartadas: cuatro catálogos con cuatro ítems de sidebar; una tabla genérica `ClasificadorTarea` con discriminador).
Entidades, repositorios, servicios y endpoints separados siguiendo la plantilla `Categoria` — integridad referencial garantizada por la base y evolución independiente de cada catálogo. Pero una sola pantalla "Catálogos de Tareas" con `TabControl` de cuatro pestañas, un único ítem de sidebar.
Se descartó la tabla genérica con discriminador pese a ahorrar ~45 archivos: `Tarea` tendría cuatro FKs a la misma tabla y la base no podría garantizar que `ZonaId` apunte a una fila de tipo Zona; esa validación se mudaría a código. El repo ya tiene la postura contraria explícita — el índice único `IX_DocumentosAdministrativos_Tipo_Anio_Numero` existe como "última defensa" en la base en vez de confiar solo en el servicio.

**D3. Fuente de financiamiento: catálogo propio nuevo, no se reusa el de Finanzas.**
El universo que pide el municipio para tareas es más amplio que las fuentes FIGM (incluye convenios, fondos nacionales, presupuesto propio). Reusar `FuenteFinanciamiento` haría aparecer esos valores en los combos de Finanzas y contaminaría las asignaciones presupuestales. Se llama `OrigenFinanciamiento` para que la distinción sea evidente en el código. Fusionar dos catálogos más adelante es barato; descontaminar el de Finanzas no lo es.

**D4. "Responsable" es un organismo, no un funcionario.**
El pedido ("municipio, intendencia, organismo nacional") identifica una institución. Se modela como catálogo `OrganismoResponsable`. El nombre es explícito para que nunca se confunda con `TomadaPorUsuarioId`, que es el funcionario que agarró la tarea en el sistema. La deuda D2 del spec de Documentos (referente-persona reasignable) queda fuera de alcance.

**D5. Zona es un catálogo plano de un solo nivel.**
Sin jerarquía Zona → Barrio. Para el tamaño de Carmelo alcanza, y agregar el nivel superior después es una migración aditiva.

**D6. `DimensionTematica` en el código, "Dimensión" en pantalla.**
`Dimension` a secas colisiona semánticamente en un sistema que maneja productos y unidades de medida. La UI habla el idioma del cliente; el código es inequívoco.

**D7. Los cinco campos son nullable.**
Hace que la migración sea puramente aditiva: ninguna tarea existente se toca, no hay backfill, no hay riesgo sobre datos de producción. `OnDelete: Restrict` en las cinco FKs, coherente con el resto del repo; como los catálogos tienen baja lógica (`Activo`), nunca se borra nada físicamente.

**D8. Opcionales e inmutables para el operador, reclasificables por Admin.**
Coherente con la regla vigente del módulo (`Titulo`, `Descripcion` y `FechaLimite` no son editables post-creación). La válvula de escape es `ReclasificarAsync`, gateada por `Permisos.AdministrarTareas` — permiso estructural (Admin siempre, Operador nunca, no configurable) que ya protege Cancelar y Cambiar prioridad. Sin mecanismo nuevo.

**D9. La reclasificación alcanza también a tareas terminales.**
Una tarea Terminada o Cancelada puede reclasificarse. El caso de uso es corregir un reporte mal salido; si la clasificación queda congelada al cerrar, el error es permanente y contamina la estadística para siempre. El estado sigue siendo inmutable: reclasificar no reabre la tarea.
Esto tiene una consecuencia directa en la UI: el botón "Reclasificar" debe ser visible también en tareas terminales, así que NO puede heredar sin más el gate de `MuestraCambioPrioridad` (que excluye estados terminales) — ver sección Presentation para la fórmula exacta.

**D10. Doble registro de la reclasificación.**
Nota automática en el hilo (`EsAutomatica = true`) con el diff legible, del estilo "admin reclasificó — Zona: (sin asignar) → Centro; Dimensión: Tránsito → Infraestructura", más entrada en `LogAuditoria` con un valor nuevo del enum `AccionAuditada` (el siguiente libre del bloque de Tareas). Mismo patrón que `AnularAsync` en Documentos (D8 de aquel spec).
`ReclasificarAsync` replica el guard de "sin cambios" que ya existe en `CambiarPrioridadAsync` (`src/StockApp.Application/Tareas/TareaService.cs:197-198`: `if (tarea.Prioridad == prioridad) return; // sin cambios: no hay nada que registrar`): si la reclasificación no cambia ningún valor, no se genera nota automática ni entrada de auditoría.

**D11. La dependencia entre Tareas y Documentos va en un solo sentido.**
`Tarea` conoce al `DocumentoAdministrativo`; `DocumentoAdministrativo` NO tiene una colección `Tareas`. La ficha del expediente obtiene sus tareas preguntando (`ListarPorDocumentoAsync(documentoId)` del lado de Tareas). Si se colgara la colección al agregado Documento, los dos módulos pasarían a conocerse mutuamente, Documentos arrastraría tareas en sus `Include` y sus ~244 tests entrarían en zona de riesgo. Misma funcionalidad para el usuario, cero acoplamiento nuevo en el modelo.

**D12. El vínculo acepta solo documentos de tipo Expediente, y solo activos.**
Validación en el servicio, custodiada por test. El campo se llama `DocumentoAdministrativoId` porque esa es la tabla a la que apunta — el esquema no debe mentir — y en pantalla se etiqueta "Expediente". Al asignar, el documento debe estar activo (`EsActivo`); los vínculos ya existentes sobreviven al cierre o anulación del expediente sin romperse. Aceptar Oficios y Suministros más adelante es sacar una línea de validación, no migrar una columna.
`ReclasificarAsync` aplica exactamente las mismas validaciones que `CrearAsync`: cada catálogo asignado debe existir y estar activo, y el documento debe ser de tipo Expediente y estar activo. Un Admin no puede vincular un Oficio, un Suministro ni un expediente cerrado por la vía de la reclasificación — la reclasificación no es un atajo que esquive esta validación.

**D13. El endpoint de "tareas de un expediente" vive en el módulo Tareas.**
`GET /tareas?documentoId={id}`, no `GET /documentos/{id}/tareas`. El endpoint vive donde vive el dato. La pantalla de Documentos lo consume, pero el módulo Documentos no crece.
Consecuencia a manejar: un operador puede tener `documentos.gestionar` sin tener `tareas.gestionar`. Para ese usuario la sección "Tareas vinculadas" no se muestra, con gate de UI y `try/catch` propio, siguiendo el patrón ya resuelto en `InicioViewModel.cs:249-278` para el panel de vencimientos (el gate `if (PuedeVerTareas)` arranca en la línea 249; el try/catch completo, con el `else` que oculta el panel, llega hasta la 278).

**D14. El reporte de tareas no se cachea.**
`CategoriaService` invalida `IVersionReportes` porque los reportes de stock están cacheados una hora. Los servicios de catálogo de tareas NO invalidan nada, porque el reporte de tareas no usa cache: las tareas cambian de estado todo el día y un reporte de estados cacheado una hora informa mal. Un `GROUP BY` sobre el volumen de tareas de un municipio chico es instantáneo en Postgres.

**D15. Un solo reporte-matriz con selector de agrupador.**
Agrupar por zona, dimensión, organismo, financiamiento o expediente es la misma pregunta con distinto eje. Una pantalla en vez de cinco casi idénticas. Columnas: Pendientes, En curso, Terminadas, Canceladas, Total. Formato `DataGrid`, sin librería de gráficos — coherente con los cinco reportes existentes.

**D16. Selector de criterio de fecha: creación o cierre.**
"Tareas hechas en 2026" es ambiguo: una tarea creada en 2025 y terminada en 2026 pertenece a un período según se mida carga entrante o trabajo completado. Ambas preguntas son legítimas; un radio button de dos opciones evita que los números no coincidan con lo que el usuario esperaba.
`FechaFin` es `null` para tareas Pendientes y En curso — solo se setea en `TerminarAsync` y `CancelarAsync`. Cuando el criterio es **cierre**, el reporte cuenta únicamente tareas cerradas (Terminadas y Canceladas); las columnas Pendientes y En curso quedan en cero, y la UI lo indica explícitamente en pantalla para que no se lea como falta de datos. Cuando el criterio es **creación**, entran los cuatro estados.

**D17. Fila "(sin asignar)" siempre visible.**
Las tareas sin ese clasificador (las viejas y las mal cargadas) se muestran en su propia fila. Si las filas no suman el total, el reporte pierde credibilidad.
Esto vale dentro del universo filtrado por el criterio de fecha elegido (D16): con criterio cierre, el universo son solo las tareas cerradas, y las filas siguen sumando el total de ESE universo, no del total histórico de tareas.

**D18. Rango de fechas obligatorio, default año en curso.**
Agrupar por expediente puede devolver cientos de filas. Mismo criterio que la decisión D9 del spec de Documentos, donde `ListarHistorialAsync` exige el año validado en el servicio (no solo por defecto en la UI) para justificar no paginar. Toda la agregación se hace con `GROUP BY` en SQL, nunca contando en memoria.

**D19. La lista de tareas no se migra a grilla.**
`TareaListView` usa `ItemsControl` con tarjetas por diseño, no es una tabla simulada. Los clasificadores se muestran como etiquetas dentro de la tarjeta.

**D20. Se actualiza el comentario de invariantes de `Tarea`.**
`src/StockApp.Domain/Entities/Tarea.cs:6-12` afirma hoy "sin FK a otras entidades del dominio (módulo independiente)". Deja de ser cierto con este cambio y debe reescribirse en el mismo commit que introduce las FKs.

**D21. `DatosClasificacionTarea` es un reemplazo total, no un update parcial.**
Los cinco campos se reemplazan enteros en cada llamada a `ReclasificarAsync`; `null` en un campo significa desasignar explícitamente ese clasificador, no "no tocar". Esta semántica es la que permite que el modal precargado (ver Presentation) funcione sin ambigüedad: lo que el Admin ve en el modal es exactamente lo que queda guardado.

**D22. Orden alfabético de filas en el reporte, con "(sin asignar)" siempre al final; buscador de expediente con mínimo de caracteres y tope de resultados.**
Las filas del reporte-matriz se ordenan alfabéticamente por el nombre del clasificador; la fila "(sin asignar)" (D17) queda siempre al final, sin importar el orden alfabético. El buscador de expediente (Presentation) exige un mínimo de caracteres antes de disparar la búsqueda y acota los resultados con un tope, mostrando un aviso de refinar la búsqueda si se alcanza. Motivo: `ListarActivosAsync(FiltroDocumentos)` (`src/StockApp.Infrastructure/Repositories/DocumentoAdministrativoRepository.cs:54-68`) no pagina ni limita — devuelve todo lo que matchea el filtro.

## Modelo de datos

Cuatro entidades nuevas, todas con el molde de `Categoria` (`Id:int`, `Nombre:string`, `Activo:bool = true`): `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`.

Cinco propiedades nuevas en `Tarea`, todas nullable, cada una con su navegación: `ZonaId`, `DimensionTematicaId`, `OrganismoResponsableId`, `OrigenFinanciamientoId`, `DocumentoAdministrativoId`.

Configuración EF en el `OnModelCreating` centralizado de `AppDbContext` (el repo no usa clases `IEntityTypeConfiguration` separadas). Una única migración aditiva que crea las cuatro tablas y agrega las cinco columnas nullable con sus FKs `Restrict`.

## Application

- Cuatro servicios de catálogo clonados de `CategoriaService` (nombre único, baja lógica, auditoría), sin invalidación de cache de reportes (D14).
- `ITareaService` suma: `ReclasificarAsync(int id, DatosClasificacionTarea datos)` y `ListarPorDocumentoAsync(int documentoId)`.
- `CrearAsync` acepta los cinco ids y valida que cada catálogo exista y esté activo, y que el documento sea de tipo Expediente y esté activo (D12). Una tarea que apunta a un catálogo dado de baja después se sigue mostrando con su valor.
- `ReclasificarAsync` corre exactamente la misma validación que `CrearAsync` (D12) — no hay atajo de validación para la vía de reclasificación — y respeta el guard de "sin cambios" (D10).
- `DatosClasificacionTarea` es un reemplazo total de los cinco campos, no un update parcial (D21): `ReclasificarAsync` sobreescribe cada campo con lo que reciba, incluido `null` como desasignación explícita.
- Nuevo servicio de reporte en `src/StockApp.Application/Reportes/` con su DTO de matriz.

## API

| Verbo | Ruta | Policy |
|---|---|---|
| POST | `/tareas` (request extendido con los 5 ids) | `GestionarTareas` |
| PUT | `/tareas/{id}/clasificacion` | `AdministrarTareas` |
| GET | `/tareas?documentoId={id}` | `GestionarTareas` |
| CRUD | 4 grupos de catálogo, molde `/categorias` | `GestionarTablasMaestras` |
| GET | `/reportes/tareas` | `VerReportes` |

Cada endpoint con su cliente tipado correspondiente en `StockApp.ApiClient`, siguiendo el patrón de implementar la misma interfaz que consume Application.

## Presentation

- Formulario de alta: cuatro combos con los activos de cada catálogo, más un buscador de expediente (no un combo — un municipio acumula cientos). Reusa `ListarActivosAsync(FiltroDocumentos)` con `Tipo = Expediente`. El buscador exige un mínimo de caracteres antes de disparar la búsqueda y acota los resultados con un tope, con aviso de refinar la búsqueda si se alcanza (D22) — `ListarActivosAsync` no pagina ni limita.
- Detalle de tarea: los cinco campos en solo lectura, más botón "Reclasificar". Sigue el patrón de visibilidad-solo-Admin de `MuestraCambioPrioridad` (`TareaFormViewModel.cs:82`, hoy `EsAdmin && !EsNuevaTarea && !_tareaEsTerminal`), pero **deliberadamente sin la condición `!_tareaEsTerminal`**: la fórmula del botón nuevo es `EsAdmin && !EsNuevaTarea` — consecuencia directa de D9, que exige que la reclasificación alcance también a tareas Terminadas y Canceladas.
- El botón "Reclasificar" abre un modal con los mismos cuatro combos y el mismo buscador de expediente que el formulario de alta, sujeto a las mismas validaciones (D12). El modal se abre precargado con los valores actuales de la tarea; dado que `DatosClasificacionTarea` es reemplazo total (D21), lo que el Admin ve precargado es exactamente lo que se guarda si no toca nada.
- Nueva vista `CatalogosTareaView` con `TabControl` de cuatro pestañas, un ítem de sidebar bajo `GestionarTablasMaestras`.
- Sección "Tareas vinculadas" en `DocumentoFormView`, gateada por `tareas.gestionar` (D13).
- Nueva vista de reporte en `Views/Reportes/`, `DataGrid`, junto a las cinco existentes.

## Testing

Se sigue la cobertura por capas que ya tiene el módulo (Domain, Application, Infrastructure, Api, ApiClient, Presentation, Presentation.UiTests). Guardianes que no pueden faltar:
- La validación de tipo Expediente y de documento activo (D12) — los guardianes cubren tanto `CrearAsync` como `ReclasificarAsync`, no solo el alta.
- Que un Operador no pueda reclasificar y un Admin sí — verificado con un usuario de permisos mixtos, no con Admin, porque Admin cortocircuita la evaluación antes de mirar los permisos.
- Que la reclasificación de una tarea terminal funcione y no cambie el estado (D9).
- Que reclasificar sin cambiar ningún valor no genere nota automática ni entrada de auditoría (D10).
- Guardián en `Presentation.UiTests` (con la View real, no solo el ViewModel) que verifique que el botón "Reclasificar" está visible para un Admin sobre una tarea Terminada (D9).
- Que la fila "(sin asignar)" aparezca y que las filas sumen el total (D17).
- Con criterio cierre, que las columnas Pendientes y En curso den cero y que la UI indique explícitamente que el criterio activo es cierre (D16).
- Que el rango de fechas obligatorio se valide en el servicio, no solo en la UI (D18).
- Gates de UI verificados en `Presentation.UiTests` con la View real: un test de ViewModel no custodia la visibilidad declarada en el XAML.

## Riesgos

- Es el primer vínculo entre dos módulos hasta hoy aislados. El riesgo se acota con D11 (dependencia en un solo sentido) y D13 (endpoint del lado de Tareas).
- La migración es aditiva y nullable, sin backfill, por lo que no toca datos de producción.
- Los ~145 tests de Tareas van a requerir ajuste de constructores/fixtures; los ~244 de Documentos no deberían tocarse — si alguno se rompe, es señal de que se filtró acoplamiento y hay que revisar D11.
