# Fase 6: Reportes, el hub y los 6 reportes (UI v2). Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear el hub de Reportes (pantalla nueva, spec §4.2) que reemplaza los seis ítems de ANÁLISIS por uno solo, "Reportes", y migrar a la UI v2 los seis reportes (Valorización de inventario, Stock por categoría, Historial por producto, Productos más movidos, Log de auditoría y Estadística de tareas) sobre un marco común local de la fase, comparados contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** La fase corre EN PARALELO con las Fases 5 y 7, en su propio worktree (`~/workspace/stockapp-worktrees/ui-v2-fase6`, rama `feat/ui-v2-fase6`, que sale de `feat/ui-v2` después del merge de la 4e) y se integra al final en `feat/ui-v2` con rebase y `--ff-only`. Cuatro bloques. (A) ViewModels sin tocar vistas: el hub (`ReportesViewModel`, gates por tarjeta como getters sobre la sesión), el sidebar con un solo ítem "Reportes", y los seis VMs de reporte con los patrones v2 que ya existen (convenciones 18, 23 y 32), la vuelta al hub y el criterio de Tareas como opciones de combo. (B) Datos fijos, medición y los 15 mockups (30 PNG) de TODA la fase, con UN solo GATE 1. (C) Un componente local `MarcoReporte` (en `Views/Reportes/`, NO en el tema) con encabezado, breadcrumb, filtros, estados, grilla y pie; el hub y las seis vistas sobre él, con sus UiTests (View real, operador de permisos mixtos) y sus comparaciones. (D) Guardianes, mutaciones, suite completa, UN solo GATE 2 orgánico, integración en `feat/ui-v2`, documentación de cierre y review final.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, pypdf (verificación orgánica).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`: §4.1 (ANÁLISIS: Reportes), §4.2 (hub de Reportes), §4.4 (breadcrumb sin back-stack), §5.1 (listado), §6 (fidelidad, umbral §6.5), §7 (fila "6 Reportes"), §8.1 a §8.3 (tests que se mantienen, se reemplazan y nuevos: "Gates de las tarjetas del hub de Reportes"), §9 (riesgos) y la deuda abierta hasta la 4e. La spec no tiene un bloque de diseño de la Fase 6: lo reemplazan las preguntas Q-* (abajo) y las decisiones D-* del GATE 1. Convenciones 1 a 36 (y las que agregue la 4e): `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-15-ui-v2-fase-4d-gastos.md`.

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2-fase6` (rama `feat/ui-v2-fase6`, base: el `feat/ui-v2` con la 4e mergeada, anotado en la Task 0), como `cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 && <comando>`. Salvo la Task 8, que integra desde `/home/capua25/workspace/stockapp-worktrees/ui-v2`.
- **Otras dos fases corren a la vez** (5 Gestión y 7 Administración) en sus propios worktrees. Nada de esta fase toca sus worktrees, sus ramas ni `feat/ui-v2` hasta la Task 8. Nunca `git checkout` de otra rama dentro de un worktree ajeno (memoria "Agentes paralelos y worktrees").
- CodeGraph: este worktree tiene su PROPIO índice `.codegraph/` (lo crea la Task 0); nunca se copia ni se reusa el de otro checkout. Para C# usar primero la CLI `codegraph` dentro del worktree (`codegraph query|explore|node|callers|impact`); el MCP `codegraph_explore` puede responder con el índice de otro checkout, así que solo se usa si confirma rutas de `ui-v2-fase6`. `grep`/Read como fallback, y Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- **PROHIBIDO tocar el tema compartido:** `src/StockApp.Presentation/Themes/V2/` (`ControlesV2.axaml`, `DataGridV2.axaml`, `Tokens.axaml`), `docs/ui-v2/tokens.json`, `App.axaml`, los estilos globales, `src/StockApp.Presentation/Controls/V2/` (componentes y `ComponentesV2.axaml`), `src/StockApp.Presentation/Behaviors/` y `docs/ui-v2/mockups/_base/` (`base.css`, `tokens.css`). Si algo de ahí hace falta, NO se resuelve en la fase: se anota en el ledger y va a "Necesidades de tema compartido (para el integrador)". Un estilo que solo necesita un mockup va en un `<style>` dentro de ESE mockup.
- Tema claro fijo; tokens de §2.1 sin valores nuevos; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366); `DockPanel Margin="28,24"` como las vistas v2.
- Formato es-UY: importes con `MonedaConverter` ("$ 1.250.000,00"), cantidades con `CantidadConverter`, fechas "dd/MM/yyyy" (y "dd/MM/yyyy HH:mm" con `FechaUtcALocalConverter` en Historial y Auditoría); nunca `CultureInfo.GetCultureInfo("es-UY")`.
- Sin cambios de dominio, DTOs, API, servicios, `ViewLocator` ni `INavigationService`. VMs: cambian SOLO `ShellMainViewModel` (ANÁLISIS a un ítem, Task 1), el VM nuevo `ReportesViewModel` (Task 1) y los seis VMs de `ViewModels/Reportes/` por lo que lista la Task 2. `InicioViewModel` y sus accesos a Valorización y Auditoría NO cambian.
- Los PDF NO cambian de columnas, título, nombre de archivo, membrete ni aviso de volumen (`ColumnasPdfContraDtosRealesTests` y `ExportarPdfBotonesGuardianTests` siguen verdes); solo cambia de dónde sale la descripción de filtros (lo cargado, convención 32) y cuándo se habilita el botón.
- `x:DataType` / compiled bindings en las vistas, en `MarcoReporte` y en cada `DataTemplate`; constantes con `{x:Static}`.
- Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001, cascadea a MSB4025).
- Pantallas de consulta: SIN botón primario (precedentes Libro caja y Control POA, 4a y 4c). Todo botón `secundario`.
- Prohibidos en las vistas v2: `Opacity`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:` del tema viejo, `Classes="primary|secondary|caption|seccion|card|body"`, `MargenVista`, `DangerBrush`, `WarningBrush`, `RadioButton` (no tiene estilo v2).
- Gates de UI con la View real en `Presentation.UiTests` (un test de VM no custodia un `IsVisible` del XAML); gates de permisos con un Operador de permisos MIXTOS (`new SesionFake(RolUsuario.Operador, Permisos.VerReportes, …)`), NUNCA con Admin (cortocircuita). Revocación en caliente por el camino real (`ManejadorAccesoRevocado.ManejarAsync` → `ShellMainViewModel.NotificarPermisosActualizados`) con la View real dentro de `ShellMainView`.
- `ArbolVisual.EsVisibleEnArbol` falla fuerte con null: nada de `x is not null && EsVisibleEnArbol(x)`; buscar por `x:Name` y dejar que un nombre que falta dé rojo.
- TDD en cada task: test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde. Un test que nace verde (caracterización) se declara así y su custodia es una mutación con rojo pegado.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-6 && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (líneas o fragmentos); `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; se corre el test y se PEGA el rojo; se restaura con `cp "$SCRATCH/<nombre>.bak" <archivo>`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato real: `IReporteStockService.ObtenerValorizacionAsync()` → `Task<ValorizacionReporteDto>` (`Items` + `Totales`), `ObtenerStockPorCategoriaAsync()` → `Task<IReadOnlyList<StockCategoriaDto>>`, `ObtenerMasMovidosAsync(DateTime?, DateTime?, int)` y `ObtenerHistorialPorProductoAsync(int, DateTime?, DateTime?)` con fechas en UTC; `IAuditoriaQueryService.ObtenerLogAsync(int?, DateTime?, DateTime?)`; `IReporteTareasService.ObtenerAsync(FiltroReporteTareas)` → `ReporteTareasDto(Filas, TotalGeneral)`; `IProductoService.BuscarPorTextoAsync(string?)`; `IUsuarioService.ListarAsync()`. Los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException` (sin servidor).
- Mockups (§6.1): `docs/ui-v2/mockups/reportes/*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosReportes`, celdas numéricas con `v2-num` y el valor en un `<span>`; se aprueban ANTES del XAML (nada de XAML de las siete vistas antes del GATE 1).
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, "Martin", …)` tras revisar cada tesela densa con el usuario (16 o más exige además `MutacionPalidaEnRojo`); nunca para tapar un defecto.
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, "Reportes", vm)`), sidebar oculto en los dos renders (`ProductosComparacionTests.OcultarSidebar`, `ocultarEnMockup: [".v2-sidebar"]`).
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln` (la `.sln` entera). Tests de a UN `.csproj` por comando y siempre con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez. Toda task que toque UI o VM corre las suites COMPLETAS de `Presentation.Tests` y `Presentation.UiTests` (Ruling R7 de la 4d: un filtro dejó pasar un rojo de guardián).
- En el worktree, los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido; `DataGridColumnasAutoAjusteTests.F_GrillaGrande_ElBehaviorNoAgregaCostoPerceptible` es flaky de tiempo conocido. Cualquier otro rojo es real.
- Línea base: se mide en la Task 0 sobre la base de la rama (la 4d cerró con 6103 en `7fd41cf`; la 4e la cambia). Cada task anota el total tras su commit; solo baja por tests heredados REEMPLAZADOS (convención 13), listados con su reemplazo.
- Sin referencias al plan en el código (nada de "Task 4", "Q-MARCO", "D7" en `.cs`/`.axaml`/`.css`/`.html`).
- Verificación GUI: ventana "Gestión Municipal" (`pgrep -x GestionMunicipa`), toolkit `scripts/gui-verificacion/` (leer su `README.md` antes: ventanas fantasma, selector GTK, `pkill -f` prohibido); `capturar.sh` NO es fail-closed: MIRAR cada captura y comparar `md5sum` con la anterior; popups con `capturar-x11.py`; matar procesos por PID.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA (instrucción del usuario, prevalece sobre cualquier recordatorio del harness); `git add` por ruta, nunca `-f` ni `-A` (`.codegraph/` y `.superpowers/` quedan afuera); sin push, amend, `git reflog expire`, `git gc` ni `git prune`. El rebase y el merge `--ff-only` solo en la Task 8.
- GATE 1 (Task 3) y GATE 2 (Task 7) son las DOS únicas paradas para el usuario: no se sigue sin un "aprobado" explícito. Las Q-* se le preguntan al usuario ANTES de la Task 1 (todas juntas, con su recomendación).

## Review Focus

1. **El PDF rotula un período que no es el de las filas.** Caso: en Productos más movidos el usuario busca setiembre, cambia "Desde" a agosto sin tocar Buscar y exporta; el PDF dice "Período: 01/08/2026 …" con las filas de setiembre. Esperado: la descripción sale de la consulta CARGADA (convención 32). Test: `ExportarPdf_ConFechasCambiadasSinBuscar_RotulaElPeriodoCargado` en los cuatro VMs con filtros (Task 2).
2. **Una búsqueda vieja pisa la nueva.** Caso: Log de auditoría, el usuario busca "Todos" (lenta), cambia a "mlopez" y busca de nuevo; la primera termina última y la grilla muestra todo con "mlopez" en el combo. Esperado: solo la última consulta aplica (convención 23). Test: `DosBusquedas_LaPrimeraTerminaTarde_NoPisaLaSegunda` en los cuatro VMs con filtros (Task 2).
3. **Historial por producto con un operador sin `catalogo.productos`.** Caso: la tarjeta se ve (VerReportes + RegistrarMovimientos), pero el buscador de producto pega a `GET /productos` (exige `GestionarProductos`, `ProductosEndpoints.cs:34`) y salta el Aviso global en cada tecla. Esperado (Q-PERMISO-HISTORIAL = A): la tarjeta exige también `GestionarProductos`. Test: `OperadorConVerReportesYMovimientosSinCatalogo_NoVeHistorialPorProducto` (Task 4, View real).
4. **Perder `reportes.ver` con un reporte abierto.** Caso: Admin le quita el permiso a un operador que tiene Valorización abierta; el operador toca Actualizar. Esperado: un solo Aviso, "Sin permiso" EN LUGAR de la card, Exportar PDF deshabilitado, el ítem "Reportes" desaparece del sidebar. Test: `Un403AlActualizar_DentroDelShell_SinPermisoPdfDeshabilitadoYSinItemReportes` (Task 5).
5. **Abrir un reporte sin servidor.** Caso: la API está caída y el usuario abre Log de auditoría; hoy la carga de usuarios y la búsqueda escapan al modal global. Esperado: "No se pudo conectar con el servidor" con Reintentar DENTRO de la card y los filtros a mano; Reintentar repite con los filtros de pantalla y carga el combo de usuarios si faltaba (convención 18). Tests: `SinConexionAlCargarUsuarios_Reintentar_CargaUsuariosYLog` (Task 2) y `SinConexion_EstadoEnLineaConReintentar_SinModalGlobal` (Task 6).

---

## Preguntas previas a la ejecución (Q-*, a confirmar por el usuario ANTES de la Task 1)

La spec no tiene bloque de diseño de la Fase 6. El plan está escrito con la opción A de cada Q (la recomendada). El orquestador las presenta TODAS JUNTAS antes de la Task 1; si el usuario elige B en alguna, se aplica el cambio indicado (con TDD) y se anota en el ledger.

| # | Pregunta (evidencia) | A (recomendada, la que implementa el plan) | B (alternativa) y qué cambia |
|---|---|---|---|
| Q-SIDEBAR | El sidebar tiene hoy 6 ítems en ANÁLISIS ("P1, sidebar transitorio hasta el hub", `ShellMainViewModel.cs` L249, L276-283). §4.1 pide "ANÁLISIS: Reportes". Los 6 `Nav*Command` del shell los custodian `ShellMainViewGatesTests` (§8.1 "se mantiene tal cual"), `ShellMainViewModelTests`, `ShellMainViewModelReportesTests`, `ShellMainViewModelGruposTests` y `ShellMainSeccionActivaTests`. | Un solo ítem "Reportes" (`NavReportesCommand`, gate `PuedeVerReportes`: toda tarjeta exige `reportes.ver`, así que "visible si hay al menos una tarjeta" equivale a ese gate). Se BORRAN los 6 `Nav*Command` y `PuedeVerHistorialPorProducto` del shell; sus tests se REEMPLAZAN (convención 13) por los del hub (`ReportesViewModelTests`, `ReportesViewV2Tests`): desvío declarado de §8.1, que la propia spec anticipa en §8.3. | Se conservan los 6 `Nav*Command` (sin ítem en el sidebar, solo para los tests): código muerto en el shell; los tests heredados no se tocan. |
| Q-SECCION | ¿Qué ítem del sidebar se marca con un reporte abierto? Hoy cada reporte marca su propia sección (`SeccionPorPantalla`, L369-374). | El hub y los 6 reportes marcan "Reportes" (también al llegar desde los accesos de Inicio). | Solo el hub marca "Reportes"; con un reporte abierto no se marca ninguno. |
| Q-VOLVER | §4.4: breadcrumb en altas y ediciones, con el comando de vuelta que el VM ya expone; sin back-stack. Los reportes cuelgan del hub y no exponen ningún comando de vuelta. | Breadcrumb "Reportes / <título>" en los 6 reportes; el primer tramo ejecuta `VolverCommand` (nuevo en cada VM: `Navegar<ReportesViewModel>()`). `INavigationService` entra como ÚLTIMO parámetro OPCIONAL del constructor (`INavigationService? navegacion = null`), así los tests existentes compilan sin cambios y la DI lo inyecta; `VolverCommand` deshabilitado sin navegación. | Sin breadcrumb: se vuelve al hub solo por el sidebar. Los VMs no cambian por esto. |
| Q-ACCIONES | Hoy cada reporte tiene "Buscar" `primary` y "Exportar PDF" en el header. Con filtros, Historial de movimientos v2 (3c-2) puso "Buscar" `secundario` al final de la barra de filtros. Sin filtros, Libro caja (4c) usa "Actualizar" `secundario`. | Sin primario. Header: "Exportar PDF" `secundario` (y "Actualizar" `secundario` antes, en Valorización y Stock por categoría, mismo `BuscarCommand`). Con filtros: "Buscar" `secundario` al final de la `BarraFiltros`, alineado abajo. | "Buscar" como único primario en los reportes con filtros (desvío del precedente de consulta). |
| Q-RECARGA | ¿Los filtros recargan solos (convención 34, como Gastos U3) o con Buscar? | Con "Buscar" explícito (VMs casi intactos; alcance de un día). El PDF rotula y exporta lo CARGADO (convención 32), no lo que está en los filtros. | Recarga sola con `RecargaConDebounce` y sin Buscar en los 4 con filtros (más cambios de VM, convenciones 34 y 35, más tests). |
| Q-ESTADOS | Hoy: Valorización, Stock, Más movidos y Tareas cargan por `EjecutarCargaProtegidaAsync`; Auditoría solo en `InicializarAsync` (su Buscar deja escapar el 403); Historial traga el 403 en silencio. Ninguno opta a `SinConexion` ni usa `ConsultaVigente`. | Los 6 adoptan la convención 18 completa (Cargando, `SinPermiso`, `AvisaSinConexionEnLinea => true` con `AlPerderConexion` que vacía) y la 23 (`ConsultaVigente`) en toda carga que reemplaza `Items`. | Solo lo que ya tienen; un 403 en Buscar de Auditoría sigue escapando al modal y el servidor caído al modal global (deuda). |
| Q-HISTORIAL-INICIAL | `HistorialPorProductoView` llama `CargarAsync` al abrir y consulta con `ProductoId = 0` (`HistorialPorProductoViewModel.cs` `CargarAsync`): una consulta inútil. | Sin producto elegido no consulta: el vacío dice "Elegí un producto" / "Buscá un producto por código o nombre para ver sus movimientos." y Exportar PDF queda deshabilitado. | Se conserva la consulta con id 0 (resultado vacío) y un único vacío "Sin movimientos". |
| Q-PERMISO-HISTORIAL | La tarjeta Historial por producto exige `reportes.ver` + `movimientos.registrar` (§4.2), pero su buscador pega a `GET /productos`, que exige `catalogo.productos` (`ProductosEndpoints.cs:34`): un operador sin ese permiso ve la tarjeta y cada búsqueda da 403 (Review Focus 3). Mismo criterio que `PuedeRegistrarEntradaSalida` ("el gate exige el MÁXIMO de los permisos de abajo"). | La tarjeta exige además `catalogo.productos` (desvío de §4.2, a anotar en la spec al cierre). | Gate de §4.2 tal cual; el campo Producto se oculta sin `catalogo.productos` (como `PuedeFiltrarPorProducto` del Historial de movimientos) y la pantalla queda sin forma de elegir producto: inútil. |
| Q-CRITERIO | Estadística de tareas elige el criterio con dos `RadioButton`; el tema v2 no tiene estilo de `RadioButton` y la fase no puede tocar el tema. | Un `ComboBox` "Contar por" (Fecha de creación / Fecha de cierre), como el "Período" de Libro caja (`OpcionesModo`): `CriteriosDisponibles` y `CriterioOpcionSeleccionada` nuevos en el VM, sincronizados con `CriterioSeleccionado` (que no cambia, ni `EsCriterioCreacion`/`EsCriterioCierre`). | `RadioButton` v2: necesidad de tema para el integrador (estilo `.ui2 RadioButton` en `ControlesV2.axaml` + espejo en `base.css`); la vista de Tareas queda con el radio v1 hasta que el integrador lo agregue. |
| Q-USUARIO-AUDITORIA | Log de auditoría filtra por usuario con un `AutoCompleteBox` sobre una lista PRECARGADA y chica (~14 usuarios) con un ítem "Todos". | `ComboBox` "Usuario" (200) con la misma lista (`Usuarios`, `UsuarioFiltroSeleccionado`; VM sin cambios por esto). | Se conserva el `AutoCompleteBox` (como el Producto de Historial). |
| Q-MARCO | Los 6 reportes comparten encabezado con breadcrumb y acciones, barra de filtros opcional, alerta de error, cuatro estados (cargando, sin conexión, sin permiso, vacío), grilla y pie. | Un componente LOCAL de la fase, `Views/Reportes/MarcoReporte` (`UserControl` con ranuras), que las 6 vistas usan. No entra al tema ni a `Controls/V2`. | Seis XAML independientes que copian el patrón (más líneas, más tests por vista, mismo resultado visual). |
| Q-MOCKUPS | Los estados comunes salen del mismo marco: dibujarlos en las seis pantallas no prueba nada nuevo. | 15 mockups (30 PNG): hub Admin y operador; Valorización con datos, cargando, sin conexión, sin permiso y vacío; los otros cinco con datos; Historial sin producto; Más movidos con el error de fechas; Tareas con el criterio de cierre. Los estados de los otros cinco los custodian UiTests sobre el marco. | Los cuatro estados en las seis pantallas: 35 mockups (70 PNG), sin valor de detección extra. |
| Q-SIDEBAR-MOCKUPS | Hay 69 mockups que dibujan el sidebar viejo; en las comparaciones de contenido el sidebar está oculto. | Se actualiza SOLO `shell/sidebar-admin.html` (el único que compara el sidebar y tiene reportes; `sidebar-operador.html` no tiene `reportes.ver`) y los mockups nuevos nacen con el sidebar nuevo. Los demás quedan con el sidebar viejo (oculto en su comparación): deuda para la Fase 9. | Se actualizan los 69 (choque seguro con las Fases 5 y 7, que también los tocan). |

## Decisiones que se confirman al aprobar los mockups (Task 3, GATE 1)

El plan está escrito con estos valores; si el usuario cambia uno, se aplica en los mockups, en los tests de VM que lo fijan (con su rojo y su verde) y en las Tasks 4 a 6.

- **D1. Ítem del sidebar:** "Reportes", ícono `mdi-chart-box-outline` (se verifica con `icono-svg`; si no existe, `mdi-file-chart` y se anota), en ANÁLISIS.
- **D2. Hub:** `EncabezadoPagina` sin breadcrumb, `Titulo="Reportes"`, `Subtitulo="Consultas e informes del inventario, la auditoría y las tareas."`, sin acciones. Debajo, sobre el fondo de la app (sin card), `v2:GrillaUniformeV2 Columnas="3" Espaciado="12"` con seis `v2:TarjetaAcceso` en dos filas, en el orden de §4.2:

  | x:Name | Ícono | Título | Descripción |
  |---|---|---|---|
  | `TarjetaValorizacion` | `mdi-currency-usd` | Valorización de inventario | Valor del stock a precio de costo, producto por producto. |
  | `TarjetaStockCategoria` | `mdi-chart-pie` | Stock por categoría | Productos, stock y valor agrupados por categoría. |
  | `TarjetaHistorialPorProducto` | `mdi-file-document` | Historial por producto | Los movimientos de un producto en un período. |
  | `TarjetaMasMovidos` | `mdi-trending-up` | Productos más movidos | Los productos con más movimientos en un período. |
  | `TarjetaAuditoria` | `mdi-shield-search` | Log de auditoría | Quién hizo qué y cuándo en el sistema. |
  | `TarjetaEstadisticaTareas` | `mdi-chart-bar` | Estadística de tareas | Tareas por estado, agrupadas por zona, tema u organismo. |

  Una tarjeta invisible no deja hueco (`GrillaUniformeV2` salta los invisibles): el operador sin Historial ve 5 tarjetas (3 + 2).
- **D3. Encabezado de un reporte:** breadcrumb "Reportes / <Título>" (separador " / "), título del reporte, SIN subtítulo; acciones a la derecha (Q-ACCIONES).
- **D4. Filtros** (`BarraFiltros MostrarBusqueda="False"`, `CampoFormulario` con etiqueta arriba, `Spacing="8"`, "Buscar" alineado abajo): Más movidos: Desde (`CalendarDatePicker` 140) · Hasta (140) · Top (`NumericUpDown` 100, mínimo 1) · Buscar. Historial por producto: Producto (`AutoCompleteBox` 280, marca de agua "Buscar producto…") · Desde · Hasta · Buscar. Log de auditoría: Usuario (`ComboBox` 200) · Desde · Hasta · Buscar. Estadística de tareas: Agrupar por (`ComboBox` 220) · Contar por (`ComboBox` 180) · Desde · Hasta · Buscar. Anchos a confirmar con la medición de la Task 3 (los nombres más largos sin cortar).
- **D5. Grillas** (headers en MAYÚSCULAS, `ColumnaAuto` de la convención 19 en las de contenido libre, numéricas con `num`):
  - Valorización: CÓDIGO · NOMBRE (`*`, elipsis + tooltip si se corta) · CATEGORÍA · STOCK (ancho fijo medido: `Insignia` "Stock negativo" peligro a la izquierda + número a la derecha con `Classes.negativo`, como Productos, convención 11) · P. COSTO · VALOR COSTO.
  - Stock por categoría: CATEGORÍA (`*`) · PRODUCTOS · STOCK TOTAL (fijo, insignia + número) · VALOR COSTO.
  - Historial por producto: FECHA ("dd/MM/yyyy HH:mm", `NoCede`) · TIPO · MOTIVO · CANTIDAD · P. UNITARIO · STOCK ANT. (número con `Classes.negativo`) · STOCK NUEVO (fijo, insignia + número) · COMENTARIO (`*`).
  - Productos más movidos: CÓDIGO · NOMBRE (`*`) · MOVIMIENTOS · VOLUMEN TOTAL.
  - Log de auditoría: FECHA (`NoCede`) · USUARIO · ACCIÓN · ENTIDAD · ENTIDAD ID (`num`) · DETALLE (`*`).
  - Estadística de tareas: CLASIFICADOR (`*`) · PENDIENTES · EN CURSO · TERMINADAS · CANCELADAS · TOTAL.
  Orden por click en todas las columnas.
- **D6. Pie de la card** (convención 10, `ConteoConverter` sobre `Items.Count`, oculto sin filas o sin conexión): Valorización "N productos" y, a la derecha, "Total valor costo" + importe (`titulo-seccion`, `tnum`); Stock por categoría "N categorías"; Historial "N movimientos"; Más movidos "N productos"; Auditoría "N registros"; Tareas "N filas" y, a la derecha, "Total general" + número.
- **D7. Estados** (dentro de la card con los filtros a mano, salvo Sin permiso): cargando "Cargando reporte…" (`EstadoCargando`, solo sin filas); sin servidor, los textos de la convención 18 con "Reintentar"; tras un 403, `EstadoVacio` "Sin permiso" con el `MensajeSinPermiso` que ya fija cada VM, EN LUGAR de la card; vacío (solo con la carga terminada y sin error):

  | Reporte | Título | Mensaje |
  |---|---|---|
  | Valorización | No hay productos para valorizar | Cuando haya productos activos, su valor va a aparecer acá. |
  | Stock por categoría | No hay categorías con productos | Cuando haya productos en una categoría, van a aparecer acá. |
  | Historial (sin producto) | Elegí un producto | Buscá un producto por código o nombre para ver sus movimientos. |
  | Historial (con producto) | Sin movimientos en el período | Probá con otro período. |
  | Más movidos | No hubo movimientos en el período | Probá con otro período. |
  | Auditoría | No hay registros para estos filtros | Probá con otro usuario o período. |
  | Tareas | No hay tareas en el período | Probá con otro período o criterio. |
- **D8. Error de filtros** (`MensajeError` de hoy, mismos textos): `v2:Alerta` Error debajo de la barra de filtros, sin tocar la grilla anterior.
- **D9. Aviso del criterio de cierre** (Tareas): `v2:Alerta` Advertencia entre los filtros y la grilla, con el texto de hoy ("Criterio 'Fecha de cierre': solo se cuentan tareas Terminadas o Canceladas. Las columnas 'Pendientes' y 'En curso' muestran 0 a propósito, no faltan datos."), visible con `EsCriterioCierre`.
- **D10. Exportar PDF** habilitado solo con filas cargadas, sin `SinPermiso`, sin `SinConexion` y sin carga en curso (hoy el botón está siempre habilitado y no hace nada sin filas).
- **D11. Datos fijos** (Task 3): `DatosReportes` con 8 productos valorizados (uno con stock negativo), 5 categorías (una con stock total negativo), 9 movimientos de un producto (uno que deja el stock negativo), top 10 más movidos, 12 registros de auditoría y 5 filas de tareas agrupadas por zona; fechas de setiembre y octubre de 2026 (nada relativo a "hoy": `ReporteTareasViewModel` pone el año en curso en el constructor, los datos fijos lo pisan antes de la carga).

## Necesidades de tema compartido (para el integrador)

Con las recomendaciones A, la fase NO necesita ningún cambio en el tema compartido: el hub usa `EncabezadoPagina`, `GrillaUniformeV2` y `TarjetaAcceso` como Inicio; los reportes, `EncabezadoPagina`, `TarjetaContenido`, `BarraFiltros`, `CampoFormulario`, `Alerta`, `EstadoVacio`, `EstadoCargando`, `Insignia`, el tema de `DataGrid`, `ColumnaAuto` y `TooltipSiSeCorta` como Gastos e Historial de movimientos; el criterio de Tareas pasa a `ComboBox` (Q-CRITERIO = A).

Condicionales (se agregan a esta sección solo si ocurren, con el cambio exacto y por qué; la fase NO los implementa):

1. **Q-CRITERIO = B:** estilo `.ui2 RadioButton` en `Themes/V2/ControlesV2.axaml` (círculo de 16, borde `V2BordeInputBrush`, marcado con `V2AcentoBrush`, texto `V2FuenteControlTamano`) y su espejo `.v2-radio` en `docs/ui-v2/mockups/_base/base.css`. Por qué: el tema no tiene `RadioButton` y Estadística de tareas lo necesita.
2. **Si la medición de la Task 3 muestra que el `AutoCompleteBox` de Historial no mide 36 o no toma el borde v2 dentro de `BarraFiltros`** (hoy `.ui2 AutoCompleteBox` solo fija fuente y color, `ControlesV2.axaml` L419-422): el estilo de la caja interna. Por qué: un filtro desalineado con los demás. (Historial de movimientos ya lo usa a 36 en la 3c-2, así que se espera que no haga falta.)
3. **Cualquier clase de `base.css` que un mockup necesite y no exista:** se resuelve con un `<style>` local en ese mockup; si el integrador prefiere llevarla a `base.css`, se lista acá con su CSS.

## Riesgos de conflicto con las Fases 5 y 7 (para la Task 8)

| Archivo | Quién más lo toca | Qué cambia esta fase | Cómo se resuelve |
|---|---|---|---|
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | Fase 7 (ADMINISTRACIÓN a "Catálogos", sus `Nav*Command`, `SeccionPorPantalla`) | Grupo ANÁLISIS (L276-283), `SeccionPorPantalla` (L369-374), `NavReportes`, borra 6 `Nav*` (L597-639) y `PuedeVerHistorialPorProducto` (L150-153, L418) | Hunks vecinos: conflicto textual probable en la lista `Grupos` y en el diccionario. Se conservan los dos lados (ANÁLISIS de la 6, ADMINISTRACIÓN de la 7). |
| `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs`, `ShellMainViewModelGruposTests.cs` (L60-61), `ShellMainSeccionActivaTests.cs`, `ShellMainViewModelReportesTests.cs`; `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs` | Fase 7 | Reemplazo de los tests de los 6 ítems | Ambos lados; correr las cinco clases tras resolver. |
| `docs/ui-v2/mockups/shell/sidebar-admin.html` | Fase 7 | Sección ANÁLISIS | Ambos lados; regenerar los 2 PNG y correr `ShellComparacionTests`. |
| `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`InlineData` del `HeaderVista` L64-74, `VistasDeLaTanda`, `VistasMigradasAV2`) | Fases 5 y 7 | Saca las 6 vistas de las listas viejas y agrega 7 a `VistasMigradasAV2` | Unión de las tres; `Guardian_CubreTodasLasVistasDelEnsamblado` verde. |
| `src/StockApp.Presentation/App.axaml.cs` (registro DI, L336-341) | Fase 7 (VM de Catálogos) | `services.AddTransient<ReportesViewModel>();` | Ambas líneas; `ComposicionDIApiTests` verde. |
| `tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs`, `CargaProtegidaEstadoVacioUiTests.cs`, `ExportarPdfBotonesGuardianTests.cs`, `GrillasSinRecorteBarridoTests.cs`, `DataGridColumnasAutoAjusteTests.cs`, `DataGridCeldaNumericaOrdenTests.cs` | Fase 5 (Tareas y Documentos en los mismos archivos) | Adaptación de los casos de reportes (convención 13) | Métodos distintos del mismo archivo: unión. |
| `docs/ui-v2/herramientas.md` (numeración de convenciones) y la spec (Estado, §7, §6.5, deuda) | Fases 5 y 7 | Convenciones nuevas y cierre | Se escriben DESPUÉS del rebase (Task 8), con el siguiente número libre. |
| `artifacts/ui-v2/` | Todas | PNG regenerados | No se versiona; se regenera tras el rebase. |

---

## Convenciones heredadas que aplican (de `herramientas.md`)

6 (comparación dentro del shell), 10 (`EncabezadoPagina` + card + pie de conteo con `ConteoConverter` sobre `Count`), 11 (número negativo con `Classes.negativo` e `Insignia` peligro, nunca `Opacity`), 13 (tests heredados se adaptan sin borrarse; si se reemplazan, se nombra el reemplazo), 14 y 19 (columnas medidas en headless antes del mockup; `ColumnaAuto` con el conversor de la celda; `NoCede` en FECHA; STOCK de ancho fijo por ser conjunto cerrado), 18 (`Cargando`, `SinPermiso`, `SinConexion` con Reintentar), 20 (tooltip solo si se corta), 21 (permisos con operador mixto dentro de `ShellMainView`), 23 (`ConsultaVigente`), 32 (lo que se exporta y cómo se rotula sale de lo cargado), y la convención de `GrillaUniformeV2` (nunca `UniformGrid`). NO aplican: 12, 15, 16, 22, 24 a 31, 33 a 36.

## Convenciones que agrega esta fase (para `herramientas.md`, Task 8, numeradas tras el rebase)

- **Marco de reporte (`Views/Reportes/MarcoReporte`):** un reporte v2 es su vista con `Classes="ui2"` + `MarcoReporte` (breadcrumb "Reportes / título" al `VolverCommand` del VM, acciones, filtros opcionales, `MensajeError` en `Alerta`, los estados de la convención 18 y el vacío propio por `TituloVacio`/`MensajeVacio`, grilla en `Contenido`, pie). El marco toma los estados de `ViewModelBase` por su `DataContext`; la vista solo le da `HayDatos`. Pantalla de consulta: sin primario.
- **Hub de tarjetas con gates:** `GrillaUniformeV2` + `TarjetaAcceso` con `IsVisible` sobre getters `Puede*` del VM del hub (getters sobre la sesión: la revocación en caliente los re-evalúa sin override); el ítem del sidebar que lleva al hub es visible si alguna tarjeta lo es.

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (en `feat/ui-v2` al escribir el plan) |
|---|---|---|
| Hub nuevo y `ViewLocator` | `ViewModels/Reportes/ReportesViewModel.cs` ↔ `Views/Reportes/ReportesView.axaml`: el `ViewLocator` reemplaza "ViewModel" por "View" en el nombre completo. | `ViewLocator.cs` L22; spec §9 ("las nuevas siguen la convención"). |
| Gates del hub | Getters sobre `ICurrentSession` (como `InicioViewModel.PuedeVerReportes`, L58-59), no campos: `NotificarPermisosActualizados` de la base (`OnPropertyChanged(string.Empty)`) los re-evalúa en una revocación en caliente sin override. | `ViewModelBase.cs` L208; convención 21. |
| `PuedeVerHistorialPorProducto` | Se muda del shell al hub con `GestionarProductos` agregado (Q-PERMISO-HISTORIAL). | `ShellMainViewModel.cs` L139-153; `ProductosEndpoints.cs` L34. |
| Carga de los reportes | `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` con un `ConsultaVigente _consultas` por VM; el resultado se aplica solo si `turno.EsLaUltima`. `AvisaSinConexionEnLinea => true`; `AlPerderConexion` vacía `Items` (y `Totales`/`TotalGeneral`). | `ViewModelBase.cs` L113-147, L254-305; `ConsultaVigente.cs`; convenciones 18 y 23. |
| Auditoría | `InicializarAsync` (usuarios + log) y `CargarAsync` (solo log) van por el helper; `ReintentarAsync` vuelve a `InicializarAsync` si `Usuarios` está vacío (si no, `CargarAsync`). | `AuditoriaLogViewModel.cs` `InicializarAsync` y `CargarAsync`. |
| Historial | Se borra el `catch (UnauthorizedAccessException)` silencioso: el 403 marca `SinPermiso` por el helper. Sin producto: `Items = []`, sin consultar. | `HistorialPorProductoViewModel.cs` `CargarAsync`. |
| PDF de lo cargado | Cada VM con filtros calcula `var descripcion = ConstruirDescripcionFiltros();` al EMPEZAR la consulta y, al aplicar con `turno.EsLaUltima`, la fija en `_descripcionCargada`; el PDF usa `_descripcionCargada ?? ConstruirDescripcionFiltros()` (el respaldo solo se ve sin nada cargado, cuando Exportar está deshabilitado). Valorización y Stock siguen con "Sin filtros aplicados.". | Convención 32; `MasMovidosViewModel.ConstruirDescripcionFiltros`. |
| Exportar habilitado | `[RelayCommand(CanExecute = nameof(PuedeExportar))]`, `PuedeExportar => Items.Count > 0 && !SinPermiso && !SinConexion && !Cargando`; se re-notifica en el `OnItemsChanged` y en `OnPropertyChanged` de `Cargando`/`SinPermiso`/`SinConexion` (patrón de `ControlPoaViewModel`). | `ControlPoaViewModel.cs` L139-153 (4a). |
| Criterio de Tareas | `public sealed record OpcionCriterio(string Nombre, CriterioFechaTareas Valor)`; `CriteriosDisponibles` = [("Fecha de creación", Creacion), ("Fecha de cierre", Cierre)]; `CriterioOpcionSeleccionada` ↔ `CriterioSeleccionado` en los dos sentidos (partial `On…Changed`, sin bucle). | `ReporteTareasViewModel.cs` `OpcionAgrupador`, `EsCriterioCreacion`; `LibroCajaViewModel.OpcionesModo`. |
| Marco local | `UserControl` con `x:DataType="vm:ViewModelBase"` (bindings compilados a `Cargando`, `SinPermiso`, `SinConexion`, `MensajeSinPermiso`, `ReintentarCargaCommand`, todos públicos en la base) y ranuras `StyledProperty<object?>` presentadas con `ContentControl` bindeado a `$parent[r:MarcoReporte]`. El contenido de una ranura hereda el `DataContext` del VM del reporte. | `ViewModelBase.cs` L99-166. |
| Tests heredados que buscan el sistema viejo | `SignoNegativoBadgeTests` (StockCategoria busca `BadgeEstado`), `GrillaStockHelpers`, `ReportesStockOrdenTests`, `DataGridCeldaNumericaOrdenTests`, `ReportesAlineacionNumericaTests`, `ReporteTareasCriterioCierreAvisoTests` (busca el `TextBlock` del aviso), `CargaProtegidaEstadoVacioUiTests` (busca `c:EstadoVacio`), `ExportarPdfBotonesGuardianTests`, `DataGridColumnasAutoAjusteTests` (headers "P. Costo"), `GrillasSinRecorteBarridoTests`, `GrillaEspacioDerechoVistaRealTests`, `GuardianDePatronTests`: se adaptan para aceptar el sistema v2 (convención 13), cada adaptación con su rojo. | grep de los seis tipos de vista en `tests/`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/ViewModels/Reportes/ReportesViewModel.cs` | VM del hub: gates por tarjeta y comandos de navegación. |
| `src/StockApp.Presentation/Views/Reportes/ReportesView.axaml` (+ `.axaml.cs`) | Vista del hub. |
| `src/StockApp.Presentation/Views/Reportes/MarcoReporte.axaml` (+ `.axaml.cs`) | Marco común LOCAL de los seis reportes (Q-MARCO). |
| `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReportesViewModelTests.cs` | Gates y navegación del hub (reemplaza los de los 6 ítems del shell). |
| `tests/StockApp.Presentation.UiTests/V2/ReportesFakes.cs` | Fakes de `IReporteStockService`, `IReporteTareasService`, `IAuditoriaQueryService`, `IUsuarioService`, `IProductoService` (pendiente con `TaskCompletionSource`, falla 403 / sin servidor, llamadas grabadas). |
| `tests/StockApp.Presentation.UiTests/V2/ReportesViewV2Tests.cs` | Hub con la View real: tarjetas, gates con operador mixto, click navega, sidebar, revocación. |
| `tests/StockApp.Presentation.UiTests/V2/MarcoReporteV2Tests.cs` | Estados del marco con la View real de Valorización; breadcrumb; sin primario. |
| `tests/StockApp.Presentation.UiTests/V2/ReportesInventarioViewV2Tests.cs` | Valorización y Stock por categoría. |
| `tests/StockApp.Presentation.UiTests/V2/ReportesConFiltrosViewV2Tests.cs` | Más movidos, Historial por producto, Log de auditoría y Estadística de tareas. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/DatosReportes.cs`, `DatosReportesTests.cs` | Datos fijos de los 15 mockups y sus textos. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/ReportesComparacionTests.cs` | 30 comparaciones (15 pantallas × 2 tamaños). |
| `docs/ui-v2/mockups/reportes/*.html` | Los 15 mockups de Q-MOCKUPS. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | ANÁLISIS a un ítem; `NavReportes`; `SeccionPorPantalla`; se borran 6 `Nav*` y `PuedeVerHistorialPorProducto` (Q-SIDEBAR, Q-SECCION). |
| `src/StockApp.Presentation/App.axaml.cs` | Registro DI de `ReportesViewModel`. |
| `src/StockApp.Presentation/ViewModels/Reportes/{Valorizacion,StockCategoria,HistorialPorProducto,MasMovidos,AuditoriaLog,ReporteTareas}ViewModel.cs` | Lo que lista la Task 2. |
| `src/StockApp.Presentation/Views/Reportes/{Valorizacion,StockCategoria,HistorialPorProducto,MasMovidos,AuditoriaLog,ReporteTareas}View.axaml` | Reescritas en v2 sobre `MarcoReporte`; code-behind sin cambios (salvo lo que pida un UiTest). |
| `docs/ui-v2/mockups/shell/sidebar-admin.html` | ANÁLISIS con un solo ítem (Q-SIDEBAR-MOCKUPS). |
| Tests heredados (convención 13) | Los de la tabla "Tests heredados…" y los del shell de Q-SIDEBAR. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 8, después del rebase). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln --blame-hang-timeout 5m`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh Reportes` (y `Sidebar`) y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-17-ui-v2-fase-6-reportes/progress.md`. Se escribió en el worktree `ui-v2` junto con este plan; como `.superpowers/` está en `.gitignore`, la Task 0 lo COPIA al worktree de la fase y desde ahí es la única copia viva. Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa. Arriba de todo, las respuestas del usuario a las Q-*.

---

## Bloque 0 — Worktree propio

### Task 0: Worktree de la fase, índice CodeGraph y línea base

**Files:**
- Create (fuera del repo versionado): el worktree `~/workspace/stockapp-worktrees/ui-v2-fase6`, su `.codegraph/` y su copia del ledger.

**Interfaces:**
- Consumes: `feat/ui-v2` con la 4e mergeada y este plan commiteado.
- Produces: rama `feat/ui-v2-fase6`; `BASE` (hash de `feat/ui-v2` del que sale) anotado en el ledger; la línea base de la suite.

- [ ] **Step 1: Verificar el punto de partida**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && git status --short && git log --oneline -15
```

Expected: el cierre de la 4e en el log ("cierre de la Fase 4e" o la línea de la 4e en el Estado de la spec: `grep -n "Fase 4e" docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`) y este plan commiteado. Si la 4e no está mergeada, PARAR y avisar (el esquema aprobado exige salir después de la 4e). No hacer nada en este worktree: lo usan las otras fases.

- [ ] **Step 2: Crear el worktree y la rama**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && git worktree add -b feat/ui-v2-fase6 /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 feat/ui-v2
git -C /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 rev-parse HEAD
```

Expected: el hash de la punta de `feat/ui-v2`; anotarlo como `BASE`. Nunca bajo `/tmp`.

- [ ] **Step 3: Índice CodeGraph propio**

```bash
gentle-ai codegraph init --cwd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 && codegraph status && codegraph query ReportesViewModel; codegraph query ValorizacionViewModel
```

Expected: `.codegraph/` dentro de `ui-v2-fase6`; `ValorizacionViewModel` encontrado con ruta de `ui-v2-fase6`; `ReportesViewModel` sin resultados (todavía no existe). Nunca copiar el `.codegraph/` de otro checkout.

- [ ] **Step 4: Copiar el ledger**

```bash
mkdir -p /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6/.superpowers/sdd/2026-10-17-ui-v2-fase-6-reportes
cp /home/capua25/workspace/stockapp-worktrees/ui-v2/.superpowers/sdd/2026-10-17-ui-v2-fase-6-reportes/progress.md /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6/.superpowers/sdd/2026-10-17-ui-v2-fase-6-reportes/progress.md
```

Anotar en el ledger de la fase: `BASE`, la fecha y que la copia de `ui-v2` queda congelada.

- [ ] **Step 5: Línea base**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 && dotnet build StockApp.sln && dotnet test StockApp.sln --blame-hang-timeout 5m
```

Expected: 0 errores; anotar el total por proyecto en el ledger y los rojos conocidos por nombre (`.ods`, intermitente, flaky). Un rojo distinto: PARAR y reportar antes de la Task 1.

- [ ] **Step 6: Q-* al usuario (PARADA corta)**

El orquestador presenta las 13 Q-* con su recomendación, todas juntas, y anota las respuestas arriba del ledger. Sin commit (no hay cambios versionados).

---

## Bloque A — ViewModels, sin tocar vistas

### Task 1: Hub de Reportes (VM) y sidebar con un solo ítem

**Decisiones aplicadas:** Q-SIDEBAR, Q-SECCION, Q-PERMISO-HISTORIAL, D1, D2 (gates).

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Reportes/ReportesViewModel.cs`, `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReportesViewModelTests.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, `src/StockApp.Presentation/App.axaml.cs`
- Modify (convención 13): `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs` (L282-329, L800-895), `ShellMainViewModelReportesTests.cs`, `ShellMainViewModelGruposTests.cs` (L50-60, L180), `ShellMainSeccionActivaTests.cs` (L66-67, L196-198); `tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs` (L124, L254-290); `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession` (`RolActual`, `PermisosActuales`), `INavigationService.Navegar<T>()`, `Permisos.VerReportes|RegistrarMovimientos|GestionarProductos`.
- Produces:
  - `public partial class ReportesViewModel : ViewModelBase` con ctor `(ICurrentSession session, INavigationService navegacion)`.
  - `public bool PuedeVerReportes` (Admin o `reportes.ver`) y `public bool PuedeVerHistorialPorProducto` (Admin o `reportes.ver` + `movimientos.registrar` + `catalogo.productos`).
  - Comandos `AbrirValorizacionCommand`, `AbrirStockCategoriaCommand`, `AbrirHistorialPorProductoCommand`, `AbrirMasMovidosCommand`, `AbrirAuditoriaCommand`, `AbrirEstadisticaTareasCommand` (`IRelayCommand`), cada uno `Navegar<XViewModel>()`.
  - En el shell: `NavReportesCommand` (`SeccionActiva = "Reportes"`, `Navegar<ReportesViewModel>()`); ítem `CrearItem("Reportes", "mdi-chart-box-outline", NavReportesCommand, "Reportes", () => PuedeVerReportes)` como único ítem de ANÁLISIS; `SeccionPorPantalla` con `ReportesViewModel` y los 6 VMs de reporte → `"Reportes"`.

- [ ] **Step 1: Tests del hub (rojo de compilación)**

En `ReportesViewModelTests` (`SesionFake` o el `Mock<ICurrentSession>` del archivo vecino; `Mock<INavigationService>`):

```csharp
[Theory]
[InlineData(new[] { Permisos.VerReportes }, true, false)]
[InlineData(new[] { Permisos.VerReportes, Permisos.RegistrarMovimientos }, true, false)]
[InlineData(new[] { Permisos.VerReportes, Permisos.RegistrarMovimientos, Permisos.GestionarProductos }, true, true)]
[InlineData(new[] { Permisos.RegistrarMovimientos, Permisos.GestionarProductos }, false, false)]
public void Operador_GatesDeLasTarjetas(string[] permisos, bool verReportes, bool historial)
// Assert.Equal(verReportes, vm.PuedeVerReportes); Assert.Equal(historial, vm.PuedeVerHistorialPorProducto);

[Fact] public void Admin_VeTodasLasTarjetas()                         // los dos true
[Fact] public void Gates_SeReevaluanConLosPermisosActuales_SinRecrear() // cambiar PermisosActuales del fake → el getter cambia
[Fact] public void CadaTarjeta_NavegaASuReporte()                       // los 6 comandos → Navegar<T>() Times.Once cada uno
[Fact] public void NotificarPermisosActualizados_NotificaLosGates()     // PropertyChanged con "" (base)
```

Agregar en `ShellMainViewModelGruposTests` el reemplazo de L60: `[InlineData("ANÁLISIS", "Reportes")]`. En `ShellMainSeccionActivaTests`, los 7 tipos (hub + 6) → `"Reportes"` en el diccionario de L66-67 y el caso de L196-198 pasa a `Navegar<ValorizacionViewModel>()` → `"Reportes"`. En `ShellMainViewModelTests`, los 5 `Nav<Reporte>_EstableceSeccionActiva_*` (L282-329) se REEMPLAZAN por `NavReportes_EstableceSeccionActiva_Reportes_YNavegaAlHub`; los tests de `PuedeVerHistorialPorProducto` (L800-895) se MUDAN a `ReportesViewModelTests` (la Theory de arriba los cubre: cada caso de L811-862 queda como fila; el de L866, "la navegación refresca y notifica", deja de aplicar al hub porque sus gates son getters: lo reemplaza `Gates_SeReevaluanConLosPermisosActuales_SinRecrear`). `ShellMainViewModelReportesTests.NavReportes_LlamaNavegar_ConViewModelCorrecto` se reemplaza por `NavReportes_NavegaAlHub`. `ComposicionDIApiTests`: agregar `ReportesViewModel` a los VMs que se resuelven.

En `ShellMainViewGatesTests`: L124 (`NavValorizacionCommand` oculto) → `NavReportesCommand` oculto; `OperadorConSoloVerReportes_VeLosReportesPeroNO_HistorialPorProducto` (L254-264) → `OperadorConSoloVerReportes_VeElItemReportes` (el ítem visible; la parte de Historial la custodia el hub en la Task 4); L290 → `NavReportesCommand` visible.

Run: `dotnet build StockApp.sln`. Expected: error de compilación (`ReportesViewModel` y `NavReportesCommand` no existen). Pegar en el ledger.

- [ ] **Step 2: Implementar el hub y el shell**

`ReportesViewModel` con los getters y los 6 `[RelayCommand]`. En el shell: el grupo ANÁLISIS con el único ítem de Produces; `NavReportes`; borrar `NavValorizacion` … `NavReporteTareas` y `PuedeVerHistorialPorProducto` (también su `OnPropertyChanged` en `NotificarPermisos`); `SeccionPorPantalla` con las 7 entradas a `"Reportes"`; borrar el comentario "P1 (sidebar transitorio): ANÁLISIS …" (L249) dejando solo la parte de ADMINISTRACIÓN (que es de la Fase 7). `App.axaml.cs`: `services.AddTransient<ReportesViewModel>();` junto a los otros VMs de reportes (L336-341).

- [ ] **Step 3: Verde y suites**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
```

Expected: verde salvo `ShellComparacionTests` (VisualTests, no se corre acá: el mockup del sidebar se actualiza en la Task 3). Anotar los totales y la lista de tests reemplazados con su reemplazo.

- [ ] **Step 4: Mutaciones (QUITANDO), rojo pegado**

1a. Quitar `&& _session.PermisosActuales.Contains(Permisos.GestionarProductos)` de `PuedeVerHistorialPorProducto` → rojo en la fila de la Theory sin `GestionarProductos`.
1b. Quitar la entrada `[typeof(ValorizacionViewModel)] = "Reportes",` de `SeccionPorPantalla` → rojo en `ShellMainSeccionActivaTests`.
1c. Quitar el `CrearItem("Reportes", …)` → rojo en `ShellMainViewModelGruposTests` y `ShellMainViewGatesTests`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/ReportesViewModel.cs src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReportesViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelGruposTests.cs tests/StockApp.Presentation.Tests/ViewModels/ShellMainSeccionActivaTests.cs tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs tests/StockApp.Presentation.UiTests/ShellMainViewGatesTests.cs
git commit -m "feat(reportes): hub de Reportes y un solo ítem en ANÁLISIS"
```

### Task 2: Los seis VMs de reporte: estados v2, consulta vigente, PDF de lo cargado y vuelta al hub

**Decisiones aplicadas:** Q-VOLVER, Q-ESTADOS, Q-RECARGA (A: Buscar explícito), Q-HISTORIAL-INICIAL, Q-CRITERIO, D7 (textos de vacío), D10.

**Files:**
- Modify: los seis `src/StockApp.Presentation/ViewModels/Reportes/*ViewModel.cs` (no `ReportesViewModel`)
- Modify (tests): `tests/StockApp.Presentation.Tests/ViewModels/Reportes/{Valorizacion,StockCategoria,HistorialPorProducto,MasMovidos,AuditoriaLog,ReporteTareas}ViewModelTests.cs`

**Interfaces:**
- Consumes: `ReportesViewModel` (Task 1); de la base `EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `AvisaSinConexionEnLinea`, `AlPerderConexion`, `ReintentarAsync(Func<Task>)`, `Cargando`, `SinPermiso`, `SinConexion`, `ReintentarCargaCommand`.
- Produces (en los seis):
  - ctor con último parámetro `INavigationService? navegacion = null`; `public IRelayCommand VolverCommand` (`Navegar<ReportesViewModel>()`, `CanExecute => navegacion is not null`).
  - `ExportarPdfCommand` con `CanExecute = PuedeExportar` (`Items.Count > 0 && !SinPermiso && !SinConexion && !Cargando`) y re-notificado (D10).
  - `public string TituloVacio { get; }`, `public string MensajeVacio { get; }` con los textos de D7 (en Historial, getters que dependen de `ProductoSeleccionado` y se notifican en `OnProductoSeleccionadoChanged`).
- Produces (específicos):
  - `ReporteTareasViewModel`: `public sealed record OpcionCriterio(string Nombre, CriterioFechaTareas Valor)`, `public IReadOnlyList<OpcionCriterio> CriteriosDisponibles`, `[ObservableProperty] OpcionCriterio _criterioOpcionSeleccionada` sincronizada con `CriterioSeleccionado`.
  - `HistorialPorProductoViewModel.CargarAsync`: sin producto, `Items = []` y no llama al servicio.
  - `AuditoriaLogViewModel.ReintentarAsync` override: `Usuarios.Count == 0 ? InicializarAsync() : CargarAsync()`.

- [ ] **Step 1: Tests (rojo)**

Por VM, en su clase de tests (los `Crear` existentes siguen compilando: el parámetro nuevo es opcional; los tests nuevos lo pasan). Nombres y aserciones:

En los seis (una Theory por archivo no aplica: cada archivo tiene los suyos con el mismo nombre):
```csharp
[Fact] public async Task SinConexion_MarcaSinConexion_VaciaLasFilas_YNoPropaga()
// el fake lanza ServidorNoDisponibleException tras una carga OK; Assert.True(vm.SinConexion); Assert.Empty(vm.Items);
// (Valorización: Assert.Null(vm.Totales); Tareas: Assert.Equal(0, vm.TotalGeneral))
[Fact] public async Task Un403_MarcaSinPermiso_YNoPropaga()
[Fact] public async Task ExportarPdf_SinFilas_EstaDeshabilitado()           // CanExecute false con Items vacío
[Fact] public async Task ExportarPdf_ConFilas_SeHabilita_YSeAvisaElCambio() // suscribir CanExecuteChanged (lección de la 4d T9)
[Fact] public async Task ExportarPdf_TrasUn403_EstaDeshabilitado()
[Fact] public void VolverCommand_NavegaAlHub()                               // Navegar<ReportesViewModel>() Times.Once
[Fact] public void VolverCommand_SinNavegacion_EstaDeshabilitado()
[Fact] public void TextosDelVacio_SonLosAprobados()                          // D7 literal
```

En los cuatro con filtros (Más movidos, Historial, Auditoría, Tareas):
```csharp
[Fact] public async Task DosBusquedas_LaPrimeraTerminaTarde_NoPisaLaSegunda()
// dos TaskCompletionSource; se completa primero la segunda; Assert.Equal(filasDeLaSegunda, vm.Items)
[Fact] public async Task ExportarPdf_ConFechasCambiadasSinBuscar_RotulaElPeriodoCargado()
// cargar con Desde=01/09/2026 Hasta=30/09/2026, cambiar FechaDesde a 01/08/2026 sin Buscar, exportar;
// Assert.Contains("01/09/2026", metadatos.DescripcionFiltros); Assert.DoesNotContain("01/08/2026", …)
[Fact] public async Task SinConexion_Reintentar_ConsultaConLosFiltrosDePantalla()
```

Específicos:
```csharp
// HistorialPorProductoViewModelTests
[Fact] public async Task SinProducto_NoConsultaAlServicio()   // Verify(ObtenerHistorialPorProductoAsync, Times.Never); Assert.Empty(vm.Items)
[Fact] public void SinProducto_ElVacioPideElegirUnProducto()   // TituloVacio == "Elegí un producto"
[Fact] public async Task Un403_YaNoSeTragaEnSilencio_MarcaSinPermiso()
// AuditoriaLogViewModelTests
[Fact] public async Task Un403EnBuscar_MarcaSinPermiso_YNoPropaga()
[Fact] public async Task SinConexionAlCargarUsuarios_Reintentar_CargaUsuariosYLog()
// ReporteTareasViewModelTests
[Fact] public void CriteriosDisponibles_SonCreacionYCierre()
[Fact] public void ElegirCierreEnElCombo_CambiaElCriterio_YEsCriterioCierre()
[Fact] public void CambiarCriterioSeleccionado_ActualizaElCombo()
```

Run: `dotnet build StockApp.sln` (rojo de compilación: `VolverCommand`, `TituloVacio`, `CriteriosDisponibles`) y, tras stubs vacíos que compilen, `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~ViewModels.Reportes"`. Pegar el rojo de comportamiento.

- [ ] **Step 2: Implementar**

En cada VM, lo de la tabla "Decisiones de diseño del plan" (carga, consulta vigente, sin conexión, PDF de lo cargado, Exportar habilitado). Un `private readonly ConsultaVigente _consultas = new();` por VM; `CargarAsync` pide el turno ANTES de llamar al servicio. En Valorización y Stock la consulta vigente también aplica (Actualizar dos veces). `ReintentarAsync` por defecto (repite la carga fallida) salvo Auditoría. Las validaciones de fecha (`MensajeError`) se conservan tal cual, antes de pedir el turno.

- [ ] **Step 3: Verde y suites**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
```

Expected: verde. Los tests heredados de los seis VMs NO cambian salvo que un `Crear` necesite el parámetro nuevo (anotar cuál y por qué). `CargaProtegidaSinPermisoGuardianTests` verde (si el regex de firmas da un falso rojo, ver el fix `b6ab739` de la 4d antes de tocar el VM).

- [ ] **Step 4: Mutaciones (QUITANDO), rojo pegado**

2a. Quitar `if (!turno.EsLaUltima) return;` (o la condición equivalente) en `MasMovidosViewModel` → rojo `DosBusquedas_…`. Repetir en Auditoría (2b).
2b. Quitar `protected override bool AvisaSinConexionEnLinea => true;` de `ValorizacionViewModel` → rojo `SinConexion_…`.
2c. Quitar el cuerpo de `AlPerderConexion` en `StockCategoriaViewModel` → rojo `SinConexion_…` (Items no se vacía).
2d. Quitar la línea `_descripcionCargada = descripcion;` del aplicar de `ReporteTareasViewModel` (el PDF cae al respaldo `_descripcionCargada ?? ConstruirDescripcionFiltros()`, que lee los filtros en vivo) → rojo `ExportarPdf_ConFechasCambiadasSinBuscar_…`.
2e. Quitar `if (ProductoSeleccionado is null) { Items = []; return; }` de Historial → rojo `SinProducto_NoConsultaAlServicio`.
2f. Quitar `Items.Count > 0 &&` de `PuedeExportar` en Más movidos → rojo `ExportarPdf_SinFilas_EstaDeshabilitado`.
2g. Quitar el `NotifyCanExecuteChanged` de `ExportarPdfCommand` en `OnItemsChanged` de Valorización → rojo `ExportarPdf_ConFilas_SeHabilita_YSeAvisaElCambio`.
2h. Quitar el override de `ReintentarAsync` de Auditoría → rojo `SinConexionAlCargarUsuarios_Reintentar_…`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Reportes/ tests/StockApp.Presentation.Tests/ViewModels/Reportes/
git commit -m "feat(reportes): estados v2, consulta vigente, PDF de lo cargado y vuelta al hub en los seis reportes"
```

---

## Bloque B — Datos fijos, medición y mockups (UN solo GATE 1)

### Task 3: Datos fijos, medición y los 15 mockups de la fase — GATE 1

**Decisiones aplicadas:** D1 a D11, Q-MOCKUPS, Q-SIDEBAR-MOCKUPS.

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/DatosReportes.cs`, `DatosReportesTests.cs`
- Create: `docs/ui-v2/mockups/reportes/hub.html`, `hub-operador.html`, `valorizacion.html`, `valorizacion-cargando.html`, `valorizacion-sin-conexion.html`, `valorizacion-sin-permiso.html`, `valorizacion-vacio.html`, `stock-categoria.html`, `historial-producto.html`, `historial-producto-sin-producto.html`, `mas-movidos.html`, `mas-movidos-error-fechas.html`, `auditoria.html`, `tareas.html`, `tareas-cierre.html`
- Modify: `docs/ui-v2/mockups/shell/sidebar-admin.html`
- Modify (local): el ledger (anchos y altos medidos)

**Interfaces:**
- Consumes: los VMs de las Tasks 1 y 2; `DatosShell.Crear`, `DatosProductos.UsuarioAdmin`, el operador de `DatosIngresos.UsuarioOperador` (o uno propio con `reportes.ver`).
- Produces: `DatosReportes` con fábricas estáticas `HubAdmin()`, `HubOperador()` (operador con `reportes.ver` solo: 5 tarjetas), `Valorizacion()`, `ValorizacionCargando()`, `ValorizacionSinConexion()`, `ValorizacionSinPermiso()`, `ValorizacionVacia()`, `StockCategoria()`, `HistorialProducto()`, `HistorialSinProducto()`, `MasMovidos()`, `MasMovidosErrorFechas()`, `Auditoria()`, `Tareas()`, `TareasCierre()`; cada una devuelve el `ShellMainViewModel` con `CurrentContent` = el VM ya cargado y `SeccionActiva = "Reportes"`. Los 15 mockups (30 PNG `reportes-*.png`). En el ledger, los anchos medidos que usan las Tasks 4 a 6 (si difieren de D4/D5, los reemplazan).

- [ ] **Step 1: `DatosReportesTests` (rojo) y `DatosReportes`**

Tests: `CadaFabrica_DejaElReporteCargadoSinEstadosDeError` (por fábrica con datos: `Items` con la cantidad de D11, `Cargando`, `SinPermiso`, `SinConexion` false), `ValorizacionSinConexion_TieneSinConexion`, `ValorizacionSinPermiso_TieneSinPermisoYMensaje`, `ValorizacionCargando_TieneCargandoYSinFilas` (servicio pendiente), `MasMovidosErrorFechas_TieneElMensajeDeHoy`, `TareasCierre_EsCriterioCierre`, `HubOperador_SinHistorial` (`PuedeVerHistorialPorProducto` false), `Valorizacion_TieneUnStockNegativo`, `StockCategoria_TieneUnStockTotalNegativo`, `HistorialProducto_UnMovimientoDejaElStockNegativo`. `VisualTests` no ve los `internal` del VM: todo se arma con la API pública y fakes. Rojo de compilación pegado; implementar; verde.

- [ ] **Step 2: Medir en headless (archivo temporal, no se commitea)**

`tests/StockApp.Presentation.VisualTests/MedicionTemporal6.cs` siguiendo `MedicionTemporal4d` (descripción en el ledger de la 4d, Task 12): con el tema v2 (`Classes="ui2"`), a 1040 y 1126 de vista, el árbol del XAML previsto armado en C#, con `--logger "console;verbosity=detailed"`:
1. Hub: alto de una `TarjetaAcceso` con la descripción más larga de D2 en una columna de `GrillaUniformeV2` de 3 (que la descripción no corte a 1040: si corta, proponer en el GATE dos líneas o un texto más corto).
2. Por grilla de D5: `AnchoMedido` de cada `ColumnaAuto` con su conversor sobre los datos de D11, el ancho fijo de STOCK / STOCK TOTAL / STOCK NUEVO (insignia "Stock negativo" + el número más ancho + padding, como el 220 heredado) y si entra sin scroll horizontal a los dos tamaños.
3. Filtros de D4: el ancho de cada fila (con "Organismo responsable" y "Origen de financiamiento" sin cortar en el combo de Agrupar por, "Fecha de creación" en Contar por) y el alto del `AutoCompleteBox` dentro de `CampoFormulario` (36 esperado; si no, va a "Necesidades de tema compartido", punto 2).
4. Encabezado: breadcrumb + título + acciones en una línea a 1040.
5. Alto de la card de Tareas con el aviso de cierre (que la grilla conserve al menos 5 filas a 1280×760).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~MedicionTemporal6" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger.

- [ ] **Step 3: Mockups**

Partir de `inicio/inicio-admin.html` (tarjetas de acceso en grilla uniforme de 3, `class="v2-grilla-uniforme v2-grilla-uniforme--3"`), `inventario/historial.html` (barra de filtros con `CampoFormulario`, fechas, autocompletar, Buscar secundario), `inventario/productos.html` (insignia "Stock negativo" + número negativo), `finanzas/gastos-*.html` (breadcrumb, estados en la card), `finanzas/libro-caja.html` (pantalla de consulta sin primario, `Alerta`). Cada mockup: `<title>Reportes: <Pantalla>[ estado] (UI v2)</title>`, `<!-- Datos: DatosReportes.<Fabrica>: … -->`, sidebar NUEVO (ANÁLISIS: solo "Reportes", activo). Contenido por archivo:
- `hub`: encabezado de D2 y las 6 tarjetas; `hub-operador`: 5 tarjetas (sin Historial), sidebar del operador con `reportes.ver`.
- `valorizacion`: breadcrumb "Reportes / Valorización de inventario", acciones Actualizar y Exportar PDF (habilitado); card sin filtros con la grilla de D5 (8 filas, una con insignia "Stock negativo" y número en peligro) y el pie de D6 ("8 productos" · "Total valor costo $ …").
- `valorizacion-cargando`, `-sin-conexion`, `-sin-permiso` (en lugar de la card), `-vacio`: los estados de D7, Exportar PDF deshabilitado.
- `stock-categoria`, `historial-producto` (Producto "Cemento portland 50 kg", Desde/Hasta, 9 filas), `historial-producto-sin-producto` (filtros vacíos y "Elegí un producto"), `mas-movidos` (Top 10), `mas-movidos-error-fechas` (Desde 30/09/2026 > Hasta 01/09/2026 y la `Alerta` Error de D8 sobre la grilla anterior), `auditoria` (Usuario "Todos"), `tareas` (Agrupar por "Zona", Contar por "Fecha de creación", 5 filas, "Total general"), `tareas-cierre` (Contar por "Fecha de cierre" y la `Alerta` Advertencia de D9).
- `shell/sidebar-admin.html`: ANÁLISIS con un solo ítem "Reportes" (lo demás igual).

- [ ] **Step 4: Capturas y verificación de la herramienta**

```bash
for p in hub hub-operador valorizacion valorizacion-cargando valorizacion-sin-conexion valorizacion-sin-permiso valorizacion-vacio \
         stock-categoria historial-producto historial-producto-sin-producto mas-movidos mas-movidos-error-fechas auditoria tareas tareas-cierre; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/reportes/$p.html artifacts/ui-v2/mockups/reportes-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/reportes/$p.html artifacts/ui-v2/mockups/reportes-$p@1366x768.png --tamano 1366x768
done
dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/shell/sidebar-admin.html artifacts/ui-v2/mockups/shell-sidebar-admin.png
ls artifacts/ui-v2/mockups/reportes-*.png | wc -l
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 30 PNG con fecha de ahora; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 5: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal6.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 6: GATE 1 — aprobación del usuario (PARADA, la única de diseño de la fase)**

PARAR. Presentar JUNTOS los 30 PNG (los dos tamaños) más el del sidebar, la lista D1 a D11, las respuestas a las Q-* y los anchos medidos. Señalar explícitamente: D2 (hub sin card, textos de las tarjetas), D3 (breadcrumb sin subtítulo), Q-ACCIONES (sin primario), D5 (columnas y STOCK de ancho fijo), D6 (pies y totales), D7 (los siete textos de vacío), D9 (aviso como `Alerta`), Q-CRITERIO (combo en lugar de radios), Q-USUARIO-AUDITORIA, y que los estados de los otros cinco reportes no tienen mockup propio (Q-MOCKUPS). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups y en los tests de VM que la fijan (con su rojo y su verde), y volver a presentar. Registrar en el ledger: `Mockups 6: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 7: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/DatosReportes.cs tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/DatosReportesTests.cs docs/ui-v2/mockups/reportes/ docs/ui-v2/mockups/shell/sidebar-admin.html
git commit -m "docs(ui-v2): mockups aprobados del hub y los seis reportes"
```

---

## Bloque C — Vistas v2 (arranca solo con los mockups aprobados)

### Task 4: `MarcoReporte` y el hub v2

**Decisiones aplicadas:** Q-MARCO, D2, D3, D7, D8; Q-PERMISO-HISTORIAL (View real).

**Files:**
- Create: `src/StockApp.Presentation/Views/Reportes/MarcoReporte.axaml` (+ `.axaml.cs`), `src/StockApp.Presentation/Views/Reportes/ReportesView.axaml` (+ `.axaml.cs`)
- Create: `tests/StockApp.Presentation.UiTests/V2/ReportesFakes.cs`, `tests/StockApp.Presentation.UiTests/V2/ReportesViewV2Tests.cs`, `tests/StockApp.Presentation.UiTests/V2/MarcoReporteV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/ReportesComparacionTests.cs` (las 4 del hub; las demás las agregan las Tasks 5 y 6)
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`ReportesView` a `VistasMigradasAV2`)

**Interfaces:**
- Consumes: `ReportesViewModel` (Task 1); los VMs de la Task 2 (`VolverCommand`, `TituloVacio`, `MensajeVacio`); `DatosReportes` (Task 3).
- Produces: `public partial class MarcoReporte : UserControl` (`x:DataType="vm:ViewModelBase"`, namespace `StockApp.Presentation.Views.Reportes`, xmlns `r`) con:
  - `StyledProperty<string?> Titulo`, `StyledProperty<ICommand?> VolverCommand`, `StyledProperty<object?> Acciones`, `StyledProperty<object?> Filtros` (null: sin `BarraFiltros`), `StyledProperty<string?> MensajeError`, `StyledProperty<object?> Contenido`, `StyledProperty<object?> Pie`, `StyledProperty<bool> HayDatos`, `StyledProperty<string?> TituloVacio`, `StyledProperty<string?> MensajeVacio`.
  - `x:Name` internos: `Encabezado` (`EncabezadoPagina`, `BreadcrumbOrigen="Reportes"`), `EstadoSinPermiso`, `Tarjeta`, `BarraDeFiltros`, `AlertaError`, `IndicadorCarga`, `EstadoSinConexion` (con `BotonReintentar`), `EstadoVacio`, `ZonaContenido`, `ZonaPie`.
  - Visibilidades: `EstadoSinPermiso` = `SinPermiso`; `Tarjeta` = `!SinPermiso`; `IndicadorCarga` = `Cargando && !HayDatos`; `EstadoSinConexion` = `SinConexion`; `EstadoVacio` = `!Cargando && !SinPermiso && !SinConexion && !HayDatos` (`MultiBinding` + `BoolConverters.And`, convención 18); `ZonaContenido` y `ZonaPie` = `HayDatos && !SinConexion`; `AlertaError` = `MensajeError` no vacío.
  - `ReportesView`: `x:Name` de las 6 tarjetas de D2, `IsVisible` sobre `PuedeVerReportes` / `PuedeVerHistorialPorProducto`.

- [ ] **Step 1: UiTests (rojo)**

`ReportesViewV2Tests` (View real dentro de `ShellMainView`, `SesionFake` con operador MIXTO):
```csharp
[AvaloniaFact] public void Raiz_TieneUi2_TituloReportes_SinPrimario_SinBreadcrumb()
[AvaloniaFact] public void Admin_SeisTarjetas_EnElOrdenDeLaSpec()            // títulos en el orden de D2
[AvaloniaFact] public void OperadorConSoloVerReportes_CincoTarjetas_SinHistorial()
[AvaloniaFact] public void OperadorConVerReportesYMovimientosSinCatalogo_NoVeHistorialPorProducto()   // Review Focus 3
[AvaloniaFact] public void OperadorConVerReportesMovimientosYCatalogo_VeHistorialPorProducto()
[AvaloniaFact] public void ClickEnUnaTarjeta_NavegaAlReporte_YElSidebarSigueEnReportes()             // click real
[AvaloniaFact] public void RevocarVerReportes_DentroDelShell_LasTarjetasYElItemDesaparecen()          // ManejadorAccesoRevocado
[AvaloniaFact] public void HistorialOculto_NoDejaHueco()   // con 5 tarjetas, la 4.ª ocupa la columna 1 de la fila 2
```

`MarcoReporteV2Tests` (View real de Valorización, la más simple, con `ReportesFakes`):
```csharp
[AvaloniaFact] public void Breadcrumb_ReportesYElTitulo_ElPrimerTramoVuelveAlHub()   // click en el tramo → Navegar<ReportesViewModel>
[AvaloniaFact] public void Cargando_SinFilas_MuestraElIndicador_SinVacioNiGrilla()
[AvaloniaFact] public void Cargando_ConFilas_NoMuestraElIndicador()                    // recarga con datos a la vista
[AvaloniaFact] public void SinConexion_EstadoEnLineaConReintentar_SinModalGlobal()
[AvaloniaFact] public void SinPermiso_ReemplazaLaCard()
[AvaloniaFact] public void Vacio_SoloConLaCargaTerminadaYSinError()                    // con Cargando, SinPermiso, SinConexion: oculto
[AvaloniaFact] public void MensajeError_MuestraLaAlertaDeError()
[AvaloniaFact] public void SinFiltros_NoHayBarraDeFiltros()
[AvaloniaFact] public void NingunBotonPrimario()
```

Run: `dotnet build StockApp.sln` (rojo de compilación: `ReportesView`, `MarcoReporte`). Pegar.

- [ ] **Step 2: `MarcoReporte` y `ReportesView`**

`MarcoReporte.axaml`: `DockPanel` con `EncabezadoPagina` arriba (`BreadcrumbComando` = `VolverCommand`, `BreadcrumbActual` = `Titulo`, `Acciones` presentadas), `EstadoSinPermiso` (`UseLayoutRounding="False"`, como Historial de movimientos) y la `TarjetaContenido`: `DockPanel` con la `BarraFiltros MostrarBusqueda="False"` arriba (visible si `Filtros` no es null, su contenido = `Filtros`), `AlertaError` debajo, `ZonaPie` abajo, y en el centro `IndicadorCarga`, `EstadoSinConexion` (textos de la convención 18, `Button` secundario "Reintentar" → `ReintentarCargaCommand`), `EstadoVacio` y `ZonaContenido`. Las ranuras se presentan con `ContentControl Content="{Binding $parent[r:MarcoReporte].Filtros}"` (y así cada una). `ReportesView.axaml`: raíz `Classes="ui2"`, `Background="{DynamicResource V2FondoBrush}"`, `DockPanel Margin="28,24"`, `EncabezadoPagina` de D2 y `GrillaUniformeV2` con las 6 tarjetas; code-behind sin carga (el hub no consulta nada). Agregar `ReportesView` a `VistasMigradasAV2`.

- [ ] **Step 3: Comparaciones del hub**

En `ReportesComparacionTests` (patrón de `GastosComparacionTests`, `Comparar(vm, mockup, tamano)` con `OcultarSidebar`): `Hub_IgualAlMockup`, `Hub_IgualAlMockup_1366x768`, `HubOperador_IgualAlMockup`, `HubOperador_IgualAlMockup_1366x768`. Y `ShellComparacionTests` (las 4 del sidebar) con el mockup actualizado en la Task 3.

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
scripts/ui-v2/comparar.sh Hub && scripts/ui-v2/comparar.sh Sidebar
```

Expected: verde; las 8 filas APROBADAS con el umbral por defecto. Una que no pasa se diagnostica (¿ancho fijo en el mockup? ¿descripción que corta?); un `UmbralAprobado` solo tras revisar cada tesela con el usuario.

- [ ] **Step 4: Mutaciones (QUITANDO), rojo pegado**

4a. Quitar `IsVisible="{Binding PuedeVerHistorialPorProducto}"` de `TarjetaHistorialPorProducto` → rojo `OperadorConSoloVerReportes_CincoTarjetas_SinHistorial` y `…SinCatalogo_NoVeHistorialPorProducto`.
4b. Quitar `IsVisible="{Binding PuedeVerReportes}"` de `TarjetaAuditoria` → rojo `RevocarVerReportes_…`.
4c. Quitar `BreadcrumbComando="{Binding $parent[r:MarcoReporte].VolverCommand}"` del marco → rojo `Breadcrumb_…_ElPrimerTramoVuelveAlHub`.
4d. Quitar el término `!SinConexion` del `MultiBinding` de `EstadoVacio` → rojo `Vacio_SoloConLaCargaTerminadaYSinError`.
4e. Quitar el término `!HayDatos` del `IndicadorCarga` → rojo `Cargando_ConFilas_NoMuestraElIndicador`.
4f. Quitar `IsVisible="{Binding !SinPermiso}"` de `Tarjeta` → rojo `SinPermiso_ReemplazaLaCard`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Views/Reportes/MarcoReporte.axaml src/StockApp.Presentation/Views/Reportes/MarcoReporte.axaml.cs src/StockApp.Presentation/Views/Reportes/ReportesView.axaml src/StockApp.Presentation/Views/Reportes/ReportesView.axaml.cs tests/StockApp.Presentation.UiTests/V2/ReportesFakes.cs tests/StockApp.Presentation.UiTests/V2/ReportesViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/MarcoReporteV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/ReportesComparacionTests.cs
git commit -m "feat(ui-v2): hub de Reportes y marco común de los reportes"
```

### Task 5: Valorización de inventario y Stock por categoría v2

**Decisiones aplicadas:** Q-ACCIONES (Actualizar + Exportar PDF), D5, D6, D7, D10.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Reportes/ValorizacionView.axaml`, `StockCategoriaView.axaml`
- Create: `tests/StockApp.Presentation.UiTests/V2/ReportesInventarioViewV2Tests.cs`
- Modify: `ReportesComparacionTests.cs` (+12: Valorización en sus 5 estados y Stock por categoría, a los dos tamaños)
- Modify (convención 13, solo si fallan con motivo, cada uno con su rojo): `SignoNegativoBadgeTests` (L91, StockCategoria: aceptar `Insignia`), `ReportesStockOrdenTests` (L56-84), `ReportesAlineacionNumericaTests` (L36), `DataGridCeldaNumericaOrdenTests` (L107), `DataGridColumnasAutoAjusteTests` (L211, headers en mayúsculas), `CargaProtegidaEstadoVacioUiTests` (L129: aceptar `v2:EstadoVacio`), `ExportarPdfBotonesGuardianTests` (L123-182), `GrillasSinRecorteBarridoTests`, `GrillaEspacioDerechoVistaRealTests`, `GrillaStockHelpers`, `GuardianDePatronTests` (sacar las dos vistas de la `InlineData` del `HeaderVista` y de `VistasDeLaTanda`, agregarlas a `VistasMigradasAV2`)

**Interfaces:**
- Consumes: `MarcoReporte` (Task 4); `ValorizacionViewModel` y `StockCategoriaViewModel` (Task 2).
- Produces: las dos vistas con raíz `Classes="ui2"`, `x:Name` `Marco` (el `MarcoReporte`), `Grilla`, `BotonActualizar`, `BotonExportarPdf`, `PieConteo`, y en Valorización `PieTotal`.

- [ ] **Step 1: UiTests (rojo)**

```csharp
[AvaloniaTheory, InlineData(typeof(ValorizacionView)), InlineData(typeof(StockCategoriaView))]
public void Raiz_TieneUi2_BreadcrumbReportes_ActualizarYExportarSecundarios_SinPrimario(Type vista)
[AvaloniaFact] public void Valorizacion_SeisColumnasEnMayusculas_EnElOrdenAprobado()
[AvaloniaFact] public void StockCategoria_CuatroColumnasEnMayusculas_EnElOrdenAprobado()
[AvaloniaFact] public void StockNegativo_InsigniaPeligroYNumeroNegativo_SoloEnEsaFila()     // las dos vistas
[AvaloniaFact] public void Pie_ConteoYTotalValorCosto()                                     // "8 productos", MonedaConverter
[AvaloniaFact] public void ExportarPdf_DeshabilitadoSinFilas_HabilitadoConFilas()
[AvaloniaFact] public void Actualizar_RecargaElReporte()                                    // dos llamadas al fake
[AvaloniaFact] public void Un403AlActualizar_DentroDelShell_SinPermisoPdfDeshabilitadoYSinItemReportes()   // Review Focus 4, operador mixto, camino real
[AvaloniaFact] public void Scroll_HastaLaFilaNegativa_NoMueveNingunaColumna()               // convención 14
```

Run el filtro de la clase nueva: rojo pegado.

- [ ] **Step 2: Reescribir las dos vistas**

Raíz `UserControl` con `Classes="ui2"`, `Background="{DynamicResource V2FondoBrush}"`, `DockPanel Margin="28,24"` y el `MarcoReporte` (`Titulo`, `VolverCommand="{Binding VolverCommand}"`, `HayDatos="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}"`, `TituloVacio`/`MensajeVacio` del VM), `Acciones` con los dos botones `secundario`, `Contenido` con la `DataGrid` de D5 (anchos MEDIDOS del ledger; columna de stock como `ProductoListView`: `Insignia` + `TextBlock Classes.negativo`, ancho fijo), `Pie` de D6. Code-behind: el `DataContextChanged` de hoy sin cambios.

- [ ] **Step 3: Comparaciones y suites**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
scripts/ui-v2/comparar.sh Valorizacion && scripts/ui-v2/comparar.sh StockCategoria
```

Expected: verde; 12 filas APROBADAS (o `UmbralAprobado` del usuario con cada tesela revisada). Anotar las adaptaciones heredadas con su rojo previo.

- [ ] **Step 4: Mutaciones (QUITANDO), rojo pegado**

5a. Quitar `Classes.negativo="…"` del número de STOCK en Valorización → rojo `StockNegativo_…` (y la comparación de `valorizacion`).
5b. Quitar el `IsVisible` de la `Insignia` "Stock negativo" en Stock por categoría → rojo `StockNegativo_…`.
5c. Quitar `Command="{Binding ExportarPdfCommand}"` del botón (dejarlo sin comando) → rojo `ExportarPdfBotonesGuardianTests` y `ExportarPdf_Deshabilitado…`.
5d. Quitar `HayDatos="…"` del marco en Valorización → rojo `MarcoReporteV2Tests.Vacio_…` o `Pie_…`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Views/Reportes/ValorizacionView.axaml src/StockApp.Presentation/Views/Reportes/StockCategoriaView.axaml tests/StockApp.Presentation.UiTests/V2/ReportesInventarioViewV2Tests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/ReportesComparacionTests.cs <tests heredados adaptados>
git commit -m "feat(ui-v2): Valorización de inventario y Stock por categoría"
```

### Task 6: Más movidos, Historial por producto, Log de auditoría y Estadística de tareas v2

**Decisiones aplicadas:** Q-ACCIONES (Buscar en la barra), Q-HISTORIAL-INICIAL, Q-CRITERIO, Q-USUARIO-AUDITORIA, D4, D5, D6, D7, D8, D9.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Reportes/MasMovidosView.axaml`, `HistorialPorProductoView.axaml`, `AuditoriaLogView.axaml`, `ReporteTareasView.axaml`
- Create: `tests/StockApp.Presentation.UiTests/V2/ReportesConFiltrosViewV2Tests.cs`
- Modify: `ReportesComparacionTests.cs` (+14: Historial con producto y sin producto, Más movidos con datos y con error de fechas, Auditoría, Tareas y Tareas con cierre, a los dos tamaños)
- Modify (convención 13, solo si fallan con motivo): `ReporteTareasCriterioCierreAvisoTests` (busca el `TextBlock` del aviso: pasa a `v2:Alerta` con el mismo texto), `ReportesStockOrdenTests` (L86), `DataGridCeldaNumericaOrdenTests` (L94), `ExportarPdfBotonesGuardianTests` (L183-340), `CargaProtegidaEstadoVacioUiTests`, `DataGridColumnasAutoAjusteTests` (L367, Auditoría), `GrillasSinRecorteBarridoTests`, `GuardianDePatronTests` (las cuatro vistas a `VistasMigradasAV2`; con esto las seis salen de la `InlineData` del `HeaderVista` L69-74 y de `VistasDeLaTanda` L114-119, reemplazadas por `Raiz_TieneUi2_BreadcrumbReportes_…` de las Tasks 5 y 6)

**Interfaces:**
- Consumes: `MarcoReporte` (Task 4); los cuatro VMs (Task 2): `CriteriosDisponibles`, `CriterioOpcionSeleccionada`, `EsCriterioCierre`, `Usuarios`, `UsuarioFiltroSeleccionado`, `BuscarProductosAsync`, `ProductoSeleccionado`, `TopN`, `FechaDesde`, `FechaHasta`, `MensajeError`, `TotalGeneral`.
- Produces: las cuatro vistas con raíz `Classes="ui2"`, `x:Name` `Marco`, `Grilla`, `BotonBuscar`, `BotonExportarPdf`, `PieConteo`; Historial `CampoProducto`; Auditoría `ComboUsuario`; Tareas `ComboAgrupador`, `ComboCriterio`, `AlertaCriterioCierre`, `PieTotalGeneral`.

- [ ] **Step 1: UiTests (rojo)**

```csharp
[AvaloniaTheory, InlineData(typeof(MasMovidosView)), InlineData(typeof(HistorialPorProductoView)),
                 InlineData(typeof(AuditoriaLogView)), InlineData(typeof(ReporteTareasView))]
public void Raiz_TieneUi2_BreadcrumbReportes_BuscarEnLaBarra_ExportarEnElEncabezado_SinPrimario(Type vista)
[AvaloniaFact] public void Filtros_EnElOrdenAprobado_ConEtiquetaArriba()           // una por vista, con los anchos medidos
[AvaloniaFact] public void FechasInvertidas_AlertaDeErrorDebajoDeLosFiltros_SinTocarLaGrilla()   // Más movidos
[AvaloniaFact] public void HistorialSinProducto_VacioElegiUnProducto_SinConsultar_PdfDeshabilitado()
[AvaloniaFact] public void HistorialConProducto_Buscar_MuestraLosMovimientos_YStockNuevoNegativoConInsignia()
[AvaloniaFact] public void Auditoria_ComboUsuarioConTodosPrimero()
[AvaloniaFact] public void Auditoria_SinConexionAlAbrir_EstadoEnLineaConReintentar_YReintentarCargaElCombo()   // Review Focus 5
[AvaloniaFact] public void Tareas_ElegirFechaDeCierre_MuestraElAviso_YVolverACreacionLoOculta()
[AvaloniaFact] public void Tareas_PieConTotalGeneral()
[AvaloniaFact] public void Tareas_SinRadioButtons()                                  // ningún RadioButton en el árbol
[AvaloniaFact] public void Buscar_EsSecundarioYEstaAlFinalDeLaBarra()
```

Run el filtro de la clase nueva: rojo pegado.

- [ ] **Step 2: Reescribir las cuatro vistas**

Igual que la Task 5, con `Filtros` = `StackPanel Orientation="Horizontal" Spacing="8"` de `CampoFormulario` (D4) y `Button x:Name="BotonBuscar" Classes="secundario" Content="Buscar" VerticalAlignment="Bottom"`; fechas con `CalendarDatePicker` (`SelectedDateFormat="Custom"`, `CustomDateFormatString="dd/MM/yyyy"`, `CalendarDatePickerFechaBehavior.NormalizarFechaTipeada` como hoy); `MensajeError="{Binding MensajeError}"` en el marco. Historial: el `AutoCompleteBox` de hoy (mismo `AsyncPopulator`, `FilterMode="None"`, `ValueMemberBinding`) dentro de `CampoFormulario`, sin el comentario de bugfix largo (el motivo queda en el VM). Auditoría: `ComboBox` sobre `Usuarios` con `ItemTemplate` de `OpcionUsuario.Nombre`. Tareas: los dos `ComboBox` (`AgrupadoresDisponibles`/`AgrupadorSeleccionado` y `CriteriosDisponibles`/`CriterioOpcionSeleccionada`) y la `v2:Alerta` Advertencia de D9 arriba de la grilla dentro de `Contenido`. Code-behind: sin cambios.

- [ ] **Step 3: Comparaciones y suites**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m
scripts/ui-v2/comparar.sh Reportes
```

Expected: verde; las 30 filas de la fase APROBADAS.

- [ ] **Step 4: Mutaciones (QUITANDO), rojo pegado**

6a. Quitar `IsVisible="{Binding EsCriterioCierre}"` de `AlertaCriterioCierre` → rojo `Tareas_ElegirFechaDeCierre_…`.
6b. Quitar `MensajeError="{Binding MensajeError}"` del marco en Más movidos → rojo `FechasInvertidas_…`.
6c. Quitar `SelectedItem="{Binding CriterioOpcionSeleccionada}"` del `ComboCriterio` → rojo `Tareas_ElegirFechaDeCierre_…`.
6d. Quitar `Classes.negativo` del número de STOCK NUEVO en Historial → rojo `HistorialConProducto_…`.
6e. Quitar `TituloVacio="{Binding TituloVacio}"` del marco en Historial → rojo `HistorialSinProducto_…`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Views/Reportes/MasMovidosView.axaml src/StockApp.Presentation/Views/Reportes/HistorialPorProductoView.axaml src/StockApp.Presentation/Views/Reportes/AuditoriaLogView.axaml src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml tests/StockApp.Presentation.UiTests/V2/ReportesConFiltrosViewV2Tests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Reportes/ReportesComparacionTests.cs <tests heredados adaptados>
git commit -m "feat(ui-v2): Más movidos, Historial por producto, Log de auditoría y Estadística de tareas"
```

---

## Bloque D — Guardianes, verificación orgánica, integración y cierre

### Task 7: Guardianes, suite completa y verificación orgánica (UN solo GATE 2)

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia completa para el GATE 2 y la aprobación de la fase en `feat/ui-v2-fase6`.

- [ ] **Step 1: Guardianes de la fase**

Confirmar con `grep` y la CLI `codegraph`, y anotar en el ledger:
1. Las 7 vistas (hub + 6) en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `Guardian_CubreTodasLasVistasDelEnsamblado` verde (incluye `MarcoReporte`: si el guardián lo trata como vista, va a `VistasMigradasAV2` con su `ui2` heredado; anotar cómo quedó).
2. `VistaV2_NoUsaPiezasDelTemaViejo`, `VistaV2_NoTieneOpacidadesLiterales` y `NingunaVista_TieneOverlaysDeModal` verdes.
3. `grep -n 'Opacity\|Classes="primary"\|Classes="secondary"\|Classes="caption"\|Classes="card"\|Classes="seccion"\|<c:\|HeaderVista\|MargenVista\|DangerBrush\|WarningBrush\|SignoNegativoBrushConverter\|BadgeEstado\|RadioButton\|Classes="primario"' src/StockApp.Presentation/Views/Reportes/*.axaml` vacío.
4. `git diff <BASE>..HEAD --stat -- src/StockApp.Presentation/Themes src/StockApp.Presentation/Controls src/StockApp.Presentation/Behaviors src/StockApp.Presentation/App.axaml docs/ui-v2/tokens.json docs/ui-v2/mockups/_base` VACÍO (tema compartido intacto).
5. `git diff <BASE>..HEAD --stat -- src/StockApp.Api src/StockApp.Domain src/StockApp.Application src/StockApp.Presentation/ViewModels/InicioViewModel.cs` VACÍO.
6. `grep -rn "NavValorizacion\|NavStockCategoria\|NavHistorialPorProducto\|NavMasMovidos\|NavAuditoriaLog\|NavReporteTareas" src/ tests/` vacío (Q-SIDEBAR = A).
7. `ColumnasPdfContraDtosRealesTests` y `ExportarPdfBotonesGuardianTests` verdes (PDF sin cambios de columnas ni nombres).

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger los rojos PEGADOS de 1a-1c, 2a-2h, 4a-4f, 5a-5d y 6a-6e. Una sin rojo pegado se corre ahora.

- [ ] **Step 3: Build, suite completa y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos (por nombre); total ≥ línea base + nuevos − reemplazados (anotar los reemplazos con su reemplazo); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS (las de fases anteriores sin cambios; si una se mueve, diagnosticarla). Commit solo si hubo ajustes: `test(ui-v2): guardianes de la Fase 6 al día`.

- [ ] **Step 4: Verificación orgánica (spec §6.4) — la ejecuta un AGENTE con la app real contra la base local, en WSLg a 1280×760 y 1366×768**

Preparación (en este orden):
1. `docker start stockapp-pg` (queda andando). **Backup ANTES de crear nada:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-6-$(date +%Y%m%d-%H%M%S).sql`; `ls -l` (no vacío); ruta al ledger.
2. API y app DEL WORKTREE DE LA FASE: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` y `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`. Si otra fase tiene su app abierta en WSLg, coordinar con el orquestador: una app a la vez (misma ventana "Gestión Municipal", mismo puerto de API).
3. Operadores de permisos MIXTOS por Administración de usuarios: `f6mixto` con `reportes.ver` + `movimientos.registrar` (sin `catalogo.productos`) y `f6historial` con `reportes.ver` + `movimientos.registrar` + `catalogo.productos`; anotar ids.
4. Datos: los que ya hay en la base local (incluidos los `PRUEBA-4D` conservados); si falta algo para un caso (un producto con stock negativo, movimientos en un período), crearlo por la app con prefijo `PRUEBA-6` y anotar ids.

Pasos (capturar cada pantalla, MIRAR cada captura, `md5sum` contra la anterior; 1, 2 y 5 se repiten a 1366×768):
1. **Sidebar y hub (Admin):** ANÁLISIS con un solo ítem "Reportes"; el hub con 6 tarjetas; comparar a ojo con `hub.html`.
2. **Cada tarjeta:** abre su reporte con "Cargando reporte…" y después los datos; el sidebar sigue en "Reportes"; el breadcrumb "Reportes" vuelve al hub. Desde Inicio, el acceso a Valorización marca "Reportes".
3. **Valorización y Stock por categoría:** insignia "Stock negativo" en la fila negativa, pie y total; Actualizar; ordenar por STOCK y por VALOR COSTO; Exportar PDF → verificar POR CONTENIDO con pypdf (`python3 -c "import pypdf,sys;r=pypdf.PdfReader(sys.argv[1]);print(r.pages[0].extract_text())" <archivo>`): título, "Sin filtros aplicados.", columnas y el total.
4. **Con filtros:** Más movidos con Desde > Hasta → alerta de error; un período válido → Buscar; cambiar Desde SIN Buscar y exportar → el PDF rotula el período CARGADO (pypdf). Historial: abre en "Elegí un producto" con PDF deshabilitado; buscar un producto (popup del autocompletar con `capturar-x11.py`), Buscar, PDF. Auditoría: combo Usuario con "Todos" primero (capturar el desplegable), filtrar por un usuario. Tareas: Contar por "Fecha de cierre" → aviso; volver → desaparece; PDF con "Total general".
5. **Operador `f6mixto`:** hub con 5 tarjetas (sin Historial, Review Focus 3); `f6historial` ve las 6. Desde la sesión de Admin quitarle `reportes.ver` a `f6historial` con Valorización abierta; en su sesión, Actualizar → un solo Aviso, "Sin permiso" en lugar de la card, PDF deshabilitado y el ítem "Reportes" fuera del sidebar (Review Focus 4).
6. **Sin servidor:** `kill` SOLO del PID de `StockApp.Api`; abrir Log de auditoría → estado en línea con Reintentar, sin modal global; levantar la API y Reintentar → combo y log cargados (Review Focus 5). Lo mismo con Actualizar en Valorización.
7. **Popups:** calendario de Desde (abre, elige día, cierra, sin foco atrapado), desplegables de Agrupar por y Contar por.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia, sin declarar la fase cerrada. **La limpieza de `PRUEBA-6`, `f6mixto` y `f6historial` la decide el usuario** (listar ids en el ledger y PREGUNTAR); nunca contra el VPS.

- [ ] **Step 5: GATE 2 — aprobación final del usuario (PARADA)**

PARAR. Presentar: `index.html` (las 30 filas de la fase y las 4 del sidebar), las capturas orgánicas junto a los mockups, los PDF verificados por contenido, los rojos de las mutaciones, los tests heredados adaptados y reemplazados, los `UmbralAprobado` si los hubo, las respuestas a las Q-*, los cambios respecto de los mockups y la pregunta de la limpieza. No seguir sin "aprobado" explícito. Registrar: `Fase 6: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

### Task 8: Integración en `feat/ui-v2`, documentación de cierre y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (las dos convenciones de la fase, con el siguiente número libre DESPUÉS del rebase)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, §6.5 si hubo compensaciones, deuda)
- Modify (local): el ledger

**Interfaces:**
- Consumes: la rama `feat/ui-v2-fase6` aprobada en el GATE 2.
- Produces: la Fase 6 en `feat/ui-v2` por fast-forward; sin push (el push y el merge a `main` los decide el orquestador según el esquema por bloques).

- [ ] **Step 1: Rebase sobre la punta actual de `feat/ui-v2`**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2-fase6 && git fetch . 2>/dev/null; git log --oneline -1 feat/ui-v2
git rebase feat/ui-v2
```

Si `feat/ui-v2` no se movió desde `BASE`, no hay nada que rebasear. Si hay conflictos, resolverlos con la tabla "Riesgos de conflicto": SIEMPRE conservar los dos lados (ANÁLISIS de esta fase, ADMINISTRACIÓN/Catálogos de la 7, Tareas/Documentos de la 5); nunca descartar un cambio de otra fase. Tras cada commit rebaseado con conflicto: `dotnet build StockApp.sln`. Prohibido `git rebase --skip`, `-i`, `reset --hard` y tocar el reflog. Si un conflicto no es mecánico (las dos fases cambiaron la misma regla), PARAR y consultar al orquestador.

- [ ] **Step 2: Suite y comparación tras el rebase**

```bash
dotnet build StockApp.sln && dotnet test StockApp.sln --blame-hang-timeout 5m && scripts/ui-v2/comparar.sh
```

Expected: verde salvo los falsos conocidos; TODAS las filas aprobadas, incluidas las de las otras fases y las 4 del sidebar con el sidebar combinado (si el mockup `sidebar-admin.html` quedó con los dos lados, regenerar sus PNG y verificar). Un rojo nacido del rebase se arregla en un commit propio con su rojo pegado.

- [ ] **Step 3: `herramientas.md` y spec**

`herramientas.md`: "Convenciones que agrega la Fase 6 (Reportes)" con las dos de este plan, numeradas a continuación de la última que haya en ese momento. Spec: (1) `**Estado:**` con "6" en la lista y "; Fase 6 (Reportes) APROBADA por el usuario el <fecha>"; la fila "6 Reportes" de §7. (2) "**Estado de la Fase 6 (aprobada el <fecha>).**" con el hub, el marco local, los seis reportes, las respuestas a las Q-* (en particular los desvíos de §4.2 por Q-PERMISO-HISTORIAL y de §8.1 por Q-SIDEBAR), comparado en 30 filas + 4 del sidebar, suite <total> en `<hash>`, la verificación orgánica resumida. (3) §6.5 con cada compensación nueva medida (si hubo). (4) "**Deuda abierta tras la Fase 6:**" con, como mínimo: los 67 mockups con el sidebar viejo (Q-SIDEBAR-MOCKUPS, Fase 9), lo que dejen las Q-* elegidas en B, cualquier `UmbralAprobado`, los minors diferidos del ledger, los datos de prueba según lo que decidió el usuario.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 6 (reportes)"
```

- [ ] **Step 4: Review final de la rama**

Review de `<BASE>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones verdes. Foco: los cinco puntos de "Review Focus"; que los VMs no cambiaron más allá de la Task 2 (y el shell más allá de la Task 1); que el tema compartido y el dominio no se tocaron (Task 7 Step 1.4 y 1.5); que los PDF siguen con sus columnas, títulos y nombres; que ningún test heredado se borró sin reemplazo nombrado; que `MarcoReporte` no se coló en `Controls/V2`. Hallazgos: commits propios con TDD o deuda con el usuario.

- [ ] **Step 5: Integrar por fast-forward**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && git status --short
git merge --ff-only feat/ui-v2-fase6
git log --oneline -3
```

Antes del merge, `git status` del worktree `ui-v2` limpio (salvo `.codegraph/` y `.superpowers/`) y SIN otra fase integrando en ese momento (coordinar con el orquestador). Si `--ff-only` falla porque `feat/ui-v2` avanzó, volver al Step 1. Expected: `feat/ui-v2` en la punta de `feat/ui-v2-fase6`. Sin push. El worktree `ui-v2-fase6` y la rama se borran solo cuando el orquestador lo diga.

---

## Fuera de alcance

- Cambios de dominio, DTOs, API, servicios o columnas de los PDF.
- Recarga automática de los filtros de los reportes (Q-RECARGA = A).
- Los accesos rápidos de Inicio a Valorización y Auditoría (no cambian).
- Actualizar el sidebar de los 67 mockups viejos (Q-SIDEBAR-MOCKUPS).
- Todo cambio en el tema compartido (`Themes/V2/`, `Controls/V2/`, `Behaviors/`, `App.axaml`, `tokens.json`, `_base/`): va a "Necesidades de tema compartido".
- Push y merge a `main`.
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| Conflicto de integración con la Fase 7 en el shell, el sidebar y sus tests. | Cambios confinados a ANÁLISIS; tabla "Riesgos de conflicto"; rebase con los dos lados y suite + comparación tras el rebase (Task 8). |
| Una vista nueva o el marco quedan fuera de los guardianes. | `VistasMigradasAV2` en las Tasks 4 a 6; `Guardian_CubreTodasLasVistasDelEnsamblado`; Task 7 Step 1. |
| El marco común esconde un estado mal combinado en una vista que no tiene mockup de ese estado. | `MarcoReporteV2Tests` con la View real y mutaciones 4d-4f; estados de Valorización comparados contra mockup; Q-MOCKUPS declara qué se compara. |
| Un test heredado se borra sin reemplazo. | Convención 13; Task 1 Step 1 y Task 7 Step 3 listan cada reemplazo. |
| Un gate se prueba con Admin y da falso verde. | Operadores mixtos en UiTests (Tasks 4 y 5) y en la verificación orgánica (`f6mixto`, `f6historial`). |
| El PDF cambia de forma sin querer. | Columnas y nombres sin tocar; `ColumnasPdfContraDtosRealesTests` y `ExportarPdfBotonesGuardianTests`; pypdf en el GATE 2. |
| Dos fases verificando en WSLg a la vez se pisan la ventana o el puerto de la API. | Una app a la vez, coordinada por el orquestador (Task 7 Step 4). |
| WSLg se congela o `capturar.sh` devuelve el escritorio de Windows con exit 0. | `md5sum` de capturas, mirar cada una, `capturar-x11.py` para popups; si se congela, PARAR y pedir el reinicio. |
| La verificación orgánica ensucia la base local. | Backup ANTES; prefijo `PRUEBA-6`; ids anotados; limpieza decidida por el usuario; nunca el VPS. |
