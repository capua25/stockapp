# Fase 5: Gestión — Tareas y Documentos (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 el módulo Gestión: Tareas (listado, formulario de alta y detalle, panel de clasificación y diálogo de reclasificar) y Documentos (listado, formulario de alta y detalle, panel de adjuntos), comparados contra mockups aprobados a 1280×760 y 1366×768.
**Architecture:** La fase corre EN PARALELO con las Fases 6 y 7, en su propio worktree (`~/workspace/stockapp-worktrees/ui-v2-fase5`, rama `feat/ui-v2-fase5`, que sale de `feat/ui-v2` después del merge de la 4e) y se integra al final con `--ff-only` o rebase. Cuatro bloques: (A) worktree, índice y línea base; (B) ViewModels y converters de Tareas y de Documentos, sin tocar las vistas (Tasks 1 y 2); (C) datos fijos, medición y TODOS los mockups de la fase con UN solo GATE 1 (Task 3); (D) las vistas v2 por módulo con sus UiTests y comparaciones (Tasks 4 y 5); (E) guardianes, suite, GATE 2 orgánico, review final e integración con los docs de cierre (Tasks 6 a 8). La fase NO toca el tema compartido: usa solo lo que `Themes/V2/`, `Controls/V2/` y los behaviors ya ofrecen.
**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), Microsoft.Playwright 1.63.0 (Chromium).
**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`: §5.1 (listado), §5.2 (formulario), §5.5 (diálogos), §6 (fidelidad, umbral §6.5), la fila "5 Gestión" de §7, la deuda abierta tras la 4d, §8.1 (tests que se mantienen) y §9 (riesgos: `AdjuntosPanelView` y `AdjuntosDocumentoPanelView` NO se unifican). Convenciones 1 a 36: `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-15-ui-v2-fase-4d-gastos.md`. La spec no tiene bloque de diseño de la Fase 5: las decisiones que lo reemplazan son las Q-1 a Q-10 (abajo, para el usuario ANTES de la Task 1) y las D1 a D20 (se confirman en el GATE 1).

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2-fase5` (rama `feat/ui-v2-fase5`), salvo los de la Task 0 y la Task 8 que dicen otro directorio. Nunca se toca `/home/capua25/workspace/stockapp` (main) ni `/home/capua25/workspace/stockapp-worktrees/ui-v2` fuera de la Task 8.
- CodeGraph: el worktree de la fase tiene su PROPIO índice `.codegraph/` (lo crea la Task 0 con `gentle-ai codegraph init`); nunca se copia ni se reusa el de otro checkout. Para código C# usar primero la CLI `codegraph` dentro del worktree (`codegraph query|explore|node|callers|impact`); el MCP `codegraph_explore` puede responder con el índice de otro checkout, así que solo se usa si `codegraph status` del MCP muestra la ruta `ui-v2-fase5`. `grep`/Read como fallback, y Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- **PROHIBIDO tocar el tema compartido:** `src/StockApp.Presentation/Themes/` (incluido `Themes/V2/ControlesV2.axaml`, `DataGridV2.axaml`, `Tokens.axaml`), `Controls/V2/` (incluido `ComponentesV2.axaml`), `App.axaml`, `docs/ui-v2/tokens.json`, `docs/ui-v2/mockups/_base/` y los behaviors existentes de `Behaviors/`. Si una task necesita algo de ahí, PARA, lo anota en el ledger con el cambio exacto y sigue con lo que el tema ya ofrece; el orquestador lo pasa a "Necesidades de tema compartido". Crear converters NUEVOS en `Converters/` sí está permitido (no son tema).
- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, peligro texto `#B91C1C`, texto primario `#1C1917`, secundario `#57534E`, terciario `#78716C`; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366); card de formulario de 760 (`V2AnchoFormularioMax`).
- Formato es-UY: fechas calendario (`Tarea.FechaLimite`, `DocumentoAdministrativo.FechaEmision`, guardadas como fecha "etiquetada" UTC) con `FechaCortaConverter` (nunca a hora local: correría un día); instantes (`NotaTarea.Fecha`, `EventoDocumento.Fecha`, `AdjuntoDocumentoDto.FechaAltaUtc`) con `FechaHoraLocalConverter` / `FechaUtcACortaConverter`; tamaños con `TamanoLegibleConverter`; nunca `CultureInfo.GetCultureInfo("es-UY")`.
- Sin cambios de dominio, DTOs, servicios de Application, endpoints, `ViewLocator`, `INavigationService` ni `ShellMainViewModel`. VMs que cambian, y solo por las excepciones E1 a E7 (abajo): `TareaListViewModel`, `TareaFormViewModel`, `DocumentoListViewModel` (+ `DocumentoFila`), `DocumentoFormViewModel`, `AdjuntosDocumentoPanelViewModel`. `TareaFila` NO cambia (la usa Inicio, ya aprobado). `ClasificacionTareaPanelViewModel` y `ReclasificarTareaDialogViewModel` no cambian.
- `x:DataType` / compiled bindings en las vistas y en cada `DataTemplate` (`vm:TareaFila`, `vm:DocumentoFila`, `dom:NotaTarea`, `dom:EventoDocumento`, `dom:Tarea`, `dto:AdjuntoDocumentoDto`, `vm:OpcionClasificador`, `dom:DocumentoAdministrativo`); constantes con `{x:Static}`.
- Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001, cascadea a MSB4025).
- Un solo botón primario visible por vista: "Nueva tarea" (listado de Tareas), "Guardar" (alta de tarea), "Nuevo documento" (listado de Documentos), "Guardar" (alta de documento), "Guardar cambios" (detalle de documento activo), "Aceptar" (diálogo de reclasificar). El detalle de tarea no tiene primario. Todo lo demás `secundario`, `ghost` o `peligro`.
- Prohibidos en las vistas v2: `Opacity`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:CampoFormulario` (el v1), `c:` del tema viejo, `Classes="primary|secondary|danger|caption|seccion|card|body"`, `MargenVista`, `DangerBrush`, `TextDecorations` (la cancelada va por token, no tachada), `TabControl`.
- Gates de UI con la View real en `Presentation.UiTests` (un test de VM no custodia un `IsVisible` del XAML). Permisos con un Operador de permisos MIXTOS, NUNCA Admin (cortocircuita): `new SesionFake(RolUsuario.Operador, Permisos.GestionarTareas)` (tareas sin documentos) y `new SesionFake(RolUsuario.Operador, Permisos.GestionarDocumentos)` (documentos sin tareas). Las acciones solo-Admin (cancelar tarea, prioridad, reclasificar, anular y reabrir documento, quitar adjunto) son ESTRUCTURALES (`AuthorizationService.PermisosEstructuralesAdmin`): su gate es el rol, y se prueban con Operador (oculto) y Admin (visible). Revocación en caliente por el camino real (`ManejadorAccesoRevocado.ManejarAsync` → `ShellMainViewModel.NotificarPermisosActualizados`) con la View dentro de `ShellMainView` (patrón de `AccesoRevocadoPantallasFinanzasTests`).
- `ArbolVisual.EsVisibleEnArbol` falla fuerte con null: nada de `x is not null && EsVisibleEnArbol(x)`; buscar por `x:Name` y dejar que un nombre que falta dé rojo.
- TDD en cada task: test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde. Un test que nace verde (caracterización) se declara así y su custodia es una mutación con rojo pegado.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-5 && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (líneas o fragmentos); `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato real: `ITareaService.ListarAsync()` → `Task<IReadOnlyList<Tarea>>` ordenada por `FechaCreacion` descendente (`TareaRepository.cs:38`), con `TomadaPor`, `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`, `DocumentoAdministrativo` y `Notas` cargados; `IDocumentoAdministrativoService.ListarActivosAsync/ListarHistorialAsync(FiltroDocumentos)` → `Task<IReadOnlyList<DocumentoAdministrativo>>` con `RegistradoPor` y `Eventos`; los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException` (sin servidor), `ReglaDeNegocioException` con el `detail` (409), `EntidadNoEncontradaException` (404).
- Mockups (§6.1): `docs/ui-v2/mockups/gestion/*.html` (carpeta NUEVA de la fase), el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css` + `gestion.css` (CSS propio de la fase, en la misma carpeta; `_base/` no se toca), UN `<div class="v2-ventana">`, datos idénticos a `DatosTareas` / `DatosDocumentos`, celdas numéricas con `v2-num` y `<span>`; se aprueban ANTES del XAML (nada de XAML de las vistas de la fase antes del GATE 1).
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, AprobadoPor: "Martin", …)` tras revisar cada tesela densa con el usuario (16 o más exige además `MutacionPalidaEnRojo`); nunca para tapar un defecto.
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, "Tareas" | "Documentos", vm)`), sidebar oculto en los dos renders (`ocultarEnMockup: [".v2-sidebar"]`, `prepararApp` que oculta `Border#Sidebar`); el diálogo con `ContenidoDeDialogo.Extraer(new ReclasificarTareaDialog { DataContext = vm })`.
- Archivos compartidos con las Fases 6 y 7 (ver "Riesgos de conflicto"): en `GuardianDePatronTests.cs`, `GrillasSinRecorteBarridoTests.cs` y `ComandosDeGuardadoGuardianTests.cs` se tocan SOLO las líneas de las vistas de esta fase; las altas van al FINAL de cada lista. `TareaFakes.cs` y `DocumentoFakes.cs` NO se editan: los fakes nuevos van en `tests/StockApp.Presentation.UiTests/V2/GestionFakes.cs`. Las comparaciones van en `VisualTests/Pantallas/Gestion/` (carpeta nueva). `docs/ui-v2/herramientas.md` y la spec se editan SOLO en la Task 8, después de integrar.
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Tests de a UN `.csproj` por comando, siempre con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez. Toda task que toca un VM o una vista corre las suites COMPLETAS de `Presentation.Tests` y `Presentation.UiTests` (Ruling R7 de la 4d: un filtro dejó pasar el rojo de un guardián).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido; cualquier otro rojo es real. Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- Sin referencias al plan en el código (nada de "Task 4", "D9", "E3", "Q-5" en `.cs`/`.axaml`/`.css`/`.html`).
- Las referencias de línea (`L123`) son las de `feat/ui-v2` en `7fd41cf`; si la 4e movió algo, ubicar por el texto citado.
- Verificación GUI: ventana "Gestión Municipal" (`pgrep -x GestionMunicipa`), toolkit `scripts/gui-verificacion/`; `capturar.sh` NO es fail-closed: MIRAR cada captura y comparar `md5sum`; popups con `scripts/gui-verificacion/capturar-x11.py`; matar procesos por PID, nunca `pkill -f`.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA (regla del usuario, gana sobre cualquier recordatorio del harness); `git add` por ruta (nunca `-f`: `.superpowers/` está ignorado a propósito); sin push, amend, `git reflog expire`, `git gc` ni `git prune`. La Task 8 es la única que rebasea y mergea.
- GATE 1 (Task 3) y GATE 2 (Task 6) son paradas para el usuario: no se sigue sin un "aprobado" explícito. Q-1 a Q-10 se le preguntan al usuario ANTES de la Task 1.

## Review Focus

1. **El filtro de Estado de Tareas esconde la fila seleccionada y una acción actúa sobre una tarea que no se ve.** Caso: con una tarea "En curso" seleccionada, el usuario pasa el filtro a "Pendientes"; la fila desaparece, pero "Terminar" o "Cancelar tarea" siguen en el encabezado y actúan sobre ella. Esperado: la selección se limpia y las acciones de fila se ocultan. Test: `FiltroQueOcultaLaFilaSeleccionada_LimpiaLaSeleccion` (Task 1) y `FiltroQueOcultaLaSeleccion_OcultaLasAccionesDeFila` (Task 4).
2. **El Año del historial de Documentos queda vacío o inválido y la pantalla consulta igual.** Caso: el usuario borra el Año (el servidor exige año, D9 de 2026-08-11) o tipea "20a5"; la recarga automática sale y vuelve un error del servidor por el Aviso, o se consulta con el año viejo. Esperado: sin consulta; el error del campo debajo de la caja. Test: `AnioVacioOInvalido_NoConsultaElHistorial` (Task 2) y `Historial_AnioInvalido_ErrorDebajoYSinConsulta` (Task 5).
3. **Perder el permiso con la pantalla abierta deja un vacío que miente.** Caso: un operador con Tareas abierto pierde `tareas.gestionar` (o `documentos.gestionar` con Documentos abierto); un 403 de otra acción refresca los permisos y la grilla vaciada muestra "Todavía no hay tareas". Esperado: "Sin permiso" en lugar de la card, como tras un 403 del listado (precedente P7-A de la 4d). Test: `NotificarPermisosActualizados_SinGestionarTareas_VaciaYMarcaSinPermiso` (Task 1), su par de Documentos (Task 2) y `RevocacionDentroDelShell_SinPermisoEnLugarDeLaCard` (Tasks 4 y 5).
4. **Escape en el diálogo de reclasificar no cancela, o Enter reclasifica sin querer.** Caso: el Admin abre "Reclasificar", cambia una zona y aprieta Escape; el diálogo no cierra o devuelve la selección cambiada y la tarea queda reclasificada. Esperado: Escape cierra y devuelve null (no se llama a `ReclasificarAsync`); Enter fuera de un combo confirma. Test: `Escape_CierraYDevuelveNull` y `Enter_EnElDialogo_ConfirmaConLosDatosDelPanel` (Task 4).
5. **La hora de una nota o de un evento sale corrida.** Caso: la v1 formatea el instante con `StringFormat` sin pasarlo a hora local (`TareaFormView.axaml` L105, `DocumentoFormView.axaml` L87); una nota agregada a las 10:00 de Uruguay se lee "13:00". Esperado: hora local con `FechaHoraLocalConverter` (Q-10). Test: `Nota_FechaEnHoraLocal` (Task 4) y `Evento_FechaEnHoraLocal` (Task 5), que comparan el texto pintado contra `FechaHoraLocalConverter` y, cuando el huso de la máquina no es UTC, contra el formateo crudo que NO debe salir.

---

## Decisiones previas a la ejecución (para el usuario, ANTES de la Task 1)

La spec no tiene diseño propio de la Fase 5 y el código actual no encaja tal cual en los patrones v2. El plan está redactado con la opción A de cada una (la recomendada). El orquestador las pregunta todas juntas antes de la Task 1; si el usuario elige B en alguna, se aplica el cambio indicado (con TDD) y se anota en el ledger.

| # | Pregunta (evidencia) | A (recomendada, la que implementa el plan) | B (alternativa) y qué cambia |
|---|---|---|---|
| Q-1 | **Listado de Tareas: grilla o secciones.** Hoy son cuatro secciones de tarjetas (`TareaListView.axaml`: Pendientes, En curso, Terminadas y, con el tilde "Mostrar canceladas", Canceladas). §5.1 define el listado v2 como card con filtros, GRILLA y pie de conteo; no hay patrón v2 de "lista de tarjetas con botones" ni su espejo CSS. | Grilla v2 (`DataGrid` con `DataGridV2`) de una sola colección, con un filtro de Estado en el cliente (convención 30): "Abiertas" (Pendientes y En curso, por defecto), "Pendientes", "En curso", "Terminadas", "Canceladas", "Todas". Reemplaza las secciones y el tilde. Columnas en D3. Recomendada: es el patrón de todos los listados ya aprobados y reusa `DataGridV2`, `ColumnaAuto` y la comparación sin piezas nuevas. | Conservar las cuatro secciones como listas de filas dentro de `TarjetaContenido`. Hay que diseñar una fila v2 con botones, su espejo CSS y un estilo `.ui2 TextBlock.inactivo` fuera de la grilla (necesidad de tema compartido 2). Cambian D3, D5, los mockups de listado y la Task 4. |
| Q-2 | **Dónde van las acciones de fila** (Tomar, Soltar, Terminar, Cancelar; Iniciar, Volver a pendiente, Finalizar, Anular…, Reabrir…). Hoy son botones dentro de cada tarjeta. §8.1 dice que `DocumentoListViewGatesTests` "se mantiene tal cual". | En el encabezado, sobre la fila seleccionada (precedente: Gastos 4d e Ingresos 4b), visibles según el estado de la fila y el rol; doble click = Ver. `DocumentoListViewGatesTests` se adapta SOLO en sus helpers (seleccionar la fila antes de buscar el botón; elegir Historial en el combo en vez de la pestaña), sin borrar ni debilitar ningún caso: la misma excepción a §8.1 que `MovimientoHistorialGatesTests` en la 3c-2. | Columna ACCIONES con los botones en cada fila (convención 28 y el fix de padding de celda). Los gates tests quedan intactos, pero la columna mide ~330 px (Ver + Soltar + Terminar + Cancelar) y a 1280 obliga a scroll horizontal. |
| Q-3 | **Pestañas Activos / Historial de Documentos.** No hay estilo v2 de `TabControl` (no existe `.ui2 TabControl` en `ControlesV2.axaml`) y la fase no puede tocar el tema. Hoy el historial se carga UNA sola vez al abrir la pestaña (`AbrirHistorialCommand`, `_historialCargado`). | Un combo "Ver" (Activos / Historial) en la barra de filtros. Año y Estado se ven solo en Historial (convención 15: sin hueco). Cada cambio de "Ver" consulta la vista elegida (sale el "solo la primera vez": siempre se ve lo último); el historial sigue sin consultarse al abrir la pantalla (D9 original). Una sola `ConsultaVigente` para la pantalla. | Mantener pestañas: necesita el estilo v2 de `TabControl` (necesidad de tema compartido 1). La fase queda bloqueada en la Task 5 hasta que el integrador lo agregue. |
| Q-4 | **Filtros de Documentos: botón "Buscar" o recarga automática.** Hoy hay dos "Buscar" (`BuscarActivosCommand`, `BuscarHistorialCommand`). La convención 34 dice que todo filtro que recarga usa `RecargaConDebounce`. | Recarga automática con `RecargaConDebounce` (300 ms; convención 34) al cambiar Ver, Tipo, texto, Año o Estado; salen los dos "Buscar" y `AbrirHistorialCommand` (sus tests se renombran y adaptan, convención 13). Un Año vacío o inválido no consulta (Review Focus 2). | Conservar "Buscar" en las dos vistas; sin debounce. Los tests de los comandos quedan. |
| Q-5 | **Estados de carga y revocación en caliente de los dos listados.** Hoy Tareas informa un 403 de la carga con "La sesión expiró…" en un Aviso (además del global) y Documentos lo traga en silencio; ninguno tiene "Cargando", "Sin conexión" ni "Sin permiso". | Las dos cargas pasan a `EjecutarCargaProtegidaAsync` + `ConsultaVigente` con `Cargando`, `SinConexion` (en línea, con Reintentar que relee los filtros de pantalla) y `SinPermiso` (convenciones 18 y 23). Además, al perder `tareas.gestionar` / `documentos.gestionar` en caliente, la pantalla vacía la grilla y marca "Sin permiso" (precedente P7-A de la 4d). | Solo la carga protegida; la revocación en caliente deja la grilla como está hasta la próxima consulta (que da 403 y recién ahí "Sin permiso"). Va a la deuda. |
| Q-6 | **Errores por campo en los formularios** (convención 31). Hoy Guardar solo se deshabilita, sin decir qué falta (`TareaFormViewModel.PuedeGuardar` L329: título; `DocumentoFormViewModel.PuedeGuardar` L233: número, descripción y fecha de emisión). | `INotifyDataErrorInfo` con estado "tocado" (patrón `IngresoFormViewModel`), con las MISMAS reglas: "El título es obligatorio."; "El número es obligatorio.", "La descripción es obligatoria.", "La fecha de emisión es obligatoria.". En un documento cerrado (campos deshabilitados) no hay errores. | Sin errores por campo; va a la deuda. |
| Q-7 | **Quitar un adjunto de documento.** Hoy quita sin confirmar y Agregar/Quitar se DESHABILITAN (`IsEnabled`), distinto del panel de Finanzas v2 (4a: confirma y OCULTA). | Confirmación con el mismo texto que Finanzas, "¿Quitar el adjunto \"{nombre}\"?" (constante propia en `AdjuntosDocumentoPanelViewModel`), y Agregar/Quitar se ocultan con `IsVisible`. Los paneles siguen separados (spec §9). | Sin confirmación; los botones se ocultan igual (es solo vista). |
| Q-8 | **Tonos de las insignias de estado** (hoy texto suelto con el nombre del enum crudo: "EnCurso", "EnProceso"). | Tarea: Pendiente neutro, En curso advertencia, Terminada éxito, Cancelada neutro (y la fila cancelada en texto terciario por token, convención 11); además "Vencida" peligro en la columna VENCE. Documento: Pendiente neutro, En proceso advertencia, Finalizado éxito, Anulado neutro (fila anulada en terciario). Textos legibles: "En curso", "En proceso". | Todas neutras salvo Vencida (peligro). |
| Q-9 | **Carga de los combos de clasificación al abrir "Nueva tarea" con el servidor caído.** La convención 22 pide `Alerta` en línea con Reintentar; hoy el panel avisa con `InformarAsync` (`ClasificacionTareaPanelViewModel.ManejarErrorAsync`) y el panel se comparte con el diálogo de reclasificar. | Sin cambio de lógica (sigue el Aviso); va a la deuda, igual que el formulario de ingresos de la 4b. El formulario se puede completar y guardar sin clasificación. | Convención 22 en el panel: `ErrorCarga` + `ReintentarCargaCommand` y una `Alerta` dentro del panel (toca `ClasificacionTareaPanelViewModel`, que se reusa en el diálogo). Suma una task de VM. |
| Q-10 | **Hora de notas y eventos.** La v1 formatea el instante UTC sin pasarlo a hora local (Review Focus 5). | `FechaHoraLocalConverter` ("dd/MM/yyyy HH:mm" en hora local) para `NotaTarea.Fecha` y `EventoDocumento.Fecha`. Se confirma en el GATE 2 agregando una nota a una hora conocida. | Mismo formato que la v1 (hora UTC). |

## Excepciones declaradas a "VMs intactos" (§3), con A en todas las Q

- **E1. `TareaListViewModel`, carga protegida y revocación (Q-5).** `CargarAsync` pide turno a una `ConsultaVigente` y va por `EjecutarCargaProtegidaAsync(…, MensajeSinPermisoTareas, turno)`; `AvisaSinConexionEnLinea => true`; `AlPerderConexion` vacía las listas; `NotificarPermisosActualizados` (override) vacía y marca sin permiso si ya no hay `tareas.gestionar`. Las acciones (Tomar, Soltar, Terminar, Cancelar) siguen con su `ManejarErrorAsync` sin cambios.
- **E2. `TareaListViewModel`, una colección filtrada en el cliente (Q-1, convención 30).** `Tareas` (todas) + `TareasView` (`DataGridCollectionView` con `Filter = EsVisible`), `FiltroEstado` (opción elegida), `CantidadVisible`, `FilaSeleccionada` (se limpia si el filtro la oculta). `Pendientes`, `EnCurso`, `Terminadas`, `Canceladas` y `MostrarCanceladas` SE CONSERVAN y se siguen llenando (sus tests no cambian; la Fase 9 los borra: deuda).
- **E3. `TareaFormViewModel`, error por campo de Título (Q-6).**
- **E4. `DocumentoListViewModel`, vista Activos/Historial con recarga automática y carga protegida (Q-3, Q-4, Q-5).** `VistaSeleccionada`, `MostrandoHistorial`, `RecargaConDebounce`, `CargarAsync` que carga la vista actual, `FilaSeleccionadaActivos`/`FilaSeleccionadaHistorial` y la calculada `FilaSeleccionada`, `HayFiltrosEnActivos`, revocación. En la Task 5 salen `BuscarActivosCommand`, `BuscarHistorialCommand` y `AbrirHistorialCommand`.
- **E5. `DocumentoFila.NumeroCompleto`** ("45/2026"): texto de presentación para la columna NÚMERO y su medición.
- **E6. `DocumentoFormViewModel`, errores por campo de Número, Descripción y Fecha de emisión (Q-6).**
- **E7. `AdjuntosDocumentoPanelViewModel`, confirmación al quitar (Q-7).**
- **No es excepción:** los textos y tonos de estado van por dos converters nuevos (`EstadoTareaConverter`, `EstadoDocumentoConverter`) que leen el enum o su nombre: los VMs no cambian para eso. Los títulos de alta y detalle ("Nueva tarea" / "Detalle de la tarea", "Nuevo documento" / "Detalle del documento", los textos reales de hoy) se resuelven en el XAML con dos `EncabezadoPagina` y `IsVisible`, como la v1 hacía con `HeaderVista`.

## Decisiones que se confirman al aprobar los mockups (Task 3, GATE 1)

Lo que ni la spec ni las Q fijan lo propone el mockup y lo decide el usuario en el GATE 1. Si el usuario cambia una, se aplica en los mockups, en los tests de VM que la fijan (rojo y verde) y en las Tasks 4 y 5.

- **D1. Encabezado del listado de Tareas:** `EncabezadoPagina` sin breadcrumb, `Titulo="Tareas"`, `Subtitulo="Trabajos de la intendencia: quién los toma y cuándo vencen."`. Acciones, de izquierda a derecha: "Ver" (secundario, habilitado con selección), "Tomar", "Soltar", "Terminar" (secundario, cada uno visible según `FilaSeleccionada.PuedeX`), "Cancelar tarea" (peligro, solo Admin) y "Nueva tarea" (primario, último).
- **D2. "Cancelar tarea" en vez de "Cancelar":** en el encabezado, al lado de "Nueva tarea", "Cancelar" a secas se lee como "salir de la pantalla". La confirmación conserva su texto: "¿Confirma cancelar la tarea \"{Titulo}\"?".
- **D3. Grilla de Tareas:** TÍTULO (`*`, `MinWidth` 200, elipsis + tooltip si se corta) · ESTADO (ancho fijo medido, `Insignia`, conjunto cerrado) · PRIORIDAD (`ColumnaAuto`) · VENCE (ancho fijo medido: fecha `FechaCortaConverter`, "—" sin fecha, y a la derecha `Insignia` "Vencida" peligro si `EsVencida`; celda compuesta como STOCK de Productos, convención 11) · TOMADA POR (`ColumnaAuto` + tooltip; vacía si nadie la tomó) · CLASIFICACIÓN (`ColumnaAuto` + tooltip, `EtiquetasClasificacion`). Orden por click en todas. Orden inicial el del servidor (creación descendente). Fila cancelada con `Classes.inactivo` en cada texto. Los anchos salen de la medición de la Task 3.
- **D4. Filtro de Tareas:** `BarraFiltros MostrarBusqueda="False"` con un solo `CampoFormulario` "Estado" (`ComboBox` de 160 con las seis opciones de Q-1). Sin "Limpiar" (un solo filtro).
- **D5. Estados del listado de Tareas** (dentro de la card, con el filtro a mano): "Cargando tareas…"; sin servidor, los textos de la convención 18 con "Reintentar"; tras un 403, `EstadoVacio` "Sin permiso" / "No tenés permiso para ver las tareas." EN LUGAR de la card; sin datos (`Tareas.Count == 0`) "Todavía no hay tareas" / "Cuando se cree una tarea, va a aparecer acá." con "Nueva tarea" secundario; sin resultados (`Tareas.Count > 0 && CantidadVisible == 0`) "No hay tareas en este estado" / "Probá con otro estado.".
- **D6. Pie de Tareas:** "N tareas" (`ConteoConverter`, `'tarea|tareas'`) sobre `CantidadVisible`, oculto sin filas visibles o sin conexión.
- **D7. Formulario de Tareas (alta):** breadcrumb "Tareas / Nueva tarea" (primer tramo = `VolverCommand`); card de 760; sección **Datos de la tarea**: Título (requerido, ancho completo), Descripción (opcional, multilínea de 80, ancho completo), Fecha límite (opcional, primera columna); sección **Clasificación (opcional)**: el panel. Pie FIJO fuera del scroll (convención 27): "Volver" (secundario) y "Guardar" (primario, `ComandoDeGuardado`), con la `Alerta` de `MensajeError` arriba del pie.
- **D8. Panel de clasificación:** grilla de dos columnas: Zona · Dimensión; Organismo responsable · Origen de financiamiento; Expediente a ancho completo, con su texto de ayuda en `texto-secundario` debajo (`MensajeBuscadorExpediente`). Etiquetas reales con "(opcional)". El mismo panel en el formulario (columnas de 347) y en el diálogo (columnas que da el ancho medido).
- **D9. Formulario de Tareas (detalle):** breadcrumb "Tareas / Detalle de la tarea"; insignia de estado (D-tono de Q-8) a la derecha del título; sección **Datos de la tarea** con pares etiqueta/valor de solo lectura (Título, Descripción, Prioridad, Vence, Tomada por) en `CampoFormulario` con un `TextBlock` `cuerpo`; sección **Clasificación** con los cinco valores ("—" si no hay) y "Reclasificar" (secundario, a la derecha del título de la sección, solo Admin); sección **Prioridad** (solo Admin y tarea no terminal): combo de 160 + "Actualizar prioridad" (secundario); sección **Notas**: hilo (texto `cuerpo` + fecha `texto-secundario` en hora local, Q-10, separadas por el separador de grilla), "Nueva nota" (multilínea de 60) y "Agregar nota" (secundario). Pie fijo con solo "Volver". Sin primario.
- **D10. Diálogo "Reclasificar tarea":** `Window` de 480 (o el ancho que dé la medición para que las dos columnas del panel no corten "Organismo responsable (opcional)"), `Border x:Name="Contenido" Classes="ui2"` con padding 24, el texto "Elegí los clasificadores. Dejar un campo vacío lo desasigna." (`cuerpo`), el panel y los botones "Cancelar" (secundario, `IsCancel`) y "Aceptar" (primario, `IsDefault`) a la derecha. Enter con el foco en un combo abre el desplegable (el combo se come el Enter; el diálogo no tiene comando para `EnterEjecutaComando`): Enter confirma desde los demás controles.
- **D11. Encabezado del listado de Documentos:** `Titulo="Documentos administrativos"`, `Subtitulo="Expedientes, oficios y suministros, y en qué estado está cada trámite."`. Acciones: "Ver" (secundario, con selección), "Iniciar", "Volver a pendiente", "Finalizar" (secundario), "Anular…" (peligro, solo Admin; abre el detalle, como hoy), "Reabrir…" (secundario, solo Admin; abre el detalle) y "Nuevo documento" (primario).
- **D12. Filtros de Documentos:** fila única. Activos: Ver (140) · Tipo (160) · el buscador de `BarraFiltros` ("Número, descripción…"). Historial: Ver (140) · Año (`NumericUpDown` 100) · Tipo (160) · Estado (160: Todos, Finalizado, Anulado) · buscador. Dos `BarraFiltros` (una por vista, ligadas a sus propiedades de siempre) que se alternan con `MostrandoHistorial`. Los anchos los confirma la medición.
- **D13. Grillas de Documentos** (dos `DataGrid` iguales, una por vista, alternadas con `IsVisible`; cada una con su `SelectedItem`): TIPO (`ColumnaAuto`) · NÚMERO (`ColumnaAuto`, `NumeroCompleto`) · DESCRIPCIÓN (`*`, `MinWidth` 200, tooltip) · EMISIÓN (`ColumnaAuto` `NoCede`, `FechaCortaConverter`) · ESTADO (fijo medido, `Insignia`) · REGISTRADO POR (`ColumnaAuto` + tooltip). Fila anulada con `Classes.inactivo`.
- **D14. Estados de Documentos:** "Cargando documentos…"; sin servidor (convención 18); "Sin permiso" / "No tenés permiso para ver los documentos."; Activos sin datos y sin filtros "Todavía no hay documentos en trámite" / "Cuando se registre un documento, va a aparecer acá." con "Nuevo documento" secundario; Activos con filtros o Historial sin filas "Ningún documento coincide con los filtros" / "Probá con otro año, tipo o texto.". Pie "N documentos" (`'documento|documentos'`) sobre la colección de la vista.
- **D15. Formulario de Documentos (alta):** breadcrumb "Documentos administrativos / Nuevo documento"; sección **Datos del documento**: Tipo (combo) · Número (requerido); Año (`NumericUpDown`) · Fecha de emisión (requerido); Descripción (requerido, multilínea de 80, ancho completo). Pie fijo: "Volver" + "Guardar" (primario).
- **D16. Formulario de Documentos (detalle):** breadcrumb "Documentos administrativos / Detalle del documento"; a la derecha del título la insignia de estado y las transiciones visibles (Iniciar, Volver a pendiente, Finalizar secundarios; Anular peligro; Reabrir secundario). Secciones: **Datos del documento** (los mismos campos, deshabilitados por tokens si el documento está cerrado; "Registrado por: {nombre}" en `texto-secundario` arriba de los campos); **Historial del trámite** (eventos en hora local + "Nueva nota" + "Agregar nota"); **Adjuntos** (el panel, sin título de sección propio: el panel trae "Adjuntos"); **Tareas vinculadas** (solo con `PuedeVerTareasVinculadas`: título de la tarea + insignia de estado de tarea, sin navegación, como hoy). Pie fijo: "Volver" y, si `PuedeEditar`, "Guardar cambios" (primario).
- **D17. Panel de adjuntos de documento:** el mismo diseño que `AdjuntosPanelView` v2 de la 4a (título "Adjuntos" `titulo-seccion` + "Agregar" secundario; filas de 40 con separador: ícono por tipo, nombre con elipsis y tooltip si se corta, tamaño legible, fecha dd/MM/yyyy local, "Ver" ghost y "Quitar" ghost), vacío "Sin adjuntos. PDF, JPG o PNG hasta 10 MB.". Agregar visible con `PuedeAgregar`, Quitar con `PuedeQuitar`. Raíz con `Classes="ui2"`.
- **D18. Sin mockup propio** (patrones ya aprobados, custodiados por UiTests): la `Alerta` de error del servidor en los dos formularios, los diálogos `ConfirmacionDialog` (cancelar tarea, quitar adjunto) y `PedirTextoDialog` (motivo de anular y reabrir), que ya se compararon en la Fase 1 con su texto genérico. Un UiTest mide que el texto más largo de cada confirmación entra (convención 17).
- **D19. Datos fijos:** vencimientos lejos de "hoy" (30/09/2026 para la vencida, 2030 para el resto) porque `TareaFila` calcula con el reloj real (sin reloj inyectable: no se cambia por esto, precedente D15 de la 4d).
- **D20. Selección tras recargar:** una recarga (acción sobre la fila, cambio de filtro o de vista) reemplaza las filas y la grilla suelta la selección; no se re-selecciona (precedente D17 de la 4d).

## Convenciones heredadas que aplican (de `herramientas.md`)

6, 10, 11, 12, 13, 14, 15, 18, 19, 20, 21, 22 (solo en lo que ya existe), 23, 25 (los modales siguen siendo `Window` con servicio), 27, 30, 31, 34, 35. NO aplican: 16 (no hay formulario de sidebar), 26 (sin fila de carga), 28 (ninguna columna se oculta por permiso), 29 (`AdjuntosDocumentoPanelViewModel` no tiene gates de sesión: Agregar depende del estado del documento y Quitar del rol estructural), 32 (no hay exporte), 33, 36 (no hay solo lectura por permiso: quien entra tiene el permiso del módulo).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 8)

Se numeran en la Task 8 con los números siguientes al último de `herramientas.md` DESPUÉS de integrar las Fases 6 y 7 (las tres fases agregan convenciones en paralelo).

- **C-A. Acciones de fila en el encabezado según la fila seleccionada:** cada botón con `IsVisible="{Binding FilaSeleccionada.PuedeX, FallbackValue=False}"` y `CommandParameter="{Binding FilaSeleccionada}"`; "Ver" con `IsEnabled` sobre `FilaSeleccionada` no nula; doble click en una fila = Ver. Con dos grillas alternadas (una por vista) cada una tiene su `SelectedItem` y el VM expone una `FilaSeleccionada` calculada según la vista.
- **C-B. Selector de vista en lugar de pestañas:** no hay `TabControl` v2; una pantalla con dos vistas usa un combo "Ver" en la barra de filtros, una `BarraFiltros` por vista y una sola `ConsultaVigente`.
- **C-C. Estado de dominio a texto y tono:** un converter por enum con dos instancias, `Texto` y `Variante`, que acepta el valor del enum o su nombre (`EstadoTareaConverter`, `EstadoDocumentoConverter`).

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Converters/EstadoTareaConverter.cs` | Estado de tarea → texto legible y tono de `Insignia` (Q-8). |
| `src/StockApp.Presentation/Converters/EstadoDocumentoConverter.cs` | Ídem para documentos. |
| `tests/StockApp.Presentation.Tests/Converters/EstadoTareaConverterTests.cs`, `EstadoDocumentoConverterTests.cs` | Los cuatro estados de cada uno, por enum y por nombre. |
| `tests/StockApp.Presentation.UiTests/V2/GestionFakes.cs` | `TareasFake` y `DocumentosFake` (pendiente con `TaskCompletionSource`, falla, 403, registro de filtros) y `AdjuntosDocumentoFake`. |
| `tests/StockApp.Presentation.UiTests/V2/TareaListViewV2Tests.cs`, `TareaFormViewV2Tests.cs`, `ReclasificarTareaDialogV2Tests.cs` | Views reales de Tareas. |
| `tests/StockApp.Presentation.UiTests/V2/DocumentoListViewV2Tests.cs`, `DocumentoFormViewV2Tests.cs` | Views reales de Documentos (incluye el panel de adjuntos). |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DatosTareas.cs`, `DatosDocumentos.cs`, `DatosGestionTests.cs` | Datos fijos de los 25 mockups. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/TareasComparacionTests.cs`, `TareaFormComparacionTests.cs`, `DocumentosComparacionTests.cs`, `DocumentoFormComparacionTests.cs` | 50 comparaciones (25 × 2 tamaños). |
| `docs/ui-v2/mockups/gestion/*.html` y `gestion.css` | Los 25 mockups (D-mockups en la Task 3) y su CSS propio. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs` | E1, E2. |
| `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs` | E3. |
| `src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs` | E4, E5. |
| `src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs` | E6. |
| `src/StockApp.Presentation/ViewModels/Documentos/AdjuntosDocumentoPanelViewModel.cs` | E7. |
| `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml` (+ `.cs`), `TareaFormView.axaml` (+ `.cs`), `ClasificacionTareaPanelView.axaml`, `ReclasificarTareaDialog.axaml` (+ `.cs` solo si hace falta) | Reescritas en v2. |
| `src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml` (+ `.cs`), `DocumentoFormView.axaml` (+ `.cs`), `AdjuntosDocumentoPanelView.axaml` | Reescritas en v2. |
| Tests heredados (convención 13) | `TareaListViewModelTests`, `TareaFormViewModelTests`, `DocumentoListViewModelTests`, `DocumentoFormViewModelTests`, `AdjuntosDocumentoPanelViewModelTests` (agregan casos; Documentos renombra los de Buscar/AbrirHistorial); `GuardianDePatronTests` (solo las líneas de las vistas de la fase); `DocumentoListViewGatesTests` (solo helpers, Q-2); y, solo si fallan y con el motivo en el ledger: `TareaListViewTests`, `TareaFormViewTests`, `ReclasificarTareaDialogFlujoTests`, `DocumentoListViewTests`, `DocumentoFormViewGatesTests`, `NumericUpDownVistasRealesTests`, `SignoNegativoBadgeTests` (sitio de Tareas), `EspaciadoBotonesEnFilaDeGridTests`, `ButtonGhostContrasteTests`, `GrillasSinRecorteBarridoTests` (escenarios nuevos al final). |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 8, después de integrar). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln --blame-hang-timeout 5m`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh Tarea`, `scripts/ui-v2/comparar.sh Documento`, `scripts/ui-v2/comparar.sh Reclasificar` y abrir `artifacts/ui-v2/comparacion/index.html`.

Ledger: `.superpowers/sdd/2026-10-17-ui-v2-fase-5-gestion/progress.md` (se escribe en el worktree `ui-v2` junto con este plan y la Task 0 lo copia al worktree de la fase, porque `.superpowers/` no viaja con git). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa. Arriba de todo, las respuestas del usuario a Q-1 a Q-10.

---

## Bloque A — Worktree, índice y línea base

### Task 0: Worktree propio de la fase, índice CodeGraph y línea base

**Files:**
- Create (local, no se commitea): el worktree `/home/capua25/workspace/stockapp-worktrees/ui-v2-fase5` y la copia del ledger.

**Interfaces:**
- Consumes: `feat/ui-v2` con la Fase 4e mergeada.
- Produces: rama `feat/ui-v2-fase5`; BASE (hash) y la línea base de la suite anotadas en el ledger.

- [ ] **Step 1: Precondición — la 4e está en `feat/ui-v2`**

Run: `git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 log --oneline -15 feat/ui-v2` y `grep -n "Fase 4e" /home/capua25/workspace/stockapp-worktrees/ui-v2/docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md | head -5`
Expected: el cierre de la 4e en el log y "4e" en el `**Estado:**` de la spec. Si no está, PARAR y avisar al orquestador: la fase sale de `feat/ui-v2` DESPUÉS de la 4e.

- [ ] **Step 2: Crear el worktree y la rama**

```bash
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 worktree add /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5 -b feat/ui-v2-fase5 feat/ui-v2
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5 status --short --branch
```

Expected: `## feat/ui-v2-fase5` sin cambios. Nunca en `/tmp`.

- [ ] **Step 3: Índice CodeGraph propio**

```bash
gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5 && codegraph status
```

Expected: `Project: /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5` con archivos y nodos indexados. No copiar `.codegraph/` de otro checkout.

- [ ] **Step 4: Copiar el ledger**

```bash
mkdir -p /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5/.superpowers/sdd
cp -r /home/capua25/workspace/stockapp-worktrees/ui-v2/.superpowers/sdd/2026-10-17-ui-v2-fase-5-gestion /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5/.superpowers/sdd/
```

- [ ] **Step 5: Build y línea base**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5 && dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 0 errores; todo verde salvo los falsos `.ods` y el intermitente conocido (por nombre). Anotar en el ledger: `BASE: <hash de HEAD>`, el total por proyecto y la ruta del worktree. Sin commit.

---

## Bloque B — ViewModels y converters, sin tocar las vistas

### Task 1: Tareas — carga protegida, colección filtrada, revocación, error de Título y converter de estado (E1, E2, E3, Q-8)

**Depende de:** las respuestas a Q-1, Q-5, Q-6, Q-8 (el plan implementa A).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs`, `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs`
- Create: `src/StockApp.Presentation/Converters/EstadoTareaConverter.cs`, `tests/StockApp.Presentation.Tests/Converters/EstadoTareaConverterTests.cs`
- Modify (agregar casos): `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs`, `TareaFormViewModelTests.cs`

**Interfaces:**
- Consumes: `ViewModelBase` (`EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `AvisaSinConexionEnLinea`, `AlPerderConexion`, `MarcarSinPermiso`, `NotificarPermisosActualizados`, `SinPermiso`, `SinConexion`, `Cargando`, `ReintentarCargaCommand`), `ConsultaVigente`, `Permisos.GestionarTareas`, `TareaFila` (sin cambios).
- Produces (las usan las Tasks 3 y 4):
  - `TareaListViewModel`: `public const string MensajeSinPermisoTareas = "No tenés permiso para ver las tareas.";` · `public ObservableCollection<TareaFila> Tareas { get; }` · `public DataGridCollectionView TareasView { get; }` · `public IReadOnlyList<OpcionEstadoTarea> FiltrosEstado { get; }` (orden: Abiertas, Pendientes, En curso, Terminadas, Canceladas, Todas) · `[ObservableProperty] OpcionEstadoTarea _filtroEstado` (default `FiltrosEstado[0]`) · `[ObservableProperty] TareaFila? _filaSeleccionada` · `public int CantidadVisible { get; private set; }` · `public bool PuedeGestionarTareas` (getter sobre la sesión: Admin o `GestionarTareas`) · `internal bool EsVisible(object item)` · `public override void NotificarPermisosActualizados()`. `CargarAsync()`, `NuevaCommand`, `VerDetalleCommand`, `TomarCommand`, `SoltarCommand`, `TerminarCommand`, `CancelarCommand` sin cambio de firma.
  - `public sealed record OpcionEstadoTarea(string Nombre, IReadOnlyList<EstadoTarea> Estados);` en el mismo archivo.
  - `TareaFormViewModel : INotifyDataErrorInfo`: `public const string MensajeTituloObligatorio = "El título es obligatorio.";` · `public string? ErrorDe(string propiedad)` · `public void MarcarTocado(string propiedad)` · `GetErrors`, `HasErrors`, `ErrorsChanged` (con el nombre `nameof(Titulo)`). Sin errores en modo detalle (`EsNuevaTarea == false`).
  - `public sealed class EstadoTareaConverter : IValueConverter` con `public static readonly EstadoTareaConverter Texto`, `Variante` e `Inactivo` (bool: true solo para Cancelada; lo usa `Classes.inactivo` de las celdas).

- [ ] **Step 1: Tests (rojo)**

`EstadoTareaConverterTests`:

```csharp
[Theory]
[InlineData(EstadoTarea.Pendiente, "Pendiente", VarianteInsignia.Neutro)]
[InlineData(EstadoTarea.EnCurso, "En curso", VarianteInsignia.Advertencia)]
[InlineData(EstadoTarea.Terminada, "Terminada", VarianteInsignia.Exito)]
[InlineData(EstadoTarea.Cancelada, "Cancelada", VarianteInsignia.Neutro)]
public void CadaEstado_TieneSuTextoYSuTono(EstadoTarea estado, string texto, VarianteInsignia variante)
{
    Assert.Equal(texto, EstadoTareaConverter.Texto.Convert(estado, typeof(string), null, CultureInfo.InvariantCulture));
    Assert.Equal(texto, EstadoTareaConverter.Texto.Convert(estado.ToString(), typeof(string), null, CultureInfo.InvariantCulture));
    Assert.Equal(variante, EstadoTareaConverter.Variante.Convert(estado, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture));
}

[Theory]
[InlineData(EstadoTarea.Pendiente, false)]
[InlineData(EstadoTarea.EnCurso, false)]
[InlineData(EstadoTarea.Terminada, false)]
[InlineData(EstadoTarea.Cancelada, true)]
public void Inactivo_SoloLaCancelada(EstadoTarea estado, bool esperado)
    => Assert.Equal(esperado, EstadoTareaConverter.Inactivo.Convert(estado, typeof(bool), null, CultureInfo.InvariantCulture));

[Fact] public void ValorDesconocido_TextoVacioNeutroYActivo() // null y "Otro" → "", Neutro y false
[Fact] public void ConvertBack_Lanza() // NotSupportedException
```

`TareaListViewModelTests` (usar el `Crear` existente del archivo; los fakes del archivo con `Mock<ITareaService>`; un helper `TareaDe(id, estado)`):
- `CargarAsync_LlenaTareasYTambienLasCuatroColeccionesDeAntes`: 4 tareas (una por estado) → `Tareas.Count == 4`, `Pendientes.Count == 1`, `EnCurso.Count == 1`, `Terminadas.Count == 1`, `Canceladas.Count == 1`.
- `FiltroPorDefecto_Abiertas_MuestraPendientesYEnCurso`: `FiltroEstado.Nombre == "Abiertas"`, `CantidadVisible == 2`, `TareasView.Cast<TareaFila>()` solo Pendiente y EnCurso.
- `FiltroCanceladas_MuestraSoloLasCanceladas` y `FiltroTodas_MuestraLasCuatro`.
- `FiltroQueOcultaLaFilaSeleccionada_LimpiaLaSeleccion` (Review Focus 1): selecciona la EnCurso, `FiltroEstado = FiltrosEstado[1]` (Pendientes) → `FilaSeleccionada` null.
- `FiltroQueNoOcultaLaSeleccion_LaConserva`.
- `CargarAsync_ServidorCaido_SinConexionSinAvisoYVacia`: servicio lanza `ServidorNoDisponibleException` tras una carga buena → `SinConexion`, `Tareas` y `Pendientes` vacías, `InformarAsync` nunca llamado.
- `CargarAsync_Un403_SinPermisoConElMensajeYSinAviso`: `SinPermiso`, `MensajeSinPermiso == TareaListViewModel.MensajeSinPermisoTareas`, `InformarAsync` nunca llamado.
- `DosCargasEnOrdenInverso_LaViejaNoPisaLaNueva`: dos `TaskCompletionSource<IReadOnlyList<Tarea>>`; la segunda termina primero con 1 tarea, la primera después con 3 → `Tareas.Count == 1`.
- `ReintentarCargaCommand_TrasUnaCaida_VuelveAConsultar`: 2 llamadas a `ListarAsync`, `SinConexion` false, filas cargadas.
- `NotificarPermisosActualizados_SinGestionarTareas_VaciaYMarcaSinPermiso` (Review Focus 3): sesión Operador con `GestionarTareas`, carga 2 tareas, la sesión deja de devolver `GestionarTareas` en `PermisosActuales` (con el doble de sesión que ya use el archivo), `NotificarPermisosActualizados()` → `Tareas.Count == 0`, `SinPermiso`, mensaje de Tareas.
- `NotificarPermisosActualizados_ConGestionarTareas_NoTocaNada`: filas intactas, `SinPermiso` false.

`TareaFormViewModelTests`:
- `Alta_TituloSinTocar_SinError`: `CargarParaCrear()` → `ErrorDe(nameof(Titulo))` null, `HasErrors` false.
- `Alta_TocarTituloVacio_MuestraElError`: `MarcarTocado(nameof(Titulo))` → `ErrorDe(...) == TareaFormViewModel.MensajeTituloObligatorio`; `GetErrors(nameof(Titulo))` lo contiene.
- `Alta_EscribirYBorrarElTitulo_MuestraElErrorYLevantaErrorsChanged`: suscripto a `ErrorsChanged`, `Titulo = "x"; Titulo = "";` → error presente y al menos un evento con `PropertyName == nameof(Titulo)`.
- `Alta_TituloCompleto_SinErrorYGuardarHabilitado`.
- `Detalle_NuncaMuestraErrorDeTitulo`: `CargarParaVer(tarea)` con título vacío + `MarcarTocado` → `ErrorDe` null.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~TareaListViewModelTests|FullyQualifiedName~TareaFormViewModelTests|FullyQualifiedName~EstadoTareaConverterTests"` → FAIL de compilación. Pegar en el ledger.

- [ ] **Step 2: `EstadoTareaConverter`**

Constructor privado con el modo (texto, variante o inactivo). `Convert` acepta `EstadoTarea` o `string` (con `Enum.TryParse<EstadoTarea>`); textos y tonos de la tabla del Step 1; desconocido → `""` / `VarianteInsignia.Neutro`; `ConvertBack` lanza `NotSupportedException`. Doc-comment de una línea con el mapeo.

- [ ] **Step 3: `TareaListViewModel`**

- Campo `private readonly ConsultaVigente _consultas = new();`.
- `CargarAsync()`: `var turno = _consultas.Empezar(); await EjecutarCargaProtegidaAsync(async () => { var tareas = await _service.ListarAsync(); if (!turno.EsLaUltima) return; <llenar> }, MensajeSinPermisoTareas, turno);`. `<llenar>`: limpia y carga `Tareas` y las cuatro de siempre con el mismo `switch` de hoy. Se borra el `try/catch` con `ManejarErrorAsync` de la carga (las acciones lo conservan: siguen llamando `CargarAsync` adentro de su propio `try`).
- `protected override bool AvisaSinConexionEnLinea => true;` · `protected override void AlPerderConexion() => VaciarListas();` (`Tareas` y las cuatro).
- `TareasView = new DataGridCollectionView(Tareas) { Filter = EsVisible };` en el constructor; `Tareas.CollectionChanged += (_, _) => Recalcular();` (patrón `IngresosViewModel` L125-158: `CantidadVisible` con el mismo predicado y la selección oculta se limpia).
- `partial void OnFiltroEstadoChanged(OpcionEstadoTarea value) { TareasView.Refresh(); Recalcular(); }`.
- `EsVisible(object item) => item is TareaFila f && FiltroEstado.Estados.Contains(f.Tarea.Estado);`.
- `NotificarPermisosActualizados()`: `base.NotificarPermisosActualizados(); if (!PuedeGestionarTareas) { VaciarListas(); MarcarSinPermiso(MensajeSinPermisoTareas); }`.

- [ ] **Step 4: `TareaFormViewModel`**

Patrón `IngresoFormViewModel` L59-119 con un solo campo validado (`nameof(Titulo)`): `_tocados`, `ErrorDe` (solo en alta y tocado), `GetErrors`, `HasErrors`, `MarcarTocado`, `partial void OnTituloChanged(string value) => AlCambiarCampo(nameof(Titulo));`. `CargarParaCrear()` limpia `_tocados`. `PuedeGuardar` no cambia.

- [ ] **Step 5: Verde, build, suites completas y mutaciones (QUITANDO)**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
```

Expected: verde (las vistas v1 siguen compilando: nada de lo que bindean cambió). Mutaciones, cada una con su rojo pegado:
- 1a: quitar `if (!turno.EsLaUltima) return;` → rojo `DosCargasEnOrdenInverso_LaViejaNoPisaLaNueva`.
- 1b: quitar el override `AvisaSinConexionEnLinea` → rojo `CargarAsync_ServidorCaido_SinConexionSinAvisoYVacia`.
- 1c: quitar la limpieza de la selección oculta en `Recalcular` → rojo `FiltroQueOcultaLaFilaSeleccionada_LimpiaLaSeleccion`.
- 1d: quitar `MarcarSinPermiso(MensajeSinPermisoTareas);` del override → rojo `NotificarPermisosActualizados_SinGestionarTareas_VaciaYMarcaSinPermiso`.
- 1e: quitar `Pendientes.Add(fila)` (o el `case` entero) del `switch` → rojo `CargarAsync_LlenaTareasYTambienLasCuatroColeccionesDeAntes` y el heredado que lo cubra.
- 1f: quitar `_tocados.Add(propiedad)` de `MarcarTocado` → rojo `Alta_TocarTituloVacio_MuestraElError`.
- 1g: quitar el brazo `EnCurso` de la tabla de textos del converter → rojo `CadaEstado_TieneSuTextoYSuTono(EnCurso…)`.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs src/StockApp.Presentation/Converters/EstadoTareaConverter.cs tests/StockApp.Presentation.Tests/Converters/EstadoTareaConverterTests.cs tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs
git commit -m "feat(ui-v2): tareas con carga protegida, filtro de estado en el cliente y error de título por campo"
```

### Task 2: Documentos — vista Activos/Historial con recarga automática, carga protegida, errores por campo, confirmación al quitar y converter (E4 a E7, Q-3 a Q-8)

**Depende de:** las respuestas a Q-3 a Q-8.

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs` (incluye `DocumentoFila`), `DocumentoFormViewModel.cs`, `AdjuntosDocumentoPanelViewModel.cs`
- Create: `src/StockApp.Presentation/Converters/EstadoDocumentoConverter.cs`, `tests/StockApp.Presentation.Tests/Converters/EstadoDocumentoConverterTests.cs`
- Modify (agregar casos): `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs`, `DocumentoFormViewModelTests.cs`, `AdjuntosDocumentoPanelViewModelTests.cs`

**Interfaces:**
- Consumes: lo de la Task 1 en `ViewModelBase`; `RecargaConDebounce` (convención 34); `Permisos.GestionarDocumentos`.
- Produces (Tasks 3 y 5):
  - `DocumentoFila.NumeroCompleto => $"{Numero}/{Anio}"`.
  - `DocumentoListViewModel`: `public const string MensajeSinPermisoDocumentos = "No tenés permiso para ver los documentos.";` · `public IReadOnlyList<OpcionVistaDocumentos> Vistas { get; }` (Activos, Historial) · `[ObservableProperty] OpcionVistaDocumentos _vistaSeleccionada` · `public bool MostrandoHistorial => VistaSeleccionada.EsHistorial` · `[ObservableProperty] DocumentoFila? _filaSeleccionadaActivos` y `_filaSeleccionadaHistorial` · `public DocumentoFila? FilaSeleccionada` (calculada según la vista, notificada al cambiar cualquiera de las tres) · `public bool HayFiltrosEnActivos` · `public bool PuedeGestionarDocumentos` · `internal TimeSpan EsperaDebounce { get; set; } = TimeSpan.FromMilliseconds(300);` · `internal Task _tareaDebounce = Task.CompletedTask;` · `CargarAsync()` (carga la vista ACTUAL: Activos o Historial) · `CargarHistorialAsync()` (se conserva pública) · `public override void NotificarPermisosActualizados()`. `BuscarActivosCommand`, `BuscarHistorialCommand` y `AbrirHistorialCommand` siguen en esta task (los borra la Task 5 junto con la vista v1 que los bindea).
  - `public sealed record OpcionVistaDocumentos(string Nombre, bool EsHistorial);`.
  - `DocumentoFormViewModel : INotifyDataErrorInfo`: `MensajeNumeroObligatorio = "El número es obligatorio."`, `MensajeDescripcionObligatoria = "La descripción es obligatoria."`, `MensajeFechaEmisionObligatoria = "La fecha de emisión es obligatoria."`; `ErrorDe`, `MarcarTocado`, `GetErrors`, `HasErrors`, `ErrorsChanged` sobre `nameof(Numero)`, `nameof(Descripcion)`, `nameof(FechaEmisionSeleccionada)`; sin errores cuando `!PuedeEditarCampos`.
  - `AdjuntosDocumentoPanelViewModel.MensajeConfirmarQuitar(string nombreArchivo) => $"¿Quitar el adjunto \"{nombreArchivo}\"?"` (static).
  - `EstadoDocumentoConverter` con `Texto`, `Variante` e `Inactivo` (Pendiente "Pendiente" Neutro, EnProceso "En proceso" Advertencia, Finalizado "Finalizado" Exito, Anulado "Anulado" Neutro; `Inactivo` true solo para Anulado).

- [ ] **Step 1: Tests (rojo)**

`EstadoDocumentoConverterTests`: la misma forma que `EstadoTareaConverterTests` con la tabla de arriba.

`DocumentoListViewModelTests` (el `Crear` del archivo con `EsperaDebounce = TimeSpan.Zero` en los nuevos):
- `DocumentoFila_NumeroCompleto_EsNumeroBarraAnio`: `"45/2026"`.
- `CargarAsync_PorDefecto_ConsultaActivosYNoElHistorial`.
- `CambiarAHistorial_ConsultaElHistorialConElAnioActual`: `VistaSeleccionada = Vistas[1]`, `await vm._tareaDebounce` → `ListarHistorialAsync` con `Anio == DateTime.UtcNow.Year`.
- `VolverAActivos_VuelveAConsultarActivos` (2 llamadas a `ListarActivosAsync`: carga inicial y la vuelta).
- `CambiarTipoYTextoEnActivos_UnaSolaConsulta`: con `EsperaDebounce = TimeSpan.FromMilliseconds(50)`, dos cambios seguidos → 2 llamadas en total (inicial + una).
- `AnioVacioOInvalido_NoConsultaElHistorial` (Review Focus 2): en Historial, `FiltroHistorialAnio = null` → sin nueva llamada; y con `HayErroresDeEntrada` en true (vía `IConErroresDeEntrada`) → sin llamada.
- `FiltrosPuestosAntesDeLaCargaInicial_NoProgramanConsulta` (convención 35).
- `DosConsultasEnOrdenInverso_LaViejaNoPisaLaNueva` (dos `TaskCompletionSource` del historial).
- `CargarAsync_Un403_SinPermisoConElMensaje` · `CargarAsync_ServidorCaido_SinConexionYVaciaLaVistaActual` · `Reintentar_ConsultaConLosFiltrosDePantalla` (cambia el tipo después de la caída y Reintentar lo manda).
- `FilaSeleccionada_SigueALaVistaActual`: con selección en las dos, cambia según `VistaSeleccionada`, con `PropertyChanged` de `FilaSeleccionada`.
- `HayFiltrosEnActivos_ConTipoOTexto`.
- `NotificarPermisosActualizados_SinGestionarDocumentos_VaciaYMarcaSinPermiso` (Review Focus 3, Operador con `GestionarDocumentos` que lo pierde).

`DocumentoFormViewModelTests`:
- `Alta_CamposSinTocar_SinErrores` · `Alta_TocarNumeroVacio_MuestraSuError` · `Alta_TocarDescripcionVacia_MuestraSuError` · `Alta_BorrarLaFecha_MuestraSuError` (con `ErrorsChanged` de `FechaEmisionSeleccionada`) · `DetalleCerrado_NingunError` (Finalizado + `MarcarTocado` de los tres → todos null) · `DetalleActivo_BorrarNumero_MuestraSuError`.

`AdjuntosDocumentoPanelViewModelTests`:
- `Quitar_SinConfirmar_NoLlamaAlServicio`: `PreguntarAsync` devuelve false → `QuitarAsync` del servicio nunca.
- `Quitar_Confirmado_QuitaYRecarga`: con el texto `MensajeConfirmarQuitar("oficio.pdf")` == `"¿Quitar el adjunto \"oficio.pdf\"?"`.

Run el filtro de los cinco archivos → FAIL de compilación. Pegar.

- [ ] **Step 2: `EstadoDocumentoConverter`** (forma de la Task 1 Step 2).

- [ ] **Step 3: `DocumentoListViewModel`**

- `NumeroCompleto` en `DocumentoFila`.
- `_consultas = new ConsultaVigente()` (UNA para la pantalla, Q-3). `CargarAsync()`: `MostrandoHistorial ? CargarHistorialAsync() : CargarActivosAsync()` (privado, el cuerpo de hoy de `CargarAsync`). Cada consulta: turno → `EjecutarCargaProtegidaAsync(…, MensajeSinPermisoDocumentos, turno)` → `if (!turno.EsLaUltima) return;` → llenar su colección. `CargarHistorialAsync` no consulta si `FiltroHistorialAnio is null` o `HayErroresDeEntrada`. Se borra el `try/catch` de la carga; `ManejarErrorAsync` queda para Iniciar, Volver a pendiente y Finalizar.
- `AvisaSinConexionEnLinea => true`; `AlPerderConexion` vacía la colección de la vista actual.
- Debounce (convención 34): `private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);`; `private bool _recargaArmada;` que se prende en la primera `CargarAsync`, ANTES de pedir el listado (convención 35: lo que se puso antes no programa; un cambio durante la espera del listado gana por turno); `ProgramarRecarga()` → `if (_recargaArmada) _tareaDebounce = Recarga.Programar(CargarAsync);`. Lo llaman los `partial void On…Changed` de `VistaSeleccionada`, `FiltroActivosTipo`, `FiltroActivosTexto`, `FiltroHistorialTipo`, `FiltroHistorialAnio`, `FiltroHistorialTexto`, `FiltroHistorialEstado`. `OnVistaSeleccionadaChanged` además notifica `MostrandoHistorial` y `FilaSeleccionada`.
- `_historialCargado` se borra junto con `AbrirHistorialCommand` en la Task 5 (hasta entonces queda).
- `NotificarPermisosActualizados()`: como la Task 1 con `PuedeGestionarDocumentos` y `MensajeSinPermisoDocumentos`.

- [ ] **Step 4: `DocumentoFormViewModel` y `AdjuntosDocumentoPanelViewModel`**

Formulario: patrón de la Task 1 Step 4 con tres campos y la regla extra `if (!PuedeEditarCampos) return null;` en `ErrorDe`. `CargarParaCrear` y `CargarParaVerAsync` limpian `_tocados`. Panel: `QuitarAsync` empieza con `if (!await _confirmacion.PreguntarAsync(MensajeConfirmarQuitar(item.NombreArchivo))) return;`.

- [ ] **Step 5: Verde, build, suites completas y mutaciones (QUITANDO)**

Mismos comandos que la Task 1 Step 5. Mutaciones:
- 2a: quitar `if (!turno.EsLaUltima) return;` del historial → rojo `DosConsultasEnOrdenInverso_LaViejaNoPisaLaNueva`.
- 2b: quitar la guarda `FiltroHistorialAnio is null` → rojo `AnioVacioOInvalido_NoConsultaElHistorial`.
- 2c: quitar la guarda `HayErroresDeEntrada` → rojo el mismo test (segunda parte).
- 2d: quitar `ProgramarRecarga()` de `OnVistaSeleccionadaChanged` → rojo `CambiarAHistorial_ConsultaElHistorialConElAnioActual`.
- 2e: quitar la guarda `_recargaArmada` → rojo `FiltrosPuestosAntesDeLaCargaInicial_NoProgramanConsulta`.
- 2f: quitar `MarcarSinPermiso(...)` del override → rojo `NotificarPermisosActualizados_SinGestionarDocumentos_VaciaYMarcaSinPermiso`.
- 2g: quitar `if (!PuedeEditarCampos) return null;` → rojo `DetalleCerrado_NingunError`.
- 2h: quitar la línea de confirmación de `QuitarAsync` → rojo `Quitar_SinConfirmar_NoLlamaAlServicio`.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs src/StockApp.Presentation/ViewModels/Documentos/DocumentoFormViewModel.cs src/StockApp.Presentation/ViewModels/Documentos/AdjuntosDocumentoPanelViewModel.cs src/StockApp.Presentation/Converters/EstadoDocumentoConverter.cs tests/StockApp.Presentation.Tests/Converters/EstadoDocumentoConverterTests.cs tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoFormViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Documentos/AdjuntosDocumentoPanelViewModelTests.cs
git commit -m "feat(ui-v2): documentos con vista activos/historial, recarga automática, carga protegida y errores por campo"
```

---

## Bloque C — Datos fijos, medición y mockups (UN solo GATE 1 antes de todo XAML)

### Task 3: Datos fijos, medición y los 25 mockups de la fase — GATE 1 con el usuario

**Depende de:** Tasks 1 y 2.

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DatosTareas.cs`, `DatosDocumentos.cs`, `DatosGestionTests.cs`
- Create: `docs/ui-v2/mockups/gestion/gestion.css` y los 25 HTML de abajo
- Create y BORRAR en la misma task: `tests/StockApp.Presentation.VisualTests/MedicionTemporal5.cs`
- Modify (local): el ledger (anchos y altos medidos)

**Interfaces:**
- Consumes: los VMs de las Tasks 1 y 2 (solo miembros PÚBLICOS: VisualTests no ve los `internal`); `DatosShell.Crear(usuario, permisos, seccion, contenido)`; `DataGridColumnasAutoBehavior.AnchoMedido`; los converters de las Tasks 1 y 2.
- Produces: `DatosTareas` y `DatosDocumentos` (las comparaciones de las Tasks 4 y 5) y los anchos medidos del ledger (reemplazan a los de D3, D8, D10, D12 y D13 si difieren).
  - `DatosTareas`: `UsuarioAdmin`, `UsuarioOperador` (permisos `{ GestionarTareas }`), `Tareas` (6: dos Pendiente, una de ellas vencida el 30/09/2026; dos En curso tomadas por "mperez" y "jsosa"; una Terminada; una Cancelada; vencimientos del resto en 2030; clasificaciones variadas, una con expediente "Exp. 45/2026") y las fábricas `Listado()` (Admin, sin selección; la selección la pone el `prepararApp`), `ListadoOperador()`, `Cargando()`, `SinConexion()`, `SinPermiso()`, `SinTareas()`, `SinResultados()` (filtro Canceladas sin canceladas), `Nueva()`, `NuevaConErrorDeCampo()` (título tocado vacío), `Detalle()` (Admin, En curso, tres notas), `DetalleOperador()`, `Reclasificar()` (devuelve `ReclasificarTareaDialogViewModel` con el panel inicializado) — todas devuelven `ShellMainViewModel` salvo `Reclasificar`.
  - `DatosDocumentos`: `UsuarioAdmin`, `UsuarioOperador` (`{ GestionarDocumentos }`), `Activos` (6: Expediente/Oficio/Suministro en Pendiente y En proceso), `Historial` (4: Finalizado y Anulado, año 2026), `Eventos`, `Adjuntos` (uno "oficio-firmado.pdf", 245 760 bytes), `TareasVinculadas` (2), y `Listado()`, `ListadoHistorial()` (Vista Historial puesta ANTES de la carga), `ListadoOperador()`, `Cargando()`, `SinConexion()`, `SinPermiso()`, `SinDocumentos()`, `SinResultados()`, `Nuevo()`, `NuevoConErrorDeCampo()`, `Detalle()` (Admin, En proceso), `DetalleCerrado()` (Admin, Finalizado), `DetalleOperador()` (sin `GestionarTareas`: sin Tareas vinculadas).

- [ ] **Step 1: Datos fijos con sus tests (rojo → verde)**

`DatosGestionTests` fija lo que el mockup copia: `Tareas_FijanEstadoPrioridadVenceYClasificacion` (las seis filas con su estado, prioridad, `FechaCortaConverter.Formatear` del vencimiento y `EtiquetasClasificacion`), `Tareas_LaVencidaEsLaUnica` (`EsVencida` solo en una), `Documentos_FijanTipoNumeroEstadoYEmision`, `Notas_YEventos_FijanTextoYHoraLocal` (con `FechaHoraLocalConverter`), `MensajesDeConfirmacion_SonLosDelMockup` (`"¿Confirma cancelar la tarea \"…\"?"` del VM y `MensajeConfirmarQuitar`), y una aserción por fábrica de estado (`Cargando().… Cargando == true`, `SinPermiso()` con el mensaje, etc.). Rojo de compilación → implementar → verde.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~DatosGestionTests"`.

- [ ] **Step 2: Medir en headless (archivo temporal, no se commitea)**

`MedicionTemporal5.cs` siguiendo `MedicionTemporal4c` (plan de la 4c, Task 6 Step 1) y `MedicionTemporal4d` (ledger de la 4d), con el tema v2 a 1040 y 1126 de vista, `--logger "console;verbosity=detailed"`:
1. Grilla de Tareas con los datos fijos: `AnchoMedido` de PRIORIDAD, TOMADA POR y CLASIFICACIÓN; ancho de la insignia más ancha de ESTADO ("Cancelada" o "En curso") + 16; ancho de VENCE con "30/09/2026" + 8 + insignia "Vencida"; si entra sin scroll horizontal a los dos tamaños.
2. Grilla de Documentos: TIPO, NÚMERO, EMISIÓN, REGISTRADO POR; ESTADO ("Finalizado" / "En proceso"); si entra.
3. Encabezados: las acciones máximas simultáneas de cada listado entran en una línea con título y subtítulo (Tareas Admin con En curso: Ver, Soltar, Terminar, Cancelar tarea, Nueva tarea; Documentos Admin con En proceso: Ver, Volver a pendiente, Finalizar, Anular…, Nuevo documento).
4. Filtros de Documentos: la fila de Historial (Ver 140 · Año 100 · Tipo 160 · Estado 160 · buscador) entra en el interior de la card a 1040.
5. Formularios: alto de la card de alta y detalle de los dos (con los datos fijos), para fijar qué mockups scrollean y cuánto; ancho de las columnas del panel de clasificación en el formulario (712 de interior) y en el diálogo a 480 (432 de interior): que "Organismo responsable (opcional)" y "(dado de baja — id 12)" no se corten; si no entran a 480, el ancho mínimo que entra (D10).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~MedicionTemporal5" --logger "console;verbosity=detailed"`. Anotar en el ledger. Si algo de lo medido muestra que falta una pieza del tema (p. ej. el `AutoCompleteBox` v2 no da 36 de alto), anotarlo como necesidad de tema compartido y seguir con lo que hay.

- [ ] **Step 3: Mockups**

Partir de `finanzas/gastos*.html` (listado con acciones en el encabezado, estados, fila inactiva, insignias), `finanzas/gasto-*.html` (formulario de 760 con pie fijo y adjuntos), `finanzas/adjuntos-vacio.html` (panel) e `inventario/historial.html` (filtros con `CampoFormulario`, `NumericUpDown`, glifo de combo) y `dialogos/recalcular-stock.html` (diálogo con `AutoCompleteBox`). CSS nuevo SOLO en `gestion/gestion.css`. Cada mockup lleva `<title>Gestión: … (UI v2)</title>`, `<!-- Datos: DatosTareas.<Fábrica> -->` (o `DatosDocumentos`), el sidebar con su ítem activo, y los anchos MEDIDOS como `flex: 0 0 Npx`.

Tareas (sidebar "Tareas" activo):
- `tareas.html`: Admin, filtro "Abiertas", 4 filas, la En curso de "mperez" SELECCIONADA → Ver, Soltar, Terminar, Cancelar tarea y Nueva tarea; la vencida con su insignia; pie "4 tareas".
- `tareas-operador.html`: Operador, la Pendiente no vencida seleccionada → Ver, Tomar y Nueva tarea (sin Cancelar tarea).
- `tareas-cargando.html`, `tareas-sin-conexion.html`, `tareas-sin-permiso.html`, `tareas-vacio.html`, `tareas-sin-resultados.html` (D5).
- `tarea-nueva.html`: D7 con el panel vacío ("(ninguna)" en los cuatro combos), Guardar deshabilitado.
- `tarea-nueva-error-campo.html`: Título en rojo con "El título es obligatorio.".
- `tarea-detalle.html`: Admin, En curso (insignia advertencia), las secciones de D9 con las tres notas en hora local y "Reclasificar"; scrolleado según la medición (pie pegado al borde).
- `tarea-detalle-operador.html`: sin Reclasificar ni Prioridad.
- `reclasificar-tarea.html`: el área cliente del diálogo (D10) con la clasificación de `Reclasificar()` precargada.

Documentos (sidebar "Documentos" activo):
- `documentos.html`: Admin, Activos, 6 filas, una En proceso SELECCIONADA → Ver, Volver a pendiente, Finalizar, Anular…, Nuevo documento; pie "6 documentos".
- `documentos-historial.html`: Admin, Historial 2026, una Finalizado seleccionada → Ver, Reabrir…; la anulada en terciario; filtros de Historial.
- `documentos-operador.html`: Operador, la En proceso seleccionada → sin Anular….
- `documentos-cargando.html`, `documentos-sin-conexion.html`, `documentos-sin-permiso.html`, `documentos-vacio.html`, `documentos-sin-resultados.html` (D14).
- `documento-nuevo.html` (D15, Guardar deshabilitado hasta completar) y `documento-error-campo.html` (Número y Descripción en rojo).
- `documento-detalle.html`: Admin, En proceso: transiciones e insignia en el encabezado, eventos, el adjunto, dos tareas vinculadas con su insignia, "Guardar cambios"; scrolleado según la medición.
- `documento-detalle-cerrado.html`: Finalizado: campos deshabilitados, solo "Reabrir" en el encabezado, adjuntos sin Agregar ni Quitar, pie con solo "Volver".
- `documento-detalle-operador.html`: En proceso, operador sin `tareas.gestionar`: sin Anular, sin Quitar, SIN la sección Tareas vinculadas.

- [ ] **Step 4: Capturas y verificación de la herramienta**

```bash
for p in tareas tareas-operador tareas-cargando tareas-sin-conexion tareas-sin-permiso tareas-vacio tareas-sin-resultados \
         tarea-nueva tarea-nueva-error-campo tarea-detalle tarea-detalle-operador reclasificar-tarea \
         documentos documentos-historial documentos-operador documentos-cargando documentos-sin-conexion documentos-sin-permiso documentos-vacio documentos-sin-resultados \
         documento-nuevo documento-error-campo documento-detalle documento-detalle-cerrado documento-detalle-operador; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/gestion/$p.html artifacts/ui-v2/mockups/gestion-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/gestion/$p.html artifacts/ui-v2/mockups/gestion-$p@1366x768.png --tamano 1366x768
done
ls artifacts/ui-v2/mockups/gestion-*.png | wc -l
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 50 PNG con fecha de ahora; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 5: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal5.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 6: GATE 1 — aprobación del usuario (PARADA, una sola para toda la fase)**

PARAR. Presentar JUNTOS los 50 PNG (los dos tamaños), las respuestas a Q-1 a Q-10 que se aplicaron, la lista D1 a D20 y los anchos medidos. Señalar explícitamente: D2 ("Cancelar tarea"), D3 y D13 (columnas y si hay scroll horizontal), D5 y D14 (textos de los estados), D8 y D10 (panel en dos columnas y ancho del diálogo), D9 y D16 (cómo se ve el detalle de solo lectura y dónde van las transiciones), D17 (panel de adjuntos igual al de Finanzas), D18 (estados sin mockup) y cualquier necesidad de tema que haya salido en la medición. No seguir sin "aprobado" explícito. Si el usuario cambia una D o una Q, aplicarla en los mockups, en los tests de VM de las Tasks 1 y 2 que la fijan (rojo y verde) y en las Tasks 4 y 5, y volver a presentar. Registrar en el ledger: `Mockups Fase 5: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 7: Commit**

```bash
git add docs/ui-v2/mockups/gestion/ tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DatosTareas.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DatosDocumentos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DatosGestionTests.cs
git commit -m "docs(ui-v2): mockups aprobados de tareas y documentos, con sus datos fijos"
```

---

## Bloque D — Vistas v2 (arranca solo con los mockups aprobados)

### Task 4: Tareas v2 — listado, formulario, panel de clasificación y diálogo de reclasificar

**Depende de:** Tasks 1 y 3 (mockups aprobados).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml` (completo) y `.axaml.cs`, `TareaFormView.axaml` (completo) y `.axaml.cs`, `ClasificacionTareaPanelView.axaml` (completo), `ReclasificarTareaDialog.axaml` (completo; `.axaml.cs` sin cambios salvo que haga falta)
- Create: `tests/StockApp.Presentation.UiTests/V2/GestionFakes.cs`, `V2/TareaListViewV2Tests.cs`, `V2/TareaFormViewV2Tests.cs`, `V2/ReclasificarTareaDialogV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/TareasComparacionTests.cs`, `TareaFormComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (SOLO: quitar `TareaListView` y `TareaFormView` de la `InlineData` de `Vista_TieneHeaderVistaConElTituloEsperado` y de `VistasDeLaTanda`; quitar `ClasificacionTareaPanelView` de `VistasEmbebidas`; agregar las tres al FINAL de `VistasMigradasAV2`; agregar `"StockApp.Presentation/Views/Tareas/ReclasificarTareaDialog.axaml"` al final de `DialogosV2`)
- Modify, solo si fallan y con el motivo en el ledger (convención 13): `TareaListViewTests.cs`, `TareaFormViewTests.cs`, `ReclasificarTareaDialogFlujoTests.cs`, `SignoNegativoBadgeTests.cs` (sitio de Tareas: `BadgeEstado` → `Insignia`), `EspaciadoBotonesEnFilaDeGridTests.cs`, `ButtonGhostContrasteTests.cs`, `GrillasSinRecorteBarridoTests.cs` (escenario "Tareas" al FINAL de `Escenarios`)

**Interfaces:**
- Consumes: Task 1 (`TareasView`, `FiltrosEstado`, `FiltroEstado`, `FilaSeleccionada`, `CantidadVisible`, `Tareas`, `MensajeSinPermisoTareas`, `ErrorDe`, `MarcarTocado`, `MensajeTituloObligatorio`, `EstadoTareaConverter`); `TareaFila` (`Titulo`, `PrioridadTexto`, `FechaLimite`, `EsVencida`, `TomadaPorNombre`, `EtiquetasClasificacion`, `PuedeTomar`, `PuedeSoltar`, `PuedeTerminar`, `PuedeCancelar`, `Tarea.Estado`); `TareaFormViewModel` (`EsNuevaTarea`, `Titulo`, `Descripcion`, `FechaLimiteSeleccionada`, `EstadoTexto`, `TomadaPorNombre`, `ZonaTexto`, `DimensionTematicaTexto`, `OrganismoResponsableTexto`, `OrigenFinanciamientoTexto`, `DocumentoAdministrativoTexto`, `MuestraReclasificar`, `MuestraCambioPrioridad`, `PrioridadesDisponibles`, `PrioridadSeleccionada`, `Notas`, `NuevaNotaTexto`, `MensajeError`, `ClasificacionPanel`, `GuardarCommand`, `AgregarNotaCommand`, `CambiarPrioridadCommand`, `ReclasificarCommand`, `VolverCommand`); `ClasificacionTareaPanelViewModel` (sin cambios); `DatosTareas` (Task 3); `FechaCortaConverter`, `FechaHoraLocalConverter`, `ConteoConverter`, `ColumnaAuto`, `TooltipSiSeCorta`, `CalendarDatePickerFechaBehavior`, `ErroresDeEntradaBehavior`; `MontajeV2`, `ArbolVisual`, `SesionFake`, `ManejadorAccesoRevocado`.
- Produces: `GestionFakes` (`TareasFake : ITareaService` con `ListarPendiente()` (`TaskCompletionSource`), `FallarCon(Exception)`, registro de acciones; `DocumentosFake : IDocumentoAdministrativoService` con lo mismo más el registro de cada `FiltroDocumentos`; `AdjuntosDocumentoFake : IAdjuntoDocumentoService`) — la Task 5 los reusa. `x:Name` de las vistas: listado `Encabezado`, `BotonVer`, `BotonTomar`, `BotonSoltar`, `BotonTerminar`, `BotonCancelarTarea`, `BotonNueva`, `EstadoSinPermiso`, `Tarjeta`, `Filtros`, `CampoEstado`, `ComboEstado`, `Grilla`, `PieConteo`, `IndicadorCarga`, `EstadoSinConexion`, `BotonReintentar`, `EstadoSinTareas`, `BotonNuevaVacio`, `EstadoSinResultados`; formulario `EncabezadoAlta`, `EncabezadoDetalle`, `InsigniaEstado`, `Scroll`, `Tarjeta`, `CampoTitulo`, `CajaTitulo`, `CampoDescripcion`, `CampoFechaLimite`, `SeccionClasificacionAlta`, `PanelClasificacion`, `SeccionDatos`, `SeccionClasificacion`, `BotonReclasificar`, `SeccionPrioridad`, `ComboPrioridad`, `BotonActualizarPrioridad`, `SeccionNotas`, `ListaNotas`, `CajaNuevaNota`, `BotonAgregarNota`, `AlertaError`, `Pie`, `BotonVolver`, `BotonGuardar`; panel `ComboZona`, `ComboDimension`, `ComboOrganismo`, `ComboOrigen`, `BuscadorExpediente`, `MensajeExpediente`; diálogo `Contenido`, `CancelarButton`, `AceptarButton` (los dos últimos YA existen: el code-behind los usa).

- [ ] **Step 1: Fakes**

`V2/GestionFakes.cs` con los tres fakes de "Produces", fieles al contrato (Global Constraints). Sin tocar `TareaFakes.cs` ni `DocumentoFakes.cs`.

- [ ] **Step 2: UiTests (rojo)**

`TareaListViewV2Tests` (la View real; las de permisos dentro de `ShellMainView` con `ShellMainViewModel` y un Operador mixto `new SesionFake(RolUsuario.Operador, Permisos.GestionarTareas)`):
- `Raiz_TieneUi2_EncabezadoTareas_NuevaTareaEsElUnicoPrimario`.
- `Grilla_SeisColumnasEnOrden`: headers `["TÍTULO","ESTADO","PRIORIDAD","VENCE","TOMADA POR","CLASIFICACIÓN"]`.
- `FiltroEstado_PorDefectoAbiertas_ConteoYFilas`: `ComboEstado.SelectedItem` "Abiertas", filas visibles = 4 con los datos fijos, `PieConteo` "4 tareas".
- `SinSeleccion_AccionesDeFilaOcultasYVerDeshabilitado`.
- `SeleccionEnCurso_ComoAdmin_VerSoltarTerminarYCancelarTarea` (y `BotonTomar` oculto).
- `SeleccionPendiente_ComoOperadorMixto_VerYTomarSinCancelarTarea`.
- `FiltroQueOcultaLaSeleccion_OcultaLasAccionesDeFila` (Review Focus 1).
- `DobleClickEnUnaFila_NavegaAlDetalle` y `DobleClickEnElEncabezado_NoNavega`.
- `FilaCancelada_TextoInactivoEInsigniaNeutra` (filtro "Canceladas"; `Classes.Contains("inactivo")` en cada `TextBlock` de la fila y la insignia "Cancelada" `Neutro`).
- `FilaVencida_InsigniaVencidaPeligroEnVence` (y ninguna en las demás).
- `EstadoInsignia_ConTextoLegible`: "En curso" con `Advertencia`.
- `Cargando_IndicadorSinGrillaNiPie` · `SinConexion_EstadoConReintentarYFiltroAMano` · `Un403_SinPermisoEnLugarDeLaCard` · `SinTareas_VacioConNuevaTareaSecundario` · `SinResultados_VacioDelFiltro` (y NO el de sin tareas).
- `RevocacionDentroDelShell_SinPermisoEnLugarDeLaCard` (Review Focus 3): operador mixto con Tareas abierto; quitar `GestionarTareas` de la sesión fake y correr `ManejadorAccesoRevocado.ManejarAsync` → `EstadoSinPermiso` visible, `Tarjeta` oculta.
- `ClickReal_EnTomar_LlamaAlServicioYRecarga` (con la fila Pendiente seleccionada).
- `ClickReal_EnCancelarTarea_PideConfirmacionConElTextoDeSiempre`.

`TareaFormViewV2Tests`:
- `Alta_BreadcrumbTareasNuevaTarea_GuardarEsElUnicoPrimario`.
- `Alta_SeccionesDatosYClasificacion_PanelEnDosColumnas` (Zona y Dimensión en la misma fila; Expediente a todo el ancho).
- `Alta_TocarTituloYSalir_ElErrorDebajoDeLaCaja` (foco a otra caja → `DataValidationErrors.GetHasErrors(CajaTitulo)` y el texto `MensajeTituloObligatorio` visible).
- `Alta_ErrorDelServidor_AlertaArribaDelPie`.
- `Pie_FueraDelScroll_ADosTamanos` (convención 27: el borde inferior del `Scroll` no pasa el superior del `Pie`, a 1280×760 y 1366×768).
- `Detalle_Admin_ReclasificarYPrioridadVisibles` · `Detalle_OperadorMixto_SinReclasificarNiPrioridad` · `Detalle_AdminConTareaTerminada_ReclasificarSinPrioridad` (los dos gates de rol de siempre, ahora sobre la View v2).
- `Detalle_EstadoComoInsigniaConTextoLegible`.
- `Detalle_ClasificacionSinValor_MuestraGuion`.
- `Nota_FechaEnHoraLocal` (Review Focus 5): el texto pintado de la fecha de la nota == `FechaHoraLocalConverter.Instance.Convert(nota.Fecha, …)`; si `TimeZoneInfo.Local.BaseUtcOffset != TimeSpan.Zero`, además != `nota.Fecha.ToString("dd/MM/yyyy HH:mm")`.
- `BuscadorExpediente_OperadorSinDocumentos_MuestraElMensaje` (Operador con `GestionarTareas` y SIN `GestionarDocumentos`: tipear 3 caracteres → `MensajeExpediente` "No tenés permiso para buscar expedientes." visible).
- `Detalle_SinPrimario`.

`ReclasificarTareaDialogV2Tests`:
- `Contenido_TieneUi2YAceptarEsElUnicoPrimario` (`CancelarButton.IsCancel`, `AceptarButton.IsDefault`).
- `Escape_CierraYDevuelveNull` y `Enter_EnElDialogo_ConfirmaConLosDatosDelPanel` (Review Focus 4; Enter con el foco en `AceptarButton` o en el buscador, no en un combo).
- `PanelEnElDialogo_EtiquetasYValoresSinRecorte` (el guardián de texto recortado de `ComparacionVisual` lo cubre en la comparación; este test lo mide además con "(dado de baja — id 12)" elegido).

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~TareaListViewV2Tests|FullyQualifiedName~TareaFormViewV2Tests|FullyQualifiedName~ReclasificarTareaDialogV2Tests"` → rojo (nombres que faltan). Pegar.

- [ ] **Step 3: Comparaciones (rojo)**

`TareasComparacionTests` (7 mockups × 2) y `TareaFormComparacionTests` (5 × 2, el diálogo con `ContenidoDeDialogo.Extraer`), en el molde de `GastosComparacionTests`: `Comparar(vm, "gestion/tareas.html")` dentro del shell con el sidebar oculto; la selección de `tareas.html` y `tareas-operador.html` la pone el `prepararApp` (`Grilla.SelectedItem = …` por título) además de ocultar el sidebar; los scrolleados fijan el offset medido. Umbral por defecto. Rojo: la vista v1 no se parece.

- [ ] **Step 4: XAML**

Reescribir las cuatro vistas según los mockups aprobados (D1 a D10). Pautas que el mockup no muestra:
- Raíz `Classes="ui2"` en las tres vistas; en el diálogo, en `Border x:Name="Contenido"` (el `Window` con `Background="{DynamicResource V2SuperficieBrush}"`, como `PedirTextoDialog`).
- Listado: `Grid RowDefinitions="Auto,*"`: `EncabezadoPagina` con las acciones (C-A: `IsVisible="{Binding FilaSeleccionada.PuedeTomar, FallbackValue=False}"`, `CommandParameter="{Binding FilaSeleccionada}"`; "Cancelar tarea" con `FilaSeleccionada.PuedeCancelar`); `EstadoSinPermiso` en lugar de la card; `TarjetaContenido` con `BarraFiltros`, los estados (convención 18, `MultiBinding` con `BoolConverters.And`), la grilla (`ItemsSource="{Binding TareasView}"`, `SelectedItem="{Binding FilaSeleccionada}"`) y el pie.
- Celdas: cada `TextBlock` de celda lleva `Classes.inactivo="{Binding Tarea.Estado, Converter={x:Static conv:EstadoTareaConverter.Inactivo}}"` (convención 11; el estilo `.ui2 DataGridCell TextBlock.inactivo` ya existe).
- VENCE: `Grid ColumnDefinitions="Auto,Auto"` con el `TextBlock` (`FechaCortaConverter`) y la `Insignia` "Vencida" `Peligro` con `IsVisible="{Binding EsVencida}"`, 8 de separación.
- Formulario: `Grid RowDefinitions="*,Auto,Auto"` (convención 27): `ScrollViewer x:Name="Scroll"` con la columna de 760 (`Grid` `*` con `MaxWidth` `V2AnchoFormularioMax` y `UseLayoutRounding="False"`, como `GastoFormView`), la `Alerta` de `MensajeError` y el `Pie`. Dos `EncabezadoPagina` (alta / detalle) con `BreadcrumbOrigen="Tareas"`, `BreadcrumbComando="{Binding VolverCommand}"` y `BreadcrumbActual` "Nueva tarea" / "Detalle de la tarea". Notas con `FechaHoraLocalConverter` (Q-10).
- Code-behind del formulario: conservar el `DataContextChanged` de hoy (panel en alta) y agregar `CajaTitulo.LostFocus += (_, _) => (DataContext as TareaFormViewModel)?.MarcarTocado(nameof(TareaFormViewModel.Titulo));`.
- Code-behind del listado: conservar el `DataContextChanged` → `CargarAsync()` (el guardián `CargaProtegidaSinPermisoGuardianTests` lo descubre por ese patrón exacto) y agregar el doble click sobre una fila (`DataGridRow`, no el encabezado) → `VerDetalleCommand.Execute(FilaSeleccionada)`.

Build tras cada archivo. Correr el Step 2 y el Step 3 hasta verde. Una comparación que pase el umbral sin un defecto visible se diagnostica tesela por tesela; un tope mayor solo con `UmbralAprobado` firmado por el usuario.

- [ ] **Step 5: Guardianes, heredados y suites completas**

Editar `GuardianDePatronTests` (solo las líneas de "Files"). Correr las tres suites de UI, VM y Visual completas. Un heredado que falle se adapta (convención 13) con el motivo en el ledger y sin borrar casos: `TareaListViewTests` pasa a seleccionar la fila antes de buscar "Tomar"/"Cancelar tarea" y a usar el combo en vez del tilde; `SignoNegativoBadgeTests` busca la `Insignia` "Vencida" en vez de `BadgeEstado`. Agregar el escenario "Tareas" al FINAL de `GrillasSinRecorteBarridoTests.Escenarios` (con `Muchos<object>(i => new TareaFila(…))`) y correrlo.

- [ ] **Step 6: Mutaciones (QUITANDO), cada una con su rojo pegado**

- 4a: quitar `IsVisible="{Binding SinPermiso}"` de `EstadoSinPermiso` → rojo `Un403_SinPermisoEnLugarDeLaCard` y `RevocacionDentroDelShell_…`.
- 4b: quitar el `IsVisible` de `BotonCancelarTarea` → rojo `SeleccionPendiente_ComoOperadorMixto_VerYTomarSinCancelarTarea`.
- 4c: quitar `FallbackValue=False` de `BotonTomar` → rojo `SinSeleccion_AccionesDeFilaOcultasYVerDeshabilitado` (si queda verde, declararlo y custodiar con la 4d).
- 4d: quitar `SelectedItem="{Binding FilaSeleccionada}"` de la grilla → rojo `SeleccionEnCurso_ComoAdmin_…`.
- 4e: quitar el `Classes.inactivo` de la celda de TÍTULO → rojo `FilaCancelada_TextoInactivoEInsigniaNeutra`.
- 4f: quitar la `Insignia` "Vencida" de VENCE → rojo `FilaVencida_…` y `Tareas_IgualAlMockup`.
- 4g: quitar el `LostFocus` del code-behind → rojo `Alta_TocarTituloYSalir_ElErrorDebajoDeLaCaja`.
- 4h: quitar `Grid.Row="2"` del `Pie` (cae en la fila 0, encima del área del scroll) → rojo `Pie_FueraDelScroll_ADosTamanos`.
- 4i: quitar el `IsVisible` de `BotonReclasificar` → rojo `Detalle_OperadorMixto_SinReclasificarNiPrioridad`.
- 4j: quitar el `Converter={x:Static conv:FechaHoraLocalConverter.Instance}` de la fecha de la nota → rojo `Nota_FechaEnHoraLocal` (con huso distinto de UTC; si el runner es UTC, anotar que la custodia es la comparación visual con los datos fijos).
- 4k: quitar `IsCancel="True"` de `CancelarButton` → rojo `Escape_CierraYDevuelveNull`.
- 4l: quitar el doble click del code-behind → rojo `DobleClickEnUnaFila_NavegaAlDetalle`.
- 4m: quitar `typeof(TareaListView)` de `VistasMigradasAV2` → rojo `Guardian_CubreTodasLasVistasDelEnsamblado`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Tareas/ tests/StockApp.Presentation.UiTests/V2/GestionFakes.cs tests/StockApp.Presentation.UiTests/V2/TareaListViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/TareaFormViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/ReclasificarTareaDialogV2Tests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/TareasComparacionTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/TareaFormComparacionTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs <heredados adaptados, por ruta>
git commit -m "feat(ui-v2): tareas en v2 (listado, formulario, clasificación y diálogo de reclasificar)"
```

### Task 5: Documentos v2 — listado, formulario y panel de adjuntos

**Depende de:** Tasks 2, 3 y 4 (`GestionFakes`).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Documentos/DocumentoListView.axaml` (completo) y `.axaml.cs`, `DocumentoFormView.axaml` (completo) y `.axaml.cs`, `AdjuntosDocumentoPanelView.axaml` (completo)
- Modify: `src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs` (borrar `BuscarActivosCommand`, `BuscarHistorialCommand`, `AbrirHistorialCommand` y `_historialCargado`, que la vista v1 bindeaba)
- Create: `tests/StockApp.Presentation.UiTests/V2/DocumentoListViewV2Tests.cs`, `V2/DocumentoFormViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DocumentosComparacionTests.cs`, `DocumentoFormComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs` (renombrar y adaptar los tests de los comandos borrados, convención 13: `BuscarActivosCommand_RecargaLaColeccionActivos` → `CambiarTipoEnActivos_RecargaLaColeccionActivos`; `BuscarActivosCommand_ConFiltroTipoElegido_…` → `CambiarTipoEnActivos_LoEnviaAlServicio`; `BuscarHistorialCommand_*` → `CambiarFiltroEnHistorial_*`; `AbrirHistorialCommand_PrimeraVez_…` → `ElegirHistorial_ConsultaElHistorial`; `AbrirHistorialCommand_SegundaVez_NoVuelveALlamar…` → `ElegirHistorialDosVeces_ConsultaCadaVez` (Q-3: cambia el contrato, a propósito); el de M9 (Finalizar e historial) → `Finalizar_YElegirHistorial_ElCerradoAparece`)
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (SOLO: quitar `DocumentoListView` y `DocumentoFormView` de la `InlineData` y de `VistasDeLaTanda`, `AdjuntosDocumentoPanelView` de `VistasEmbebidas`, y `DocumentoFormView` de `VistasExentasPorPrimariosMutuamenteExcluyentesSinVm` si queda sin uso; agregar las tres al FINAL de `VistasMigradasAV2`)
- Modify (no compila sin `BuscarHistorialCommand`): `tests/StockApp.Presentation.UiTests/NumericUpDownVistasRealesTests.cs` — el caso del Año del historial elige Historial en el combo y, en vez de "Buscar bloqueado con tooltip", afirma el error debajo de la caja y que NO hubo consulta (mismo error, otro efecto: convención 13).
- Modify (helpers, Q-2): `tests/StockApp.Presentation.UiTests/DocumentoListViewGatesTests.cs` — `Montar` selecciona la fila del caso (`vm.FilaSeleccionadaActivos = vm.Activos[0]`) y `SeleccionarSolapaHistorial` pasa a elegir `vm.Vistas[1]` en el combo y esperar `_tareaDebounce`; las aserciones y los casos NO cambian.
- Modify, solo si fallan y con el motivo en el ledger: `DocumentoListViewTests.cs`, `DocumentoFormViewGatesTests.cs` (§8.1: solo helpers), `EspaciadoBotonesEnFilaDeGridTests.cs`, `ButtonGhostContrasteTests.cs`, `GrillasSinRecorteBarridoTests.cs` (escenario "Documentos" al FINAL)

**Interfaces:**
- Consumes: Task 2 (todo lo de "Produces"), `GestionFakes` (Task 4), `DatosDocumentos` (Task 3), `EstadoDocumentoConverter`, `EstadoTareaConverter` (tareas vinculadas), `IconoAdjuntoConverter`, `TamanoLegibleConverter`, `FechaUtcACortaConverter`, `FechaHoraLocalConverter`, `FechaCortaConverter`, `NumericUpDownValidacionBehavior`, `ConfirmacionServiceFake`.
- Produces: `x:Name` listado `Encabezado`, `BotonVer`, `BotonIniciar`, `BotonVolverAPendiente`, `BotonFinalizar`, `BotonAnular`, `BotonReabrir`, `BotonNuevo`, `EstadoSinPermiso`, `Tarjeta`, `FiltrosActivos`, `FiltrosHistorial`, `ComboVistaActivos`, `ComboVistaHistorial`, `ComboTipoActivos`, `ComboTipoHistorial`, `CampoAnio`, `SelectorAnio`, `ComboEstadoHistorial`, `GrillaActivos`, `GrillaHistorial`, `PieActivos`, `PieHistorial`, `IndicadorCarga`, `EstadoSinConexion`, `BotonReintentar`, `EstadoSinDocumentos`, `BotonNuevoVacio`, `EstadoSinResultados`; formulario `EncabezadoAlta`, `EncabezadoDetalle`, `InsigniaEstado`, `BotonIniciar`, `BotonVolverAPendiente`, `BotonFinalizar`, `BotonAnular`, `BotonReabrir`, `Scroll`, `Tarjeta`, `RegistradoPor`, `CampoTipo`, `CampoNumero`, `CajaNumero`, `CampoAnio`, `CampoFechaEmision`, `CampoDescripcion`, `CajaDescripcion`, `SeccionHistorial`, `ListaEventos`, `CajaNuevaNota`, `BotonAgregarNota`, `SeccionAdjuntos`, `SeccionTareasVinculadas`, `AlertaError`, `Pie`, `BotonVolver`, `BotonGuardar`, `BotonGuardarCambios`; panel `BotonAgregar`, `SinAdjuntos`, `ListaAdjuntos`.

- [ ] **Step 1: UiTests (rojo)**

`DocumentoListViewV2Tests` (operador mixto `new SesionFake(RolUsuario.Operador, Permisos.GestionarDocumentos)`; Admin solo para las acciones estructurales visibles):
- `Raiz_TieneUi2_EncabezadoDocumentos_NuevoDocumentoEsElUnicoPrimario`.
- `Grillas_SeisColumnasEnOrden` (las dos).
- `PorDefecto_ActivosVisiblesHistorialOcultoYSinConsultarlo` (D9).
- `ElegirHistorial_MuestraSusFiltrosYSuGrilla_SinHueco` (Año y Estado visibles solo ahí; la fila de filtros no deja hueco al volver a Activos).
- `Historial_AnioInvalido_ErrorDebajoYSinConsulta` (Review Focus 2: tipear "20a5" → error de `NumericUpDownValidacionBehavior` debajo y `DocumentosFake` sin llamada nueva).
- `SeleccionEnProceso_ComoAdmin_VerVolverAPendienteFinalizarYAnular` · `SeleccionEnProceso_ComoOperadorMixto_SinAnular` · `SeleccionFinalizado_EnHistorial_ComoAdmin_VerYReabrir` · `SeleccionFinalizado_ComoOperadorMixto_SinReabrir`.
- `AccionesSiguenALaGrillaVisible` (seleccionar en Activos, pasar a Historial sin selección → acciones ocultas).
- `DobleClickEnUnaFila_NavegaAlDetalle`.
- `FilaAnulada_TextoInactivoEInsigniaNeutra` · `EstadoInsignia_EnProcesoConTextoLegible`.
- Estados: `Cargando_…`, `SinConexion_…`, `Un403_SinPermisoEnLugarDeLaCard`, `SinDocumentos_VacioConNuevoDocumento`, `ConFiltros_SinResultados` (y no el de sin documentos).
- `RevocacionDentroDelShell_SinPermisoEnLugarDeLaCard` (Review Focus 3).
- `ClickReal_EnFinalizar_LlamaAlServicioYRecarga` · `ClickReal_EnAnular_NavegaAlDetalle` (como hoy).

`DocumentoFormViewV2Tests`:
- `Alta_BreadcrumbYGuardarUnicoPrimario` · `Alta_TocarNumeroYDescripcion_ErroresDebajo` · `Alta_ErrorDelServidor_AlertaArribaDelPie` · `Pie_FueraDelScroll_ADosTamanos`.
- `DetalleActivo_GuardarCambiosEsElPrimario_YCamposHabilitados` · `DetalleCerrado_CamposDeshabilitadosSinGuardarCambios_ConReabrirParaAdmin`.
- `Detalle_TransicionesEnElEncabezadoSegunEstadoYRol` (Operador mixto En proceso: Volver a pendiente y Finalizar, sin Anular).
- `Detalle_OperadorConTareasGestionar_TareasVinculadasVisible` y `Detalle_OperadorSinTareasGestionar_TareasVinculadasOculta` (Operador con `GestionarDocumentos` + `GestionarTareas` y sin la segunda; nunca Admin).
- `RevocacionDeTareasGestionar_ConLaFichaAbierta_OcultaTareasVinculadas` (dentro del shell, camino real).
- `Evento_FechaEnHoraLocal` (Review Focus 5, como `Nota_FechaEnHoraLocal`).
- Panel: `Adjuntos_DocumentoActivoComoOperador_AgregarVisibleQuitarOculto` · `Adjuntos_DocumentoActivoComoAdmin_AgregarYQuitarVisibles` · `Adjuntos_DocumentoCerrado_SinAgregarNiQuitar` · `Adjuntos_TamanoLegibleYFechaCorta` ("240 KB", fecha local) · `Adjuntos_ClickEnQuitar_PideConfirmacionConElTexto` · `Adjuntos_SinArchivos_TextoDeVacio`.
- `Anular_PideElMotivoConPedirTexto` (el `PedirTextoAsync` de siempre, "Anular documento").

Run el filtro de los dos archivos → rojo. Pegar.

- [ ] **Step 2: Comparaciones (rojo)**

`DocumentosComparacionTests` (8 × 2) y `DocumentoFormComparacionTests` (5 × 2), como la Task 4 Step 3 (selección por `prepararApp`).

- [ ] **Step 3: Borrar los comandos de la v1 y adaptar sus tests**

Borrar `BuscarActivosCommand`, `BuscarHistorialCommand`, `AbrirHistorialCommand` y `_historialCargado` del VM (Q-3, Q-4) y renombrar/adaptar sus tests (lista de "Files"). Correr `DocumentoListViewModelTests` → verde con los nombres nuevos (el rojo previo: no compila la vista v1 hasta el Step 4; hacer Step 3 y Step 4 antes de buildear).

- [ ] **Step 4: XAML**

Según los mockups aprobados (D11 a D17), con las pautas de la Task 4 Step 4 y además:
- Listado: dos `BarraFiltros` (`FiltrosActivos` con `TextoBusqueda="{Binding FiltroActivosTexto}"`, `FiltrosHistorial` con `FiltroHistorialTexto`), cada una con su combo "Ver" ligado a `VistaSeleccionada` (C-B); dos grillas con su `SelectedItem` (`FilaSeleccionadaActivos` / `FilaSeleccionadaHistorial`) y su pie; las acciones del encabezado sobre `FilaSeleccionada` (C-A). El code-behind pierde `OnSolapaSeleccionada`; conserva el `DataContextChanged` → `CargarAsync()`; agrega el doble click.
- Celdas con `Classes.inactivo="{Binding Documento.Estado, Converter={x:Static conv:EstadoDocumentoConverter.Inactivo}}"`.
- `SelectorAnio` con `NumericUpDownValidacionBehavior.Que="año"` y `PermitirVacio="True"` (como hoy).
- Formulario: pie fijo (convención 27), dos encabezados, las transiciones en el slot de acciones del `EncabezadoDetalle`, los campos en un contenedor con `IsEnabled="{Binding PuedeEditarCampos}"` (tokens de deshabilitado), eventos con `FechaHoraLocalConverter`, el panel en un `Panel` envolvente con su `IsVisible` (gotcha AVLN2000 que ya documenta el XAML de hoy), Tareas vinculadas con `PuedeVerTareasVinculadas` y la insignia de `EstadoTareaConverter`. Code-behind: `LostFocus` de `CajaNumero` y `CajaDescripcion` → `MarcarTocado`.
- Panel: el de `AdjuntosPanelView` v2 (4a) con `PuedeAgregar` / `PuedeQuitar` en `IsVisible` (Q-7).

Build tras cada archivo; Steps 1 y 2 hasta verde.

- [ ] **Step 5: Guardianes, heredados y suites completas**

Como la Task 4 Step 5 (escenario "Documentos" al final de `Escenarios`).

- [ ] **Step 6: Mutaciones (QUITANDO), cada una con su rojo pegado**

- 5a: quitar el `IsVisible` de `FiltrosHistorial` (o de `CampoAnio`) → rojo `ElegirHistorial_MuestraSusFiltrosYSuGrilla_SinHueco`.
- 5b: quitar `NumericUpDownValidacionBehavior.Que` del `SelectorAnio` → rojo `Historial_AnioInvalido_ErrorDebajoYSinConsulta`.
- 5c: quitar el `IsVisible` de `BotonAnular` → rojo `SeleccionEnProceso_ComoOperadorMixto_SinAnular`.
- 5d: quitar el `IsVisible` de `GrillaHistorial` → rojo `PorDefecto_ActivosVisiblesHistorialOcultoYSinConsultarlo`.
- 5e: quitar `IsVisible="{Binding SinPermiso}"` de `EstadoSinPermiso` → rojo `Un403_…` y `RevocacionDentroDelShell_…`.
- 5f: quitar el `IsEnabled="{Binding PuedeEditarCampos}"` del contenedor de campos → rojo `DetalleCerrado_…` y `PuedeEditarCampos_NingunaRamaDocumentoCerrado_ControlesDeshabilitados` (heredado).
- 5g: quitar el `IsVisible` de `SeccionTareasVinculadas` → rojo `Detalle_OperadorSinTareasGestionar_TareasVinculadasOculta` y el heredado de `DocumentoFormViewGatesTests`.
- 5h: quitar el `IsVisible` de `BotonAgregar` del panel → rojo `Adjuntos_DocumentoCerrado_SinAgregarNiQuitar`.
- 5i: quitar `Converter={x:Static conv:TamanoLegibleConverter.Instance}` → rojo `Adjuntos_TamanoLegibleYFechaCorta`.
- 5j: quitar el `LostFocus` de `CajaNumero` → rojo `Alta_TocarNumeroYDescripcion_ErroresDebajo`.
- 5k: quitar `typeof(DocumentoListView)` de `VistasMigradasAV2` → rojo `Guardian_CubreTodasLasVistasDelEnsamblado`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Documentos/ src/StockApp.Presentation/ViewModels/Documentos/DocumentoListViewModel.cs tests/StockApp.Presentation.UiTests/NumericUpDownVistasRealesTests.cs tests/StockApp.Presentation.UiTests/V2/DocumentoListViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/DocumentoFormViewV2Tests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DocumentosComparacionTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Gestion/DocumentoFormComparacionTests.cs tests/StockApp.Presentation.Tests/ViewModels/Documentos/DocumentoListViewModelTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/DocumentoListViewGatesTests.cs <heredados adaptados, por ruta>
git commit -m "feat(ui-v2): documentos en v2 (listado activos/historial, formulario y adjuntos)"
```

---

## Bloque E — Guardianes, verificación orgánica, review e integración

### Task 6: Repaso de guardianes y mutaciones, suite y comparación completas, GATE 2 orgánico

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia del GATE 2.

- [ ] **Step 1: Guardianes de la fase** (con `grep` y la CLI `codegraph`; anotar en el ledger)

1. Las seis vistas en `VistasMigradasAV2` y en ninguna otra lista; `ReclasificarTareaDialog` en `DialogosV2`; `Guardian_CubreTodasLasVistasDelEnsamblado`, `VistaV2_NoUsaPiezasDelTemaViejo`, `VistaV2_NoTieneOpacidadesLiterales`, `DialogoV2_NoTieneOpacidadLiteralEnSuXaml`, `NingunaVista_TieneOverlaysDeModal` y `VistaV2_BotonEstiradoEstiraSuContenido` verdes.
2. `grep -n 'Opacity\|Classes="primary"\|Classes="secondary"\|Classes="danger"\|Classes="caption"\|Classes="seccion"\|Classes="card"\|<c:\|HeaderVista\|MargenVista\|DangerBrush\|SignoNegativoBrushConverter\|ActivoOpacidadConverter\|BadgeEstado\|TabControl\|TextDecorations' src/StockApp.Presentation/Views/Tareas/*.axaml src/StockApp.Presentation/Views/Documentos/*.axaml` vacío.
3. `git diff <BASE>..HEAD --stat -- src/StockApp.Presentation/Themes src/StockApp.Presentation/Controls src/StockApp.Presentation/App.axaml src/StockApp.Presentation/Behaviors docs/ui-v2/tokens.json docs/ui-v2/mockups/_base` VACÍO (tema compartido intacto).
4. `git diff <BASE>..HEAD --stat -- src/StockApp.Api src/StockApp.Domain src/StockApp.Application src/StockApp.ApiClient src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs src/StockApp.Presentation/ViewModels/Tareas/ClasificacionTareaPanelViewModel.cs src/StockApp.Presentation/ViewModels/Tareas/ReclasificarTareaDialogViewModel.cs` VACÍO; `TareaFila` sin cambios (`git diff <BASE>..HEAD -- src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs` no toca la clase `TareaFila`).
5. `git diff <BASE>..HEAD --stat -- tests/StockApp.Presentation.UiTests/TareaFakes.cs tests/StockApp.Presentation.UiTests/DocumentoFakes.cs docs/ui-v2/herramientas.md docs/superpowers/specs/` VACÍO (archivos compartidos con 6 y 7 sin tocar hasta la Task 8).
6. `grep -rn "BuscarActivosCommand\|BuscarHistorialCommand\|AbrirHistorialCommand\|_historialCargado\|OnSolapaSeleccionada" src/ tests/` vacío.
7. `CargaProtegidaSinPermisoGuardianTests` y `ComandosDeGuardadoGuardianTests` verdes.

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger los rojos PEGADOS de 1a-1g, 2a-2h, 4a-4m y 5a-5k. Una sin rojo pegado se corre ahora.

- [ ] **Step 3: Build, suite completa, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos (por nombre); total ≥ línea base + nuevos − reemplazados (anotar número y los renombres de la Task 5: ninguno se borra sin reemplazo); los dos `--verificar` exit 0; TODAS las filas de `index.html` aprobadas a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla). Commit solo si hubo ajustes: `git commit -m "test(ui-v2): guardianes de la Fase 5 al día"`.

- [ ] **Step 4: Verificación orgánica (spec §6.4) — la ejecuta un agente con la app real contra la base local, en WSLg a 1280×760 y 1366×768**

**Exclusión con las Fases 6 y 7:** la base `stockapp-pg`, `~/.config/GestionMunicipal/conexion.json`, el puerto de la API y la pantalla de WSLg son de la máquina, no del worktree. Antes de empezar, el agente confirma con el orquestador que NINGUNA otra fase está en su GATE 2 (`pgrep -x GestionMunicipa` vacío y `ss -ltnp | grep <puerto de conexion.json>` vacío); si hay otra, ESPERA. Nunca se mata un proceso de otra fase.

Preparación (en este orden):
1. `docker start stockapp-pg`. **Backup ANTES de crear datos:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-5-$(date +%Y%m%d-%H%M%S).sql`; `ls -l` (no vacío); ruta al ledger.
2. API del worktree de la fase: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (puerto contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`.
3. Operador `f5mixto` por Administración de usuarios con `tareas.gestionar` y `documentos.gestionar` (anotar id).
4. Datos con prefijo `PRUEBA-5` en títulos, números y descripciones: tres tareas (una con fecha límite vencida, una con clasificación completa y expediente `PRUEBA-5`), dos documentos (un Expediente y un Oficio) con un adjunto PDF. Anotar ids.
5. Toolkit `scripts/gui-verificacion/`; MIRAR cada captura y comparar `md5sum` con la anterior (dos iguales cuando la pantalla debía cambiar = WSLg congelado: PARAR y pedir el reinicio al usuario). Popups con `capturar-x11.py`.

Pasos (capturar cada pantalla; 1, 3, 5 y 7 se repiten a 1366×768):
1. **Tareas (Admin):** "Cargando tareas…" y la grilla "Abiertas" con insignias y "Vencida"; ordenar por VENCE y PRIORIDAD; tooltip de una clasificación cortada; cambiar el filtro a "Todas", "Canceladas" y de vuelta. Comparar a ojo con `tareas.html`.
2. **Acciones:** seleccionar → Tomar (pasa a En curso), Soltar, Terminar; "Cancelar tarea" con su confirmación; Review Focus 1: seleccionar una En curso y filtrar "Pendientes" → acciones ocultas.
3. **Alta de tarea:** tocar Título y salir → error debajo; completar con clasificación y expediente `PRUEBA-5` → Guardar → vuelve al listado con la fila nueva. Comparar con `tarea-nueva.html`.
4. **Detalle:** doble click → detalle con la insignia; agregar una nota a una hora anotada → la hora mostrada es la LOCAL (Q-10, Review Focus 5); "Actualizar prioridad"; "Reclasificar" → diálogo (captura), cambiar un combo y Escape → nada cambió (Review Focus 4); repetir con Aceptar → los valores nuevos.
5. **Documentos (Admin):** Activos con la grilla, Ver/Iniciar/Volver a pendiente/Finalizar según la fila; "Ver" → Historial (Año, Estado; filtros que recargan solos, una sola consulta en el log de la API por cambio); borrar el Año y tipear "20a5" → error debajo y SIN request en el log (Review Focus 2).
6. **Detalle de documento:** editar y "Guardar cambios"; nota en el historial del trámite (hora local); Agregar un PDF, Ver (se abre), Quitar con confirmación; Anular con motivo (`PedirTextoDialog`) → cerrado en solo lectura; Reabrir con motivo. Tareas vinculadas con la tarea `PRUEBA-5`.
7. **Operador mixto `f5mixto`:** sin "Cancelar tarea", sin Prioridad ni Reclasificar; sin "Anular…"/"Reabrir…" ni Quitar. Desde Admin quitarle `documentos.gestionar`; en su sesión, una acción en Documentos → un solo Aviso y "Sin permiso" en lugar de la card (Review Focus 3). Devolverle el permiso, quitarle `tareas.gestionar` con un documento abierto → la sección Tareas vinculadas desaparece; con Tareas abierto → "Sin permiso".
8. **Sin servidor:** `kill` SOLO del pid de la API de esta fase → en Tareas y en Documentos, "No se pudo conectar con el servidor" con Reintentar, sin modal global; levantarla y Reintentar → carga con los filtros de pantalla. "Nueva tarea" con la API caída → el Aviso de la clasificación (Q-9, comportamiento conservado).
9. **Popups:** calendario de Fecha límite y de Fecha de emisión, desplegable de Estado y de Zona, y la lista del buscador de expediente, con `capturar-x11.py`.

Si un paso no se puede hacer por el entorno, PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5), sin declarar la fase cerrada. **La limpieza de los datos `PRUEBA-5` y de `f5mixto` la decide el usuario** (ids en el ledger); nunca contra el VPS.

- [ ] **Step 5: GATE 2 — aprobación del usuario (PARADA)**

PARAR. Presentar: `index.html` (las 50 filas de la fase), las capturas junto a los mockups, los rojos de las mutaciones, los tests heredados adaptados y renombrados, los `UmbralAprobado` si los hubo, las respuestas a Q-1 a Q-10, los cambios respecto de los mockups aprobados y la pregunta de la limpieza. No seguir sin "aprobado" explícito. Registrar `Fase 5: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

### Task 7: Review final de la rama

**Files:**
- Modify: lo que pidan los hallazgos (cada arreglo en su commit, con rojo pegado)

**Interfaces:**
- Consumes: `<BASE>..HEAD` de `feat/ui-v2-fase5`.
- Produces: la rama lista para integrar.

- [ ] **Step 1: Review de todo el rango** (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus"; que las excepciones a "VMs intactos" no excedan E1 a E7; que `TareaFila`, `ClasificacionTareaPanelViewModel`, `ShellMainViewModel` y el tema compartido no cambiaron; que ninguna pieza del tema viejo quedó en las seis vistas; que las acciones solo-Admin siguen ocultas para un Operador en las dos pantallas y en el panel; que ningún test heredado se borró sin reemplazo (convención 13) y que `DocumentoListViewGatesTests` y `DocumentoFormViewGatesTests` conservan todos sus casos (§8.1).
- [ ] **Step 2:** los hallazgos se resuelven en commits propios (TDD) o se anotan como deuda con el usuario. Suite completa y `comparar.sh` otra vez si hubo cambios. Sin push ni merge.

### Task 8: Integración en `feat/ui-v2` y docs de cierre

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (C-A, C-B y C-C con los números que correspondan tras integrar)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (`**Estado:**`, fila "5 Gestión" de §7, "Estado de la Fase 5", §6.5 si hubo compensaciones, "Deuda abierta tras la Fase 5")
- Modify: los archivos con conflicto de la integración (ver "Riesgos de conflicto")

**Interfaces:**
- Consumes: `feat/ui-v2` (con lo que hayan integrado las Fases 6 y 7) y `feat/ui-v2-fase5` aprobada.
- Produces: la Fase 5 dentro de `feat/ui-v2`; sin push (el merge a `main` es por bloques).

- [ ] **Step 1: Traer `feat/ui-v2` y rebasear**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase5
git log --oneline -1 feat/ui-v2
git merge-base --is-ancestor feat/ui-v2 HEAD && echo "ff posible" || git rebase feat/ui-v2
```

Si hay conflictos: resolverlos como UNIÓN en las listas compartidas (`GuardianDePatronTests`: `VistasMigradasAV2`, `VistasDeLaTanda`, `VistasEmbebidas`, `InlineData`, `DialogosV2`; `GrillasSinRecorteBarridoTests.Escenarios`; `ComandosDeGuardadoGuardianTests`); en cualquier otro archivo, PARAR y consultar al orquestador (no elegir "la versión de la fase" a ciegas). Si `DatosShell.Crear` u otra API de VisualTests cambió por las otras fases, adaptar las comparaciones de esta fase en un commit propio. Nunca `git reflog expire`, `gc` ni `prune`.

- [ ] **Step 2: Build, suite completa y comparación sobre la base integrada**

Los comandos de la Task 6 Step 3. Expected: verde salvo los falsos conocidos; `index.html` con las filas de las fases integradas aprobadas.

- [ ] **Step 3: Docs de cierre** (sobre la base ya integrada, para no chocar con los de las Fases 6 y 7)

`herramientas.md`: "Convenciones que agrega la Fase 5 (Gestión: Tareas y Documentos)" con C-A, C-B y C-C numeradas a continuación de la última que haya. Spec:
1. `**Estado:**`: "5" en la lista de fases implementadas y "; Fase 5 (Gestión: Tareas y Documentos) APROBADA por el usuario el <fecha>"; la fila "5 Gestión" de §7.
2. §7, después del estado de la última fase: "**Estado de la Fase 5 (aprobada el <fecha>).** …" con: Tareas en grilla con filtro de estado y acciones sobre la selección, formulario con pie fijo, panel de clasificación en dos columnas y diálogo v2; Documentos con vista Activos/Historial y recarga automática, formulario con pie fijo y panel de adjuntos como el de Finanzas; E1 a E7; respuestas Q-1 a Q-10; 50 comparaciones (<máximo de teselas densas>); suite <total> en `<hash>`; verificación orgánica <resumen>; D1 a D20 aprobadas o cambiadas.
3. §6.5: cada compensación medida en las Tasks 4 y 5, con antes y después.
4. "**Deuda abierta tras la Fase 5:**" como mínimo: las cuatro colecciones y `MostrarCanceladas` que `TareaListViewModel` conserva sin uso en la vista (Fase 9); el estado calculado con el reloj real en `TareaFila` (D19); la selección que se suelta al recargar (D20); Q-9 (carga de la clasificación sin estado en línea); el detalle de documento sin estado propio de revocación (las acciones dan 403 y Aviso); lo que dejen las Q si alguna fue B; los `UmbralAprobado`; los minors diferidos del ledger; los datos de prueba según lo que decidió el usuario.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 5 (gestión: tareas y documentos)"
```

- [ ] **Step 4: Fast-forward en `feat/ui-v2`**

```bash
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 status --short   # sin cambios propios de otra fase en curso; si hay, PARAR
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 merge --ff-only feat/ui-v2-fase5
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2 log --oneline -3
```

Si `--ff-only` falla (otra fase integró entre el Step 1 y ahora), volver al Step 1. Sin push. Borrar el worktree y la rama de la fase SOLO con el OK del usuario (`git worktree remove`, `git branch -d`).

---

## Necesidades de tema compartido (para el integrador)

Con las opciones A de Q-1 a Q-10 la fase NO necesita ningún cambio del tema compartido. Estas entradas son condicionales; si alguna se activa, el cambio lo hace el integrador fuera de la fase, y la fase sigue con lo que hay hasta entonces.

1. **Estilo v2 de `TabControl` / `TabItem`** — solo si el usuario elige Q-3 = B. Cambio: en `Themes/V2/ControlesV2.axaml`, `Style Selector=".ui2 TabItem"` con texto 13/600 `V2TextoSecundarioBrush`, padding 0,8 y sin el subrayado de Fluent; `.ui2 TabItem:selected` con texto `V2AcentoOscuroBrush` y borde inferior de 2 px `V2AcentoBrush`; `.ui2 TabControl` con un borde inferior de 1 px `V2BordeBrush` bajo la tira; espejo `.v2-pestanas` en `docs/ui-v2/mockups/_base/base.css` y el tamaño en `tokens.json` si hace falta uno nuevo. Por qué: hoy no existe ningún estilo v2 de pestañas y el `TabControl` de Fluent no respeta tokens, tipografía ni alturas v2.
2. **`.ui2 TextBlock.inactivo` fuera de la grilla** — solo si el usuario elige Q-1 = B. Cambio: en `ControlesV2.axaml`, `Style Selector=".ui2 TextBlock.inactivo"` con `Foreground` `V2TextoTerciarioBrush`, y espejo `.v2-texto-inactivo` en `base.css`. Por qué: hoy la clase existe solo como `.ui2 DataGridCell TextBlock.inactivo` (`DataGridV2.axaml` L168); una lista de filas fuera de un `DataGrid` no tiene cómo pintar una tarea cancelada por token (convención 11 prohíbe `Opacity`).
3. **A confirmar en la medición de la Task 3: `AutoCompleteBox` v2.** Existe `.ui2 AutoCompleteBox` (`ControlesV2.axaml`), usado por el diálogo de Recalcular stock. Si la medición muestra que el buscador de expediente no queda en 36 de alto, que su placeholder no toma el estilo v2 o que la lista desplegada no tiene superficie y borde v2, el cambio exacto (selector, propiedad y valor medidos) se anota acá y en el ledger en ese momento; la fase usa el control como está.
4. **Candidato a `base.css` (no bloquea):** el CSS del hilo de notas y eventos (fila con separador superior de 1 px `--v2-separador`, texto cuerpo y fecha en texto secundario) vive en `docs/ui-v2/mockups/gestion/gestion.css`. Si el integrador lo quiere como patrón común (lo usaría otra pantalla con historial), lo mueve a `base.css` como `.v2-hilo` sin cambiar el render.

## Riesgos de conflicto con las Fases 6 y 7

| Archivo o recurso | Qué toca cada fase | Mitigación |
|---|---|---|
| `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` | Las tres quitan sus vistas de `InlineData` (L65-76), `VistasDeLaTanda` y `VistasEmbebidas`, y agregan a `VistasMigradasAV2` (y quizás a `DialogosV2`). | Cada fase toca solo sus líneas y agrega al FINAL; la Task 8 resuelve como unión y corre `Guardian_CubreTodasLasVistasDelEnsamblado`. |
| `tests/StockApp.Presentation.UiTests/GrillasSinRecorteBarridoTests.cs` (`Escenarios`) | La 6 cambia escenarios de Reportes (L69-81) y la 7 los de catálogos (L93-98). | Esta fase solo AGREGA "Tareas" y "Documentos" al final; unión en la Task 8. |
| `docs/ui-v2/herramientas.md` y la spec | Las tres agregan convenciones (numeración 37+), estado, §7 y deuda. | Esta fase los edita SOLO en la Task 8, sobre la base ya integrada, y numera sus convenciones ahí. |
| `TareaFakes.cs` / `DocumentoFakes.cs` | La 7 (catálogos de Tareas: Zonas, Dimensiones, Organismos, Orígenes) puede extender los fakes de catálogo de `TareaFakes.cs`. | Esta fase no los edita: fakes nuevos en `V2/GestionFakes.cs`. |
| `tests/StockApp.Presentation.Tests/ViewModels/ComandosDeGuardadoGuardianTests.cs` | Diccionario de comandos de guardado por VM; la 7 puede agregar los de Catálogos y Usuarios. | Esta fase no lo cambia salvo que un comando suyo cambie de tipo; si lo cambia, unión. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Shell/DatosShell.cs` y el sidebar de los mockups | La 6 (hub de Reportes) y la 7 (Catálogos) cambian ítems del sidebar y pueden cambiar `DatosShell`. | Esta fase solo consume `DatosShell.Crear`; las comparaciones ocultan el sidebar. Si la firma cambió, se adapta en la Task 8. El sidebar de los mockups de la fase puede quedar desactualizado respecto de la 6 y la 7: es visual de aprobación, no se compara. |
| `SignoNegativoBadgeTests.cs`, `EspaciadoBotonesEnFilaDeGridTests.cs`, `ButtonGhostContrasteTests.cs` | Tienen sitios de Tareas/Documentos y de Reportes o Catálogos. | Esta fase adapta solo sus sitios y solo si fallan; conflicto de línea improbable, se resuelve en la Task 8. |
| `ReporteTareasView` (Fase 6) | Reporte "Estadística de tareas", otro VM y otro servicio. | Sin archivos en común; la Fase 5 no toca `ReporteTareas*`. |
| Base `stockapp-pg`, `conexion.json`, puerto de la API y WSLg | Los GATE 2 de las tres fases usan la misma máquina. | Los GATE 2 se hacen de a UNO (Task 6 Step 4: el agente confirma que no hay otra app ni API corriendo); prefijos y usuarios propios (`PRUEBA-5`, `f5mixto`); backup antes; nunca matar procesos de otra fase. |
| `feat/ui-v2` | Las tres integran ahí. | `--ff-only` tras rebase (Task 8); si otra fase integró en el medio, se repite el rebase. Sin push. |

## Fuera de alcance

- Fase 8 (Importación) y cualquier pantalla de otra fase; `ReporteTareasView` (Fase 6); los catálogos de Tareas y `CatalogosTareaView` (Fase 7).
- Cambios de dominio, DTOs, servicios, endpoints, `ApiClient` y `ShellMainViewModel`.
- Editar título, descripción o fecha límite de una tarea ya creada (no lo permite ninguna acción hoy).
- Unificar `AdjuntosPanelView` y `AdjuntosDocumentoPanelView` (spec §9).
- Reloj inyectable en `TareaFila` (D19); re-seleccionar tras recargar (D20); búsqueda de texto en Tareas; navegar desde una tarea vinculada al detalle de la tarea.
- Estado "sin conexión" en línea para la carga de la clasificación (Q-9 A).
- Merge a `main` y push.
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| La grilla de Tareas cambia la forma de trabajar del usuario (secciones → filtro). | Q-1 al usuario antes de empezar; "Abiertas" por defecto reproduce lo que hoy se ve arriba; mockups en el GATE 1. |
| Una acción de fila actúa sobre una tarea que el filtro ocultó. | Review Focus 1: VM (1c) y View (Task 4). |
| El historial consulta sin año o con un año inválido. | Review Focus 2: guarda en el VM (2b, 2c) y error del campo en la View (5b). |
| Perder el permiso deja "Todavía no hay …" en pantalla. | Q-5 A y Review Focus 3, VM y View dentro del shell. |
| `DocumentoListViewGatesTests` se debilita al adaptarlo a las acciones del encabezado. | Solo cambian los helpers; Task 7 revisa que ningún caso ni aserción se perdió (§8.1). |
| El panel de clasificación no entra a dos columnas en el diálogo. | Medición en la Task 3 (D10) y `PanelEnElDialogo_EtiquetasYValoresSinRecorte`. |
| Hora de notas y eventos corrida. | Q-10 A, Review Focus 5 y paso 4 del GATE 2 con una hora conocida. |
| La fase toca sin querer el tema compartido o archivos de las otras fases. | Global Constraints, Task 6 Step 1 (diffs vacíos), fakes y comparaciones en archivos propios. |
| Conflictos al integrar con las Fases 6 y 7. | Tabla de arriba; docs solo en la Task 8; unión en listas; consulta al orquestador ante cualquier otro conflicto. |
| GATE 2 simultáneo con otra fase ensucia datos o mata su app. | Exclusión explícita en la Task 6 Step 4. |
| WSLg se congela o `capturar.sh` devuelve el escritorio de Windows con exit 0. | md5 de capturas, mirar cada una, `capturar-x11.py`; si se congela, PARAR. |
