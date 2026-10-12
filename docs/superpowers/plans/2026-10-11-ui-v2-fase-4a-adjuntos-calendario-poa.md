# Fase 4a — Adjuntos, Calendario de pagos y Control POA — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar a la UI v2 `AdjuntosPanelView`, Calendario de pagos y Control POA, con los estados de pantalla de las convenciones 18, 22 y 23 y las excepciones E1 a E5 de la spec, comparados contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** Cuatro bloques. (A) Los ViewModels primero, sin tocar vistas: `AdjuntosPanelViewModel` pide confirmación al quitar (E3) y su `PuedeModificar` pasa a getter refrescable con reenvío desde los tres hosts (E5); `CalendarioPagosViewModel` y `ControlPoaViewModel` cargan con `EjecutarCargaProtegidaAsync` + `ConsultaVigente` y optan por `SinConexion` (E1); Control POA recarga solo al cambiar Ejercicio con debounce inyectable (E2); tres conversores nuevos (tamaño legible E4, fecha corta, porcentaje). (B) Datos fijos, medición y mockups, con GATE del usuario antes de todo XAML. (C) Las tres vistas v2 (un componente nuevo, `v2:BarraEjecucion`), con sus comparaciones, gates de UI con operador de permisos mixtos y la exclusión `.panel-adjuntos-v1` de Ingreso por factura quitada. (D) Repaso de guardianes y mutaciones, verificación orgánica con GATE del usuario y docs de cierre.

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Projektanker.Icons.Avalonia (MDI), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, bloque "Diseño de la Fase 4a" (subfases, excepciones E1 a E5, secciones 1 a 4: Calendario, Control POA, Panel de adjuntos, y tests, verificación y deuda) y, para convenciones, los bloques de las Fases 3a a 3c-3 (§7) y §6.5. Convenciones 1 a 27: `docs/ui-v2/herramientas.md` (en especial 10, 11, 13, 14, 18, 19, 20, 21, 23 y 27). Molde de formato: `docs/superpowers/plans/2026-10-09-ui-v2-fase-3c2-historial.md` (Bloque A de ViewModels, Task 4 de medición y GATE, Task 7 de vista y Task 8 de cierre) y `docs/superpowers/plans/2026-10-11-ui-v2-fase-3c3-ingreso-factura.md` (Global Constraints, mutaciones, Task 10 de verificación orgánica).

Todos los comandos se corren desde la raíz del worktree, con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>` (rama `feat/ui-v2`, base: el commit de este plan). Ese worktree tiene su propio índice `.codegraph/`: usar CodeGraph MCP (`codegraph_explore`) ANTES de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Al escribir este plan CodeGraph devolvió como "changed on disk" (índice desactualizado, sin fuente) `GastoFormViewModel.cs`, `IngresoPorFacturaViewModel.cs`, `ProductoListViewModel.cs`, `MovimientoHistorialViewModel.cs`, `GastosViewModel.cs` y otros: si vuelve a pasar, leerlos con Read y decirlo en el reporte.

## Decisiones del usuario (2026-10-11, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Subfases de la Fase 4**: esta es la 4a (`AdjuntosPanelView` + Calendario de pagos + Control POA). Los hosts `GastoFormView` y `PagosGastoView` siguen v1 hasta la 4d y la 4e. | Fuera de alcance: los hosts. El panel v2 se ve v2 adentro de ellos (excepción inversa temporal). | todas |
| U2 | **Excepciones a "VMs intactos" E1 a E5** (spec): E1 estados de carga en Calendario y Control POA; E2 Ejercicio recarga solo con debounce inyectable y `RecargarCommand` sale del encabezado de Control POA; E3 confirmación al quitar un adjunto; E4 tamaño legible en es-UY; **E5 (decidida el 2026-10-11) `PuedeModificar` se refresca al revocar permisos con el panel abierto**, con test en `Presentation.UiTests` con la View real, operador MIXTO y mutación. | E3, E4, E5: Task 1. E1: Tasks 2 y 3. E2: Task 3. Ningún otro cambio de VM. | 1, 2, 3 |
| U3 | **Opción A en las tres pantallas** (spec, secciones 1 a 3): cuatro cards apiladas en el Calendario; grilla con barra de ejecución e `Insignia` en Control POA; lista compacta en el panel. | D1 a D18 abajo, a confirmar en el GATE. | 5 a 8 |
| U4 | **Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE** (criterio de 3a a 3c-3). | D1 a D18. Si el usuario cambia una, se ajustan mockups y los tests que la fijan en la Task 5, antes de las Tasks 6 a 8. | 5 |
| U5 | **Ejecución con subagent-driven-development** (decidido antes de escribir el plan). | Header del plan. | — |

## Decisiones que se confirman al aprobar los mockups (Task 5)

- **D1 — Calendario, encabezado:** `EncabezadoPagina` sin breadcrumb (pantalla del sidebar), `Titulo="Calendario de pagos"`, `Subtitulo="Facturas por vencimiento y pagos de los últimos 7 días."`, y "Actualizar" (`secundario`, `RecargarCommand`). La pantalla no tiene primario.
- **D2 — Calendario, cards:** cuatro `TarjetaContenido` apiladas con 16 de separación, dentro de un `ScrollViewer` debajo del encabezado. Título de cada card con `titulo-seccion` y el contador con punto medio: "Vencidas · 2", "A vencer en 7 días · 1", "A vencer en 30 días · 2", "Pagos recientes · 2" (`StringFormat` sobre `X.Count`, que `ObservableCollection` notifica). 12 de separación entre el título y la grilla.
- **D3 — Calendario, columnas de facturas:** PROVEEDOR (`*`, `MinWidth` 200, elipsis + tooltip si se corta), FACTURA (`Auto` medida, "—" si no tiene número), VENCE (`Auto` medida, `NoCede`, "dd/MM/yyyy"; en Vencidas el texto lleva `Classes="negativo"`, el token de peligro), SALDO (`Auto` medida, `num`, `MonedaConverter`) y una columna sin título con "Registrar pago" (`secundario`, `CellStyleClasses="sin-padding-vertical"`), oculta sin `PuedeRegistrarPagos` (D4). `CanUserSortColumns="False"`: la lista ya viene ordenada por vencimiento. Pagos recientes: PROVEEDOR (`*`), FECHA (`Auto` medida, `NoCede`), MONTO (`Auto` medida, `num`).
- **D4 — Calendario, columna "Registrar pago":** se oculta la COLUMNA entera (spec), no solo el botón. Una `DataGridColumn` no está en el árbol lógico y no recibe el `DataContext`, así que su `IsVisible` lo sincroniza el code-behind de la vista con `PuedeRegistrarPagos`, al cambiar el `DataContext` y en cada `PropertyChanged` del VM con nombre vacío o `PuedeRegistrarPagos` (lo que dispara `NotificarPermisosActualizados`). El botón de la celda conserva su `IsVisible` sobre el gate (doble barrera).
- **D5 — Calendario, vacío por sección:** una línea `texto-secundario` dentro de la card, en lugar de la grilla ("No hay facturas vencidas", "No hay facturas que venzan en los próximos 7 días", "No hay facturas que venzan en los próximos 30 días", "No hay pagos en los últimos 7 días"). No es un `EstadoVacio`: cuatro bloques centrados de 120 de alto llenarían la pantalla para decir "no hay". Se muestra solo con la carga terminada (`!Cargando`).
- **D6 — Calendario, estados de pantalla (E1):** mientras carga sin datos, UNA card con `v2:EstadoCargando` "Cargando el calendario de pagos…" en lugar de las cuatro; sin servidor, UNA card con `EstadoVacio` de error "No se pudo conectar con el servidor" + "Reintentar" (los textos de la convención 18); sin permiso (403), `EstadoVacio` "Sin permiso" en lugar de las cards (convención 18, sin mockup propio: el componente ya está comparado en Historial y Productos). Con datos en pantalla, "Actualizar" deja las cards mientras recarga.
- **D7 — Control POA, encabezado:** `Titulo="Control POA"`, `Subtitulo="Ejecución del presupuesto por línea. Doble click en una línea abre sus facturas."`, "Exportar CSV" y "Exportar PDF" (`secundario`, texto solo, sin ícono). Sin "Actualizar" (E2) y sin primario.
- **D8 — Control POA, filtros:** `BarraFiltros MostrarBusqueda="False"` con `v2:CampoFormulario Etiqueta="Ejercicio"` y el `NumericUpDown` de hoy (2020 a 2100, `FormatString="0"`, ancho 120, `NumericUpDownValidacionBehavior.Que="año"`). Cambiar el valor recarga sola tras 300 ms (E2).
- **D9 — Control POA, columnas:** LÍNEA (`*`, `MinWidth` 200, elipsis + tooltip si se corta), PROGRAMA (`Auto` medida), PRESUPUESTO y GASTADO (`Auto` medidas, `num`, `MonedaConverter`), SALDO (ancho FIJO medido en la Task 5, `num`, patrón de STOCK NUEVO del Historial: `Insignia` "Sobreejecutado" a la izquierda y el número a la derecha en una sola `Grid`) y % EJECUCIÓN (ancho FIJO medido, `num`: barra a la izquierda y "47,30 %" a la derecha). Orden por click conservado (`CanUserSortColumns="True"`, `FilasView`). Pie de conteo "N líneas" (convención 10).
- **D10 — `v2:BarraEjecucion`:** 64 × 6, radio 3; pista `V2BordeBrush`; relleno `V2AcentoBrush` hasta 100 % y `V2PeligroTextoBrush` por encima de 100 %; ancho del relleno `round(64 × min(%, 100) / 100)`. Sin tokens nuevos. Espejo `.v2-barra-ejecucion` en `base.css`.
- **D11 — Control POA, saldo negativo:** `Insignia` `Variante="Peligro"` "Sobreejecutado" + número con `Classes="negativo"`. Prohibidos `BadgeEstado`, `SignoNegativoBrushConverter` y `Opacity` (convención 11; los custodia `VistaV2_NoUsaPiezasDelTemaViejo`).
- **D12 — Control POA, estados (E1):** dentro de la card, con el filtro a mano (patrón del Historial): `EstadoCargando` "Cargando el control POA…"; `EstadoVacio` "No hay líneas POA en este ejercicio" / "Probá con otro ejercicio."; sin servidor, `EstadoVacio` de error con "Reintentar" (relee el Ejercicio en pantalla, convención 18); sin permiso, `EstadoVacio` "Sin permiso" en lugar de la card.
- **D13 — Panel de adjuntos, encabezado:** `titulo-seccion` "Adjuntos" a la izquierda y "Agregar" (`secundario`) a la derecha, oculto sin `PuedeModificar` (antes se deshabilitaba). Nunca `primario`: los hosts ya tienen su Guardar.
- **D14 — Panel de adjuntos, fila:** alto 40, borde superior de 1 px `V2SeparadorGrillaBrush`; columnas: ícono 16 (`mdi-file-pdf-box` para PDF, `mdi-file-image-outline` para JPG y PNG; `texto-secundario`) con 8 a la derecha · nombre (`*`, elipsis y tooltip solo si se corta, convención 20) · tamaño (72, a la derecha, `texto-secundario`, E4) · fecha (80, a la derecha, `texto-secundario`, "dd/MM/yyyy") · "Ver" (`ghost`, siempre) · "Quitar" (`ghost`, oculto sin `PuedeModificar`). Anchos 72 y 80 a confirmar con la medición de la Task 5.
- **D15 — Panel de adjuntos, vacío y confirmación:** vacío como línea `texto-secundario` "Sin adjuntos. PDF, JPG o PNG hasta 10 MB."; Quitar pregunta "¿Quitar el adjunto "<nombre>"?" con los botones por defecto ("Cancelar" / "Sí, continuar"; la convención 17 no aplica: nada se hizo todavía).
- **D16 — Panel dentro de Ingreso por factura:** la card "Adjuntá la factura" conserva su subtítulo y su frase, y el panel suma su propio encabezado "Adjuntos" + "Agregar" (el panel es el mismo en sus tres hosts). Alternativa: ocultar el subtítulo de la card. El mockup `ingreso-factura-guardado.html` dibuja el panel real con el adjunto fijo de `DatosIngresoFactura` ("factura-A-0004521.pdf", 178 KB, 06/10/2026).
- **D17 — Datos fijos** (Task 4): los de `DatosCalendario`, `DatosControlPoa` y `DatosAdjuntos` (abajo, en la Task 4). Reloj fijo 06/10/2026 para el Ejercicio 2026.
- **D18 — Mockups a aprobar (a 1280×760 y 1366×768):** `finanzas/calendario-pagos.html`, `-cargando`, `-vacio`, `-sin-conexion`; `finanzas/control-poa.html`, `-cargando`, `-vacio`, `-sin-conexion`, `-sobreejecutado`; `finanzas/adjuntos-vacio.html` (el panel en una card de 760 sobre fondo app, el mismo marco que arma `DatosAdjuntos.EnTarjeta`); `inventario/ingreso-factura-guardado.html` actualizado con el panel real. No se mockean tooltips, desplegables ni el estado sin permiso.

## Global Constraints

- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, acento `#1D4ED8`, peligro texto `#B91C1C`, texto primario `#1C1917`, texto secundario `#57534E`. Fila de grilla 40, header de grilla 36, input y botón 36. Espaciado múltiplo de 4. Sidebar 240: la vista mide 1040 a 1280 y 1126 a 1366.
- **`tokens.json` es la fuente de `Tokens.axaml`; nunca editar `Tokens.axaml` a mano.** Esta fase no prevé tokens nuevos. Toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- **Formato es-UY** (decisión del usuario, 2026-10-01; el cliente es un municipio uruguayo): importes con `MonedaConverter` / `FormatoEsUy.Moneda` ("$ 1.200.000,00", "-$ 42.900,00"), porcentaje "47,30 %" (`FormatoEsUy.Decimal` + " %"), tamaño "1,2 MB" / "850 KB", fechas "dd/MM/yyyy". Los tests de conversores corren también con la cultura ambiente en `en-US`.
- **`x:DataType` / compiled bindings:** un binding mal escrito es error de build, no un null silencioso; cada `DataTemplate` lleva su `x:DataType` (`dto:FacturaCalendarioDto`, `dto:PagoRecienteDto`, `dto:ControlPoaLineaDto`, `dto:AdjuntoDto`).
- **Sin comentarios con `--` en `.axaml` ni `.csproj`** (AVLN1001, cascadea a MSB4025).
- **Un solo primario por vista** (§3.3): Calendario y Control POA no tienen primario; el panel de adjuntos tampoco (sus hosts sí).
- **VMs: solo E1 a E5.** `AdjuntosPanelViewModel` (E3, E5), `CalendarioPagosViewModel` (E1), `ControlPoaViewModel` (E1, E2) y el override `NotificarPermisosActualizados` de `GastoFormViewModel`, `PagosGastoViewModel` e `IngresoPorFacturaViewModel` (E5). Ningún otro VM. No cambian `FinanzasVistasService`, `ViewLocator`, `INavigationService`, la navegación Registrar pago → `PagosGastoViewModel` con `ConfigurarVolver`, los exportes CSV/PDF (con el aviso de más de 500 filas), la validación de adjuntos ni "Ver" por archivo temporal.
- **Gates de UI con la View real y gates de permisos con un Operador de permisos MIXTOS** (`new SesionFake(RolUsuario.Operador, …)`), nunca con Admin (cortocircuita el OR). El 403 en caliente se prueba con el camino real: `ManejadorAccesoRevocado.ManejarAsync(...)` → `ShellMainViewModel.NotificarPermisosActualizados` (patrón de `AccesoRevocadoPantallasFinanzasTests`). Un test de VM NO custodia un `IsVisible` del XAML.
- **TDD en cada task:** test primero, correrlo y PEGAR el rojo real, código mínimo, verde.
- **Mutaciones QUITANDO, nunca agregando:** `export SCRATCH=~/.cache/stockapp-ui-v2-fase-4a && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`); `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO líneas quitadas; se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. **NUNCA `git checkout --` ni `git restore`.**
- **Stubs fieles al contrato real** (confirmar con `src/StockApp.Application/Finanzas/FinanzasVistasDtos.cs` y `AdjuntoDto.cs` antes de escribir uno): `FacturaCalendarioDto(int GastoId, string ProveedorNombre, string? NumeroFactura, decimal SaldoPendiente, DateOnly? FechaVencimiento, string Estado)`, `PagoRecienteDto(int GastoId, string ProveedorNombre, string? NumeroFactura, DateOnly FechaPago, decimal Monto)`, `CalendarioPagosDto(Vencidas, AVencer7Dias, AVencer30Dias, PagosRecientes)`, `ControlPoaLineaDto(int LineaPoaId, string Nombre, string Programa, int Ejercicio, decimal Presupuesto, decimal Gastado, decimal Saldo, decimal PorcentajeEjecucion, bool Sobregirada)`, `AdjuntoDto(int Id, string NombreArchivo, string ContentType, long TamanoBytes, int? GastoId, int? PagoGastoId, DateTime FechaAltaUtc)`. Los fakes lanzan lo que lanza `ApiErrores`: `UnauthorizedAccessException` para 401/403 y `ServidorNoDisponibleException` sin servidor.
- Mockups (§6.1): `docs/ui-v2/mockups/finanzas/*.html` (carpeta nueva) e `inventario/ingreso-factura-guardado.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a los `Datos*`. Solo colores sólidos, bordes de 1 px, radios, una `BoxShadow`, Inter, px enteros, múltiplos de 4. Celdas numéricas con `v2-num` y el valor dentro de un `<span>`. Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado(TeselasDeTono, AprobadoPor, "yyyy-MM-dd", Motivo)` tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto; antialias contra diferencia real se diagnostica con `teselas.py` y `tinta.py` (plan de la 3a, "Herramientas de diagnóstico"; copiarlos a `$SCRATCH/diag/`, nunca se commitean).
- Convención 6: las pantallas se comparan DENTRO del shell real (`ShellMainView`, `DatosShell.Crear(usuario, permisos, "CalendarioPagos" | "ControlPoa", vm)`) con el sidebar oculto en los dos renders (`ProductosComparacionTests.OcultarSidebar`, `ocultarEnMockup: [".v2-sidebar"]`).
- Compensaciones conocidas Chromium/Avalonia (§6.5): Chromium colapsa márgenes verticales y Avalonia los suma; texto centrado con ancho fraccionario pide `UseLayoutRounding="False"` (los `EstadoVacio` lo llevan por instancia); una compensación nueva se mide, se anota con su antes y después y entra a §6.5 en la Task 10.
- **Build obligatorio** tras cada cambio de XAML o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). **Tests con `dotnet test StockApp.sln` o con UN `.csproj` por comando, nunca varios `.csproj` juntos.** Nunca dos `dotnet test` a la vez (comparten `obj/`: rojos falsos).
- **En este worktree los rojos de `StockApp.Infrastructure.Tests` y `StockApp.Api.Tests` por fixtures `.ods` ausentes son FALSOS conocidos** (el worktree no copia los fixtures): confirmarlos por nombre (tests que leen `*.ods`) y anotarlos; cualquier otro rojo es real. Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 4", "D9", "E5" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits, la spec y `herramientas.md` sí pueden citar el plan.
- **Línea base de la suite:** se mide en la Task 1 Step 1 con `dotnet test StockApp.sln` en el commit de este plan (la última anotada: 5595/5595 en `c5c9a14`, cierre de la 3c-3). Cada task anota en el ledger el total tras su commit; el total solo sube salvo los tests heredados que se REEMPLAZAN (convención 13), que se listan por nombre con su reemplazo.
- Las referencias de línea (`L123`) son las del commit de este plan. Después de que una task edita un archivo, ubicar el lugar por el texto citado.
- **Scripts del skill de ejecución** (`task-brief`, `review-package`): `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y verificar con `ls -l --time-style=full-iso` que el archivo quedó DENTRO del worktree y con fecha de ahora.
- Verificación GUI: ventana "Gestión Municipal" (proceso `GestionMunicipal`; `pgrep -x GestionMunicipa`, `/proc/<pid>/comm` trunca a 15). `capturar.sh` NO es fail-closed: mirar CADA captura y comparar `md5sum` entre capturas sucesivas. Si el usuario necesita la PC, la verificación GUI se pausa.
- **Commits:** conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). **No mergear a `main`, no pushear**; prohibidos push, merge, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` se mergea a `main` una sola vez, al final de todas las fases.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell real), 10 (listado v2: `EncabezadoPagina` + `TarjetaContenido` + `BarraFiltros` + pie de conteo con `ConteoConverter` sobre `X.Count`), 11 (negativo por token, nunca `Opacity`), 13 (tests heredados se adaptan sin borrarse; un reemplazo custodia lo mismo sobre la View v2), 14 y 19 (columnas `Auto` medidas en headless antes del mockup; `ColumnaAuto` con el mismo conversor de la celda; `NoCede` para fechas), 18 (estados `Cargando`, `SinPermiso`, `SinConexion` con Reintentar que relee el filtro en pantalla), 20 (tooltip solo si se corta), 21 (permisos en caliente con operador mixto y `ShellMainView`), 23 (`ConsultaVigente`: solo la última consulta decide).
- NO aplican: 22 (no son formularios con carga de combos), 24 y 25 (no hay diálogos nuevos: la confirmación de Quitar usa `IConfirmacionService`, que ya es una `Window` v2), 26 y 27 (sin fila de carga ni pie fijo).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 10)

28. **Visibilidad de una columna de `DataGrid` por permiso:** una `DataGridColumn` no recibe el `DataContext` (no está en el árbol lógico), así que un `IsVisible="{Binding Puede…}"` en la columna no se reevalúa. La vista sincroniza `Columna.IsVisible` desde el code-behind al cambiar el `DataContext` y en cada `PropertyChanged` del VM con nombre vacío (el de `NotificarPermisosActualizados`) o el del gate, y el control de la celda conserva su propio `IsVisible` sobre el gate. Primer caso: "Registrar pago" del Calendario de pagos.
29. **Panel embebido con gates:** un VM hijo que vive en una propiedad del host (`AdjuntosPanel`) no se entera del `OnPropertyChanged(string.Empty)` del host (relee la misma instancia); el host sobrescribe `NotificarPermisosActualizados` y lo reenvía al hijo. Los gates del hijo son getters sobre la sesión, nunca un campo calculado una vez.

## Review Focus

1. **Permiso revocado con el panel de adjuntos abierto dentro de un host.** Caso: un operador con `finanzas.gastos` edita una factura, un 403 le quita el permiso; Agregar y Quitar del panel siguen ofreciéndose (hoy pasa: E5) o, al revés, el gate se pierde para el panel de un PAGO cuando el permiso revocado era el de gastos. Esperado: Agregar y Quitar dejan de estar disponibles en el panel del gasto sin navegar, y el panel de un pago depende solo de `finanzas.pagos`. Tests: `Un403_ConLaFacturaEnEdicion_AgregarYQuitarAdjuntoDejanDeEstarDisponibles`, `Un403_QueQuitaPagos_NoTocaElPanelDeAdjuntosDeUnGasto` (Task 1), `AdjuntosPanelViewV2Tests.Gates_OperadorMixto…` (Task 6).
2. **Quitar un adjunto por accidente.** Casos: Quitar sin preguntar (hoy), o la confirmación muestra otro nombre que el de la fila, o Cancelar igual quita. Esperado: pregunta con el nombre del archivo de esa fila; "Cancelar" no llama al servicio ni recarga. Tests: `QuitarAsync_PreguntaConElNombreDelArchivo`, `QuitarAsync_Cancelado_NoQuitaNiRecarga` (Task 1).
3. **Ejercicio que cambia rápido o con texto inválido en Control POA.** Casos: tipear 2024 → 2025 con el servidor lento muestra las líneas de 2024 (la consulta vieja llega última); "20a5" en el `NumericUpDown` recarga con el año anterior; Reintentar tras una caída consulta el año de la consulta que falló y no el que está en pantalla. Esperado: solo la última consulta pinta (convención 23); un texto inválido no cambia `Ejercicio` y no dispara recarga; Reintentar relee `Ejercicio`. Tests: `CambiosRapidos_SoloConsultaElUltimoEjercicio`, `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`, `Reintentar_ConsultaElEjercicioQueEstaEnPantalla` (Task 3), `EjercicioInvalido_NoRecarga` (Task 8).
4. **Servidor caído o sin permiso en Calendario y Control POA.** Casos: la pantalla afirma "No hay facturas vencidas" sin haber consultado; un 403 muestra el modal genérico de error; Reintentar deja las filas de la consulta anterior mientras espera. Esperado: `SinConexion` con "Reintentar" y sin filas viejas, "Sin permiso" tras un 403, y los vacíos solo con la carga terminada bien. Tests: `ServidorCaido_…`, `Un403_…`, `Cargando_…` en los VM tests (Tasks 2 y 3) y en `CalendarioPagosViewV2Tests` / `ControlPoaViewV2Tests` (Tasks 7 y 8).
5. **Columna "Registrar pago" con permisos mixtos y revocación en caliente.** Casos: un operador con `finanzas.ver` sin `finanzas.pagos` ve la columna (o una columna vacía de 140 px); tras un 403 que quita `finanzas.pagos` la columna sigue. Esperado: sin el permiso la columna no existe en ninguna de las tres grillas, tampoco tras el 403, y vuelve en la próxima navegación si se recupera. Tests: `SinPagos_LaColumnaRegistrarPagoNoSeVeEnNingunaGrilla`, `Un403_QueQuitaPagos_OcultaLaColumnaSinNavegar` (Task 7).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| E5: por qué hace falta el reenvío | `ShellMainViewModel.NotificarPermisosActualizados` llama SOLO a la pantalla abierta (`CurrentContent?.NotificarPermisosActualizados()`); la base hace `OnPropertyChanged(string.Empty)`, que relee `AdjuntosPanel` (la misma instancia: el `ContentControl` no cambia de `DataContext`) y nunca toca los bindings del panel. Ningún host sobrescribe el método hoy. | `ShellMainViewModel.cs` L431-434; `ViewModelBase.cs` L208; `GastoFormView.axaml` L111, `PagosGastoView.axaml` L113, `IngresoPorFacturaView.axaml` L251. |
| E5: el permiso por tipo | `InicializarAsync` fija el permiso que corresponde (`RegistrarGastos` con `gastoId`, `RegistrarPagos` con `pagoGastoId`) y `PuedeModificar` lo evalúa contra `PermisosActuales` en cada lectura. Antes de inicializar es false (igual que hoy). `PagosGastoViewModel` reinicializa el panel al cambiar de pago (`OnPagoSeleccionadoChanged`). | `AdjuntosPanelViewModel.cs` L50-59; `PagosGastoViewModel.cs` L95. |
| E3: confirmación | `IConfirmacionService.PreguntarAsync(string)` (un parámetro, botones por defecto). Los mocks de Moq sin `Setup` devuelven false: `QuitarAsync_LlamaAlServicioYRecarga` (heredado) pasa a configurar `PreguntarAsync` en true (convención 13, se adapta sin borrarse). | `IConfirmacionService.cs`; `AdjuntosPanelViewModelTests.cs` L85-98. |
| E1: carga protegida | `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` + `ConsultaVigente` + `AvisaSinConexionEnLinea => true` + `AlPerderConexion` vacía las colecciones, como Productos. El `catch (UnauthorizedAccessException)` silencioso actual se reemplaza: un 403 marca `SinPermiso` (sin modal nuevo) y un servidor caído ya no sale por el modal global. Cualquier otra excepción se sigue repropagando. | `ProductoListViewModel.cs` L38-44, L98-109; `ViewModelBase.cs` L232-300. |
| E2: debounce | `EsperaDebounce` interna (300 ms), `_tareaDebounce` interna para que los tests la esperen y `CancellationTokenSource` por cambio, copia de Productos. `Presentation.Tests` y `Presentation.UiTests` ven los `internal` (`InternalsVisibleTo`); `VisualTests` no, por eso el Ejercicio inicial sale de un `TimeProvider` opcional (patrón de `InicioViewModel`) y los datos fijos no tocan el debounce. | `ProductoListViewModel.cs` L24-28, L117-142; `StockApp.Presentation.csproj` L33-42; `InicioViewModel.cs` L231-239; `herramientas.md` ("Fechas en mockups con reloj inyectable"). |
| E2: `RecargarCommand` de Control POA | Se borra (YAGNI): nadie más lo usa (grep: solo `ControlPoaView.axaml` L18) y `ReintentarCargaCommand` de la base cubre la recarga tras una caída. `ComandosDeGuardadoGuardianTests` lo descubría por nombre; al no existir, no queda nada que custodiar. Calendario conserva el suyo ("Actualizar"). | grep `RecargarCommand` en `src/` y `tests/`. |
| Reintentar con el filtro actual | `ControlPoaViewModel` sobrescribe `ReintentarAsync` con `CargarAsync()` (lee `Ejercicio` al empezar), como Historial (convención 18). Calendario no tiene filtros: el `ReintentarAsync` por defecto alcanza. | `ViewModelBase.cs` L140-147; convención 18. |
| Columna por permiso | Code-behind (D4, convención 28 nueva); el `DataGrid` resuelve la columna por índice (`Columns[^1]`), sin `x:Name` en la columna. | `CalendarioPagosView.axaml` (no hay ningún `DataGridColumn` con `IsVisible` bindeado en `src/`: grep). |
| Barra de ejecución | Control propio `v2:BarraEjecucion : Control` con `Render` (dos rectángulos redondeados) y `ControlTheme` en `ComponentesV2.axaml`; el relleno que usa lo expone `RellenoVigente` para que el test lo lea. No `ProgressBar` de Fluent: su plantilla trae alturas mínimas y animaciones que no se fijan contra un mockup. | `Controls/V2/ComponentesV2.axaml` (patrón `Insignia`). |
| Tamaño legible | `TamanoLegibleConverter.Formatear(long)`: menos de 1024 → "N B"; KB redondeado sin decimales mientras sea menor a 1024; si no, MB con hasta un decimal ("0.#", `FormatoEsUy.Formato`). 182.400 → "178 KB"; 870.400 → "850 KB"; 1.258.291 → "1,2 MB"; 10.485.760 → "10 MB". | `FormatoEsUy.cs` L29-48. |
| Panel en los hosts v1 | La raíz del panel lleva `Classes="ui2"` (excepción inversa temporal de la spec). `GuardianDePatronTests` lo mueve de `VistasEmbebidas` a `VistasMigradasAV2`. Ningún contenedor que hospede vistas viejas lleva `ui2`: el panel es hoja. | `GuardianDePatronTests.cs` L146-160, L206-222; `herramientas.md` (convivencia). |
| Guardianes de vistas viejas | `ControlPoaView` y `CalendarioPagosView` salen de las `InlineData` del `HeaderVista` (L68, L70) y de `VistasDeLaTanda` (L120, L122); los reemplaza `Raiz_TieneUi2_EncabezadoConTitulo` en su test v2 (convención 13). | `GuardianDePatronTests.cs`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Converters/TamanoLegibleConverter.cs` | Bytes → "178 KB" / "1,2 MB" (E4). |
| `src/StockApp.Presentation/Converters/IconoAdjuntoConverter.cs` | `ContentType` → nombre del ícono MDI. |
| `src/StockApp.Presentation/Converters/FechaCortaConverter.cs` | `DateOnly` / `DateOnly?` → "dd/MM/yyyy" (null → "—"). |
| `src/StockApp.Presentation/Converters/PorcentajeConverter.cs` | `decimal` → "47,30 %". |
| `src/StockApp.Presentation/Controls/V2/BarraEjecucion.cs` | Barra fina de ejecución (D10). |
| `tests/StockApp.Presentation.Tests/Converters/TamanoLegibleConverterTests.cs`, `IconoAdjuntoConverterTests.cs`, `FechaCortaConverterTests.cs`, `PorcentajeConverterTests.cs` | Los conversores. |
| `tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs` | `FinanzasVistasFake` configurable (lista, pendiente, falla) y `GastoServiceSinUsoFake`. |
| `tests/StockApp.Presentation.UiTests/V2/BarraEjecucionTests.cs` | El componente con el tema real. |
| `tests/StockApp.Presentation.UiTests/V2/AdjuntosPanelViewV2Tests.cs`, `CalendarioPagosViewV2Tests.cs`, `ControlPoaViewV2Tests.cs` | Las vistas reales: estados, gates, columnas, conversores en pantalla. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosCalendario.cs`, `DatosControlPoa.cs`, `DatosAdjuntos.cs`, `DatosFinanzasTests.cs` | Datos fijos de los mockups. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/CalendarioComparacionTests.cs`, `ControlPoaComparacionTests.cs`, `AdjuntosComparacionTests.cs` | Comparaciones a los dos tamaños. |
| `docs/ui-v2/mockups/finanzas/*.html` | Los 10 mockups de D18. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs` | E3, E5. |
| `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs`, `PagosGastoViewModel.cs`, `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs` | Override `NotificarPermisosActualizados` que reenvía al panel (E5). |
| `src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs` | E1. |
| `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs` | E1, E2, `TimeProvider` opcional. |
| `src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml`, `CalendarioPagosView.axaml` (+ `.axaml.cs`), `ControlPoaView.axaml` | Reescritas en v2 (el `.axaml.cs` de Control POA y del panel no cambian). |
| `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` | `ControlTheme` de `BarraEjecucion`. |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-barra-ejecucion` (espejo) y lo que pidan los mockups (se declara en el GATE). |
| `docs/ui-v2/mockups/inventario/ingreso-factura-guardado.html` | Panel real en lugar de `.panel-adjuntos-v1`. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/IngresoFacturaComparacionTests.cs`, `DatosIngresoFactura.cs` | Sin la exclusión del panel v1 (`OcultarPanelAdjuntosV1` se borra). |
| `tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs` | `AdjuntoServiceFake` con lista configurable y `QuitarAsync` que registra. |
| Tests heredados (convención 13) | `AdjuntosPanelViewModelTests`, `CalendarioPagosViewModelTests`, `ControlPoaViewModelTests`, `AccesoRevocadoPantallasFinanzasTests` (tests nuevos), `GuardianDePatronTests` (L68, L70, L120, L122, L157, `VistasMigradasAV2`), `SignoNegativoBadgeTests` (Control POA → `Insignia`), `EtiquetadoDeCamposTests` (acepta `v2:CampoFormulario`), `GrillasSinRecorteBarridoTests` (escenario "Control POA", si la medición lo pide). |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, `docs/ui-v2/herramientas.md` | Cierre (Task 10). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln` (rojos `.ods` de `Infrastructure.Tests`/`Api.Tests`: falsos conocidos del worktree, confirmar por nombre)
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de Finanzas (tras las Tasks 1, 6, 7 y 8): `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Adjuntos|FullyQualifiedName~Calendario|FullyQualifiedName~ControlPoa|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~DataGridCeldaConControl|FullyQualifiedName~EtiquetadoDeCampos|FullyQualifiedName~IngresoPorFactura|FullyQualifiedName~PagosGasto|FullyQualifiedName~ShellMainViewGates"` y `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~Finanzas|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~ColumnasPdfContraDtosReales|FullyQualifiedName~IngresoPorFactura"`
- Comparación con página: `scripts/ui-v2/comparar.sh Calendario` (y `ControlPoa`, `Adjuntos`, `IngresoFactura`) y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-11-ui-v2-fase-4a/progress.md` (lo crea la Task 1 con BASE = el commit de este plan; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque A — ViewModels y conversores, sin tocar vistas

### Task 1: Panel de adjuntos: confirmación al quitar, tamaño legible y `PuedeModificar` que se refresca (E3, E4, E5)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs`, `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs`, `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs`
- Create: `src/StockApp.Presentation/Converters/TamanoLegibleConverter.cs`, `src/StockApp.Presentation/Converters/IconoAdjuntoConverter.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs`, `GastoFormViewModelTests.cs`, `PagosGastoViewModelTests.cs`, `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/Converters/TamanoLegibleConverterTests.cs`, `IconoAdjuntoConverterTests.cs`
- Test: `tests/StockApp.Presentation.UiTests/AccesoRevocadoPantallasFinanzasTests.cs`; Modify: `tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `IConfirmacionService.PreguntarAsync(string)`, `ICurrentSession.PermisosActuales` / `RolActual`, `Permisos.RegistrarGastos` / `RegistrarPagos`, `ViewModelBase.NotificarPermisosActualizados()` (virtual), `ManejadorAccesoRevocado.ManejarAsync(...)`.
- Produces: `AdjuntosPanelViewModel.PuedeModificar` (getter, ya no `[ObservableProperty]`); `AdjuntosPanelViewModel.MensajeConfirmarQuitar(string nombreArchivo)` (`public static string`); overrides `NotificarPermisosActualizados()` en los tres hosts; `TamanoLegibleConverter.Instance` / `TamanoLegibleConverter.Formatear(long)`; `IconoAdjuntoConverter.Instance` / `IconoAdjuntoConverter.Icono(string? contentType)`; `AdjuntoServiceFake.Items` (`List<AdjuntoDto>`) y `AdjuntoServiceFake.Quitados` (`List<int>`).

- [ ] **Step 1: Línea base de la suite y ledger**

```bash
cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && mkdir -p .superpowers/sdd/2026-10-11-ui-v2-fase-4a && git rev-parse --short HEAD
dotnet test StockApp.sln 2>&1 | tee "$HOME/.cache/stockapp-ui-v2-fase-4a-linea-base.log" | grep -E "Passed!|Failed!|Failed "
```

Crear `.superpowers/sdd/2026-10-11-ui-v2-fase-4a/progress.md` con `BASE: <hash>`, "Línea base 4a: <total> (<verdes>/<total>)" y los rojos `.ods` POR NOMBRE (falsos conocidos del worktree). Cualquier otro rojo: PARAR y reportar.

- [ ] **Step 2: Tests de VM (rojo)**

En `AdjuntosPanelViewModelTests.cs`, adaptar el heredado (convención 13: misma custodia, ahora con la confirmación aceptada) y agregar:

```csharp
    [Fact]
    public async Task QuitarAsync_LlamaAlServicioYRecarga()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.SetupSequence(a => a.ListarPorGastoAsync(5))
            .ReturnsAsync(new List<AdjuntoDto> { item })
            .ReturnsAsync(new List<AdjuntoDto>());
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(1), Times.Once);
        Assert.Empty(_vm.Items);
    }

    [Fact]
    public async Task QuitarAsync_PreguntaConElNombreDelArchivo()
    {
        var item = new AdjuntoDto(1, "factura-A-0004521.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto> { item });
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _confirmacion.Verify(c => c.PreguntarAsync("¿Quitar el adjunto \"factura-A-0004521.pdf\"?"), Times.Once);
        Assert.Equal("¿Quitar el adjunto \"factura-A-0004521.pdf\"?", AdjuntosPanelViewModel.MensajeConfirmarQuitar("factura-A-0004521.pdf"));
    }

    [Fact]
    public async Task QuitarAsync_Cancelado_NoQuitaNiRecarga()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto> { item });
        _confirmacion.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(It.IsAny<int>()), Times.Never);
        _adjuntos.Verify(a => a.ListarPorGastoAsync(5), Times.Once); // solo la carga inicial
        Assert.Single(_vm.Items);
    }

    [Fact]
    public async Task PuedeModificar_SeRecalculaConLosPermisosActuales_SinVolverAInicializar()
    {
        var permisos = new HashSet<string> { Permisos.RegistrarGastos };
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(() => permisos);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);
        Assert.True(_vm.PuedeModificar);

        permisos.Remove(Permisos.RegistrarGastos);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task PuedeModificar_PanelDeGasto_NoDependeDelPermisoDePagos()
    {
        var permisos = new HashSet<string> { Permisos.RegistrarGastos, Permisos.RegistrarPagos };
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Operador);
        _session.Setup(s => s.PermisosActuales).Returns(() => permisos);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);

        permisos.Remove(Permisos.RegistrarPagos);

        Assert.True(_vm.PuedeModificar);
    }

    [Fact]
    public void PuedeModificar_AntesDeInicializar_EsFalse_AunSiendoAdmin()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        Assert.False(_vm.PuedeModificar);
    }

    [Fact]
    public async Task InicializarAsync_AvisaElCambioDePuedeModificar()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>());
        var avisos = new List<string?>();
        _vm.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        await _vm.InicializarAsync(5, null);

        Assert.Contains(nameof(AdjuntosPanelViewModel.PuedeModificar), avisos);
    }
```

En `GastoFormViewModelTests.cs` y `PagosGastoViewModelTests.cs` (sus `Crear()` ya existen), y en `IngresoPorFacturaViewModelTests.cs` (con `CrearConDialogos()`):

```csharp
    [Fact]
    public void NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos()
    {
        var (vm, _, _, _) = Crear();   // PagosGasto: var (vm, _, _, _, _) = Crear();  IngresoPorFactura: var (vm, _, _, _, _) = CrearConDialogos();
        var avisos = new List<string?>();
        vm.AdjuntosPanel.PropertyChanged += (_, e) => avisos.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(avisos, n => string.IsNullOrEmpty(n) || n == nameof(AdjuntosPanelViewModel.PuedeModificar));
    }
```

(usar la tupla de cada archivo tal como la devuelve su helper; el cuerpo es el mismo en los tres).

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AdjuntosPanelViewModelTests|FullyQualifiedName~NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos"`
Expected: FAIL de compilación (`MensajeConfirmarQuitar` no existe). Pegar. Comentar temporalmente esa línea del assert para ver los rojos de comportamiento: `QuitarAsync_PreguntaConElNombreDelArchivo` (PreguntarAsync nunca llamado), `QuitarAsync_Cancelado_NoQuitaNiRecarga` (QuitarAsync llamado), `PuedeModificar_SeRecalcula…` (sigue true), los tres `…ReenviaAlPanelDeAdjuntos` (sin avisos); `PuedeModificar_PanelDeGasto_NoDepende…` y `PuedeModificar_AntesDeInicializar…` pasan (custodias, se anota). Pegar y descomentar.

- [ ] **Step 3: Tests de conversores (rojo)**

`tests/StockApp.Presentation.Tests/Converters/TamanoLegibleConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class TamanoLegibleConverterTests
{
    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(850L, "850 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1024L, "1 KB")]
    [InlineData(182_400L, "178 KB")]
    [InlineData(870_400L, "850 KB")]
    [InlineData(1_048_064L, "1 MB")]
    [InlineData(1_258_291L, "1,2 MB")]
    [InlineData(10_485_760L, "10 MB")]
    public void Formatea_EnEsUy(long bytes, string esperado) =>
        Assert.Equal(esperado, TamanoLegibleConverter.Formatear(bytes));

    [Fact]
    public void ConLaCulturaAmbienteEnIngles_SigueConComa()
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("en-US");
        try
        {
            Assert.Equal("1,2 MB", TamanoLegibleConverter.Instance.Convert(1_258_291L, typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Null_DaVacio() =>
        Assert.Equal(string.Empty, TamanoLegibleConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
```

`tests/StockApp.Presentation.Tests/Converters/IconoAdjuntoConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class IconoAdjuntoConverterTests
{
    [Theory]
    [InlineData("application/pdf", "mdi-file-pdf-box")]
    [InlineData("image/jpeg", "mdi-file-image-outline")]
    [InlineData("image/png", "mdi-file-image-outline")]
    [InlineData("application/octet-stream", "mdi-file-outline")]
    [InlineData(null, "mdi-file-outline")]
    public void IconoPorTipo(string? contentType, string esperado) =>
        Assert.Equal(esperado, IconoAdjuntoConverter.Instance.Convert(contentType, typeof(string), null, CultureInfo.InvariantCulture));
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~TamanoLegible|FullyQualifiedName~IconoAdjunto"` → FAIL de compilación. Pegar.

- [ ] **Step 4: Test de UI con la View real (rojo): el panel dentro de un host, 403 en caliente, operador mixto**

En `IngresoPorFacturaFakes.cs`, `AdjuntoServiceFake` gana lista y registro (los métodos no usados siguen lanzando):

```csharp
internal sealed class AdjuntoServiceFake : IAdjuntoService
{
    /// <summary>Lo que devuelven los listados (por gasto y por pago). Vacía por defecto.</summary>
    public List<AdjuntoDto> Items { get; } = new();
    public List<int> Quitados { get; } = new();

    public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(Items.ToList());
    public Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(Items.ToList());
    public Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId) => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task QuitarAsync(int adjuntoId)
    {
        Quitados.Add(adjuntoId);
        Items.RemoveAll(a => a.Id == adjuntoId);
        return Task.CompletedTask;
    }
}
```

En `AccesoRevocadoPantallasFinanzasTests.cs`, junto a `Un403_ConElFormularioDeGastoAbierto_OcultaGuardarSinBorrarLoTipeado`:

```csharp
    /// <summary>
    /// E5: el panel de adjuntos es un VM hijo del formulario. El 403 llega al formulario (pantalla abierta) y el formulario
    /// tiene que reenviarlo al panel: su OnPropertyChanged(string.Empty) relee AdjuntosPanel, que es la misma instancia,
    /// y los bindings del panel no se reevalúan. "Disponible" = visible y habilitado: el panel viejo deshabilitaba y el v2
    /// oculta. Operador de permisos mixtos: Admin cortocircuita el gate.
    /// </summary>
    [AvaloniaFact]
    public async Task Un403_ConLaFacturaEnEdicion_AgregarYQuitarAdjuntoDejanDeEstarDisponibles()
    {
        var (ventana, shell, sesion, tras) = MontarShell(FormularioEnEdicionConUnAdjunto);

        Assert.True(Disponible(BotonDelPanel(ventana, "Agregar")), "Precondición: con finanzas.gastos, Agregar adjunto está disponible.");
        Assert.True(Disponible(BotonDelPanel(ventana, "Quitar")), "Precondición: con finanzas.gastos, Quitar adjunto está disponible.");

        await Disparar403(ventana, shell, sesion, tras);

        Assert.False(Disponible(BotonDelPanel(ventana, "Agregar")));
        Assert.False(Disponible(BotonDelPanel(ventana, "Quitar")));
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaPagos_NoTocaElPanelDeAdjuntosDeUnGasto()
    {
        var (ventana, shell, sesion, tras) = MontarShell(FormularioEnEdicionConUnAdjunto,
            permisosAntes: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos, Permisos.RegistrarPagos },
            permisosTras: new[] { Permisos.VerFinanzas, Permisos.RegistrarGastos });

        await Disparar403(ventana, shell, sesion, tras);

        Assert.True(Disponible(BotonDelPanel(ventana, "Agregar")), "El panel de un gasto depende de finanzas.gastos, no de finanzas.pagos.");
        Assert.True(Disponible(BotonDelPanel(ventana, "Quitar")));
    }

    private static GastoFormViewModel FormularioEnEdicionConUnAdjunto(SesionFake s)
    {
        var servicioAdjuntos = new AdjuntoServiceFake();
        servicioAdjuntos.Items.Add(new AdjuntoDto(1, "factura-A-0001234.pdf", "application/pdf", 182_400, 42, null,
            new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc)));
        var adjuntos = new AdjuntosPanelViewModel(
            servicioAdjuntos, new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), new ConfirmacionServiceFake(), s);
        var form = new GastoFormViewModel(
            new GastoServiceFake(), s,
            new ProveedorServiceFake(Array.Empty<Proveedor>()),
            new FuenteFinanciamientoServiceFake(Array.Empty<FuenteFinanciamiento>()),
            new RubroGastoServiceFake(Array.Empty<RubroGasto>()),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            new NavigationServiceFake(), new ConfirmacionServiceFake(), adjuntos);
        form.CargarParaEditar(new Gasto
        {
            Id = 42, ProveedorId = 1, Detalle = "Materiales de obra", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = 1, RubroGastoId = 1,
        });
        return form;
    }

    private static Button BotonDelPanel(Window ventana, string texto) =>
        ventana.GetVisualDescendants().OfType<StockApp.Presentation.Views.Finanzas.AdjuntosPanelView>().Single()
            .GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static bool Disponible(Button boton) => boton.IsEffectivelyEnabled && ArbolVisual.EsVisibleEnArbol(boton);
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Un403_ConLaFacturaEnEdicion|FullyQualifiedName~Un403_QueQuitaPagos_NoTocaElPanel"`
Expected: `Un403_ConLaFacturaEnEdicion_…` FAIL en `Assert.False(Disponible(… "Agregar"))` (el panel no se entera); `Un403_QueQuitaPagos_…` PASS (custodia de la especificidad, se anota). Pegar.

- [ ] **Step 5: Implementación**

`AdjuntosPanelViewModel.cs`: borrar el `[ObservableProperty] private bool _puedeModificar;` y reemplazar `InicializarAsync` y `QuitarAsync`:

```csharp
    /// <summary>Permiso que habilita Agregar y Quitar: el de gastos para el panel de una factura, el de pagos para el
    /// de un pago. Null hasta <see cref="InicializarAsync"/>.</summary>
    private string? _permisoParaModificar;

    /// <summary>
    /// Gate de Agregar y Quitar. Getter sobre la sesión (no un valor calculado una vez): un 403 que cambia los permisos
    /// con el panel abierto lo re-evalúa por <see cref="ViewModelBase.NotificarPermisosActualizados"/>, que el host le
    /// reenvía (el panel no es la pantalla abierta: el shell solo le avisa al host).
    /// </summary>
    public bool PuedeModificar =>
        _permisoParaModificar is { } permiso
        && (_session.RolActual == RolUsuario.Admin || _session.PermisosActuales.Contains(permiso));

    public static string MensajeConfirmarQuitar(string nombreArchivo) => $"¿Quitar el adjunto \"{nombreArchivo}\"?";

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;
        _permisoParaModificar = gastoId is int ? Permisos.RegistrarGastos : Permisos.RegistrarPagos;
        OnPropertyChanged(nameof(PuedeModificar));

        await RecargarAsync();
    }
```

```csharp
    /// <summary>Pide confirmación antes de quitar: es una baja lógica, pero el adjunto deja de verse en la factura.</summary>
    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDto item)
    {
        if (!await _confirmacion.PreguntarAsync(MensajeConfirmarQuitar(item.NombreArchivo)))
            return;

        try
        {
            await _adjuntos.QuitarAsync(item.Id);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
```

En `GastoFormViewModel`, `PagosGastoViewModel` e `IngresoPorFacturaViewModel` (cada uno tiene el campo `_adjuntosPanel`), un override nuevo junto a la propiedad `AdjuntosPanel`:

```csharp
    /// <summary>
    /// Un 403 refrescó los permisos con la pantalla abierta: además de los Puede* propios (base), se reenvía al panel de
    /// adjuntos. El shell solo avisa a la pantalla abierta, y el OnPropertyChanged(string.Empty) de la base relee
    /// AdjuntosPanel (la misma instancia), así que los gates del panel no se re-evaluarían.
    /// </summary>
    public override void NotificarPermisosActualizados()
    {
        base.NotificarPermisosActualizados();
        _adjuntosPanel.NotificarPermisosActualizados();
    }
```

`src/StockApp.Presentation/Converters/TamanoLegibleConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Tamaño de un adjunto para leer de un vistazo, en es-UY: "850 B", "178 KB", "1,2 MB". KB sin decimales; MB con hasta
/// uno. El separador decimal es el de es-UY sin importar la cultura de la máquina.
/// </summary>
public sealed class TamanoLegibleConverter : IValueConverter
{
    public static readonly TamanoLegibleConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is long bytes ? Formatear(bytes) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(long bytes)
    {
        if (bytes < 1024)
            return bytes.ToString("0", FormatoEsUy.Formato) + " B";

        var kb = Math.Round(bytes / 1024m, 0, MidpointRounding.AwayFromZero);
        if (kb < 1024)
            return kb.ToString("0", FormatoEsUy.Formato) + " KB";

        return (bytes / (1024m * 1024m)).ToString("0.#", FormatoEsUy.Formato) + " MB";
    }
}
```

`src/StockApp.Presentation/Converters/IconoAdjuntoConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>Ícono MDI por tipo de adjunto: PDF o imagen (los únicos que acepta el servidor), y uno genérico por las dudas.</summary>
public sealed class IconoAdjuntoConverter : IValueConverter
{
    public static readonly IconoAdjuntoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Icono(value as string);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Icono(string? contentType) => contentType switch
    {
        "application/pdf" => "mdi-file-pdf-box",
        { } t when t.StartsWith("image/", StringComparison.OrdinalIgnoreCase) => "mdi-file-image-outline",
        _ => "mdi-file-outline",
    };
}
```

El `AdjuntosPanelView.axaml` v1 NO cambia en esta task: sus dos `IsEnabled="{Binding … PuedeModificar}"` siguen compilando contra el getter.

- [ ] **Step 6: Verde, build y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~AdjuntosPanelViewModelTests|FullyQualifiedName~NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos|FullyQualifiedName~TamanoLegible|FullyQualifiedName~IconoAdjunto|FullyQualifiedName~LoginViewModelTests|FullyQualifiedName~ComandosDeGuardado"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AccesoRevocado|FullyQualifiedName~FinanzasRevocacion|FullyQualifiedName~IngresoPorFactura|FullyQualifiedName~PagosGasto|FullyQualifiedName~ViewLocator"
```

Expected: 0 errores, todo verde. (`LoginViewModelTests` L181 cita `PuedeModificar` en un comentario: confirmar que su test sigue verde.)

- [ ] **Step 7: Mutaciones (QUITANDO)**

- (a) En `GastoFormViewModel.NotificarPermisosActualizados`, quitar la línea `_adjuntosPanel.NotificarPermisosActualizados();` → rojo `Un403_ConLaFacturaEnEdicion_…` (UI) y `NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos` (VM de Gasto). Pegar los dos. Restaurar.
- (b) En `AdjuntosPanelViewModel.QuitarAsync`, quitar las dos líneas del `if (!await _confirmacion.PreguntarAsync(…)) return;` → rojo `QuitarAsync_Cancelado_NoQuitaNiRecarga` y `QuitarAsync_PreguntaConElNombreDelArchivo`. Restaurar.
- (c) En `InicializarAsync`, quitar `OnPropertyChanged(nameof(PuedeModificar));` → rojo `InicializarAsync_AvisaElCambioDePuedeModificar`. Restaurar.
- (d) En `PuedeModificar`, quitar el término `_session.RolActual == RolUsuario.Admin ||` → ningún test de esta task da rojo (ninguno de VM monta Admin): se corre en la Task 4 Step 3, donde `DatosFinanzasTests.Adjuntos_VacioYConPermiso` (Admin) tiene que dar rojo. Restaurar.

Cada una con `cp` al `$SCRATCH`, `git diff --no-index` mostrando solo lo quitado, rojo pegado, restauración y `git diff --no-index` vacío.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs src/StockApp.Presentation/Converters/TamanoLegibleConverter.cs src/StockApp.Presentation/Converters/IconoAdjuntoConverter.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs tests/StockApp.Presentation.Tests/Converters/TamanoLegibleConverterTests.cs tests/StockApp.Presentation.Tests/Converters/IconoAdjuntoConverterTests.cs tests/StockApp.Presentation.UiTests/AccesoRevocadoPantallasFinanzasTests.cs tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs
git commit -m "fix(adjuntos): confirmar antes de quitar y refrescar el permiso de modificar con el panel abierto"
```

Ledger: total de la suite tras el commit (`dotnet test StockApp.sln`), rojos (a) a (c) pegados ((d) se pega en la Task 4).

### Task 2: Calendario de pagos con carga protegida, sin conexión y consulta vigente (E1)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs`
- Create: `src/StockApp.Presentation/Converters/FechaCortaConverter.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs`, `tests/StockApp.Presentation.Tests/Converters/FechaCortaConverterTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string, ConsultaVigente.Turno)`, `ConsultaVigente`, `AvisaSinConexionEnLinea`, `AlPerderConexion`, `ReintentarCargaCommand`, `Cargando`, `SinPermiso`, `SinConexion`.
- Produces: `CalendarioPagosViewModel.MensajeSinPermisoCalendario` (`public const string` = "No tenés permiso para ver el calendario de pagos."); `CargarAsync()` con turno; `FechaCortaConverter.Instance` / `FechaCortaConverter.Formatear(DateOnly?)`.

- [ ] **Step 1: Tests (rojo)**

En `CalendarioPagosViewModelTests.cs`, un helper para el servicio con respuestas en orden y los tests:

```csharp
    private static CalendarioPagosDto Calendario(int vencidas) => new(
        Enumerable.Range(1, vencidas).Select(i => new FacturaCalendarioDto(i, $"Proveedor {i}", $"A-{i:0000}", 100m * i,
            new DateOnly(2026, 9, 28), "Pendiente")).ToList(),
        new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>());

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _) = Crear();
        var pendiente = new TaskCompletionSource<CalendarioPagosDto>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(Calendario(2));
        await carga;

        Assert.False(vm.Cargando);
        Assert.Equal(2, vm.Vencidas.Count);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(CalendarioPagosViewModel.MensajeSinPermisoCalendario, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLasSecciones()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null))
            .ReturnsAsync(Calendario(2))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.RecargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Vencidas);
        Assert.Empty(vm.AVencer7Dias);
        Assert.Empty(vm.AVencer30Dias);
        Assert.Empty(vm.PagosRecientes);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(Calendario(3));
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Equal(3, vm.Vencidas.Count);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _) = Crear();
        var vieja = new TaskCompletionSource<CalendarioPagosDto>();
        var nueva = new TaskCompletionSource<CalendarioPagosDto>();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null)).Returns(vieja.Task).Returns(nueva.Task);

        var primera = vm.CargarAsync();
        var segunda = vm.CargarAsync();
        nueva.SetResult(Calendario(3));
        await segunda;
        vieja.SetResult(Calendario(1));
        await primera;

        Assert.Equal(3, vm.Vencidas.Count);
    }
```

Agregar `using StockApp.ApiClient;` si falta. El heredado `CargarAsync_SiObtenerCalendarioPagosLanzaUnauthorized_NoPropagaLaExcepcion` queda igual (sigue custodiando que no propaga).

`tests/StockApp.Presentation.Tests/Converters/FechaCortaConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class FechaCortaConverterTests
{
    [Fact]
    public void DateOnly_ConBarras_SinImportarLaCultura()
    {
        var antes = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");   // separador "."
        try
        {
            Assert.Equal("05/11/2026", FechaCortaConverter.Instance.Convert(new DateOnly(2026, 11, 5), typeof(string), null, CultureInfo.CurrentCulture));
        }
        finally
        {
            CultureInfo.CurrentCulture = antes;
        }
    }

    [Fact]
    public void Null_EsUnGuion() =>
        Assert.Equal("—", FechaCortaConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~CalendarioPagosViewModelTests|FullyQualifiedName~FechaCorta"` → FAIL de compilación (`MensajeSinPermisoCalendario`, `FechaCortaConverter`). Pegar. Con esos dos comentados, pegar los rojos de comportamiento (`Cargando` siempre false, `SinPermiso` false, la caída escapa como excepción, la vieja pisa a la nueva).

- [ ] **Step 2: Implementación**

En `CalendarioPagosViewModel`, reemplazar `CargarAsync` (y su comentario) por:

```csharp
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoCalendario = "No tenés permiso para ver el calendario de pagos.";

    private readonly ConsultaVigente _consultas = new();

    /// <summary>La vista muestra "No se pudo conectar con el servidor" con Reintentar (ver <see cref="ViewModelBase.SinConexion"/>).</summary>
    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor no se muestra el calendario anterior: al reintentar, las secciones no pueden quedar con datos viejos.</summary>
    protected override void AlPerderConexion()
    {
        Vencidas.Clear();
        AVencer7Dias.Clear();
        AVencer30Dias.Clear();
        PagosRecientes.Clear();
    }

    /// <summary>
    /// La dispara la View (DataContextChanged) y "Actualizar". Va por EjecutarCargaProtegidaAsync: un 403 deja SinPermiso
    /// (el aviso global ya lo dio AuthTokenHandler), un servidor caído deja SinConexion en vez del modal global, y mientras
    /// espera Cargando está en true. Pide turno: si mientras espera empezó otra carga, su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var calendario = await _service.ObtenerCalendarioPagosAsync();
            if (!turno.EsLaUltima)
                return;
            Reemplazar(Vencidas, calendario.Vencidas);
            Reemplazar(AVencer7Dias, calendario.AVencer7Dias);
            Reemplazar(AVencer30Dias, calendario.AVencer30Dias);
            Reemplazar(PagosRecientes, calendario.PagosRecientes);
        }, MensajeSinPermisoCalendario, turno);
    }

    private static void Reemplazar<T>(ObservableCollection<T> destino, IEnumerable<T> origen)
    {
        destino.Clear();
        foreach (var item in origen)
            destino.Add(item);
    }
```

(agregar `using System.Collections.Generic;`). `RecargarCommand` no cambia.

`src/StockApp.Presentation/Converters/FechaCortaConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Fecha sin hora ("05/11/2026") con barras fijas: un StringFormat "dd/MM/yyyy" usa el separador de la cultura de la
/// máquina. Null (factura sin vencimiento) → "—". También es el conversor de ColumnaAuto en las columnas de fecha.
/// </summary>
public sealed class FechaCortaConverter : IValueConverter
{
    public static readonly FechaCortaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Formatear(value as DateOnly?);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(DateOnly? fecha)
        => fecha?.ToString("dd'/'MM'/'yyyy", CultureInfo.InvariantCulture) ?? "—";
}
```

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~CalendarioPagos|FullyQualifiedName~FechaCorta|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~InicioViewModel"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Calendario|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~GuardianDePatron"
```

Expected: verde (la vista v1 sigue llamando `CargarAsync` desde `DataContextChanged`).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (e) Quitar `if (!turno.EsLaUltima) return;` (las dos líneas) → rojo `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`. Restaurar.
- (f) Quitar la línea `protected override bool AvisaSinConexionEnLinea => true;` → rojo `ServidorCaido_…` y `Reintentar_…` (la excepción escapa). Restaurar.
- (g) Quitar el cuerpo de `AlPerderConexion` (las cuatro líneas `Clear()`) → rojo `ServidorCaido_TrasUnaCargaBuena_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs src/StockApp.Presentation/Converters/FechaCortaConverter.cs tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs tests/StockApp.Presentation.Tests/Converters/FechaCortaConverterTests.cs
git commit -m "feat(finanzas): el calendario de pagos muestra carga, sin conexión con reintento y sin permiso"
```

### Task 3: Control POA con carga protegida, Ejercicio que recarga solo y reloj inyectable (E1, E2)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs`
- Create: `src/StockApp.Presentation/Converters/PorcentajeConverter.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs`, `tests/StockApp.Presentation.Tests/Converters/PorcentajeConverterTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: lo mismo que la Task 2 más `ReintentarAsync(Func<Task>)` (virtual).
- Produces: constructor `ControlPoaViewModel(IFinanzasVistasService, INavigationService, ICsvExporter, IServicioGuardadoArchivo, IConfirmacionService, IPdfExporter, IServicioAperturaArchivo, ICurrentSession, TimeProvider? reloj = null)`; `MensajeSinPermisoControlPoa` (`public const string` = "No tenés permiso para ver el control POA."); `internal TimeSpan EsperaDebounce`; `internal Task _tareaDebounce`; `RecargarCommand` BORRADO; `PorcentajeConverter.Instance` / `PorcentajeConverter.Formatear(decimal)`.

- [ ] **Step 1: Tests (rojo)**

En `ControlPoaViewModelTests.cs`: el helper `Crear` gana `TimeProvider? reloj = null` (lo pasa al constructor) y se agregan:

```csharp
    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static readonly TimeProvider Octubre2026 = new RelojFijo(new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.Zero));

    private static ControlPoaLineaDto Linea(int id, int ejercicio) =>
        new(id, $"Línea {id}", "Obras", ejercicio, 1000m, 400m, 600m, 40m, false);

    [Fact]
    public void EjercicioInicial_SaleDelReloj_YNoConsulta()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);

        Assert.Equal(2026, vm.Ejercicio);
        svc.Verify(s => s.ObtenerControlPoaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarEjercicio_RecargaSoloConElEjercicioNuevo()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2025) });

        vm.Ejercicio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
    }

    [Fact]
    public async Task CambiosRapidos_SoloConsultaElUltimoEjercicio()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);

        vm.Ejercicio = 2024;
        vm.Ejercicio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerControlPoaAsync(2024), Times.Never);
        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        var vieja = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        var nueva = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(vieja.Task);
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).Returns(nueva.Task);
        vm.EsperaDebounce = TimeSpan.Zero;

        var primera = vm.CargarAsync();
        vm.Ejercicio = 2025;
        nueva.SetResult(new List<ControlPoaLineaDto> { Linea(2, 2025) });
        await vm._tareaDebounce;
        vieja.SetResult(new List<ControlPoaLineaDto> { Linea(1, 2026) });
        await primera;

        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
    }

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        var pendiente = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);
        pendiente.SetResult(new List<ControlPoaLineaDto>());
        await carga;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(ControlPoaViewModel.MensajeSinPermisoControlPoa, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_MarcaSinConexionYVaciaLasFilas()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.SetupSequence(s => s.ObtenerControlPoaAsync(2026))
            .ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2026) })
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task Reintentar_ConsultaElEjercicioQueEstaEnPantalla()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).ThrowsAsync(new ServidorNoDisponibleException());
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2025) });
        await vm.CargarAsync();
        vm.EsperaDebounce = Timeout.InfiniteTimeSpan;   // el cambio queda en pantalla sin que el debounce lo consulte
        vm.Ejercicio = 2025;

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
        Assert.False(vm.SinConexion);
        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
    }
```

(`using StockApp.ApiClient;`, `using System.Threading;`). Si `CargarAsync_PopulaLasFilas` u otro heredado depende de `Ejercicio == DateTime.UtcNow.Year` con el mock `It.IsAny<int>()`, sigue verde; si alguno usa `RecargarCommand`, reemplazarlo por `CargarAsync()` y anotarlo (grep previo: ninguno).

`tests/StockApp.Presentation.Tests/Converters/PorcentajeConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class PorcentajeConverterTests
{
    [Theory]
    [InlineData(47.3, "47,30 %")]
    [InlineData(94.26, "94,26 %")]
    [InlineData(100, "100,00 %")]
    [InlineData(114.3, "114,30 %")]
    [InlineData(0, "0,00 %")]
    public void Formatea_EnEsUy(decimal valor, string esperado) =>
        Assert.Equal(esperado, PorcentajeConverter.Instance.Convert(valor, typeof(string), null, new CultureInfo("en-US")));
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ControlPoaViewModelTests|FullyQualifiedName~PorcentajeConverter"` → FAIL de compilación (`reloj`, `EsperaDebounce`, `_tareaDebounce`, `MensajeSinPermisoControlPoa`, `PorcentajeConverter`). Pegar.

- [ ] **Step 2: Implementación**

En `ControlPoaViewModel`:

```csharp
    /// <summary>Espera antes de recargar al cambiar el Ejercicio con las flechas o tipeando: un año de 4 cifras son
    /// 4 cambios y solo el último tiene que ir al servidor.</summary>
    private static readonly TimeSpan DebouncePorDefecto = TimeSpan.FromMilliseconds(300);

    /// <summary>Espera real del debounce; los tests que no miden el tiempo la bajan a cero.</summary>
    internal TimeSpan EsperaDebounce { get; set; } = DebouncePorDefecto;

    /// <summary>Tarea del último debounce, para que los tests la esperen de forma determinista (patrón de Productos).</summary>
    internal Task _tareaDebounce = Task.CompletedTask;

    private CancellationTokenSource? _debounceCts;
    private readonly ConsultaVigente _consultas = new();

    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga (403).</summary>
    public const string MensajeSinPermisoControlPoa = "No tenés permiso para ver el control POA.";

    protected override bool AvisaSinConexionEnLinea => true;

    /// <summary>Sin servidor la grilla queda vacía: el reintento no puede mostrar las líneas de otra consulta.</summary>
    protected override void AlPerderConexion() => Filas.Clear();

    /// <summary>Reintentar consulta el Ejercicio que está en pantalla, no el de la consulta que falló (convención 18).</summary>
    protected override Task ReintentarAsync(Func<Task> cargaFallida) => CargarAsync();
```

`[ObservableProperty] private int _ejercicio;` (sin inicializador) y el constructor gana el parámetro final `TimeProvider? reloj = null` con, como primera línea del cuerpo:

```csharp
        // El año arranca del reloj (inyectable para los datos fijos de los mockups). Se escribe el campo: no dispara la
        // recarga de OnEjercicioChanged, la primera carga la pide la vista.
        _ejercicio = (reloj ?? TimeProvider.System).GetUtcNow().Year;
```

Reemplazar `CargarAsync`, borrar `RecargarCommand` y `RecargarAsync`, y agregar:

```csharp
    /// <summary>
    /// La dispara la View (DataContextChanged) y el cambio de Ejercicio. Protegida (403 → SinPermiso, servidor caído →
    /// SinConexion, Cargando mientras espera) y con turno: si mientras espera empezó otra carga, su resultado se descarta.
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        var ejercicio = Ejercicio;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var lineas = await _service.ObtenerControlPoaAsync(ejercicio);
            if (!turno.EsLaUltima)
                return;
            Filas.Clear();
            foreach (var l in lineas)
                Filas.Add(l);
        }, MensajeSinPermisoControlPoa, turno);
    }

    /// <summary>Cambiar el Ejercicio recarga sola tras la espera; un cambio nuevo cancela la espera anterior. Un texto
    /// inválido en el NumericUpDown no llega acá (Ejercicio conserva el valor), así que no recarga con el año viejo.</summary>
    partial void OnEjercicioChanged(int value)
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();
        _tareaDebounce = DebounceCargarAsync(_debounceCts.Token);
    }

    private async Task DebounceCargarAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(EsperaDebounce, ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (ct.IsCancellationRequested)
            return;

        await CargarAsync();
    }
```

(`using System.Threading;`). Nota: `Task.Delay(Timeout.InfiniteTimeSpan, ct)` es válido (espera hasta la cancelación).

`src/StockApp.Presentation/Converters/PorcentajeConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>Porcentaje de ejecución en es-UY con dos decimales: 47.3 → "47,30 %". El valor ya viene en escala 0-100.</summary>
public sealed class PorcentajeConverter : IValueConverter
{
    public static readonly PorcentajeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? Formatear(d) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    public static string Formatear(decimal porcentaje) => FormatoEsUy.Decimal(porcentaje) + " %";
}
```

En `ControlPoaView.axaml` v1 (transitorio hasta la Task 8) quitar SOLO el botón `<Button Classes="primary" Content="Actualizar" Command="{Binding RecargarCommand}" />` (el comando ya no existe: si no, el build falla por compiled bindings).

- [ ] **Step 3: Verde y build**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ControlPoa|FullyQualifiedName~PorcentajeConverter|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~ColumnasPdfContraDtosReales|FullyQualifiedName~ShellMainSeccionActiva"
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlPoa|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~EtiquetadoDeCampos"
```

Expected: verde. Si `GuardianDePatronTests.Vista_NoTieneUnSegundoBotonPrimario` o la regla "primario por vista" no cambia, bien; si un test heredado buscaba "Actualizar" en Control POA, se adapta (convención 13) y se anota.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (h) Quitar `if (!turno.EsLaUltima) return;` de `CargarAsync` → rojo `ConsultaVieja_QueTerminaDespues_NoPisaLaNueva`. Restaurar.
- (i) Quitar las dos líneas `_debounceCts?.Cancel();` y `_debounceCts?.Dispose();` → rojo `CambiosRapidos_SoloConsultaElUltimoEjercicio`. Restaurar.
- (j) Quitar el override `ReintentarAsync` (la línea) → rojo `Reintentar_ConsultaElEjercicioQueEstaEnPantalla` (consulta 2026). Restaurar.
- (k) Quitar el cuerpo de `OnEjercicioChanged` → rojo `CambiarEjercicio_RecargaSoloConElEjercicioNuevo`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs src/StockApp.Presentation/Converters/PorcentajeConverter.cs src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs tests/StockApp.Presentation.Tests/Converters/PorcentajeConverterTests.cs
git commit -m "feat(finanzas): control POA recarga al cambiar el ejercicio y muestra carga, sin conexión y sin permiso"
```

---

## Bloque B — Datos fijos, medición y mockups (GATE antes de todo XAML de vista)

### Task 4: Datos fijos de Calendario, Control POA y Adjuntos

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosCalendario.cs`, `DatosControlPoa.cs`, `DatosAdjuntos.cs`, `DatosFinanzasTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: constructores de las Tasks 2 y 3; `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; `DatosProductos.UsuarioAdmin`.
- Produces: `DatosCalendario.Calendario` (`CalendarioPagosDto`), `Vm(Func<Task<CalendarioPagosDto>>)`, `ConDatos()`, `SinDatos()`, `Cargando()`, `SinConexion()` (→ `ShellMainViewModel`); `DatosControlPoa.Lineas`, `LineasConSobreejecutada`, `Reloj`, `Vm(...)`, `ConLineas()`, `Sobreejecutado()`, `SinLineas()`, `Cargando()`, `SinConexion()`; `DatosAdjuntos.VmVacio()` (`AdjuntosPanelViewModel` ya inicializado), `EnTarjeta(AdjuntosPanelViewModel)` (`Control`).

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/DatosFinanzasTests.cs`:

```csharp
using StockApp.Domain.Formato;
using StockApp.Presentation.Converters;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Los mockups de finanzas escriben estos textos a mano: si un dato cambia y el mockup no, la comparación falla
/// por un texto distinto y el diagnóstico no lo dice.</summary>
public class DatosFinanzasTests
{
    [Fact]
    public void Calendario_TextosDelMockup()
    {
        var c = DatosCalendario.Calendario;
        Assert.Equal(new[] { 2, 1, 2, 2 }, new[] { c.Vencidas.Count, c.AVencer7Dias.Count, c.AVencer30Dias.Count, c.PagosRecientes.Count });
        Assert.Equal("$ 18.450,00", FormatoEsUy.Moneda(c.Vencidas[0].SaldoPendiente));
        Assert.Equal("28/09/2026", FechaCortaConverter.Formatear(c.Vencidas[0].FechaVencimiento));
        Assert.Equal("—", c.Vencidas[1].NumeroFactura ?? "—");
        Assert.Equal("20/10/2026", FechaCortaConverter.Formatear(c.AVencer30Dias[0].FechaVencimiento));
        Assert.Equal("05/10/2026", FechaCortaConverter.Formatear(c.PagosRecientes[0].FechaPago));
    }

    [Fact]
    public void ControlPoa_TextosDelMockup()
    {
        Assert.Equal("47,30 %", PorcentajeConverter.Formatear(DatosControlPoa.Lineas[0].PorcentajeEjecucion));
        Assert.Equal("94,26 %", PorcentajeConverter.Formatear(DatosControlPoa.Lineas[1].PorcentajeEjecucion));
        Assert.Equal("$ 1.200.000,00", FormatoEsUy.Moneda(DatosControlPoa.Lineas[0].Presupuesto));
        var sobre = DatosControlPoa.LineasConSobreejecutada[^1];
        Assert.Equal("-$ 42.900,00", FormatoEsUy.Moneda(sobre.Saldo));
        Assert.Equal("114,30 %", PorcentajeConverter.Formatear(sobre.PorcentajeEjecucion));
        Assert.True(sobre.Sobregirada);
        foreach (var l in DatosControlPoa.LineasConSobreejecutada)
        {
            Assert.Equal(l.Presupuesto - l.Gastado, l.Saldo);
            Assert.Equal(Math.Round(l.Gastado / l.Presupuesto * 100m, 2), l.PorcentajeEjecucion);
        }
    }

    [Fact]
    public void ControlPoa_ElEjercicioSaleDelRelojFijo()
    {
        Assert.Equal(2026, DatosControlPoa.Vm(() => Task.FromResult(DatosControlPoa.Lineas)).Ejercicio);
    }

    [Fact]
    public void Adjuntos_VacioYConPermiso()
    {
        var vm = DatosAdjuntos.VmVacio();

        Assert.Empty(vm.Items);
        Assert.True(vm.PuedeModificar);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosFinanzasTests"` → FAIL de compilación. Pegar.

- [ ] **Step 2: Implementación**

`DatosCalendario.cs`:

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/calendario-pagos*.html (textos fijados en DatosFinanzasTests).
/// Las facturas vienen ordenadas por vencimiento, como las devuelve el servidor.</summary>
public static class DatosCalendario
{
    public static readonly CalendarioPagosDto Calendario = new(
        Vencidas:
        [
            new(101, "Barraca Carmelo S.A.", "A-0004398", 18_450m, new DateOnly(2026, 9, 28), "Pendiente"),
            new(102, "Ferretería Industrial del Este", null, 6_120.50m, new DateOnly(2026, 10, 2), "Pendiente"),
        ],
        AVencer7Dias:
        [
            new(103, "Distribuidora Eléctrica Colonia", "B-0012877", 9_870m, new DateOnly(2026, 10, 9), "Pendiente"),
        ],
        AVencer30Dias:
        [
            new(104, "Imprenta Rosales", "A-0000815", 3_200m, new DateOnly(2026, 10, 20), "Pendiente"),
            new(105, "Barraca Carmelo S.A.", "A-0004521", 44_940m, new DateOnly(2026, 11, 5), "Pendiente"),
        ],
        PagosRecientes:
        [
            new(106, "Corralón Sur", "A-0002210", new DateOnly(2026, 10, 5), 12_300m),
            new(104, "Imprenta Rosales", "A-0000815", new DateOnly(2026, 10, 1), 1_500m),
        ]);

    public static readonly CalendarioPagosDto Vacio = new([], [], [], []);

    public static CalendarioPagosViewModel Vm(Func<Task<CalendarioPagosDto>> responder)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerCalendarioPagosAsync(null)).Returns(responder);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new CalendarioPagosViewModel(servicio.Object, Mock.Of<IGastoService>(), sesion.Object, Mock.Of<INavigationService>());
    }

    private static ShellMainViewModel EnShell(CalendarioPagosViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "CalendarioPagos", vm);

    public static ShellMainViewModel ConDatos() => EnShell(Vm(() => Task.FromResult(Calendario)));
    public static ShellMainViewModel SinDatos() => EnShell(Vm(() => Task.FromResult(Vacio)));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<CalendarioPagosDto>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<CalendarioPagosDto>(new ServidorNoDisponibleException())));
}
```

`DatosControlPoa.cs` (mismo esqueleto; reloj fijo 06/10/2026 para el Ejercicio):

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/control-poa*.html. Saldo = presupuesto − gastado y el porcentaje
/// redondeado a 2, como los calcula el servidor (fijado en DatosFinanzasTests).</summary>
public static class DatosControlPoa
{
    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
    }

    public static readonly TimeProvider Reloj = new RelojFijo();

    public static readonly IReadOnlyList<ControlPoaLineaDto> Lineas =
    [
        new(1, "Mantenimiento de espacios públicos", "Obras", 2026, 1_200_000m, 567_600m, 632_400m, 47.30m, false),
        new(2, "Alumbrado público", "Obras", 2026, 850_000m, 801_250m, 48_750m, 94.26m, false),
        new(3, "Comunicación institucional", "Prensa", 2026, 150_000m, 37_500m, 112_500m, 25.00m, false),
        new(4, "Fiestas tradicionales", "Cultura", 2026, 400_000m, 400_000m, 0m, 100.00m, false),
    ];

    public static readonly IReadOnlyList<ControlPoaLineaDto> LineasConSobreejecutada =
        [.. Lineas, new(5, "Limpieza de playas", "Turismo", 2026, 300_000m, 342_900m, -42_900m, 114.30m, true)];

    public static ControlPoaViewModel Vm(Func<Task<IReadOnlyList<ControlPoaLineaDto>>> responder)
    {
        var servicio = new Mock<IFinanzasVistasService>();
        servicio.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(responder);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(DatosProductos.UsuarioAdmin.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new ControlPoaViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<ICsvExporter>(),
            Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), Mock.Of<IPdfExporter>(),
            Mock.Of<IServicioAperturaArchivo>(), sesion.Object, Reloj);
    }

    private static ShellMainViewModel EnShell(ControlPoaViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "ControlPoa", vm);

    public static ShellMainViewModel ConLineas() => EnShell(Vm(() => Task.FromResult(Lineas)));
    public static ShellMainViewModel Sobreejecutado() => EnShell(Vm(() => Task.FromResult(LineasConSobreejecutada)));
    public static ShellMainViewModel SinLineas() => EnShell(Vm(() => Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>([])));
    public static ShellMainViewModel Cargando() => EnShell(Vm(() => new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>().Task));
    public static ShellMainViewModel SinConexion() =>
        EnShell(Vm(() => Task.FromException<IReadOnlyList<ControlPoaLineaDto>>(new ServidorNoDisponibleException())));
}
```

`DatosAdjuntos.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Datos fijos de docs/ui-v2/mockups/finanzas/adjuntos-vacio.html. El panel no es una pantalla: se compara en
/// una card de 760 sobre fondo app, el mismo marco que dibuja el mockup. El panel con un adjunto se compara dentro de
/// Ingreso por factura (ingreso-factura-guardado.html).</summary>
public static class DatosAdjuntos
{
    public static AdjuntosPanelViewModel VmVacio()
    {
        var servicio = new Mock<IAdjuntoService>();
        servicio.Setup(a => a.ListarPorGastoAsync(4521)).ReturnsAsync([]);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var vm = new AdjuntosPanelViewModel(servicio.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion.Object);
        vm.InicializarAsync(4521, null).GetAwaiter().GetResult();
        return vm;
    }

    public static Control EnTarjeta(AdjuntosPanelViewModel vm)
    {
        Application.Current!.TryGetResource("V2FondoBrush", null, out var fondo);
        var raiz = new UserControl
        {
            Background = (IBrush?)fondo,
            Content = new TarjetaContenido
            {
                Width = 760,
                Margin = new Thickness(28, 24),
                Padding = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Content = new AdjuntosPanelView { DataContext = vm },
            },
        };
        raiz.Classes.Add("ui2");
        return raiz;
    }
}
```

- [ ] **Step 3: Verde**

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosFinanzasTests"` → PASS.

Mutación (d) de la Task 1: `cp` de `AdjuntosPanelViewModel.cs` al `$SCRATCH`, quitar `_session.RolActual == RolUsuario.Admin ||` de `PuedeModificar`, correr el mismo filtro → rojo en `Adjuntos_VacioYConPermiso` (pegar), restaurar con `cp`, `git diff --no-index` vacío, verde.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/
git commit -m "test(ui-v2): datos fijos de Calendario de pagos, Control POA y panel de adjuntos"
```

### Task 5: Medición y mockups de las tres vistas — GATE con el usuario

**Decisiones aplicadas:** D1 a D18.

**Files:**
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `docs/ui-v2/mockups/finanzas/calendario-pagos.html`, `calendario-pagos-cargando.html`, `calendario-pagos-vacio.html`, `calendario-pagos-sin-conexion.html`, `control-poa.html`, `control-poa-cargando.html`, `control-poa-vacio.html`, `control-poa-sin-conexion.html`, `control-poa-sobreejecutado.html`, `adjuntos-vacio.html`
- Modify: `docs/ui-v2/mockups/inventario/ingreso-factura-guardado.html`
- Modify (local): el ledger (anchos medidos)

**Interfaces:**
- Consumes: `DatosCalendario`, `DatosControlPoa`, `DatosAdjuntos`, `DatosIngresoFactura` (Task 4 y 3c-3); `ColumnaAuto` y `DataGridColumnasAutoBehavior.AnchoMedido` (3c-1).
- Produces: las pantallas `finanzas-calendario-pagos[-cargando|-vacio|-sin-conexion]`, `finanzas-control-poa[-cargando|-vacio|-sin-conexion|-sobreejecutado]`, `finanzas-adjuntos-vacio`, `inventario-ingreso-factura-guardado`; `.v2-barra-ejecucion` y `.v2-barra-ejecucion--excedida` en `base.css`; en el ledger los anchos medidos que usan las Tasks 6 a 8.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal4a.cs` siguiendo `MedicionTemporal3c2` (plan de la 3c-2, Task 4 Step 1). Con el tema v2 (`Classes="ui2"`), a 1280×760 y 1366×768 (vista de 1040 y 1126), y `--logger "console;verbosity=detailed"`, escribe:
1. **Calendario:** un `DataGrid` de prueba con las columnas de D3 (FACTURA, VENCE con `FechaCortaConverter`, SALDO con `MonedaConverter`, `ColumnaAuto.Medir="True"`) sobre `DatosCalendario.Calendario` (las tres listas de facturas juntas, para que las tres grillas compartan anchos) y otro para Pagos recientes (FECHA, MONTO): `AnchoMedido` de cada columna y el ancho de la columna de "Registrar pago" con un `Button.secundario` v2.
2. **Control POA:** PROGRAMA, PRESUPUESTO y GASTADO medidos sobre `DatosControlPoa.LineasConSobreejecutada`; el ancho de SALDO = `Insignia` Peligro "Sobreejecutado" + 8 + el número más ancho del barrido de `GrillasSinRecorteBarridoTests` ("-$ 12.345.678,90") + el padding de celda, redondeado hacia arriba a múltiplo de 4; el ancho de % EJECUCIÓN = 64 + 8 + "114,30 %" + padding, idem. Anotar también si con esos fijos y LÍNEA en su piso de 200 la grilla entra en 1040 − 48 sin scroll.
3. **Panel de adjuntos:** ancho de "178 KB", "1,2 MB", "1023 KB" y de "06/10/2026" en `texto-secundario` (confirma 72 y 80 de D14); alto de la fila con un `Button.ghost` (confirma 40); alto total del panel con el adjunto de `DatosIngresoFactura` dentro de la card "Adjuntá la factura" a 1280 y 1366 (para el `translateY` de `ingreso-factura-guardado.html`).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal4a" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger.

- [ ] **Step 2: Espejo CSS de la barra**

Al final de `base.css`:

```css
/* Barra de ejecución de Control POA (espejo de v2:BarraEjecucion): pista de 64 x 6, radio 3, relleno a la izquierda con
   el ancho round(64 * min(%, 100) / 100) escrito en el HTML; por encima de 100 % el relleno usa el texto de peligro. */
.v2-barra-ejecucion { position: relative; width: 64px; height: 6px; border-radius: 3px; background: var(--v2-color-borde); overflow: hidden; }
.v2-barra-ejecucion > span { position: absolute; left: 0; top: 0; height: 6px; border-radius: 3px; background: var(--v2-color-acento); }
.v2-barra-ejecucion--excedida > span { background: var(--v2-color-peligro-texto); }
```

Confirmar los nombres exactos de las variables en `docs/ui-v2/mockups/_base/tokens.css` (`grep -n "borde\|acento\|peligro" docs/ui-v2/mockups/_base/tokens.css`) antes de escribirlas. Si un mockup necesita otra clase de geometría nueva (fila de adjunto, título con contador), se agrega con su espejo en el XAML y se declara en el GATE.

- [ ] **Step 3: Mockups del Calendario**

Partir de `inventario/historial.html` (encabezado, card, grilla con `v2-num`) y `inventario/historial-cargando.html` / `historial-vacio.html`. Cada uno:
- `<title>Finanzas: Calendario de pagos[ estado] (UI v2)</title>` y `<!-- Datos: DatosCalendario.<Estado>: … -->`.
- Sidebar con "Calendario de pagos" activo (se oculta en la comparación, pero el usuario aprueba la pantalla entera).
- `calendario-pagos.html`: encabezado de D1, cuatro cards de D2 con las grillas de D3 (anchos MEDIDOS como `flex: 0 0 Npx`; PROVEEDOR flexible; VENCE de Vencidas con `.v2-texto-peligro`; "—" en la factura sin número; "Registrar pago" `v2-boton--secundario`). A 760 de alto no entra todo: el contenido se corta abajo como en la app (scroll), sin barra dibujada salvo que la app la muestre (medirlo y copiarlo).
- `-vacio`: las cuatro cards con su línea de D5 y "· 0" en los títulos.
- `-cargando`: una card con `.v2-cargando` "Cargando el calendario de pagos…".
- `-sin-conexion`: una card con `.v2-vacio` de error (título "No se pudo conectar con el servidor", mensaje de la convención 18) y "Reintentar" secundario.

- [ ] **Step 4: Mockups de Control POA**

Partir de `inventario/historial*.html`. Encabezado de D7 (dos secundarios), barra de filtros con "Ejercicio" y "2026" en el campo (D8), grilla de D9 con `DatosControlPoa.Lineas` (anchos medidos; % EJECUCIÓN con `.v2-barra-ejecucion` y el `span` de ancho `round(64 × min(%,100) / 100)`: 30, 60, 16 y 64 px), pie "4 líneas". `-sobreejecutado`: `LineasConSobreejecutada`, la fila 5 con `.v2-insignia--peligro` "Sobreejecutado" y "-$ 42.900,00" en `.v2-texto-peligro`, barra `--excedida` con relleno 64, pie "5 líneas". `-cargando`, `-vacio` (D12: "No hay líneas POA en este ejercicio" / "Probá con otro ejercicio.") y `-sin-conexion` dentro de la card, con el filtro visible.

- [ ] **Step 5: Mockup del panel y de Ingreso por factura guardado**

- `finanzas/adjuntos-vacio.html`: `.v2-ventana` en fondo app, una `.v2-tarjeta` de 760 en (28, 24) con padding 24: encabezado "Adjuntos" (`v2-titulo-seccion`) y "Agregar" (`v2-boton--secundario`) a la derecha; debajo, la línea `v2-texto-secundario` de D15.
- `inventario/ingreso-factura-guardado.html`: borrar `.panel-adjuntos-v1` (regla CSS y `<div>`) y dibujar el panel real de D13/D14 con la fila "factura-A-0004521.pdf" · "178 KB" · "06/10/2026" · "Ver" · "Quitar", con el ícono PDF (`dotnet run --project tools/StockApp.UiV2.Herramientas -- icono-svg mdi-file-pdf-box`). Recalcular el `translateY` de `.pagina` y el pulgar con el alto medido en el Step 1 (3) y anotarlo.

- [ ] **Step 6: Capturas y verificación de la herramienta**

```bash
for p in calendario-pagos calendario-pagos-cargando calendario-pagos-vacio calendario-pagos-sin-conexion control-poa control-poa-cargando control-poa-vacio control-poa-sin-conexion control-poa-sobreejecutado adjuntos-vacio; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/finanzas/$p.html artifacts/ui-v2/mockups/finanzas-$p@1366x768.png --tamano 1366x768
done
dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/ingreso-factura-guardado.html artifacts/ui-v2/mockups/inventario-ingreso-factura-guardado.png
dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/ingreso-factura-guardado.html artifacts/ui-v2/mockups/inventario-ingreso-factura-guardado@1366x768.png --tamano 1366x768
ls -l --time-style=full-iso artifacts/ui-v2/mockups/finanzas-*.png artifacts/ui-v2/mockups/inventario-ingreso-factura-guardado*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 22 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 7: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal4a.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 8: GATE — aprobación del usuario**

PARAR. Presentar los 22 PNG (LOS DOS TAMAÑOS), la lista D1 a D18 y los anchos medidos del ledger. Señalar explícitamente: la columna "Registrar pago" oculta entera por code-behind (D4, convención 28); el vacío por sección como línea y no como `EstadoVacio` (D5); una sola card en cargando y sin conexión del Calendario (D6); "Actualizar" que se queda en el Calendario y se va de Control POA (D1, D7, E2); la barra (colores y ancho, D10) y SALDO con insignia (D11); el panel v2 dentro de los hosts v1 (excepción inversa, U1) y el doble encabezado en Ingreso por factura (D16); el texto de la confirmación (D15); y que el estado sin permiso no tiene mockup propio (D6, D12). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups y en los tests de las Tasks 6 a 8 que la fijan, y volver a presentar. Registrar en el ledger: `Mockups 4a: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 9: Commit**

```bash
git add docs/ui-v2/mockups/_base/base.css docs/ui-v2/mockups/finanzas/ docs/ui-v2/mockups/inventario/ingreso-factura-guardado.html
git commit -m "docs(ui-v2): mockups aprobados de Calendario de pagos, Control POA y panel de adjuntos"
```

---

## Bloque C — Vistas v2 (arranca solo con los mockups aprobados)

### Task 6: `AdjuntosPanelView` v2 y la card de Ingreso por factura sin exclusión

**Depende de:** Tasks 1, 4 y 5 (leer del ledger las D# cambiadas en el GATE y los anchos medidos).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml` (el `.axaml.cs` no cambia)
- Create: `tests/StockApp.Presentation.UiTests/V2/AdjuntosPanelViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/AdjuntosComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/IngresoFacturaComparacionTests.cs`, `DatosIngresoFactura.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L157 sale de `VistasEmbebidas`, entra a `VistasMigradasAV2`)
- Modify: `tests/StockApp.Presentation.UiTests/AccesoRevocadoPantallasFinanzasTests.cs` (el test de la Task 1 gana el assert de ocultamiento)

**Interfaces:**
- Consumes: `AdjuntosPanelViewModel` (Task 1), `TamanoLegibleConverter`, `IconoAdjuntoConverter`, `FechaUtcALocalConverter`, `ConteoConverter.EsCero` / `HayElementos`, `TooltipSiSeCorta`, `DatosAdjuntos` (Task 4).
- Produces: en la vista, `x:Name` `TituloAdjuntos`, `BotonAgregar`, `SinAdjuntos`, `ListaAdjuntos`; por fila, botones con `Content` "Ver" y "Quitar".

- [ ] **Step 1: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/AdjuntosPanelViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Projektanker.Icons.Avalonia;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Panel de adjuntos v2 con la View REAL y el tema real: estructura, vacío, tamaño legible, gates con un
/// operador de permisos mixtos (Admin cortocircuita) y la confirmación al quitar.</summary>
public class AdjuntosPanelViewV2Tests
{
    private static readonly AdjuntoDto Pdf = new(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 42, null,
        new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc));
    private static readonly AdjuntoDto Foto = new(2, "remito-firmado.jpg", "image/jpeg", 1_258_291, 42, null,
        new DateTime(2026, 10, 6, 15, 25, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, AdjuntosPanelView Vista, AdjuntosPanelViewModel Vm,
        AdjuntoServiceFake Servicio, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(SesionFake sesion, int? gastoId = 42, int? pagoId = null, params AdjuntoDto[] adjuntos)
    {
        var servicio = new AdjuntoServiceFake();
        servicio.Items.AddRange(adjuntos);
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new AdjuntosPanelViewModel(servicio, new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, sesion);
        vm.InicializarAsync(gastoId, pagoId).GetAwaiter().GetResult();
        var vista = new AdjuntosPanelView { DataContext = vm };
        var ventana = new Window { Width = 760, Height = 400, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, confirmacion);
    }

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static Button Boton(Control raiz, string texto) =>
        raiz.GetVisualDescendants().OfType<Button>().First(b => (b.Content as string) == texto);

    private static IEnumerable<Button> Botones(Control raiz, string texto) =>
        raiz.GetVisualDescendants().OfType<Button>().Where(b => (b.Content as string) == texto);

    private static List<string?> Textos(Control raiz) =>
        raiz.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Raiz_TieneUi2_TituloAdjuntos_YAgregarSecundario()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos));

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Adjuntos", m.Vista.FindControl<TextBlock>("TituloAdjuntos")!.Text);
        var agregar = m.Vista.FindControl<Button>("BotonAgregar")!;
        Assert.Contains("secundario", agregar.Classes);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void SinAdjuntos_MuestraElTextoDelVacio_YNoLaLista()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos));

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
        Assert.Equal("Sin adjuntos. PDF, JPG o PNG hasta 10 MB.", m.Vista.FindControl<TextBlock>("SinAdjuntos")!.Text);
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<ItemsControl>("ListaAdjuntos")!));
    }

    [AvaloniaFact]
    public void ConAdjuntos_TamanoLegibleFechaEIconoPorTipo()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [Pdf, Foto]);

        var textos = Textos(m.Vista);
        Assert.Contains("178 KB", textos);
        Assert.Contains("1,2 MB", textos);
        Assert.DoesNotContain(textos, t => t is not null && t.EndsWith(" B") && !t.EndsWith("KB") && !t.EndsWith("MB"));
        var iconos = m.Vista.GetVisualDescendants().OfType<Icon>().Select(i => i.Value).ToList();
        Assert.Equal(new[] { "mdi-file-pdf-box", "mdi-file-image-outline" }, iconos);
        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixtoSinElPermisoDelGasto_SoloVer()
    {
        var m = Montar(Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), adjuntos: [Pdf]);

        Assert.False(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<Button>("BotonAgregar")!));
        Assert.All(Botones(m.Vista, "Quitar"), b => Assert.False(ArbolVisual.EsVisibleEnArbol(b)));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Boton(m.Vista, "Ver")));
    }

    [AvaloniaFact]
    public void Gates_OperadorMixtoConElPermisoDelPago_PanelDePago_Modifica()
    {
        var m = Montar(Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), gastoId: null, pagoId: 7, adjuntos: [Pdf]);

        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<Button>("BotonAgregar")!));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Boton(m.Vista, "Quitar")));
    }

    [AvaloniaFact]
    public async Task Quitar_ConClickReal_PreguntaConElNombreYQuita()
    {
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [Pdf]);

        MontajeV2.Clic(m.Ventana, Boton(m.Vista, "Quitar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(AdjuntosPanelViewModel.MensajeConfirmarQuitar("factura-A-0004521.pdf"),
            Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal(new[] { 1 }, m.Servicio.Quitados);
        Assert.True(ArbolVisual.EsVisibleEnArbol(m.Vista.FindControl<TextBlock>("SinAdjuntos")!));
    }

    [AvaloniaFact]
    public void NombreLargo_TieneElipsisYTooltipSoloSiSeCorta()
    {
        var largo = Pdf with { NombreArchivo = "factura-A-0004521-barraca-carmelo-materiales-rambla-costanera-octubre-2026.pdf" };
        var m = Montar(Mixto(Permisos.RegistrarGastos), adjuntos: [largo]);

        var nombre = m.Vista.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == largo.NombreArchivo);
        Assert.Equal(TextTrimming.CharacterEllipsis, nombre.TextTrimming);
        Assert.Equal(largo.NombreArchivo, ToolTip.GetTip(nombre));
        Assert.True(StockApp.Presentation.Behaviors.TooltipSiSeCorta.GetActivo(nombre));
    }
}
```

`ConfirmacionServiceFake` (en `MovimientoRegistroFakes.cs`) necesita registrar las preguntas: si no tiene una lista `Preguntas`, agregarla (`public List<string> Preguntas { get; } = new();` y `PreguntarAsync(string mensaje) { Preguntas.Add(mensaje); return Task.FromResult(true); }`), sin cambiar su respuesta. Confirmar el nombre real de `TooltipSiSeCorta.GetActivo` con CodeGraph antes de usarlo.

En `AccesoRevocadoPantallasFinanzasTests.Un403_ConLaFacturaEnEdicion_…` agregar al final, ya con el panel v2:

```csharp
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonDelPanel(ventana, "Agregar")), "El panel v2 oculta (no deshabilita) sin permiso.");
        Assert.False(ArbolVisual.EsVisibleEnArbol(BotonDelPanel(ventana, "Quitar")));
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AdjuntosPanelViewV2Tests|FullyQualifiedName~Un403_ConLaFacturaEnEdicion"` → FAIL (no hay `TituloAdjuntos`/`ui2`; el panel v1 deshabilita en vez de ocultar). Pegar.

- [ ] **Step 2: Vista v2**

Reescribir `AdjuntosPanelView.axaml` completo (anchos de D14 o los cambiados en el GATE):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:dto="using:StockApp.Application.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:b="using:StockApp.Presentation.Behaviors"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="712" d:DesignHeight="240"
             x:Class="StockApp.Presentation.Views.Finanzas.AdjuntosPanelView"
             x:DataType="vm:AdjuntosPanelViewModel"
             Classes="ui2">

    <!-- UI v2. Espejo: docs/ui-v2/mockups/finanzas/adjuntos-vacio.html e inventario/ingreso-factura-guardado.html.
         Panel embebido: lo inicializa el host (InicializarAsync). La raíz lleva ui2 para verse v2 también dentro de los
         formularios de gasto y de pagos, que siguen en el tema viejo hasta migrar. Agregar y Quitar se ocultan sin el
         permiso del gasto o del pago (PuedeModificar, que se re-evalúa si un 403 cambia los permisos); Ver siempre. -->
    <StackPanel>
        <Grid ColumnDefinitions="*,Auto" Margin="0,0,0,8">
            <TextBlock x:Name="TituloAdjuntos" Classes="titulo-seccion" Text="Adjuntos" VerticalAlignment="Center" />
            <Button x:Name="BotonAgregar" Grid.Column="1" Classes="secundario" Content="Agregar"
                    Command="{Binding AgregarCommand}"
                    IsVisible="{Binding PuedeModificar}" />
        </Grid>

        <TextBlock x:Name="SinAdjuntos" Classes="texto-secundario"
                   Text="Sin adjuntos. PDF, JPG o PNG hasta 10 MB."
                   IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.EsCero}}" />

        <ItemsControl x:Name="ListaAdjuntos" ItemsSource="{Binding Items}"
                      IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="dto:AdjuntoDto">
                    <Border Height="40" BorderThickness="0,1,0,0" BorderBrush="{DynamicResource V2SeparadorGrillaBrush}">
                        <Grid ColumnDefinitions="Auto,*,72,80,Auto,Auto">
                            <i:Icon Value="{Binding ContentType, Converter={x:Static conv:IconoAdjuntoConverter.Instance}}"
                                    FontSize="16" Margin="0,0,8,0" VerticalAlignment="Center"
                                    Foreground="{DynamicResource V2TextoSecundarioBrush}" />
                            <TextBlock Grid.Column="1" Text="{Binding NombreArchivo}"
                                       TextTrimming="CharacterEllipsis"
                                       ToolTip.Tip="{Binding NombreArchivo}"
                                       b:TooltipSiSeCorta.Activo="True"
                                       VerticalAlignment="Center" />
                            <TextBlock Grid.Column="2" Classes="texto-secundario" HorizontalAlignment="Right" VerticalAlignment="Center"
                                       Text="{Binding TamanoBytes, Converter={x:Static conv:TamanoLegibleConverter.Instance}}" />
                            <TextBlock Grid.Column="3" Classes="texto-secundario" HorizontalAlignment="Right" VerticalAlignment="Center"
                                       Text="{Binding FechaAltaUtc, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy'}" />
                            <Button Grid.Column="4" Classes="ghost" Content="Ver" Margin="8,0,0,0" VerticalAlignment="Center"
                                    Command="{Binding $parent[UserControl].((vm:AdjuntosPanelViewModel)DataContext).VerCommand}"
                                    CommandParameter="{Binding}" />
                            <Button Grid.Column="5" Classes="ghost" Content="Quitar" VerticalAlignment="Center"
                                    Command="{Binding $parent[UserControl].((vm:AdjuntosPanelViewModel)DataContext).QuitarCommand}"
                                    CommandParameter="{Binding}"
                                    IsVisible="{Binding $parent[UserControl].((vm:AdjuntosPanelViewModel)DataContext).PuedeModificar}" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </StackPanel>
</UserControl>
```

`GuardianDePatronTests`: quitar `typeof(AdjuntosPanelView),` de `VistasEmbebidas` (L157) y agregarlo a `VistasMigradasAV2`.

- [ ] **Step 3: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AdjuntosPanelViewV2Tests|FullyQualifiedName~AccesoRevocado|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~PagosGasto|FullyQualifiedName~IngresoPorFactura|FullyQualifiedName~ViewLocator|FullyQualifiedName~ButtonGhost|FullyQualifiedName~ControlesV2Convivencia"
```

Expected: verde. Si un test heredado de `PagosGastoViewTests` o `IngresoPorFacturaViewV2Tests` buscaba el `Classes="secondary"` o el `IsEnabled` del panel v1, se adapta (convención 13) y se lista por nombre en el ledger.

- [ ] **Step 4: Comparaciones (rojo → verde)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/AdjuntosComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Panel de adjuntos vacío en la card de 760 sobre fondo app (el panel con un adjunto se compara dentro de
/// Ingreso por factura guardado).</summary>
[Trait("Categoria", "ComparacionVisual")]
public class AdjuntosComparacionTests
{
    [AvaloniaFact]
    public void AdjuntosVacio_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(DatosAdjuntos.EnTarjeta(DatosAdjuntos.VmVacio()), null, "finanzas/adjuntos-vacio.html");

    [AvaloniaFact]
    public void AdjuntosVacio_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(DatosAdjuntos.EnTarjeta(DatosAdjuntos.VmVacio()), null, "finanzas/adjuntos-vacio.html",
            tamano: TamanoLienzo.Municipio);
}
```

En `IngresoFacturaComparacionTests.Comparar`: `ocultarEnMockup: [".v2-sidebar"]` siempre (borrar el `string[] ocultar = …` condicional) y en `prepararApp` borrar el bloque `if (estado.Guardado) { vista.UpdateLayout(); DatosIngresoFactura.OcultarPanelAdjuntosV1(vista); }` dejando solo `vista.UpdateLayout()` cuando `estado.Guardado` (la card se hizo visible y su `ContentControl` resuelve la vista en el próximo layout). En `DatosIngresoFactura.cs` borrar `OcultarPanelAdjuntosV1` y su comentario.

Run: `scripts/ui-v2/comparar.sh Adjuntos` y `scripts/ui-v2/comparar.sh IngresoFacturaGuardado`. Expected: las 4 filas APROBADAS con el umbral por defecto (si una no, diagnosticar con la página y `teselas.py`; un `UmbralAprobado` solo con el usuario). Correr también `scripts/ui-v2/comparar.sh IngresoFactura` entero: las otras 6 filas de la 3c-3 no se mueven.

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (l) En la vista, quitar `IsVisible="{Binding PuedeModificar}"` de `BotonAgregar` → rojo `Gates_OperadorMixtoSinElPermisoDelGasto_SoloVer` y `Un403_ConLaFacturaEnEdicion_…`. Restaurar.
- (m) Quitar el `IsVisible="{Binding $parent[UserControl]…PuedeModificar}"` del botón Quitar → rojo `Gates_OperadorMixtoSinElPermisoDelGasto_SoloVer`. Restaurar.
- (n) Quitar `Classes="ui2"` de la raíz → rojo `Raiz_TieneUi2_…` y las dos comparaciones de adjuntos. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml tests/StockApp.Presentation.UiTests/V2/AdjuntosPanelViewV2Tests.cs tests/StockApp.Presentation.UiTests/AccesoRevocadoPantallasFinanzasTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/MovimientoRegistroFakes.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/AdjuntosComparacionTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/IngresoFacturaComparacionTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs
git commit -m "feat(ui-v2): panel de adjuntos v2 con gates que se ocultan y comparado también dentro de Ingreso por factura"
```

### Task 7: Calendario de pagos v2

**Depende de:** Tasks 2, 4 y 5.

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml`, `CalendarioPagosView.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs`, `tests/StockApp.Presentation.UiTests/V2/CalendarioPagosViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/CalendarioComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L70 y L122 salen; entra a `VistasMigradasAV2`)

**Interfaces:**
- Consumes: `CalendarioPagosViewModel` (Task 2), `FechaCortaConverter`, `MonedaConverter`, `ColumnaAuto`, `ConteoConverter`, `EstadoCargando`, `EstadoVacio`, `ManejadorAccesoRevocado`.
- Produces: `FinanzasVistasFake` (`Calendario`, `PendienteCalendario`, `FallaCalendario`, `Lineas`, `PendientePoa`, `FallaPoa`, `EjerciciosConsultados`), `GastoServiceSinUsoFake`; en la vista, `x:Name` `Encabezado`, `BotonActualizar`, `EstadoSinPermiso`, `TarjetaCargando`, `TarjetaSinConexion`, `BotonReintentar`, `Secciones`, `TituloVencidas`, `Titulo7Dias`, `Titulo30Dias`, `TituloPagos`, `GrillaVencidas`, `Grilla7Dias`, `Grilla30Dias`, `GrillaPagos`, `VaciasVencidas`, `Vacias7Dias`, `Vacias30Dias`, `VaciosPagos`.

- [ ] **Step 1: Fakes compartidos**

`tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>IFinanzasVistasService con los tipos reales. Cada consulta responde, en este orden: Falla, Pendiente o el valor.</summary>
internal sealed class FinanzasVistasFake : IFinanzasVistasService
{
    public CalendarioPagosDto Calendario { get; set; } = new([], [], [], []);
    public TaskCompletionSource<CalendarioPagosDto>? PendienteCalendario { get; set; }
    public Exception? FallaCalendario { get; set; }

    public List<ControlPoaLineaDto> Lineas { get; set; } = new();
    public TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>? PendientePoa { get; set; }
    public Exception? FallaPoa { get; set; }
    public List<int> EjerciciosConsultados { get; } = new();

    public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) =>
        FallaCalendario is not null ? Task.FromException<CalendarioPagosDto>(FallaCalendario)
        : PendienteCalendario is not null ? PendienteCalendario.Task
        : Task.FromResult(Calendario);

    public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        EjerciciosConsultados.Add(ejercicio);
        return FallaPoa is not null ? Task.FromException<IReadOnlyList<ControlPoaLineaDto>>(FallaPoa)
            : PendientePoa is not null ? PendientePoa.Task
            : Task.FromResult<IReadOnlyList<ControlPoaLineaDto>>(Lineas.ToArray());
    }

    public Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes) => throw new NotSupportedException();
    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotSupportedException();
}

internal sealed class GastoServiceSinUsoFake : IGastoService
{
    public Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null) => throw new NotSupportedException();
    public Task<ResultadoGastoDto> ModificarAsync(Gasto gasto) => throw new NotSupportedException();
    public Task AnularAsync(int id, bool confirmarAnulacionDePagoAutomatico = false) => throw new NotSupportedException();
    public Task<Gasto> ObtenerPorIdAsync(int id) => throw new NotSupportedException();
    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden) => throw new NotSupportedException();
    public Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro) => throw new NotSupportedException();
    public Task<int> RegistrarPagoAsync(PagoGasto pago) => throw new NotSupportedException();
    public Task AnularPagoAsync(int gastoId, int pagoId) => throw new NotSupportedException();
    public Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds) => throw new NotSupportedException();
}
```

(Confirmar con CodeGraph la firma de `IGastoService` antes de escribirlo; la de arriba es la de `AccesoRevocadoPantallasFinanzasTests.GastoServiceFake` al escribir el plan.)

- [ ] **Step 2: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/CalendarioPagosViewV2Tests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Calendario de pagos v2 con la View REAL: estructura, estados de carga, vacíos por sección, formato es-UY y
/// la columna "Registrar pago" con un operador de permisos mixtos, también tras un 403 en caliente.</summary>
public class CalendarioPagosViewV2Tests
{
    private static readonly CalendarioPagosDto Datos = new(
        [new(101, "Barraca Carmelo S.A.", "A-0004398", 18_450m, new DateOnly(2026, 9, 28), "Pendiente"),
         new(102, "Ferretería Industrial del Este", null, 6_120.50m, new DateOnly(2026, 10, 2), "Pendiente")],
        [new(103, "Distribuidora Eléctrica Colonia", "B-0012877", 9_870m, new DateOnly(2026, 10, 9), "Pendiente")],
        [],
        [new(106, "Corralón Sur", "A-0002210", new DateOnly(2026, 10, 5), 12_300m)]);

    private sealed record Montaje(Window Ventana, CalendarioPagosView Vista, CalendarioPagosViewModel Vm, FinanzasVistasFake Servicio);

    private static SesionFake Mixto(params string[] permisos) => new(RolUsuario.Operador, permisos);

    private static Montaje Montar(FinanzasVistasFake servicio, SesionFake? sesion = null)
    {
        var vm = new CalendarioPagosViewModel(servicio, new GastoServiceSinUsoFake(),
            sesion ?? Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos), new NavigationServiceFake());
        var vista = new CalendarioPagosView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);
    private static DataGrid[] GrillasDeFacturas(Montaje m) =>
        [Control<DataGrid>(m, "GrillaVencidas"), Control<DataGrid>(m, "Grilla7Dias"), Control<DataGrid>(m, "Grilla30Dias")];

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_YActualizarSecundario()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Calendario de pagos", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonActualizar").Classes);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void ConDatos_TitulosConContador_FormatoEsUy_YVaciosSoloDondeNoHay()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        Assert.Equal("Vencidas · 2", Control<TextBlock>(m, "TituloVencidas").Text);
        Assert.Equal("A vencer en 30 días · 0", Control<TextBlock>(m, "Titulo30Dias").Text);
        var textos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();
        Assert.Contains("$ 18.450,00", textos);
        Assert.Contains("28/09/2026", textos);
        Assert.Contains("—", textos);
        Assert.Contains("05/10/2026", textos);
        Assert.True(Visible(Control<TextBlock>(m, "Vacias30Dias")));
        Assert.Equal("No hay facturas que venzan en los próximos 30 días", Control<TextBlock>(m, "Vacias30Dias").Text);
        Assert.False(Visible(Control<TextBlock>(m, "VaciasVencidas")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla30Dias")));
    }

    [AvaloniaFact]
    public void VenceDeVencidas_EnTonoPeligro()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos });

        var vence = Control<DataGrid>(m, "GrillaVencidas").GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "28/09/2026");
        Assert.Contains("negativo", vence.Classes);
    }

    [AvaloniaFact]
    public void Cargando_SinDatos_UnaSolaCardConElIndicador()
    {
        var m = Montar(new FinanzasVistasFake { PendienteCalendario = new TaskCompletionSource<CalendarioPagosDto>() });

        Assert.True(Visible(Control<Border>(m, "TarjetaCargando")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));
        Assert.False(Visible(Control<TextBlock>(m, "VaciasVencidas")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new FinanzasVistasFake { FallaCalendario = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<Border>(m, "TarjetaSinConexion")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));

        servicio.FallaCalendario = null;
        servicio.Calendario = Datos;
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<Border>(m, "TarjetaSinConexion")));
        Assert.Equal("Vencidas · 2", Control<TextBlock>(m, "TituloVencidas").Text);
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLasCards()
    {
        var m = Montar(new FinanzasVistasFake { FallaCalendario = new UnauthorizedAccessException("403") });

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.False(Visible(Control<StackPanel>(m, "Secciones")));
    }

    [AvaloniaFact]
    public void SinPagos_LaColumnaRegistrarPagoNoSeVeEnNingunaGrilla()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos }, Mixto(Permisos.VerFinanzas));

        Assert.All(GrillasDeFacturas(m), g => Assert.False(g.Columns[^1].IsVisible));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(),
            b => (b.Content as string) == "Registrar pago" && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public void ConPagos_LaColumnaSeVe()
    {
        var m = Montar(new FinanzasVistasFake { Calendario = Datos }, Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos));

        Assert.All(GrillasDeFacturas(m), g => Assert.True(g.Columns[^1].IsVisible));
        Assert.Contains(m.Vista.GetVisualDescendants().OfType<Button>(),
            b => (b.Content as string) == "Registrar pago" && ArbolVisual.EsVisibleEnArbol(b));
    }

    [AvaloniaFact]
    public async Task Un403_QueQuitaPagos_OcultaLaColumnaSinNavegar()
    {
        var sesion = Mixto(Permisos.VerFinanzas, Permisos.RegistrarPagos);
        var tras = new System.Collections.Generic.HashSet<string> { Permisos.VerFinanzas };
        var shell = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        var vm = new CalendarioPagosViewModel(new FinanzasVistasFake { Calendario = Datos }, new GastoServiceSinUsoFake(), sesion,
            new NavigationServiceFake());
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
        var vista = ventana.GetVisualDescendants().OfType<CalendarioPagosView>().Single();
        Assert.True(vista.FindControl<DataGrid>("GrillaVencidas")!.Columns[^1].IsVisible, "Precondición: con finanzas.pagos la columna se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras), new ConfirmacionServiceFake(),
            shell.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vista.FindControl<DataGrid>("GrillaVencidas")!.Columns[^1].IsVisible);
        Assert.False(vista.FindControl<DataGrid>("Grilla7Dias")!.Columns[^1].IsVisible);
        Assert.Same(vm, shell.CurrentContent);
    }
}
```

(`ManejadorAccesoRevocado`, `ViewLocator`, `TestApp`: los `using` que usa `AccesoRevocadoPantallasFinanzasTests`.)

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CalendarioPagosViewV2Tests"` → FAIL (controles con nombre inexistentes, sin `ui2`). Pegar.

- [ ] **Step 3: Vista v2**

`CalendarioPagosView.axaml` completo (anchos de columnas: los de D3 medidos en la Task 5; las `Auto` miden solas, los `MinWidth` son pisos):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:dto="using:StockApp.Application.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:b="using:StockApp.Presentation.Behaviors"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.CalendarioPagosView"
             x:DataType="vm:CalendarioPagosViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2. Espejo: docs/ui-v2/mockups/finanzas/calendario-pagos*.html. Cuatro cards apiladas con el contador en el
         título; grillas sin orden por click (la lista viene ordenada por vencimiento). Mientras carga sin datos, una sola
         card con el indicador; sin servidor, una card con Reintentar; tras un 403, Sin permiso en lugar de todo. La
         columna Registrar pago la oculta el code-behind (una columna de DataGrid no recibe el DataContext). -->
    <DockPanel Margin="28,24">

        <v2:EncabezadoPagina x:Name="Encabezado" DockPanel.Dock="Top" Titulo="Calendario de pagos"
                             Subtitulo="Facturas por vencimiento y pagos de los últimos 7 días.">
            <Button x:Name="BotonActualizar" Classes="secundario" Content="Actualizar" Command="{Binding RecargarCommand}" />
        </v2:EncabezadoPagina>

        <v2:EstadoVacio x:Name="EstadoSinPermiso" DockPanel.Dock="Top" UseLayoutRounding="False"
                        Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                        IsVisible="{Binding SinPermiso}" />

        <ScrollViewer IsVisible="{Binding !SinPermiso}">
            <StackPanel Spacing="16">

                <v2:TarjetaContenido x:Name="TarjetaCargando">
                    <v2:TarjetaContenido.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" />
                            <Binding Path="Vencidas.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                            <Binding Path="AVencer7Dias.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                            <Binding Path="AVencer30Dias.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                            <Binding Path="PagosRecientes.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:TarjetaContenido.IsVisible>
                    <v2:EstadoCargando Texto="Cargando el calendario de pagos…" />
                </v2:TarjetaContenido>

                <v2:TarjetaContenido x:Name="TarjetaSinConexion" IsVisible="{Binding SinConexion}">
                    <v2:EstadoVacio UseLayoutRounding="False"
                                    Titulo="No se pudo conectar con el servidor"
                                    Mensaje="Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar."
                                    EsError="True">
                        <Button x:Name="BotonReintentar" Classes="secundario" Content="Reintentar"
                                Command="{Binding ReintentarCargaCommand}" />
                    </v2:EstadoVacio>
                </v2:TarjetaContenido>

                <!-- Fuera de las cards mientras carga sin datos o sin servidor. Con datos en pantalla, Actualizar las deja. -->
                <StackPanel x:Name="Secciones" Spacing="16" IsVisible="{Binding !SinConexion}">
                    <StackPanel Spacing="16">
                        <StackPanel.IsVisible>
                            <MultiBinding Converter="{x:Static BoolConverters.Or}">
                                <Binding Path="!Cargando" />
                                <Binding Path="Vencidas.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                                <Binding Path="AVencer7Dias.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                                <Binding Path="AVencer30Dias.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                                <Binding Path="PagosRecientes.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            </MultiBinding>
                        </StackPanel.IsVisible>

                        <v2:TarjetaContenido>
                            <StackPanel>
                                <TextBlock x:Name="TituloVencidas" Classes="titulo-seccion" Margin="0,0,0,12"
                                           Text="{Binding Vencidas.Count, StringFormat='Vencidas · {0}'}" />
                                <TextBlock x:Name="VaciasVencidas" Classes="texto-secundario" Text="No hay facturas vencidas">
                                    <TextBlock.IsVisible>
                                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                                            <Binding Path="!Cargando" />
                                            <Binding Path="Vencidas.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                                        </MultiBinding>
                                    </TextBlock.IsVisible>
                                </TextBlock>
                                <DataGrid x:Name="GrillaVencidas" ItemsSource="{Binding Vencidas}" IsReadOnly="True" CanUserSortColumns="False"
                                          IsVisible="{Binding Vencidas.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="PROVEEDOR" Width="*" MinWidth="200">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding ProveedorNombre}" TextTrimming="CharacterEllipsis"
                                                               ToolTip.Tip="{Binding ProveedorNombre}" b:TooltipSiSeCorta.Activo="True"
                                                               VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="FACTURA" Width="Auto" SortMemberPath="NumeroFactura" b:ColumnaAuto.Medir="True">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding NumeroFactura, TargetNullValue='—'}" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="VENCE" Width="Auto" SortMemberPath="FechaVencimiento"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Classes="negativo" VerticalAlignment="Center"
                                                               Text="{Binding FechaVencimiento, Converter={x:Static conv:FechaCortaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="SALDO" Width="Auto" CellStyleClasses="num" SortMemberPath="SaldoPendiente"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Width="Auto" CellStyleClasses="sin-padding-vertical">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <Button Classes="secundario" Content="Registrar pago" VerticalAlignment="Center"
                                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                                            CommandParameter="{Binding}"
                                                            IsVisible="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).PuedeRegistrarPagos}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>

                        <!-- 7 y 30 días: las columnas de Vencidas sin el tono de peligro en VENCE. -->
                        <v2:TarjetaContenido>
                            <StackPanel>
                                <TextBlock x:Name="Titulo7Dias" Classes="titulo-seccion" Margin="0,0,0,12"
                                           Text="{Binding AVencer7Dias.Count, StringFormat='A vencer en 7 días · {0}'}" />
                                <TextBlock x:Name="Vacias7Dias" Classes="texto-secundario" Text="No hay facturas que venzan en los próximos 7 días">
                                    <TextBlock.IsVisible>
                                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                                            <Binding Path="!Cargando" />
                                            <Binding Path="AVencer7Dias.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                                        </MultiBinding>
                                    </TextBlock.IsVisible>
                                </TextBlock>
                                <DataGrid x:Name="Grilla7Dias" ItemsSource="{Binding AVencer7Dias}" IsReadOnly="True" CanUserSortColumns="False"
                                          IsVisible="{Binding AVencer7Dias.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="PROVEEDOR" Width="*" MinWidth="200">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding ProveedorNombre}" TextTrimming="CharacterEllipsis"
                                                               ToolTip.Tip="{Binding ProveedorNombre}" b:TooltipSiSeCorta.Activo="True"
                                                               VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="FACTURA" Width="Auto" SortMemberPath="NumeroFactura" b:ColumnaAuto.Medir="True">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding NumeroFactura, TargetNullValue='—'}" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="VENCE" Width="Auto" SortMemberPath="FechaVencimiento"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding FechaVencimiento, Converter={x:Static conv:FechaCortaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="SALDO" Width="Auto" CellStyleClasses="num" SortMemberPath="SaldoPendiente"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Width="Auto" CellStyleClasses="sin-padding-vertical">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <Button Classes="secundario" Content="Registrar pago" VerticalAlignment="Center"
                                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                                            CommandParameter="{Binding}"
                                                            IsVisible="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).PuedeRegistrarPagos}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>

                        <v2:TarjetaContenido>
                            <StackPanel>
                                <TextBlock x:Name="Titulo30Dias" Classes="titulo-seccion" Margin="0,0,0,12"
                                           Text="{Binding AVencer30Dias.Count, StringFormat='A vencer en 30 días · {0}'}" />
                                <TextBlock x:Name="Vacias30Dias" Classes="texto-secundario" Text="No hay facturas que venzan en los próximos 30 días">
                                    <TextBlock.IsVisible>
                                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                                            <Binding Path="!Cargando" />
                                            <Binding Path="AVencer30Dias.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                                        </MultiBinding>
                                    </TextBlock.IsVisible>
                                </TextBlock>
                                <DataGrid x:Name="Grilla30Dias" ItemsSource="{Binding AVencer30Dias}" IsReadOnly="True" CanUserSortColumns="False"
                                          IsVisible="{Binding AVencer30Dias.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="PROVEEDOR" Width="*" MinWidth="200">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding ProveedorNombre}" TextTrimming="CharacterEllipsis"
                                                               ToolTip.Tip="{Binding ProveedorNombre}" b:TooltipSiSeCorta.Activo="True"
                                                               VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="FACTURA" Width="Auto" SortMemberPath="NumeroFactura" b:ColumnaAuto.Medir="True">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock Text="{Binding NumeroFactura, TargetNullValue='—'}" VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="VENCE" Width="Auto" SortMemberPath="FechaVencimiento"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding FechaVencimiento, Converter={x:Static conv:FechaCortaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="SALDO" Width="Auto" CellStyleClasses="num" SortMemberPath="SaldoPendiente"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Width="Auto" CellStyleClasses="sin-padding-vertical">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:FacturaCalendarioDto">
                                                    <Button Classes="secundario" Content="Registrar pago" VerticalAlignment="Center"
                                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                                            CommandParameter="{Binding}"
                                                            IsVisible="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).PuedeRegistrarPagos}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>

                        <v2:TarjetaContenido>
                            <StackPanel>
                                <TextBlock x:Name="TituloPagos" Classes="titulo-seccion" Margin="0,0,0,12"
                                           Text="{Binding PagosRecientes.Count, StringFormat='Pagos recientes · {0}'}" />
                                <TextBlock x:Name="VaciosPagos" Classes="texto-secundario" Text="No hay pagos en los últimos 7 días">
                                    <TextBlock.IsVisible>
                                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                                            <Binding Path="!Cargando" />
                                            <Binding Path="PagosRecientes.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                                        </MultiBinding>
                                    </TextBlock.IsVisible>
                                </TextBlock>
                                <DataGrid x:Name="GrillaPagos" ItemsSource="{Binding PagosRecientes}" IsReadOnly="True" CanUserSortColumns="False"
                                          IsVisible="{Binding PagosRecientes.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                                    <DataGrid.Columns>
                                        <DataGridTemplateColumn Header="PROVEEDOR" Width="*" MinWidth="200">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:PagoRecienteDto">
                                                    <TextBlock Text="{Binding ProveedorNombre}" TextTrimming="CharacterEllipsis"
                                                               ToolTip.Tip="{Binding ProveedorNombre}" b:TooltipSiSeCorta.Activo="True"
                                                               VerticalAlignment="Center" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="FECHA" Width="Auto" SortMemberPath="FechaPago"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.NoCede="True"
                                                                b:ColumnaAuto.Conversor="{x:Static conv:FechaCortaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:PagoRecienteDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding FechaPago, Converter={x:Static conv:FechaCortaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                        <DataGridTemplateColumn Header="MONTO" Width="Auto" CellStyleClasses="num" SortMemberPath="Monto"
                                                                b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                            <DataGridTemplateColumn.CellTemplate>
                                                <DataTemplate x:DataType="dto:PagoRecienteDto">
                                                    <TextBlock VerticalAlignment="Center"
                                                               Text="{Binding Monto, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                                </DataTemplate>
                                            </DataGridTemplateColumn.CellTemplate>
                                        </DataGridTemplateColumn>
                                    </DataGrid.Columns>
                                </DataGrid>
                            </StackPanel>
                        </v2:TarjetaContenido>
                    </StackPanel>
                </StackPanel>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</UserControl>
```

Si `{Binding Path="!Cargando"}` no compila dentro de un `MultiBinding` (Avalonia 12 admite `!` en la ruta), usar `<Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />`, como el Historial, en todos los sitios.

`CalendarioPagosView.axaml.cs` completo:

```csharp
using System.ComponentModel;
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class CalendarioPagosView : UserControl
{
    private CalendarioPagosViewModel? _vm;

    public CalendarioPagosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (_vm is not null)
                _vm.PropertyChanged -= AlCambiarElVm;
            _vm = DataContext as CalendarioPagosViewModel;
            if (_vm is null)
                return;
            _vm.PropertyChanged += AlCambiarElVm;
            SincronizarColumnasDePago();
            await _vm.CargarAsync();
        };
    }

    /// <summary>Un 403 que cambia los permisos llega como PropertyChanged con nombre vacío
    /// (NotificarPermisosActualizados): la columna se re-evalúa sin navegar.</summary>
    private void AlCambiarElVm(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(CalendarioPagosViewModel.PuedeRegistrarPagos))
            SincronizarColumnasDePago();
    }

    /// <summary>
    /// La columna "Registrar pago" se oculta entera sin el permiso. Una DataGridColumn no está en el árbol lógico y no
    /// recibe el DataContext, así que un binding de IsVisible en la columna no se re-evaluaría; el botón de la celda
    /// conserva su propio IsVisible sobre el mismo gate.
    /// </summary>
    private void SincronizarColumnasDePago()
    {
        var visible = _vm?.PuedeRegistrarPagos == true;
        GrillaVencidas.Columns[^1].IsVisible = visible;
        Grilla7Dias.Columns[^1].IsVisible = visible;
        Grilla30Dias.Columns[^1].IsVisible = visible;
    }
}
```

`GuardianDePatronTests`: quitar `[InlineData(typeof(CalendarioPagosView), "Calendario de pagos", "FINANZAS")]` (L70) y `typeof(CalendarioPagosView),` de `VistasDeLaTanda` (L122); agregarlo a `VistasMigradasAV2`. Lo que custodiaba la `InlineData` (título de la vista) lo custodia `Raiz_TieneUi2_EncabezadoConTitulo_YActualizarSecundario` (convención 13; anotar el reemplazo en el ledger).

- [ ] **Step 4: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CalendarioPagosViewV2Tests|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~DataGridCeldaConControl|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~ShellSidebarSigueNavegacion|FullyQualifiedName~InicioView"
```

- [ ] **Step 5: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/CalendarioComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Calendario de pagos dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class CalendarioComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void Calendario_IgualAlMockup() => Comparar(DatosCalendario.ConDatos(), "finanzas/calendario-pagos.html");
    [AvaloniaFact] public void Calendario_IgualAlMockup_1366x768() => Comparar(DatosCalendario.ConDatos(), "finanzas/calendario-pagos.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioCargando_IgualAlMockup() => Comparar(DatosCalendario.Cargando(), "finanzas/calendario-pagos-cargando.html");
    [AvaloniaFact] public void CalendarioCargando_IgualAlMockup_1366x768() => Comparar(DatosCalendario.Cargando(), "finanzas/calendario-pagos-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioVacio_IgualAlMockup() => Comparar(DatosCalendario.SinDatos(), "finanzas/calendario-pagos-vacio.html");
    [AvaloniaFact] public void CalendarioVacio_IgualAlMockup_1366x768() => Comparar(DatosCalendario.SinDatos(), "finanzas/calendario-pagos-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void CalendarioSinConexion_IgualAlMockup() => Comparar(DatosCalendario.SinConexion(), "finanzas/calendario-pagos-sin-conexion.html");
    [AvaloniaFact] public void CalendarioSinConexion_IgualAlMockup_1366x768() => Comparar(DatosCalendario.SinConexion(), "finanzas/calendario-pagos-sin-conexion.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh Calendario`. Expected: 8 filas APROBADAS con el umbral por defecto. Si una columna medida queda corrida (convención 14), re-medir y corregir el mockup (≤ 4 px se anota; más vuelve al usuario). Un `UmbralAprobado` solo tras revisar cada tesela con el usuario.

- [ ] **Step 6: Mutaciones (QUITANDO)**

- (o) En `SincronizarColumnasDePago`, quitar las tres líneas de asignación → rojo `SinPagos_LaColumnaRegistrarPagoNoSeVeEnNingunaGrilla` y `Un403_QueQuitaPagos_…`. Restaurar.
- (p) En `AlCambiarElVm`, quitar `string.IsNullOrEmpty(e.PropertyName) ||` → rojo `Un403_QueQuitaPagos_OcultaLaColumnaSinNavegar`. Restaurar.
- (q) Quitar el `IsVisible` del `Button` de la celda → `ConPagos_…` verde y `SinPagos_…` verde (la columna oculta lo tapa): anotar que el botón es la segunda barrera y que su custodia es la verificación orgánica; NO inventar un test que exija las dos. Restaurar.
- (r) Quitar `Classes="negativo"` del VENCE de Vencidas → rojo `VenceDeVencidas_EnTonoPeligro` y la comparación `Calendario_IgualAlMockup`. Restaurar.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml.cs tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs tests/StockApp.Presentation.UiTests/V2/CalendarioPagosViewV2Tests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/CalendarioComparacionTests.cs
git commit -m "feat(ui-v2): calendario de pagos v2 con vencimiento, número de factura y columna de pago por permiso"
```

### Task 8: `v2:BarraEjecucion` y Control POA v2

**Depende de:** Tasks 3, 4, 5 y 7 (`FinanzasVistasFake`).

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/BarraEjecucion.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml`
- Modify: `src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml` (el `.axaml.cs` no cambia: doble click y `DataContextChanged → CargarAsync`)
- Create: `tests/StockApp.Presentation.UiTests/V2/BarraEjecucionTests.cs`, `tests/StockApp.Presentation.UiTests/V2/ControlPoaViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/ControlPoaComparacionTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (L68 y L120), `SignoNegativoBadgeTests.cs` (sitio Control POA), `EtiquetadoDeCamposTests.cs` (`TieneCampoFormularioCon`), `GrillasSinRecorteBarridoTests.cs` (escenario "Control POA", solo si hace falta)

**Interfaces:**
- Consumes: `ControlPoaViewModel` (Task 3), `PorcentajeConverter`, `MonedaConverter`, `EsNegativoConverter`, `ColumnaAuto`, `FinanzasVistasFake` (Task 7).
- Produces: `BarraEjecucion` (`Porcentaje` decimal, `Pista`, `Relleno`, `RellenoExcedido` brushes, `RellenoVigente`, `AnchoRelleno`); en la vista, `x:Name` `Encabezado`, `BotonExportarCsv`, `BotonExportarPdf`, `EstadoSinPermiso`, `Tarjeta`, `Filtros`, `CampoEjercicio`, `IndicadorCarga`, `EstadoSinConexion`, `BotonReintentar`, `EstadoSinLineas`, `Grilla`, `PieConteo`.

- [ ] **Step 1: Tests del componente (rojo)**

`tests/StockApp.Presentation.UiTests/V2/BarraEjecucionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using StockApp.Presentation.Controls.V2;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

public class BarraEjecucionTests
{
    private static BarraEjecucion Montar(decimal porcentaje)
    {
        var ventana = MontajeV2.Montar($"""<v2:BarraEjecucion x:Name="Barra" Porcentaje="{porcentaje.ToString(System.Globalization.CultureInfo.InvariantCulture)}" HorizontalAlignment="Left" />""");
        return MontajeV2.Buscar<BarraEjecucion>(ventana, "Barra");
    }

    [AvaloniaTheory]
    [InlineData(47.3, 30)]
    [InlineData(94.26, 60)]
    [InlineData(100, 64)]
    [InlineData(114.3, 64)]
    [InlineData(0, 0)]
    public void Geometria_64x6_YRellenoProporcionalConTopeEnCien(decimal porcentaje, double ancho)
    {
        var barra = Montar(porcentaje);

        Assert.Equal(64, barra.Bounds.Width);
        Assert.Equal(6, barra.Bounds.Height);
        Assert.Equal(ancho, barra.AnchoRelleno);
    }

    [AvaloniaFact]
    public void HastaCien_RellenoAcento_PistaBorde()
    {
        var barra = Montar(100m);

        Assert.Equal(MontajeV2.Hex("#1D4ED8"), MontajeV2.ColorDe(barra.RellenoVigente));
        Assert.Equal(MontajeV2.Hex("#E7E5E4"), MontajeV2.ColorDe(barra.Pista));
    }

    [AvaloniaFact]
    public void PorEncimaDeCien_RellenoPeligro()
    {
        var barra = Montar(114.3m);

        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(barra.RellenoVigente));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~BarraEjecucionTests"` → FAIL de compilación. Pegar.

- [ ] **Step 2: Componente**

`src/StockApp.Presentation/Controls/V2/BarraEjecucion.cs`:

```csharp
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Barra fina de ejecución presupuestal (Control POA): pista y relleno redondeados, el relleno proporcional al porcentaje
/// con tope en 100 y en tono de peligro por encima. Se dibuja a mano (dos rectángulos) para fijarla contra el mockup:
/// el ProgressBar de Fluent trae alturas mínimas y animación.
/// </summary>
public class BarraEjecucion : Control
{
    public static readonly StyledProperty<decimal> PorcentajeProperty =
        AvaloniaProperty.Register<BarraEjecucion, decimal>(nameof(Porcentaje));

    public static readonly StyledProperty<IBrush?> PistaProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(Pista));

    public static readonly StyledProperty<IBrush?> RellenoProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(Relleno));

    public static readonly StyledProperty<IBrush?> RellenoExcedidoProperty =
        AvaloniaProperty.Register<BarraEjecucion, IBrush?>(nameof(RellenoExcedido));

    static BarraEjecucion()
    {
        AffectsRender<BarraEjecucion>(PorcentajeProperty, PistaProperty, RellenoProperty, RellenoExcedidoProperty);
    }

    public decimal Porcentaje { get => GetValue(PorcentajeProperty); set => SetValue(PorcentajeProperty, value); }
    public IBrush? Pista { get => GetValue(PistaProperty); set => SetValue(PistaProperty, value); }
    public IBrush? Relleno { get => GetValue(RellenoProperty); set => SetValue(RellenoProperty, value); }
    public IBrush? RellenoExcedido { get => GetValue(RellenoExcedidoProperty); set => SetValue(RellenoExcedidoProperty, value); }

    /// <summary>El relleno que se pinta: el de peligro cuando la línea gastó más que su presupuesto.</summary>
    public IBrush? RellenoVigente => Porcentaje > 100m ? RellenoExcedido : Relleno;

    /// <summary>Ancho del relleno en píxeles enteros (el mockup escribe el mismo redondeo).</summary>
    public double AnchoRelleno => Math.Round(Bounds.Width * (double)Math.Clamp(Porcentaje, 0m, 100m) / 100d, MidpointRounding.AwayFromZero);

    public override void Render(DrawingContext contexto)
    {
        var alto = Bounds.Height;
        var radio = alto / 2;
        if (Pista is { } pista)
            contexto.DrawRectangle(pista, null, new Rect(0, 0, Bounds.Width, alto), radio, radio);
        if (AnchoRelleno > 0 && RellenoVigente is { } relleno)
            contexto.DrawRectangle(relleno, null, new Rect(0, 0, AnchoRelleno, alto), radio, radio);
    }
}
```

En `ComponentesV2.axaml`, después del `ControlTheme` de `Alerta`:

```xml
    <!-- Barra de ejecución de Control POA: 64 x 6; pista en el color de borde, relleno acento y peligro por encima de 100 %. -->
    <ControlTheme x:Key="{x:Type v2:BarraEjecucion}" TargetType="v2:BarraEjecucion">
        <Setter Property="Width" Value="64" />
        <Setter Property="Height" Value="6" />
        <Setter Property="VerticalAlignment" Value="Center" />
        <Setter Property="Pista" Value="{DynamicResource V2BordeBrush}" />
        <Setter Property="Relleno" Value="{DynamicResource V2AcentoBrush}" />
        <Setter Property="RellenoExcedido" Value="{DynamicResource V2PeligroTextoBrush}" />
    </ControlTheme>
```

Run: `dotnet build StockApp.sln` y `BarraEjecucionTests` → PASS. Correr `CatalogoComponentesTests` (VisualTests) y `ControlesConsumenTokensTests` (UiTests): sin cambios. Herramientas.Tests y `tokens --verificar` verdes.

- [ ] **Step 3: Tests de la vista (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ControlPoaViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.ApiClient;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.Views.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Control POA v2 con la View REAL: estructura, estados, Ejercicio que recarga, insignia y barra por token,
/// formato es-UY y doble click a Gastos.</summary>
public class ControlPoaViewV2Tests
{
    private static ControlPoaLineaDto L(int id, string nombre, decimal presupuesto, decimal gastado, int ejercicio = 2026) =>
        new(id, nombre, "Obras", ejercicio, presupuesto, gastado, presupuesto - gastado,
            Math.Round(gastado / presupuesto * 100m, 2), gastado > presupuesto);

    private static readonly ControlPoaLineaDto Normal = L(1, "Mantenimiento de espacios públicos", 1_200_000m, 567_600m);
    private static readonly ControlPoaLineaDto Excedida = L(5, "Limpieza de playas", 300_000m, 342_900m);

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);
    }

    private sealed record Montaje(Window Ventana, ControlPoaView Vista, ControlPoaViewModel Vm, FinanzasVistasFake Servicio,
        NavigationServiceFake Navegacion);

    private static Montaje Montar(FinanzasVistasFake servicio, double ancho = 1040)
    {
        var navegacion = new NavigationServiceFake();
        var vm = new ControlPoaViewModel(servicio, navegacion, new CsvExporterSinUsoFake(), new GuardadoSinUsoFake(),
            new ConfirmacionServiceFake(), new PdfExporterSinUsoFake(), new ServicioAperturaArchivoFake(),
            new SesionFake(RolUsuario.Operador, StockApp.Application.Authorization.Permisos.VerFinanzas), new RelojFijo())
        { EsperaDebounce = TimeSpan.Zero };
        var vista = new ControlPoaView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Control<T>(Montaje m, string nombre) where T : Control => m.Vista.FindControl<T>(nombre)!;
    private static bool Visible(Control c) => ArbolVisual.EsVisibleEnArbol(c);

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoConTitulo_ExportesSecundarios_SinActualizar()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Control POA", Control<EncabezadoPagina>(m, "Encabezado").Titulo);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarCsv").Classes);
        Assert.Contains("secundario", Control<Button>(m, "BotonExportarPdf").Classes);
        Assert.Same(m.Vm.ExportarPdfCommand, Control<Button>(m, "BotonExportarPdf").Command);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => (b.Content as string) == "Actualizar");
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void ConLineas_FormatoEsUy_PieDeConteo_YBarra()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });

        var textos = m.Vista.GetVisualDescendants().OfType<TextBlock>().Where(ArbolVisual.EsVisibleEnArbol).Select(t => t.Text).ToList();
        Assert.Contains("$ 1.200.000,00", textos);
        Assert.Contains("47,30 %", textos);
        Assert.Equal("1 línea", Control<TextBlock>(m, "PieConteo").Text);
        var barra = Assert.Single(m.Vista.GetVisualDescendants().OfType<BarraEjecucion>());
        Assert.Equal(30, barra.AnchoRelleno);
    }

    [AvaloniaFact]
    public void Sobreejecutada_InsigniaPeligro_NumeroNegativo_YBarraPeligro_SoloEnEsaFila()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal, Excedida] });

        var insignias = m.Vista.GetVisualDescendants().OfType<Insignia>().Where(i => i.Texto == "Sobreejecutado").ToList();
        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(VarianteInsignia.Peligro, visible.Variante);
        var saldo = m.Vista.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == "-$ 42.900,00");
        Assert.Contains("negativo", saldo.Classes);
        var barras = m.Vista.GetVisualDescendants().OfType<BarraEjecucion>().ToList();
        Assert.Equal(MontajeV2.Hex("#B91C1C"), MontajeV2.ColorDe(barras.Single(b => b.Porcentaje > 100m).RellenoVigente));
        Assert.Equal(MontajeV2.Hex("#1D4ED8"), MontajeV2.ColorDe(barras.Single(b => b.Porcentaje <= 100m).RellenoVigente));
    }

    [AvaloniaFact]
    public void Cargando_IndicadorEnLaCard_ConElFiltroAMano()
    {
        var m = Montar(new FinanzasVistasFake { PendientePoa = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>() });

        Assert.True(Visible(Control<EstadoCargando>(m, "IndicadorCarga")));
        Assert.True(Visible(Control<CampoFormulario>(m, "CampoEjercicio")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));
        Assert.False(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void SinLineas_VacioConSuTexto()
    {
        var m = Montar(new FinanzasVistasFake());

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));
        Assert.Equal("No hay líneas POA en este ejercicio", Control<EstadoVacio>(m, "EstadoSinLineas").Titulo);
        Assert.False(Visible(Control<TextBlock>(m, "PieConteo")));
    }

    [AvaloniaFact]
    public async Task SinConexion_ReintentarConClickReal_Carga()
    {
        var servicio = new FinanzasVistasFake { FallaPoa = new ServidorNoDisponibleException() };
        var m = Montar(servicio);
        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinLineas")));

        servicio.FallaPoa = null;
        servicio.Lineas = [Normal];
        MontajeV2.Clic(m.Ventana, Control<Button>(m, "BotonReintentar"));
        await Task.Yield();
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(Control<EstadoVacio>(m, "EstadoSinConexion")));
        Assert.True(Visible(Control<DataGrid>(m, "Grilla")));
    }

    [AvaloniaFact]
    public void Un403_SinPermisoEnLugarDeLaCard()
    {
        var m = Montar(new FinanzasVistasFake { FallaPoa = new UnauthorizedAccessException("403") });

        Assert.True(Visible(Control<EstadoVacio>(m, "EstadoSinPermiso")));
        Assert.False(Visible(Control<TarjetaContenido>(m, "Tarjeta")));
    }

    [AvaloniaFact]
    public async Task CambiarEjercicioEnElCampo_RecargaConEseAnio()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var campo = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<NumericUpDown>().Single();

        campo.Value = 2025;
        await m.Vm._tareaDebounce;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(new[] { 2026, 2025 }, m.Servicio.EjerciciosConsultados);
    }

    [AvaloniaFact]
    public void EjercicioInvalido_NoRecarga()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var texto = Control<CampoFormulario>(m, "CampoEjercicio").GetVisualDescendants().OfType<TextBox>().First();

        texto.Text = "20a5";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(2026, m.Vm.Ejercicio);
        Assert.Equal(new[] { 2026 }, m.Servicio.EjerciciosConsultados);
    }

    [AvaloniaFact]
    public void DobleClickEnUnaFila_AbreGastosDeEsaLinea()
    {
        var m = Montar(new FinanzasVistasFake { Lineas = [Normal] });
        var grilla = Control<DataGrid>(m, "Grilla");
        var celda = grilla.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == Normal.Nombre);
        var centro = celda.TranslatePoint(new Avalonia.Point(celda.Bounds.Width / 2, celda.Bounds.Height / 2), m.Ventana)!.Value;

        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        m.Ventana.MouseDown(centro, MouseButton.Left, clickCount: 2);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(typeof(GastosViewModel), m.Navegacion.UltimoDestino);
    }
}
```

Antes de escribirlo, confirmar con CodeGraph: (1) el nombre de la propiedad de `NavigationServiceFake` que registra el último destino (si no existe, usar la que tenga y anotarlo; si no registra nada, agregar `public Type? UltimoDestino { get; private set; }` en `Navegar<TVm>` de las dos sobrecargas); (2) si `HeadlessWindowExtensions.MouseDown` acepta `clickCount` (si no, usar `RaiseEvent` de `DoubleTappedEvent` sobre la celda, como haga `DataGridSortClickTests` o el test más cercano que dispare doble tap, y anotarlo); (3) los fakes `CsvExporterSinUsoFake`, `GuardadoSinUsoFake`, `PdfExporterSinUsoFake`: si ya existen equivalentes internos en `UiTests` (`ExportarPdfBotonesGuardianTests` usa `CsvExporterFake`, `GuardadoFake`, `PdfExporterNoOpFake` privados), moverlos a `FinanzasVistasFakes.cs` como `internal` con esos nombres y anotarlo.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlPoaViewV2Tests"` → FAIL. Pegar.

- [ ] **Step 4: Vista v2**

`ControlPoaView.axaml` completo (los anchos FIJOS de SALDO y % EJECUCIÓN son los MEDIDOS en la Task 5; 240 y 168 son los valores de referencia):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:dto="using:StockApp.Application.Finanzas"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.ControlPoaView"
             x:DataType="vm:ControlPoaViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (listado §5.1). Espejo: docs/ui-v2/mockups/finanzas/control-poa*.html. Sin Actualizar: cambiar el
         Ejercicio recarga sola. Carga, vacío y sin servidor dentro de la card con el filtro a mano; tras un 403, Sin permiso
         en lugar de la card. Saldo negativo: insignia Sobreejecutado y número por token, nunca opacidad. Doble click en una
         fila abre sus facturas (code-behind). -->
    <DockPanel Margin="28,24">

        <v2:EncabezadoPagina x:Name="Encabezado" DockPanel.Dock="Top" Titulo="Control POA"
                             Subtitulo="Ejecución del presupuesto por línea. Doble click en una línea abre sus facturas.">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button x:Name="BotonExportarCsv" Classes="secundario" Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" />
                <Button x:Name="BotonExportarPdf" Classes="secundario" Content="Exportar PDF" Command="{Binding ExportarPdfCommand}" />
            </StackPanel>
        </v2:EncabezadoPagina>

        <v2:EstadoVacio x:Name="EstadoSinPermiso" DockPanel.Dock="Top" UseLayoutRounding="False"
                        Titulo="Sin permiso" Mensaje="{Binding MensajeSinPermiso}" EsError="True"
                        IsVisible="{Binding SinPermiso}" />

        <v2:TarjetaContenido x:Name="Tarjeta" IsVisible="{Binding !SinPermiso}">
            <DockPanel>
                <v2:BarraFiltros x:Name="Filtros" DockPanel.Dock="Top" MostrarBusqueda="False">
                    <v2:CampoFormulario x:Name="CampoEjercicio" Etiqueta="Ejercicio">
                        <NumericUpDown Value="{Binding Ejercicio}" Minimum="2020" Maximum="2100" FormatString="0" Width="120"
                                       beh:NumericUpDownValidacionBehavior.Que="año" />
                    </v2:CampoFormulario>
                </v2:BarraFiltros>

                <TextBlock x:Name="PieConteo" DockPanel.Dock="Bottom" Classes="pie-conteo" Margin="0,12,0,0"
                           Text="{Binding Filas.Count, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='línea|líneas'}">
                    <TextBlock.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Filas.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </TextBlock.IsVisible>
                </TextBlock>

                <v2:EstadoCargando x:Name="IndicadorCarga" DockPanel.Dock="Top" Texto="Cargando el control POA…">
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

                <v2:EstadoVacio x:Name="EstadoSinLineas" DockPanel.Dock="Top" UseLayoutRounding="False"
                                Titulo="No hay líneas POA en este ejercicio" Mensaje="Probá con otro ejercicio.">
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="Filas.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
                </v2:EstadoVacio>

                <DataGrid x:Name="Grilla" ItemsSource="{Binding FilasView}" SelectedItem="{Binding FilaSeleccionada}"
                          DoubleTapped="OnFilaDobleClick" IsReadOnly="True" CanUserSortColumns="True" CanUserResizeColumns="True"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Filas.Count" Converter="{x:Static conv:ConteoConverter.HayElementos}" />
                            <Binding Path="SinConexion" Converter="{x:Static BoolConverters.Not}" />
                        </MultiBinding>
                    </DataGrid.IsVisible>
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="LÍNEA" Width="*" MinWidth="200" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <TextBlock Text="{Binding Nombre}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Nombre}"
                                               beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="PROGRAMA" Width="Auto" SortMemberPath="Programa" beh:ColumnaAuto.Medir="True">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <TextBlock Text="{Binding Programa}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Programa}"
                                               beh:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="PRESUPUESTO" Width="Auto" CellStyleClasses="num" SortMemberPath="Presupuesto"
                                                beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <TextBlock Text="{Binding Presupuesto, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="GASTADO" Width="Auto" CellStyleClasses="num" SortMemberPath="Gastado"
                                                beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <TextBlock Text="{Binding Gastado, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <!-- Ancho fijo medido con el peor caso (insignia + saldo negativo de 8 cifras): una sola Grid con la
                             insignia a la izquierda y el número a la derecha, como STOCK NUEVO del historial (la grilla recicla
                             celdas al ordenar). -->
                        <DataGridTemplateColumn Header="SALDO" Width="240" CellStyleClasses="num" SortMemberPath="Saldo">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <Grid VerticalAlignment="Center">
                                        <v2:Insignia Texto="Sobreejecutado" Variante="Peligro" HorizontalAlignment="Left"
                                                     IsVisible="{Binding Saldo, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
                                        <TextBlock Text="{Binding Saldo, Converter={x:Static conv:MonedaConverter.Instance}}"
                                                   Classes.negativo="{Binding Saldo, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                                   HorizontalAlignment="Right" VerticalAlignment="Center" />
                                    </Grid>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="% EJECUCIÓN" Width="168" CellStyleClasses="num" SortMemberPath="PorcentajeEjecucion">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ControlPoaLineaDto">
                                    <Grid VerticalAlignment="Center">
                                        <v2:BarraEjecucion Porcentaje="{Binding PorcentajeEjecucion}" HorizontalAlignment="Left" />
                                        <TextBlock Text="{Binding PorcentajeEjecucion, Converter={x:Static conv:PorcentajeConverter.Instance}}"
                                                   HorizontalAlignment="Right" VerticalAlignment="Center" />
                                    </Grid>
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

Confirmar que `ConteoConverter` acepta `'línea|líneas'` (convención 10) y que `Classes.negativo` colorea dentro de `DataGridCell` (`DataGridV2.axaml` L174).

Tests heredados (convención 13):
- `GuardianDePatronTests`: quitar `[InlineData(typeof(ControlPoaView), "Control POA", "FINANZAS")]` (L68) y `typeof(ControlPoaView),` de `VistasDeLaTanda` (L120); agregar a `VistasMigradasAV2`. Reemplazo: `Raiz_TieneUi2_EncabezadoConTitulo_ExportesSecundarios_SinActualizar`.
- `SignoNegativoBadgeTests.Montar_ControlPoaViewConSaldoNegativo_…`: reemplazar `BadgesConTexto(window, "Sobreejecutado")` por el patrón del sitio Historial del mismo archivo (L250-255): `Insignia` con `Texto == "Sobreejecutado"`, una sola visible y `Variante == Peligro`. Montar con `height: 900` (sin `DataContext` todos los estados quedan visibles sobre la grilla, como anota el sitio Historial).
- `EtiquetadoDeCamposTests.TieneCampoFormularioCon`: la regex acepta `(c|v2):CampoFormulario` (`$@"<(c|v2):CampoFormulario\b[^>]*Etiqueta=""{Regex.Escape(etiqueta)}"""`).
- `GrillasSinRecorteBarridoTests` escenario "Control POA": correrlo; si el SALDO medido con el peor caso del barrido no entra, la medición de la Task 5 estaba mal: re-medir, no relajar el test.
- `ExportarPdfBotonesGuardianTests.MontarControlPoa`: debe seguir verde sin cambios (busca "Exportar PDF" por texto).

- [ ] **Step 5: Build, verde y regresión**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ControlPoaViewV2Tests|FullyQualifiedName~BarraEjecucion|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~SignoNegativoBadge|FullyQualifiedName~EtiquetadoDeCampos|FullyQualifiedName~ExportarPdfBotones|FullyQualifiedName~GrillasSinRecorte|FullyQualifiedName~GrillasEspacioDerecho|FullyQualifiedName~NumericUpDown|FullyQualifiedName~DataGridColumnas|FullyQualifiedName~ShellMainViewGates"
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ControlPoa|FullyQualifiedName~ComandosDeGuardado|FullyQualifiedName~ColumnasPdfContraDtosReales"
```

- [ ] **Step 6: Comparaciones**

`tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/ControlPoaComparacionTests.cs` (mismo `Comparar` que el Calendario):

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Finanzas;

/// <summary>Control POA dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en los dos renders.</summary>
[Trait("Categoria", "ComparacionVisual")]
public class ControlPoaComparacionTests
{
    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: [".v2-sidebar"], prepararApp: ProductosComparacionTests.OcultarSidebar);

    [AvaloniaFact] public void ControlPoa_IgualAlMockup() => Comparar(DatosControlPoa.ConLineas(), "finanzas/control-poa.html");
    [AvaloniaFact] public void ControlPoa_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.ConLineas(), "finanzas/control-poa.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaSobreejecutado_IgualAlMockup() => Comparar(DatosControlPoa.Sobreejecutado(), "finanzas/control-poa-sobreejecutado.html");
    [AvaloniaFact] public void ControlPoaSobreejecutado_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.Sobreejecutado(), "finanzas/control-poa-sobreejecutado.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaCargando_IgualAlMockup() => Comparar(DatosControlPoa.Cargando(), "finanzas/control-poa-cargando.html");
    [AvaloniaFact] public void ControlPoaCargando_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.Cargando(), "finanzas/control-poa-cargando.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaVacio_IgualAlMockup() => Comparar(DatosControlPoa.SinLineas(), "finanzas/control-poa-vacio.html");
    [AvaloniaFact] public void ControlPoaVacio_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.SinLineas(), "finanzas/control-poa-vacio.html", TamanoLienzo.Municipio);
    [AvaloniaFact] public void ControlPoaSinConexion_IgualAlMockup() => Comparar(DatosControlPoa.SinConexion(), "finanzas/control-poa-sin-conexion.html");
    [AvaloniaFact] public void ControlPoaSinConexion_IgualAlMockup_1366x768() => Comparar(DatosControlPoa.SinConexion(), "finanzas/control-poa-sin-conexion.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh ControlPoa`. Expected: 10 filas APROBADAS con el umbral por defecto (mismo criterio que la Task 7 si no).

- [ ] **Step 7: Mutaciones (QUITANDO)**

- (s) En `BarraEjecucion.RellenoVigente`, quitar `Porcentaje > 100m ? RellenoExcedido : ` (queda `=> Relleno;`) → rojo `PorEncimaDeCien_RellenoPeligro`, `Sobreejecutada_…` y `ControlPoaSobreejecutado_IgualAlMockup`. Pegar los tres. Restaurar.
- (t) En `AnchoRelleno`, quitar el `Math.Clamp(…, 0m, 100m)` dejando `(double)Porcentaje` (quitar la llamada y sus dos argumentos de tope) → rojo `Geometria_…(114.3, 64)`. Restaurar.
- (u) En la vista, quitar `Classes.negativo="…"` del saldo → rojo `Sobreejecutada_…`. Restaurar.
- (v) Quitar el `IsVisible` de la `Insignia` → rojo `Sobreejecutada_…` (dos visibles) y `SignoNegativoBadgeTests` del sitio Control POA. Restaurar.
- (w) Quitar el `MultiBinding` de `EstadoSinLineas.IsVisible` (las líneas del bloque) → rojo `Cargando_…` (el vacío se ve mientras carga). Restaurar.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2/BarraEjecucion.cs src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml tests/StockApp.Presentation.UiTests/V2/BarraEjecucionTests.cs tests/StockApp.Presentation.UiTests/V2/ControlPoaViewV2Tests.cs tests/StockApp.Presentation.UiTests/V2/FinanzasVistasFakes.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs tests/StockApp.Presentation.UiTests/EtiquetadoDeCamposTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Finanzas/ControlPoaComparacionTests.cs
git commit -m "feat(ui-v2): control POA v2 con barra de ejecución, insignia de sobreejecutado y estados de carga"
```

(Sumar al `git add` los fakes movidos o `NavigationServiceFake` si se tocaron, y `GrillasSinRecorteBarridoTests.cs` si cambió.)

---

## Bloque D — Guardianes, verificación orgánica y cierre

### Task 9: Repaso de guardianes y de mutaciones; suite y comparación completas

**Files:**
- Modify (local): el ledger
- Modify: los tests que haga falta ajustar (cada ajuste con su rojo y su motivo)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la evidencia completa (suite, comparación, mutaciones) para el GATE de la Task 10.

- [ ] **Step 1: Guardianes de la fase**

Confirmar con grep y CodeGraph, y anotar en el ledger:
1. Las tres vistas están en `VistasMigradasAV2` y en ninguna otra lista de `GuardianDePatronTests`; `Guardian_CubreTodasLasVistasDelEnsamblado` verde.
2. `VistaV2_NoUsaPiezasDelTemaViejo` cubre las tres (`SignoNegativoBrushConverter`, `BadgeEstado`, `HeaderVista`, `c:EstadoVacio` ausentes).
3. `NingunaVista_TieneOverlaysDeModal` sigue verde.
4. `ComandosDeGuardadoGuardianTests` verde sin el `RecargarCommand` de Control POA y con `AgregarCommand` del panel en `Eximidos`.
5. `DataGridCeldaConControlPaddingPropioGuardTests`: las columnas con `Button` (Registrar pago) llevan `sin-padding-vertical`.
6. Ningún `Classes="primary"`/`"secondary"` ni `c:` del tema viejo en los tres `.axaml` (`grep -n 'Classes="primary"\|Classes="secondary"\|<c:' <los tres>` vacío).

Si un punto falla, se corrige en un commit propio con su rojo pegado.

- [ ] **Step 2: Repaso de mutaciones**

Confirmar en el ledger que quedaron PEGADOS los rojos de las reglas que pide la spec (sección 4): el gate de la acción (l, m, o), la confirmación al quitar (b), el tono peligro de la barra (s) y el reenvío de E5 (a); y las demás de la fase (c, d, e a k, n, p, r, t a w). Una sin rojo pegado se corre ahora. (q) queda anotada como "sin custodia automática: segunda barrera, cubierta por la verificación orgánica".

- [ ] **Step 3: Build, suite completa, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos `.ods` de `Infrastructure.Tests` / `Api.Tests` (confirmar por nombre contra los de la línea base); total ≥ línea base + nuevos − reemplazados (anotar número y reemplazos); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla).

- [ ] **Step 4: Commit (solo si hubo ajustes)**

```bash
git add <archivos ajustados>
git commit -m "test(ui-v2): guardianes de la Fase 4a al día"
```

### Task 10: Verificación orgánica (GATE con el usuario), docs de cierre y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 28 y 29)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda, §6.5)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 4a cerrada en `feat/ui-v2` (sin push ni merge).

- [ ] **Step 1: Verificación orgánica (spec §6.4) — app real contra la base local, en WSLg a 1280×760**

Preparación:
- `docker start stockapp-pg` (queda andando al terminar). **Backup ANTES de crear datos:** `mkdir -p ~/stockapp-backups-local && docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-4a-$(date +%Y%m%d-%H%M%S).sql` (usuario y base de `~/.config/GestionMunicipal/conexion.json` o del `.env` local; verificar con `ls -l` que el archivo no está vacío). Anotar la ruta en el ledger.
- API del worktree: `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (escucha en `http://localhost:5043`, confirmarlo contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`. `admin` / `Admin1234`.
- Operador de permisos MIXTOS: crear `f4amixto` con `finanzas.ver` y `finanzas.gastos` (sin `finanzas.pagos`); anotar su id.
- Datos de prueba con prefijo `PRUEBA-4A`: una factura a crédito vencida, una que vence en 5 días y una en 20 (proveedor de prueba), un pago reciente, una línea POA del ejercicio actual sobreejecutada; anotar los ids.
- Toolkit `scripts/gui-verificacion/`. MIRAR cada captura y comparar `md5sum` con la anterior (dos iguales cuando la pantalla debía cambiar = WSLg congelado: PARAR y pedir al usuario que reinicie WSL). Tras un diálogo modal la ventana principal puede quedar sin foco (recuperar con `xdotool windowminimize` + `windowmap`). El Aviso global puede quedar DETRÁS de la ventana: buscarlo con `xdotool search --name` antes de declarar un cuelgue.

Todo a **1280×760**; los pasos 1 y 3 se repiten a 1366×768. Capturar cada pantalla.

1. **Calendario (Admin):** cuatro cards con contador, VENCE en rojo en Vencidas, número de factura y "—", "Registrar pago" → Pagos de la factura → "Volver" regresa al Calendario. Comparar a ojo con `calendario-pagos.html`.
2. **Calendario con `f4amixto`:** sin la columna "Registrar pago" en las tres grillas; "Actualizar" funciona.
3. **Control POA (Admin):** filas con barra e insignia en la sobreejecutada (barra roja), cambiar Ejercicio con las flechas → recarga sola (sin botón), tipear "20a5" → campo en rojo y la grilla no cambia, ordenar por SALDO y % EJECUCIÓN, doble click → Gastos filtrado por esa línea, Exportar CSV y PDF (abrir el PDF).
4. **Sin servidor:** con la API detenida (`kill` SOLO del pid de `StockApp.Api`, acotado por nombre; nunca un comando de alcance global), abrir Calendario y Control POA → "No se pudo conectar con el servidor" con Reintentar en línea, sin modal global; levantar la API y "Reintentar" → carga. Anotar si salió un Aviso global y dónde.
5. **Adjuntos en una factura (Admin):** Gastos y facturas → editar la factura `PRUEBA-4A` → panel v2 dentro del formulario v1: "Agregar" un PDF y un JPG (`~/stockapp-pruebas/`), tamaño legible y fecha, "Ver" abre, "Quitar" pregunta con el nombre → "Cancelar" no quita → "Quitar" de nuevo y confirmar → desaparece.
6. **E5 en caliente:** con `f4amixto` editando la factura (panel con un adjunto), desde otra sesión de Admin quitarle `finanzas.gastos`; en la sesión de `f4amixto`, una acción que pegue al servidor (p. ej. "Ver" otro adjunto o "Guardar") dispara el 403 → tras cerrar el aviso, "Agregar" y "Quitar" del panel ya no se ven, sin navegar. Restaurar el permiso.
7. **Ingreso por factura:** guardar una factura `PRUEBA-4A` → la card "Adjuntá la factura" con el panel v2 a la vista (D16) → adjuntar un PDF → Finalizar.
8. **Limpieza:** listar los ids creados (prefijo `PRUEBA-4A` y el usuario `f4amixto`) y PREGUNTAR al usuario si se borran; SQL listo, acotado por esos ids, SOLO contra el contenedor local `stockapp-pg` (nunca el VPS). Anotar en el ledger lo borrado o lo que quedó. El contenedor queda andando.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5 de las capturas), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa.

- [ ] **Step 2: GATE — aprobación final del usuario**

PARAR. Presentar: `index.html` (las filas de Calendario, Control POA, Adjuntos e Ingreso por factura guardado), las capturas de la verificación orgánica junto a los mockups, los rojos de las mutaciones (a) a (w), los tests heredados reemplazados (con su reemplazo), los `UmbralAprobado` si los hubo y los cambios respecto de los mockups aprobados. Registrar en el ledger: `Fase 4a: aprobada por <quién> <fecha>`. Lo que el usuario observe se arregla en commits propios con TDD (rojo pegado) o va a la deuda con su OK.

- [ ] **Step 3: `herramientas.md` y spec**

`herramientas.md`, después de la 27: "Convenciones que agrega la Fase 4a (Adjuntos, Calendario de pagos y Control POA)" con la 28 y la 29 de este plan (como quedaron aprobadas).

Spec:
1. `**Estado:**`: "; Fase 4a (Adjuntos, Calendario de pagos y Control POA) APROBADA por el usuario el <fecha>" y "4a" en la lista de fases implementadas; la fila de Finanzas de la tabla de §7.
2. §7, después de "Estado de la Fase 3c-3": "**Estado de la Fase 4a (aprobada el <fecha>).** `AdjuntosPanelView` v2 (también dentro de los hosts v1, excepción inversa hasta 4d y 4e), Calendario de pagos v2 (cuatro cards con contador, VENCE y número de factura, columna Registrar pago por permiso con la convención 28) y Control POA v2 (`v2:BarraEjecucion`, `Insignia` Sobreejecutado, Ejercicio que recarga solo), con E1 a E5. Comparado en <N> filas a los dos tamaños (<máximo de teselas densas>). Suite <total> en `<hash>`. Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D18 aprobadas o cambiadas>."
3. §6.5: cada compensación nueva medida en las Tasks 6 a 8 (si hubo), con su antes y después.
4. "**Deuda abierta tras la Fase 4a:**" con, como mínimo: los hosts `GastoFormView` y `PagosGastoView` en v1 hasta 4d y 4e (el panel v2 adentro); lo observado en el paso 4 (Aviso global); cualquier `UmbralAprobado`; (q) sin custodia automática; los minors diferidos del ledger y los datos de prueba que hayan quedado.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 4a (adjuntos, calendario de pagos y control POA)"
```

- [ ] **Step 4: Review final de la rama**

Review de todo el rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus", que la excepción a "VMs intactos" no exceda E1 a E5, que ninguna pieza del tema viejo haya quedado en las tres vistas y que la columna por permiso se re-evalúe tras un 403. Los hallazgos se resuelven en commits propios (TDD, con rojo pegado) o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- Migrar `GastoFormView`, `PagosGastoView` (4d, 4e), Ingresos de caja (4b) y Libro caja (4c).
- Cambios de API o de datos: `FacturaCalendarioDto` ya trae vencimiento y número de factura; el porcentaje y el saldo los calcula el servidor.
- Estado "sin conexión" del panel de adjuntos (sus errores siguen por `IConfirmacionService.InformarAsync`) y de la carga de combos de los hosts.
- Gate de `CanExecute` en Agregar y Quitar del panel (los botones se ocultan y el servidor exige el permiso).
- Modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| La columna oculta por code-behind no se re-evalúa en algún camino (navegación que reusa la vista, `DataContext` que cambia dos veces). | `Un403_QueQuitaPagos_…` con el camino real del shell; el code-behind desuscribe el VM anterior; mutaciones (o) y (p); paso 2 de la verificación orgánica. |
| `MultiBinding` con `BoolConverters.Or` y rutas con `!` no compila o no se re-evalúa al cambiar un `Count`. | Alternativa escrita en la Task 7 (`BoolConverters.Not`); `Cargando_SinDatos_…` y `ConDatos_…` fijan las dos transiciones; las comparaciones `-cargando` y `-vacio`. |
| Cuatro `DataGrid` dentro de un `ScrollViewer` sin virtualización se ponen lentos. | Volumen real: decenas de facturas abiertas; la verificación orgánica lo mira; si se nota, se anota con el número (no se resuelve en esta fase). |
| SALDO y % EJECUCIÓN de ancho fijo no entran con montos de 9 cifras o la grilla scrollea a 1280. | Medición del peor caso del barrido en la Task 5; `GrillasSinRecorteBarridoTests` corre en la Task 8; el GATE decide. |
| El panel v2 dentro de los formularios v1 se ve fuera de lugar (otra tipografía alrededor). | Excepción inversa declarada en la spec y temporal (4d, 4e); paso 5 de la verificación orgánica; lo que se vea va a la deuda. |
| Cambiar el Ejercicio con las flechas dispara varias consultas o una vieja pisa a la nueva. | Debounce con cancelación y `ConsultaVigente`; mutaciones (h) e (i); `CambiarEjercicioEnElCampo_…` con la View real. |
| E5 se arregla en el VM pero un host nuevo (4b a 4e) embebe el panel y no reenvía. | Convención 29 nueva; `NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos` en los tres hosts actuales. |
| Un test heredado se borra sin reemplazo. | Convención 13: los reemplazos de `GuardianDePatronTests` L68 y L70 nombrados en las Tasks 7 y 8; `SignoNegativoBadgeTests` y `EtiquetadoDeCamposTests` se adaptan; el cierre lista los reemplazos. |
| WSLg se congela o pierde el foco y la verificación orgánica da falsos rojos. | md5 de capturas, recupero `windowminimize` + `windowmap`, distinción artefacto/bug por el UiTest headless; si se congela, PARAR y pedir el reinicio al usuario. |
| La verificación orgánica ensucia la base local. | Backup antes; prefijo `PRUEBA-4A`; ids anotados; borrado con SQL acotado y con OK del usuario; nunca el VPS. |
