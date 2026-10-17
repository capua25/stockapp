# Fase 4d: Gastos y facturas (UI v2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 "Gastos y facturas" (`GastosView`, el listado) y `GastoFormView` (alta, edición y "desde entrada"), con el helper `RecargaConDebounce` que reemplaza el debounce triplicado, la validación de servidor que rechaza adjuntar a un gasto anulado (409) y las excepciones E1 a E9 de la spec, comparado contra mockups aprobados a 1280×760 y 1366×768.
**Architecture:** Cinco bloques, los de la spec ("Bloques del plan", L806). (A) Helper `RecargaConDebounce` con tests propios y migración de Productos, Control POA y Libro caja sin tocar sus tests (E1, U4, P1-A). (B) Primero el servidor (E9, P6: `AdjuntoService.AgregarAGastoAsync` rechaza un gasto anulado y la API responde 409) y después los ViewModels sin tocar las vistas: `GastosViewModel` (E2 carga protegida, E4 Estado en el cliente, E3 recarga con debounce, E8/P3 Anular) y `GastoFormViewModel` (E5 carga inicial, E7 errores por campo, E6 solo lectura y adjuntos, E8/P4 tilde a crédito). (C) Datos fijos, medición y 18 mockups (36 PNG) con GATE 1 antes de todo XAML. (D) El converter de tonos de Estado (P2) y las dos vistas v2 con sus UiTests (View real, operador de permisos mixtos) y sus comparaciones. (E) Guardianes, mutaciones, suite completa, GATE 2 orgánico, documentación de cierre y review final.
**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, CommunityToolkit.Mvvm, ASP.NET Core Minimal API + EF Core (`StockApp.Api.Tests` con `ApiFactory`), Moq 4.20.72, xUnit v2 (`StockApp.Application.Tests`, `StockApp.Api.Tests`, `StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, UglyToad.PdfPig (tests), pypdf (verificación orgánica).
**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, bloque "Diseño de la Fase 4d (4d: diseño aprobado por el usuario el 2026-10-15, …)" (L697-806 en `ce134ac`: decisiones U1 a U4 y P1 a P6; "Revisado en el código"; excepciones E1 a E9; sección 1, helper y listado; sección 2, formulario; sección 3, tests, verificación y deuda), §5.1 y §5.2 y, como contexto, los bloques de la 4a a la 4c. Convenciones 1 a 33: `docs/ui-v2/herramientas.md`. Molde de formato: `docs/superpowers/plans/2026-10-13-ui-v2-fase-4c-libro-caja.md`.

## Global Constraints

- Todos los comandos corren desde `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base: el commit de este plan), como `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>`.
- CodeGraph: este worktree tiene su PROPIO índice `.codegraph/`; para código C# usar primero la CLI `codegraph` dentro del worktree (`codegraph query|explore|node|callers|impact`), NUNCA el MCP `codegraph_explore` (responde con el índice del checkout principal); `grep`/Read como fallback, y Read directo para `.axaml`, markdown y HTML/CSS (CodeGraph no los indexa).
- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, peligro texto `#B91C1C`, texto primario `#1C1917`, texto secundario `#57534E`, texto terciario `#78716C`; fila de grilla 40, header 36, input y botón 36; espaciado múltiplo de 4; sidebar 240 (la vista mide 1040 a 1280 y 1126 a 1366); card de formulario de 760 (`V2AnchoFormularioMax`).
- `docs/ui-v2/tokens.json` es la fuente de `Tokens.axaml`: nunca editar `Tokens.axaml` a mano; toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- Formato es-UY (el cliente es un municipio uruguayo): importes con `MonedaConverter` / `FormatoEsUy.Moneda` ("$ 1.250.000,00"), parseo con `FormatoEsUy.TryParseDecimal`, fechas "dd/MM/yyyy" con `FechaCortaConverter`; nunca `CultureInfo.GetCultureInfo("es-UY")`.
- Sin cambios de dominio ni de DTOs. Única excepción declarada a "sin cambios de API": E9, una regla nueva en `AdjuntoService.AgregarAGastoAsync` que NO cambia firma, ruta ni DTOs (`POST /finanzas/gastos/{id}/adjuntos` igual). `GastoService` y el resto de `AdjuntoService` no cambian (`AgregarAPagoAsync` y `QuitarAsync` quedan como están: deuda de la 4e).
- VMs: cambian `GastosViewModel` (E2, E3, E4, E8) y `GastoFormViewModel` (E5, E6, E7, E8), y solo por eso; `ProductoListViewModel`, `ControlPoaViewModel` y `LibroCajaViewModel` cambian SOLO su debounce por el helper (E1), sin tocar sus cargas (P1-A). Ningún otro VM, servicio, endpoint, `ViewLocator` ni `INavigationService`. `PagosGastoView` / `PagosGastoViewModel` no se tocan (U1: Pagos es la 4e).
- Los tests de los tres VMs migrados (`ProductoViewModelTests`, `ControlPoaViewModelTests`, `LibroCajaViewModelTests`) y de sus vistas (`ControlPoaViewV2Tests`, `LibroCajaViewV2Tests`, `ProductoListViewV2Tests`) NO se tocan en toda la fase: son los guardianes de que la migración no cambió nada. Si alguno necesita cambiar para compilar o pasar, PARAR y reportar (decisión U4).
- `x:DataType` / compiled bindings en las vistas y en cada `DataTemplate` (`vm:GastoFila`, `e:Proveedor`, `e:FuenteFinanciamiento`, `e:RubroGasto`, `e:LineaPoa`); constantes con `{x:Static}`.
- Sin comentarios con `--` en `.axaml` ni `.csproj` (AVLN1001, cascadea a MSB4025).
- Un solo botón primario por vista: "Nuevo gasto" en el listado, "Guardar" en el formulario; todo lo demás `secundario`.
- Prohibidos en las vistas v2: `Opacity`, `ActivoOpacidadConverter`, `SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:` del tema viejo, `Classes="primary|secondary|caption|seccion|card"`, `MargenVista`, `DangerBrush`.
- Gates de UI con la View real en `Presentation.UiTests` (un test de VM no custodia un `IsVisible` del XAML); gates de permisos con un Operador de permisos MIXTOS (`new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas)`, más `RegistrarGastos` o `RegistrarPagos` según el caso), NUNCA con Admin (cortocircuita). Revocación en caliente por el camino real (`ManejadorAccesoRevocado.ManejarAsync` → `ShellMainViewModel.NotificarPermisosActualizados`) con la View real dentro de `ShellMainView`.
- `ArbolVisual.EsVisibleEnArbol` falla fuerte con null desde `89202a4`: nada de `x is not null && EsVisibleEnArbol(x)`; buscar el control por `x:Name` y dejar que un nombre que falta dé rojo.
- TDD en cada task: test primero, correrlo y PEGAR el rojo real en el ledger, código mínimo, verde. Un test que nace verde (caracterización) se declara así en el ledger y su custodia es una mutación con rojo pegado.
- Mutaciones QUITANDO, nunca agregando: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-4d && mkdir -p "$SCRATCH"`; `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (líneas o fragmentos, nunca se agrega ni se reemplaza por otra cosa); `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas o acortadas; se corre el test y se PEGA el rojo; se restaura con `cp "$SCRATCH/<nombre>.bak" <archivo>`; `git diff --no-index` vacío; verde. NUNCA `git checkout --` ni `git restore`.
- Stubs fieles al contrato real: `IGastoService.ListarAsync(GastoFiltro)` → `Task<IReadOnlyList<Gasto>>` (con `Proveedor`, `FuenteFinanciamiento`, `RubroGasto`, `LineaPoa` y `Pagos` cargados, como `GastoRepository`); los combos `ListarActivasAsync()` / `ListarActivosAsync()` → `Task<IReadOnlyList<T>>`; los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` (401/403), `ServidorNoDisponibleException` (sin servidor), `ReglaDeNegocioException` con el `detail` del problema (409), `EntidadNoEncontradaException` (404).
- Mockups (§6.1): `docs/ui-v2/mockups/finanzas/gasto*.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosGastos`, celdas numéricas con `v2-num` y el valor en un `<span>`; se aprueban ANTES del XAML (nada de XAML de las dos vistas antes del GATE 1, salvo quitar el botón Filtrar de la v1 en la Task 6, forzado por el build).
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños; uno mayor solo con `UmbralAprobado(…, AprobadoPor: "Martin", …)` tras revisar cada tesela densa con el usuario en un GATE (16 o más exige además `MutacionPalidaEnRojo`); nunca para tapar un defecto.
- Convención 6: comparación DENTRO de `ShellMainView` (`DatosShell.Crear(usuario, permisos, "Gastos", vm)`), sidebar oculto en los dos renders (`ProductosComparacionTests.OcultarSidebar`, `ocultarEnMockup: [".v2-sidebar"]`); los dos modales con `ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje))`.
- Build obligatorio tras cada cambio de `.axaml` o de un `.cs` de `src/`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Tests de a UN `.csproj` por comando y siempre con `--blame-hang-timeout 5m`; nunca dos `dotnet test` a la vez (comparten `obj/`).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos (confirmar por nombre); `ApiStartupMigrationTests.Arranque_SinMigracionExterna_MigraSolaYAtiendeRequests` es intermitente conocido; cualquier otro rojo es real. Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- Línea base: suite 5897/5897 en `cae75df` y `StockApp.Presentation.UiTests` 1213/1213 en `89202a4` (cierre de la 4c); se re-mide en la Task 1 Step 1 y cada task anota el total tras su commit; solo baja por tests heredados REEMPLAZADOS (convención 13), listados con su reemplazo.
- Sin referencias al plan en el código (nada de "Task 6", "D9", "E5", "P3", "U2" en `.cs`/`.axaml`/`.css`/`.html`).
- Las referencias de línea (`L123`) son las del commit de este plan; después de que una task edita un archivo, ubicar el lugar por el texto citado.
- Verificación GUI: ventana "Gestión Municipal" (`pgrep -x GestionMunicipa`), toolkit `scripts/gui-verificacion/`; `capturar.sh` NO es fail-closed (con WSLg caído captura el escritorio de Windows con exit 0): MIRAR cada captura y comparar `md5sum` con la anterior; los popups (calendario, combos) se capturan con `scripts/gui-verificacion/capturar-x11.py`; la rueda del mouse no se simula; matar procesos por PID, nunca `pkill -f`.
- Commits: conventional commits en español, uno por task, SIN `Co-Authored-By` ni línea de atribución de IA; sin push, merge, rebase, amend, `git reflog expire`, `git gc` ni `git prune`.
- GATE 1 (Task 12) y GATE 2 (Task 17) son paradas para el usuario: no se sigue sin un "aprobado" explícito. P7 y P8 (abajo) se le preguntan al usuario ANTES de la Task 4.

## Review Focus

1. **Un filtro cambiado mientras corre la carga inicial.** Caso: Gastos abre con muchas facturas, el listado tarda y el usuario ya eligió "Desde"; la respuesta de la carga inicial (sin filtro) llega después y la grilla muestra todo con "Desde" puesto en pantalla, o el cambio se pierde porque la recarga todavía no estaba armada. Esperado: lo que se ve corresponde a los filtros de pantalla. Test: `FiltroCambiadoDuranteLaCargaInicial_LaRespuestaInicialNoPisaLaDelFiltro` (Task 6; depende de P8-A).
2. **Los combos fallan al abrir y Reintentar no los recupera.** Caso: el servidor no responde al abrir Gastos; aparece "No se pudo conectar" con Reintentar; al reintentar solo se repite el listado y los combos de Proveedor, Fuente, Rubro y Línea POA quedan vacíos para siempre (no se puede filtrar). Esperado: Reintentar carga los combos que faltan y el listado. Test: `SinConexionAlCargarLosCombos_Reintentar_CargaLosCombosYElListado` (Task 4).
3. **El PDF rotula filtros que no son los del resultado.** Caso: el usuario elige un proveedor y exporta antes de que venza la espera de 300 ms; el PDF dice "Proveedor: X" con las facturas de todos los proveedores. Esperado: la descripción sale de la consulta CARGADA más el Estado vigente (convención 32). Test: `ExportarPdf_ConFiltroCambiadoSinRecargar_RotulaLoCargado` (Task 5).
4. **El filtro de Estado esconde la fila seleccionada y Editar o Anular actúan sobre una fila que no se ve.** Caso: con "Pendiente" seleccionada, el usuario pasa Estado a "Pagada"; la fila desaparece pero queda seleccionada y Anular la anula. Esperado: la selección se limpia y Editar, Pagos y Anular quedan deshabilitados. Test: `Estado_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccion` (Task 5).
5. **El vencimiento de un gasto que dejó de ser a crédito bloquea Guardar.** Caso: el usuario tilda "A crédito", no completa el vencimiento (Guardar deshabilitado, error debajo del campo), destilda; el campo se oculta pero la regla sigue y Guardar no se habilita sin que se vea por qué. Esperado: a contado el vencimiento no es regla, su error desaparece y Guardar se habilita. Test: `DestildarCredito_SinVencimiento_GuardarSeHabilita` (Task 9).

---

## Decisiones previas a la ejecución (contradicciones spec/código, a confirmar por el usuario ANTES de la Task 4)

La spec pide cosas que el código, tal como queda, resuelve de una forma que la spec no dice. El plan está redactado con la opción A de cada una; el orquestador las pregunta al usuario antes de arrancar la Task 4. Si el usuario elige B, se aplica el cambio indicado (con TDD) y se anota en el ledger.

| # | Contradicción (evidencia) | A (propuesta, la que implementa el plan) | B (alternativa) y qué cambia |
|---|---|---|---|
| P7 | **Revocación de `finanzas.ver` en caliente y el vacío que miente.** E2 dice "La revocación en caliente se conserva tal cual (`NotificarPermisosActualizados` vacía `Filas` sin `VerFinanzas` …)". Con la vista v2 (sección 1, estados de pantalla), una grilla vaciada con `SinPermiso` en false muestra el vacío "Todavía no hay gastos" (y, con `finanzas.gastos`, el botón "Nuevo gasto"): afirma algo falso. Pasa cuando el 403 que refresca los permisos no viene del listado (p. ej. Anular tras perder `finanzas.gastos` y `finanzas.ver` a la vez): `GastosViewModel.cs` L259-265 solo hace `Filas.Clear()`; `ViewModelBase.MarcarSinPermiso` (L216-220) es `protected` y está disponible. | Además de vaciar, `NotificarPermisosActualizados` llama `MarcarSinPermiso(MensajeSinPermisoGastos)` sin `VerFinanzas`: la pantalla muestra "Sin permiso" en lugar de la card, igual que tras un 403 del listado (convención 18). Test `NotificarPermisosActualizados_TrasPerderVerFinanzas_MarcaSinPermiso` (Task 4) y `RevocacionDeVerFinanzas_DentroDelShell_SinPermisoEnLugarDeLaCard` (Task 14). | Tal cual la spec: solo `Filas.Clear()`; tras esa revocación se ve "Todavía no hay gastos" hasta la próxima consulta (que da 403 y pasa a "Sin permiso"). Se quitan la línea `MarcarSinPermiso(...)` y esos dos tests, y va a la deuda. |
| P8 | **Cuándo se arma la recarga automática.** E3: "La recarga automática se arma recién cuando termina la carga inicial: la preselección de `FiltrarPorLineaPoa` y el remapeo de `LineaPoaSeleccionada` dentro de `CargarAsync` (`GastosViewModel.cs:226-230`) no programan una segunda consulta". Armarla recién al TERMINAR hace que un filtro cambiado mientras espera el listado de la carga inicial (la carga de combos es rápida, el listado puede tardar) no programe nada: el resultado de la carga inicial queda en pantalla con otro filtro a la vista (Review Focus 1). | Se arma después de cargar y remapear los combos y ANTES de pedir el listado inicial. La preselección y el remapeo siguen sin programar consulta (que es lo que E3 protege); un cambio durante la espera del listado programa una recarga con turno nuevo y la respuesta inicial se descarta (convención 23). Tests `FiltrarPorLineaPoa_YLaCargaInicial_ConsultanUnaSolaVez` y `FiltroCambiadoDuranteLaCargaInicial_…` (Task 6). | Literal: `_recargaArmada = true` al final de `CargarAsync`. El test de Review Focus 1 se reemplaza por `FiltroCambiadoDuranteLaCargaInicial_NoRecarga` (documenta que el resultado inicial queda con el filtro nuevo a la vista hasta el próximo cambio) y va a la deuda. |

## Decisiones del usuario (2026-10-15, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | Pagos fuera de la 4d (4e). El botón Pagos sigue navegando a `PagosGastoView` v1. | `PagosCommand` sin cambios; `PagosGastoView`/`PagosGastoViewModel` intactos; "Fuera de alcance". | 14 |
| U2 | El adjunto de un gasto anulado se OCULTA en la UI, sin darlo de baja y sin cambio de dominio. | `EsAnulado` y `MostrarSeccionAdjuntos` en el VM (Task 10); sección Adjuntos oculta en la vista (Task 15). | 10, 15 |
| U3 | Filtros con recarga automática por debounce; sale el botón Filtrar; Limpiar queda. | `FiltrarCommand` se borra; `LimpiarFiltrosCommand` repone sin consultar (Task 6); vista sin "Filtrar" (Task 14). | 6, 14 |
| U4 | Debounce extraído a un helper; se migran Productos, Control POA y Libro caja; Gastos nace usándolo. | `RecargaConDebounce` (Task 1), migración sin tocar sus tests (Task 2), Gastos (Task 6). | 1, 2, 6 |
| P1 = A | El helper solo hace el debounce; el turno de `ConsultaVigente` sigue en el método de carga de cada VM. Integración probada en los tests del helper con un `ConsultaVigente` real. | `ConLaConsultaVigente_LaPrimeraRecargaQueTerminaTarde_NoPisaLaSegunda` (Task 1); las cargas de los tres VMs no cambian (Task 2). | 1, 2 |
| P2 = A | Tonos de la `Insignia` de Estado: Vencida peligro, Parcial advertencia, Pagada éxito, Pendiente y Anulada neutras; la fila anulada además en token de inactivo (convención 11). | `VarianteEstadoGastoConverter` (Task 13); `Classes.inactivo` en cada celda (Task 14). | 13, 14 |
| P3 = A | Anular deshabilitado sobre una fila ya anulada (E8); Editar sigue habilitado y abre en solo lectura. | `PuedeAnular` (Task 7); UiTest con click real (Task 14). | 7, 14 |
| P4 = A | En edición el tilde "A crédito" queda deshabilitado con el texto "La condición de pago no se cambia: anulá el gasto y cargalo de nuevo" (E8). El servidor sigue rechazando (`GastoService.cs:93-95`). | `PuedeCambiarCondicionPago`, `TextoCondicionPagoFija` (Task 10); vista (Task 15). | 10, 15 |
| P5 = A | Pie del formulario fijo fuera del scroll, con la `Alerta` del error general (convención 27). | `Grid RowDefinitions="*,Auto,Auto"` (Task 15); `Pie_FueraDelScroll_…` (Task 15). | 15 |
| P6 = A | Validación en el servidor: `AdjuntoService.AgregarAGastoAsync` rechaza un gasto anulado (E9). | Task 3 (servicio, endpoint 409, panel). | 3 |
| — | Ejecución con subagent-driven-development, sin preguntar el método (memoria "Planes siempre con subagentes"). | Header del plan. | — |

## Decisiones que se confirman al aprobar los mockups (Task 12, GATE 1)

Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE 1 (criterio de la 3a a la 4c). El plan está escrito con estos valores; si el usuario cambia uno, se aplica en los mockups, en los tests de VM que lo fijan (con su rojo y su verde) y en las Tasks 14 y 15.

- **D1. Encabezado del listado:** `EncabezadoPagina` sin breadcrumb (pantalla del sidebar), `Titulo="Gastos y facturas"`, `Subtitulo="Facturas y compromisos de pago de la caja municipal."`. Acciones de izquierda a derecha: `Insignia` "Solo lectura" (neutra, solo sin `finanzas.gastos`), "Editar", "Pagos", "Anular", "Exportar PDF" (los cuatro `secundario`) y "Nuevo gasto" (`primario`, el último), como Ingresos (4b).
- **D2. Filtros:** `BarraFiltros MostrarBusqueda="False"` con dos filas de `CampoFormulario` (los siete no entran en una a 1040): fila 1 Desde (`CalendarDatePicker` 140) · Hasta (140) · Proveedor (`ComboBox` 220, "Todos") · Fuente (200, "Todas"); fila 2, a 12 de la primera, Rubro (228, "Todos"; DECIDIDO por el usuario el 2026-10-16: con 200 "Materiales de construcción" se cortaba, 167 en 142) · Línea POA (220, "Todas") · Estado (140, con "Todos") · "Limpiar" (`secundario`, alineado abajo). Los anchos son los medidos en la Task 12 y aprobados en el GATE 1 (filas de 724 y 693 en el interior de 950 / 1036; el 665 de la medición era con Rubro 200). Alternativa: un `WrapPanel` que reparte solo (pierde el orden fijo por fila).
- **D3. Grilla (DECIDIDO por el usuario el 2026-10-16):** 11 columnas en un orden NUEVO que reemplaza "el orden de la v1" (FECHA · PROVEEDOR · FACTURA · DETALLE · FUENTE · RUBRO · LÍNEA POA · MONTO · PAGADO · SALDO · ESTADO), con los montos y el estado a la vista: FECHA (`ColumnaAuto`, `NoCede`, `FechaCortaConverter`) · PROVEEDOR · DETALLE (`*`, `MinWidth` 200, elipsis + tooltip si se corta) · MONTO · PAGADO · SALDO (`ColumnaAuto`, `num`, `MonedaConverter`) · ESTADO (ancho fijo 90 = insignia "Pendiente" medida 74 + 16, conjunto cerrado, convención 14; DECIDIDO por el usuario el 2026-10-16, reemplaza el 96 anterior) · FACTURA (`ColumnaAuto` + `TooltipSiSeCorta`) · FUENTE · RUBRO · LÍNEA POA (`ColumnaAuto` + `TooltipSiSeCorta`). Orden por click en las once. Con DETALLE en su piso no entran, scroll horizontal (precedente: Historial, 3c-2): hasta ESTADO suman 866 con los anchos medidos (ESTADO 90), así que entran en la vista inicial a 1280 (interior 948) y a 1366 (interior 1034); a 1280 se corta FACTURA y a 1366 se corta FUENTE. Una FACTURA o LÍNEA POA vacía queda vacía (paridad v1).
- **D4. Fila anulada:** todas sus celdas de texto en terciario por token (`Classes.inactivo`), y en ESTADO la `Insignia` "Anulada" neutra (P2). Sin `Opacity`.
- **D5. Estados del listado** (dentro de la card, con los filtros a mano): "Cargando gastos…" (`EstadoCargando`); sin servidor, `EstadoVacio` de error con los textos de la convención 18 y "Reintentar"; tras un 403, `EstadoVacio` "Sin permiso" / "No tenés permiso para ver los gastos." EN LUGAR de la card; vacío sin datos "Todavía no hay gastos" / "Cuando se registre una factura o un compromiso, va a aparecer acá." con "Nuevo gasto" `secundario` (solo con `finanzas.gastos`); sin resultados "Ningún gasto coincide con los filtros" / "Probá con otro período o tocá Limpiar.".
- **D6. Pie:** "N gastos" (`ConteoConverter`, `'gasto|gastos'`) sobre `CantidadVisible`, oculto sin filas visibles o sin conexión.
- **D7. Formulario:** breadcrumb "Gastos y facturas / {Titulo}" (separador " / ", como en la 4b y en los mockups; decidido el 2026-10-16); card de 760 a la izquierda (convención 12); cuatro secciones con `subtitulo-formulario` y separador: **Factura** (Proveedor · N.º de factura; Orden de compra · Fecha), **Detalle** (Detalle y, debajo, Destino, los dos a todo el ancho), **Importe e imputación** (Monto · Fuente; Rubro · Línea POA), **Condición de pago** ("A crédito" · Fecha de vencimiento; debajo, el texto de P4 en edición). Requeridos con asterisco: Proveedor, Fecha, Detalle, Monto, Fuente, Rubro y Vencimiento. **Encabezado limitado a la columna de 760** (DECIDIDO por el usuario el 2026-10-16): breadcrumb, título y las insignias "Anulado" y "Solo lectura" quedan dentro de la misma columna `*` con `MaxWidth` `V2AnchoFormularioMax` que la card y el pie (D11), así que las insignias terminan en el borde derecho de la card (x 788 de la vista), no en el de la ventana. Mismo mecanismo que el pie: un `Grid` de una columna `*` con `MaxWidth` y `UseLayoutRounding="False"` (`ColumnaEncabezado`).
- **D8. Textos de error por campo** (constantes del VM, Task 9): "Elegí el proveedor.", "La fecha del gasto es obligatoria.", "El detalle es obligatorio.", "El monto total es obligatorio.", monto inválido con `MensajeNumeroInvalido.Armar("El monto total no es un número válido.", …)` (el de hoy, p. ej. "… Usá coma para los decimales: 5,4"), "Elegí la fuente de financiamiento.", "Elegí el rubro.", "Un gasto a crédito exige fecha de vencimiento." (el de hoy).
- **D9. Condición de pago:** el tilde dice "A crédito" (la v1 decía "Compra a crédito"); el texto de P4 en `texto-secundario` debajo de la fila, sin punto final (literal de la spec).
- **D10. Adjuntos:** sin `subtitulo-formulario` propio (el panel v2 trae su título "Adjuntos" en `titulo-seccion`): separador y el panel. En alta y "desde entrada", el mismo título "Adjuntos" y "Guardá el gasto para poder adjuntar." en `texto-secundario`. Con el gasto anulado, la sección entera (separador incluido) se oculta.
- **D11. Pie fijo:** `Border` a todo el ancho, fondo superficie y borde superior, padding 28,16; Cancelar (`secundario`) y Guardar (`primario`) alineados al borde derecho de la CARD (columna de 760), no al de la ventana; la `Alerta` del error general arriba del pie con el ancho de la card. DECIDIDO por el usuario el 2026-10-16 (no queda como pregunta abierta; descartada la alternativa de los botones al borde de la vista).
- **D12. Solo lectura y anulado:** `Insignia` "Solo lectura" (neutra) en el lugar de las acciones del encabezado, campos deshabilitados con los tokens de deshabilitado, Guardar oculto, Cancelar sigue; con el gasto anulado además `Insignia` "Anulado" (neutra) y una `Alerta` Info arriba de la card: "Este gasto está anulado: se puede consultar, pero no modificar." (el texto que la spec deja para el GATE 1).
- **D13. Carga inicial del formulario:** sin servidor, dentro de la card y arriba de las secciones, `Alerta` Error "No se pudo conectar con el servidor. Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar." y "Reintentar" `secundario` (patrón de `MovimientoFormControl`, convención 22); tras un 403, `EstadoVacio` "Sin permiso" / "No tenés permiso para ver finanzas, así que no se puede abrir el gasto." en lugar de la card, y en el pie solo Cancelar. Sin mockup propio para estos dos estados (precedente D16 de la 4b): los custodian UiTests. DECIDIDO por el usuario el 2026-10-16: sin mockups extra, cubierto por UiTests (descartada la alternativa de 4 PNG más).
- **D14. Doble click en una fila = Editar**, con el mismo gate (NUEVO respecto de la v1; precedente Ingresos 4b). Sobre el encabezado no edita.
- **D15. Datos fijos** (Task 11): `DatosGastos` con 6 gastos de setiembre y octubre de 2026 (Pendiente, Parcial, dos Pagada, Vencida y otra Pagada) más uno anulado para `gastos-con-anulado`; vencimientos lejos de "hoy" (2030 para los que no vencen, 30/09/2026 para el vencido) porque el estado se calcula con el reloj real (`GastoFila` usa `DateTime.UtcNow`, sin reloj inyectable: no se cambia el VM por esto).
- **D16. Mockups** (18, 36 PNG): listado `gastos`, `gastos-cargando`, `gastos-sin-conexion`, `gastos-sin-permiso`, `gastos-vacio`, `gastos-sin-resultados`, `gastos-solo-lectura`, `gastos-con-anulado` (la fila anulada seleccionada y Anular deshabilitado); formulario `gasto-nuevo`, `gasto-editar` (contado con un adjunto, tilde deshabilitado con su texto, SCROLLEADO al final), `gasto-editar-credito` (SCROLLEADO al final), `gasto-error-campo`, `gasto-error-servidor`, `gasto-credito`, `gasto-solo-lectura`, `gasto-anulado`; modales `gasto-confirmar-anular` y `gasto-factura-duplicada` (el `ConfirmacionDialog` con el texto que arma el VM). No se mockean tooltips ni desplegables.
- **D17. Selección tras recargar:** una recarga (cambio de filtro, Anular) reemplaza las filas y la grilla suelta la selección (Editar, Pagos y Anular vuelven a deshabilitarse); no se intenta re-seleccionar la misma factura.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell), 10 (`EncabezadoPagina` + card + pie de conteo con `ConteoConverter`), 11 (fila anulada por token, nunca `Opacity`), 12 (formulario v2: breadcrumb, card de 760, secciones, sin permiso en lugar de la card, deshabilitado por tokens), 13 (tests heredados se adaptan sin borrarse), 14 y 19 (columnas medidas en headless antes del mockup, `ColumnaAuto` con el conversor de la celda, `NoCede` en FECHA, ESTADO de ancho fijo por ser conjunto cerrado), 15 (vencimiento condicional sin hueco: el 16 va en el `Margin` superior), 18 (`Cargando`, `SinPermiso`, `SinConexion` con Reintentar que relee los filtros de pantalla), 20 (tooltip solo si se corta), 21 (permisos con operador mixto y `ShellMainView`), 22 (carga inicial del formulario con `Alerta` y Reintentar), 23 (`ConsultaVigente`), 25 (los modales son `ConfirmacionDialog` servido por `IConfirmacionService`), 27 (pie fijo de formulario largo), 29 (el formulario reenvía `NotificarPermisosActualizados` al panel de adjuntos, ya lo hace), 30 (Estado filtra en el cliente con el predicado del VM; el pie cuenta con el mismo; la selección oculta se limpia), 31 (errores por campo con `INotifyDataErrorInfo` y estado "tocado"), 32 (lo que se exporta y cómo se rotula sale de lo cargado).
- NO aplican: 16 (no es formulario de sidebar), 17 (los dos modales usan "Cancelar" / "Sí, continuar"), 24 (sin diálogo con búsqueda), 26 (sin fila de carga), 28 (ninguna columna se oculta por permiso), 33 (no hay números negativos fuera de la grilla: SALDO no puede quedar negativo, `GastoService.cs:98-100`).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 17)

34. **Recarga con debounce (`RecargaConDebounce`):** todo filtro o campo que recarga solo usa el helper (`ViewModels/RecargaConDebounce.cs`): `Programar(recarga)` cancela y libera la espera pendiente y programa otra; la espera sale de un `Func<TimeSpan>` leído en cada `Programar`. El VM conserva `internal TimeSpan EsperaDebounce` (300 ms por defecto, los tests la bajan) e `internal Task _tareaDebounce` (lo que devuelve `Programar`, para esperarlo en los tests), y crea el helper perezoso: `private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);`. El helper solo hace el debounce: el turno de `ConsultaVigente` lo pide el método de carga del VM, que comparten los caminos con y sin debounce (carga inicial, Reintentar, Actualizar, Buscar). Primeros casos: Productos, Control POA, Libro caja y Gastos.
35. **Armado de la recarga automática:** los filtros que se ponen antes de la carga inicial (preselección por navegación, `FiltrarPorLineaPoa`) o durante el remapeo de los combos no programan consulta; la recarga se arma después de cargar los combos y antes de pedir el listado inicial, así un cambio durante la espera de ese listado gana por turno (según P8). "Limpiar" repone los filtros y no consulta por su cuenta: los cambios se juntan en una sola recarga.
36. **Formulario de solo lectura por permiso o por estado del registro:** `SoloLectura` (sin el permiso de escritura o con el registro anulado) es un getter sobre la sesión más un flag del registro, así la revocación en caliente lo re-evalúa; la vista deshabilita un contenedor con los campos (no la card: el panel de adjuntos queda afuera y su "Ver" sigue), oculta Guardar y muestra la `Insignia` "Solo lectura" (y "Anulado" con el registro anulado). Primer caso: el formulario de gasto.

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| E1: los tres debounce | Mecánica idéntica: 300 ms, `CancellationTokenSource` que se cancela, se libera y se reemplaza, `Task.Delay(EsperaDebounce, ct)` con `OperationCanceledException` tragada y doble chequeo de `IsCancellationRequested`. El helper copia esa mecánica; el VM cambia el cuerpo del disparador por `_tareaDebounce = Recarga.Programar(<carga>)` y borra `_debounceCts` y el método `Debounce…Async`. La espera se lee al programar, como hoy (el `Task.Delay` corre sincrónico hasta su primer `await`). | `ProductoListViewModel.cs` L25-52, L119-148; `ControlPoaViewModel.cs` L70-78, L112-135; `LibroCajaViewModel.cs` L112-156. |
| E1: `EsperaDebounce` en inicializador | `ProductoViewModelTests` L612 la asigna en un inicializador de objeto: se conserva `internal TimeSpan EsperaDebounce { get; set; }`. `LibroCajaViewModelTests` L158 asigna `_tareaDebounce`: se conserva el campo `internal Task _tareaDebounce`. El helper es perezoso (`field ??=`) porque un inicializador de campo no puede capturar `this` (CS0236). | grep `EsperaDebounce\|_tareaDebounce` en `tests/`. |
| E2: carga protegida | `CargarAsync` (combos + listado) y `RecargarAsync` (solo listado) van por `EjecutarCargaProtegidaAsync(cargar, MensajeSinPermisoGastos, turno)`; `AvisaSinConexionEnLinea => true`; `AlPerderConexion => Filas.Clear()`; `ReintentarAsync => CargarAsync()` (relee los filtros de pantalla; los combos se piden solo si no se cargaron, `_combosCargados`). Tras un 403 de la consulta vigente `Filas` se vacía (conserva la deuda 3c-2 cerrada en `137b7bf`: nada viejo exportable). El `catch (ReglaDeNegocioException or EntidadNoEncontradaException)` con `InformarAsync` de hoy se conserva alrededor. | `GastosViewModel.cs` L193-252, L283-308; `ViewModelBase.cs` L254-305; `IngresosViewModel.cs` L181-200. |
| E3: debounce de Gastos | `partial void On<Filtro>Changed` de los seis filtros de servidor → `ProgramarRecarga()`, que solo programa con `_recargaArmada`. `FiltrarCommand` se borra (los tres tests que lo usan se adaptan, convención 13). `LimpiarFiltrosCommand` pasa a sincrónico y no consulta. | `GastosViewModel.cs` L85-91, L281, L310-321; spec E3. |
| E4: Estado en el cliente | `FilasView.Filter = EsVisible`; `Refresh()` y `CantidadVisible` al cambiar Estado o las filas; selección oculta → null; `HayFiltrosAplicados` (filtros de la consulta cargada o Estado distinto de "Todos") elige el vacío. La consulta guarda su descripción al empezar (`ConsultaGastos`) y la que se aplica queda en `_consultaCargada`: el PDF rotula con eso más el Estado vigente y exporta solo las visibles. `PuedeExportar => PuedeVerFinanzas && !SinPermiso && !Cargando`, re-notificado en `OnPropertyChanged` como en Control POA. | `IngresosViewModel.cs` L125-162; `ControlPoaViewModel.cs` L139-153; `GastosViewModel.cs` L462-519. |
| E5: carga del formulario | `InicializarAsync` por `EjecutarCargaProtegidaAsync(…, MensajeSinPermisoFormulario)` con `AvisaSinConexionEnLinea => true`: servidor caído → `SinConexion` + `ReintentarCargaCommand` de la base (repite la carga entera); 403 → `SinPermiso`. Se borra el `catch (UnauthorizedAccessException)` silencioso. | `GastoFormViewModel.cs` L168-228; `IngresoFormViewModel.cs` L170-190. |
| E6: solo lectura | `EsAnulado` (observable, fijado en `CargarParaEditar` con `!gasto.Activo`), `SoloLectura => !PuedeRegistrarGastos \|\| EsAnulado`, `MostrarSeccionAdjuntos => !EsAnulado`. El panel se sigue inicializando también con el gasto anulado (sin cambio de lógica; solo se oculta). | `GastoFormViewModel.cs` L60-62, L138-155; spec "Revisado en el código". |
| E7: errores por campo | Patrón de `IngresoFormViewModel` (4b): `CamposValidados`, `_tocados`, `ErrorDe`, `GetErrors`, `MarcarTocado`, `ErrorsChanged` con el nombre de la propiedad bindeada; `PuedeGuardar => !SinPermiso && todas las reglas`. Reglas = las de hoy (`PuedeGuardar` L237-242 y los tres chequeos de `GuardarAsync` L250-265); el vencimiento solo a crédito. "Monto > 0" sigue en el servidor. | `IngresoFormViewModel.cs` L59-119, L192-205. |
| E8: Anular y tilde | `PuedeAnular() => FilaSeleccionada is { Gasto.Activo: true }`; `PuedeCambiarCondicionPago => !EsEdicion` y `TextoCondicionPagoFija`. | `GastosViewModel.cs` L327-348; `GastoService.cs` L91-95, L171. |
| E9: servidor | `if (!gasto.Activo) throw new ReglaDeNegocioException("No se puede adjuntar a un gasto anulado.");` entre la resolución del gasto (L35-36) y `AdjuntoValidador.Validar` (L38). 409 por `DomainExceptionHandler.cs` L31 con el mensaje en `detail`; el cliente lo muestra por `AdjuntosPanelViewModel.AgregarAsync` (`catch` → `InformarAsync`, L111-114) sin código nuevo. | `AdjuntoService.cs` L31-56; `AdjuntosEndpointTests.cs` L37-59, L111-121. |
| Tonos de Estado | Converter nuevo `VarianteEstadoGastoConverter` (string del estado → `VarianteInsignia`); `GastoFila` no cambia. | `Controls/V2/Variantes.cs` L4-10; `EstadoGasto.cs`. |
| Fila anulada | `Classes.inactivo="{Binding !Gasto.Activo}"` sobre `GastoFila.Gasto` (ya público): sin propiedad nueva. | `GastosViewModel.cs` L30-61. |
| Datos fijos | `VisualTests` no ve los `internal` del VM: `DatosGastos` no toca `EsperaDebounce`; los filtros que necesita un mockup se ponen ANTES de la carga (no programan) y la selección de `gastos-con-anulado` la hace el `prepararApp` de la comparación. | `StockApp.Presentation.csproj` (InternalsVisibleTo); plan de la 4c, tabla "Datos fijos en modo año". |
| Guardianes de las vistas viejas | `GastosView` y `GastoFormView` salen de la `InlineData` del `HeaderVista` (`GuardianDePatronTests.cs` L64-65) y de `VistasDeLaTanda` (L111-112) y entran a `VistasMigradasAV2` (L195-217); los reemplazan `Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_NuevoGastoEsElUnicoPrimario` y `Raiz_TieneUi2_BreadcrumbConElTituloDelVm_EnLosTresModos` (convención 13). | `GuardianDePatronTests.cs`. |
| Tests heredados que tocan Gastos | `GastosViewModelTests` (Crear L52-98; `FiltroDeEstado_FiltraEnMemoria` L160-175; `FiltrarCommand_*` L177-209 y L905-916; `ExportarPdfCommand_ConEstadoSeleccionado_*` L862-903), `GastoFormViewModelTests` (Crear L22-82; montos L209-220 y L462-473), `GastosViewTests` (busca "Nuevo gasto"/"Editar" por texto), `GastosViewColumnasAnchoTests` (11 columnas ≥ 60 px a 1300), `GrillasSinRecorteBarridoTests` (escenario "Gastos", L108-117), `AccesoRevocadoPantallasFinanzasTests` (Gastos y formulario en el shell), `ExportarPdfBotonesGuardianTests` (L328-386), `ColumnasPdfContraDtosRealesTests` (L115, sin cambio). Solo regresión: `FinanzasRevocacionPermisosTests`, `EntradaRegistroFacturaTests`, `CalendarioPagosViewModelTests`, `ShellMainViewGatesTests`. | grep `GastosView\|GastoFormView\|GastosViewModel\|GastoFormViewModel` en `tests/`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/ViewModels/RecargaConDebounce.cs` | El debounce compartido (E1). |
| `src/StockApp.Presentation/Converters/VarianteEstadoGastoConverter.cs` | Estado del gasto → tono de la `Insignia` (P2). |
| `tests/StockApp.Presentation.Tests/ViewModels/RecargaConDebounceTests.cs` | Contrato del helper e integración con `ConsultaVigente` (P1-A). |
| `tests/StockApp.Presentation.Tests/Converters/VarianteEstadoGastoConverterTests.cs` | Los cinco tonos. |
| `tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs` | `GastosFake` (IGastoService con filtros grabados, pendiente, falla, anulados, altas) y `ProveedoresFake` (IProveedorService que puede fallar). |
| `tests/StockApp.Presentation.UiTests/V2/GastosViewV2Tests.cs` | La View real del listado: estructura, filtros, grilla, insignias, estados, gates, doble click, Anular, revocación. |
| `tests/StockApp.Presentation.UiTests/V2/GastoFormViewV2Tests.cs` | La View real del formulario: estructura, secciones, errores por campo, pie fijo, tilde, solo lectura, anulado, adjuntos, carga inicial, revocación. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs`, `DatosGastosTests.cs` | Datos fijos de los 18 mockups y sus textos. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastosComparacionTests.cs`, `GastoFormComparacionTests.cs` | 16 + 20 comparaciones. |
| `docs/ui-v2/mockups/finanzas/gastos*.html`, `gasto-*.html` | Los 18 mockups de D16. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs`, `ViewModels/Finanzas/ControlPoaViewModel.cs`, `ViewModels/Finanzas/LibroCajaViewModel.cs` | Solo el debounce, por el helper (E1). |
| `src/StockApp.Application/Finanzas/AdjuntoService.cs` | E9. |
| `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` | E2, E3, E4, E8 (P3), P7. |
| `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` | E5, E6, E7, E8 (P4). |
| `src/StockApp.Presentation/Views/Finanzas/GastosView.axaml` (+ `.axaml.cs`) | Reescrita en v2; code-behind: carga y doble click. |
| `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` (+ `.axaml.cs`) | Reescrita en v2 con pie fijo; code-behind: carga y "tocado" al salir de un campo. |
| `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`, `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs`, `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs` | E9. |
| Tests heredados (convención 13) | `GastosViewModelTests`, `GastoFormViewModelTests`, `GuardianDePatronTests`; y, solo si fallan con motivo, `GastosViewTests`, `GastosViewColumnasAnchoTests`, `GrillasSinRecorteBarridoTests`, `ExportarPdfBotonesGuardianTests`, `AccesoRevocadoPantallasFinanzasTests`. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 17). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln --blame-hang-timeout 5m` (falsos `.ods` e intermitente de arranque: confirmar por nombre)
- Servicio: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --blame-hang-timeout 5m`
- API: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m` (levanta su propio Postgres con Testcontainers: necesita Docker andando)
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de VM (tras cada task de los bloques A, B y D): `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AdjuntosPanel|FullyQualifiedName~RecargaConDebounce|FullyQualifiedName~ConsultaVigente|FullyQualifiedName~ProductoViewModel|FullyQualifiedName~ControlPoa|FullyQualifiedName~LibroCaja|FullyQualifiedName~ColumnasPdfContraDtosReales|FullyQualifiedName~ComandosDeGuardadoGuardian|FullyQualifiedName~EntradaRegistroFactura|FullyQualifiedName~VarianteEstadoGasto"`
- Regresión de UI (tras cada task de los bloques A, B y D): `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~AdjuntosPanel|FullyQualifiedName~ControlPoa|FullyQualifiedName~LibroCaja|FullyQualifiedName~ProductoListView|FullyQualifiedName~GuardarBloqueado|FullyQualifiedName~DataGridColumnas|FullyQualifiedName~ComandoDeGuardadoTooltip"`
- Comparación con página: `scripts/ui-v2/comparar.sh Gasto` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-15-ui-v2-fase-4d/progress.md` (lo crea la Task 1 con BASE = el commit de este plan; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa. Arriba de todo, la elección del usuario para P7 y P8.

---

## Bloque A — Helper `RecargaConDebounce` y migración de los tres debounce

### Task 1: Helper `RecargaConDebounce` (E1, P1-A)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/RecargaConDebounce.cs`
- Create: `tests/StockApp.Presentation.Tests/ViewModels/RecargaConDebounceTests.cs`
- Create (local): el ledger

**Interfaces:**
- Consumes: `ConsultaVigente` (`Empezar()`, `Turno.EsLaUltima`), solo en los tests.
- Produces: `public sealed class RecargaConDebounce` en `StockApp.Presentation.ViewModels`, con `public RecargaConDebounce(Func<TimeSpan> espera)` y `public Task Programar(Func<Task> recarga)`.

- [ ] **Step 1: Línea base y ledger**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m 2>&1 | tail -40
mkdir -p .superpowers/sdd/2026-10-15-ui-v2-fase-4d
```

Crear `.superpowers/sdd/2026-10-15-ui-v2-fase-4d/progress.md` con `BASE: <hash del commit de este plan>`, la elección del usuario para P7 y P8, el total de la suite por proyecto (esperado 5897 verdes; `StockApp.Presentation.UiTests` 1213) y los rojos `.ods` / intermitentes por nombre.

- [ ] **Step 2: Tests (rojo)**

`tests/StockApp.Presentation.Tests/ViewModels/RecargaConDebounceTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>El debounce compartido: solo la última programación de una ráfaga corre su recarga.</summary>
public class RecargaConDebounceTests
{
    [Fact]
    public async Task Programar_TrasLaEspera_CorreLaRecarga()
    {
        var corridas = 0;
        var recarga = new RecargaConDebounce(() => TimeSpan.Zero);

        await recarga.Programar(() => { corridas++; return Task.CompletedTask; });

        Assert.Equal(1, corridas);
    }

    [Fact]
    public async Task DosProgramacionesSeguidas_SoloCorreLaUltima()
    {
        var corridas = new List<string>();
        var recarga = new RecargaConDebounce(() => TimeSpan.FromMilliseconds(50));

        var primera = recarga.Programar(() => { corridas.Add("primera"); return Task.CompletedTask; });
        var segunda = recarga.Programar(() => { corridas.Add("segunda"); return Task.CompletedTask; });
        await Task.WhenAll(primera, segunda);

        Assert.Equal(new[] { "segunda" }, corridas);
    }

    [Fact]
    public async Task EsperaCancelada_NoCorreLaRecarga_YTerminaSinError()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        var corrioLaPrimera = false;
        var primera = recarga.Programar(() => { corrioLaPrimera = true; return Task.CompletedTask; });

        espera = TimeSpan.Zero;
        await recarga.Programar(() => Task.CompletedTask);
        await Task.WhenAny(primera, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.True(primera.IsCompletedSuccessfully, "La espera cancelada no terminó o terminó con error.");
        Assert.False(corrioLaPrimera);
    }

    [Fact]
    public async Task EsperaInfinita_NoCorreLaRecarga()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        var corrio = false;

        var tarea = recarga.Programar(() => { corrio = true; return Task.CompletedTask; });

        Assert.False(tarea.IsCompleted);
        Assert.False(corrio);

        espera = TimeSpan.Zero;                       // libera la espera infinita: no queda un Task.Delay vivo
        await recarga.Programar(() => Task.CompletedTask);
    }

    [Fact]
    public async Task LaEspera_SeLeeEnCadaProgramar()
    {
        var espera = Timeout.InfiniteTimeSpan;
        var recarga = new RecargaConDebounce(() => espera);
        espera = TimeSpan.Zero;                       // como un test que baja EsperaDebounce después de construir el VM
        var corrio = false;

        await recarga.Programar(() => { corrio = true; return Task.CompletedTask; });

        Assert.True(corrio);
    }

    /// <summary>El turno lo pide la carga, no el helper: dos recargas programadas cuya primera respuesta llega tarde no
    /// pisan la segunda.</summary>
    [Fact]
    public async Task ConLaConsultaVigente_LaPrimeraRecargaQueTerminaTarde_NoPisaLaSegunda()
    {
        var consultas = new ConsultaVigente();
        var recarga = new RecargaConDebounce(() => TimeSpan.Zero);
        var vieja = new TaskCompletionSource<string>();
        var nueva = new TaskCompletionSource<string>();
        string? enPantalla = null;

        async Task Cargar(TaskCompletionSource<string> respuesta)
        {
            var turno = consultas.Empezar();
            var resultado = await respuesta.Task;
            if (turno.EsLaUltima)
                enPantalla = resultado;
        }

        var primera = recarga.Programar(() => Cargar(vieja));
        var segunda = recarga.Programar(() => Cargar(nueva));
        nueva.SetResult("nueva");
        await segunda;
        vieja.SetResult("vieja");
        await primera;

        Assert.Equal("nueva", enPantalla);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~RecargaConDebounceTests"`
Expected: FAIL de compilación (`CS0246: The type or namespace name 'RecargaConDebounce' could not be found`). Pegar en el ledger.

- [ ] **Step 3: Implementación**

`src/StockApp.Presentation/ViewModels/RecargaConDebounce.cs`:

```csharp
using System;
using System.Threading;
using System.Threading.Tasks;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Debounce de una recarga: cada <see cref="Programar"/> cancela la espera pendiente y programa una nueva, así una ráfaga
/// de cambios (un año de cuatro cifras, varios filtros que se reponen juntos) consulta una sola vez, con el último valor.
/// Solo hace el debounce: el turno de la consulta (<see cref="ConsultaVigente"/>) lo pide el método de carga del
/// ViewModel, que también usan caminos sin debounce (la carga inicial, Reintentar, Actualizar, Buscar).
/// </summary>
public sealed class RecargaConDebounce
{
    private readonly Func<TimeSpan> _espera;
    private CancellationTokenSource? _cts;

    /// <param name="espera">Se lee en cada <see cref="Programar"/>: los tests la cambian después de construir el ViewModel.</param>
    public RecargaConDebounce(Func<TimeSpan> espera) => _espera = espera;

    /// <summary>
    /// Cancela y libera la espera pendiente (si la hay) y programa una nueva. Devuelve la tarea de esa espera: si vence
    /// sin cancelarse corre <paramref name="recarga"/>; si se cancela, termina sin error.
    /// </summary>
    public Task Programar(Func<Task> recarga)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return EsperarYRecargarAsync(recarga, _espera(), _cts.Token);
    }

    private static async Task EsperarYRecargarAsync(Func<Task> recarga, TimeSpan espera, CancellationToken ct)
    {
        try
        {
            await Task.Delay(espera, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await recarga();
    }
}
```

- [ ] **Step 4: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~RecargaConDebounceTests|FullyQualifiedName~ConsultaVigente"
```

Expected: 0 errores; 6 + 3 PASS.

- [ ] **Step 5: Mutación (QUITANDO)**

- (1a) En `Programar`, quitar la línea `_cts?.Cancel();` → rojo `DosProgramacionesSeguidas_SoloCorreLaUltima` (corren las dos) y `EsperaCancelada_NoCorreLaRecarga_YTerminaSinError` ("La espera cancelada no terminó…"). Pegar. Restaurar con `cp`; `git diff --no-index` vacío; verde. (Spec: "quitar la cancelación de la espera anterior tiene que dar rojo".)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/RecargaConDebounce.cs tests/StockApp.Presentation.Tests/ViewModels/RecargaConDebounceTests.cs
git commit -m "feat(ui-v2): helper RecargaConDebounce para recargar con debounce"
```

### Task 2: Productos, Control POA y Libro caja usan el helper (E1, U4)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` (campo `_debounceCts` L38; `OnFiltroBusquedaChanged` y `DebounceBuscarAsync` L119-148)
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs` (campo `_debounceCts` L78; `OnEjercicioChanged` y `DebounceCargarAsync` L112-135)
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs` (campo `_debounceCts` L120; `ProgramarRecarga` y `DebounceCargarAsync` L133-156)
- Test: NINGUNO nuevo. Los guardianes son los tests existentes, que NO se tocan: `ProductoViewModelTests` (`FiltroBusqueda_AlCambiar_DisparaBusquedaAutomaticaConDebounce`, `FiltroBusqueda_CambiosRapidosSeguidos_CancelaBusquedaObsoleta`, `DosBusquedasEnOrdenInverso_GanaLaUltimaPedida`, `Reintentar_TrasFallarConElTextoA_BuscaConElTextoActualB`), `ControlPoaViewModelTests` (`CambiarEjercicio_RecargaSoloConElEjercicioNuevo`, `CambiosRapidos_SoloConsultaElUltimoEjercicio`, `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`), `LibroCajaViewModelTests` (`CambiarAnio_*`, `CambiarMes_*`, `CambiosRapidos_SoloConsultaElUltimo`, `PasarAAnioCompleto_RecargaElAnual`), `ControlPoaViewV2Tests` y `LibroCajaViewV2Tests`.

**Interfaces:**
- Consumes: `RecargaConDebounce(Func<TimeSpan>)`, `Programar(Func<Task>)` (Task 1).
- Produces: en los tres VMs, sin cambio de superficie: `internal TimeSpan EsperaDebounce { get; set; }` e `internal Task _tareaDebounce` se CONSERVAN con el mismo nombre, tipo y visibilidad; nuevo `private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);`.

- [ ] **Step 1: Regresión antes de tocar (verde de partida)**

```bash
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~ProductoViewModelTests|FullyQualifiedName~ControlPoaViewModelTests|FullyQualifiedName~LibroCajaViewModelTests"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~ControlPoaViewV2Tests|FullyQualifiedName~LibroCajaViewV2Tests|FullyQualifiedName~ProductoListViewV2Tests"
```

Anotar los totales en el ledger: son los que tienen que dar igual después. Esta task no tiene rojo propio (es un refactor custodiado por tests existentes): su evidencia son las mutaciones del Step 5.

- [ ] **Step 2: Productos**

En `ProductoListViewModel.cs` borrar la línea 38 (`private CancellationTokenSource? _debounceCts;`) y reemplazar las líneas 119-148 (desde el `/// <summary>` de "Dispara la búsqueda automática con debounce…" hasta el cierre de `DebounceBuscarAsync`) por:

```csharp
    /// <summary>Debounce de la búsqueda automática: de una ráfaga de teclas solo la última va al servidor.</summary>
    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>
    /// Dispara la búsqueda automática con debounce cada vez que cambia el filtro, para no golpear el servicio en cada
    /// tecla. Un cambio nuevo cancela la espera anterior. El botón Buscar consulta sin debounce.
    /// </summary>
    partial void OnFiltroBusquedaChanged(string? value) => _tareaDebounce = Recarga.Programar(EjecutarBusquedaAsync);
```

- [ ] **Step 3: Control POA**

En `ControlPoaViewModel.cs` borrar la línea 78 (`private CancellationTokenSource? _debounceCts;`) y reemplazar las líneas 112-135 (el `/// <summary>` de "Cambiar el Ejercicio recarga sola…", `OnEjercicioChanged` y `DebounceCargarAsync`) por:

```csharp
    /// <summary>Debounce del Ejercicio: de una ráfaga de cambios solo el último va al servidor.</summary>
    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>Cambiar el Ejercicio recarga sola tras la espera; un cambio nuevo cancela la espera anterior. Un texto
    /// inválido en el NumericUpDown no llega acá (Ejercicio conserva el valor), así que no recarga con el año viejo.</summary>
    partial void OnEjercicioChanged(int value) => _tareaDebounce = Recarga.Programar(CargarAsync);
```

- [ ] **Step 4: Libro caja**

En `LibroCajaViewModel.cs` borrar la línea 120 (`private CancellationTokenSource? _debounceCts;`) y reemplazar las líneas 133-156 (el `/// <summary>` de "Un cambio nuevo cancela la espera anterior…", `ProgramarRecarga` y `DebounceCargarAsync`) por:

```csharp
    /// <summary>Debounce de año, mes y modo: de una ráfaga de cambios solo el último va al servidor.</summary>
    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>Un cambio nuevo cancela la espera anterior. Un texto inválido en un NumericUpDown no llega acá (el VM
    /// conserva el valor), así que no recarga con el valor viejo.</summary>
    private void ProgramarRecarga() => _tareaDebounce = Recarga.Programar(CargarAsync);
```

`OnAnioChanged`, `OnMesChanged` (con su condición de año completo) y `OnVerAnioCompletoChanged` no cambian. Los `using System.Threading;` que queden sin uso se dejan (no hay analizador que lo marque como error).

- [ ] **Step 5: Verde, build y mutaciones (QUITANDO)**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~ProductoViewModelTests|FullyQualifiedName~ControlPoaViewModelTests|FullyQualifiedName~LibroCajaViewModelTests|FullyQualifiedName~RecargaConDebounce"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~ControlPoaViewV2Tests|FullyQualifiedName~LibroCajaViewV2Tests|FullyQualifiedName~ProductoListViewV2Tests"
git diff --stat tests/
```

Expected: los mismos totales del Step 1, todo verde; `git diff --stat tests/` VACÍO (ningún test de los tres se tocó). Si algo pide tocar un test, PARAR y reportar (U4).

Mutaciones (cada una: `cp` al `$SCRATCH`, quitar, correr el filtro de VM de arriba, pegar el rojo, restaurar, `git diff --no-index` vacío):
- (2a) Productos: quitar la línea `partial void OnFiltroBusquedaChanged(string? value) => _tareaDebounce = Recarga.Programar(EjecutarBusquedaAsync);` → rojo `FiltroBusqueda_AlCambiar_DisparaBusquedaAutomaticaConDebounce`.
- (2b) Control POA: quitar la línea `partial void OnEjercicioChanged(int value) => …;` → rojo `CambiarEjercicio_RecargaSoloConElEjercicioNuevo`.
- (2c) Libro caja: quitar la línea `partial void OnAnioChanged(int value) => ProgramarRecarga();` → rojo `CambiarAnio_EnModoMes_RecargaEseMesDelAnioNuevo` (correr SOLO ese test con `--filter`: `CambiarAnio_EnAnioCompleto_…` esperaría el debounce infinito de pasar a año completo y lo cortaría el `--blame-hang-timeout`).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs
git commit -m "refactor(ui-v2): Productos, Control POA y Libro caja recargan con RecargaConDebounce"
```

---

## Bloque B — Servidor y ViewModels, sin tocar las vistas

### Task 3: El servidor rechaza adjuntar a un gasto anulado (E9, P6)

**Files:**
- Modify: `src/StockApp.Application/Finanzas/AdjuntoService.cs` (entre L36 y L38)
- Modify: `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs` (tests nuevos tras `AgregarAGastoAsync_SinPermiso_NoPersisteNiAuditaYPropagaExcepcion`)
- Modify: `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs` (`SembrarGastoAsync` L37-59; test nuevo tras `PostAdjuntoGasto_MimeInvalido_Devuelve409`, L111-121)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs` (test nuevo al final)

**Interfaces:**
- Consumes: `IGastoRepository.ObtenerPorIdAsync(int)` → `Gasto?`; `ReglaDeNegocioException(string)`; `DomainExceptionHandler` (409 con `detail`).
- Produces: el mensaje `"No se puede adjuntar a un gasto anulado."` (lo verifica la Task 17 por la API en la verificación orgánica). Firma, ruta y DTOs sin cambio.

- [ ] **Step 1: Tests de servicio y de endpoint (rojo)**

En `AdjuntoServiceTests.cs`, después de `AgregarAGastoAsync_SinPermiso_NoPersisteNiAuditaYPropagaExcepcion`:

```csharp
    [Fact]
    public async Task AgregarAGastoAsync_GastoAnulado_LanzaReglaDeNegocio()
    {
        _gastos.Setup(g => g.ObtenerPorIdAsync(7)).ReturnsAsync(new Gasto { Id = 7, Activo = false });

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAGastoAsync(7, "factura.pdf", BytesPdf));

        Assert.Equal("No se puede adjuntar a un gasto anulado.", ex.Message);
        _adjuntos.Verify(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>()), Times.Never);
        _audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>()),
            Times.Never);
    }

    /// <summary>La regla va antes de validar el archivo: a un gasto anulado no se le adjunta nada, sea cual sea el archivo.</summary>
    [Fact]
    public async Task AgregarAGastoAsync_GastoAnuladoYArchivoInvalido_RechazaPorAnulado()
    {
        _gastos.Setup(g => g.ObtenerPorIdAsync(7)).ReturnsAsync(new Gasto { Id = 7, Activo = false });

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAGastoAsync(7, "archivo.exe", new byte[] { 0x00, 0x01, 0x02 }));

        Assert.Equal("No se puede adjuntar a un gasto anulado.", ex.Message);
    }
```

En `AdjuntosEndpointTests.cs`: agregar `using System.Text.Json;` arriba; cambiar la firma de `SembrarGastoAsync` (L37) a `private async Task<int> SembrarGastoAsync(bool activo = true)` y, en el inicializador del `Gasto` (L51-55), agregar `Activo = activo,` después de `CondicionPago = CondicionPago.Contado,`. Después de `PostAdjuntoGasto_MimeInvalido_Devuelve409`:

```csharp
    [Fact]
    public async Task PostAdjuntoGasto_GastoAnulado_Devuelve409()
    {
        var gastoId = await SembrarGastoAsync(activo: false);
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("No se puede adjuntar a un gasto anulado.", doc.RootElement.GetProperty("detail").GetString());
    }
```

Run:
```bash
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AdjuntoServiceTests"
dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AdjuntosEndpointTests"
```
Expected: FAIL `AgregarAGastoAsync_GastoAnulado_LanzaReglaDeNegocio` ("Assert.Throws() Failure: No exception was thrown"), FAIL `…YArchivoInvalido_RechazaPorAnulado` (lanza la del MIME, con otro mensaje), FAIL `PostAdjuntoGasto_GastoAnulado_Devuelve409` (Expected: Conflict, Actual: Created). Los demás de las dos clases, verdes. Pegar los tres rojos en el ledger.

- [ ] **Step 2: Implementación**

En `AdjuntoService.cs`, entre la resolución del gasto (L35-36) y `AdjuntoValidador.Validar(contenido, nombreArchivo);` (L38):

```csharp
        if (!gasto.Activo)
            throw new ReglaDeNegocioException("No se puede adjuntar a un gasto anulado.");
```

(Mismo estilo que `GastoService.cs` L91-92, "No se puede modificar un gasto anulado.".)

- [ ] **Step 3: El cliente muestra el 409 (caracterización)**

En `AdjuntosPanelViewModelTests.cs`, agregar `using StockApp.Domain.Exceptions;` y, al final de la clase:

```csharp
    /// <summary>El 409 de adjuntar a un gasto anulado llega como ReglaDeNegocioException con el detail del servidor
    /// (ApiErrores) y sale por el mismo aviso que cualquier error del panel.</summary>
    [Fact]
    public async Task AgregarAsync_GastoAnuladoRechazadoPorElServidor_InformaElMensaje()
    {
        _seleccion.Setup(s => s.SeleccionarArchivoAsync()).ReturnsAsync(("factura.pdf", new byte[] { 1, 2 }));
        _adjuntos.Setup(a => a.AgregarAGastoAsync(5, "factura.pdf", It.IsAny<byte[]>()))
            .ThrowsAsync(new ReglaDeNegocioException("No se puede adjuntar a un gasto anulado."));
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);

        await _vm.AgregarCommand.ExecuteAsync(null);

        _confirmacion.Verify(c => c.InformarAsync("No se puede adjuntar a un gasto anulado."), Times.Once);
    }
```

Este test NACE VERDE (el cliente no cambia, spec E9): anotarlo así en el ledger; su custodia es la mutación (3b).

- [ ] **Step 4: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AdjuntoServiceTests"
dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AdjuntosEndpointTests"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AdjuntosPanelViewModelTests"
```

Expected: todo verde (los `.ods` no aplican a estas clases).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (3a) En `AdjuntoService.cs`, quitar las dos líneas de la regla (`if (!gasto.Activo)` y su `throw`) → rojo `AgregarAGastoAsync_GastoAnulado_LanzaReglaDeNegocio`, `…YArchivoInvalido_RechazaPorAnulado` (Application.Tests) Y `PostAdjuntoGasto_GastoAnulado_Devuelve409` (Api.Tests). Pegar los dos proyectos. Restaurar. (Spec: "la validación de servidor `!gasto.Activo` de E9 (rojo en `AdjuntoServiceTests` y en `AdjuntosEndpointTests`)".)
- (3b) En `AdjuntosPanelViewModel.cs` (`AgregarAsync`, L111-114), quitar la línea `await _confirmacion.InformarAsync(ex.Message);` del `catch` → rojo `AgregarAsync_GastoAnuladoRechazadoPorElServidor_InformaElMensaje`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Finanzas/AdjuntoService.cs tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs
git commit -m "feat(finanzas): no se puede adjuntar a un gasto anulado"
```

### Task 4: Gastos — carga protegida, consulta vigente, sin conexión y sin permiso (E2, P7)

**Depende de:** la respuesta del usuario a P7 (el plan implementa P7-A).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` (constantes L70; campos L72-82; `CargarAsync` L192-252; `NotificarPermisosActualizados` L259-265; `FiltrarCommand`/`FiltrarAsync` L281-308; `LimpiarFiltrosAsync` L320; `AnularAsync` L371; `ReintentarAnulacionConPagoAutomaticoAsync` L411)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs` (`Crear` L52-98; tests nuevos al final)

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `AvisaSinConexionEnLinea`, `AlPerderConexion()`, `ReintentarAsync(Func<Task>)`, `ReintentarCargaCommand`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`, `MarcarSinPermiso(string)`; `ConsultaVigente`.
- Produces: `public const string MensajeSinPermisoGastos = "No tenés permiso para ver los gastos."`; `private async Task CargarCombosAsync()`; `private async Task RecargarAsync()` (listado con los filtros de pantalla, protegida y con turno; la usan Anular, `FiltrarCommand` hasta la Task 6 y el debounce desde la Task 6); `private async Task ListarYAplicarAsync(GastoFiltro filtro, ConsultaVigente.Turno turno)` (la Task 5 le cambia el primer parámetro por `ConsultaGastos`); `private void VaciarTrasUn403(ConsultaVigente.Turno turno)`; campo `private bool _combosCargados`. En el helper de tests: `Crear(…, Func<Task<IReadOnlyList<Proveedor>>>? proveedoresActivos = null)`.

- [ ] **Step 1: Tests (rojo)**

En `GastosViewModelTests.cs`:

1. `Crear` gana un último parámetro opcional `Func<Task<IReadOnlyList<Proveedor>>>? proveedoresActivos = null` (después de `UsuarioSesion? usuarioSesion = null`) y la línea `proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(proveedoresDisponibles);` (L79) pasa a:

```csharp
        proveedores.Setup(p => p.ListarActivasAsync())
            .Returns(proveedoresActivos ?? (() => Task.FromResult<IReadOnlyList<Proveedor>>(proveedoresDisponibles)));
```

2. Al final de la clase:

```csharp
    // ── Carga protegida, consulta vigente y estados de pantalla ──────────────────

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<Gasto> { GastoDe(1, "Uno") });
        await carga;

        Assert.False(vm.Cargando);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task CargarAsync_403EnElListado_MarcaSinPermisoConSuMensaje_SinOtroAviso()
    {
        var (vm, svc, _, confirm, _, _, _) = Crear();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(GastosViewModel.MensajeSinPermisoGastos, vm.MensajeSinPermiso);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CargarAsync_403EnLosCombos_MarcaSinPermiso_YNoConsultaElListado()
    {
        var (vm, svc, _, _, _, _, _) = Crear(
            proveedoresActivos: () => Task.FromException<IReadOnlyList<Proveedor>>(new UnauthorizedAccessException("403")));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Never);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLaGrilla()
    {
        var (vm, svc, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Uno") });
        await vm.CargarAsync();
        Assert.Single(vm.Filas);
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ThrowsAsync(new ServidorNoDisponibleException());

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task SinConexionAlCargarLosCombos_Reintentar_CargaLosCombosYElListado()
    {
        var intentos = 0;
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Uno") },
            proveedoresActivos: () => ++intentos == 1
                ? Task.FromException<IReadOnlyList<Proveedor>>(new ServidorNoDisponibleException())
                : Task.FromResult<IReadOnlyList<Proveedor>>(new List<Proveedor> { new() { Id = 1, Nombre = "Barraca X", Activo = true } }));
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);
        Assert.Empty(vm.ProveedoresDisponibles);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.ProveedoresDisponibles);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaidaDelListado_ConsultaConLosFiltrosDePantalla()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.SetupSequence(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(new List<Gasto> { GastoDe(1, "Uno") });
        await vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 7, 1);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.FechaDesde == new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
        Assert.False(vm.SinConexion);
        Assert.Single(vm.Filas);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var vieja = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        var nueva = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        svc.SetupSequence(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .Returns(vieja.Task)
            .Returns(nueva.Task);

        var primera = vm.CargarAsync();
        var segunda = vm.CargarAsync();
        nueva.SetResult(new List<Gasto> { GastoDe(2, "Nueva") });
        await segunda;
        vieja.SetResult(new List<Gasto> { GastoDe(1, "Vieja") });
        await primera;

        Assert.Equal("Nueva", Assert.Single(vm.Filas).Detalle);
    }

    /// <summary>Un 403 que no vino del listado (p. ej. de Anular) quitó finanzas.ver: la pantalla no puede quedar
    /// diciendo "Todavía no hay gastos" con la grilla vaciada.</summary>
    [Fact]
    public async Task NotificarPermisosActualizados_TrasPerderVerFinanzas_MarcaSinPermiso()
    {
        var permisos = new HashSet<string> { Permisos.VerFinanzas };
        var (vm, _, _, _, _, _, _) = Crear(
            new List<Gasto> { GastoDe(1, "Vieja") }, rol: RolUsuario.Operador, permisos: permisos);
        await vm.CargarAsync();

        permisos.Remove(Permisos.VerFinanzas);
        vm.NotificarPermisosActualizados();

        Assert.True(vm.SinPermiso);
        Assert.Equal(GastosViewModel.MensajeSinPermisoGastos, vm.MensajeSinPermiso);
        Assert.Empty(vm.Filas);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests"`
Expected: FAIL de compilación (`CS0117: 'GastosViewModel' does not contain a definition for 'MensajeSinPermisoGastos'`). Pegar. Rojo de comportamiento: agregar SOLO la constante del Step 2.1 y volver a correr → FAIL `CargarAsync_MientrasEspera_…` (`Cargando` false), `CargarAsync_403EnElListado_…` y `CargarAsync_403EnLosCombos_…` (`SinPermiso` false: el `catch` silencioso de hoy), `ServidorCaido_…`, `SinConexionAlCargarLosCombos_…` y `Reintentar_…` (la `ServidorNoDisponibleException` escapa), `ConsultaVieja_…` (queda "Vieja") y `NotificarPermisosActualizados_TrasPerderVerFinanzas_…`; pegar.

- [ ] **Step 2: Implementación**

En `GastosViewModel.cs`:

1. Después de `public const string EstadoTodos = "Todos";` (L70):

```csharp
    /// <summary>Texto del estado "Sin permiso" del listado: el servidor rechazó la consulta (403) o se perdió finanzas.ver
    /// con la pantalla abierta.</summary>
    public const string MensajeSinPermisoGastos = "No tenés permiso para ver los gastos.";
```

2. Después de `private readonly IServicioAperturaArchivo _apertura;` (L82):

```csharp

    private readonly ConsultaVigente _consultas = new();

    /// <summary>Los combos de los filtros se piden una sola vez: Reintentar los vuelve a pedir solo si fallaron.</summary>
    private bool _combosCargados;
```

3. Después de la propiedad `PuedeVerFinanzas` (L153-155):

```csharp

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar dentro de la card.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las facturas de otra consulta.</summary>
    protected override void AlPerderConexion() => Filas.Clear();

    /// <summary>Reintentar consulta con los filtros que están en pantalla (y pide los combos si fallaron), no con los de
    /// la consulta que falló.</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();
```

4. Reemplazar `CargarAsync` completo (L192-252, desde `/// <summary>Carga combos de filtros + primer listado…` hasta su `}`) por:

```csharp
    /// <summary>
    /// Combos de los filtros (una sola vez) y listado. La dispara la View (DataContextChanged) y Reintentar. Protegida: un
    /// 403 deja SinPermiso (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del
    /// modal global y mientras espera Cargando está en true. Con turno: si mientras espera empezó otra consulta, su
    /// resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        try
        {
            await EjecutarCargaProtegidaAsync(async () =>
            {
                if (!_combosCargados)
                {
                    await CargarCombosAsync();
                    _combosCargados = true;
                }
                await ListarYAplicarAsync(ArmarFiltro(), turno);
            }, MensajeSinPermisoGastos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        VaciarTrasUn403(turno);
    }

    private async Task CargarCombosAsync()
    {
        // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15): el servidor exige GestionarTablasMaestras para
        // ListarTodosAsync, pero un Operador de Finanzas solo tiene VerFinanzas. Los cuatro vienen filtrados a Activo=true
        // del lado del servidor.
        var proveedores = await _proveedoresService.ListarActivasAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores)
            ProveedoresDisponibles.Add(p);

        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        var rubros = await _rubrosService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros)
            RubrosDisponibles.Add(r);

        var lineas = await _lineasService.ListarActivasAsync();
        LineasPoaDisponibles.Clear();
        foreach (var l in lineas)
            LineasPoaDisponibles.Add(l);

        // Bug real (verificación orgánica F4): la línea POA preseleccionada (FiltrarPorLineaPoa, desde Control POA) viene
        // de otra consulta y el ComboBox matchea SelectedItem por referencia, no por Id: sin este remap el filtro de datos
        // queda bien pero el combo se muestra en "Todas".
        if (LineaPoaSeleccionada is not null)
        {
            LineaPoaSeleccionada = LineasPoaDisponibles
                .FirstOrDefault(l => l.Id == LineaPoaSeleccionada.Id) ?? LineaPoaSeleccionada;
        }
    }
```

5. Reemplazar `NotificarPermisosActualizados` (L254-265, con su `/// <summary>`) por:

```csharp
    /// <summary>
    /// Un 403 refrescó los permisos en caliente (convención 21): además de re-leer los <c>Puede*</c> (base), se reevalúa
    /// Exportar y, si ya no hay <c>VerFinanzas</c>, se vacía la grilla y la pantalla pasa a "Sin permiso" (si no, la grilla
    /// vacía diría "Todavía no hay gastos").
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        if (!PuedeVerFinanzas)
        {
            Filas.Clear();
            MarcarSinPermiso(MensajeSinPermisoGastos);
        }
        ExportarPdfCommand.NotifyCanExecuteChanged();
        base.NotificarPermisosActualizados();
    }
```

(Con P7-B: sin la línea `MarcarSinPermiso(...)`, sin las llaves y sin el test `NotificarPermisosActualizados_TrasPerderVerFinanzas_MarcaSinPermiso`.)

6. Reemplazar `FiltrarCommand` y `FiltrarAsync` (L281-308) por:

```csharp
    public IAsyncRelayCommand FiltrarCommand => field ??= ComandoDeGuardado(RecargarAsync);

    /// <summary>Recarga el listado con los filtros que están en pantalla (tras Anular y por cambio de filtro). Misma
    /// protección y mismo turno que la carga.</summary>
    private async Task RecargarAsync()
    {
        var turno = _consultas.Empezar();
        var filtro = ArmarFiltro();
        try
        {
            await EjecutarCargaProtegidaAsync(() => ListarYAplicarAsync(filtro, turno), MensajeSinPermisoGastos, turno);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        VaciarTrasUn403(turno);
    }

    private async Task ListarYAplicarAsync(GastoFiltro filtro, ConsultaVigente.Turno turno)
    {
        var gastos = await _service.ListarAsync(filtro);
        if (!turno.EsLaUltima)
            return;

        var ahora = DateTime.UtcNow;
        var filas = gastos.Select(g => new GastoFila(g, ahora));
        if (EstadoSeleccionado != EstadoTodos)
            filas = filas.Where(f => f.Estado == EstadoSeleccionado);

        Filas.Clear();
        foreach (var fila in filas)
            Filas.Add(fila);
    }

    /// <summary>Tras un 403 de la consulta vigente la grilla no conserva filas viejas (ni exportables).</summary>
    private void VaciarTrasUn403(ConsultaVigente.Turno turno)
    {
        if (SinPermiso && turno.EsLaUltima)
            Filas.Clear();
    }
```

7. En `LimpiarFiltrosAsync` (L320), `await FiltrarAsync();` → `await RecargarAsync();`. En `AnularAsync` (L371) y en `ReintentarAnulacionConPagoAutomaticoAsync` (L411), `await FiltrarAsync();` → `await RecargarAsync();`.
8. Actualizar los `<see cref="FiltrarAsync"/>` de los comentarios (L465 y L491) a `RecargarAsync`.

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~ExportarPdfBotones"
```

Expected: verde, incluidos los heredados sin tocar `CargarAsync_Un403_VaciaLaGrilla`, `FiltrarCommand_Un403_VaciaLaGrillaYNoPropaga`, `CargarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcionYNoDuplicaAviso` y los UiTests de la vista v1 (que sigue llamando `CargarAsync`).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (4a) En `ListarYAplicarAsync`, quitar `if (!turno.EsLaUltima)` y su `return;` → rojo `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`. (Spec: "el turno de la recarga de Gastos".)
- (4b) Quitar `protected override bool AvisaSinConexionEnLinea => true;` → rojo `ServidorCaido_TrasUnaCargaBuena_…` y `SinConexionAlCargarLosCombos_…`.
- (4c) Quitar `protected override void AlPerderConexion() => Filas.Clear();` → rojo `ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLaGrilla`.
- (4d) Quitar `protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();` → rojo `Reintentar_TrasUnaCaidaDelListado_ConsultaConLosFiltrosDePantalla` (la base repite el filtro viejo).
- (4e) En `NotificarPermisosActualizados`, quitar `MarcarSinPermiso(MensajeSinPermisoGastos);` → rojo `NotificarPermisosActualizados_TrasPerderVerFinanzas_MarcaSinPermiso` (solo con P7-A).
- (4f) En `CargarAsync`, quitar `VaciarTrasUn403(turno);` → rojo `CargarAsync_Un403_VaciaLaGrilla`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs
git commit -m "feat(finanzas): gastos muestra carga, sin conexión con reintento y sin permiso, y solo la última consulta"
```

### Task 5: Gastos — Estado en el cliente, pie, vacíos y PDF de lo cargado (E4)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` (usings; constructor L157-183; `CargarAsync`, `RecargarAsync` y `ListarYAplicarAsync` de la Task 4; `ConstruirDescripcionFiltros` L462-486; `ExportarPdfAsync` L488-519)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs` (`FiltroDeEstado_FiltraEnMemoria` L160-175; `ExportarPdfCommand_ConEstadoSeleccionado_…` L862-903; tests nuevos al final)

**Interfaces:**
- Consumes: lo de la Task 4.
- Produces: `internal bool EsVisible(object item)`; `public int CantidadVisible { get; }`; `public bool HayFiltrosAplicados { get; }`; `public bool PuedeExportar { get; }`; `private sealed record ConsultaGastos(GastoFiltro Filtro, IReadOnlyList<string> Descripcion)`; `private ConsultaGastos CapturarConsulta()`; `private ConsultaGastos? _consultaCargada`; `ListarYAplicarAsync(ConsultaGastos consulta, ConsultaVigente.Turno turno)` (cambia el primer parámetro); `ExportarPdfCommand` con `CanExecute = nameof(PuedeExportar)`.

- [ ] **Step 1: Tests (rojo)**

1. Adaptar (convención 13) `FiltroDeEstado_FiltraEnMemoria` (L160-175): reemplazar

```csharp
        vm.EstadoSeleccionado = "Pagada";
        await vm.FiltrarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.Filas);
```

por

```csharp
        vm.EstadoSeleccionado = "Pagada";

        var fila = Assert.Single(vm.FilasView.Cast<GastoFila>());
```

2. Adaptar `ExportarPdfCommand_ConEstadoSeleccionado_SoloExportaFilasDeEseEstadoYLoMencionaEnFiltros` (L871-903): borrar la línea `await vm.FiltrarCommand.ExecuteAsync(null);` y, en su `/// <summary>` (L862-870), cambiar "el filtro de estado se aplica EN MEMORIA sobre … dentro de <c>FiltrarAsync</c> (no en el servidor)" por "el filtro de estado se aplica EN MEMORIA con el predicado <c>EsVisible</c> (no en el servidor)".

3. Al final de la clase:

```csharp
    // ── Estado en el cliente, pie, vacíos y PDF de lo cargado ────────────────────

    private static List<GastoFila> Visibles(GastosViewModel vm) => vm.FilasView.Cast<GastoFila>().ToList();

    private static List<Gasto> PendienteYPagada() => new() { GastoDe(1, "Pendiente de pago"), GastoDe(2, "Ya pagado", pagado: true) };

    [Fact]
    public async Task Estado_RefiltraAlInstante_SinConsultarAlServidor()
    {
        var (vm, svc, _, _, _, _, _) = Crear(PendienteYPagada());
        await vm.CargarAsync();

        vm.EstadoSeleccionado = "Pagada";

        Assert.Equal("Pagada", Assert.Single(Visibles(vm)).Estado);
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal(1, vm.CantidadVisible);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Once);
    }

    [Fact]
    public async Task CantidadVisible_TrasCargar_CuentaTodas()
    {
        var (vm, _, _, _, _, _, _) = Crear(PendienteYPagada());

        await vm.CargarAsync();

        Assert.Equal(2, vm.CantidadVisible);
    }

    [Fact]
    public async Task Estado_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccion()
    {
        var (vm, _, _, _, _, _, _) = Crear(PendienteYPagada());
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas.Single(f => f.Estado == "Pendiente");

        vm.EstadoSeleccionado = "Pagada";

        Assert.Null(vm.FilaSeleccionada);
        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.AnularCommand.CanExecute(null));
    }

    [Fact]
    public async Task Estado_QueDejaVerLaFilaSeleccionada_LaConserva()
    {
        var (vm, _, _, _, _, _, _) = Crear(PendienteYPagada());
        await vm.CargarAsync();
        var pagada = vm.Filas.Single(f => f.Estado == "Pagada");
        vm.FilaSeleccionada = pagada;

        vm.EstadoSeleccionado = "Pagada";

        Assert.Same(pagada, vm.FilaSeleccionada);
    }

    [Fact]
    public async Task HayFiltrosAplicados_SinFiltrosEsFalse_ConEstadoEsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear(PendienteYPagada());
        await vm.CargarAsync();
        Assert.False(vm.HayFiltrosAplicados);

        vm.EstadoSeleccionado = "Vencida";

        Assert.True(vm.HayFiltrosAplicados);
        Assert.Equal(0, vm.CantidadVisible);
    }

    [Fact]
    public async Task HayFiltrosAplicados_ConUnFiltroDeServidorCargado_EsTrue()
    {
        var (vm, _, _, _, _, _, _) = Crear();
        vm.FechaDesde = new DateTime(2026, 11, 1);

        await vm.CargarAsync();

        Assert.True(vm.HayFiltrosAplicados);
    }

    private static void CapturarExporte(Mock<IPdfExporter> pdf, Mock<IServicioGuardadoArchivo> guardado,
        Action<IEnumerable<GastoFila>, MetadatosDocumento> capturar)
    {
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<GastoFila>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((items, _, m, _) => capturar(items, m))
            .Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
    }

    /// <summary>Un filtro cambiado que todavía no recargó no figura en el PDF: el PDF rotula lo que se ve.</summary>
    [Fact]
    public async Task ExportarPdf_ConFiltroCambiadoSinRecargar_RotulaLoCargado()
    {
        var (vm, _, _, _, guardado, pdf, _) = Crear(new List<Gasto> { GastoDe(1, "Factura de luz") });
        await vm.CargarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        MetadatosDocumento? metadatos = null;
        CapturarExporte(pdf, guardado, (_, m) => metadatos = m);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal("Sin filtros aplicados.", metadatos!.DescripcionFiltros);
    }

    [Fact]
    public async Task ExportarPdf_MientrasCarga_NoSePuedeEjecutar()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        pendiente.SetResult(new List<Gasto> { GastoDe(1, "Uno") });
        await carga;
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportarPdf_ConSinPermiso_NoSePuedeEjecutar()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests"`
Orden para tener los dos rojos reales: (a) aplicar SOLO las adaptaciones 1 y 2 y correr `--filter "FullyQualifiedName~FiltroDeEstado_FiltraEnMemoria|FullyQualifiedName~ExportarPdfCommand_ConEstadoSeleccionado"` → FAIL las dos (`FilasView` todavía sin filtro: 2 elementos; el PDF recibe 2 filas porque `Filas` ya no viene filtrada sin `FiltrarCommand`); pegar. (b) Pegar los tests nuevos y correr la clase → FAIL de compilación (`CS1061: 'GastosViewModel' does not contain a definition for 'CantidadVisible'`, ídem `HayFiltrosAplicados`); pegar.

- [ ] **Step 2: Implementación**

En `GastosViewModel.cs`:

1. Agregar `using System.ComponentModel;` entre los usings.

2. Después de `private bool _combosCargados;` (Task 4):

```csharp

    /// <summary>Una consulta al servidor: su filtro y cómo se describe en el PDF. Se arma al empezar la consulta.</summary>
    private sealed record ConsultaGastos(GastoFiltro Filtro, IReadOnlyList<string> Descripcion);

    /// <summary>La consulta cuyo resultado está en pantalla (los filtros pueden ir por delante mientras espera una recarga).</summary>
    private ConsultaGastos? _consultaCargada;

    private int _cantidadVisible;

    /// <summary>Filas que deja ver el filtro de Estado (pie y vacío "sin resultados").</summary>
    public int CantidadVisible
    {
        get => _cantidadVisible;
        private set => SetProperty(ref _cantidadVisible, value);
    }

    /// <summary>True si lo que se ve está filtrado: algún filtro de la consulta cargada o un Estado distinto de "Todos".
    /// Elige entre "Todavía no hay gastos" y "Ningún gasto coincide con los filtros".</summary>
    public bool HayFiltrosAplicados => (_consultaCargada?.Descripcion.Count ?? 0) > 0 || EstadoSeleccionado != EstadoTodos;

    /// <summary>
    /// El export serializa en el cliente las filas ya cargadas: sin finanzas.ver no salen de la pantalla, tras un 403 son
    /// de una consulta rechazada y mientras carga pueden ser de otra consulta.
    /// </summary>
    public bool PuedeExportar => PuedeVerFinanzas && !SinPermiso && !Cargando;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is nameof(SinPermiso) or nameof(Cargando))
        {
            OnPropertyChanged(nameof(PuedeExportar));
            ExportarPdfCommand.NotifyCanExecuteChanged();
        }
    }
```

3. En el constructor, reemplazar `FilasView = new DataGridCollectionView(Filas);` por:

```csharp
        FilasView = new DataGridCollectionView(Filas) { Filter = EsVisible };
        Filas.CollectionChanged += (_, _) => RecalcularVisibles();
```

4. Después de `FiltrarPorLineaPoa` (L190):

```csharp

    /// <summary>Predicado del filtro de Estado: la grilla, el pie y el PDF usan el mismo, así lo que se cuenta y lo que se
    /// exporta es lo que se ve.</summary>
    internal bool EsVisible(object item) =>
        item is GastoFila fila && (EstadoSeleccionado == EstadoTodos || fila.Estado == EstadoSeleccionado);

    /// <summary>Estado no viaja al servidor (es calculado): refiltra al instante, sin consulta y sin espera.</summary>
    partial void OnEstadoSeleccionadoChanged(string value) => AplicarFiltroEstado();

    /// <summary>Si el filtro esconde la fila seleccionada, la selección se limpia: Editar, Pagos y Anular no actúan sobre
    /// una fila que no se ve.</summary>
    private void AplicarFiltroEstado()
    {
        FilasView.Refresh();
        if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))
            FilaSeleccionada = null;
        RecalcularVisibles();
        OnPropertyChanged(nameof(HayFiltrosAplicados));
    }

    private void RecalcularVisibles() => CantidadVisible = Filas.Count(f => EsVisible(f));

    /// <summary>Arma la consulta con los filtros de pantalla y su descripción para el PDF (solo los activos: un PDF que no
    /// dice que está filtrado miente sobre su universo).</summary>
    private ConsultaGastos CapturarConsulta()
    {
        var partes = new List<string>();
        if (FechaDesde is not null || FechaHasta is not null)
            partes.Add($"Período: {FechaDesde?.ToString("dd/MM/yyyy") ?? "(sin desde)"} a {FechaHasta?.ToString("dd/MM/yyyy") ?? "(sin hasta)"}");
        if (ProveedorSeleccionado is not null)
            partes.Add($"Proveedor: {ProveedorSeleccionado.Nombre}");
        if (FuenteSeleccionada is not null)
            partes.Add($"Fuente: {FuenteSeleccionada.Nombre}");
        if (RubroSeleccionado is not null)
            partes.Add($"Rubro: {RubroSeleccionado.Nombre}");
        if (LineaPoaSeleccionada is not null)
            partes.Add($"Línea POA: {LineaPoaSeleccionada.Nombre}");
        return new ConsultaGastos(ArmarFiltro(), partes);
    }
```

5. En `CargarAsync`, `await ListarYAplicarAsync(ArmarFiltro(), turno);` → `await ListarYAplicarAsync(CapturarConsulta(), turno);`. En `RecargarAsync`, `var filtro = ArmarFiltro();` → `var consulta = CapturarConsulta();` y `ListarYAplicarAsync(filtro, turno)` → `ListarYAplicarAsync(consulta, turno)`.

6. Reemplazar `ListarYAplicarAsync` (Task 4) por:

```csharp
    private async Task ListarYAplicarAsync(ConsultaGastos consulta, ConsultaVigente.Turno turno)
    {
        var gastos = await _service.ListarAsync(consulta.Filtro);
        if (!turno.EsLaUltima)
            return;

        var ahora = DateTime.UtcNow;
        Filas.Clear();
        foreach (var gasto in gastos)
            Filas.Add(new GastoFila(gasto, ahora));
        _consultaCargada = consulta;
        OnPropertyChanged(nameof(HayFiltrosAplicados));
    }
```

7. Reemplazar `ConstruirDescripcionFiltros` (L462-486, con su `/// <summary>`) por:

```csharp
    /// <summary>
    /// Describe los filtros del resultado EN PANTALLA: los de la consulta cargada (no los que el usuario cambió y todavía
    /// no recargaron) más el Estado vigente, que filtra en memoria y no viaja al servidor.
    /// </summary>
    private string ConstruirDescripcionFiltros()
    {
        var partes = new List<string>(_consultaCargada?.Descripcion ?? []);
        if (EstadoSeleccionado != EstadoTodos)
            partes.Add($"Estado: {EstadoSeleccionado}");

        return partes.Count == 0 ? "Sin filtros aplicados." : string.Join(" | ", partes) + ".";
    }
```

8. En `ExportarPdfAsync` (L488-519): el atributo pasa a `[RelayCommand(CanExecute = nameof(PuedeExportar))]`; el `/// <summary>` dice "Exporta las filas VISIBLES (las que pasan <see cref="EsVisible"/>), no <see cref="Filas"/>: con un Estado elegido, el PDF no puede traer filas que el usuario no está viendo."; el cuerpo empieza con

```csharp
        var visibles = Filas.Where(f => EsVisible(f)).ToList();
        if (visibles.Count == 0)
            return;

        if (!await AvisoVolumenExportacion.ConfirmarAsync(visibles.Count, _confirmacion))
            return;
```

y `_pdfExporter.Exportar(Filas, ColumnasPdf, metadatos)` pasa a `_pdfExporter.Exportar(visibles, ColumnasPdf, metadatos)`.

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests|FullyQualifiedName~ColumnasPdfContraDtosReales"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~ExportarPdfBotones"
```

Expected: verde (los heredados `ExportarPdfCommand_*` y `Exportar_*` siguen; `ExportarPdfCommand_LaDescripcionDeFiltrosSoloIncluyeLosFiltrosActivos` pone las fechas ANTES de `CargarAsync`, así que quedan en la consulta cargada).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (5a) Quitar ` { Filter = EsVisible }` del constructor → rojo `Estado_RefiltraAlInstante_SinConsultarAlServidor` y `FiltroDeEstado_FiltraEnMemoria`.
- (5b) En `RecalcularVisibles`, quitar `(f => EsVisible(f))` (queda `CantidadVisible = Filas.Count;`, la propiedad) → rojo `Estado_RefiltraAlInstante_…` (`CantidadVisible` 2) y `HayFiltrosAplicados_SinFiltrosEsFalse_ConEstadoEsTrue`. (Spec: "el predicado de Estado en el pie".)
- (5c) En `ExportarPdfAsync`, quitar `.Where(f => EsVisible(f))` → rojo `ExportarPdfCommand_ConEstadoSeleccionado_SoloExportaFilasDeEseEstadoYLoMencionaEnFiltros`. (Spec: "y en el PDF".)
- (5d) En `AplicarFiltroEstado`, quitar las dos líneas `if (FilaSeleccionada is { } seleccionada && !EsVisible(seleccionada))` / `FilaSeleccionada = null;` → rojo `Estado_QueOcultaLaFilaSeleccionada_LimpiaLaSeleccion`.
- (5e) En `ListarYAplicarAsync`, quitar `_consultaCargada = consulta;` → rojo `ExportarPdfCommand_LaDescripcionDeFiltrosSoloIncluyeLosFiltrosActivos` y `HayFiltrosAplicados_ConUnFiltroDeServidorCargado_EsTrue`.
- (5f) En `PuedeExportar`, quitar ` && !Cargando` → rojo `ExportarPdf_MientrasCarga_NoSePuedeEjecutar`.
- (5g) En `PuedeExportar`, quitar ` && !SinPermiso` → rojo `ExportarPdf_ConSinPermiso_NoSePuedeEjecutar`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs
git commit -m "feat(finanzas): el Estado de gastos filtra en memoria y el PDF exporta y rotula lo que se ve"
```

### Task 6: Gastos — recarga automática con debounce; sale Filtrar; Limpiar en una sola recarga (E3, U3, P8)

**Depende de:** la respuesta del usuario a P8 (el plan implementa P8-A).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` (campos tras `_consultaCargada`; `CargarAsync` de la Task 4; `FiltrarCommand` de la Task 4; `LimpiarFiltrosAsync` L310-321)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs` (`Crear` L95-98; `FiltrarCommand_PasaLosFiltrosAlServicio` L177-189; `FiltrarCommand_FechaDesdeYHasta_SinCorrimientoDeDia` L191-209; `FiltrarCommand_Un403_VaciaLaGrillaYNoPropaga` L905-916; tests nuevos al final)

**Interfaces:**
- Consumes: `RecargaConDebounce` (Task 1); `RecargarAsync`, `CapturarConsulta` (Tasks 4 y 5).
- Produces: `internal TimeSpan EsperaDebounce { get; set; }` (300 ms); `internal Task _tareaDebounce`; `private RecargaConDebounce Recarga`; `private bool _recargaArmada`; `private void ProgramarRecarga()`; `LimpiarFiltrosCommand` pasa a `IRelayCommand` sincrónico; `FiltrarCommand` deja de existir.

- [ ] **Step 1: Tests (rojo)**

1. En `Crear`, entre `var vm = new GastosViewModel(…);` (L95-97) y el `return` (L98):

```csharp
        vm.EsperaDebounce = Timeout.InfiniteTimeSpan;   // un filtro que un test cambia no consulta solo; el que lo mide baja la espera
```

2. Adaptar (convención 13) los tres tests que usaban `FiltrarCommand`:

`FiltrarCommand_PasaLosFiltrosAlServicio` (L177-189) pasa a:

```csharp
    [Fact]
    public async Task CambiarFiltros_PasaLosFiltrosAlServicio()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.FechaDesde = new DateTime(2026, 7, 1);
        await vm._tareaDebounce;
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.ProveedorId == 1 && f.FechaDesde != null)), Times.AtLeastOnce);
    }
```

`FiltrarCommand_FechaDesdeYHasta_SinCorrimientoDeDia` (L191-209) pasa a `CambiarFechas_SinCorrimientoDeDia`: mismo comentario, mismo `Verify`, y el cuerpo entre `await vm.CargarAsync();` y el `Verify` queda:

```csharp
        vm.EsperaDebounce = TimeSpan.Zero;
        vm.FechaDesde = new DateTime(2026, 7, 1);
        await vm._tareaDebounce;
        vm.FechaHasta = new DateTime(2026, 7, 31);
        await vm._tareaDebounce;
```

`FiltrarCommand_Un403_VaciaLaGrillaYNoPropaga` (L905-916) pasa a:

```csharp
    [Fact]
    public async Task RecargaPorFiltro_Un403_VaciaLaGrillaYMarcaSinPermiso()
    {
        var (vm, svc, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Vieja") });
        await vm.CargarAsync();
        Assert.Single(vm.Filas);
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).ThrowsAsync(new UnauthorizedAccessException());
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.FechaDesde = new DateTime(2026, 7, 1);
        await vm._tareaDebounce;

        Assert.Empty(vm.Filas);
        Assert.True(vm.SinPermiso);
    }
```

3. Al final de la clase:

```csharp
    // ── Recarga automática por cambio de filtro ─────────────────────────────────

    public static TheoryData<string> FiltrosDeServidor => new() { "Desde", "Hasta", "Proveedor", "Fuente", "Rubro", "LineaPoa" };

    private static void Cambiar(GastosViewModel vm, string filtro)
    {
        switch (filtro)
        {
            case "Desde": vm.FechaDesde = new DateTime(2026, 7, 1); break;
            case "Hasta": vm.FechaHasta = new DateTime(2026, 7, 31); break;
            case "Proveedor": vm.ProveedorSeleccionado = new Proveedor { Id = 1, Nombre = "Barraca X", Activo = true }; break;
            case "Fuente": vm.FuenteSeleccionada = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B", Activo = true }; break;
            case "Rubro": vm.RubroSeleccionado = new RubroGasto { Id = 3, Codigo = 3, Nombre = "Materiales", Activo = true }; break;
            case "LineaPoa": vm.LineaPoaSeleccionada = new LineaPoa { Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true }; break;
        }
    }

    private static bool LoTrae(GastoFiltro f, string filtro) => filtro switch
    {
        "Desde" => f.FechaDesde == new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
        "Hasta" => f.FechaHasta == new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(-1),
        "Proveedor" => f.ProveedorId == 1,
        "Fuente" => f.FuenteFinanciamientoId == 2,
        "Rubro" => f.RubroGastoId == 3,
        "LineaPoa" => f.LineaPoaId == 5,
        _ => false,
    };

    [Theory]
    [MemberData(nameof(FiltrosDeServidor))]
    public async Task CambiarUnFiltroDeServidor_RecargaSolaConEseFiltro(string filtro)
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.EsperaDebounce = TimeSpan.Zero;

        Cambiar(vm, filtro);
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f => LoTrae(f, filtro))), Times.Once);
    }

    [Fact]
    public async Task CambiosRapidos_SoloConsultaElUltimo()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);

        vm.FechaDesde = new DateTime(2026, 7, 1);
        vm.FechaDesde = new DateTime(2026, 7, 2);
        vm.FechaDesde = new DateTime(2026, 7, 3);
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Exactly(2));   // la carga inicial y la última
        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.FechaDesde == new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc))), Times.Once);
    }

    [Fact]
    public async Task Limpiar_JuntaLosCambiosEnUnaSolaRecargaSinFiltros()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        vm.FechaDesde = new DateTime(2026, 7, 1);
        vm.FechaHasta = new DateTime(2026, 7, 31);
        vm.ProveedorSeleccionado = new Proveedor { Id = 1, Nombre = "Barraca X", Activo = true };
        vm.EstadoSeleccionado = "Pagada";
        await vm.CargarAsync();                    // los filtros puestos antes de la carga no programan nada
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);

        vm.LimpiarFiltrosCommand.Execute(null);
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Exactly(2));
        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f => f.Equals(new GastoFiltro()))), Times.Once);
        Assert.Equal(GastosViewModel.EstadoTodos, vm.EstadoSeleccionado);
        Assert.False(vm.HayFiltrosAplicados);
    }

    [Fact]
    public async Task Limpiar_SinFiltrosPuestos_NoConsulta()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        await vm.CargarAsync();
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.LimpiarFiltrosCommand.Execute(null);
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Once);
    }

    [Fact]
    public async Task FiltrarPorLineaPoa_YLaCargaInicial_ConsultanUnaSolaVez()
    {
        var lineaDelCombo = new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 };
        var (vm, svc, _, _, _, _, _) = Crear(lineasPoa: new List<LineaPoa> { lineaDelCombo });
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.FiltrarPorLineaPoa(new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 });
        await vm.CargarAsync();
        await vm._tareaDebounce;

        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.Once);
        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f => f.LineaPoaId == 5)), Times.Once);
        Assert.Same(lineaDelCombo, vm.LineaPoaSeleccionada);
    }

    [Fact]
    public async Task FiltroCambiadoDuranteLaCargaInicial_LaRespuestaInicialNoPisaLaDelFiltro()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var inicial = new TaskCompletionSource<IReadOnlyList<Gasto>>();
        svc.Setup(s => s.ListarAsync(It.Is<GastoFiltro>(f => f.FechaDesde == null))).Returns(inicial.Task);
        svc.Setup(s => s.ListarAsync(It.Is<GastoFiltro>(f => f.FechaDesde != null)))
            .ReturnsAsync(new List<Gasto> { GastoDe(2, "Del filtro") });
        vm.EsperaDebounce = TimeSpan.Zero;

        var carga = vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 7, 1);
        await vm._tareaDebounce;
        inicial.SetResult(new List<Gasto> { GastoDe(1, "De la carga inicial") });
        await carga;

        Assert.Equal("Del filtro", Assert.Single(vm.Filas).Detalle);
    }

    [Fact]
    public void SinBotonFiltrar_LaRecargaEsAutomatica()
    {
        Assert.Null(typeof(GastosViewModel).GetProperty("FiltrarCommand"));
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests"`
Expected: FAIL de compilación (`CS1061: 'GastosViewModel' does not contain a definition for 'EsperaDebounce'`, ídem `_tareaDebounce`). Pegar.

- [ ] **Step 2: Implementación**

En `GastosViewModel.cs`:

1. Después de la propiedad `CantidadVisible` (Task 5):

```csharp

    /// <summary>Espera antes de recargar al cambiar un filtro: varios cambios seguidos (Limpiar repone siete) consultan
    /// una sola vez, con los últimos valores.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista.</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);

    /// <summary>
    /// False hasta que la carga inicial cargó y remapeó los combos: la preselección de <see cref="FiltrarPorLineaPoa"/> y
    /// el remapeo de la línea POA no programan una segunda consulta. Se arma antes de pedir el listado inicial: un filtro
    /// que el usuario cambia mientras espera ese listado programa su recarga y gana por turno.
    /// </summary>
    private bool _recargaArmada;
```

2. Después de `partial void OnEstadoSeleccionadoChanged` (Task 5):

```csharp

    partial void OnFechaDesdeChanged(DateTime? value) => ProgramarRecarga();
    partial void OnFechaHastaChanged(DateTime? value) => ProgramarRecarga();
    partial void OnProveedorSeleccionadoChanged(Proveedor? value) => ProgramarRecarga();
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => ProgramarRecarga();
    partial void OnRubroSeleccionadoChanged(RubroGasto? value) => ProgramarRecarga();
    partial void OnLineaPoaSeleccionadaChanged(LineaPoa? value) => ProgramarRecarga();

    /// <summary>Los seis filtros que viajan al servidor recargan solos tras la espera; un cambio nuevo cancela la anterior.</summary>
    private void ProgramarRecarga()
    {
        if (_recargaArmada)
            _tareaDebounce = Recarga.Programar(RecargarAsync);
    }
```

3. En `CargarAsync`, dentro del lambda, entre el bloque `if (!_combosCargados) { … }` y `await ListarYAplicarAsync(CapturarConsulta(), turno);`:

```csharp
                _recargaArmada = true;
```

(Con P8-B: esta línea va DESPUÉS del `VaciarTrasUn403(turno);` del final de `CargarAsync`, fuera del lambda, y el test de Review Focus 1 se reemplaza por el de la tabla de P8.)

4. Borrar la línea `public IAsyncRelayCommand FiltrarCommand => field ??= ComandoDeGuardado(RecargarAsync);` y ajustar el `/// <summary>` de `RecargarAsync`: "Recarga el listado con los filtros que están en pantalla (por cambio de filtro y tras Anular)…".

5. En `GastosView.axaml` (v1), quitar la línea 128 (`<Button Classes="secondary" Content="Filtrar" Command="{Binding FiltrarCommand}" />`): con compiled bindings (`x:DataType`), un binding a una propiedad que ya no existe es error de build (AVLN2000). Es la única línea de XAML que toca el bloque B (U3: el botón sale); la vista entera se reescribe en la Task 14, después del GATE 1.

6. Reemplazar `LimpiarFiltrosAsync` (L310-321) por:

```csharp
    /// <summary>Repone los siete filtros. No consulta por su cuenta: los cambios de los filtros de servidor se juntan en
    /// una sola recarga por el debounce, y Estado refiltra en memoria.</summary>
    [RelayCommand]
    private void LimpiarFiltros()
    {
        FechaDesde = null;
        FechaHasta = null;
        ProveedorSeleccionado = null;
        FuenteSeleccionada = null;
        RubroSeleccionado = null;
        LineaPoaSeleccionada = null;
        EstadoSeleccionado = EstadoTodos;
    }
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests|FullyQualifiedName~ComandosDeGuardadoGuardian|FullyQualifiedName~ControlPoa|FullyQualifiedName~CalendarioPagos"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~ExportarPdfBotones"
```

Expected: build con 0 errores (sin el punto 5 del Step 2 daría AVLN2000 en `GastosView.axaml`); verde, incluidos los adaptados y los heredados `AnularCommand_ConConfirmacion_AnulaYRecarga` (recarga por `RecargarAsync`) y `ExportarPdfCommand_LaDescripcionDeFiltrosSoloIncluyeLosFiltrosActivos`.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (6a) En `ProgramarRecarga`, quitar las dos líneas del `if` (`if (_recargaArmada)` y `_tareaDebounce = Recarga.Programar(RecargarAsync);`) → rojo los seis casos de `CambiarUnFiltroDeServidor_RecargaSolaConEseFiltro` y `FiltroCambiadoDuranteLaCargaInicial_…`. (Spec: "la programación por cambio de filtro".)
- (6b) Quitar solo la línea `if (_recargaArmada)` → rojo `FiltrarPorLineaPoa_YLaCargaInicial_ConsultanUnaSolaVez` (consulta dos veces).
- (6c) Quitar `_recargaArmada = true;` → rojo `CambiarUnFiltroDeServidor_…` (nunca se arma).
- (6d) Quitar la línea `partial void OnRubroSeleccionadoChanged(RubroGasto? value) => ProgramarRecarga();` → rojo `CambiarUnFiltroDeServidor_RecargaSolaConEseFiltro(filtro: "Rubro")`.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs src/StockApp.Presentation/Views/Finanzas/GastosView.axaml tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs
git commit -m "feat(finanzas): los filtros de gastos recargan solos con debounce y sale el botón Filtrar"
```

### Task 7: Gastos — Anular deshabilitado sobre una fila anulada (E8, P3)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` (`TieneSeleccion` L327; atributo de `AnularAsync` L347)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs` (al final)

**Interfaces:**
- Produces: `private bool PuedeAnular()`; `AnularCommand.CanExecute` = selección no anulada. `EditarCommand` y `PagosCommand` siguen con `TieneSeleccion`.

- [ ] **Step 1: Tests (rojo)**

```csharp
    // ── Anular sobre una fila anulada ───────────────────────────────────────────

    [Fact]
    public async Task Anular_SobreUnaFilaAnulada_EstaDeshabilitado_YEditarNo()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Anulado", activo: false) });
        await vm.CargarAsync();

        vm.FilaSeleccionada = vm.Filas[0];

        Assert.Equal("Anulada", vm.FilaSeleccionada.Estado);
        Assert.False(vm.AnularCommand.CanExecute(null));
        Assert.True(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Anular_SobreUnaFilaActiva_EstaHabilitado()
    {
        var (vm, _, _, _, _, _, _) = Crear(new List<Gasto> { GastoDe(1, "Activo") });
        await vm.CargarAsync();

        vm.FilaSeleccionada = vm.Filas[0];

        Assert.True(vm.AnularCommand.CanExecute(null));
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests.Anular_Sobre"`
Expected: FAIL `Anular_SobreUnaFilaAnulada_EstaDeshabilitado_YEditarNo` ("Assert.False() Failure"); el otro verde. Pegar.

- [ ] **Step 2: Implementación**

Después de `private bool TieneSeleccion() => FilaSeleccionada is not null;` (L327):

```csharp

    /// <summary>Un gasto ya anulado no se vuelve a anular (el servidor respondería "ya está anulado"); Editar sí lo abre,
    /// en solo lectura.</summary>
    private bool PuedeAnular() => FilaSeleccionada is { Gasto.Activo: true };
```

y el atributo de `AnularAsync` (L347) pasa de `[RelayCommand(CanExecute = nameof(TieneSeleccion))]` a `[RelayCommand(CanExecute = nameof(PuedeAnular))]`.

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewModelTests"
```

Expected: verde, incluidos los `AnularCommand_*` heredados (todos sobre filas activas) y `EditarCommand_SinSeleccion_EstaDeshabilitado`.

- [ ] **Step 4: Mutación (QUITANDO)**

- (7a) En `PuedeAnular`, quitar ` Gasto.Activo: true ` dejando `FilaSeleccionada is { }` → rojo `Anular_SobreUnaFilaAnulada_EstaDeshabilitado_YEditarNo`. (Spec: "la condición de `CanExecute` de Anular sobre fila anulada (P3)".)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs
git commit -m "feat(finanzas): Anular queda deshabilitado sobre un gasto ya anulado"
```

### Task 8: Formulario de gasto — carga inicial con sin conexión y sin permiso (E5)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` (`InicializarAsync` L167-228; constantes y override nuevos antes de `InicializarAsync`)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs` (`Crear` L22-82; tests nuevos al final)

**Interfaces:**
- Consumes: `EjecutarCargaProtegidaAsync(Func<Task>, string)`, `AvisaSinConexionEnLinea`, `SinConexion`, `ReintentarCargaCommand`, `SinPermiso`, `MensajeSinPermiso`.
- Produces: `public const string MensajeSinPermisoFormulario = "No tenés permiso para ver finanzas, así que no se puede abrir el gasto."`; en el helper de tests, `Crear(…, Func<Task<IReadOnlyList<Proveedor>>>? proveedoresActivos = null)`.

- [ ] **Step 1: Tests (rojo)**

En `GastoFormViewModelTests.cs`: agregar `using StockApp.ApiClient;`; `Crear` gana el último parámetro opcional `Func<Task<IReadOnlyList<Proveedor>>>? proveedoresActivos = null` y la línea `proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(new List<Proveedor> { … });` (L42-45) pasa a:

```csharp
        proveedores.Setup(p => p.ListarActivasAsync())
            .Returns(proveedoresActivos ?? (() => Task.FromResult<IReadOnlyList<Proveedor>>(new List<Proveedor>
            {
                new() { Id = 1, Nombre = "Barraca X", Activo = true },
            })));
```

Al final de la clase:

```csharp
    // ── Carga inicial: sin conexión y sin permiso ───────────────────────────────

    private static Func<Task<IReadOnlyList<Proveedor>>> FallaUnaVez(Exception falla)
    {
        var intentos = 0;
        return () => ++intentos == 1
            ? Task.FromException<IReadOnlyList<Proveedor>>(falla)
            : Task.FromResult<IReadOnlyList<Proveedor>>(new List<Proveedor> { new() { Id = 1, Nombre = "Barraca X", Activo = true } });
    }

    [Fact]
    public async Task InicializarAsync_ServidorCaido_MarcaSinConexion_SinModalNiExcepcion()
    {
        var (vm, _, _, confirm) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));

        var excepcion = await Record.ExceptionAsync(() => vm.InicializarAsync());

        Assert.Null(excepcion);
        Assert.True(vm.SinConexion);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reintentar_TrasLaCaida_CargaLosCombos()
    {
        var (vm, _, _, _) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));
        await vm.InicializarAsync();

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.ProveedoresDisponibles);
        Assert.Single(vm.FuentesDisponibles);
        Assert.Single(vm.RubrosDisponibles);
    }

    [Fact]
    public async Task Reintentar_EnEdicion_ResuelveLasSeleccionesDelGasto()
    {
        var (vm, _, _, _) = Crear(proveedoresActivos: FallaUnaVez(new ServidorNoDisponibleException()));
        vm.CargarParaEditar(new Gasto
        {
            Id = 9, ProveedorId = 1, Detalle = "Histórico", Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            FuenteFinanciamientoId = 2, RubroGastoId = 3, CondicionPago = CondicionPago.Contado,
        });
        await vm.InicializarAsync();

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.Equal(1, vm.ProveedorSeleccionado?.Id);
        Assert.Equal(2, vm.FuenteSeleccionada?.Id);
        Assert.Equal(3, vm.RubroSeleccionado?.Id);
    }

    [Fact]
    public async Task InicializarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, _, _, confirm) = Crear(proveedoresActivos: FallaUnaVez(new UnauthorizedAccessException("403")));

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(GastoFormViewModel.MensajeSinPermisoFormulario, vm.MensajeSinPermiso);
        confirm.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InicializarAsync_MientrasEspera_CargandoEsTrue()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<Proveedor>>();
        var (vm, _, _, _) = Crear(proveedoresActivos: () => pendiente.Task);

        var carga = vm.InicializarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<Proveedor>());
        await carga;
        Assert.False(vm.Cargando);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests"`
Expected: FAIL de compilación (`MensajeSinPermisoFormulario` no existe). Pegar. Rojo de comportamiento: agregar SOLO la línea de la constante `MensajeSinPermisoFormulario` (sin el override ni el cambio de `InicializarAsync`) y correr de nuevo → FAIL `InicializarAsync_ServidorCaido_…` (la excepción escapa), `Reintentar_*` (ídem), `InicializarAsync_403_…` (`SinPermiso` false: el `catch` silencioso de hoy) y `InicializarAsync_MientrasEspera_…` (`Cargando` false); pegar.

- [ ] **Step 2: Implementación**

En `GastoFormViewModel.cs`:

1. Antes del `/// <summary>Carga los combos…` de `InicializarAsync` (L167):

```csharp
    /// <summary>Estado "Sin permiso" del formulario: la carga de los combos (que exigen finanzas.ver) respondió 403.</summary>
    public const string MensajeSinPermisoFormulario = "No tenés permiso para ver finanzas, así que no se puede abrir el gasto.";

    /// <summary>Sin servidor al abrir, el formulario avisa en línea con Reintentar en vez del modal global: si no, los
    /// combos quedan vacíos para siempre.</summary>
    protected override bool AvisaSinConexionEnLinea => true;

```

2. Reemplazar `InicializarAsync` (L167-228, con su `/// <summary>`) por:

```csharp
    /// <summary>
    /// Carga los combos (y en edición resuelve las selecciones del gasto). La dispara la View (DataContextChanged) y la
    /// repite Reintentar. Protegida: un 403 deja SinPermiso (el aviso global ya lo dio AuthTokenHandler) y un servidor
    /// caído deja SinConexion, sin modal.
    /// </summary>
    public async Task InicializarAsync()
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            // ListarActivasAsync, no ListarTodosAsync (bugfix 2026-08-15): ListarTodosAsync exige GestionarTablasMaestras
            // y un Operador de Finanzas solo tiene VerFinanzas. Los cuatro vienen filtrados a Activo=true del servidor.
            var proveedores = await _proveedoresService.ListarActivasAsync();
            ProveedoresDisponibles.Clear();
            foreach (var p in proveedores)
                ProveedoresDisponibles.Add(p);

            var fuentes = await _fuentesService.ListarActivasAsync();
            FuentesDisponibles.Clear();
            foreach (var f in fuentes)
                FuentesDisponibles.Add(f);

            var rubros = await _rubrosService.ListarActivosAsync();
            RubrosDisponibles.Clear();
            foreach (var r in rubros)
                RubrosDisponibles.Add(r);

            var lineas = await _lineasService.ListarActivasAsync();
            LineasPoaDisponibles.Clear();
            foreach (var l in lineas)
                LineasPoaDisponibles.Add(l);

            if (_gastoParaEditar is not null)
            {
                // Resuelve las selecciones por Id contra los combos; si un maestro fue dado de baja después, cae al objeto
                // de la nav para no perder el dato histórico.
                ProveedorSeleccionado =
                    ProveedoresDisponibles.FirstOrDefault(p => p.Id == _gastoParaEditar.ProveedorId)
                    ?? Agregar(ProveedoresDisponibles, _gastoParaEditar.Proveedor);
                FuenteSeleccionada =
                    FuentesDisponibles.FirstOrDefault(f => f.Id == _gastoParaEditar.FuenteFinanciamientoId)
                    ?? Agregar(FuentesDisponibles, _gastoParaEditar.FuenteFinanciamiento);
                RubroSeleccionado =
                    RubrosDisponibles.FirstOrDefault(r => r.Id == _gastoParaEditar.RubroGastoId)
                    ?? Agregar(RubrosDisponibles, _gastoParaEditar.RubroGasto);
                if (_gastoParaEditar.LineaPoaId is not null)
                    LineaPoaSeleccionada =
                        LineasPoaDisponibles.FirstOrDefault(l => l.Id == _gastoParaEditar.LineaPoaId)
                        ?? Agregar(LineasPoaDisponibles, _gastoParaEditar.LineaPoa);
            }
        }, MensajeSinPermisoFormulario);
        GuardarCommand.NotifyCanExecuteChanged();
    }
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests|FullyQualifiedName~EntradaRegistroFactura"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AccesoRevocado|FullyQualifiedName~Gasto"
```

Expected: verde, incluido el heredado `InicializarAsync_SiProveedoresLanzaUnauthorized_NoPropagaLaExcepcion`.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (8a) Quitar `protected override bool AvisaSinConexionEnLinea => true;` → rojo `InicializarAsync_ServidorCaido_…` y `Reintentar_*`.
- El estado `SinPermiso` del formulario lo custodia la mutación (15d) sobre la vista (Task 15), como pide la spec ("el estado `SinPermiso` del listado y del formulario"); acá el comportamiento lo fija `InicializarAsync_403_…` con su rojo previo pegado en el Step 1.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs
git commit -m "feat(finanzas): el formulario de gasto avisa sin conexión con reintento y sin permiso al cargar"
```

### Task 9: Formulario de gasto — errores por campo y Guardar con las reglas del cliente (E7)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` (usings L1-18; declaración de la clase L29; propiedades observables L68-98; `PuedeGuardar` L237-242; `GuardarAsync` L246-284)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs` (`Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio` L209-220; `Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio` L462-473; tests nuevos al final)

**Interfaces:**
- Consumes: `MensajeNumeroInvalido.Armar(string, string?)`, `FormatoEsUy.TryParseDecimal(string, out decimal, out string?)`; patrón de `IngresoFormViewModel` (4b).
- Produces: `GastoFormViewModel : ViewModelBase, INotifyDataErrorInfo`; constantes `MensajeProveedorObligatorio`, `MensajeFechaObligatoria`, `MensajeDetalleObligatorio`, `MensajeMontoObligatorio`, `MensajeFuenteObligatoria`, `MensajeRubroObligatorio`, `MensajeVencimientoObligatorio`; `public string? ErrorDe(string propiedad)`; `public void MarcarTocado(string propiedad)` (lo llama el code-behind de la Task 15); `HasErrors`, `GetErrors`, `ErrorsChanged`.

- [ ] **Step 1: Tests (rojo)**

1. Adaptar (convención 13) los dos tests de monto, cuyo error deja de ser un mensaje general y pasa a ser del campo:
   - `Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio` (L209-220): `Assert.NotNull(vm.MensajeError);` → `Assert.NotNull(vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto))); Assert.Null(vm.MensajeError);`.
   - `Guardar_MontoConPuntoDecimal_SeRechazaConMensajeClaroSinLlamarAlServicio` (L462-473): `Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 5,4", vm.MensajeError);` → `Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 5,4", vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto))); Assert.Null(vm.MensajeError);`.

2. Al final de la clase (agregar `using System.Collections;` y `using System.ComponentModel;` si faltan):

```csharp
    // ── Errores por campo ───────────────────────────────────────────────────────

    private static readonly string[] Campos =
    [
        nameof(GastoFormViewModel.ProveedorSeleccionado), nameof(GastoFormViewModel.FechaSeleccionada),
        nameof(GastoFormViewModel.Detalle), nameof(GastoFormViewModel.MontoTexto),
        nameof(GastoFormViewModel.FuenteSeleccionada), nameof(GastoFormViewModel.RubroSeleccionado),
        nameof(GastoFormViewModel.FechaVencimientoSeleccionada),
    ];

    [Fact]
    public async Task AlAbrir_NingunCampoTieneError()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.False(vm.HasErrors);
        Assert.All(Campos, c => Assert.Empty(vm.GetErrors(c).Cast<object>()));
    }

    [Fact]
    public async Task TocarDetalleVacio_MuestraSuError()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));

        Assert.Equal(GastoFormViewModel.MensajeDetalleObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio },
            vm.GetErrors(nameof(GastoFormViewModel.Detalle)).Cast<string>());
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public async Task CambiarUnCampo_LevantaErrorsChangedConElNombreDeLaPropiedadBindeada()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.MontoTexto = "abc";

        Assert.Contains(nameof(GastoFormViewModel.MontoTexto), avisos);
        Assert.StartsWith("El monto total no es un número válido.", vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
    }

    public static TheoryData<string> CamposObligatorios => new()
    {
        nameof(GastoFormViewModel.ProveedorSeleccionado), nameof(GastoFormViewModel.FechaSeleccionada),
        nameof(GastoFormViewModel.Detalle), nameof(GastoFormViewModel.MontoTexto),
        nameof(GastoFormViewModel.FuenteSeleccionada), nameof(GastoFormViewModel.RubroSeleccionado),
    };

    private static void Vaciar(GastoFormViewModel vm, string campo)
    {
        switch (campo)
        {
            case nameof(GastoFormViewModel.ProveedorSeleccionado): vm.ProveedorSeleccionado = null; break;
            case nameof(GastoFormViewModel.FechaSeleccionada): vm.FechaSeleccionada = null; break;
            case nameof(GastoFormViewModel.Detalle): vm.Detalle = "  "; break;
            case nameof(GastoFormViewModel.MontoTexto): vm.MontoTexto = ""; break;
            case nameof(GastoFormViewModel.FuenteSeleccionada): vm.FuenteSeleccionada = null; break;
            case nameof(GastoFormViewModel.RubroSeleccionado): vm.RubroSeleccionado = null; break;
        }
    }

    [Theory]
    [MemberData(nameof(CamposObligatorios))]
    public async Task UnCampoObligatorioVacio_GuardarDeshabilitado_YSuErrorVisible(string campo)
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        Assert.True(vm.GuardarCommand.CanExecute(null));

        Vaciar(vm, campo);

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.NotNull(vm.ErrorDe(campo));
    }

    [Fact]
    public async Task MontoIlegible_GuardarDeshabilitado()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MontoTexto = "5.4";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Credito_SinVencimiento_GuardarDeshabilitado_YElErrorDelVencimiento()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.EsCredito = true;
        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));

        Assert.False(vm.GuardarCommand.CanExecute(null));
        Assert.Equal(GastoFormViewModel.MensajeVencimientoObligatorio,
            vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
    }

    [Fact]
    public async Task DestildarCredito_SinVencimiento_GuardarSeHabilita()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));
        var avisos = new List<string?>();
        vm.ErrorsChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.EsCredito = false;

        Assert.True(vm.GuardarCommand.CanExecute(null));
        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
        Assert.Contains(nameof(GastoFormViewModel.FechaVencimientoSeleccionada), avisos);
    }

    [Fact]
    public async Task Contado_SinVencimiento_NoEsError()
    {
        var (vm, _, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        vm.MarcarTocado(nameof(GastoFormViewModel.FechaVencimientoSeleccionada));

        Assert.Null(vm.ErrorDe(nameof(GastoFormViewModel.FechaVencimientoSeleccionada)));
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task IntentarGuardarIncompleto_MarcaTodosLosCampos_SinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await vm.InicializarAsync();

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(GastoFormViewModel.MensajeProveedorObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.ProveedorSeleccionado)));
        Assert.Equal(GastoFormViewModel.MensajeDetalleObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal(GastoFormViewModel.MensajeMontoObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
        Assert.Equal(GastoFormViewModel.MensajeFuenteObligatoria, vm.ErrorDe(nameof(GastoFormViewModel.FuenteSeleccionada)));
        Assert.Equal(GastoFormViewModel.MensajeRubroObligatorio, vm.ErrorDe(nameof(GastoFormViewModel.RubroSeleccionado)));
        Assert.Null(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task Edicion_ConTodoCargado_NingunCampoEnRojo()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaEditar(new Gasto
        {
            Id = 9, ProveedorId = 1, NumeroFactura = "A-9", Detalle = "Histórico", Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            FuenteFinanciamientoId = 2, RubroGastoId = 3, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = DateTime.UtcNow.AddDays(10),
        });

        await vm.InicializarAsync();

        Assert.False(vm.HasErrors);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public void MensajesDeLosCampos_SonLosAprobados()
    {
        Assert.Equal("Elegí el proveedor.", GastoFormViewModel.MensajeProveedorObligatorio);
        Assert.Equal("La fecha del gasto es obligatoria.", GastoFormViewModel.MensajeFechaObligatoria);
        Assert.Equal("El detalle es obligatorio.", GastoFormViewModel.MensajeDetalleObligatorio);
        Assert.Equal("El monto total es obligatorio.", GastoFormViewModel.MensajeMontoObligatorio);
        Assert.Equal("Elegí la fuente de financiamiento.", GastoFormViewModel.MensajeFuenteObligatoria);
        Assert.Equal("Elegí el rubro.", GastoFormViewModel.MensajeRubroObligatorio);
        Assert.Equal("Un gasto a crédito exige fecha de vencimiento.", GastoFormViewModel.MensajeVencimientoObligatorio);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests"`
Expected: FAIL de compilación (`CS1061: 'GastoFormViewModel' does not contain a definition for 'ErrorDe'`, ídem `MarcarTocado`, `HasErrors` y las constantes). Pegar. Todos los tests nuevos y los dos adaptados usan miembros nuevos: el rojo de comportamiento de esta task lo dan las mutaciones (9a) a (9e), que se pegan todas.

- [ ] **Step 2: Implementación**

En `GastoFormViewModel.cs`:

1. Usings: agregar `using System.Collections;` y `using System.ComponentModel;`.
2. Declaración: `public partial class GastoFormViewModel : ViewModelBase, INotifyDataErrorInfo`.
3. Agregar `[NotifyCanExecuteChangedFor(nameof(GuardarCommand))]` a `_fechaSeleccionada` (L81), `_esCredito` (L97) y `_fechaVencimientoSeleccionada` (L98) (cada una en su propia declaración con su `[ObservableProperty]`).
4. Después de `public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();` (L113):

```csharp

    public const string MensajeProveedorObligatorio = "Elegí el proveedor.";
    public const string MensajeFechaObligatoria = "La fecha del gasto es obligatoria.";
    public const string MensajeDetalleObligatorio = "El detalle es obligatorio.";
    public const string MensajeMontoObligatorio = "El monto total es obligatorio.";
    public const string MensajeFuenteObligatoria = "Elegí la fuente de financiamiento.";
    public const string MensajeRubroObligatorio = "Elegí el rubro.";
    public const string MensajeVencimientoObligatorio = "Un gasto a crédito exige fecha de vencimiento.";

    /// <summary>Propiedades bindeadas que se validan en el cliente. ErrorsChanged se levanta con estos nombres: son los
    /// que el binding de cada control le pregunta a GetErrors.</summary>
    private static readonly string[] CamposValidados =
    [
        nameof(ProveedorSeleccionado), nameof(FechaSeleccionada), nameof(Detalle), nameof(MontoTexto),
        nameof(FuenteSeleccionada), nameof(RubroSeleccionado), nameof(FechaVencimientoSeleccionada),
    ];

    /// <summary>Campos que el usuario ya tocó (cambió el valor, salió del campo o intentó guardar). Un campo sin tocar no
    /// muestra su error: el formulario recién abierto no aparece en rojo.</summary>
    private readonly HashSet<string> _tocados = new();

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposValidados.Any(c => ErrorDe(c) is not null);

    /// <summary>El error que la vista muestra debajo del campo: el de su regla, solo si el campo está tocado.</summary>
    public string? ErrorDe(string propiedad) => _tocados.Contains(propiedad) ? ReglaDe(propiedad) : null;

    public IEnumerable GetErrors(string? propertyName)
        => propertyName is not null && ErrorDe(propertyName) is { } error ? new[] { error } : Array.Empty<string>();

    /// <summary>Marca el campo como tocado sin cambiar su valor (la vista lo llama al salir de un campo).</summary>
    public void MarcarTocado(string propiedad)
    {
        if (_tocados.Add(propiedad))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    private void AlCambiarCampo(string propiedad)
    {
        _tocados.Add(propiedad);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    partial void OnProveedorSeleccionadoChanged(Proveedor? value) => AlCambiarCampo(nameof(ProveedorSeleccionado));
    partial void OnFechaSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaSeleccionada));
    partial void OnDetalleChanged(string value) => AlCambiarCampo(nameof(Detalle));
    partial void OnMontoTextoChanged(string value) => AlCambiarCampo(nameof(MontoTexto));
    partial void OnFuenteSeleccionadaChanged(FuenteFinanciamiento? value) => AlCambiarCampo(nameof(FuenteSeleccionada));
    partial void OnRubroSeleccionadoChanged(RubroGasto? value) => AlCambiarCampo(nameof(RubroSeleccionado));
    partial void OnFechaVencimientoSeleccionadaChanged(DateTime? value) => AlCambiarCampo(nameof(FechaVencimientoSeleccionada));

    /// <summary>El vencimiento solo es regla a crédito: tildar o destildar cambia su error sin tocar el campo.</summary>
    partial void OnEsCreditoChanged(bool value) =>
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(FechaVencimientoSeleccionada)));

    /// <summary>
    /// Reglas del cliente, las de antes: proveedor, fuente y rubro elegidos; detalle y monto no vacíos; monto numérico en
    /// es-UY; fecha obligatoria; vencimiento obligatorio solo a crédito. "El monto debe ser mayor a cero", los maestros dados
    /// de baja, la factura duplicada y el cambio de condición de pago son del servidor y salen por la Alerta de error.
    /// </summary>
    private string? ReglaDe(string propiedad) => propiedad switch
    {
        nameof(ProveedorSeleccionado) => ProveedorSeleccionado is null ? MensajeProveedorObligatorio : null,
        nameof(FechaSeleccionada) => FechaSeleccionada is null ? MensajeFechaObligatoria : null,
        nameof(Detalle) => string.IsNullOrWhiteSpace(Detalle) ? MensajeDetalleObligatorio : null,
        nameof(MontoTexto) => ReglaDelMonto(),
        nameof(FuenteSeleccionada) => FuenteSeleccionada is null ? MensajeFuenteObligatoria : null,
        nameof(RubroSeleccionado) => RubroSeleccionado is null ? MensajeRubroObligatorio : null,
        nameof(FechaVencimientoSeleccionada) => EsCredito && FechaVencimientoSeleccionada is null ? MensajeVencimientoObligatorio : null,
        _ => null,
    };

    private string? ReglaDelMonto()
    {
        if (string.IsNullOrWhiteSpace(MontoTexto))
            return MensajeMontoObligatorio;
        return FormatoEsUy.TryParseDecimal(MontoTexto, out _, out var detalle)
            ? null
            : MensajeNumeroInvalido.Armar("El monto total no es un número válido.", detalle);
    }
```

5. Reemplazar `PuedeGuardar` (L237-242) por:

```csharp
    /// <summary>Guardar se habilita con todas las reglas del cliente cumplidas (estén o no tocados los campos) y con los
    /// datos de finanzas a la vista.</summary>
    private bool PuedeGuardar() => !SinPermiso && CamposValidados.All(c => ReglaDe(c) is null);
```

6. En `GuardarAsync`, reemplazar el bloque de validación (desde `MensajeError = null;` L248 hasta el `return;` del vencimiento, L265) por:

```csharp
        MensajeError = null;

        foreach (var campo in CamposValidados)
            MarcarTocado(campo);
        if (CamposValidados.Any(c => ReglaDe(c) is not null))
            return;

        FormatoEsUy.TryParseDecimal(MontoTexto, out var monto, out _);
```

y en el inicializador del `Gasto` (L275), `Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),` → `Fecha = DateTime.SpecifyKind(FechaSeleccionada!.Value.Date, DateTimeKind.Utc),`. El resto de `GuardarAsync` (alta o modificación, sobregiro, factura duplicada) no cambia.

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests|FullyQualifiedName~ComandosDeGuardadoGuardian|FullyQualifiedName~EntradaRegistroFactura"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~AccesoRevocado|FullyQualifiedName~Gasto|FullyQualifiedName~GuardarBloqueado"
```

Expected: verde, incluidos los heredados `Guardar_*`, `CargarParaEditar_*`, `CargarDesdeEntrada_*` (el monto sugerido es válido) y `Un403_ConElFormularioDeGastoAbierto_OcultaGuardarSinBorrarLoTipeado` (v1).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (9a) En `ReglaDe`, quitar `EsCredito && ` del brazo del vencimiento → rojo `Contado_SinVencimiento_NoEsError` y `DestildarCredito_SinVencimiento_GuardarSeHabilita`. (Spec: "el vencimiento condicionado".)
- (9b) En `ErrorDe`, quitar `_tocados.Contains(propiedad) ? ` y ` : null` (queda `=> ReglaDe(propiedad);`) → rojo `AlAbrir_NingunCampoTieneError`.
- (9c) En `PuedeGuardar`, quitar ` && CamposValidados.All(c => ReglaDe(c) is null)` → rojo `UnCampoObligatorioVacio_GuardarDeshabilitado_…` (los seis) y `MontoIlegible_GuardarDeshabilitado`.
- (9d) En `GuardarAsync`, quitar las dos líneas `foreach (var campo in CamposValidados)` / `MarcarTocado(campo);` → rojo `IntentarGuardarIncompleto_MarcaTodosLosCampos_…`.
- (9e) Quitar la línea `partial void OnEsCreditoChanged(bool value) => …;` (y su continuación) → rojo `DestildarCredito_…` (no avisa el cambio del error).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs
git commit -m "feat(finanzas): el formulario de gasto marca los errores en cada campo y bloquea Guardar"
```

### Task 10: Formulario de gasto — solo lectura, gasto anulado sin adjuntos y condición de pago fija en edición (E6, E8, U2, P4)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` (`PuedeRegistrarGastos` L60-62; `_esEdicion` L102-104; `CargarParaEditar` L138-155)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs` (`Crear`, la línea de `PermisosActuales` L69; tests nuevos al final)

**Interfaces:**
- Produces: `[ObservableProperty] bool EsAnulado` (fijado en `CargarParaEditar`); `public bool SoloLectura`; `public bool MostrarSeccionAdjuntos`; `public bool PuedeCambiarCondicionPago`; `public const string TextoCondicionPagoFija = "La condición de pago no se cambia: anulá el gasto y cargalo de nuevo"`. Los usa la vista de la Task 15.

- [ ] **Step 1: Tests (rojo)**

En `Crear`, la línea `session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));` pasa a `session.Setup(s => s.PermisosActuales).Returns(permisos as HashSet<string> ?? new HashSet<string>(permisos ?? Enumerable.Empty<string>()));` (como en `GastosViewModelTests`: un test que pasa su propio `HashSet` puede revocar en caliente). Al final de la clase:

```csharp
    // ── Solo lectura, gasto anulado y condición de pago ─────────────────────────

    private static Gasto GastoExistente(bool activo = true, CondicionPago condicion = CondicionPago.Contado) => new()
    {
        Id = 9, ProveedorId = 1, Detalle = "Histórico", Fecha = DateTime.UtcNow, MontoTotal = 2000m,
        FuenteFinanciamientoId = 2, RubroGastoId = 3, CondicionPago = condicion,
        FechaVencimiento = condicion == CondicionPago.Credito ? DateTime.UtcNow.AddDays(10) : null, Activo = activo,
    };

    [Fact]
    public void ConPermiso_NoEsSoloLectura()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        Assert.False(vm.SoloLectura);
    }

    [Fact]
    public void SinRegistrarGastos_EsSoloLectura()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.VerFinanzas });

        Assert.True(vm.SoloLectura);
    }

    [Fact]
    public void GastoAnulado_EsSoloLecturaAunConPermiso_YOcultaAdjuntos()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente(activo: false));

        Assert.True(vm.EsAnulado);
        Assert.True(vm.SoloLectura);
        Assert.False(vm.MostrarSeccionAdjuntos);
    }

    [Fact]
    public void GastoActivo_MuestraAdjuntos_YNoEsSoloLectura()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente());

        Assert.False(vm.EsAnulado);
        Assert.False(vm.SoloLectura);
        Assert.True(vm.MostrarSeccionAdjuntos);
    }

    [Fact]
    public void CargarParaEditarUnAnulado_AvisaSoloLecturaYAdjuntos()
    {
        var (vm, _, _, _) = Crear();
        var avisos = new List<string?>();
        vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.CargarParaEditar(GastoExistente(activo: false));

        Assert.Contains(nameof(GastoFormViewModel.SoloLectura), avisos);
        Assert.Contains(nameof(GastoFormViewModel.MostrarSeccionAdjuntos), avisos);
    }

    [Fact]
    public void SoloLectura_SeReevaluaConLosPermisosActuales_SinReabrir()
    {
        var permisos = new HashSet<string> { Permisos.VerFinanzas, Permisos.RegistrarGastos };
        var (vm, _, _, _) = Crear(RolUsuario.Operador, permisos);
        Assert.False(vm.SoloLectura);

        permisos.Remove(Permisos.RegistrarGastos);

        Assert.True(vm.SoloLectura);
    }

    [Fact]
    public void Edicion_LaCondicionDePagoNoSeCambia()
    {
        var (vm, _, _, _) = Crear();

        vm.CargarParaEditar(GastoExistente(condicion: CondicionPago.Credito));

        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.True(vm.EsCredito);
    }

    [Fact]
    public void AltaYDesdeEntrada_LaCondicionDePagoSeElige()
    {
        var (alta, _, _, _) = Crear();
        var (desdeEntrada, _, _, _) = Crear();

        desdeEntrada.CargarDesdeEntrada(movimientoId: 61, montoSugerido: 1500m);

        Assert.True(alta.PuedeCambiarCondicionPago);
        Assert.True(desdeEntrada.PuedeCambiarCondicionPago);
    }

    [Fact]
    public void TextoDeLaCondicionFija_EsElAprobado()
    {
        Assert.Equal("La condición de pago no se cambia: anulá el gasto y cargalo de nuevo",
            GastoFormViewModel.TextoCondicionPagoFija);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests"`
Expected: FAIL de compilación (`SoloLectura`, `EsAnulado`, `MostrarSeccionAdjuntos`, `PuedeCambiarCondicionPago`, `TextoCondicionPagoFija` inexistentes). Pegar.

- [ ] **Step 2: Implementación**

En `GastoFormViewModel.cs`:

1. Después de `PuedeRegistrarGastos` (L60-62):

```csharp

    /// <summary>
    /// Formulario de solo lectura: sin permiso de registrar gastos o con el gasto anulado (el servidor no deja modificar un
    /// gasto anulado). Getter sobre la sesión: la revocación en caliente lo re-evalúa (la base notifica todo).
    /// </summary>
    public bool SoloLectura => !PuedeRegistrarGastos || EsAnulado;

    /// <summary>La sección Adjuntos no se muestra con el gasto anulado: su adjunto queda en la base, sin baja, pero no se
    /// ve ni se puede agregar otro (el servidor además lo rechaza).</summary>
    public bool MostrarSeccionAdjuntos => !EsAnulado;

    /// <summary>La condición de pago de un gasto registrado no cambia (el servidor lo rechaza): en edición el tilde queda
    /// deshabilitado; en alta y "desde entrada" se elige.</summary>
    public bool PuedeCambiarCondicionPago => !EsEdicion;

    /// <summary>Explicación debajo del tilde deshabilitado en edición.</summary>
    public const string TextoCondicionPagoFija = "La condición de pago no se cambia: anulá el gasto y cargalo de nuevo";
```

2. Reemplazar la declaración de `_esEdicion` (L102-104) por:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    [NotifyPropertyChangedFor(nameof(PuedeCambiarCondicionPago))]
    private bool _esEdicion;

    /// <summary>El gasto que se editó está anulado (Activo == false): el formulario lo muestra en solo lectura.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoloLectura))]
    [NotifyPropertyChangedFor(nameof(MostrarSeccionAdjuntos))]
    private bool _esAnulado;
```

3. En `CargarParaEditar`, después de `EsEdicion        = true;` (L150):

```csharp
        EsAnulado        = !gasto.Activo;
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewModelTests"
```

Expected: verde (también `CargarParaEditar_InicializaElPanelDeAdjuntosConElGastoId`: el panel se inicializa igual con el gasto anulado).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (10a) En `SoloLectura`, quitar ` || EsAnulado` → rojo `GastoAnulado_EsSoloLecturaAunConPermiso_YOcultaAdjuntos`. (Spec: "la solo lectura con gasto anulado".)
- (10b) En `CargarParaEditar`, quitar `EsAnulado        = !gasto.Activo;` → rojo `GastoAnulado_…` y `CargarParaEditarUnAnulado_AvisaSoloLecturaYAdjuntos`.
- (10c) En `MostrarSeccionAdjuntos`, quitar el `!` → rojo `GastoAnulado_…` y `GastoActivo_MuestraAdjuntos_…`. (Spec: "el ocultamiento de Adjuntos con gasto anulado"; la custodia en la vista es la (15c).)
- (10d) En `PuedeCambiarCondicionPago`, quitar el `!` → rojo `Edicion_LaCondicionDePagoNoSeCambia` y `AltaYDesdeEntrada_LaCondicionDePagoSeElige`. (Spec: "la deshabilitación del tilde 'A crédito' en edición (P4)"; en la vista, (15e).)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs
git commit -m "feat(finanzas): el formulario de gasto queda en solo lectura sin permiso o anulado y fija la condición de pago al editar"
```

---

## Bloque C — Datos fijos, medición y mockups (GATE 1 antes de todo XAML de las vistas)

### Task 11: Datos fijos de Gastos y del formulario

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs`, `DatosGastosTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: los VMs de las Tasks 4 a 10; `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; `DatosProductos.UsuarioAdmin`; `DatosIngresos.UsuarioOperador`.
- Produces: `DatosGastos.Proveedores`, `Fuentes`, `Rubros`, `Lineas`, `Gastos` (6), `Anulado`, `ConAnuladoLista` (7), `AdjuntoDeLaFacturaA4521`; `Listado(Func<GastoFiltro, Task<IReadOnlyList<Gasto>>> responder, bool soloLectura = false, IConfirmacionService? confirmacion = null)`; `Formulario(bool soloLectura = false, IGastoService? servicio = null, IConfirmacionService? confirmacion = null)`; pantallas (→ `ShellMainViewModel`) `ConGastos()`, `Cargando()`, `SinConexion()`, `SinPermiso()`, `SinGastos()`, `SinResultados()`, `SoloLectura()`, `ConAnulado()`, `Nuevo()`, `Editar()`, `EditarCredito()`, `ConErrorDeCampo()`, `ConErrorDelServidor()`, `Credito()`, `FormularioSoloLectura()`, `FormularioAnulado()`; textos de los modales `MensajeConfirmarAnular()` y `MensajeFacturaDuplicada()` (los arma el VM real).

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastosTests.cs`:

```csharp
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de gastos escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla por
/// un texto distinto y el diagnóstico no lo dice. El estado se calcula con el reloj real: los vencimientos están lejos de
/// "hoy" para que no cambie con el día.</summary>
public class DatosGastosTests
{
    private static GastoFila Fila(StockApp.Domain.Entities.Gasto g) => new(g, DateTime.UtcNow);

    [Fact]
    public void Gastos_CuatroEstadosEnElListado_YAnuladaEnElOtro()
    {
        Assert.Equal(new[] { "Pendiente", "Parcial", "Pagada", "Pagada", "Vencida", "Pagada" },
            DatosGastos.Gastos.Select(g => Fila(g).Estado));
        Assert.Equal("Anulada", Fila(DatosGastos.Anulado).Estado);
        Assert.Equal(7, DatosGastos.ConAnuladoLista.Count);
    }

    [Fact]
    public void Gastos_TextosDelMockup()
    {
        var parcial = Fila(DatosGastos.Gastos[1]);
        Assert.Equal("12/10/2026", FechaCortaConverter.Formatear(parcial.Fecha));
        Assert.Equal("Electro Sur S.R.L.", parcial.ProveedorNombre);
        Assert.Equal("$ 310.000,00", FormatoEsUy.Moneda(parcial.MontoTotal));
        Assert.Equal("$ 150.000,00", FormatoEsUy.Moneda(parcial.TotalPagado));
        Assert.Equal("$ 160.000,00", FormatoEsUy.Moneda(parcial.Saldo));
        Assert.Equal("", Fila(DatosGastos.Gastos[2]).LineaPoaNombre);
        Assert.Equal("$ 0,00", FormatoEsUy.Moneda(Fila(DatosGastos.Gastos[2]).Saldo));
    }

    [Fact]
    public async Task Listado_SinFiltros_CargaLosSeis_YElPieDice6Gastos()
    {
        var vm = DatosGastos.Listado(_ => Task.FromResult(DatosGastos.Gastos));

        await vm.CargarAsync();

        Assert.Equal(6, vm.CantidadVisible);
        Assert.Equal(4, vm.ProveedoresDisponibles.Count);
        Assert.False(vm.HayFiltrosAplicados);
    }

    [Fact]
    public async Task SinResultados_ConDesdeDeNoviembre_NoHayFilas_YHayFiltros()
    {
        var shell = DatosGastos.SinResultados();
        var vm = (GastosViewModel)shell.CurrentContent!;

        await vm.CargarAsync();

        Assert.Empty(vm.Filas);
        Assert.True(vm.HayFiltrosAplicados);
    }

    [Fact]
    public void MensajeConfirmarAnular_EsElDelMockup()
    {
        Assert.Equal(
            "¿Confirma anular el gasto \"Materiales para bacheo de calle Zorrilla\" (factura A-0004521 — $ 186.540,00)? " +
            "Esta anulación va a descontar el stock asociado.",
            DatosGastos.MensajeConfirmarAnular());
    }

    [Fact]
    public void MensajeFacturaDuplicada_EsElDelMockup()
    {
        Assert.Equal(
            "La factura 'A-0004521' ya existe para ese proveedor (\"Materiales para bacheo de calle Zorrilla\"). " +
            "¿Asociar la entrada de stock a esa factura?",
            DatosGastos.MensajeFacturaDuplicada());
    }

    [Fact]
    public async Task Formulario_Editar_PrecargaElGastoYSuAdjunto()
    {
        var vm = (GastoFormViewModel)DatosGastos.Editar().CurrentContent!;

        await vm.InicializarAsync();

        Assert.Equal(new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), vm.FechaSeleccionada);
        Assert.Equal("186.540,00", vm.MontoTexto);
        Assert.Equal("Barraca Carmelo S.A.", vm.ProveedorSeleccionado?.Nombre);
        Assert.False(vm.PuedeCambiarCondicionPago);
        Assert.Equal("factura-A-0004521.pdf", Assert.Single(vm.AdjuntosPanel.Items).NombreArchivo);
    }

    [Fact]
    public async Task Formulario_ConErrorDeCampo_DetalleYMontoEnRojo()
    {
        var vm = (GastoFormViewModel)DatosGastos.ConErrorDeCampo().CurrentContent!;

        await vm.InicializarAsync();

        Assert.Equal("El detalle es obligatorio.", vm.ErrorDe(nameof(GastoFormViewModel.Detalle)));
        Assert.Equal("El monto total no es un número válido. Usá coma para los decimales: 1,5",
            vm.ErrorDe(nameof(GastoFormViewModel.MontoTexto)));
    }

    [Fact]
    public async Task Formulario_Anulado_SoloLecturaSinAdjuntos()
    {
        var vm = (GastoFormViewModel)DatosGastos.FormularioAnulado().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.SoloLectura);
        Assert.False(vm.MostrarSeccionAdjuntos);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~DatosGastosTests"` → FAIL de compilación (`DatosGastos` no existe). Pegar.

- [ ] **Step 2: Implementación**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs`:

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/finanzas/gasto*.html (textos fijados en DatosGastosTests). El listado viene como lo
/// devuelve el servidor: fecha descendente. El estado lo calcula GastoFila con el reloj real, así que los vencimientos están
/// lejos de "hoy": 2030 para los que no vencen y 30/09/2026 para el vencido.
/// </summary>
public static class DatosGastos
{
    public static readonly IReadOnlyList<Proveedor> Proveedores =
    [
        new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true },
        new() { Id = 2, Nombre = "Ancap Distribuidora", Activo = true },
        new() { Id = 3, Nombre = "Electro Sur S.R.L.", Activo = true },
        new() { Id = 4, Nombre = "Constructora Oriental S.A.", Activo = true },
    ];

    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "FIGM Literal B", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
        new() { Id = 3, Nombre = "Préstamo BROU", Activo = true },
    ];

    public static readonly IReadOnlyList<RubroGasto> Rubros =
    [
        new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true },
        new() { Id = 2, Codigo = 20, Nombre = "Combustibles", Activo = true },
        new() { Id = 3, Codigo = 30, Nombre = "Alumbrado público", Activo = true },
        new() { Id = 4, Codigo = 40, Nombre = "Obras viales", Activo = true },
    ];

    public static readonly IReadOnlyList<LineaPoa> Lineas =
    [
        new() { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026, Activo = true },
        new() { Id = 2, Nombre = "Alumbrado de plazas", Programa = "Servicios", Ejercicio = 2026, Activo = true },
    ];

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto G(int id, int mes, int dia, int proveedor, string factura, string detalle, int fuente, int rubro,
        int? linea, decimal monto, decimal pagado = 0m, DateTime? vence = null, bool activo = true, bool conStock = false)
    {
        var gasto = new Gasto
        {
            Id = id, ProveedorId = proveedor, Proveedor = Proveedores[proveedor - 1], NumeroFactura = factura, Detalle = detalle,
            Fecha = Dia(2026, mes, dia), MontoTotal = monto,
            FuenteFinanciamientoId = fuente, FuenteFinanciamiento = Fuentes[fuente - 1],
            RubroGastoId = rubro, RubroGasto = Rubros[rubro - 1],
            LineaPoaId = linea, LineaPoa = linea is int l ? Lineas[l - 1] : null,
            CondicionPago = vence is null ? CondicionPago.Contado : CondicionPago.Credito, FechaVencimiento = vence,
            Activo = activo, TieneMovimientosDeStock = conStock,
        };
        if (pagado > 0m)
            gasto.Pagos.Add(new PagoGasto { Id = id * 10, GastoId = id, Fecha = gasto.Fecha, Monto = pagado, Activo = true });
        return gasto;
    }

    /// <summary>Pendiente, Parcial, Pagada, Pagada (con stock: la pregunta de Anular lo avisa), Vencida y Pagada.</summary>
    public static readonly IReadOnlyList<Gasto> Gastos =
    [
        G(7, 10, 14, 4, "A-0002210", "Reparación de cordón cuneta en calle Zorrilla", 3, 4, 1, 420_000m, vence: Dia(2030, 12, 31)),
        G(6, 10, 12, 3, "A-0001190", "Luminarias LED plaza Artigas", 1, 3, 2, 310_000m, pagado: 150_000m, vence: Dia(2030, 11, 30)),
        G(5, 10, 8, 2, "B-0012877", "Combustible de la flota municipal", 2, 2, null, 92_300m, pagado: 92_300m),
        G(4, 10, 3, 1, "A-0004521", "Materiales para bacheo de calle Zorrilla", 1, 1, 1, 186_540m, pagado: 186_540m, conStock: true),
        G(3, 9, 15, 1, "A-0004377", "Arena y pedregullo para caminería rural", 1, 1, 1, 64_800m, vence: Dia(2026, 9, 30)),
        G(2, 9, 2, 3, "A-0001102", "Cableado del alumbrado de la rambla", 2, 3, null, 48_950m, pagado: 48_950m),
    ];

    /// <summary>La carga duplicada del combustible, anulada (su pago automático se dio de baja con ella).</summary>
    public static readonly Gasto Anulado =
        G(8, 10, 10, 2, "B-0012901", "Combustible de la flota municipal (duplicado)", 2, 2, null, 92_300m, activo: false);

    public static readonly IReadOnlyList<Gasto> ConAnuladoLista = [Gastos[0], Gastos[1], Anulado, Gastos[2], Gastos[3], Gastos[4], Gastos[5]];

    public static readonly AdjuntoDto AdjuntoDeLaFacturaA4521 =
        new(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 4, null, new DateTime(2026, 10, 3, 15, 20, 0, DateTimeKind.Utc));

    private static ICurrentSession Sesion(bool soloLectura)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(soloLectura ? RolUsuario.Operador : RolUsuario.Admin);
        sesion.Setup(s => s.UsuarioActual).Returns(soloLectura ? DatosIngresos.UsuarioOperador : DatosProductos.UsuarioAdmin);
        sesion.Setup(s => s.PermisosActuales).Returns(soloLectura ? new HashSet<string> { Permisos.VerFinanzas } : new HashSet<string>());
        return sesion.Object;
    }

    private static (IProveedorService, IFuenteFinanciamientoService, IRubroGastoService, ILineaPoaService) Maestros()
    {
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(Proveedores);
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes);
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(Rubros);
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(Lineas);
        return (proveedores.Object, fuentes.Object, rubros.Object, lineas.Object);
    }

    public static GastosViewModel Listado(Func<GastoFiltro, Task<IReadOnlyList<Gasto>>> responder, bool soloLectura = false,
        IConfirmacionService? confirmacion = null)
    {
        var servicio = new Mock<IGastoService>();
        servicio.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>())).Returns(responder);
        var (proveedores, fuentes, rubros, lineas) = Maestros();
        return new GastosViewModel(servicio.Object, Sesion(soloLectura), proveedores, fuentes, rubros, lineas,
            Mock.Of<INavigationService>(), confirmacion ?? Mock.Of<IConfirmacionService>(), Mock.Of<IServicioGuardadoArchivo>(),
            Mock.Of<IPdfExporter>(), Mock.Of<IServicioAperturaArchivo>());
    }

    /// <summary>Formulario con los cuatro combos, el adjunto de la factura A-0004521 y la fecha del alta fija.</summary>
    public static GastoFormViewModel Formulario(bool soloLectura = false, IGastoService? servicio = null,
        IConfirmacionService? confirmacion = null)
    {
        var sesion = Sesion(soloLectura);
        var adjuntos = new Mock<IAdjuntoService>();
        adjuntos.Setup(a => a.ListarPorGastoAsync(4)).ReturnsAsync(new List<AdjuntoDto> { AdjuntoDeLaFacturaA4521 });
        adjuntos.Setup(a => a.ListarPorGastoAsync(It.Is<int>(id => id != 4))).ReturnsAsync(new List<AdjuntoDto>());
        var panel = new AdjuntosPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion);
        var (proveedores, fuentes, rubros, lineas) = Maestros();
        return new GastoFormViewModel(servicio ?? Mock.Of<IGastoService>(), sesion, proveedores, fuentes, rubros, lineas,
            Mock.Of<INavigationService>(), confirmacion ?? Mock.Of<IConfirmacionService>(), panel)
        {
            FechaSeleccionada = new DateTime(2026, 10, 6),
        };
    }

    private static ShellMainViewModel EnShell(ViewModelBase vm) => DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Gastos", vm);

    private static ShellMainViewModel EnShellSoloLectura(ViewModelBase vm) =>
        DatosShell.Crear(DatosIngresos.UsuarioOperador, [Permisos.VerFinanzas], "Gastos", vm);

    public static ShellMainViewModel ConGastos() => EnShell(Listado(_ => Task.FromResult(Gastos)));
    public static ShellMainViewModel Cargando() => EnShell(Listado(_ => new TaskCompletionSource<IReadOnlyList<Gasto>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Listado(_ => Task.FromException<IReadOnlyList<Gasto>>(new ServidorNoDisponibleException())));
    public static ShellMainViewModel SinPermiso() =>
        EnShell(Listado(_ => Task.FromException<IReadOnlyList<Gasto>>(new UnauthorizedAccessException("403"))));
    public static ShellMainViewModel SinGastos() => EnShell(Listado(_ => Task.FromResult<IReadOnlyList<Gasto>>([])));
    public static ShellMainViewModel SoloLectura() => EnShellSoloLectura(Listado(_ => Task.FromResult(Gastos), soloLectura: true));
    public static ShellMainViewModel ConAnulado() => EnShell(Listado(_ => Task.FromResult(ConAnuladoLista)));

    /// <summary>"Desde" puesto antes de la carga (no programa recarga): el servidor no tiene gastos desde el 01/11/2026.</summary>
    public static ShellMainViewModel SinResultados()
    {
        var vm = Listado(f => Task.FromResult<IReadOnlyList<Gasto>>(f.FechaDesde is null ? Gastos : []));
        vm.FechaDesde = new DateTime(2026, 11, 1);
        return EnShell(vm);
    }

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Gastos[3]);
        return EnShell(vm);
    }

    public static ShellMainViewModel EditarCredito()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Gastos[0]);
        return EnShell(vm);
    }

    /// <summary>Detalle tocado y vacío, y un monto con punto decimal: los dos errores debajo de su campo.</summary>
    public static ShellMainViewModel ConErrorDeCampo()
    {
        var vm = Formulario();
        vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));
        vm.MontoTexto = "1.5";
        return EnShell(vm);
    }

    /// <summary>La regla del servidor ("mayor a cero") en la Alerta de arriba del pie, con el formulario completo.</summary>
    public static ShellMainViewModel ConErrorDelServidor()
    {
        var vm = Formulario();
        vm.ProveedorSeleccionado = Proveedores[1];
        vm.NumeroFactura = "B-0012950";
        vm.Detalle = "Combustible de la flota municipal";
        vm.MontoTexto = "0,00";
        vm.FuenteSeleccionada = Fuentes[1];
        vm.RubroSeleccionado = Rubros[1];
        vm.MensajeError = "El monto total del gasto debe ser mayor a cero.";
        return EnShell(vm);
    }

    /// <summary>Alta a crédito con su vencimiento.</summary>
    public static ShellMainViewModel Credito()
    {
        var vm = Formulario();
        vm.ProveedorSeleccionado = Proveedores[3];
        vm.NumeroFactura = "A-0002301";
        vm.Detalle = "Hormigón para badenes en camino Las Flores";
        vm.MontoTexto = "245.000,00";
        vm.FuenteSeleccionada = Fuentes[2];
        vm.RubroSeleccionado = Rubros[3];
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = new DateTime(2026, 11, 5);
        return EnShell(vm);
    }

    public static ShellMainViewModel FormularioSoloLectura()
    {
        var vm = Formulario(soloLectura: true);
        vm.CargarParaEditar(Gastos[3]);
        return EnShellSoloLectura(vm);
    }

    public static ShellMainViewModel FormularioAnulado()
    {
        var vm = Formulario();
        vm.CargarParaEditar(Anulado);
        return EnShell(vm);
    }

    /// <summary>La pregunta de Anular sobre los materiales para bacheo (con stock), armada por el VM real.</summary>
    public static string MensajeConfirmarAnular()
    {
        string? mensaje = null;
        var confirmacion = new Mock<IConfirmacionService>();
        confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).Callback<string>(m => mensaje = m).ReturnsAsync(false);
        var vm = Listado(_ => Task.FromResult(Gastos), confirmacion: confirmacion.Object);
        vm.CargarAsync().GetAwaiter().GetResult();
        vm.FilaSeleccionada = vm.Filas.Single(f => f.Id == 4);
        vm.AnularCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return mensaje!;
    }

    /// <summary>La pregunta de asociar la entrada a una factura que ya existe, armada por el VM real ("desde entrada").</summary>
    public static string MensajeFacturaDuplicada()
    {
        string? mensaje = null;
        var confirmacion = new Mock<IConfirmacionService>();
        confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).Callback<string>(m => mensaje = m).ReturnsAsync(false);
        var servicio = new Mock<IGastoService>();
        servicio.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-0004521' para ese proveedor."));
        servicio.Setup(s => s.ObtenerPorProveedorYFacturaAsync(1, "A-0004521", null)).ReturnsAsync(Gastos[3]);
        var vm = Formulario(servicio: servicio.Object, confirmacion: confirmacion.Object);
        vm.CargarDesdeEntrada(movimientoId: 61, montoSugerido: 186_540m);
        vm.InicializarAsync().GetAwaiter().GetResult();
        vm.ProveedorSeleccionado = Proveedores[0];
        vm.NumeroFactura = "A-0004521";
        vm.Detalle = "Materiales para bacheo de calle Zorrilla";
        vm.FuenteSeleccionada = Fuentes[0];
        vm.RubroSeleccionado = Rubros[0];
        vm.GuardarCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return mensaje!;
    }
}
```

Confirmar con la CLI `codegraph node UsuarioOperador` que `DatosIngresos.UsuarioOperador` es `public static readonly UsuarioSesion` y con `codegraph node AdjuntoDto` el orden de su constructor `(Id, NombreArchivo, ContentType, TamanoBytes, GastoId, PagoGastoId, FechaAltaUtc)`.

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~DatosGastosTests"` → PASS (9).

Mutación (11a): en `DatosGastos.Gastos[1]`, quitar `pagado: 150_000m, ` → rojo `Gastos_CuatroEstadosEnElListado_YAnuladaEnElOtro` ("Pendiente" en vez de "Parcial") y `Gastos_TextosDelMockup`. Pegar, restaurar con `cp`.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosGastosTests.cs
git commit -m "test(ui-v2): datos fijos de gastos y del formulario de gasto"
```

### Task 12: Medición y mockups de Gastos y del formulario — GATE 1 con el usuario

**Decisiones aplicadas:** D1 a D17 (y P7, P8 como los eligió el usuario).

**Files:**
- Create: `docs/ui-v2/mockups/finanzas/gastos.html`, `gastos-cargando.html`, `gastos-sin-conexion.html`, `gastos-sin-permiso.html`, `gastos-vacio.html`, `gastos-sin-resultados.html`, `gastos-solo-lectura.html`, `gastos-con-anulado.html`, `gasto-nuevo.html`, `gasto-editar.html`, `gasto-editar-credito.html`, `gasto-error-campo.html`, `gasto-error-servidor.html`, `gasto-credito.html`, `gasto-solo-lectura.html`, `gasto-anulado.html`, `gasto-confirmar-anular.html`, `gasto-factura-duplicada.html`
- Modify (local): el ledger (anchos y altos medidos)

**Interfaces:**
- Consumes: `DatosGastos` (Task 11); `ColumnaAuto` y `DataGridColumnasAutoBehavior.AnchoMedido` (3c-1); `VarianteEstadoGastoConverter` NO existe todavía: el tono de cada insignia se escribe en el mockup según P2.
- Produces: las 18 pantallas `finanzas-gasto…` (36 PNG); en el ledger los anchos medidos que usan las Tasks 14 y 15 (si difieren de D2 o D3, reemplazan a esos valores).

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal4d.cs` siguiendo `MedicionTemporal4c` (plan de la 4c, Task 6 Step 1). Con el tema v2 (`Classes="ui2"`), a 1280×760 y 1366×768 (vista de 1040 y 1126), con `--logger "console;verbosity=detailed"`, escribe:
1. **Grilla:** un `DataGrid` de prueba con las columnas de D3 sobre `DatosGastos.ConAnuladoLista` envuelta en `GastoFila`: `AnchoMedido` de cada `ColumnaAuto` (FECHA con `FechaCortaConverter` y `NoCede`; MONTO, PAGADO y SALDO con `MonedaConverter`), el ancho de la insignia "Pendiente" (la más ancha de las cinco) para ESTADO, y si con DETALLE en su piso de 200 la grilla entra en el ancho de la card o hace scroll horizontal (y desde qué columna se corta la vista) a los dos tamaños.
2. **Filtros:** el ancho de cada fila de D2 (Desde 140, Hasta 140, Proveedor 220, Fuente 200 / Rubro 228, Línea POA 220, Estado 140, Limpiar) con el `CampoFormulario` y 8 de separación; que cada fila entre en el ancho útil de la card a 1040 sin cortar "Barraca Carmelo S.A.", "Constructora Oriental S.A.", "Materiales de construcción" ni "Bacheo y caminería rural" en el combo cerrado (si no entran, proponer en el GATE otro reparto; no resolverlo en silencio).
3. **Encabezado:** que el subtítulo de D1 y las seis acciones entran en una línea a 1040.
4. **Formulario:** alto de la card de `gasto-editar` (cuatro secciones + adjuntos con una fila) y del pie fijo, para confirmar que a 1280×760 el contenido scrollea (y cuánto) y que a 1366×768 también; ancho de "A crédito" y del texto de P4 en una línea dentro de 712 (760 menos el padding de 24 por lado).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~MedicionTemporal4d" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger.

- [ ] **Step 2: Mockups**

Partir de `finanzas/ingresos.html`, `ingresos-cargando.html`, `ingresos-sin-conexion.html`, `ingresos-vacio.html`, `ingresos-sin-resultados.html`, `ingresos-solo-lectura.html` y `ingresos-con-inactivos.html` (listado con insignia "Solo lectura", fila inactiva por token, los dos vacíos), `inventario/historial.html` (barra de filtros con `CampoFormulario`, fechas y combos con `.v2-glifo-combo`, grilla ancha con scroll horizontal), `finanzas/ingreso-nuevo.html`, `ingreso-editar.html`, `ingreso-error-campo.html`, `ingreso-error-servidor.html` (formulario v2 de 760, errores debajo del campo), `inventario/ingreso-factura.html` e `ingreso-factura-guardado.html` (pie fijo, card de adjuntos, contenido scrolleado) y `dialogos/confirmacion.html` (modal). Cada mockup:
- `<title>Finanzas: Gastos y facturas[ estado] (UI v2)</title>` (o `Finanzas: Nuevo gasto (UI v2)`, etc.) y `<!-- Datos: DatosGastos.<Pantalla>: … -->`.
- Sidebar con "Gastos y facturas" activo (se oculta en la comparación, pero el usuario aprueba la pantalla entera).
- `gastos.html`: encabezado de D1 (Admin: sin "Solo lectura", Editar, Pagos, Anular, Exportar PDF y Nuevo gasto); card con los filtros de D2 vacíos (Estado "Todos") y la grilla de D3 con las 6 filas de `Gastos` (anchos MEDIDOS como `flex: 0 0 Npx`; importes con `v2-num`; ESTADO con las insignias Pendiente neutra, Parcial advertencia, Pagada éxito, Vencida peligro); pie "6 gastos". Ninguna fila seleccionada.
- `-cargando`: filtros, `.v2-cargando` "Cargando gastos…", sin grilla ni pie.
- `-sin-conexion`: filtros, `.v2-vacio` de error con los textos de la convención 18 y "Reintentar" `secundario`.
- `-sin-permiso`: encabezado y el `.v2-vacio` de error "Sin permiso" / "No tenés permiso para ver los gastos." en lugar de la card.
- `-vacio`: filtros y "Todavía no hay gastos" / "Cuando se registre una factura o un compromiso, va a aparecer acá." con "Nuevo gasto" `secundario`.
- `-sin-resultados`: Desde "01/11/2026" y "Ningún gasto coincide con los filtros" / "Probá con otro período o tocá Limpiar.".
- `-solo-lectura`: operador; en el encabezado solo "Solo lectura" y "Exportar PDF"; grilla y pie como `gastos.html`.
- `-con-anulado`: las 7 filas de `ConAnuladoLista`; la fila anulada en texto terciario con la insignia "Anulada" neutra, SELECCIONADA (fondo de selección de la grilla v2), con "Anular" deshabilitado (tokens de deshabilitado) y "Editar" habilitado; pie "7 gastos".
- `gasto-nuevo.html`: breadcrumb "Gastos y facturas / Nuevo gasto", título "Nuevo gasto", card de 760 con las cuatro secciones de D7 vacías salvo Fecha "06/10/2026", tilde "A crédito" sin tildar y sin vencimiento, sección Adjuntos de D10 con "Guardá el gasto para poder adjuntar."; pie fijo de D11 con Cancelar y Guardar (deshabilitado: faltan los obligatorios).
- `gasto-editar.html`: `Gastos[3]` (Barraca, A-0004521, sin orden de compra, 03/10/2026, Materiales para bacheo…, 186.540,00, FIGM Literal B, Materiales de construcción, Bacheo y caminería rural), tilde deshabilitado sin tildar con el texto de P4, panel de adjuntos con `factura-A-0004521.pdf` (Agregar, Ver y Quitar); SCROLLEADO al final (mismo recurso que `inventario/ingreso-factura-guardado.html`): se ve el final de la card y el pie pegado al borde.
- `gasto-editar-credito.html`: `Gastos[0]` (Constructora Oriental, A-0002210, 14/10/2026, 420.000,00, Préstamo BROU, Obras viales, Bacheo y caminería rural), tilde tildado y deshabilitado, vencimiento "31/12/2030", texto de P4, adjuntos sin archivos ("Sin adjuntos. PDF, JPG o PNG hasta 10 MB."); SCROLLEADO al final.
- `gasto-error-campo.html`: como `gasto-nuevo` con Detalle en rojo ("El detalle es obligatorio.") y Monto "1.5" en rojo ("El monto total no es un número válido. Usá coma para los decimales: 1,5"), Guardar deshabilitado.
- `gasto-error-servidor.html`: el formulario completo de `ConErrorDelServidor` y la `Alerta` Error "El monto total del gasto debe ser mayor a cero." arriba del pie, con el ancho de la card; Guardar habilitado.
- `gasto-credito.html`: el alta a crédito de `Credito()` con el vencimiento "05/11/2026" a la derecha del tilde.
- `gasto-solo-lectura.html`: `Gastos[3]` para el operador: insignia "Solo lectura", campos deshabilitados, sin Guardar (pie con Cancelar), panel con "Ver" y sin Agregar ni Quitar.
- `gasto-anulado.html`: `Anulado`: insignias "Anulado" y "Solo lectura", `Alerta` Info de D12, campos deshabilitados, SIN la sección Adjuntos, pie con Cancelar.
- `gasto-confirmar-anular.html` y `gasto-factura-duplicada.html`: el área cliente del `ConfirmacionDialog` (patrón `dialogos/confirmacion.html`) con los textos de `DatosGastosTests` (`MensajeConfirmarAnular_EsElDelMockup` y `MensajeFacturaDuplicada_EsElDelMockup`) y los botones "Cancelar" / "Sí, continuar".

- [ ] **Step 3: Capturas y verificación de la herramienta**

```bash
for p in gastos gastos-cargando gastos-sin-conexion gastos-sin-permiso gastos-vacio gastos-sin-resultados gastos-solo-lectura gastos-con-anulado \
         gasto-nuevo gasto-editar gasto-editar-credito gasto-error-campo gasto-error-servidor gasto-credito gasto-solo-lectura gasto-anulado \
         gasto-confirmar-anular gasto-factura-duplicada; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/finanzas-gasto*.png | wc -l
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 36 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 4: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal4d.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 5: GATE 1 — aprobación del usuario (PARADA)**

PARAR. Presentar los 36 PNG (LOS DOS TAMAÑOS), la lista D1 a D17, la elección de P7 y P8 y los anchos medidos del ledger. Señalar explícitamente:
- D2: los siete filtros en dos filas (y la alternativa).
- D3: el scroll horizontal de la grilla si la medición lo dio, y ESTADO de ancho fijo.
- D5: los textos de los cinco estados.
- D9: "A crédito" en vez de "Compra a crédito".
- D10 y D11: cómo se ve Adjuntos en alta y la alineación de los botones del pie con la card.
- D12: el texto del aviso de anulado (la spec lo deja para este GATE).
- D13: sin mockup propio para el formulario sin conexión y sin permiso.
- D14: el doble click, que es nuevo.
- D17: la selección se suelta al recargar.

No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups, en los tests de VM de las Tasks 4 a 10 que la fijan (con su rojo y su verde) y en las Tasks 14 y 15, y volver a presentar. Registrar en el ledger: `Mockups 4d: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/finanzas/gastos*.html docs/ui-v2/mockups/finanzas/gasto-*.html
git commit -m "docs(ui-v2): mockups aprobados de gastos y del formulario de gasto"
```

---

## Bloque D — Vistas v2 (arranca solo con los mockups aprobados)

### Task 13: Tonos de la insignia de Estado (P2)

**Files:**
- Create: `src/StockApp.Presentation/Converters/VarianteEstadoGastoConverter.cs`
- Create: `tests/StockApp.Presentation.Tests/Converters/VarianteEstadoGastoConverterTests.cs`

**Interfaces:**
- Consumes: `VarianteInsignia` (`Controls/V2/Variantes.cs`), `EstadoGasto` (`Domain/Enums`).
- Produces: `public sealed class VarianteEstadoGastoConverter : IValueConverter` con `public static readonly VarianteEstadoGastoConverter Instance` (lo usa la columna ESTADO de la Task 14).

- [ ] **Step 1: Test (rojo)**

```csharp
using System.Globalization;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class VarianteEstadoGastoConverterTests
{
    [Theory]
    [InlineData(nameof(EstadoGasto.Vencida), VarianteInsignia.Peligro)]
    [InlineData(nameof(EstadoGasto.Parcial), VarianteInsignia.Advertencia)]
    [InlineData(nameof(EstadoGasto.Pagada), VarianteInsignia.Exito)]
    [InlineData(nameof(EstadoGasto.Pendiente), VarianteInsignia.Neutro)]
    [InlineData(nameof(EstadoGasto.Anulada), VarianteInsignia.Neutro)]
    [InlineData(null, VarianteInsignia.Neutro)]
    public void CadaEstado_TieneSuTono(string? estado, VarianteInsignia esperada)
    {
        var variante = VarianteEstadoGastoConverter.Instance.Convert(estado, typeof(VarianteInsignia), null, CultureInfo.InvariantCulture);

        Assert.Equal(esperada, variante);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~VarianteEstadoGastoConverterTests"` → FAIL de compilación. Pegar.

- [ ] **Step 2: Implementación**

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Estado calculado de un gasto (el texto de <see cref="EstadoGasto"/>) → tono de su Insignia: Vencida peligro, Parcial
/// advertencia, Pagada éxito; Pendiente y Anulada neutras (la fila anulada además va en texto terciario).
/// </summary>
public sealed class VarianteEstadoGastoConverter : IValueConverter
{
    public static readonly VarianteEstadoGastoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        nameof(EstadoGasto.Vencida) => VarianteInsignia.Peligro,
        nameof(EstadoGasto.Parcial) => VarianteInsignia.Advertencia,
        nameof(EstadoGasto.Pagada) => VarianteInsignia.Exito,
        _ => VarianteInsignia.Neutro,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 3: Verde, build y mutación (QUITANDO)**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~VarianteEstadoGastoConverterTests"
```

Expected: 6 PASS. Mutación (13a): quitar el brazo `nameof(EstadoGasto.Parcial) => VarianteInsignia.Advertencia,` → rojo `CadaEstado_TieneSuTono(estado: "Parcial", …)`. Pegar, restaurar.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Presentation/Converters/VarianteEstadoGastoConverter.cs tests/StockApp.Presentation.Tests/Converters/VarianteEstadoGastoConverterTests.cs
git commit -m "feat(ui-v2): tono de la insignia según el estado del gasto"
```

### Task 14: Gastos y facturas v2 (listado)

**Depende de:** Tasks 4 a 7, 11 a 13 (mockups aprobados).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/GastosView.axaml` (completo), `GastosView.axaml.cs` (completo)
- Create: `tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs`, `tests/StockApp.Presentation.UiTests/V2/GastosViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastosComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L65, L112, `VistasMigradasAV2` L195-217)
- Modify, solo si fallan y con el motivo en el ledger (convención 13): `GastosViewTests.cs`, `GastosViewColumnasAnchoTests.cs`, `GrillasSinRecorteBarridoTests.cs` (escenario "Gastos"), `ExportarPdfBotonesGuardianTests.cs`, `AccesoRevocadoPantallasFinanzasTests.cs`

**Interfaces:**
- Consumes: `GastosViewModel` (`FechaDesde`, `FechaHasta`, `ProveedorSeleccionado`, `FuenteSeleccionada`, `RubroSeleccionado`, `LineaPoaSeleccionada`, `EstadoSeleccionado`, `EstadosDisponibles`, `ProveedoresDisponibles`, `FuentesDisponibles`, `RubrosDisponibles`, `LineasPoaDisponibles`, `Filas`, `FilasView`, `FilaSeleccionada`, `CantidadVisible`, `HayFiltrosAplicados`, `Cargando`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`, `ReintentarCargaCommand`, `PuedeRegistrarGastos`, `PuedeRegistrarPagos`, `PuedeVerFinanzas`, `NuevoCommand`, `EditarCommand`, `PagosCommand`, `AnularCommand`, `ExportarPdfCommand`, `LimpiarFiltrosCommand`, `EsperaDebounce`, `_tareaDebounce`, `MensajeSinPermisoGastos`, `EstadoTodos`, `FiltrarPorLineaPoa`); `GastoFila` (`Gasto`, `Id`, `Fecha`, `ProveedorNombre`, `NumeroFactura`, `Detalle`, `FuenteNombre`, `RubroNombre`, `LineaPoaNombre`, `MontoTotal`, `TotalPagado`, `Saldo`, `Estado`); `VarianteEstadoGastoConverter` (Task 13); `FechaCortaConverter`, `MonedaConverter`, `ConteoConverter`, `ColumnaAuto`, `TooltipSiSeCorta`, `CalendarDatePickerFechaBehavior`; fakes de UiTests (`SesionFake`, `NavigationRecorderFake`, `NavigationServiceFake`, `ConfirmacionServiceFake`, `InfoAppFake`, `AuthServiceFake`, `PreferenciasSidebarFake`, `FuenteFinanciamientoServiceFake`, `RubroGastoServiceFake`, `LineaPoaServiceFake`, `ServicioAperturaArchivoFake`, `GuardadoSinUsoFake`, `PdfExporterSinUsoFake`), `MontajeV2`, `ArbolVisual`.
- Produces: `GastosFake` y `ProveedoresFake` (los usa también la Task 15); en la vista, `x:Name` `Encabezado`, `InsigniaSoloLectura`, `BotonEditar`, `BotonPagos`, `BotonAnular`, `BotonExportarPdf`, `BotonNuevo`, `EstadoSinPermiso`, `Tarjeta`, `Filtros`, `CampoDesde`, `SelectorDesde`, `CampoHasta`, `SelectorHasta`, `CampoProveedor`, `ComboProveedor`, `CampoFuente`, `ComboFuente`, `CampoRubro`, `ComboRubro`, `CampoLineaPoa`, `ComboLineaPoa`, `CampoEstado`, `ComboEstado`, `BotonLimpiar`, `PieConteo`, `IndicadorCarga`, `EstadoSinConexion`, `BotonReintentar`, `EstadoSinGastos`, `BotonNuevoVacio`, `EstadoSinResultados`, `Grilla`.

- [ ] **Step 1: Fakes**

`tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>IGastoService con los tipos reales. ListarAsync graba cada filtro y responde, en este orden: Falla, Pendiente
/// o Lista. Alta y Modificar graban o lanzan FallaAlGuardar (lo que ApiErrores reconstruye de un 409 o un 400).</summary>
internal sealed class GastosFake : IGastoService
{
    public List<Gasto> Lista { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<Gasto>>? Pendiente { get; set; }
    public Exception? Falla { get; set; }
    public List<GastoFiltro> Filtros { get; } = new();
    public List<int> Anulados { get; } = new();
    public List<Gasto> Altas { get; } = new();
    public List<Gasto> Modificaciones { get; } = new();
    public Exception? FallaAlGuardar { get; set; }

    public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        Filtros.Add(filtro);
        return Falla is not null ? Task.FromException<IReadOnlyList<Gasto>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<Gasto>>(Lista.ToList());
    }

    public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false)
    {
        Anulados.Add(id);
        return Task.CompletedTask;
    }

    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<ResultadoGastoDto>(FallaAlGuardar);
        Altas.Add(gasto);
        return Task.FromResult(new ResultadoGastoDto(99, null));
    }

    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
    {
        if (FallaAlGuardar is not null)
            return Task.FromException<ResultadoGastoDto>(FallaAlGuardar);
        Modificaciones.Add(gasto);
        return Task.FromResult(new ResultadoGastoDto(gasto.Id, null));
    }

    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) =>
        Task.FromResult<Gasto?>(null);

    public Task<Gasto> ObtenerPorIdAsync(int id) => throw new NotSupportedException();
    public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
    public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
    public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
}

/// <summary>IProveedorService que puede fallar: la carga de combos del listado y del formulario empieza por proveedores.</summary>
internal sealed class ProveedoresFake : IProveedorService
{
    public List<Proveedor> Activos { get; set; } = new();
    public Exception? Falla { get; set; }

    public Task<IReadOnlyList<Proveedor>> ListarActivasAsync() =>
        Falla is not null ? Task.FromException<IReadOnlyList<Proveedor>>(Falla)
            : Task.FromResult<IReadOnlyList<Proveedor>>(Activos.ToList());

    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => throw new NotSupportedException();
    public Task<int> AltaAsync(Proveedor proveedor) => throw new NotSupportedException();
    public Task ModificarAsync(Proveedor proveedor) => throw new NotSupportedException();
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
}
```

- [ ] **Step 2: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/GastosViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Gastos y facturas v2 con la View REAL: estructura, filtros que recargan solos, grilla con la insignia de
/// Estado por tono y la fila anulada por token, pie, estados de pantalla, gates con un Operador de permisos mixtos, Anular
/// sobre una fila anulada, doble click y revocación en caliente por el camino del shell.</summary>
public class GastosViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");
    private static readonly Proveedor Barraca = new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };
    private static readonly RubroGasto Materiales = new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true };
    private static readonly LineaPoa Bacheo = new() { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026, Activo = true };

    private static DateTime Dia(int anio, int mes, int dia) => new(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto G(int id, string detalle, decimal monto, decimal pagado = 0m, DateTime? vence = null, bool activo = true)
    {
        var gasto = new Gasto
        {
            Id = id, ProveedorId = 1, Proveedor = Barraca, NumeroFactura = $"A-000{id}", Detalle = detalle,
            Fecha = Dia(2026, 10, 6), MontoTotal = monto, FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm,
            RubroGastoId = 1, RubroGasto = Materiales, LineaPoaId = 1, LineaPoa = Bacheo,
            CondicionPago = vence is null ? CondicionPago.Contado : CondicionPago.Credito, FechaVencimiento = vence, Activo = activo,
        };
        if (pagado > 0m)
            gasto.Pagos.Add(new PagoGasto { Id = id * 10, GastoId = id, Fecha = gasto.Fecha, Monto = pagado, Activo = true });
        return gasto;
    }

    private static Gasto Pendiente() => G(1, "Reparación de cordón cuneta", 420_000m, vence: Dia(2030, 12, 31));
    private static Gasto Parcial() => G(2, "Luminarias LED plaza Artigas", 310_000m, pagado: 150_000m, vence: Dia(2030, 11, 30));
    private static Gasto Pagada() => G(3, "Combustible de la flota", 92_300m, pagado: 92_300m);
    private static Gasto Vencida() => G(4, "Arena y pedregullo", 64_800m, vence: Dia(2026, 9, 30));
    private static Gasto Anulada() => G(5, "Combustible duplicado", 92_300m, activo: false);

    private static SesionFake ConTodo() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos);
    private static SesionFake SoloVer() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, GastosView Vista, GastosViewModel Vm, GastosFake Servicio, NavigationRecorderFake Navegacion);

    private static GastosViewModel NuevoVm(GastosFake servicio, ICurrentSession sesion, INavigationService navegacion) =>
        new(servicio, sesion, new ProveedoresFake { Activos = [Barraca] }, new FuenteFinanciamientoServiceFake([Figm]),
            new RubroGastoServiceFake([Materiales]), new LineaPoaServiceFake([Bacheo]), navegacion,
            new ConfirmacionServiceFake(), new GuardadoSinUsoFake(), new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake())
        { EsperaDebounce = TimeSpan.Zero };

    private static Montaje Montar(GastosFake servicio, ICurrentSession? sesion = null, double ancho = 1040,
        Action<GastosViewModel>? antesDeMostrar = null)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = NuevoVm(servicio, sesion ?? ConTodo(), navegacion);
        antesDeMostrar?.Invoke(vm);
        var vista = new GastosView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static Montaje Montar(params Gasto[] gastos) => Montar(new GastosFake { Lista = gastos.ToList() });

    private static T Control<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);

    private static DataGridRow Fila(Montaje m, int idGasto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridRow>()
            .Single(r => Visible(r) && r.DataContext is GastoFila f && f.Id == idGasto);

    private static TextBlock CeldaConTexto(Montaje m, string texto) =>
        Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == texto && Visible(t));

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Headless no lleva clickCount: el doble toque es la secuencia de dos clicks seguidos, con el puntero que
    /// entra primero (como en IngresosViewV2Tests).</summary>
    private static void DobleClick(Window ventana, Control control)
    {
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        Dispatcher.UIThread.RunJobs();
        for (var i = 0; i < 2; i++)
        {
            ventana.MouseDown(p, MouseButton.Left);
            ventana.MouseUp(p, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_NuevoGastoEsElUnicoPrimario()
    {
        var m = Montar(Pendiente());
        var encabezado = Control<EncabezadoPagina>(m, "Encabezado");

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Gastos y facturas", encabezado.Titulo);
        Assert.Equal("Facturas y compromisos de pago de la caja municipal.", encabezado.Subtitulo);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario") && Visible(b));
        Assert.Same(Control<Button>(m, "BotonNuevo"), Assert.Single(primarios));
        Assert.Same(m.Vm.NuevoCommand, Control<Button>(m, "BotonNuevo").Command);
        foreach (var nombre in new[] { "BotonEditar", "BotonPagos", "BotonAnular", "BotonExportarPdf" })
            Assert.Contains("secundario", Control<Button>(m, nombre).Classes);
    }

    [AvaloniaFact]
    public void Filtros_SieteCamposConSusEtiquetas_LimpiarSecundario_SinFiltrar()
    {
        var m = Montar(Pendiente());

        Assert.Equal(new[] { "Desde", "Hasta", "Proveedor", "Fuente", "Rubro", "Línea POA", "Estado" },
            new[] { "CampoDesde", "CampoHasta", "CampoProveedor", "CampoFuente", "CampoRubro", "CampoLineaPoa", "CampoEstado" }
                .Select(n => Control<CampoFormulario>(m, n).Etiqueta));
        var limpiar = Control<Button>(m, "BotonLimpiar");
        Assert.Contains("secundario", limpiar.Classes);
        Assert.Same(m.Vm.LimpiarFiltrosCommand, limpiar.Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Filtrar");
        Assert.Equal(GastosViewModel.EstadoTodos, Control<ComboBox>(m, "ComboEstado").SelectedItem);
    }

    [AvaloniaFact]
    public void Grilla_OnceColumnasEnOrdenYEnMayusculas_Ordenable()
    {
        var grilla = Control<DataGrid>(Montar(Pendiente()), "Grilla");

        Assert.Equal(new object?[] { "FECHA", "PROVEEDOR", "DETALLE", "MONTO", "PAGADO", "SALDO", "ESTADO", "FACTURA", "FUENTE", "RUBRO", "LÍNEA POA" },
            grilla.Columns.Select(c => c.Header).ToArray());
        Assert.True(grilla.CanUserSortColumns);
    }

    [AvaloniaFact]
    public void Celdas_FechaEnDdMmAaaa_EImportesEnEsUy()
    {
        var m = Montar(new GastosFake { Lista = [Parcial()] }, ancho: 1600);   // ancha: las once columnas a la vista
        var textos = Fila(m, 2).GetVisualDescendants().OfType<TextBlock>().Where(Visible).Select(t => t.Text).ToList();

        Assert.Contains("06/10/2026", textos);
        Assert.Contains("$ 310.000,00", textos);
        Assert.Contains("$ 150.000,00", textos);
        Assert.Contains("$ 160.000,00", textos);
        Assert.Contains("Bacheo y caminería rural", textos);
    }

    [AvaloniaFact]
    public void Estado_CadaInsigniaConElTonoAprobado()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente(), Parcial(), Pagada(), Vencida(), Anulada()] }, ancho: 1600);

        var esperado = new Dictionary<int, (string Texto, VarianteInsignia Variante)>
        {
            [1] = ("Pendiente", VarianteInsignia.Neutro),
            [2] = ("Parcial", VarianteInsignia.Advertencia),
            [3] = ("Pagada", VarianteInsignia.Exito),
            [4] = ("Vencida", VarianteInsignia.Peligro),
            [5] = ("Anulada", VarianteInsignia.Neutro),
        };
        foreach (var (id, (texto, variante)) in esperado)
        {
            var insignia = Assert.Single(Fila(m, id).GetVisualDescendants().OfType<Insignia>());
            Assert.Equal(texto, insignia.Texto);
            Assert.Equal(variante, insignia.Variante);
        }
    }

    [AvaloniaFact]
    public void FilaAnulada_TextoTerciarioPorToken_YLaActivaNo()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente(), Anulada()] }, ancho: 1600);

        var textos = Fila(m, 5).GetVisualDescendants().OfType<TextBlock>()
            .Where(t => Visible(t) && !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null).ToList();
        Assert.NotEmpty(textos);
        Assert.All(textos, t => Assert.Equal(Terciario, MontajeV2.ColorDe(t.Foreground)));
        Assert.NotEqual(Terciario, MontajeV2.ColorDe(CeldaConTexto(m, "Reparación de cordón cuneta").Foreground));
    }

    [AvaloniaFact]
    public void PieConteo_CuentaLasVisibles_YSigueAlEstado()
    {
        var m = Montar(Pendiente(), Pagada());
        Assert.Equal("2 gastos", Control<TextBlock>(m, "PieConteo").Text);

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Pagada";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("1 gasto", Control<TextBlock>(m, "PieConteo").Text);
    }

    // ── Estados de pantalla ─────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Cargando_IndicadorConLosFiltros_SinVaciosNiGrilla()
    {
        var m = Montar(new GastosFake { Pendiente = new TaskCompletionSource<IReadOnlyList<Gasto>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new GastosFake { Lista = [Pendiente()], Falla = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<BarraFiltros>(m, "Filtros")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));

        servicio.Falla = null;
        Clickear(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard_YExportarDeshabilitado()
    {
        var m = Montar(new GastosFake { Falla = new UnauthorizedAccessException("403") });

        var estado = Control<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal(GastosViewModel.MensajeSinPermisoGastos, estado.Mensaje);
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Control<Button>(m, "BotonExportarPdf").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void SinGastos_VacioConNuevoGasto_YElOperadorSinPermisoNoLoVe()
    {
        var conPermiso = Montar(new GastosFake());
        Assert.True(Visible(Control<EstadoVacio>(conPermiso, "EstadoSinGastos")));
        Assert.False(Visible(Control<EstadoVacio>(conPermiso, "EstadoSinResultados")));
        Assert.True(Visible(Control<Button>(conPermiso, "BotonNuevoVacio")));

        var soloVer = Montar(new GastosFake(), SoloVer());
        Assert.True(Visible(Control<EstadoVacio>(soloVer, "EstadoSinGastos")));
        Assert.False(Visible(Control<Button>(soloVer, "BotonNuevoVacio")));
    }

    [AvaloniaFact]
    public void EstadoSinCoincidencias_SinResultados_NoElVacioDeSinGastos()
    {
        var m = Montar(Pagada());

        Control<ComboBox>(m, "ComboEstado").SelectedItem = "Vencida";
        Dispatcher.UIThread.RunJobs();

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public void FiltroDeServidorSinCoincidencias_SinResultados()
    {
        var m = Montar(new GastosFake(), antesDeMostrar: vm => vm.FechaDesde = new DateTime(2026, 11, 1));

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinResultados")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinGastos")));
    }

    // ── Gates (Operador de permisos mixtos, nunca Admin) ─────────────────────────

    [AvaloniaFact]
    public void OperadorSoloVer_OcultaNuevoEditarAnularYPagos_MuestraSoloLectura_YExportarSigue()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, SoloVer());

        Assert.True(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
        foreach (var nombre in new[] { "BotonNuevo", "BotonEditar", "BotonAnular", "BotonPagos" })
            Assert.False(Visible(Control<Button>(m, nombre)), nombre);
        Assert.True(Visible(Control<Button>(m, "BotonExportarPdf")));
    }

    [AvaloniaFact]
    public void OperadorConPagosSinGastos_VePagos_YNoAnular()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] },
            new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarPagos));

        Assert.True(Visible(Control<Button>(m, "BotonPagos")));
        Assert.False(Visible(Control<Button>(m, "BotonAnular")));
        Assert.True(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
    }

    [AvaloniaFact]
    public void OperadorConGastosSinPagos_VeNuevoYAnular_NoVePagosNiSoloLectura()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] },
            new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos));

        Assert.False(Visible(Control<Insignia>(m, "InsigniaSoloLectura")));
        Assert.True(Visible(Control<Button>(m, "BotonNuevo")));
        Assert.True(Visible(Control<Button>(m, "BotonEditar")));
        Assert.True(Visible(Control<Button>(m, "BotonAnular")));
        Assert.False(Visible(Control<Button>(m, "BotonPagos")));
    }

    // ── Acciones ────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ClickEnUnaFilaAnulada_AnularDeshabilitado_EditarHabilitado()
    {
        var m = Montar(Pendiente(), Anulada());

        Clickear(m.Ventana, Fila(m, 5));

        Assert.Equal(5, m.Vm.FilaSeleccionada?.Id);
        Assert.False(Control<Button>(m, "BotonAnular").IsEffectivelyEnabled);
        Assert.True(Control<Button>(m, "BotonEditar").IsEffectivelyEnabled);

        Clickear(m.Ventana, Fila(m, 1));

        Assert.True(Control<Button>(m, "BotonAnular").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnUnaFila_ConPermiso_Edita()
    {
        var m = Montar(Pendiente());

        DobleClick(m.Ventana, CeldaConTexto(m, "Reparación de cordón cuneta"));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Equal(typeof(GastoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_OperadorSoloVer_NoEdita()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, SoloVer());

        DobleClick(m.Ventana, CeldaConTexto(m, "Reparación de cordón cuneta"));
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task DobleClick_EnElEncabezado_NoEdita()
    {
        var m = Montar(Pendiente());
        Clickear(m.Ventana, Fila(m, 1));   // con una fila seleccionada, Editar se podría ejecutar
        var encabezado = Control<DataGrid>(m, "Grilla").GetVisualDescendants().OfType<DataGridColumnHeader>()
            .First(h => Equals(h.Content, "DETALLE"));

        DobleClick(m.Ventana, encabezado);
        await (m.Vm.EditarCommand.ExecutionTask ?? Task.CompletedTask);

        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task ElegirUnProveedorEnElCombo_RecargaSolaConEseProveedor()
    {
        var m = Montar(Pendiente());

        Control<ComboBox>(m, "ComboProveedor").SelectedItem = Barraca;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2, m.Servicio.Filtros.Count);
        Assert.Equal(1, m.Servicio.Filtros[^1].ProveedorId);
    }

    [AvaloniaFact]
    public async Task Limpiar_ConClickReal_ReponeLosFiltrosYRecargaUnaVez()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, antesDeMostrar: vm => vm.FechaDesde = new DateTime(2026, 10, 1));
        Assert.Equal(Dia(2026, 10, 1), m.Servicio.Filtros[0].FechaDesde);

        Clickear(m.Ventana, Control<Button>(m, "BotonLimpiar"));
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Null(Control<CalendarDatePicker>(m, "SelectorDesde").SelectedDate);
        Assert.Equal(2, m.Servicio.Filtros.Count);
        Assert.Null(m.Servicio.Filtros[^1].FechaDesde);
    }

    [AvaloniaFact]
    public void DesdeControlPoa_ElComboMuestraLaLineaPreseleccionada_YConsultaUnaVez()
    {
        var m = Montar(new GastosFake { Lista = [Pendiente()] }, antesDeMostrar: vm =>
            vm.FiltrarPorLineaPoa(new LineaPoa { Id = 1, Nombre = "Bacheo y caminería rural", Programa = "Infraestructura", Ejercicio = 2026 }));

        Assert.Same(Bacheo, Control<ComboBox>(m, "ComboLineaPoa").SelectedItem);
        Assert.Equal(1, Assert.Single(m.Servicio.Filtros).LineaPoaId);
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    private static (Window Ventana, ShellMainViewModel Shell, SesionFake Sesion, HashSet<string> Tras, GastosViewModel Vm)
        MontarEnShell(string[] antes, string[] tras)
    {
        var sesion = new SesionFake(RolUsuario.Operador, antes);
        var permisosTras = new HashSet<string>(tras);
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, permisosTras), new PreferenciasSidebarFake());
        var vm = NuevoVm(new GastosFake { Lista = [Pendiente()] }, sesion, new NavigationRecorderFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return (ventana, shell, sesion, permisosTras, vm);
    }

    private static async Task Disparar403(ShellMainViewModel shell, SesionFake sesion, HashSet<string> tras)
    {
        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static T EnGastos<T>(Window ventana, string nombre) where T : Control =>
        MontajeV2.Parte<T>(ventana.GetVisualDescendants().OfType<GastosView>().Single(), nombre);

    [AvaloniaFact]
    public async Task RevocacionDePagos_DentroDelShell_OcultaPagos_YElRestoSigue()
    {
        var (ventana, shell, sesion, tras, _) = MontarEnShell(
            [Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos],
            [Permisos.VerFinanzas, Permisos.RegistrarGastos]);
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonPagos")), "Precondición: con finanzas.pagos, Pagos se ve.");

        await Disparar403(shell, sesion, tras);

        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonPagos")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonNuevo")));
    }

    [AvaloniaFact]
    public async Task RevocacionDeVerFinanzas_DentroDelShell_SinPermisoEnLugarDeLaCard()
    {
        var (ventana, shell, sesion, tras, vm) = MontarEnShell(
            [Permisos.VerFinanzas, Permisos.RegistrarGastos], [Permisos.RegistrarGastos]);
        Assert.Single(vm.Filas);

        await Disparar403(shell, sesion, tras);

        Assert.True(ArbolVisual.EsVisibleEnArbol(EnGastos<EstadoVacio>(ventana, "EstadoSinPermiso")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<TarjetaContenido>(ventana, "Tarjeta")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(EnGastos<Button>(ventana, "BotonExportarPdf")));
    }
}
```

(Con P7-B, `RevocacionDeVerFinanzas_DentroDelShell_SinPermisoEnLugarDeLaCard` no se escribe.)

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastosViewV2Tests"`
Expected: la gran mayoría FAIL contra la vista v1 (`InvalidOperationException: Sequence contains no matching element` al buscar `Encabezado`, `CampoDesde`, `Grilla`, etc. por `x:Name`); los que no dependen de nombres nuevos pueden fallar por aserción. Pegar el resumen (cantidad de rojos y dos mensajes de ejemplo).

- [ ] **Step 3: Vista v2**

`src/StockApp.Presentation/Views/Finanzas/GastosView.axaml` completo (anchos de D2 y D3; si el ledger de la Task 12 midió otros, van los del ledger):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:b="using:StockApp.Presentation.Behaviors"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.GastosView"
             x:DataType="vm:GastosViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (listado §5.1). Espejo: docs/ui-v2/mockups/finanzas/gastos*.html. Editar, Pagos y Anular actúan sobre la
         fila seleccionada; Anular no se puede sobre una fila ya anulada. Doble click en una fila equivale a Editar
         (code-behind). Sin finanzas.gastos se ocultan Nuevo, Editar y Anular y aparece Solo lectura; Pagos depende de
         finanzas.pagos y Exportar PDF de finanzas.ver. Cambiar un filtro recarga sola tras una espera corta; Estado filtra
         en memoria, al instante. Cargando, sin servidor y los dos vacíos dentro de la card con los filtros a mano; tras un
         403, Sin permiso en lugar de la card. La fila anulada va en texto terciario por token, nunca con opacidad. -->
    <DockPanel Margin="28,24">

        <v2:EncabezadoPagina x:Name="Encabezado" DockPanel.Dock="Top" Titulo="Gastos y facturas"
                             Subtitulo="Facturas y compromisos de pago de la caja municipal.">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <v2:Insignia x:Name="InsigniaSoloLectura" Texto="Solo lectura" Variante="Neutro" VerticalAlignment="Center"
                             IsVisible="{Binding !PuedeRegistrarGastos}" />
                <Button x:Name="BotonEditar" Classes="secundario" Content="Editar" Command="{Binding EditarCommand}"
                        IsVisible="{Binding PuedeRegistrarGastos}" />
                <Button x:Name="BotonPagos" Classes="secundario" Content="Pagos" Command="{Binding PagosCommand}"
                        IsVisible="{Binding PuedeRegistrarPagos}" />
                <Button x:Name="BotonAnular" Classes="secundario" Content="Anular" Command="{Binding AnularCommand}"
                        IsVisible="{Binding PuedeRegistrarGastos}" />
                <Button x:Name="BotonExportarPdf" Classes="secundario" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}"
                        IsVisible="{Binding PuedeVerFinanzas}" />
                <Button x:Name="BotonNuevo" Classes="primario" Content="Nuevo gasto" Command="{Binding NuevoCommand}"
                        IsVisible="{Binding PuedeRegistrarGastos}" />
            </StackPanel>
        </v2:EncabezadoPagina>

        <v2:EstadoVacio x:Name="EstadoSinPermiso" DockPanel.Dock="Top" UseLayoutRounding="False"
                        Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                        IsVisible="{Binding SinPermiso}" />

        <v2:TarjetaContenido x:Name="Tarjeta" IsVisible="{Binding !SinPermiso}">
            <DockPanel>
                <!-- Siete filtros en dos filas: los seis primeros viajan al servidor y recargan con espera; Estado filtra en
                     memoria. Limpiar repone todo y no consulta por su cuenta. -->
                <v2:BarraFiltros x:Name="Filtros" DockPanel.Dock="Top" MostrarBusqueda="False">
                    <StackPanel>
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <v2:CampoFormulario x:Name="CampoDesde" Etiqueta="Desde">
                                <CalendarDatePicker x:Name="SelectorDesde" Width="140" SelectedDate="{Binding FechaDesde}"
                                                    PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                                    CustomDateFormatString="dd/MM/yyyy"
                                                    b:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoHasta" Etiqueta="Hasta">
                                <CalendarDatePicker x:Name="SelectorHasta" Width="140" SelectedDate="{Binding FechaHasta}"
                                                    PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                                    CustomDateFormatString="dd/MM/yyyy"
                                                    b:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoProveedor" Etiqueta="Proveedor">
                                <ComboBox x:Name="ComboProveedor" Width="220" PlaceholderText="Todos"
                                          ItemsSource="{Binding ProveedoresDisponibles}" SelectedItem="{Binding ProveedorSeleccionado}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="e:Proveedor"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoFuente" Etiqueta="Fuente">
                                <ComboBox x:Name="ComboFuente" Width="200" PlaceholderText="Todas"
                                          ItemsSource="{Binding FuentesDisponibles}" SelectedItem="{Binding FuenteSeleccionada}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="e:FuenteFinanciamiento"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                        </StackPanel>
                        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,12,0,0">
                            <v2:CampoFormulario x:Name="CampoRubro" Etiqueta="Rubro">
                                <ComboBox x:Name="ComboRubro" Width="228" PlaceholderText="Todos"
                                          ItemsSource="{Binding RubrosDisponibles}" SelectedItem="{Binding RubroSeleccionado}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="e:RubroGasto"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoLineaPoa" Etiqueta="Línea POA">
                                <ComboBox x:Name="ComboLineaPoa" Width="220" PlaceholderText="Todas"
                                          ItemsSource="{Binding LineasPoaDisponibles}" SelectedItem="{Binding LineaPoaSeleccionada}">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="e:LineaPoa"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoEstado" Etiqueta="Estado">
                                <ComboBox x:Name="ComboEstado" Width="140"
                                          ItemsSource="{Binding EstadosDisponibles}" SelectedItem="{Binding EstadoSeleccionado}" />
                            </v2:CampoFormulario>
                            <Button x:Name="BotonLimpiar" Classes="secundario" Content="Limpiar" VerticalAlignment="Bottom"
                                    Command="{Binding LimpiarFiltrosCommand}" />
                        </StackPanel>
                    </StackPanel>
                </v2:BarraFiltros>

                <TextBlock x:Name="PieConteo" DockPanel.Dock="Bottom" Classes="pie-conteo" Margin="0,12,0,0"
                           Text="{Binding CantidadVisible, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='gasto|gastos'}">
                    <TextBlock.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </TextBlock.IsVisible>
                </TextBlock>

                <v2:EstadoCargando x:Name="IndicadorCarga" DockPanel.Dock="Top" Texto="Cargando gastos…">
                    <v2:EstadoCargando.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" />
                            <Binding Path="Filas.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoCargando.IsVisible>
                </v2:EstadoCargando>

                <v2:EstadoVacio x:Name="EstadoSinConexion" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="No se pudo conectar con el servidor"
                                Mensaje="Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar."
                                EsError="True" IsVisible="{Binding SinConexion}">
                    <Button x:Name="BotonReintentar" Classes="secundario" Content="Reintentar" Command="{Binding ReintentarCargaCommand}" />
                </v2:EstadoVacio>

                <!-- Sin datos y sin ningún filtro: el botón es secundario porque el primario ya está en el encabezado. -->
                <v2:EstadoVacio x:Name="EstadoSinGastos" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="Todavía no hay gastos"
                                Mensaje="Cuando se registre una factura o un compromiso, va a aparecer acá.">
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="Filas.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                            <Binding Path="HayFiltrosAplicados" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
                    <Button x:Name="BotonNuevoVacio" Classes="secundario" Content="Nuevo gasto" Command="{Binding NuevoCommand}"
                            IsVisible="{Binding PuedeRegistrarGastos}" />
                </v2:EstadoVacio>

                <!-- Algún filtro (de servidor o Estado) activo y ninguna fila a la vista. -->
                <v2:EstadoVacio x:Name="EstadoSinResultados" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="Ningún gasto coincide con los filtros"
                                Mensaje="Probá con otro período o tocá Limpiar.">
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                            <Binding Path="HayFiltrosAplicados" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
                </v2:EstadoVacio>

                <!-- Cada TextBlock de celda lleva Classes.inactivo con el gasto anulado (texto terciario por token, nunca
                     opacidad). Las de texto libre miden su contenido sobre todas las filas y, si ceden, se cortan con
                     tooltip; DETALLE es la proporcional. ESTADO es un conjunto cerrado: ancho fijo medido. La fecha es
                     calendario (DateOnly): FechaCortaConverter no la pasa a hora local. -->
                <DataGrid x:Name="Grilla" ItemsSource="{Binding FilasView}" SelectedItem="{Binding FilaSeleccionada}"
                          DoubleTapped="OnFilaDobleClick" IsReadOnly="True" CanUserSortColumns="True" CanUserResizeColumns="True"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="CantidadVisible" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </DataGrid.IsVisible>
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="FECHA" Width="Auto" SortMemberPath="Fecha"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding Fecha, Converter={x:Static conv:FechaCortaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Gasto.Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="PROVEEDOR" Width="Auto" SortMemberPath="ProveedorNombre" b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding ProveedorNombre}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding ProveedorNombre}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="DETALLE" Width="*" MinWidth="200" SortMemberPath="Detalle">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding Detalle}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Detalle}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="MONTO" Width="Auto" CellStyleClasses="num" SortMemberPath="MontoTotal"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding MontoTotal, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Gasto.Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="PAGADO" Width="Auto" CellStyleClasses="num" SortMemberPath="TotalPagado"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding TotalPagado, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Gasto.Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="SALDO" Width="Auto" CellStyleClasses="num" SortMemberPath="Saldo"
                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding Saldo, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Gasto.Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="ESTADO" Width="90" SortMemberPath="Estado">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <v2:Insignia Texto="{Binding Estado}" HorizontalAlignment="Left" VerticalAlignment="Center"
                                                 Variante="{Binding Estado, Converter={x:Static conv:VarianteEstadoGastoConverter.Instance}}" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="FACTURA" Width="Auto" SortMemberPath="NumeroFactura" b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding NumeroFactura}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding NumeroFactura}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="FUENTE" Width="Auto" SortMemberPath="FuenteNombre" b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding FuenteNombre}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding FuenteNombre}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="RUBRO" Width="Auto" SortMemberPath="RubroNombre" b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding RubroNombre}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding RubroNombre}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="LÍNEA POA" Width="Auto" SortMemberPath="LineaPoaNombre" b:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="vm:GastoFila">
                                    <TextBlock Text="{Binding LineaPoaNombre}" Classes.inactivo="{Binding !Gasto.Activo}"
                                               TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding LineaPoaNombre}"
                                               b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                    </DataGrid.Columns>
                </DataGrid>
            </DockPanel>
        </v2:TarjetaContenido>
    </DockPanel>

</UserControl>
```

`GastosView.axaml.cs` completo:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastosView : UserControl
{
    public GastosView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara cuando la navegación asigna el
        // DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastosViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// Doble click en una fila = Editar, con el mismo gate que el botón (oculto sin finanzas.gastos). Solo sobre una fila:
    /// un doble click en el encabezado ordena, no edita.
    /// </summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is GastosViewModel { PuedeRegistrarGastos: true } vm && vm.EditarCommand.CanExecute(null))
            vm.EditarCommand.Execute(null);
    }
}
```

`GuardianDePatronTests.cs`: quitar `[InlineData(typeof(GastosView), "Gastos y facturas", "FINANZAS")]` (L65) y `typeof(GastosView),` de `VistasDeLaTanda` (L112); agregar `typeof(GastosView),` al final de `VistasMigradasAV2` (después de `typeof(LibroCajaView),`). El reemplazo del chequeo de `HeaderVista` es `Raiz_TieneUi2_EncabezadoConTituloYSubtitulo_NuevoGastoEsElUnicoPrimario` (convención 13; anotarlo en el ledger).

- [ ] **Step 4: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~DataGridColumnas"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~VarianteEstadoGasto"
```

Expected: verde. Heredados que pueden pedir ajuste (convención 13: se ADAPTAN, nunca se borran; cada ajuste con su rojo y su motivo en el ledger): `GastosViewTests` (busca "Nuevo gasto"/"Editar" por texto: el primero del árbol es el del encabezado, sigue verde), `GastosViewColumnasAnchoTests` (11 columnas ≥ 60 px a 1300: ESTADO mide 90 y las medidas no bajan de su título), `GrillasSinRecorteBarridoTests` escenario "Gastos" (si una columna medida aparece recortada, la medición de la Task 12 estaba mal: re-medir, no relajar), `ExportarPdfBotonesGuardianTests.Montar_GastosView_BotonExportarPdf_ExisteYEstaBindeadoAlComando` (busca por texto, sigue), `AccesoRevocadoPantallasFinanzasTests` (`Un403_ConGastosAbierto_…`, `Un403_QueQuitaVerFinanzas_OcultaExportarYVaciaLaGrilla`, `SinVerFinanzas_DesdeElMontaje_ExportarNoSeVe`: buscan por texto o por `Command`, siguen).

- [ ] **Step 5: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastosComparacionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Gastos y facturas dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class GastosComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    /// <summary>La fila anulada seleccionada (Anular deshabilitado). Se selecciona con la vista ya cargada: la carga que dispara
    /// la vista reemplaza las filas.</summary>
    private static void OcultarSidebarYSeleccionarLaAnulada(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var vm = (GastosViewModel)((ShellMainViewModel)vista.DataContext!).CurrentContent!;
        vm.FilaSeleccionada = vm.Filas.Single(f => !f.Gasto.Activo);
    }

    [AvaloniaFact] public void Gastos_IgualAlMockup() => Comparar(DatosGastos.ConGastos(), "finanzas/gastos.html");
    [AvaloniaFact] public void Gastos_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConGastos(), "finanzas/gastos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosCargando_IgualAlMockup() => Comparar(DatosGastos.Cargando(), "finanzas/gastos-cargando.html");
    [AvaloniaFact] public void GastosCargando_IgualAlMockup_1366x768() => Comparar(DatosGastos.Cargando(), "finanzas/gastos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinConexion_IgualAlMockup() => Comparar(DatosGastos.SinConexion(), "finanzas/gastos-sin-conexion.html");
    [AvaloniaFact] public void GastosSinConexion_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinConexion(), "finanzas/gastos-sin-conexion.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinPermiso_IgualAlMockup() => Comparar(DatosGastos.SinPermiso(), "finanzas/gastos-sin-permiso.html");
    [AvaloniaFact] public void GastosSinPermiso_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinPermiso(), "finanzas/gastos-sin-permiso.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosVacio_IgualAlMockup() => Comparar(DatosGastos.SinGastos(), "finanzas/gastos-vacio.html");
    [AvaloniaFact] public void GastosVacio_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinGastos(), "finanzas/gastos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSinResultados_IgualAlMockup() => Comparar(DatosGastos.SinResultados(), "finanzas/gastos-sin-resultados.html");
    [AvaloniaFact] public void GastosSinResultados_IgualAlMockup_1366x768() => Comparar(DatosGastos.SinResultados(), "finanzas/gastos-sin-resultados.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosSoloLectura_IgualAlMockup() => Comparar(DatosGastos.SoloLectura(), "finanzas/gastos-solo-lectura.html");
    [AvaloniaFact] public void GastosSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosGastos.SoloLectura(), "finanzas/gastos-solo-lectura.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastosConAnulado_IgualAlMockup() =>
        Comparar(DatosGastos.ConAnulado(), "finanzas/gastos-con-anulado.html", preparar: OcultarSidebarYSeleccionarLaAnulada);
    [AvaloniaFact] public void GastosConAnulado_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.ConAnulado(), "finanzas/gastos-con-anulado.html", TamanoLienzo.Municipio, OcultarSidebarYSeleccionarLaAnulada);
}
```

Si la grilla hace scroll horizontal, las columnas fuera de vista no se reportan; un texto cortado a la vista se declara en `recortesPermitidos` SOLO si el mockup aprobado también lo corta (precedente: `ControlPoaComparacionTests.CorteDelMockupA1280`).

Run: `scripts/ui-v2/comparar.sh Gastos`. Expected: 16 filas APROBADAS con el umbral por defecto. Si una no entra: diagnosticar con `teselas.py`/`tinta.py` (copiados a `$SCRATCH/diag/`, nunca se commitean), corregir la vista o el mockup (≤ 4 px no visible se anota; mayor vuelve al usuario); un `UmbralAprobado` mayor solo en un GATE con el usuario.

- [ ] **Step 6: Mutaciones (QUITANDO)**

Sobre `GastosView.axaml` / `.axaml.cs`, cada una con `cp` al `$SCRATCH`, build, el filtro de `GastosViewV2Tests` (y el de la clase heredada que se nombra), rojo pegado y restauración:
- (14a) Quitar `IsVisible="{Binding !SinPermiso}"` de `Tarjeta` → rojo `Un403_SinPermisoEnLugarDeLaCard_YExportarDeshabilitado`. (Spec: "el estado `SinPermiso` del listado".)
- (14b) Quitar `IsVisible="{Binding PuedeRegistrarGastos}"` de `BotonNuevo` → rojo `OperadorSoloVer_…`. (Spec: "el `IsVisible` de cada gate".)
- (14c) Ídem de `BotonEditar` → rojo `OperadorSoloVer_…`.
- (14d) Ídem de `BotonAnular` → rojo `OperadorSoloVer_…` y `OperadorConPagosSinGastos_…`.
- (14e) Quitar `IsVisible="{Binding PuedeRegistrarPagos}"` de `BotonPagos` → rojo `OperadorConGastosSinPagos_…` y `RevocacionDePagos_DentroDelShell_…`.
- (14f) Quitar `IsVisible="{Binding PuedeVerFinanzas}"` de `BotonExportarPdf` → rojo `AccesoRevocadoPantallasFinanzasTests.SinVerFinanzas_DesdeElMontaje_ExportarNoSeVe` y `Un403_QueQuitaVerFinanzas_OcultaExportarYVaciaLaGrilla`.
- (14g) Quitar `IsVisible="{Binding !PuedeRegistrarGastos}"` de `InsigniaSoloLectura` → rojo `OperadorConGastosSinPagos_VeNuevoYAnular_NoVePagosNiSoloLectura`.
- (14h) Quitar `Classes.inactivo="{Binding !Gasto.Activo}"` del `TextBlock` de DETALLE → rojo `FilaAnulada_TextoTerciarioPorToken_YLaActivaNo`.
- (14i) Quitar el atributo `Variante="{Binding Estado, Converter=…}"` de la `Insignia` de ESTADO → rojo `Estado_CadaInsigniaConElTonoAprobado`.
- (14j) En `OnFilaDobleClick`, quitar ` { PuedeRegistrarGastos: true }` → rojo `DobleClick_OperadorSoloVer_NoEdita`.
- (14k) En `OnFilaDobleClick`, quitar las dos líneas del `if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>…` / `return;` → rojo `DobleClick_EnElEncabezado_NoEdita`.
- (14l) En el `MultiBinding` de `EstadoSinGastos`, quitar `<Binding Path="HayFiltrosAplicados" Converter="{x:Static BoolConverters.Not}" />` → rojo `EstadoSinCoincidencias_SinResultados_NoElVacioDeSinGastos` y `FiltroDeServidorSinCoincidencias_SinResultados`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/GastosView.axaml src/StockApp.Presentation/Views/Finanzas/GastosView.axaml.cs tests/StockApp.Presentation.UiTests/V2/GastosFakes.cs tests/StockApp.Presentation.UiTests/V2/GastosViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastosComparacionTests.cs
git commit -m "feat(ui-v2): gastos y facturas v2 con filtros que recargan solos, insignias de estado y estados de pantalla"
```

(Sumar al `git add` los tests heredados que se hayan adaptado en el Step 4.)

### Task 15: Formulario de gasto v2 con pie fijo, solo lectura y adjuntos

**Depende de:** Tasks 8 a 12 y 14 (`GastosFake`, `ProveedoresFake`).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` (completo), `GastoFormView.axaml.cs` (completo)
- Create: `tests/StockApp.Presentation.UiTests/V2/GastoFormViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastoFormComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L64, L111, `VistasMigradasAV2`)
- Modify, solo si fallan y con el motivo en el ledger: `AccesoRevocadoPantallasFinanzasTests.cs` (los tres tests del formulario: buscan "Guardar", "Agregar" y "Quitar" por texto y los textos tipeados en `TextBox`, deberían seguir verdes)

**Interfaces:**
- Consumes: `GastoFormViewModel` (`Titulo`, `CancelarCommand`, `GuardarCommand`, `ProveedoresDisponibles`, `FuentesDisponibles`, `RubrosDisponibles`, `LineasPoaDisponibles`, `ProveedorSeleccionado`, `NumeroFactura`, `NumeroOrden`, `FechaSeleccionada`, `Detalle`, `Destino`, `MontoTexto`, `FuenteSeleccionada`, `RubroSeleccionado`, `LineaPoaSeleccionada`, `EsCredito`, `FechaVencimientoSeleccionada`, `EsEdicion`, `EsAnulado`, `SoloLectura`, `MostrarSeccionAdjuntos`, `PuedeCambiarCondicionPago`, `TextoCondicionPagoFija`, `MensajeError`, `AdjuntosPanel`, `SinPermiso`, `MensajeSinPermiso`, `SinConexion`, `ReintentarCargaCommand`, `MarcarTocado`, `MensajeSinPermisoFormulario`, `MensajeDetalleObligatorio`); `AdjuntosPanelView` v2 (4a, sin cambios); `CalendarDatePickerFechaBehavior`; fakes de la Task 14 y de UiTests (`AdjuntoServiceFake`, `ServicioSeleccionArchivoFake`, `ServicioAperturaArchivoFake`, `FuenteFinanciamientoServiceFake`, `RubroGastoServiceFake`, `LineaPoaServiceFake`, `SesionFake`, `NavigationRecorderFake`, `NavigationServiceFake`, `ConfirmacionServiceFake`, `InfoAppFake`, `AuthServiceFake`, `PreferenciasSidebarFake`).
- Produces: en la vista, `x:Name` `Raiz`, `Desplazamiento`, `ColumnaEncabezado`, `Encabezado`, `InsigniaAnulado`, `InsigniaSoloLectura`, `EstadoSinPermiso`, `ColumnaFormulario`, `Tarjeta`, `ErrorCarga`, `AlertaSinConexion`, `BotonReintentar`, `AlertaAnulado`, `Campos`, `CampoProveedor`, `ComboProveedor`, `CampoNumeroFactura`, `CajaNumeroFactura`, `CampoOrdenCompra`, `CajaOrdenCompra`, `CampoFecha`, `SelectorFecha`, `CampoDetalle`, `CajaDetalle`, `CampoDestino`, `CajaDestino`, `CampoMonto`, `CajaMonto`, `CampoFuente`, `ComboFuente`, `CampoRubro`, `ComboRubro`, `CampoLineaPoa`, `ComboLineaPoa`, `CheckCredito`, `CampoVencimiento`, `SelectorVencimiento`, `TextoCondicionFija`, `SeccionAdjuntos`, `PanelAdjuntos`, `AdjuntosTrasGuardar`, `TextoGuardarParaAdjuntar`, `FilaAlertaError`, `AlertaError`, `Pie`, `BotonCancelar`, `BotonGuardar`.

- [ ] **Step 1: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/GastoFormViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de gasto v2 con la View REAL: breadcrumb en los tres modos, card de 760 con cuatro secciones,
/// errores por campo, pie fijo fuera del scroll, tilde a crédito fijo en edición, solo lectura (permiso y gasto anulado),
/// adjuntos, carga inicial sin servidor y con 403, y revocación en caliente por el camino del shell.</summary>
public class GastoFormViewV2Tests
{
    private static readonly Proveedor Barraca = new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true };
    private static readonly FuenteFinanciamiento Figm = new() { Id = 1, Nombre = "FIGM Literal B", Activo = true };
    private static readonly RubroGasto Materiales = new() { Id = 1, Codigo = 10, Nombre = "Materiales de construcción", Activo = true };

    private static Gasto Existente(bool activo = true, CondicionPago condicion = CondicionPago.Contado) => new()
    {
        Id = 4, ProveedorId = 1, Proveedor = Barraca, NumeroFactura = "A-0004521", Detalle = "Materiales para bacheo",
        Fecha = new DateTime(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc), MontoTotal = 186_540m,
        FuenteFinanciamientoId = 1, FuenteFinanciamiento = Figm, RubroGastoId = 1, RubroGasto = Materiales,
        CondicionPago = condicion,
        FechaVencimiento = condicion == CondicionPago.Credito ? new DateTime(2030, 12, 31, 0, 0, 0, DateTimeKind.Utc) : null,
        Activo = activo,
    };

    private static AdjuntoServiceFake ConAdjuntos(int cantidad)
    {
        var adjuntos = new AdjuntoServiceFake();
        for (var i = 1; i <= cantidad; i++)
            adjuntos.Items.Add(new AdjuntoDto(i, $"factura-A-000452{i}.pdf", "application/pdf", 182_400, 4, null,
                new DateTime(2026, 10, 3, 15, 20, 0, DateTimeKind.Utc)));
        return adjuntos;
    }

    private static SesionFake ConPermiso() => new(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);
    private static SesionFake SoloVer() => new(RolUsuario.Operador, Permisos.VerFinanzas);

    private sealed record Montaje(Window Ventana, GastoFormView Vista, GastoFormViewModel Vm, GastosFake Servicio, NavigationRecorderFake Navegacion);

    private static GastoFormViewModel NuevoVm(GastosFake servicio, ICurrentSession sesion, INavigationService navegacion,
        ProveedoresFake? proveedores = null, AdjuntoServiceFake? adjuntos = null)
    {
        var confirmacion = new ConfirmacionServiceFake();
        var panel = new AdjuntosPanelViewModel(adjuntos ?? new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), confirmacion, sesion);
        return new GastoFormViewModel(servicio, sesion, proveedores ?? new ProveedoresFake { Activos = [Barraca] },
            new FuenteFinanciamientoServiceFake([Figm]), new RubroGastoServiceFake([Materiales]),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()), navegacion, confirmacion, panel);
    }

    private static Montaje Montar(Action<GastoFormViewModel>? preparar = null, ICurrentSession? sesion = null,
        double ancho = 1040, double alto = 760, ProveedoresFake? proveedores = null, AdjuntoServiceFake? adjuntos = null,
        GastosFake? servicio = null)
    {
        servicio ??= new GastosFake();
        var navegacion = new NavigationRecorderFake();
        var vm = NuevoVm(servicio, sesion ?? ConPermiso(), navegacion, proveedores, adjuntos);
        preparar?.Invoke(vm);
        var vista = new GastoFormView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Parte<T>(Montaje m, string nombre) where T : Control => MontajeV2.Parte<T>(m.Vista, nombre);
    private static bool Visible(Control c) => TopLevel.GetTopLevel(c) is not null && ArbolVisual.EsVisibleEnArbol(c);
    private static Rect EnVentana(Window ventana, Control c) => new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);
    private static IEnumerable<string> Errores(Control c) => DataValidationErrors.GetErrors(c)?.Select(e => e?.ToString() ?? "") ?? [];

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Completar(GastoFormViewModel vm)
    {
        vm.ProveedorSeleccionado = Barraca;
        vm.Detalle = "Materiales para bacheo";
        vm.MontoTexto = "186.540,00";
        vm.FuenteSeleccionada = Figm;
        vm.RubroSeleccionado = Materiales;
        Dispatcher.UIThread.RunJobs();
    }

    // ── Estructura ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Raiz_TieneUi2_BreadcrumbConElTituloDelVm_EnLosTresModos()
    {
        var alta = Montar();
        var encabezado = Parte<EncabezadoPagina>(alta, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Gastos y facturas", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo gasto", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo gasto", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);
        Assert.Equal("Editar gasto", Parte<EncabezadoPagina>(Montar(vm => vm.CargarParaEditar(Existente())), "Encabezado").BreadcrumbActual);
        Assert.Equal("Asociar factura a la entrada",
            Parte<EncabezadoPagina>(Montar(vm => vm.CargarDesdeEntrada(61, 186_540m)), "Encabezado").BreadcrumbActual);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAGastosSinGuardar()
    {
        var m = Montar();
        Completar(m.Vm);
        var origen = Parte<EncabezadoPagina>(m, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(GastosViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void Tarjeta_Mide760YQuedaALaIzquierda(double ancho)
    {
        var m = Montar(ancho: ancho);
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaTheory]
    [InlineData(1040.0)]
    [InlineData(1126.0)]
    public void Encabezado_SeLimitaALaColumnaDeLaCard_LasInsigniasTerminanEnSuBorde(double ancho)
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(activo: false)), ancho: ancho);
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));
        var encabezado = EnVentana(m.Ventana, Parte<EncabezadoPagina>(m, "Encabezado"));
        var insignia = EnVentana(m.Ventana, Parte<Insignia>(m, "InsigniaSoloLectura"));

        Assert.Equal(tarjeta.Right, encabezado.Right, 1);
        Assert.Equal(tarjeta.Right, insignia.Right, 1);
    }

    [AvaloniaFact]
    public void Secciones_ConSuTituloEnOrden()
    {
        var m = Montar();

        var titulos = Parte<StackPanel>(m, "Campos").GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("subtitulo-formulario")).Select(t => t.Text).ToList();

        Assert.Equal(new[] { "Factura", "Detalle", "Importe e imputación", "Condición de pago" }, titulos);
    }

    [AvaloniaFact]
    public void Campos_DeAPares_DetalleYDestinoATodoElAncho()
    {
        var m = Montar();
        Rect R(string nombre) => EnVentana(m.Ventana, Parte<CampoFormulario>(m, nombre));

        Assert.Equal(R("CampoProveedor").Y, R("CampoNumeroFactura").Y);
        Assert.Equal(R("CampoProveedor").Right + 16, R("CampoNumeroFactura").X, 1);
        Assert.Equal(R("CampoOrdenCompra").Y, R("CampoFecha").Y);
        Assert.Equal(R("CampoMonto").Y, R("CampoFuente").Y);
        Assert.Equal(R("CampoRubro").Y, R("CampoLineaPoa").Y);
        Assert.Equal(R("CampoProveedor").X, R("CampoDetalle").X);
        Assert.Equal(R("CampoNumeroFactura").Right, R("CampoDetalle").Right, 1);
        Assert.Equal(R("CampoDetalle").Width, R("CampoDestino").Width, 1);
        Assert.True(R("CampoDestino").Y > R("CampoDetalle").Bottom);
    }

    [AvaloniaFact]
    public void Vencimiento_SoloACredito()
    {
        var m = Montar();
        Assert.False(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));

        Clickear(m.Ventana, Parte<CheckBox>(m, "CheckCredito"));

        Assert.True(m.Vm.EsCredito);
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));
    }

    // ── Errores y Guardar ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AlAbrir_NadaEnRojo_YTocarDetalleVacio_ElErrorVaDebajoDelCampo()
    {
        var m = Montar();
        Assert.Empty(Errores(Parte<TextBox>(m, "CajaDetalle")));

        m.Vm.MarcarTocado(nameof(GastoFormViewModel.Detalle));
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio }, Errores(Parte<TextBox>(m, "CajaDetalle")));
    }

    [AvaloniaFact]
    public void SalirDeDetalleSinEscribir_MuestraSuError()
    {
        var m = Montar();
        Parte<TextBox>(m, "CajaDetalle").Focus();
        Dispatcher.UIThread.RunJobs();

        Parte<TextBox>(m, "CajaMonto").Focus();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { GastoFormViewModel.MensajeDetalleObligatorio }, Errores(Parte<TextBox>(m, "CajaDetalle")));
    }

    [AvaloniaFact]
    public void MontoConPuntoDecimal_ErrorDebajoDelCampo_YGuardarBloqueado()
    {
        var m = Montar();
        Completar(m.Vm);

        Parte<TextBox>(m, "CajaMonto").Text = "5.4";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { "El monto total no es un número válido. Usá coma para los decimales: 5,4" },
            Errores(Parte<TextBox>(m, "CajaMonto")));
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Guardar_DeshabilitadoHastaCompletarLosObligatorios()
    {
        var m = Montar();
        Assert.False(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);

        Completar(m.Vm);

        Assert.True(Parte<Button>(m, "BotonGuardar").IsEffectivelyEnabled);
        Assert.Contains("primario", Parte<Button>(m, "BotonGuardar").Classes);
        Assert.Contains("secundario", Parte<Button>(m, "BotonCancelar").Classes);
    }

    [AvaloniaFact]
    public void ErrorDelServidor_EnLaAlertaDeArribaDelPie_FueraDelScroll()
    {
        var servicio = new GastosFake { FallaAlGuardar = new ReglaDeNegocioException("El proveedor 'Barraca Carmelo S.A.' está dado de baja.") };
        var m = Montar(servicio: servicio);
        Completar(m.Vm);

        Clickear(m.Ventana, Parte<Button>(m, "BotonGuardar"));

        var alerta = Parte<Alerta>(m, "AlertaError");
        Assert.True(Visible(alerta));
        Assert.Equal("El proveedor 'Barraca Carmelo S.A.' está dado de baja.", alerta.Texto);
        Assert.False(Parte<ScrollViewer>(m, "Desplazamiento").IsVisualAncestorOf(alerta));
        Assert.True(EnVentana(m.Ventana, alerta).Bottom <= EnVentana(m.Ventana, Parte<Border>(m, "Pie")).Y + 0.5);
        Assert.Equal(EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta")).Width, EnVentana(m.Ventana, alerta).Width, 1);
    }

    [AvaloniaTheory]
    [InlineData(1040.0, 760.0)]
    [InlineData(1126.0, 768.0)]
    public void Pie_FueraDelScroll_ConElFormularioScrolleado_GuardarALaVista(double ancho, double alto)
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), ancho: ancho, alto: alto, adjuntos: ConAdjuntos(4));
        var scroll = Parte<ScrollViewer>(m, "Desplazamiento");
        var pie = Parte<Border>(m, "Pie");

        scroll.Offset = new Vector(0, scroll.Extent.Height);
        Dispatcher.UIThread.RunJobs();

        Assert.False(scroll.IsVisualAncestorOf(pie), "El pie quedó dentro del scroll.");
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "El formulario no scrollea: el pie fijo no se está probando.");
        Assert.Equal(m.Vista.Bounds.Height, EnVentana(m.Ventana, pie).Bottom, 0.5);
        Assert.True(EnVentana(m.Ventana, scroll).Bottom <= EnVentana(m.Ventana, pie).Y + 0.5, "El scroll se mete debajo del pie.");
        var guardar = EnVentana(m.Ventana, Parte<Button>(m, "BotonGuardar"));
        Assert.True(guardar.Y >= 0 && guardar.Bottom <= alto + 0.5, "Guardar quedó fuera de la ventana.");
        var tarjeta = EnVentana(m.Ventana, Parte<TarjetaContenido>(m, "Tarjeta"));
        Assert.Equal(tarjeta.Right, guardar.Right, 1);
    }

    // ── Condición de pago ───────────────────────────────────────────────────────

    [AvaloniaFact]
    public void Edicion_TildeACreditoDeshabilitado_ConSuTexto_YElVencimientoALaVista()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(condicion: CondicionPago.Credito)));

        var tilde = Parte<CheckBox>(m, "CheckCredito");
        Assert.True(tilde.IsChecked);
        Assert.False(tilde.IsEffectivelyEnabled);
        var texto = Parte<TextBlock>(m, "TextoCondicionFija");
        Assert.True(Visible(texto));
        Assert.Equal(GastoFormViewModel.TextoCondicionPagoFija, texto.Text);
        Assert.True(Visible(Parte<CampoFormulario>(m, "CampoVencimiento")));
    }

    [AvaloniaFact]
    public void AltaYDesdeEntrada_TildeHabilitado_SinElTexto()
    {
        foreach (var m in new[] { Montar(), Montar(vm => vm.CargarDesdeEntrada(61, 186_540m)) })
        {
            Assert.True(Parte<CheckBox>(m, "CheckCredito").IsEffectivelyEnabled);
            Assert.False(Visible(Parte<TextBlock>(m, "TextoCondicionFija")));
        }
    }

    // ── Solo lectura, anulado y adjuntos ────────────────────────────────────────

    [AvaloniaFact]
    public void SinRegistrarGastos_CamposDeshabilitados_GuardarOculto_InsigniaSoloLectura_YVerSigue()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), sesion: SoloVer(), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.False(Parte<TextBox>(m, "CajaDetalle").IsEffectivelyEnabled);
        Assert.False(Parte<ComboBox>(m, "ComboProveedor").IsEffectivelyEnabled);
        Assert.False(Parte<CalendarDatePicker>(m, "SelectorFecha").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
        var panel = m.Vista.GetVisualDescendants().OfType<AdjuntosPanelView>().Single();
        var ver = panel.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == "Ver");
        Assert.True(Visible(ver));
        Assert.True(ver.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void GastoAnulado_SoloLecturaConInsigniasYAviso_SinLaSeccionAdjuntos()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente(activo: false)), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
        Assert.True(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.True(Visible(Parte<Alerta>(m, "AlertaAnulado")));
        Assert.False(Visible(Parte<StackPanel>(m, "SeccionAdjuntos")));
        Assert.False(Parte<TextBox>(m, "CajaDetalle").IsEffectivelyEnabled);
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
    }

    [AvaloniaFact]
    public void Alta_SinPanel_ConElAvisoDeGuardarParaAdjuntar()
    {
        var m = Montar();

        var aviso = Parte<TextBlock>(m, "TextoGuardarParaAdjuntar");
        Assert.True(Visible(aviso));
        Assert.Equal("Guardá el gasto para poder adjuntar.", aviso.Text);
        Assert.False(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
    }

    [AvaloniaFact]
    public void Edicion_ConElPanelDeAdjuntos()
    {
        var m = Montar(vm => vm.CargarParaEditar(Existente()), adjuntos: ConAdjuntos(1));

        Assert.True(Visible(Parte<ContentControl>(m, "PanelAdjuntos")));
        Assert.Single(m.Vista.GetVisualDescendants().OfType<AdjuntosPanelView>());
        Assert.False(Visible(Parte<TextBlock>(m, "TextoGuardarParaAdjuntar")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaSoloLectura")));
        Assert.False(Visible(Parte<Insignia>(m, "InsigniaAnulado")));
    }

    // ── Carga inicial ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public async Task CargaInicialSinServidor_AlertaConReintentar_ClickRealCargaLosCombos()
    {
        var proveedores = new ProveedoresFake { Activos = [Barraca], Falla = new ServidorNoDisponibleException() };
        var m = Montar(proveedores: proveedores);
        Assert.True(Visible(Parte<Alerta>(m, "AlertaSinConexion")));
        Assert.True(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));

        proveedores.Falla = null;
        Clickear(m.Ventana, Parte<Button>(m, "BotonReintentar"));
        await (m.Vm.ReintentarCargaCommand.ExecutionTask ?? Task.CompletedTask);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Parte<Alerta>(m, "AlertaSinConexion")));
        Assert.Single(m.Vm.ProveedoresDisponibles);
    }

    [AvaloniaFact]
    public void CargaInicialCon403_SinPermisoEnLugarDeLaCard_YEnElPieSoloCancelar()
    {
        var m = Montar(proveedores: new ProveedoresFake { Falla = new UnauthorizedAccessException("403") });

        var estado = Parte<EstadoVacio>(m, "EstadoSinPermiso");
        Assert.True(Visible(estado));
        Assert.Equal(GastoFormViewModel.MensajeSinPermisoFormulario, estado.Mensaje);
        Assert.False(Visible(Parte<TarjetaContenido>(m, "Tarjeta")));
        Assert.False(Visible(Parte<Button>(m, "BotonGuardar")));
        Assert.True(Visible(Parte<Button>(m, "BotonCancelar")));
    }

    // ── Revocación en caliente dentro del shell ─────────────────────────────────

    [AvaloniaFact]
    public async Task RevocacionDeRegistrarGastos_DentroDelShell_PasaASoloLecturaSinBorrarLoTipeado()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.VerFinanzas, Permisos.RegistrarGastos);
        var tras = new HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = NuevoVm(new GastosFake(), sesion, new NavigationRecorderFake());
        shell.CurrentContent = vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shell;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        vm.Detalle = "Materiales de obra";
        Dispatcher.UIThread.RunJobs();
        var vista = ventana.GetVisualDescendants().OfType<GastoFormView>().Single();
        Assert.True(MontajeV2.Parte<TextBox>(vista, "CajaDetalle").IsEffectivelyEnabled, "Precondición: con finanzas.gastos se edita.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(MontajeV2.Parte<TextBox>(vista, "CajaDetalle").IsEffectivelyEnabled);
        Assert.True(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Insignia>(vista, "InsigniaSoloLectura")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(MontajeV2.Parte<Button>(vista, "BotonGuardar")));
        Assert.Equal("Materiales de obra", MontajeV2.Parte<TextBox>(vista, "CajaDetalle").Text);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoFormViewV2Tests"`
Expected: la gran mayoría FAIL contra la vista v1 (`Sequence contains no matching element` al buscar `Encabezado`, `Tarjeta`, `Campos`, `Pie`, etc.). Pegar el resumen.

- [ ] **Step 2: Vista v2**

`src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` completo:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.GastoFormView"
             x:DataType="vm:GastoFormViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (formulario §5.2, patrón IngresoFormView con el pie fijo de Ingreso por factura). Espejo:
         docs/ui-v2/mockups/finanzas/gasto-*.html. El breadcrumb vuelve con CancelarCommand en los tres modos. Los errores
         de cada campo los dibuja el DataValidationErrors del control (solo con el campo tocado: nada en rojo al abrir); el
         error del servidor va en la Alerta de arriba del pie, que queda fijo fuera del scroll con Cancelar y Guardar. Sin
         finanzas.gastos o con el gasto anulado, solo lectura: campos deshabilitados, Guardar oculto e insignias; con el
         gasto anulado además se oculta Adjuntos. El tilde A crédito no se cambia en una edición. Sin servidor al abrir,
         Alerta con Reintentar dentro de la card; tras un 403, Sin permiso en lugar de la card. -->
    <Grid x:Name="Raiz" RowDefinitions="*,Auto,Auto">

        <ScrollViewer x:Name="Desplazamiento" Grid.Row="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <StackPanel Margin="28,24">

                <!-- Encabezado en la misma columna de 760 que la card y el pie: las insignias terminan en el borde de la card. -->
                <Grid x:Name="ColumnaEncabezado" UseLayoutRounding="False">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                    </Grid.ColumnDefinitions>
                    <v2:EncabezadoPagina x:Name="Encabezado"
                                         Titulo="{Binding Titulo}"
                                         BreadcrumbOrigen="Gastos y facturas"
                                         BreadcrumbComando="{Binding CancelarCommand}"
                                         BreadcrumbActual="{Binding Titulo}">
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <v2:Insignia x:Name="InsigniaAnulado" Texto="Anulado" Variante="Neutro" VerticalAlignment="Center"
                                         IsVisible="{Binding EsAnulado}" />
                            <v2:Insignia x:Name="InsigniaSoloLectura" Texto="Solo lectura" Variante="Neutro" VerticalAlignment="Center"
                                         IsVisible="{Binding SoloLectura}" />
                        </StackPanel>
                    </v2:EncabezadoPagina>
                </Grid>

                <v2:EstadoVacio x:Name="EstadoSinPermiso" UseLayoutRounding="False"
                                Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                                IsVisible="{Binding SinPermiso}" />

                <!-- Columna * con MaxWidth 760: la card mide 760 a la izquierda. UseLayoutRounding False en la grilla: con el
                     redondeo Avalonia infla en 1 px una columna acotada por MaxWidth; la card lo recupera. -->
                <Grid x:Name="ColumnaFormulario" UseLayoutRounding="False" IsVisible="{Binding !SinPermiso}">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                    </Grid.ColumnDefinitions>

                    <v2:TarjetaContenido x:Name="Tarjeta" Padding="24" UseLayoutRounding="True">
                        <StackPanel>
                            <StackPanel x:Name="ErrorCarga" Margin="0,0,0,16" Spacing="8" IsVisible="{Binding SinConexion}">
                                <v2:Alerta x:Name="AlertaSinConexion" Variante="Error"
                                           Texto="No se pudo conectar con el servidor. Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar." />
                                <Button x:Name="BotonReintentar" Classes="secundario" HorizontalAlignment="Left"
                                        Content="Reintentar" Command="{Binding ReintentarCargaCommand}" />
                            </StackPanel>

                            <v2:Alerta x:Name="AlertaAnulado" Margin="0,0,0,16" Variante="Info"
                                       Texto="Este gasto está anulado: se puede consultar, pero no modificar."
                                       IsVisible="{Binding EsAnulado}" />

                            <!-- Los campos se deshabilitan juntos en solo lectura; Adjuntos queda afuera para que Ver siga. El
                                 16 entre filas va en el Margin superior de cada campo: el vencimiento aparece y desaparece
                                 sin dejar hueco. -->
                            <StackPanel x:Name="Campos" IsEnabled="{Binding !SoloLectura}">
                                <TextBlock Classes="subtitulo-formulario" Text="Factura" Margin="0,0,0,12" />
                                <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,Auto">
                                    <v2:CampoFormulario x:Name="CampoProveedor" Grid.Row="0" Grid.Column="0" Etiqueta="Proveedor" Requerido="True">
                                        <ComboBox x:Name="ComboProveedor" HorizontalAlignment="Stretch" PlaceholderText="Elegí el proveedor"
                                                  ItemsSource="{Binding ProveedoresDisponibles}" SelectedItem="{Binding ProveedorSeleccionado}">
                                            <ComboBox.ItemTemplate>
                                                <DataTemplate x:DataType="e:Proveedor"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                            </ComboBox.ItemTemplate>
                                        </ComboBox>
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoNumeroFactura" Grid.Row="0" Grid.Column="2" Etiqueta="N.º de factura">
                                        <TextBox x:Name="CajaNumeroFactura" Text="{Binding NumeroFactura}" PlaceholderText="Ej.: A-0001234" />
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoOrdenCompra" Grid.Row="1" Grid.Column="0" Margin="0,16,0,0" Etiqueta="Orden de compra">
                                        <TextBox x:Name="CajaOrdenCompra" Text="{Binding NumeroOrden}" PlaceholderText="Ej.: OC-77" />
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoFecha" Grid.Row="1" Grid.Column="2" Margin="0,16,0,0" Etiqueta="Fecha" Requerido="True">
                                        <CalendarDatePicker x:Name="SelectorFecha" HorizontalAlignment="Stretch"
                                                            SelectedDate="{Binding FechaSeleccionada}"
                                                            PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                                            CustomDateFormatString="dd/MM/yyyy"
                                                            beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                                    </v2:CampoFormulario>
                                </Grid>

                                <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />
                                <TextBlock Classes="subtitulo-formulario" Text="Detalle" Margin="0,0,0,12" />
                                <v2:CampoFormulario x:Name="CampoDetalle" Etiqueta="Detalle" Requerido="True">
                                    <TextBox x:Name="CajaDetalle" Text="{Binding Detalle}" PlaceholderText="Ej.: Materiales para la rambla" />
                                </v2:CampoFormulario>
                                <v2:CampoFormulario x:Name="CampoDestino" Margin="0,16,0,0" Etiqueta="Destino">
                                    <TextBox x:Name="CajaDestino" Text="{Binding Destino}" PlaceholderText="Ej.: Corralón municipal" />
                                </v2:CampoFormulario>

                                <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />
                                <TextBlock Classes="subtitulo-formulario" Text="Importe e imputación" Margin="0,0,0,12" />
                                <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,Auto">
                                    <v2:CampoFormulario x:Name="CampoMonto" Grid.Row="0" Grid.Column="0" Etiqueta="Monto total" Requerido="True">
                                        <TextBox x:Name="CajaMonto" Text="{Binding MontoTexto}" PlaceholderText="0,00" TextAlignment="Right" />
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoFuente" Grid.Row="0" Grid.Column="2" Etiqueta="Fuente de financiamiento" Requerido="True">
                                        <ComboBox x:Name="ComboFuente" HorizontalAlignment="Stretch" PlaceholderText="Elegí la fuente"
                                                  ItemsSource="{Binding FuentesDisponibles}" SelectedItem="{Binding FuenteSeleccionada}">
                                            <ComboBox.ItemTemplate>
                                                <DataTemplate x:DataType="e:FuenteFinanciamiento"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                            </ComboBox.ItemTemplate>
                                        </ComboBox>
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoRubro" Grid.Row="1" Grid.Column="0" Margin="0,16,0,0" Etiqueta="Rubro" Requerido="True">
                                        <ComboBox x:Name="ComboRubro" HorizontalAlignment="Stretch" PlaceholderText="Elegí el rubro"
                                                  ItemsSource="{Binding RubrosDisponibles}" SelectedItem="{Binding RubroSeleccionado}">
                                            <ComboBox.ItemTemplate>
                                                <DataTemplate x:DataType="e:RubroGasto"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                            </ComboBox.ItemTemplate>
                                        </ComboBox>
                                    </v2:CampoFormulario>
                                    <v2:CampoFormulario x:Name="CampoLineaPoa" Grid.Row="1" Grid.Column="2" Margin="0,16,0,0" Etiqueta="Línea POA">
                                        <ComboBox x:Name="ComboLineaPoa" HorizontalAlignment="Stretch" PlaceholderText="Sin línea POA"
                                                  ItemsSource="{Binding LineasPoaDisponibles}" SelectedItem="{Binding LineaPoaSeleccionada}">
                                            <ComboBox.ItemTemplate>
                                                <DataTemplate x:DataType="e:LineaPoa"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                            </ComboBox.ItemTemplate>
                                        </ComboBox>
                                    </v2:CampoFormulario>
                                </Grid>

                                <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />
                                <TextBlock Classes="subtitulo-formulario" Text="Condición de pago" Margin="0,0,0,12" />
                                <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,Auto">
                                    <CheckBox x:Name="CheckCredito" Grid.Row="0" Grid.Column="0" VerticalAlignment="Bottom"
                                              Content="A crédito" IsChecked="{Binding EsCredito}"
                                              IsEnabled="{Binding PuedeCambiarCondicionPago}" />
                                    <v2:CampoFormulario x:Name="CampoVencimiento" Grid.Row="0" Grid.Column="2" Etiqueta="Fecha de vencimiento"
                                                        Requerido="True" IsVisible="{Binding EsCredito}">
                                        <CalendarDatePicker x:Name="SelectorVencimiento" HorizontalAlignment="Stretch"
                                                            SelectedDate="{Binding FechaVencimientoSeleccionada}"
                                                            PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom"
                                                            CustomDateFormatString="dd/MM/yyyy"
                                                            beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                                    </v2:CampoFormulario>
                                    <TextBlock x:Name="TextoCondicionFija" Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3" Margin="0,8,0,0"
                                               Classes="texto-secundario" TextWrapping="Wrap"
                                               Text="{x:Static vm:GastoFormViewModel.TextoCondicionPagoFija}"
                                               IsVisible="{Binding EsEdicion}" />
                                </Grid>
                            </StackPanel>

                            <!-- Con el gasto anulado la sección entera se oculta. En alta no hay gasto al que adjuntar todavía. -->
                            <StackPanel x:Name="SeccionAdjuntos" IsVisible="{Binding MostrarSeccionAdjuntos}">
                                <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />
                                <ContentControl x:Name="PanelAdjuntos" Content="{Binding AdjuntosPanel}" IsVisible="{Binding EsEdicion}" />
                                <StackPanel x:Name="AdjuntosTrasGuardar" IsVisible="{Binding !EsEdicion}">
                                    <TextBlock Classes="titulo-seccion" Text="Adjuntos" Margin="0,0,0,8" />
                                    <TextBlock x:Name="TextoGuardarParaAdjuntar" Classes="texto-secundario"
                                               Text="Guardá el gasto para poder adjuntar." />
                                </StackPanel>
                            </StackPanel>
                        </StackPanel>
                    </v2:TarjetaContenido>
                </Grid>
            </StackPanel>
        </ScrollViewer>

        <!-- Error general fuera del scroll, con el ancho de la card: se ve aunque el formulario esté scrolleado. -->
        <Grid x:Name="FilaAlertaError" Grid.Row="1" Margin="28,0,28,12" UseLayoutRounding="False"
              IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
            </Grid.ColumnDefinitions>
            <v2:Alerta x:Name="AlertaError" Variante="Error" Texto="{Binding MensajeError}" />
        </Grid>

        <Border x:Name="Pie" Grid.Row="2"
                Background="{DynamicResource V2SuperficieBrush}"
                BorderBrush="{DynamicResource V2BordeBrush}"
                BorderThickness="0,1,0,0"
                Padding="28,16">
            <Grid UseLayoutRounding="False">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                </Grid.ColumnDefinitions>
                <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                    <Button x:Name="BotonCancelar" Classes="secundario" Content="Cancelar" Command="{Binding CancelarCommand}" />
                    <Button x:Name="BotonGuardar" Classes="primario" Content="Guardar" Command="{Binding GuardarCommand}">
                        <Button.IsVisible>
                            <MultiBinding Converter="{x:Static BoolConverters.And}">
                                <Binding Path="SoloLectura" Converter="{x:Static BoolConverters.Not}" />
                                <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            </MultiBinding>
                        </Button.IsVisible>
                    </Button>
                </StackPanel>
            </Grid>
        </Border>

    </Grid>

</UserControl>
```

`GastoFormView.axaml.cs` completo:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastoFormView : UserControl
{
    public GastoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastoFormViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir de un campo sin completarlo cuenta como tocarlo: su error aparece ahí y no recién al guardar (que con el
        // campo vacío está deshabilitado, así que nunca se llegaría a ver).
        TocarAlSalir(ComboProveedor, nameof(GastoFormViewModel.ProveedorSeleccionado));
        TocarAlSalir(SelectorFecha, nameof(GastoFormViewModel.FechaSeleccionada));
        TocarAlSalir(CajaDetalle, nameof(GastoFormViewModel.Detalle));
        TocarAlSalir(CajaMonto, nameof(GastoFormViewModel.MontoTexto));
        TocarAlSalir(ComboFuente, nameof(GastoFormViewModel.FuenteSeleccionada));
        TocarAlSalir(ComboRubro, nameof(GastoFormViewModel.RubroSeleccionado));
        TocarAlSalir(SelectorVencimiento, nameof(GastoFormViewModel.FechaVencimientoSeleccionada));
    }

    /// <summary>Marca el campo como tocado cuando el foco sale del control entero, no al pasar entre sus partes (la caja y
    /// el botón del selector de fecha). La lista del combo y el calendario viven en un popup, que es otra raíz de foco:
    /// mientras está abierto no se marca; al cerrarse se mira si el foco quedó afuera (mismo criterio que IngresoFormView).</summary>
    private void TocarAlSalir(Control control, string propiedad)
    {
        control.PropertyChanged += (_, e) =>
        {
            if (e.Property != InputElement.IsKeyboardFocusWithinProperty || control.IsKeyboardFocusWithin || PopupAbierto(control))
                return;
            MarcarTocado(propiedad);
        };

        EventHandler? alCerrar = (_, _) =>
            // Diferido: al cerrar, el combo recupera el foco después de avisar el cierre.
            Dispatcher.UIThread.Post(() =>
            {
                if (!control.IsKeyboardFocusWithin)
                    MarcarTocado(propiedad);
            });
        if (control is ComboBox combo)
            combo.DropDownClosed += alCerrar;
        else if (control is CalendarDatePicker selector)
            selector.CalendarClosed += alCerrar;
    }

    private static bool PopupAbierto(Control control) =>
        control is ComboBox { IsDropDownOpen: true } or CalendarDatePicker { IsDropDownOpen: true };

    private void MarcarTocado(string propiedad) => (DataContext as GastoFormViewModel)?.MarcarTocado(propiedad);
}
```

`GuardianDePatronTests.cs`: quitar `[InlineData(typeof(GastoFormView), null, "FINANZAS")]` (L64) y `typeof(GastoFormView),` de `VistasDeLaTanda` (L111); agregar `typeof(GastoFormView),` al final de `VistasMigradasAV2` (después de `typeof(GastosView),`). El reemplazo del chequeo de `HeaderVista` es `Raiz_TieneUi2_BreadcrumbConElTituloDelVm_EnLosTresModos` (convención 13; anotarlo).

- [ ] **Step 3: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~Gasto|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~AdjuntosPanel|FullyQualifiedName~GuardarBloqueado|FullyQualifiedName~ComandoDeGuardadoTooltip|FullyQualifiedName~EtiquetadoDeCampos"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --blame-hang-timeout 5m --filter "FullyQualifiedName~GastoForm|FullyQualifiedName~EntradaRegistroFactura"
```

Expected: verde, incluidos `AccesoRevocadoPantallasFinanzasTests.Un403_ConElFormularioDeGastoAbierto_OcultaGuardarSinBorrarLoTipeado`, `Un403_ConLaFacturaEnEdicion_AgregarYQuitarAdjuntoDejanDeEstarDisponibles` y `Un403_QueQuitaPagos_NoTocaElPanelDeAdjuntosDeUnGasto` (ahora con la v2: buscan por texto y por `AdjuntosPanelView`).

- [ ] **Step 4: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastoFormComparacionTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.Views.Dialogs;
using StockApp.Presentation.Views.Finanzas;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Formulario de gasto dentro del shell real (sidebar oculto en los dos renders) y los dos modales de Gastos.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class GastoFormComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, Action<Control>? preparar = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: preparar ?? ProductosComparacionTests.OcultarSidebar);

    /// <summary>Contenido scrolleado hasta el final: se ve el pie fijo pegado al borde con la card terminada arriba.</summary>
    private static void OcultarSidebarYScrollearAlFinal(Control vista)
    {
        ProductosComparacionTests.OcultarSidebar(vista);
        var formulario = vista.GetVisualDescendants().OfType<GastoFormView>().Single();
        var desplazamiento = formulario.GetVisualDescendants().OfType<ScrollViewer>().Single(s => s.Name == "Desplazamiento");
        desplazamiento.Offset = new Vector(0, desplazamiento.Extent.Height - desplazamiento.Viewport.Height);
        Dispatcher.UIThread.RunJobs();
    }

    private static Control Modal(string mensaje) => ContenidoDeDialogo.Extraer(new ConfirmacionDialog(mensaje));

    [AvaloniaFact] public void GastoNuevo_IgualAlMockup() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html");
    [AvaloniaFact] public void GastoNuevo_IgualAlMockup_1366x768() => Comparar(DatosGastos.Nuevo(), "finanzas/gasto-nuevo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditar_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.Editar(), "finanzas/gasto-editar.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditarCredito_IgualAlMockup() =>
        Comparar(DatosGastos.EditarCredito(), "finanzas/gasto-editar-credito.html", preparar: OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoEditarCredito_IgualAlMockup_1366x768() =>
        Comparar(DatosGastos.EditarCredito(), "finanzas/gasto-editar-credito.html", TamanoLienzo.Municipio, OcultarSidebarYScrollearAlFinal);
    [AvaloniaFact] public void GastoErrorCampo_IgualAlMockup() => Comparar(DatosGastos.ConErrorDeCampo(), "finanzas/gasto-error-campo.html");
    [AvaloniaFact] public void GastoErrorCampo_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConErrorDeCampo(), "finanzas/gasto-error-campo.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoErrorServidor_IgualAlMockup() => Comparar(DatosGastos.ConErrorDelServidor(), "finanzas/gasto-error-servidor.html");
    [AvaloniaFact] public void GastoErrorServidor_IgualAlMockup_1366x768() => Comparar(DatosGastos.ConErrorDelServidor(), "finanzas/gasto-error-servidor.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoCredito_IgualAlMockup() => Comparar(DatosGastos.Credito(), "finanzas/gasto-credito.html");
    [AvaloniaFact] public void GastoCredito_IgualAlMockup_1366x768() => Comparar(DatosGastos.Credito(), "finanzas/gasto-credito.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoSoloLectura_IgualAlMockup() => Comparar(DatosGastos.FormularioSoloLectura(), "finanzas/gasto-solo-lectura.html");
    [AvaloniaFact] public void GastoSoloLectura_IgualAlMockup_1366x768() => Comparar(DatosGastos.FormularioSoloLectura(), "finanzas/gasto-solo-lectura.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoAnulado_IgualAlMockup() => Comparar(DatosGastos.FormularioAnulado(), "finanzas/gasto-anulado.html");
    [AvaloniaFact] public void GastoAnulado_IgualAlMockup_1366x768() => Comparar(DatosGastos.FormularioAnulado(), "finanzas/gasto-anulado.html", TamanoLienzo.Municipio);

    [AvaloniaFact] public void GastoConfirmarAnular_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html");
    [AvaloniaFact] public void GastoConfirmarAnular_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeConfirmarAnular()), null, "finanzas/gasto-confirmar-anular.html",
            tamano: TamanoLienzo.Municipio);
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html");
    [AvaloniaFact] public void GastoFacturaDuplicada_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(Modal(DatosGastos.MensajeFacturaDuplicada()), null, "finanzas/gasto-factura-duplicada.html",
            tamano: TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh Gasto`. Expected: las 20 filas del formulario y los modales más las 16 del listado APROBADAS con el umbral por defecto. Si una no entra: diagnosticar como en la Task 14; un `UmbralAprobado` mayor solo en un GATE con el usuario (precedente: los de Ingreso por factura de la 3c-3 y la 4a).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (15a) En `Pie`, quitar `Grid.Row="2"` (el pie cae en la fila del scroll, encima del contenido) → rojo `Pie_FueraDelScroll_ConElFormularioScrolleado_GuardarALaVista` (las dos filas: "El scroll se mete debajo del pie" o el borde inferior). (Spec: "el pie fijo (sacarlo del lugar fijo, dejándolo dentro del scroll)": quitar la fila propia es la forma de sacarlo SIN agregar.)
- (15b) Quitar `IsEnabled="{Binding !SoloLectura}"` de `Campos` → rojo `SinRegistrarGastos_CamposDeshabilitados_…`, `GastoAnulado_…` y `RevocacionDeRegistrarGastos_DentroDelShell_…`.
- (15c) Quitar `IsVisible="{Binding MostrarSeccionAdjuntos}"` de `SeccionAdjuntos` → rojo `GastoAnulado_SoloLecturaConInsigniasYAviso_SinLaSeccionAdjuntos`. (Spec: "el ocultamiento de Adjuntos con gasto anulado".)
- (15d) Quitar `IsVisible="{Binding !SinPermiso}"` de `ColumnaFormulario` → rojo `CargaInicialCon403_SinPermisoEnLugarDeLaCard_…`. (Spec: "el estado `SinPermiso` … del formulario".)
- (15e) Quitar `IsEnabled="{Binding PuedeCambiarCondicionPago}"` de `CheckCredito` → rojo `Edicion_TildeACreditoDeshabilitado_…`. (Spec: P4.)
- (15f) Quitar `IsVisible="{Binding EsCredito}"` de `CampoVencimiento` → rojo `Vencimiento_SoloACredito`.
- (15g) En el `MultiBinding` de `BotonGuardar`, quitar `<Binding Path="SoloLectura" Converter="{x:Static BoolConverters.Not}" />` → rojo `SinRegistrarGastos_…` y `GastoAnulado_…`.
- (15h) Quitar `IsVisible="{Binding SoloLectura}"` de `InsigniaSoloLectura` → rojo `Edicion_ConElPanelDeAdjuntos` (la insignia aparece con permiso). (Spec: "el `IsVisible` de cada gate (… Insignia)".)
- (15i) Quitar `IsVisible="{Binding EsAnulado}"` de `InsigniaAnulado` → rojo `SinRegistrarGastos_CamposDeshabilitados_…` ("Anulado" sin estar anulado).
- (15j) En el code-behind, quitar la línea `TocarAlSalir(CajaDetalle, nameof(GastoFormViewModel.Detalle));` → rojo `SalirDeDetalleSinEscribir_MuestraSuError`.
- (15k) En `GastoFormViewModel.NotificarPermisosActualizados`, quitar `_adjuntosPanel.NotificarPermisosActualizados();` → rojo `GastoFormViewModelTests.NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos` y `AccesoRevocadoPantallasFinanzasTests.Un403_ConLaFacturaEnEdicion_AgregarYQuitarAdjuntoDejanDeEstarDisponibles` (con la vista v2). (Spec: "el reenvío de `NotificarPermisosActualizados` al panel".)
- (15l) Quitar el `MaxWidth="{StaticResource V2AnchoFormularioMax}"` de la columna de `ColumnaEncabezado` → rojo `Encabezado_SeLimitaALaColumnaDeLaCard_LasInsigniasTerminanEnSuBorde` (las dos filas: las insignias quedan en el borde de la vista). (Decisión Q-INSIGNIAS, 2026-10-16.)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml.cs tests/StockApp.Presentation.UiTests/V2/GastoFormViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/GastoFormComparacionTests.cs
git commit -m "feat(ui-v2): formulario de gasto v2 con pie fijo, errores por campo, solo lectura y adjuntos ocultos si está anulado"
```

(Sumar los heredados adaptados en el Step 3, si los hubo.)

---

## Bloque E — Guardianes, verificación orgánica y cierre

### Task 16: Repaso de guardianes y de mutaciones; suite y comparación completas

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia completa (suite, comparación, mutaciones) para el GATE 2 de la Task 17.

- [ ] **Step 1: Guardianes de la fase**

Confirmar con `grep` y la CLI `codegraph`, y anotar en el ledger:
1. `GastosView` y `GastoFormView` están en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `PagosGastoView` sigue en `VistasDeLaTanda` y en la `InlineData` del encabezado v1 (U1); `Guardian_CubreTodasLasVistasDelEnsamblado` verde.
2. `VistaV2_NoUsaPiezasDelTemaViejo` y `VistaV2_NoTieneOpacidadesLiterales` verdes sobre las dos vistas.
3. `grep -n 'Opacity\|Classes="primary"\|Classes="secondary"\|Classes="caption"\|Classes="card"\|<c:\|HeaderVista\|MargenVista\|DangerBrush\|SignoNegativoBrushConverter\|ActivoOpacidadConverter\|BadgeEstado' src/StockApp.Presentation/Views/Finanzas/GastosView.axaml src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` vacío.
4. `NingunaVista_TieneOverlaysDeModal` verde (los dos modales son `ConfirmacionDialog`, convención 25).
5. `ComandosDeGuardadoGuardianTests` verde (`GuardarCommand` sigue siendo `ComandoDeGuardado`; `FiltrarCommand` ya no existe).
6. `grep -rn "FiltrarCommand\|FiltrarAsync" src/ tests/` vacío (solo puede quedar en `docs/`).
7. `grep -rn "_debounceCts\|DebounceBuscarAsync\|DebounceCargarAsync" src/` vacío: los cuatro debounce pasan por `RecargaConDebounce`.
8. `git diff <BASE>..HEAD --stat -- tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs tests/StockApp.Presentation.UiTests/V2/ControlPoaViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/LibroCajaViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs` VACÍO (U4).
9. `git diff <BASE>..HEAD --stat -- src/StockApp.Api src/StockApp.Domain src/StockApp.Application` muestra SOLO `src/StockApp.Application/Finanzas/AdjuntoService.cs` (E9); `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` sin cambios (U1).

Si un punto falla, se corrige en un commit propio con su rojo pegado.

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger que quedaron PEGADOS los rojos de las mutaciones que pide la spec (sección 3): la cancelación en el helper (1a); el turno de la recarga de Gastos (4a); la programación por cambio de filtro (6a, 6d); el predicado de Estado en el pie y en el PDF (5b, 5c); el estado `SinPermiso` del listado y del formulario (14a, 15d); el `IsVisible` de cada gate (14b a 14g, 15h, 15i); la solo lectura con gasto anulado (10a, 15b); el ocultamiento de Adjuntos con gasto anulado (10c, 15c); la condición de `CanExecute` de Anular sobre fila anulada (7a); la deshabilitación del tilde "A crédito" en edición (10d, 15e); el pie fijo (15a); la validación de servidor de E9 (3a, en los dos proyectos); el vencimiento condicionado (9a, 15f); el reenvío de `NotificarPermisosActualizados` al panel (15k); el encabezado del formulario en la columna de 760 (15l). Y las demás de la fase (2a a 2c, 3b, 4b a 4f, 5a, 5d a 5g, 6b, 6c, 8a, 9b a 9e, 10b, 11a, 13a, 14h a 14l, 15g, 15j). Una sin rojo pegado se corre ahora.

- [ ] **Step 3: Build, suite completa, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln --blame-hang-timeout 5m
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos `.ods` de `Infrastructure.Tests` / `Api.Tests` y el intermitente de `ApiStartupMigrationTests` (confirmar por nombre contra la línea base); total ≥ 5897 + nuevos − reemplazados (anotar número y reemplazos: ninguno se borra; se RENOMBRAN `FiltrarCommand_PasaLosFiltrosAlServicio` → `CambiarFiltros_PasaLosFiltrosAlServicio`, `FiltrarCommand_FechaDesdeYHasta_SinCorrimientoDeDia` → `CambiarFechas_SinCorrimientoDeDia`, `FiltrarCommand_Un403_VaciaLaGrillaYNoPropaga` → `RecargaPorFiltro_Un403_VaciaLaGrillaYMarcaSinPermiso`, y los dos `InlineData` de `GuardianDePatronTests` se reemplazan por los dos `Raiz_*` de las Tasks 14 y 15); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla).

- [ ] **Step 4: Commit (solo si hubo ajustes)**

```bash
git add <archivos ajustados>
git commit -m "test(ui-v2): guardianes de la Fase 4d al día"
```

### Task 17: Verificación orgánica (GATE 2 con el usuario), docs de cierre y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 34 a 36)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda, §6.5)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 4d cerrada en `feat/ui-v2` (sin push ni merge: el merge a `main` es por bloques, al cerrar la 4e).

- [x] **Step 1: Verificación orgánica (spec §6.4) — la ejecuta un agente con la app real contra la base local, en WSLg a 1280×760 y 1366×768**

Preparación (en este orden; el backup va ANTES de crear cualquier dato, a diferencia de la 4c):
1. `docker start stockapp-pg` (queda andando al terminar). **Backup:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-4d-$(date +%Y%m%d-%H%M%S).sql` (usuario y base del `.env` local o de `~/.config/GestionMunicipal/conexion.json`); `ls -l` del archivo (no vacío); anotar la ruta en el ledger.
2. API del worktree: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (confirmar el puerto contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`.
3. Operador de permisos MIXTOS por Administración de usuarios: `f4dmixto` con `finanzas.ver` y `finanzas.pagos` (sin `finanzas.gastos`); anotar su id.
4. Datos de prueba por la app (camino del dominio), con prefijo `PRUEBA-4D` en el detalle y en el número de factura: un proveedor, una fuente, un rubro y una línea POA `PRUEBA-4D` si no hay activos para usar; un gasto de contado, uno a crédito no vencido, uno a crédito vencido (vencimiento en el pasado), uno con un pago parcial; una entrada de stock con "Sí, adjuntar factura" (alta desde entrada). Anotar todos los ids.
5. Toolkit `scripts/gui-verificacion/`. MIRAR cada captura y comparar `md5sum` con la anterior (dos iguales cuando la pantalla debía cambiar = WSLg congelado: PARAR y pedir al usuario que reinicie WSL). Popups (calendario, combos) con `scripts/gui-verificacion/capturar-x11.py`. Tras un diálogo modal la ventana principal puede quedar sin foco (`xdotool windowminimize` + `windowmap`). El Aviso global puede quedar DETRÁS de la ventana (`xdotool search --name`).

Pasos (capturar cada pantalla; 1, 4, 6 y 8 se repiten a 1366×768):
1. **Listado (Admin):** Gastos abre con "Cargando gastos…" y después la grilla con las insignias (Pendiente neutra, Parcial ámbar, Pagada verde, Vencida roja), el pie "N gastos"; ordenar por FECHA, PROVEEDOR y SALDO; tooltip de un detalle cortado; scroll horizontal si lo hay. Comparar a ojo con `gastos.html`.
2. **Filtros:** cambiar Desde con el calendario (capturar el popup con `capturar-x11.py`) → recarga sola sin botón Filtrar; elegir Proveedor, Fuente, Rubro y Línea POA (capturar un desplegable) → cada uno recarga; Estado "Vencida" → filtra al instante; "Limpiar" → todo vuelve, una sola recarga (mirar en el log de la API que hubo UN `GET /finanzas/gastos` tras Limpiar). Un período sin datos → "Ningún gasto coincide con los filtros".
3. **Control POA → Gastos:** doble click en una línea con facturas → Gastos con la Línea POA preseleccionada en el combo y filtrado (una sola consulta en el log de la API).
4. **Alta:** "Nuevo gasto" → formulario con el pie fijo; tocar Detalle y salir → error debajo; Monto "5.4" → error con la sugerencia y Guardar bloqueado con su tooltip; completar → Guardar → vuelve a Gastos con la fila nueva. Alta a crédito: tildar "A crédito" → aparece el vencimiento; destildar con el vencimiento vacío → Guardar se habilita. Comparar con `gasto-nuevo.html` y `gasto-credito.html`.
5. **Edición:** doble click en el gasto a crédito `PRUEBA-4D` → "Editar gasto", tilde deshabilitado con el texto, vencimiento a la vista, panel de adjuntos: Agregar un PDF, Ver (se abre) y Quitar (confirmar); scrollear a 1280×760 y verificar el pie pegado al borde. Comparar con `gasto-editar-credito.html`.
6. **Desde entrada y factura duplicada:** registrar una entrada, "Sí, adjuntar factura" → "Asociar factura a la entrada" con el monto sugerido; poner el número de factura de un gasto `PRUEBA-4D` existente del mismo proveedor → Guardar → la pregunta de asociar (texto de `gasto-factura-duplicada.html`) → "Sí, continuar" → asociada; Cancelar del breadcrumb vuelve a Gastos.
7. **Anular:** sobre el gasto con stock → pregunta con el aviso de stock (comparar con `gasto-confirmar-anular.html`); sobre uno de contado con pago automático → segunda pregunta con el monto; aceptar → la fila pasa a "Anulada" en terciario; seleccionarla → Anular deshabilitado, Editar habilitado → abre en solo lectura con "Anulado" y "Solo lectura", el aviso, campos deshabilitados y SIN la sección Adjuntos (comparar con `gasto-anulado.html`).
8. **409 de adjuntar a un gasto anulado por la API:** `TOKEN=$(curl -s -X POST http://localhost:<puerto>/auth/login -H 'Content-Type: application/json' -d '{"nombreUsuario":"admin","contrasena":"Admin1234"}' | python3 -c 'import sys,json;print(json.load(sys.stdin)["token"])')` y `curl -s -i -X POST http://localhost:<puerto>/finanzas/gastos/<id del anulado>/adjuntos -H "Authorization: Bearer $TOKEN" -F "archivo=@<un pdf>;type=application/pdf"` → `HTTP/1.1 409` con `"detail":"No se puede adjuntar a un gasto anulado."`. Pegar la salida (sin el token) en el ledger.
9. **PDF:** Exportar PDF con un filtro de servidor y Estado puestos → guardar → verificar POR CONTENIDO con pypdf (`python3 -c "import pypdf,sys;r=pypdf.PdfReader(sys.argv[1]);print(r.pages[0].extract_text())" <archivo>`): título "Gastos y facturas", la línea de filtros con lo cargado y "Estado: …", las 11 columnas y SOLO las filas visibles. Cambiar un proveedor y exportar antes de que recargue no se puede reproducir a mano de forma confiable (300 ms): lo custodia `ExportarPdf_ConFiltroCambiadoSinRecargar_RotulaLoCargado`.
10. **Operador mixto `f4dmixto`:** sin Nuevo, Editar ni Anular, con "Solo lectura", Pagos y Exportar PDF (comparar con `gastos-solo-lectura.html`); doble click no edita; Pagos navega a la pantalla de pagos v1 (U1). Desde la sesión de Admin quitarle `finanzas.pagos`; en la de `f4dmixto`, una acción que pegue al servidor → un solo Aviso global y Pagos desaparece sin navegar. Quitarle también `finanzas.ver` → "Sin permiso" en lugar de la card (P7-A), no "Todavía no hay gastos". Con `finanzas.gastos` agregado y el formulario de un gasto abierto, revocarlo → el formulario pasa a solo lectura sin perder lo tipeado.
11. **Sin servidor:** `kill` SOLO del pid de `StockApp.Api` → en Gastos, cambiar un filtro → card "No se pudo conectar con el servidor" con Reintentar y los filtros a mano, sin modal global; levantar la API y "Reintentar" → carga con los filtros de pantalla. Abrir "Nuevo gasto" con la API caída → Alerta con Reintentar dentro de la card; levantarla y Reintentar → combos cargados. Anotar si salió un Aviso global y dónde.
12. **Popups:** capturar con `capturar-x11.py` el calendario de Desde y de Fecha del formulario, y el desplegable de Proveedor en los dos lugares; verificar que el calendario abre, elige día, cierra y no deja el foco atrapado.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5 de las capturas), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa. **La limpieza de los datos `PRUEBA-4D` y de `f4dmixto` la decide el usuario:** listar los ids en el ledger y PREGUNTAR (baja por el dominio, borrado físico acotado por ids solo contra `stockapp-pg`, o dejarlos); nunca contra el VPS.

- [x] **Step 2: GATE 2 — aprobación final del usuario (PARADA)**

PARAR. Presentar: `index.html` (las 36 filas de la fase), las capturas de la verificación orgánica junto a los mockups, el PDF verificado por contenido, la salida del 409, los rojos de las mutaciones, los tests heredados adaptados y renombrados, los `UmbralAprobado` si los hubo, la elección de P7 y P8 y los cambios respecto de los mockups aprobados, y la pregunta de la limpieza de datos. No seguir sin "aprobado" explícito. Registrar en el ledger: `Fase 4d: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

- [x] **Step 3: `herramientas.md` y spec**

`herramientas.md`, después de la 33: "Convenciones que agrega la Fase 4d (Gastos y facturas)" con la 34, la 35 y la 36 de este plan (como quedaron aprobadas, y la 35 según P8).

Spec:
1. `**Estado:**`: "4d" en la lista de fases implementadas y "; Fase 4d (Gastos y facturas) APROBADA por el usuario el <fecha>"; la fila de Finanzas de la tabla de §7.
2. §7, después de "Estado de la Fase 4c": "**Estado de la Fase 4d (aprobada el <fecha>).** Gastos y facturas v2 (filtros que recargan solos con `RecargaConDebounce`, sin Filtrar; Estado en memoria con el pie y el PDF de lo cargado; insignias de Estado por tono; fila anulada por token; Anular deshabilitado sobre una anulada; estados completos) y el formulario de gasto v2 (card de 760, cuatro secciones, errores por campo, pie fijo, condición de pago fija en edición, solo lectura por permiso o anulado, adjuntos ocultos si está anulado), con E1 a E9; el debounce de Productos, Control POA y Libro caja migrado al helper sin tocar sus tests; el servidor rechaza adjuntar a un gasto anulado (409). Decisiones previas: P7 <A/B>, P8 <A/B>. Comparado en 36 filas a los dos tamaños (<máximo de teselas densas>). Suite <total> en `<hash>`. Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D17 aprobadas o cambiadas>."
3. §6.5: cada compensación nueva medida en las Tasks 14 y 15 (si hubo), con su antes y después.
4. "**Deuda abierta tras la Fase 4d:**" con, como mínimo: lo que la spec ya anotó como no resuelto en la 4d (`PagosGastoView` v1 con el panel v2 adentro; `AgregarAPagoAsync` sin validar el pago; `QuitarAsync` sobre un gasto anulado; el override de `NotificarPermisosActualizados` copiado en los tres hosts del panel; el doble Aviso tras un 403 en el panel; CS0114; los minors del debounce que el helper no cambia), lo que dejen P7 y P8 si se eligió B, el estado calculado con el reloj real en `GastoFila` (D15), la selección que se suelta al recargar (D17), cualquier `UmbralAprobado`, los minors diferidos del ledger y los datos de prueba que hayan quedado según lo que decidió el usuario. Y tachar en "Deuda abierta tras la Fase 4c" el debounce triplicado (cerrado) y en la de la 4a el adjunto activo tras anular y el ancho del formulario v1 de Gasto (cerrados).

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 4d (gastos y facturas)"
```

- [ ] **Step 4: Review final de la rama**

Review de todo el rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus"; que la excepción a "VMs intactos" no exceda E1 a E8 (y P7); que E9 no cambió firma, ruta ni DTOs; que los tests de Productos, Control POA y Libro caja no se tocaron; que ninguna pieza del tema viejo quedó en las dos vistas; que `PagosGastoView` sigue igual; que el PDF de Gastos sigue con las 11 columnas, el aviso de volumen y el nombre `gastos-AAAAMMDD.pdf`; y que el formulario no puede guardar un gasto anulado ni sin permiso. Los hallazgos se resuelven en commits propios (TDD, con rojo pegado) o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- Pagos del gasto (`PagosGastoView` v1, su migración y `AdjuntoService.AgregarAPagoAsync`): Fase 4e (U1).
- Cambios de dominio, de DTOs y de API salvo la regla de E9 (que no cambia firma, ruta ni DTOs); `GastoService` intacto.
- Dar de baja el adjunto al anular un gasto (U2: se oculta) y congelar `QuitarAsync` sobre un gasto anulado.
- Filtros nuevos, búsqueda de texto en Gastos, "Guardar y cargar otro".
- Un tooltip de Guardar que enumere los obligatorios que faltan (cambio de `ViewModelBase`).
- Reloj inyectable en `GastoFila` para el estado calculado (D15).
- Re-seleccionar la factura tras una recarga (D17).
- Merge a `main` (es por bloques, al cerrar la 4e) y push.
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| La migración del debounce cambia sin querer el comportamiento de Productos, Control POA o Libro caja. | Helper con la mecánica idéntica (tabla de decisiones), `EsperaDebounce`/`_tareaDebounce` conservados, sus tests sin tocar (Task 2 Step 5 y Task 16 Step 1.8) y mutaciones (2a a 2c). |
| Una respuesta vieja (carga inicial o un filtro anterior) pisa el resultado del filtro actual. | Un solo `ConsultaVigente` para carga, recarga y Reintentar (4a); `FiltroCambiadoDuranteLaCargaInicial_…` (Task 6); P8. |
| La preselección desde Control POA consulta dos veces o muestra el combo en "Todas". | Recarga armada después del remapeo (6b); `FiltrarPorLineaPoa_YLaCargaInicial_ConsultanUnaSolaVez`; UiTest `DesdeControlPoa_ElComboMuestraLaLineaPreseleccionada_…`. |
| El PDF exporta filas ocultas o rotula filtros no aplicados. | Exporta las visibles y rotula `_consultaCargada` + Estado (5c, 5e); `ExportarPdf_ConFiltroCambiadoSinRecargar_…`. |
| Tras perder `finanzas.ver` en caliente la pantalla dice "Todavía no hay gastos". | P7-A (`MarcarSinPermiso`), test de VM y de la View real dentro del shell. |
| El formulario de un gasto anulado deja adjuntar o guardar. | Sección Adjuntos oculta (U2, 10c, 15c), solo lectura (10a, 15b), y la barrera del servidor (E9, 3a); el 409 se verifica por la API en el GATE 2. |
| El pie fijo se mete debajo del scroll o tapa contenido a 1280×760. | `Grid` `*,Auto,Auto` (convención 27); `Pie_FueraDelScroll_…` a los dos tamaños con el formulario scrolleado (15a); mockups `gasto-editar*` scrolleados. |
| Los siete filtros no entran a 1040 o cortan nombres. | Dos filas (D2), medición en la Task 12 con los nombres más largos; si no entran, se decide en el GATE 1. |
| 11 columnas no entran y una medida se corta. | `ColumnaAuto` (convención 19), ESTADO fijo, scroll horizontal (precedente Historial), `GrillasSinRecorteBarridoTests`, `GastosViewColumnasAnchoTests`. |
| El estado calculado con el reloj real cambia los mockups con el tiempo. | Vencimientos lejos de "hoy" (D15); `DatosGastosTests` fija los cinco estados. |
| La verificación orgánica ensucia la base local. | Backup ANTES; prefijo `PRUEBA-4D`; ids anotados; limpieza decidida por el usuario; nunca el VPS. |
| WSLg se congela o `capturar.sh` devuelve el escritorio de Windows con exit 0. | md5 de capturas, mirar cada una, `capturar-x11.py` para popups, recupero de foco; si se congela, PARAR y pedir el reinicio al usuario. |
| Un test heredado se borra sin reemplazo. | Convención 13: los tres `FiltrarCommand_*` se renombran y adaptan; los dos `InlineData` de `GuardianDePatronTests` tienen su reemplazo nombrado; Task 16 Step 3 lista renombres y reemplazos. |
