# UI v2 — Fase 3c-1 Patrones comunes (estado de carga y columnas `Auto` medidas) + retro en Productos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dejar resueltos, una sola vez y para todas las grillas que vienen, los dos patrones que la 3a dejó abiertos: (1) mientras algo carga la UI dice "Cargando…" y nunca muestra el estado vacío durante la carga ni tras un 403 silenciado; (2) las columnas `Auto` de contenido libre miden el texto ya formateado de TODAS las filas al cargar, con un tope, elipsis y tooltip solo cuando se corta. Se aplican de vuelta a Productos (el único listado v2) y se comparan contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** Cuatro piezas comunes y una retro. (a) `ViewModelBase` gana `Cargando` (contador de cargas en curso) dentro del camino único `EjecutarCargaProtegidaAsync`; `ProductoListViewModel` pasa su carga y su búsqueda por ese camino (excepción U1 a "VMs intactos", asentada en la spec §3 en la misma task). (b) `ColumnaAuto` (attached de columna) mide con un `TextBlock` sonda que copia la tipografía de una celda realizada, recorre la fuente entera (no solo las filas virtualizadas) y fija el ancho en píxeles; lo dispara el mismo `DataGridColumnasAutoBehavior` que ya está en el estilo global de toda `DataGrid`, así comparte el estado de "el usuario arrastró" y el único `LayoutUpdated`. (c) `TooltipSiSeCorta` cancela el tooltip de un `TextBlock` que no está cortado. (d) `v2:EstadoCargando` (texto, sin spinner, a confirmar en el GATE). Los mockups de los estados nuevos se aprueban en un GATE (Bloque B) antes de tocar el XAML de `ProductoListView` (Bloque C).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3 VMs intactos; §5.1 listado; §6 fidelidad, §6.5 calibración y compensaciones; §7 "Deuda abierta tras la Fase 3a" y "tras la Fase 3b"). Herramienta y convenciones 1 a 17: `docs/ui-v2/herramientas.md`. Planes de los que hereda formato, receta y herramientas de diagnóstico: `docs/superpowers/plans/2026-10-07-ui-v2-fase-3b-movimientos.md` y `docs/superpowers/plans/2026-10-06-ui-v2-fase-3a-productos.md` (sección "Herramientas de diagnóstico", L138-214: `teselas.py` y `tinta.py`).

Todos los comandos se corren desde la raíz del worktree: `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base `6c94355`). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Si CodeGraph devuelve un archivo como "changed on disk" sin fuente (pasó con `ViewModelBase.cs` y `MantenimientoViewModel.cs` al escribir este plan), leerlo con Read y decirlo en el reporte.

## Decisiones del usuario (2026-10-08, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Mientras algo carga, la UI dice que está cargando; nunca muestra el estado vacío ("No hay … para mostrar") durante la carga ni tras un 403 silenciado.** Excepción aprobada a "VMs intactos" (§3), asentada en la spec. | `ViewModelBase.Cargando` + `ProductoListViewModel` por `EjecutarCargaProtegidaAsync`; la spec §3 gana la excepción en la Task 1. El vacío solo con `!Cargando && !SinPermiso && Count == 0`. | 1, 6, 7 |
| U2 | **Columnas `Auto` de contenido libre: un componente común mide, al cargar/cambiar los datos, el texto YA FORMATEADO de cada columna sobre TODAS las filas y fija el ancho al máximo, con un tope; lo que pasa el tope se corta con elipsis y se ve completo en tooltip (idealmente solo cuando se corta). Un solo criterio para todas las grillas.** Rechazadas: mínimos fijos por columna y desactivar la virtualización. | `ColumnaAuto` (Task 2) + `TooltipSiSeCorta` (Task 3); en Productos, CÓDIGO, COSTO y UNIDAD (Task 6). Convención 19 en `herramientas.md`. | 2, 3, 6, 7 |
| U3 | **La 3c se parte en 3c-1 (este plan), 3c-2 (Historial) y 3c-3 (Ingreso por factura).** | Este plan NO toca `MovimientoHistorialView` ni `IngresoPorFacturaView`, ni el patrón de modal v2. El componente igual soporta `DataGridTextColumn` (lo necesita Historial) y se prueba con una grilla de test. | todas |
| U4 | **Lo que la spec no fija del diseño lo propone el mockup y lo decide el usuario en el GATE** (criterio de 3a y 3b). | D1 a D13 abajo. Si el usuario cambia una, se ajustan los mockups y los tests que la fijan en la Task 4, antes del XAML. | 4 |

## Decisiones que se confirman al aprobar los mockups (Task 4)

- **D1 — Indicador de carga: texto, sin spinner.** "Cargando productos…" (con el carácter `…`), 13/400, texto secundario `#57534E`, alto de línea 16, centrado, con el mismo margen vertical de 32 que el estado vacío (ocupa su lugar). Motivo: un spinner es animación, y la comparación por teselas y los mockups (§6.1: sin transiciones) no pueden fijarla; el antecedente del proyecto (Mantenimiento, "Cargando backups...") es texto. Alternativa: spinner (pide excepción a §6.1 y un test aparte que no sea de píxeles).
- **D2 — El indicador solo aparece si no hay filas para mostrar** (`Cargando && Items.Count == 0`). Una recarga o una búsqueda con filas en pantalla deja la grilla como está hasta que llegan los resultados (el VM no vacía `Items` hasta tenerlos). Alternativa: mostrarlo en cada carga (ocultaría la grilla en cada búsqueda con debounce: parpadeo con cada tecla).
- **D3 — Sin permiso en el listado: `EstadoVacio` "Sin permiso" (`EsError`) con "No tenés permiso para ver los productos.", que REEMPLAZA la card** (misma regla que la convención 12 para el formulario y `producto-sin-permiso.html`); el encabezado queda con sus botones según las reglas del VM. Alternativa: dentro de la card, debajo de la barra de búsqueda (dejaría un buscador que solo puede dar 403).
- **D4 — El formulario de Productos no muestra indicador de carga.** `ProductoFormViewModel` ya usa `EjecutarCargaProtegidaAsync`, así que su `Cargando` también se prende, pero el formulario no afirma nada falso mientras carga (los combos se llenan en milisegundos y la card ya está). Alternativa: indicador sobre la card (otro mockup y otro estado comparado).
- **D5 — Tope ADAPTABLE de las columnas medidas (REEMPLAZADO por el usuario en el GATE de la Task 4, 2026-10-08).** Ancho = min(max(contenido, título + reserva), 240, espacio libre que deja el resto de la grilla): la grilla NUNCA scrollea en horizontal ni corta una columna en el borde por una columna medida; lo que no entra se corta con elipsis + `TooltipSiSeCorta`. Lo implementa `DataGridColumnasAutoBehavior` en el reparto (no `ColumnaAuto.Medir`, que sigue devolviendo lo que la columna pide), recalculado al cambiar el ancho de la grilla y nunca al scrollear, ordenar ni filtrar (depende solo de lo medido y del ancho, no del reparto actual: sin loops). **Orden de cesión:** (1) las `*` bajan de su parte proporcional a su piso (160); (2) las medidas bajan desde su ancho medido nivelándose hacia abajo, la más ancha primero (CÓDIGO y COSTO cortos quedan enteros), hasta el ancho de su título + reserva de la flecha; (3) si ni así entra, las `*` NO bajan del piso (a 40 px el nombre no se lee, regla ya aprobada) y queda el scroll: en Productos eso pasa recién con la grilla por debajo de 886 px internos (las fijas 298 + títulos de CÓDIGO 85, COSTO 100 y UNIDAD 83 + pisos 320; ventana de ~1218 con el sidebar), por debajo de 1280 y 1366. Sin columnas `*`, las medidas ceden igual. Medido en headless sobre la vista real con `ConCeldaCortada`: UNIDAD 145 a 1280 (grilla 948 interna) y 231 a 1366 (1034), NOMBRE y CATEGORÍA 160, suma = ancho interno, sin scroll. Con los datos de siempre no cambia nada (UNIDAD 97). El 240 sigue siendo el tope con lugar de sobra (`ColumnaAuto.TopePorDefecto`). Tests: `ColumnaAutoTopeAdaptableTests` (UiTests). Reemplaza al "tope fijo de 240 con scroll" que mostraba el mockup del GATE.
- **D6 — El ancho reserva lugar para la flecha de orden del header** (20 = 8 de margen + 12 del glifo, `DataGridV2.axaml` `Path#SortIcon`): ordenar por click no corta el título ni mueve columnas. Costo: una columna cuyo título es más ancho que su contenido crece hasta 20 px respecto de hoy (CÓDIGO de Productos: 81 → ~86; `productos.html` cambia). Alternativa: sin reserva (ordenar por esa columna corta el título o le tapa la flecha).
- **D7 — El ancho medido solo crece mientras vive la grilla.** Una búsqueda que devuelve filas más angostas no achica columnas (no saltan mientras se tipea); una con un valor más ancho las agranda. Al volver a entrar a la pantalla (grilla nueva) se mide de cero. Alternativa: re-medir libre (las columnas se achican y agrandan con cada búsqueda).
- **D8 — Tooltip solo cuando el texto se corta, en las cinco columnas de texto de Productos** (CÓDIGO, NOMBRE, COSTO, UNIDAD, CATEGORÍA). NOMBRE y CATEGORÍA hoy muestran tooltip siempre: dejan de mostrarlo cuando el texto entra. Alternativa: solo en las tres medidas (dos criterios en la misma grilla).
- **D9 — Estados a mockear (a 1280×760 y 1366×768):** `productos-cargando` (sin filas, indicador), `productos-sin-permiso` (403 en la carga), `productos-celda-cortada` (una UNIDAD más larga que el tope, con elipsis) y `productos.html` re-medido (D6). `productos-vacio.html` no cambia (mismo dibujo; cambia la condición). **No se mockea el tooltip:** es un popup fuera del área comparada (la comparación no lo captura); su regla la custodia un UiTest (Task 6) y se ve en la verificación orgánica (Task 7). Alternativa: un mockup estático con el tooltip dibujado, presentado en el GATE sin comparación.
- **D10 — Datos de la celda cortada:** los 12 productos de `DatosProductos.Productos` con la UNIDAD de "Arena gruesa" (fila 2) cambiada a "Metro cúbico puesto en obra con flete incluido" (~290 px a 13 px: pasa el tope). Alternativa: un CÓDIGO largo.
- **D11 — El indicador no entra al catálogo de componentes.** `componentes/catalogo.html` es la pantalla de calibración del umbral (§6.5): agregarle un elemento mueve su línea base y pide re-aprobarla. El indicador se compara dentro de `productos-cargando`. Alternativa: agregarlo y re-aprobar el catálogo.
- **D12 — `UseLayoutRounding="False"` en el `ControlTheme` de `EstadoCargando`** (no por instancia): es el mismo caso de texto centrado con ancho fraccionario que `EstadoVacio`, pero el control es nuevo y no tiene línea base que mover (la deuda de la 3a pedía evaluarlo para lo que se repita). `EstadoVacio` no cambia.
- **D13 — Textos:** indicador "Cargando productos…"; sin permiso, título "Sin permiso" y mensaje "No tenés permiso para ver los productos." (constante `ProductoListViewModel.MensajeSinPermisoListado`). El vacío conserva "No hay productos para mostrar" / "Probá con otra búsqueda o creá uno con Nuevo.".

**Ruling R1 — `GrillasSinRecorteBarridoTests.SidebarPx` pasa de 232 a 240 en la 3c-1** (no se difiere a 3c-2). Motivo: la Task 6 ya edita ese archivo (le agrega la excepción del corte en el tope, U2), el sidebar real es el v2 de 240 en toda la app desde la Fase 1, y 3c-2 va a depender del barrido para Historial. Si al cambiarlo se pone rojo un escenario que NO es Productos (también `GrillasEspacioDerechoBarridoTests`, que usa la misma constante), es un recorte real a 1366 en la app de hoy: PARAR, anotarlo en el ledger con el mensaje del test y consultar al usuario; no se parchea esa vista en esta fase.

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10). Tokens de la spec §2 (copiados en el plan de la 3a): fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, texto primario `#1C1917`, texto secundario `#57534E`, peligro texto `#B91C1C`; control 13/400 con alto de línea 16; sidebar 240 (a 1280 quedan 1040 para la vista, a 1366 quedan 1126).
- Listado (§5.1, convención 10): `EncabezadoPagina` + `TarjetaContenido` con `BarraFiltros`, grilla y pie de conteo; conteo, vacío y visibilidad por `Items.Count` con `ConteoConverter` (bindear `Count`, nunca la colección).
- **ViewModels: solo lo que pide U1.** Cambian `ViewModelBase` (`Cargando`, `EmpezarCarga`, `TerminarCarga`), `ProductoListViewModel` (carga y búsqueda por `EjecutarCargaProtegidaAsync`, `MensajeSinPermisoListado`) y `MantenimientoViewModel` (su `[ObservableProperty] _cargando` choca por nombre con el `Cargando` de la base: pasa a usar el de la base, sin cambiar lo que muestra). Ningún otro VM. No cambian `ViewLocator`, `INavigationService`, `ErrorConverter`, la lógica de permisos ni ningún code-behind (`ProductoListView.axaml.cs` sigue con `DataContextChanged → CargarAsync`).
- **`calt` de Inter apagado en toda la UI v2** (Style `:is(Control).ui2` con `-calt`): todo `FontFeatures` propio incluye `-calt`. La sonda de medición NO declara `FontFeatures` propios: COPIA los del `TextBlock` de una celda realizada (que ya traen `-calt`, y `+tnum,-calt` en `.num`); lo fija `ConMilFilas_ElAnchoEsElDelTextoMasLargo...` con un valor "2x1,5" en los datos.
- **`tokens.json` es la fuente de `Tokens.axaml` (generado): nunca editar `Tokens.axaml` a mano.** Esta fase no prevé tokens nuevos (el indicador usa `V2FuenteControlTamano`, `V2FuenteControlAltoLinea`, `V2TextoSecundarioBrush`). Toda task (y su review) que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- **La comparación por teselas no ve un glifo ni un borde de 1 px.** Las reglas clave (vacío nunca durante la carga ni tras 403, indicador, sin permiso que reemplaza la card, ancho medido sobre toda la fuente, tope + elipsis, tooltip solo si se corta, reserva de la flecha, ceder ante el arrastre) se custodian con UiTests sobre la View REAL o sobre una grilla real con el tema real (efecto, no declaración), verificados por mutación QUITANDO: `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO lo quitado; se corre el test y se PEGA el rojo real; se restaura con `cp`; `git diff --no-index` vacío; verde. `export SCRATCH=~/.cache/stockapp-ui-v2-fase-3c1 && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`). Nunca se agrega un efecto destructivo para mutar. **NUNCA `git checkout --` ni `git restore`** (en la 3b un `git checkout` borró cambios sin commitear).
- **Compensaciones conocidas Chromium/Avalonia** (spec §6.5): Chromium colapsa márgenes verticales entre bloques y Avalonia los suma (ojo: `.pagina` y `.v2-tarjeta` de los mockups de Productos son `display: flex`, y entre ítems flex NO hay colapso, así que el `-16` de `ProductoFormView` NO se copia al listado); un `Grid` con `MaxWidth` y `UseLayoutRounding` da +1 px; texto centrado con ancho fraccionario pide `UseLayoutRounding="False"` (D12); texto con alto de línea fijo toma `LineHeight` de un token.
- Mockups (§6.1): `docs/ui-v2/mockups/inventario/<pantalla>.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosProductos`. Solo colores sólidos, bordes de 1 px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones/animaciones, pseudo-elementos decorativos, `text-shadow`. Sin scroll vertical de página. Se aprueban ANTES del XAML de la vista.
- Umbral (§6.5): `CompararConMockup` con los calibrados (11 teselas densas, 1,68 %) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado` tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto. Diagnóstico con `teselas.py` y `tinta.py` (plan de la 3a, "Herramientas de diagnóstico"; copiarlos a `$SCRATCH/diag/`, nunca se commitean).
- **Convención 6:** las pantallas se comparan DENTRO del shell real (`ShellMainView`, `CurrentContent` = el VM, resuelto por `ViewLocator`) con el sidebar oculto en ambos renders (`ProductosComparacionTests.OcultarSidebar`).
- Formato es-UY: el costo sale de `MonedaConverter` (`FormatoEsUy.Moneda`, independiente de la cultura del hilo); la medición usa ESE conversor (`ColumnaAuto.Conversor`), nunca un `ToString()` propio.
- Los comentarios de `.axaml` y `.csproj` NO pueden contener `--` (AVLN1001, cascadea a MSB4025). En vistas con `x:DataType` un binding mal escrito es error de build; los `DataTemplate` de celda llevan `x:DataType="dto:ProductoDto"`.
- **Build obligatorio** tras cada cambio de XAML o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Testear con la solución o con UN `.csproj` por comando.
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos (confirmar por nombre). Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 3", "D5", "U2" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits, la spec y `herramientas.md` sí pueden citar el plan.
- **Scripts del skill de ejecución** (`task-brief`, `review-package` de subagent-driven-development): se corren con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y después se verifica con `ls -l --time-style=full-iso` que el archivo quedó DENTRO del worktree y con fecha de ahora (en la 3a uno cayó en el checkout principal).
- Verificación GUI: la ventana se llama "Gestión Municipal". `capturar.sh` NO es fail-closed (con WSLg caído captura el escritorio de Windows con exit 0): mirar CADA captura. WSLg pierde el teclado tras un diálogo modal de la app (y `escribir.sh` sale con exit 0 igual) y puede dejar la ventana congelada tras relanzar (capturas idénticas con procesos nuevos son la señal); `escribir.sh` no puede seleccionar ni borrar texto. **Si el usuario necesita la PC, la verificación GUI se pausa** y se retoma cuando él diga.
- Commits: conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). Prohibido push, merge, tocar `main`, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` no se mergea a `main` hasta terminar todas las fases.
- Las referencias de línea (`L123`) son las del commit `6c94355`. Después de que una task edita un archivo, ubicar el lugar por el texto citado.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6: comparación dentro del shell real con el sidebar oculto. 10: listado v2 y `ConteoConverter` sobre `Items.Count`. 11: inactivo y negativo por token, nunca `Opacity` (las celdas nuevas conservan `Classes.inactivo`). 13: tests heredados que buscan tipos viejos se adaptan sin borrarse. 14: columnas `Auto` del mockup medidas en headless ANTES del mockup y copiadas como `flex: 0 0 Npx` (la Task 4 lo hace con el componente nuevo; esta fase reescribe la última frase de la 14, ver Task 7).

## Convenciones que agrega esta fase (para `herramientas.md`, Task 7)

18. **Estado de carga de un listado v2.** Toda carga que llena el listado pasa por `ViewModelBase.EjecutarCargaProtegidaAsync` (prende `Cargando`, marca `SinPermiso` ante un 403). La vista muestra `v2:EstadoCargando` con `Cargando && Count == 0`, el `EstadoVacio` de "no hay" SOLO con `!Cargando && !SinPermiso && Count == 0` (`MultiBinding` con `BoolConverters.And`: cada parte notifica sola, sin propiedades calculadas en el VM) y el `EstadoVacio` "Sin permiso" (`EsError`) en lugar de la card. Espejo: `.v2-cargando` en `base.css`.
19. **Columnas `Auto` de contenido libre (U2).** `Width="Auto"` + `b:ColumnaAuto.Medir="True"` (`Ruta` por defecto = `SortMemberPath`; `b:ColumnaAuto.Conversor` = el MISMO conversor de la celda; una `DataGridTextColumn` usa su `Binding`). El ancho = `ceil(max(texto más ancho de TODA la fuente + padding de celda, título + padding + 20 de la flecha))`, acotado por `MinWidth` y por el tope (240), solo crece mientras vive la grilla y cede ante un arrastre del usuario. Si lo que piden las columnas no entra, cada medida cede hasta el espacio libre (tope adaptable, D5): primero las `*` bajan a su piso, después las medidas se nivelan hacia abajo hasta su título; la grilla no scrollea por una columna medida. El `TextBlock` de la celda lleva `TextTrimming="CharacterEllipsis"`, `ToolTip.Tip` con el mismo texto y `b:TooltipSiSeCorta.Activo="True"`. Columnas con insignia (STOCK, ESTADO) y las `*` no se miden: quedan fijas o proporcionales. Un UiTest de la vista scrollea más de 1000 filas y exige que ninguna columna se mueva, y otro exige que el texto medido sea el texto pintado (paridad).
20. **Tooltip solo si se corta:** `b:TooltipSiSeCorta.Activo="True"` sobre un `TextBlock` con `TextTrimming` y `ToolTip.Tip`; cancela la apertura si ninguna línea está cortada (`TextLine.HasCollapsed`).

## Review Focus

1. **"No hay productos para mostrar" aparece mientras carga o después de un 403.** Es exactamente lo que U1 prohíbe: el usuario cree que el catálogo está vacío. Esperado: durante la carga sin filas, "Cargando productos…" (ni vacío ni grilla); tras un 403 en la carga o en una búsqueda, "Sin permiso" en lugar de la card; el vacío solo cuando la carga terminó bien y no hay filas. Tests: `Cargando_SinFilas_MuestraElIndicadorYNiElVacioNiLaGrilla`, `SinPermiso_EnLaCarga_MuestraSinPermisoEnLugarDeLaCardYNuncaElVacio`, `SinPermiso_EnUnaBusqueda_...` (Task 6) y los de VM de la Task 1.
2. **Dos cargas superpuestas (búsqueda con debounce + recarga tras Eliminar) dejan `Cargando` pegado en true o lo apagan antes de tiempo.** Esperado: el indicador sigue mientras quede alguna carga en curso y se va cuando terminan todas, también si una falla con otra excepción. Tests: `DosCargasSuperpuestas_LaPrimeraEnTerminarNoApagaElFlag`, `Cargando_SeApagaAunqueLaCargaLanceOtraExcepcion` (Task 1), `BusquedaYCargaSuperpuestas_CargandoSigueHastaQueTerminanAmbas` (Task 1).
3. **Una columna se mueve al scrollear un catálogo grande u ordenar.** Era la deuda de la 3a (CÓDIGO 65→140, COSTO 100→115, UNIDAD 63→97). Esperado: con 1200 productos y el valor más ancho en la última fila, ningún ancho cambia al scrollear hasta el final ni al ordenar por cada columna, y el título ordenado no se corta. Tests: `Scroll_MasDeMilFilas_HastaElFinal_NoMueveNingunaColumna`, `Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo` (Task 6), `ConMilFilas_...` y `Ordenar_ElTituloYLaFlechaEntran...` (Task 2).
4. **Un valor más largo que el tope o que el espacio libre rompe la grilla o se pierde.** Esperado: la columna queda en min(240, espacio libre) (Productos con la celda cortada: UNIDAD 145 a 1280 y 231 a 1366), sin scroll horizontal, la celda muestra el texto cortado con "…", el tooltip muestra el texto completo, y las celdas que entran no muestran tooltip. Tests: `TextoMasLargoQueElTope_...` (Task 2), `ColumnaAutoTopeAdaptableTests` (D5), `CeldaCortada_...` (Task 6), la comparación `ProductosCeldaCortada_IgualAlMockup` y la excepción del barrido (Task 6).
5. **El usuario arrastra una columna medida y una recarga se la vuelve a cambiar.** Esperado: después de un arrastre, ningún dato nuevo mueve esa grilla (mismo criterio que el behavior: los anchos pasan a ser del usuario). Test: `UsuarioArrastraLaColumna_UnaRecargaConDatosMasAnchosNoLaMueve` (Task 2).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit `6c94355`) |
|---|---|---|
| Dónde vive `Cargando` | En `ViewModelBase`, como contador (`_cargasEnCurso`): `EjecutarCargaProtegidaAsync` llama `EmpezarCarga()` al entrar y `TerminarCarga()` en un `finally`. Notifica solo en las transiciones 0↔1. Así cubre a los 14 VMs que ya usan el helper sin tocarlos, y dos cargas superpuestas no se pisan (la primera en terminar no apaga el flag de la otra). | `ViewModelBase.cs` L147-158 (helper actual sin `finally`), L94-125 (`SinPermiso`). |
| Choque de nombre | `MantenimientoViewModel` tiene `[ObservableProperty] private bool _cargando;` (L50-52) que genera `public bool Cargando`: escondería el de la base (CS0108) y su vista dejaría de ver la carga del helper. Se borra el campo, `CargarAsync` usa `EmpezarCarga()`/`TerminarCarga()` (L126 y el `finally` L140-143), y `MostrarListaVacia` se re-notifica en un `override OnPropertyChanged`. `MantenimientoView.axaml` L48-51 (`IsVisible="{Binding Cargando}"`) no cambia. | `MantenimientoViewModel.cs` L46-61, L124-143; `MantenimientoViewModelTests.cs` L96 y L111 (`Assert.False(vm.Cargando)`). |
| Productos no usa el helper | `ProductoListViewModel.CargarAsync` (L77-89) tiene su propio `catch (UnauthorizedAccessException) { }` vacío: nunca marca `SinPermiso`. Y `EjecutarBusquedaAsync` (L130-136) no atrapa nada: un 403 en una búsqueda escala al manejador global. Las dos pasan por `EjecutarCargaProtegidaAsync`. El debounce (L96-120) cancela solo el `Task.Delay`, antes de entrar al servicio: no hay carga "cancelada" a mitad de camino. | archivo citado; `ProductoViewModelTests.cs` L82-96 (test del 403 que sigue valiendo). |
| Condición del vacío | `MultiBinding` + `BoolConverters.And` sobre `Cargando` (con `BoolConverters.Not`), `SinPermiso` (ídem) y `Items.Count` (con `ConteoConverter.EsCero`): cada parte notifica sola; no hace falta una propiedad calculada que dependa de `Items.Count` (que no se re-notifica sola). Patrón ya usado en `InicioView.axaml` L76-80. | `ProductoListView.axaml` L48-53; `InicioView.axaml` L76-92. |
| Medición: por qué no un contenedor oculto | Una sonda invisible no mide (`Layoutable.MeasureCore` devuelve 0 con `IsVisible=false`) y los estilos de celda (`.ui2 DataGridCell TextBlock`, `.ui2 DataGridCell.num` con `+tnum,-calt`) necesitan una `DataGridCell` real como ancestro. La sonda es un `TextBlock` suelto que COPIA familia, tamaño, peso, estilo, stretch, `LetterSpacing` y `FontFeatures` del `TextBlock` de una celda realizada de esa columna (la celda se ubica por el centro horizontal de su header). Es lo mismo que ya hace `GrillasSinRecorteBarridoTests.AnchoNatural` (L150-160). El header se mide igual, copiando del `TextBlock` del header realizado. | `DataGridV2.axaml` L134-157; `GrillasSinRecorteBarridoTests.cs` L150-160. |
| Texto de cada celda | Columna de plantilla: `Ruta` (por defecto `SortMemberPath`) leída por reflexión + `Conversor` (el mismo de la celda). `DataGridTextColumn`: su `Binding` evaluado sobre un `TextBlock` evaluador con `DataContext = item` (respeta `StringFormat` y `Converter`). Un test de paridad compara texto medido contra texto pintado. Se descartó un attached tipo `{Binding}`: XAML lo trataría como binding de la propiedad (no como objeto a guardar), y en una vista con `x:DataType` del VM compilaría contra el VM. | `ProductoListView.axaml` L69-123 (las tres columnas `Auto` tienen `SortMemberPath`); `MonedaConverter.cs`. |
| Quién dispara la medición | `DataGridColumnasAutoBehavior` (aplicado a TODA `DataGrid` en `Themes/DataGrid.axaml` L115): observa `ItemsSource` y su `CollectionChanged` (para `DataGridCollectionView`, el del view, que re-emite los cambios de la fuente), marca `MedicionPendiente` y mide en su `OnLayoutUpdated`, ANTES del reparto de las `*`, con su guarda `Reparando`. Comparte `UsuarioRedimensiono` y el gesto en curso (L293), así que cede ante el arrastre sin exponer estado privado. Mide sobre `SourceCollection` (todas las filas, sin filtro). El `Clear` + N `Add` de una carga se coalesce solo: el flag queda en true y se mide una vez en la pasada de layout siguiente. Sin filas realizadas (grilla oculta o vacía) no toca nada y reintenta en la pasada siguiente. | `DataGridColumnasAutoBehavior.cs` L111-120, L289-364. |
| Cómo se asigna el ancho | Por el `Asignar` del behavior (L372-377: ida y vuelta por `Star` para no pasar por `DataGridColumn.Resize`, que le saca ancho a las columnas de la derecha). Con `Width` en píxeles el behavior la trata como fija (`Objetivo` = `Width.Value`, L201-202), igual que STOCK (220) y ESTADO (78). | archivo citado. |
| Header y flecha de orden | El header v2 tiene padding `12,0,4,0` y la flecha es un `Path` de alto 12 con margen izquierdo 8, visible solo con la columna ordenada (`DataGridV2.axaml` L56, L84-92). La reserva fija 20 (D6). | archivo citado. |
| Tooltip solo si se corta | `ToolTip.AddToolTipOpeningHandler` (evento cancelable de Avalonia 12.0.5) + `TextBlock.TextLayout.TextLines.Any(l => l.HasCollapsed)`; se cancela si no hay corte. No hace falta escuchar cambios de texto ni de tamaño (las celdas se reciclan): se decide al abrir. | `Avalonia.Controls.xml` (`ToolTip.ToolTipOpeningEvent`), `Avalonia.Base.xml` (`TextLine.HasCollapsed`). |
| Barrido de recortes vs U2 | `GrillasSinRecorteBarridoTests` exige que ningún texto de una columna no proporcional quede más angosto que su ancho natural; con U2 un valor más largo que el tope o que el espacio libre (D5) SE CORTA a propósito. El barrido gana una excepción acotada (columna medida, más angosta que lo que midió, `TextTrimming`, `TooltipSiSeCorta` y `ToolTip.Tip` igual al texto) y un escenario que la ejercita. El barrido asigna `ItemsSource` directo (L136-144): el componente tiene que reaccionar a un `ItemsSource` nuevo, no solo al de la VM. | `GrillasSinRecorteBarridoTests.cs` L44, L136-208, L229-233; `GrillasEspacioDerechoBarridoTests.cs` L52. |
| COSTO con `MinWidth="100"` | Se conserva (fix 3548397): el ancho medido nunca baja del `MinWidth` de la columna. | `ProductoListView.axaml` L87. |
| Anchos actuales del mockup | `productos.html` L19-25: CÓDIGO 81, COSTO 100, STOCK 220, UNIDAD 97, CATEGORÍA 160, ESTADO 78. Con D6, CÓDIGO cambia (su título + 36 supera a "MAT-0142" + 16). Se re-mide en la Task 4 Step 1. | archivo citado. |
| Test del vacío heredado | `ListaVacia_MuestraEstadoVacio_YAlLlegarDatosVuelveLaGrilla` (L258) sigue valiendo (su fake responde sincrónico: la carga terminó cuando se mira). Gana un assert de que el indicador no está. | `ProductoListViewV2Tests.cs` L258-273. |
| Guardián de carga protegida | `CargaProtegidaSinPermisoGuardianTests` descubre los puntos de carga en los `.axaml.cs` y exige que atrapen el 403; `CargarAsync` vía el helper sigue protegido (el helper cuenta como protección; ver su `ProtegeContraSinPermiso`). | `CargaProtegidaSinPermisoGuardianTests.cs` L193-240. |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Behaviors/ColumnaAuto.cs` | Attached de columna (`Medir`, `Ruta`, `Conversor`, `Tope`), texto de una celda y medición del ancho. |
| `src/StockApp.Presentation/Behaviors/TooltipSiSeCorta.cs` | Cancela el tooltip de un `TextBlock` que no está cortado. |
| `src/StockApp.Presentation/Controls/V2/EstadoCargando.cs` | Indicador de carga v2 (D1). |
| `tests/StockApp.Presentation.UiTests/V2/ColumnaAutoTests.cs` | El componente sobre una grilla real con el tema v2 (sin vista de la app). |
| `tests/StockApp.Presentation.UiTests/V2/TooltipSiSeCortaTests.cs` | Tooltip cancelado o no según el corte. |
| `tests/StockApp.Presentation.UiTests/V2/EstadoCargandoTests.cs` | Tipografía, color, centrado y redondeo del indicador. |
| `docs/ui-v2/mockups/inventario/productos-cargando.html`, `productos-sin-permiso.html`, `productos-celda-cortada.html` | Mockups (D9). |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/ViewModelBase.cs` | `Cargando`, `EmpezarCarga`, `TerminarCarga`; `finally` en `EjecutarCargaProtegidaAsync`. |
| `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs` | Usa el `Cargando` de la base. |
| `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` | Carga y búsqueda por el helper; `MensajeSinPermisoListado`. |
| `src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs` | Observa la fuente y llama a `ColumnaAuto` antes del reparto. |
| `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` | `ControlTheme` de `EstadoCargando`. |
| `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` | Indicador, condición del vacío, sin permiso, columnas medidas, tooltips. |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-cargando`, `.v2-celda-cortada`. |
| `docs/ui-v2/mockups/inventario/productos.html` | Anchos re-medidos (D6). |
| `tests/StockApp.Presentation.Tests/ViewModels/ViewModelBaseCargaProtegidaTests.cs`, `.../Catalogo/ProductoViewModelTests.cs` | Tests de `Cargando` y de la búsqueda con 403. |
| `tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs` | Estados de carga y sin permiso, scroll de 1200 filas, orden, paridad, tooltip. |
| `tests/StockApp.Presentation.UiTests/GrillasSinRecorteBarridoTests.cs` | `SidebarPx` 240 (R1), excepción del corte en el tope y su escenario. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs`, `DatosProductosTests.cs`, `ProductosComparacionTests.cs` | Estados nuevos y sus comparaciones. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | §3 excepción (Task 1); Estado, §7 y deuda (Task 7). |
| `docs/ui-v2/herramientas.md` | Convenciones 18 a 20 y la 14 reescrita (Task 7). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de grillas (correr entera tras las Tasks 2 y 6): `--filter "FullyQualifiedName~DataGridColumnasAutoAjusteTests|FullyQualifiedName~DataGridSortClickTests|FullyQualifiedName~DataGridCeldaNumericaOrdenTests|FullyQualifiedName~DataGridV2|FullyQualifiedName~GrillasSinRecorteBarridoTests|FullyQualifiedName~GrillasEspacioDerechoBarridoTests|FullyQualifiedName~DataGridColumnasResizeDeshabilitadoGuardTests|FullyQualifiedName~ProductoListView"`
- Comparación con página: `scripts/ui-v2/comparar.sh Productos` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-08-ui-v2-fase-3c1-patrones-comunes/progress.md` (crearlo en la Task 1; `.superpowers/sdd/` está en `.gitignore`).

---

## Bloque A — Piezas comunes, testeadas sin tocar vistas

### Task 1: `Cargando` en `ViewModelBase` y Productos por la carga protegida

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ViewModelBase.cs` (L94-158)
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs` (L46-61, L124-143)
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` (L77-89, L130-136)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/ViewModelBaseCargaProtegidaTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs` (clase `ProductoListViewModelTests`)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3, la excepción U1)
- Create (local): `.superpowers/sdd/2026-10-08-ui-v2-fase-3c1-patrones-comunes/progress.md`

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string)`, `SinPermiso`, `MensajeSinPermiso`.
- Produces: `public bool ViewModelBase.Cargando { get; }` (notifica `PropertyChanged` solo en las transiciones), `protected void EmpezarCarga()`, `protected void TerminarCarga()`; `public const string ProductoListViewModel.MensajeSinPermisoListado = "No tenés permiso para ver los productos."`.

- [ ] **Step 1: Ledger y tests del contador (rojo)**

Crear el ledger con: base `6c94355`, fecha, y una línea por task a medida que cierran.

Agregar a `ViewModelBaseCargaProtegidaTests` (al final de la clase; `using System.Collections.Generic;` arriba):

```csharp
    [Fact]
    public async Task Cargando_EsTrueMientrasCorreLaCarga_YFalseAlTerminar()
    {
        var vm = new VmDePrueba();
        var pendiente = new TaskCompletionSource();

        var carga = vm.EjecutarAsync(() => pendiente.Task, "x");
        Assert.True(vm.Cargando);

        pendiente.SetResult();
        await carga;
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task DosCargasSuperpuestas_LaPrimeraEnTerminarNoApagaElFlag()
    {
        var vm = new VmDePrueba();
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        var cargaA = vm.EjecutarAsync(() => a.Task, "x");
        var cargaB = vm.EjecutarAsync(() => b.Task, "x");
        b.SetResult();
        await cargaB;
        Assert.True(vm.Cargando);

        a.SetResult();
        await cargaA;
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Cargando_SeApagaAunqueLaCargaLanceOtraExcepcion()
    {
        var vm = new VmDePrueba();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.EjecutarAsync(() => throw new InvalidOperationException("bug"), "x"));

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Cargando_SeApagaTrasUn403_YQuedaSinPermiso()
    {
        var vm = new VmDePrueba();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso.");

        Assert.False(vm.Cargando);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task Cargando_NotificaSoloEnLasTransiciones()
    {
        var vm = new VmDePrueba();
        var cambios = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ViewModelBase.Cargando)) cambios.Add(vm.Cargando);
        };
        var a = new TaskCompletionSource();
        var b = new TaskCompletionSource();

        var cargaA = vm.EjecutarAsync(() => a.Task, "x");
        var cargaB = vm.EjecutarAsync(() => b.Task, "x");
        a.SetResult();
        b.SetResult();
        await Task.WhenAll(cargaA, cargaB);

        Assert.Equal(new[] { true, false }, cambios);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ViewModelBaseCargaProtegidaTests"`
Expected: FAIL de compilación (`'ViewModelBase' does not contain a definition for 'Cargando'`).

- [ ] **Step 2: `ViewModelBase`**

En `ViewModelBase.cs`, después de `private string? _mensajeSinPermiso;` (L18): `private int _cargasEnCurso;`. Después de `MensajeSinPermiso` (L101-105):

```csharp
    /// <summary>
    /// True mientras corre al menos una carga (las de <see cref="EjecutarCargaProtegidaAsync"/> y las que un
    /// ViewModel marque con <see cref="EmpezarCarga"/>/<see cref="TerminarCarga"/>). Es un contador y no un
    /// bool: una búsqueda con debounce y una recarga pueden superponerse, y la primera en terminar no puede
    /// apagar el estado de la otra. La vista lo usa para decir "Cargando…" en vez de afirmar "no hay datos"
    /// antes de tenerlos. Notifica solo cuando cambia de valor.
    /// </summary>
    public bool Cargando => _cargasEnCurso > 0;

    /// <summary>Marca el inicio de una carga (ver <see cref="Cargando"/>). Cada llamada pide su <see cref="TerminarCarga"/>.</summary>
    protected void EmpezarCarga()
    {
        if (++_cargasEnCurso == 1)
            OnPropertyChanged(nameof(Cargando));
    }

    /// <summary>Marca el fin de una carga empezada con <see cref="EmpezarCarga"/>; va en un finally.</summary>
    protected void TerminarCarga()
    {
        if (_cargasEnCurso > 0 && --_cargasEnCurso == 0)
            OnPropertyChanged(nameof(Cargando));
    }
```

Y `EjecutarCargaProtegidaAsync` (L147-158) queda:

```csharp
    protected async Task EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso)
    {
        EmpezarCarga();
        LimpiarSinPermiso();
        try
        {
            await cargar();
        }
        catch (UnauthorizedAccessException)
        {
            MarcarSinPermiso(mensajeSinPermiso);
        }
        finally
        {
            TerminarCarga();
        }
    }
```

Agregar al final del `<summary>` del helper (antes de `</summary>`): "También prende <see cref=\"Cargando\"/> mientras corre (se apaga en el finally, falle o no)."

- [ ] **Step 3: `MantenimientoViewModel` usa el `Cargando` de la base**

En `MantenimientoViewModel.cs`:
1. Borrar el comentario L46-49 y el campo L50-52 (`[ObservableProperty] [NotifyPropertyChangedFor(nameof(MostrarListaVacia))] private bool _cargando;`).
2. En `CargarAsync`: `Cargando = true;` (L126) → `EmpezarCarga();`; en el `finally` (L140-143) `Cargando = false;` → `TerminarCarga();`.
3. Agregar (con `using System.ComponentModel;` si falta):

```csharp
    /// <summary>
    /// <see cref="MostrarListaVacia"/> depende de <see cref="ViewModelBase.Cargando"/>, que vive en la base:
    /// se re-notifica acá. Solo se recalcula al cambiar Cargando (no con Corridas.CollectionChanged):
    /// CargarAsync llena Corridas de forma síncrona ANTES del finally que termina la carga, así que cuando el
    /// binding se re-evalúa el conteo ya es el definitivo.
    /// </summary>
    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(Cargando))
            OnPropertyChanged(nameof(MostrarListaVacia));
    }
```

Run: `dotnet build StockApp.sln` (0 errores; sin CS0108 nuevo: `dotnet build StockApp.sln 2>&1 | grep -c CS0108` debe dar `0`).
Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ViewModelBaseCargaProtegidaTests|FullyQualifiedName~MantenimientoViewModelTests"`
Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MantenimientoView|FullyQualifiedName~AccesoLimitadoView"`
Expected: todo PASS.

- [ ] **Step 4: Tests de Productos (rojo)**

En `ProductoViewModelTests.cs`, clase `ProductoListViewModelTests`, agregar:

```csharp
    [Fact]
    public async Task CargarAsync_MientrasEsperaAlServicio_CargandoEsTrue_YAlTerminarFalse()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarAsync(null, null, null)).Returns(pendiente.Task);
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);
        Assert.Empty(vm.Items);

        pendiente.SetResult(new List<ProductoDto> { CrearProductoDto(1, "P001", "Uno") });
        await carga;
        Assert.False(vm.Cargando);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_MarcaSinPermisoConElMensajeDelListado()
    {
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(ProductoListViewModel.MensajeSinPermisoListado, vm.MensajeSinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Buscar_SiElServicioLanzaUnauthorized_NoPropagaYMarcaSinPermiso()
    {
        var (vm, svcMock, _, _) = Crear();
        svcMock.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>())).ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.BuscarCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Buscar_ExitosaDespuesDeUn403_LimpiaSinPermiso()
    {
        var (vm, svcMock, _, _) = Crear();
        svcMock.SetupSequence(s => s.BuscarPorTextoAsync(It.IsAny<string?>()))
            .ThrowsAsync(new UnauthorizedAccessException())
            .ReturnsAsync(new List<ProductoDto> { CrearProductoDto(1, "P001", "Uno") });

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.False(vm.SinPermiso);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task BusquedaYCargaSuperpuestas_CargandoSigueHastaQueTerminanAmbas()
    {
        var carga = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var busqueda = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarAsync(null, null, null)).Returns(carga.Task);
        svc.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>())).Returns(busqueda.Task);
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());

        var t1 = vm.CargarAsync();
        var t2 = vm.BuscarCommand.ExecuteAsync(null);
        busqueda.SetResult(new List<ProductoDto>());
        await t2;
        Assert.True(vm.Cargando);

        carga.SetResult(new List<ProductoDto>());
        await t1;
        Assert.False(vm.Cargando);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ProductoListViewModelTests"`
Expected: FAIL de compilación (`MensajeSinPermisoListado` no existe).

- [ ] **Step 5: `ProductoListViewModel`**

Agregar arriba de la clase (después de `DebounceMs`):

```csharp
    /// <summary>Texto del estado "Sin permiso" del listado cuando el servidor rechaza la carga o la búsqueda (403).</summary>
    public const string MensajeSinPermisoListado = "No tenés permiso para ver los productos.";
```

Reemplazar `CargarAsync` (L68-89, con su `<summary>`):

```csharp
    /// <summary>
    /// La dispara la View (DataContextChanged) fire-and-forget. Va por EjecutarCargaProtegidaAsync: un 403
    /// (UnauthorizedAccessException) deja SinPermiso en true en vez de escapar al manejador global, y mientras
    /// espera al servicio Cargando está en true, así la vista no afirma "no hay productos" antes de tenerlos.
    /// El aviso global del 403 ya lo da AuthTokenHandler + App.axaml.cs: acá no se agrega otro.
    /// </summary>
    public async Task CargarAsync() =>
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.BuscarAsync(null, null, null);
            Items.Clear();
            foreach (var p in resultados)
                Items.Add(p);
        }, MensajeSinPermisoListado);
```

Y `EjecutarBusquedaAsync` (L126-136):

```csharp
    /// <summary>
    /// Búsqueda por término único: matchea por Nombre, SKU o código de barras (lógica OR). Reusada por el
    /// comando del botón y por el debounce automático. Mismo camino protegido que la carga: un 403 en una
    /// búsqueda marca SinPermiso y no escapa; Items no se vacía hasta tener los resultados.
    /// </summary>
    private async Task EjecutarBusquedaAsync() =>
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.BuscarPorTextoAsync(FiltroBusqueda);
            Items.Clear();
            foreach (var p in resultados)
                Items.Add(p);
        }, MensajeSinPermisoListado);
```

Run: `dotnet build StockApp.sln`
Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CargaProtegida|FullyQualifiedName~ProductoListView|FullyQualifiedName~ProductoFormView"`
Expected: todo PASS (incluido `CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaLaExcepcion` y `CargaProtegidaSinPermisoGuardianTests`).

- [ ] **Step 6: Mutaciones (QUITANDO)**

Con el procedimiento de Global Constraints:
- (a) En `ViewModelBase.cs`, quitar la línea `TerminarCarga();` del `finally` → rojo en `Cargando_EsTrueMientrasCorreLaCarga_YFalseAlTerminar` y `Cargando_SeApagaAunqueLaCargaLanceOtraExcepcion`. Pegar el rojo. Restaurar.
- (b) En `ProductoListViewModel.EjecutarBusquedaAsync`, quitar el envoltorio (dejar solo el cuerpo con `await _service.BuscarPorTextoAsync`) → rojo en `Buscar_SiElServicioLanzaUnauthorized_NoPropagaYMarcaSinPermiso`. Restaurar.

- [ ] **Step 7: Excepción U1 en la spec §3**

En `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, §3, bullet "Se reescribe el XAML de cada vista…", agregar al final de "Los ViewModels quedan intactos salvo lo mínimo: …": "; y el estado de carga (decisión del usuario 2026-10-08, Fase 3c-1): `ViewModelBase` expone `Cargando` (contador de cargas en curso, prendido por `EjecutarCargaProtegidaAsync`) y cada listado que se migra pasa su carga por ese helper, para no mostrar el estado vacío durante la carga ni tras un 403".

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/ViewModelBase.cs src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/ViewModelBaseCargaProtegidaTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "feat(ui-v2): estado de carga en ViewModelBase y Productos por la carga protegida"
```

### Task 2: `ColumnaAuto`: ancho medido sobre toda la fuente, con tope

**Files:**
- Create: `src/StockApp.Presentation/Behaviors/ColumnaAuto.cs`
- Modify: `src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs` (L111-149, L289-296)
- Create: `tests/StockApp.Presentation.UiTests/V2/ColumnaAutoTests.cs`

**Interfaces:**
- Consumes: `DataGridColumnasAutoBehavior` (estado privado `Estado`, `Asignar`, `Tolerancia`); `MonedaConverter.Instance`; `ProductoDto`.
- Produces: `ColumnaAuto.MedirProperty` (`bool`, attached de `DataGridColumn`), `RutaProperty` (`string?`), `ConversorProperty` (`IValueConverter?`), `TopeProperty` (`double`, default `TopePorDefecto`), `Get/Set` de cada una; `public const double TopePorDefecto = 240`; `public const double ReservaOrden = 20`; `internal static string Texto(DataGridColumn columna, object item)`; `internal static IReadOnlyList<object> Items(IEnumerable? fuente)`; `internal static double? Medir(DataGrid grid, DataGridColumn columna, IReadOnlyList<object> items)`.

- [ ] **Step 1: Tests del componente (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ColumnaAutoTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Columnas Auto de contenido libre (U2 de la Fase 3c-1) sobre una DataGrid REAL con el tema v2: el ancho sale
/// del texto formateado de TODAS las filas (también las que la virtualización no realizó), con un tope,
/// elipsis y la reserva de la flecha de orden. Se mide el efecto (ActualWidth, texto pintado), no la configuración.
/// </summary>
public class ColumnaAutoTests
{
    private static ProductoDto P(int id, string codigo, decimal costo = 100m, string unidad = "Unidad") => new(
        id, codigo, null, $"Producto {id}", null, 1, "Construcción", null, 1, unidad, costo, 1m, 0m, true,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, DataGrid Grilla, DataGridTemplateColumn Codigo, DataGridTemplateColumn Costo,
        DataGridTextColumn Unidad, DataGridTemplateColumn SinMedir);

    private static DataGridTemplateColumn ColumnaDeTexto(string header, string ruta, IValueConverter? conversor = null) => new()
    {
        Header = header,
        Width = DataGridLength.Auto,
        SortMemberPath = ruta,
        CellTemplate = new FuncDataTemplate<ProductoDto>((_, _) => new TextBlock
        {
            [!TextBlock.TextProperty] = new Binding(ruta) { Converter = conversor },
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        }),
    };

    /// <summary>Grilla de 300 de alto (virtualiza: ~6 filas realizadas) dentro de un scope .ui2, con una columna * como Productos.</summary>
    private static Montaje Montar(IEnumerable<ProductoDto> items, double ancho = 1000)
    {
        var codigo = ColumnaDeTexto("CÓDIGO", "Codigo");
        ColumnaAuto.SetMedir(codigo, true);
        var costo = ColumnaDeTexto("COSTO", "PrecioCosto", MonedaConverter.Instance);
        costo.CellStyleClasses.Add("num");
        ColumnaAuto.SetMedir(costo, true);
        ColumnaAuto.SetConversor(costo, MonedaConverter.Instance);
        var unidad = new DataGridTextColumn { Header = "UNIDAD", Width = DataGridLength.Auto, Binding = new Binding("UnidadMedidaNombre") };
        ColumnaAuto.SetMedir(unidad, true);
        var sinMedir = ColumnaDeTexto("CATEGORÍA", "CategoriaNombre");
        var nombre = ColumnaDeTexto("NOMBRE", "Nombre");
        nombre.Width = new DataGridLength(1, DataGridLengthUnitType.Star);

        var grilla = new DataGrid { Height = 300, IsReadOnly = true, CanUserSortColumns = true, CanUserResizeColumns = true };
        foreach (var c in new DataGridColumn[] { codigo, nombre, costo, unidad, sinMedir })
            grilla.Columns.Add(c);
        grilla.ItemsSource = new DataGridCollectionView(new ObservableCollection<ProductoDto>(items));

        var ventana = new Window { Width = ancho, Height = 400, Content = new UserControl { Classes = { "ui2" }, Content = grilla } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, grilla, codigo, costo, unidad, sinMedir);
    }

    private static ObservableCollection<ProductoDto> Fuente(Montaje m) =>
        (ObservableCollection<ProductoDto>)((DataGridCollectionView)m.Grilla.ItemsSource!).SourceCollection;

    private static void Recargar(Montaje m, IEnumerable<ProductoDto> items)
    {
        var fuente = Fuente(m);
        fuente.Clear();
        foreach (var p in items) fuente.Add(p);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Ancho natural de un TextBlock con su misma tipografía (igual que GrillasSinRecorteBarridoTests).</summary>
    private static double AnchoNatural(TextBlock tb)
    {
        var gemelo = new TextBlock
        {
            Text = tb.Text, FontSize = tb.FontSize, FontWeight = tb.FontWeight, FontStyle = tb.FontStyle,
            FontFamily = tb.FontFamily, LetterSpacing = tb.LetterSpacing, FontFeatures = tb.FontFeatures,
        };
        gemelo.Measure(Size.Infinity);
        return gemelo.DesiredSize.Width;
    }

    private static TextBlock TextoDeCelda(DataGrid grilla, object item, string texto) =>
        grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item))
            .GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == texto);

    private static DataGridColumnHeader Header(DataGrid grilla, string titulo) =>
        grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(h => Equals(h.Content, titulo));

    private static double[] Anchos(DataGrid grilla) => grilla.Columns.Select(c => c.ActualWidth).ToArray();

    private static List<ProductoDto> MilCortosYUnoLargoAlFinal(string codigoLargo)
    {
        var items = Enumerable.Range(1, 999).Select(i => P(i, $"C-{i:0000}")).ToList();
        items.Add(P(1000, codigoLargo, 98765432.10m, "Metro cúbico"));
        return items;
    }

    [AvaloniaFact]
    public void ConMilFilas_ElAnchoEsElDelTextoMasLargoAunqueNoEsteRealizado_YScrollearNoLoMueve()
    {
        // "2x1,5" adentro: la sonda tiene que medir sin calt, como la celda.
        var items = MilCortosYUnoLargoAlFinal("MAT-2x1,5-CONSTRUCCION");
        var m = Montar(items);
        Assert.DoesNotContain(m.Grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, items[^1]));
        var antes = Anchos(m.Grilla);

        m.Grilla.ScrollIntoView(items[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var celda = TextoDeCelda(m.Grilla, items[^1], "MAT-2x1,5-CONSTRUCCION");
        Assert.Equal(Math.Ceiling(AnchoNatural(celda) + 16), m.Codigo.ActualWidth, 1);
        var costo = TextoDeCelda(m.Grilla, items[^1], "$ 98.765.432,10");
        Assert.Equal(Math.Ceiling(AnchoNatural(costo) + 16), m.Costo.ActualWidth, 1);
        Assert.Equal(antes, Anchos(m.Grilla));
    }

    [AvaloniaFact]
    public void ColumnaDeTexto_SeMideConSuBinding()
    {
        var items = Enumerable.Range(1, 200).Select(i => P(i, $"C-{i}", unidad: i == 200 ? "Kilogramo por metro lineal" : "Par")).ToList();
        var m = Montar(items);

        Assert.Equal("Kilogramo por metro lineal", ColumnaAuto.Texto(m.Unidad, items[^1]));
        m.Grilla.ScrollIntoView(items[^1], null);
        Dispatcher.UIThread.RunJobs();
        var celda = TextoDeCelda(m.Grilla, items[^1], "Kilogramo por metro lineal");
        Assert.Equal(Math.Ceiling(AnchoNatural(celda) + 16), m.Unidad.ActualWidth, 1);
    }

    [AvaloniaFact]
    public void Paridad_ElTextoMedidoEsElTextoPintado()
    {
        var items = Enumerable.Range(1, 5).Select(i => P(i, $"MAT-{i:0000}", 1234.5m * i)).ToList();
        var m = Montar(items);

        foreach (var p in items)
        {
            TextoDeCelda(m.Grilla, p, ColumnaAuto.Texto(m.Codigo, p));
            TextoDeCelda(m.Grilla, p, ColumnaAuto.Texto(m.Costo, p));
            TextoDeCelda(m.Grilla, p, ColumnaAuto.Texto(m.Unidad, p));
        }
    }

    [AvaloniaFact]
    public void TextoMasLargoQueElTope_LaColumnaQuedaEnElTopeYLaCeldaSeCortaConElipsis()
    {
        var largo = "CODIGO-INTERNO-DE-PROVEEDOR-EXTRANJERO-0000000001";
        var m = Montar([P(1, largo), P(2, "C-2")]);

        Assert.Equal(ColumnaAuto.TopePorDefecto, m.Codigo.ActualWidth, 1);
        var celda = TextoDeCelda(m.Grilla, Fuente(m)[0], largo);
        Assert.Contains(celda.TextLayout.TextLines, l => l.HasCollapsed);
    }

    [AvaloniaFact]
    public void Ordenar_ElTituloYLaFlechaEntranEnElHeader_YNoSeMuevenAnchos()
    {
        // Contenido más angosto que el título: el ancho lo decide el header con la reserva de la flecha.
        var m = Montar(Enumerable.Range(1, 30).Select(i => P(i, $"C{i}")));
        var antes = Anchos(m.Grilla);

        var header = Header(m.Grilla, "CÓDIGO");
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var flecha = header.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single(p => p.Name == "SortIcon");
        Assert.True(flecha.IsEffectivelyVisible, "precondición: la columna quedó ordenada");
        var bordeFlecha = flecha.TranslatePoint(new Point(flecha.Bounds.Width, 0), header)!.Value.X;
        Assert.True(bordeFlecha <= header.Bounds.Width + 0.5, $"La flecha termina en {bordeFlecha:F1} y el header mide {header.Bounds.Width:F1}.");
        var titulo = header.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "CÓDIGO");
        Assert.True(titulo.Bounds.Width >= AnchoNatural(titulo) - 0.5, "El título ordenado quedó cortado.");
        Assert.Equal(antes, Anchos(m.Grilla));
    }

    [AvaloniaFact]
    public void Recarga_ConDatosMasAngostosNoAchica_YConMasAnchosAgranda()
    {
        var m = Montar([P(1, "MAT-CONSTRUCCION-0001")]);
        var ancho = m.Codigo.ActualWidth;

        Recargar(m, [P(2, "C-2")]);
        Assert.Equal(ancho, m.Codigo.ActualWidth, 1);

        Recargar(m, [P(3, "MAT-CONSTRUCCION-REFORZADA-0003")]);
        Assert.True(m.Codigo.ActualWidth > ancho + 10, $"No creció: {ancho:F1} -> {m.Codigo.ActualWidth:F1}");
    }

    [AvaloniaFact]
    public void UsuarioArrastraLaColumna_UnaRecargaConDatosMasAnchosNoLaMueve()
    {
        var m = Montar(Enumerable.Range(1, 10).Select(i => P(i, $"MAT-{i:0000}")));
        var header = Header(m.Grilla, "CÓDIGO");
        var borde = header.TranslatePoint(new Point(header.Bounds.Width - 2, header.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseMove(borde);
        m.Ventana.MouseDown(borde, MouseButton.Left);
        for (var paso = 1; paso <= 5; paso++)
        {
            m.Ventana.MouseMove(borde + new Point(40 * paso / 5.0, 0), RawInputModifiers.LeftMouseButton);
            Dispatcher.UIThread.RunJobs();
        }
        m.Ventana.MouseUp(borde + new Point(40, 0), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var arrastrado = m.Codigo.ActualWidth;

        Recargar(m, [P(99, "MAT-CONSTRUCCION-REFORZADA-MUY-LARGA-0099")]);

        Assert.Equal(arrastrado, m.Codigo.ActualWidth, 1);
    }

    [AvaloniaFact]
    public void GrillaVacia_NoTocaLasColumnas()
    {
        var m = Montar([]);

        Assert.True(m.Codigo.Width.IsAuto);
        Assert.True(m.Costo.Width.IsAuto);
    }

    [AvaloniaFact]
    public void ColumnaSinMedir_SigueSiendoAuto()
    {
        var m = Montar(Enumerable.Range(1, 10).Select(i => P(i, $"C-{i}")));

        Assert.True(m.SinMedir.Width.IsAuto);
        Assert.True(m.Codigo.Width.IsAbsolute);
    }

    [AvaloniaFact]
    public void MinWidthDeLaColumna_GanaSobreElContenido()
    {
        var m = Montar([P(1, "C")]);
        m.Costo.MinWidth = 150;

        Recargar(m, [P(2, "C2", 1m)]);

        Assert.True(m.Costo.ActualWidth >= 150 - 0.5, $"COSTO mide {m.Costo.ActualWidth:F1}");
    }

    [AvaloniaFact]
    public void CincoMilFilas_MedirNoAgregaCostoPerceptible()
    {
        static long Cargar(bool medir, List<ProductoDto> items)
        {
            var m = Montar([]);
            foreach (var c in m.Grilla.Columns) ColumnaAuto.SetMedir(c, medir && ColumnaAuto.GetMedir(c));
            var sw = Stopwatch.StartNew();
            Recargar(m, items);
            sw.Stop();
            m.Ventana.Close();
            return sw.ElapsedMilliseconds;
        }

        var items = Enumerable.Range(1, 5000).Select(i => P(i, $"MAT-{i:00000}", 1000m + i, i % 7 == 0 ? "Metro cúbico" : "Unidad")).ToList();
        Cargar(true, items); // calentamiento (JIT)
        var sin = Cargar(false, items);
        var con = Cargar(true, items);

        Assert.True(con <= sin * 1.5 + 400, $"5000 filas: con medición {con} ms vs sin {sin} ms");
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ColumnaAutoTests"`
Expected: FAIL de compilación (`ColumnaAuto` no existe).

- [ ] **Step 2: `ColumnaAuto`**

`src/StockApp.Presentation/Behaviors/ColumnaAuto.cs`:

```csharp
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data.Converters;
using Avalonia.VisualTree;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Columna Auto de contenido libre que mide su ancho sobre TODAS las filas de la fuente, no solo las que la
/// virtualización realizó. Sin esto una Auto crece cuando aparece al scrollear una fila más ancha y mueve a las
/// demás (en Productos: CÓDIGO 65 a 140, COSTO 100 a 115, UNIDAD 63 a 97).
///
/// <para>Ancho = el mayor entre el texto más ancho de la fuente más el padding de la celda, y el título más el
/// padding del header más <see cref="ReservaOrden"/> (así ordenar no corta el título); redondeado hacia arriba,
/// nunca menor que el MinWidth de la columna ni mayor que <see cref="TopeProperty"/>. Lo que pasa el tope se
/// corta con elipsis (TextTrimming en la celda) y se lee en el tooltip (<see cref="TooltipSiSeCorta"/>).</para>
///
/// <para>El texto de cada fila es el mismo que pinta la celda: una DataGridTextColumn usa su Binding; una de
/// plantilla, <see cref="RutaProperty"/> (por defecto su SortMemberPath) con <see cref="ConversorProperty"/>,
/// que tiene que ser el mismo conversor de la plantilla. La tipografía se copia del TextBlock de una celda
/// realizada de esa columna (incluye los FontFeatures: -calt, y +tnum en las numéricas). Lo dispara
/// <see cref="DataGridColumnasAutoBehavior"/> al cambiar los datos, que además cede ante un arrastre del usuario.</para>
/// </summary>
public static class ColumnaAuto
{
    /// <summary>Tope por defecto de una columna medida: más que esto se corta con elipsis.</summary>
    public const double TopePorDefecto = 240;

    /// <summary>Lugar para la flecha de orden del header v2: margen 8 + glifo 12 (Themes/V2/DataGridV2.axaml).</summary>
    public const double ReservaOrden = 20;

    public static readonly AttachedProperty<bool> MedirProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, bool>("Medir");

    public static readonly AttachedProperty<string?> RutaProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, string?>("Ruta");

    public static readonly AttachedProperty<IValueConverter?> ConversorProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, IValueConverter?>("Conversor");

    public static readonly AttachedProperty<double> TopeProperty =
        AvaloniaProperty.RegisterAttached<DataGridColumn, DataGridColumn, double>("Tope", TopePorDefecto);

    public static bool GetMedir(DataGridColumn c) => c.GetValue(MedirProperty);
    public static void SetMedir(DataGridColumn c, bool v) => c.SetValue(MedirProperty, v);
    public static string? GetRuta(DataGridColumn c) => c.GetValue(RutaProperty);
    public static void SetRuta(DataGridColumn c, string? v) => c.SetValue(RutaProperty, v);
    public static IValueConverter? GetConversor(DataGridColumn c) => c.GetValue(ConversorProperty);
    public static void SetConversor(DataGridColumn c, IValueConverter? v) => c.SetValue(ConversorProperty, v);
    public static double GetTope(DataGridColumn c) => c.GetValue(TopeProperty);
    public static void SetTope(DataGridColumn c, double v) => c.SetValue(TopeProperty, v);

    /// <summary>Todas las filas de la fuente, sin el filtro de un DataGridCollectionView.</summary>
    internal static IReadOnlyList<object> Items(IEnumerable? fuente)
    {
        var todas = fuente is DataGridCollectionView vista ? vista.SourceCollection : fuente;
        return todas is null ? [] : todas.Cast<object?>().Where(o => o is not null).Select(o => o!).ToList();
    }

    /// <summary>El texto que pinta la celda de <paramref name="item"/> en esa columna.</summary>
    internal static string Texto(DataGridColumn columna, object item) => Evaluador(columna)(item);

    private static Func<object, string> Evaluador(DataGridColumn columna)
    {
        if (GetRuta(columna) is null && columna is DataGridBoundColumn { Binding: { } binding })
        {
            var evaluador = new TextBlock();
            evaluador.Bind(TextBlock.TextProperty, binding);
            return item =>
            {
                evaluador.DataContext = item;
                return evaluador.Text ?? string.Empty;
            };
        }

        var ruta = GetRuta(columna) ?? columna.SortMemberPath;
        if (string.IsNullOrEmpty(ruta))
            throw new InvalidOperationException(
                $"La columna '{columna.Header}' mide su contenido pero no tiene Ruta, SortMemberPath ni Binding.");
        var partes = ruta.Split('.');
        var conversor = GetConversor(columna);
        var cache = new Dictionary<(Type, string), PropertyInfo?>();
        return item =>
        {
            object? valor = item;
            foreach (var parte in partes)
            {
                if (valor is null) break;
                var clave = (valor.GetType(), parte);
                if (!cache.TryGetValue(clave, out var propiedad))
                    cache[clave] = propiedad = valor.GetType().GetProperty(parte);
                valor = propiedad?.GetValue(valor);
            }
            var texto = conversor is null ? valor : conversor.Convert(valor, typeof(string), null, CultureInfo.CurrentCulture);
            return texto?.ToString() ?? string.Empty;
        };
    }

    /// <summary>
    /// Ancho de la columna para estas filas, o null si todavía no hay un header y una celda realizados de esa
    /// columna de donde copiar la tipografía (grilla oculta o primera pasada): se reintenta en la siguiente.
    /// </summary>
    internal static double? Medir(DataGrid grid, DataGridColumn columna, IReadOnlyList<object> items)
    {
        var header = grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
            .FirstOrDefault(h => h.IsEffectivelyVisible && h.Bounds.Width > 0 && Equals(h.Content, columna.Header));
        var celda = header is null ? null : CeldaBajo(grid, header);
        var textoCelda = celda?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => t.IsEffectivelyVisible);
        var textoHeader = header?.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(t => !string.IsNullOrEmpty(t.Text));
        if (header is null || celda is null || textoCelda is null || textoHeader is null)
            return null;

        var sonda = Sonda(textoCelda);
        var texto = Evaluador(columna);
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var maximo = 0.0;
        foreach (var item in items)
        {
            var t = texto(item);
            if (!vistos.Add(t))
                continue;
            sonda.Text = t;
            sonda.Measure(Size.Infinity);
            maximo = Math.Max(maximo, sonda.DesiredSize.Width);
        }
        var contenido = maximo + celda.Padding.Left + celda.Padding.Right + textoCelda.Margin.Left + textoCelda.Margin.Right;

        var sondaHeader = Sonda(textoHeader);
        sondaHeader.Text = textoHeader.Text;
        sondaHeader.Measure(Size.Infinity);
        var titulo = sondaHeader.DesiredSize.Width + header.Padding.Left + header.Padding.Right + ReservaOrden;

        var minimo = columna.MinWidth > 0 ? columna.MinWidth : grid.MinColumnWidth;
        var ancho = Math.Min(GetTope(columna), Math.Ceiling(Math.Max(contenido, titulo)));
        return Math.Max(minimo, ancho);
    }

    /// <summary>La primera celda realizada que está debajo del centro del header (DataGridCell.OwningColumn es interno).</summary>
    private static DataGridCell? CeldaBajo(DataGrid grid, DataGridColumnHeader header)
    {
        var centro = header.TranslatePoint(new Point(header.Bounds.Width / 2, 0), grid)?.X;
        if (centro is null)
            return null;
        return grid.GetVisualDescendants().OfType<DataGridCell>().FirstOrDefault(c =>
        {
            if (!c.IsEffectivelyVisible || c.Bounds.Width <= 0)
                return false;
            var x0 = c.TranslatePoint(default, grid)?.X;
            return x0 is not null && x0 <= centro && centro <= x0 + c.Bounds.Width;
        });
    }

    /// <summary>TextBlock suelto con la tipografía del modelo: un control oculto no mide, y los estilos de celda
    /// necesitan una DataGridCell real como ancestro, así que se copia lo que ya resolvió el tema.</summary>
    private static TextBlock Sonda(TextBlock modelo) => new()
    {
        FontFamily = modelo.FontFamily,
        FontSize = modelo.FontSize,
        FontWeight = modelo.FontWeight,
        FontStyle = modelo.FontStyle,
        FontStretch = modelo.FontStretch,
        LetterSpacing = modelo.LetterSpacing,
        FontFeatures = modelo.FontFeatures,
    };
}
```

- [ ] **Step 3: El behavior dispara la medición**

En `DataGridColumnasAutoBehavior.cs` (agregar `using System.Collections.Specialized;`):

1. En `Estado` (L111-120), agregar:

```csharp
        /// <summary>Los datos cambiaron y hay columnas que miden su contenido (ColumnaAuto) por medir.</summary>
        public bool MedicionPendiente = true;
        public INotifyCollectionChanged? Fuente;
        public NotifyCollectionChangedEventHandler? AlCambiarFuente;
        /// <summary>Último ancho medido por columna: el ancho medido solo crece mientras vive la grilla.</summary>
        public readonly Dictionary<DataGridColumn, double> AnchosMedidos = new();
```

2. En el constructor estático (L124-127), agregar: `DataGrid.ItemsSourceProperty.Changed.AddClassHandler<DataGrid>((grid, _) => ObservarFuente(grid));`

3. En `OnAjustarColumnasAutoChanged` (L133-149): al principio, si hay estado previo, desuscribir la fuente (`if (Estados.TryGetValue(grid, out var previo) && previo.Fuente is not null && previo.AlCambiarFuente is not null) previo.Fuente.CollectionChanged -= previo.AlCambiarFuente;`); y al final, después de los `AddHandler`, `ObservarFuente(grid);`.

4. Métodos nuevos (después de `TerminarGesto`):

```csharp
    // ── Columnas que miden su contenido (ColumnaAuto) ─────────────────────────────────────────

    /// <summary>
    /// Escucha la fuente actual: cualquier cambio (carga, Clear y Add de a uno, filtro u orden de un
    /// DataGridCollectionView, que re-emite los de su fuente) deja la medición pendiente. Los Clear y Add de
    /// una carga se coalescen solos: se mide una vez, en la pasada de layout siguiente.
    /// </summary>
    private static void ObservarFuente(DataGrid grid)
    {
        if (!Estados.TryGetValue(grid, out var estado))
            return;
        if (estado.Fuente is not null && estado.AlCambiarFuente is not null)
            estado.Fuente.CollectionChanged -= estado.AlCambiarFuente;
        estado.AlCambiarFuente ??= (_, _) =>
        {
            estado.MedicionPendiente = true;
            grid.InvalidateMeasure();
        };
        estado.Fuente = grid.ItemsSource as INotifyCollectionChanged;
        if (estado.Fuente is not null)
            estado.Fuente.CollectionChanged += estado.AlCambiarFuente;
        estado.MedicionPendiente = true;
        grid.InvalidateMeasure();
    }

    /// <summary>
    /// Fija en píxeles el ancho de cada columna marcada con ColumnaAuto.Medir. Grilla vacía: no se toca nada.
    /// Sin header o celda realizados todavía: queda pendiente para la pasada siguiente. Con ancho en píxeles,
    /// el reparto de abajo la trata como una columna fija.
    /// </summary>
    private static void MedirColumnas(DataGrid grid, Estado estado)
    {
        var marcadas = grid.Columns.Where(c => c.IsVisible && ColumnaAuto.GetMedir(c)).ToList();
        var items = marcadas.Count == 0 ? [] : ColumnaAuto.Items(grid.ItemsSource);
        if (items.Count == 0)
        {
            estado.MedicionPendiente = false;
            return;
        }

        var anchos = new Dictionary<DataGridColumn, double>();
        foreach (var c in marcadas)
        {
            var ancho = ColumnaAuto.Medir(grid, c, items);
            if (ancho is null)
                return;
            anchos[c] = Math.Max(ancho.Value, estado.AnchosMedidos.GetValueOrDefault(c));
        }

        estado.MedicionPendiente = false;
        estado.Reparando = true;
        try
        {
            foreach (var (c, ancho) in anchos)
            {
                estado.AnchosMedidos[c] = ancho;
                if (!c.Width.IsAbsolute || Math.Abs(c.Width.Value - ancho) > Tolerancia)
                    Asignar(c, new DataGridLength(ancho, DataGridLengthUnitType.Pixel));
            }
        }
        finally
        {
            estado.Reparando = false;
        }
    }
```

5. En `OnLayoutUpdated`, después del chequeo de visibilidad (L295-296) y antes de `var visibles = …`:

```csharp
        if (estado.MedicionPendiente)
            MedirColumnas(grid, estado);
```

6. En el `<summary>` de la clase, agregar un ítem a "Robustez": "(f) Columnas que miden su contenido (<see cref=\"ColumnaAuto\"/>): al cambiar los datos se miden sobre TODA la fuente y quedan en píxeles antes del reparto; respetan el arrastre del usuario igual que el resto."

Run: `dotnet build StockApp.sln`
Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ColumnaAutoTests"`
Expected: PASS los 12. Si `CincoMilFilas_…` da rojo, PARAR: anotar los dos tiempos en el ledger y consultar (no subir el margen sin OK; opción: medir solo textos de longitud ≥ máximo − 3 caracteres, a decidir con el usuario).

Run (regresión de grillas, comando de "Comandos de verificación"). Expected: PASS (ninguna vista marca columnas todavía: nada cambia).

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (c) En `DataGridColumnasAutoBehavior.OnLayoutUpdated`, quitar las dos líneas `if (estado.MedicionPendiente) MedirColumnas(grid, estado);` → rojo en `ConMilFilas_…` (la columna toma el ancho de las filas realizadas). Pegar. Restaurar.
- (d) En `ColumnaAuto.Medir`, quitar `+ ReservaOrden` → rojo en `Ordenar_ElTituloYLaFlechaEntranEnElHeader_…`. Restaurar.
- (e) En `ColumnaAuto.Medir`, quitar `Math.Min(GetTope(columna), …)` (dejar el `Math.Ceiling(…)`) → rojo en `TextoMasLargoQueElTope_…`. Restaurar.
- (f) En `MedirColumnas`, quitar `Math.Max(…, estado.AnchosMedidos.GetValueOrDefault(c))` (dejar `ancho.Value`) → rojo en `Recarga_ConDatosMasAngostosNoAchica_…`. Restaurar.
- (g) En `OnLayoutUpdated`, quitar `estado.UsuarioRedimensiono ||` de la guarda L293 → rojo en `UsuarioArrastraLaColumna_…` (y en `DataGridColumnasAutoAjusteTests.A_*`). Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Behaviors/ColumnaAuto.cs src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs tests/StockApp.Presentation.UiTests/V2/ColumnaAutoTests.cs
git commit -m "feat(ui-v2): columnas Auto medidas sobre toda la fuente, con tope y reserva de la flecha de orden"
```

### Task 3: `TooltipSiSeCorta` y datos fijos de los estados nuevos

**Files:**
- Create: `src/StockApp.Presentation/Behaviors/TooltipSiSeCorta.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/TooltipSiSeCortaTests.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs` (L71-124)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs`

**Interfaces:**
- Consumes: `DatosProductos.Productos`, `Listado(...)`, `EnShell(...)` (privado), `ProductoListViewModel.MensajeSinPermisoListado` (Task 1).
- Produces: `TooltipSiSeCorta.ActivoProperty` (`bool`, attached de `TextBlock`), `GetActivo/SetActivo`, `internal static bool EstaCortado(TextBlock)`; `DatosProductos.UnidadLarga` (`string`), `ProductosConUnidadLarga` (`IReadOnlyList<ProductoDto>`), `Cargando()`, `SinPermisoListado()`, `ConCeldaCortada()` (todas `ShellMainViewModel`).

- [ ] **Step 1: Tests del tooltip (rojo)**

`tests/StockApp.Presentation.UiTests/V2/TooltipSiSeCortaTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>El tooltip de una celda muestra el texto completo solo cuando la celda lo corta con elipsis.</summary>
public class TooltipSiSeCortaTests
{
    private static TextBlock Montar(string texto, double ancho)
    {
        var tb = new TextBlock { Text = texto, Width = ancho, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(tb, texto);
        TooltipSiSeCorta.SetActivo(tb, true);
        var ventana = new Window { Width = 600, Height = 200, Content = new UserControl { Classes = { "ui2" }, Content = tb } };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return tb;
    }

    private static bool SeCancela(TextBlock tb)
    {
        var e = new CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent, tb);
        tb.RaiseEvent(e);
        return e.Cancel;
    }

    [AvaloniaFact]
    public void TextoQueEntra_NoAbreElTooltip()
    {
        var tb = Montar("Bolsa", 200);

        Assert.False(TooltipSiSeCorta.EstaCortado(tb));
        Assert.True(SeCancela(tb));
    }

    [AvaloniaFact]
    public void TextoCortado_AbreElTooltipConElTextoCompleto()
    {
        var tb = Montar("Metro cúbico puesto en obra con flete incluido", 120);

        Assert.True(TooltipSiSeCorta.EstaCortado(tb));
        Assert.False(SeCancela(tb));
        Assert.Equal("Metro cúbico puesto en obra con flete incluido", ToolTip.GetTip(tb));
    }

    [AvaloniaFact]
    public void SinElBehavior_ElTooltipNoSeCancela()
    {
        var tb = Montar("Bolsa", 200);
        TooltipSiSeCorta.SetActivo(tb, false);

        Assert.False(SeCancela(tb));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TooltipSiSeCortaTests"`
Expected: FAIL de compilación.

- [ ] **Step 2: `TooltipSiSeCorta`**

`src/StockApp.Presentation/Behaviors/TooltipSiSeCorta.cs`:

```csharp
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Tooltip solo cuando el texto se corta: sobre un TextBlock con TextTrimming y ToolTip.Tip, cancela la apertura
/// si ninguna línea quedó cortada. Se decide al abrir (no al cambiar el texto o el tamaño), así anda igual con
/// las celdas que la grilla recicla al scrollear u ordenar.
/// </summary>
public static class TooltipSiSeCorta
{
    public static readonly AttachedProperty<bool> ActivoProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, TextBlock, bool>("Activo");

    static TooltipSiSeCorta()
    {
        ActivoProperty.Changed.AddClassHandler<TextBlock>((tb, e) =>
        {
            ToolTip.RemoveToolTipOpeningHandler(tb, AlAbrir);
            if (e.GetNewValue<bool>())
                ToolTip.AddToolTipOpeningHandler(tb, AlAbrir);
        });
    }

    public static bool GetActivo(TextBlock tb) => tb.GetValue(ActivoProperty);

    public static void SetActivo(TextBlock tb, bool valor) => tb.SetValue(ActivoProperty, valor);

    /// <summary>True si alguna línea del texto quedó cortada con elipsis.</summary>
    internal static bool EstaCortado(TextBlock tb) => tb.TextLayout.TextLines.Any(l => l.HasCollapsed);

    private static void AlAbrir(object? sender, CancelRoutedEventArgs e)
    {
        if (sender is TextBlock tb && !EstaCortado(tb))
            e.Cancel = true;
    }
}
```

Run: build + `--filter "FullyQualifiedName~TooltipSiSeCortaTests"`. Expected: PASS.

- [ ] **Step 3: Mutación (h)** — quitar la línea `e.Cancel = true;` → rojo en `TextoQueEntra_NoAbreElTooltip`. Pegar. Restaurar.

- [ ] **Step 4: Datos fijos (rojo y verde)**

En `DatosProductosTests.cs` agregar:

```csharp
    [Fact]
    public void ProductosConUnidadLarga_SoloCambiaLaUnidadDeArenaGruesa()
    {
        var cambiadas = DatosProductos.ProductosConUnidadLarga
            .Zip(DatosProductos.Productos, (nuevo, viejo) => (nuevo, viejo))
            .Where(p => p.nuevo != p.viejo)
            .ToList();

        var (n, v) = Assert.Single(cambiadas);
        Assert.Equal("Arena gruesa", n.Nombre);
        Assert.Equal(v with { UnidadMedidaNombre = "Metro cúbico puesto en obra con flete incluido" }, n);
        Assert.Equal(DatosProductos.UnidadLarga, n.UnidadMedidaNombre);
    }

    [Fact]
    public async Task SinPermisoListado_LaCargaMarcaSinPermisoConElMensajeDelListado()
    {
        var vm = (StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel)DatosProductos.SinPermisoListado().CurrentContent!;

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel.MensajeSinPermisoListado, vm.MensajeSinPermiso);
    }

    [Fact]
    public void Cargando_LaCargaNoTermina()
    {
        var vm = (StockApp.Presentation.ViewModels.Catalogo.ProductoListViewModel)DatosProductos.Cargando().CurrentContent!;

        var carga = vm.CargarAsync();

        Assert.False(carga.IsCompleted);
        Assert.True(vm.Cargando);
    }
```

En `DatosProductos.cs`, después de `Listado(...)`:

```csharp
    /// <summary>Unidad más larga que el tope de las columnas medidas (240): se corta con elipsis.</summary>
    public const string UnidadLarga = "Metro cúbico puesto en obra con flete incluido";

    /// <summary>Los 12 productos con la unidad de "Arena gruesa" (fila 2) más larga que el tope.</summary>
    public static readonly IReadOnlyList<ProductoDto> ProductosConUnidadLarga =
        Productos.Select(p => p.Nombre == "Arena gruesa" ? p with { UnidadMedidaNombre = UnidadLarga } : p).ToList();

    /// <summary>La carga queda esperando al servidor: el listado está en "Cargando productos…".</summary>
    public static ProductoListViewModel ListadoQueNoTermina()
    {
        var servicio = new Mock<IProductoService>();
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns(new TaskCompletionSource<IReadOnlyList<ProductoDto>>().Task);
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    /// <summary>La carga responde 403 (UnauthorizedAccessException, como ApiErrores).</summary>
    public static ProductoListViewModel ListadoSinPermiso()
    {
        var servicio = new Mock<IProductoService>();
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }
```

Y al final de la clase:

```csharp
    public static ShellMainViewModel Cargando() => EnShell(ListadoQueNoTermina());

    public static ShellMainViewModel SinPermisoListado() => EnShell(ListadoSinPermiso());

    public static ShellMainViewModel ConCeldaCortada() => EnShell(Listado(ProductosConUnidadLarga));
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosProductosTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Behaviors/TooltipSiSeCorta.cs tests/StockApp.Presentation.UiTests/V2/TooltipSiSeCortaTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs
git commit -m "feat(ui-v2): tooltip solo cuando el texto se corta y datos fijos de carga, sin permiso y celda cortada"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes del XAML de la vista)

Los mockups nuevos parten de los de la 3a: el sidebar, el encabezado y la barra de filtros se copian TAL CUAL de `productos-vacio.html` (L1-87). Nada de esto toca tokens.

### Task 4: Medición de anchos y mockups de los estados nuevos

**Decisiones aplicadas:** D1 a D13 y R1 (este último se ejecuta en la Task 6).

**Files:**
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Modify: `docs/ui-v2/mockups/inventario/productos.html` (L19-25)
- Create: `docs/ui-v2/mockups/inventario/productos-cargando.html`, `productos-sin-permiso.html`, `productos-celda-cortada.html`

**Interfaces:**
- Consumes: `ColumnaAuto` (Task 2), `DatosProductos.ConProductos()`, `ConCeldaCortada()` (Task 3).
- Produces: pantallas `inventario-productos` (re-medida), `inventario-productos-cargando`, `inventario-productos-sin-permiso`, `inventario-productos-celda-cortada`; clases `.v2-cargando` y `.v2-celda-cortada` en `base.css` (espejo de `EstadoCargando` y de la celda con `TextTrimming` de la Task 6).

- [ ] **Step 1: Medir en headless los anchos con el componente (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal3c1.cs` (se borra en el Step 6):

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Behaviors;
using StockApp.Presentation.Converters;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using Xunit;

namespace StockApp.Presentation.VisualTests;

public class MedicionTemporal3c1
{
    [AvaloniaTheory]
    [InlineData(1280, 760, false)]
    [InlineData(1366, 768, false)]
    [InlineData(1280, 760, true)]
    [InlineData(1366, 768, true)]
    public void Medir(int ancho, int alto, bool celdaCortada)
    {
        var vm = celdaCortada ? DatosProductos.ConCeldaCortada() : DatosProductos.ConProductos();
        var shell = new ShellMainView { DataContext = vm };
        var ventana = new Window { Width = ancho, Height = alto, Content = shell };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        shell.FindControl<Border>("Sidebar")!.IsVisible = false;
        var grilla = shell.GetVisualDescendants().OfType<DataGrid>().Single();
        foreach (var c in grilla.Columns.Where(c => (string)c.Header! is "CÓDIGO" or "COSTO" or "UNIDAD"))
        {
            ColumnaAuto.SetMedir(c, true);
            if ((string)c.Header! == "COSTO") ColumnaAuto.SetConversor(c, MonedaConverter.Instance);
        }
        // Reasignar la misma fuente no dispara el cambio: se pasa por null para que el behavior la vuelva a observar.
        var fuente = grilla.ItemsSource;
        grilla.ItemsSource = null;
        grilla.ItemsSource = fuente;
        for (var i = 0; i < 4; i++) Dispatcher.UIThread.RunJobs();

        var linea = string.Join(" | ", grilla.Columns.Select(c => $"{c.Header}={c.ActualWidth:F1}"));
        TestContext.Current.TestOutputHelper!.WriteLine($"{ancho}x{alto} cortada={celdaCortada} grilla={grilla.Bounds.Width:F1}: {linea}");
    }
}
```

(`TestContext.Current.TestOutputHelper` es el de xUnit v3, como en `DataGridColumnasAutoAjusteTests.F_GrillaGrande…`.)

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal3c1" --logger "console;verbosity=detailed"`

Anotar en el ledger las 4 líneas (anchos de CÓDIGO, COSTO, UNIDAD, NOMBRE, CATEGORÍA y el ancho de la grilla a cada tamaño). Esperado: CÓDIGO cambia respecto de 81 (D6); COSTO queda en 100 (`MinWidth`) o sube; UNIDAD ~97; con la celda cortada UNIDAD = 240 y, si las `*` quedan en 160, la grilla desborda: anotar si aparece el scroll horizontal (es la consecuencia de D5 que se muestra en el GATE).

- [ ] **Step 2: Espejos CSS**

Al final de `docs/ui-v2/mockups/_base/base.css`:

```css
/* Indicador de carga (Controls/V2/ComponentesV2.axaml, v2:EstadoCargando): texto secundario centrado en el
   lugar del estado vacío, con su mismo margen vertical. Sin animación. */
.v2-cargando { margin: 32px 0; text-align: center; font-size: var(--v2-fuente-control-tamano); line-height: var(--v2-fuente-control-alto-linea); color: var(--v2-color-texto-secundario); }
/* Celda cortada por el tope de su columna (TextBlock con TextTrimming CharacterEllipsis): la celda es flex, así
   que la elipsis va en un span hijo que puede encogerse. */
.v2-celda-cortada { min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
```

- [ ] **Step 3: `productos.html` re-medido**

Reemplazar en `productos.html` L19-25 los `flex: 0 0 Npx` de `.col-codigo`, `.col-costo`, `.col-unidad` y `.col-categoria` por los medidos a 1280 (Step 1); si a 1366 alguno difiere, agregarlo en un `@media (min-width: 1366px) { … }` al final del `<style>`. Actualizar el comentario de la L18 a: "Anchos medidos en headless con el tema v2, estos datos y las columnas que miden su contenido (CÓDIGO, COSTO, UNIDAD); STOCK y ESTADO fijos; NOMBRE y CATEGORÍA proporcionales."

- [ ] **Step 4: Tres mockups nuevos**

`productos-cargando.html`: copia de `productos-vacio.html` con estos cambios:
- `<title>Inventario: Productos cargando (UI v2)</title>`
- Comentario de datos: `<!-- Datos: DatosProductos.Cargando(): la carga espera al servidor; sin filas todavía, la grilla y el pie se ocultan y se ve el indicador (nunca el estado vacío). -->`
- El bloque `<div class="v2-vacio">…</div>` (L88-91) se reemplaza por `<div class="v2-cargando">Cargando productos…</div>`.

`productos-sin-permiso.html`: copia de `productos-vacio.html` con:
- `<title>Inventario: Productos sin permiso (UI v2)</title>`
- Comentario: `<!-- Datos: DatosProductos.SinPermisoListado(): la carga respondió 403; el estado reemplaza a la card y el encabezado conserva sus botones. .pagina es flex: el margen del encabezado (16) y el del estado (32) se suman igual que en Avalonia. -->`
- Todo el `<div class="v2-tarjeta">…</div>` (L83-92) se reemplaza por:

```html
      <div class="v2-vacio">
        <div class="v2-vacio-titulo v2-vacio-titulo--error">Sin permiso</div>
        <div class="v2-vacio-mensaje">No tenés permiso para ver los productos.</div>
      </div>
```

`productos-celda-cortada.html`: copia de `productos.html` (ya re-medido) con:
- `<title>Inventario: Productos con una celda cortada (UI v2)</title>`
- Comentario: `<!-- Datos: DatosProductos.ConCeldaCortada(): la unidad de Arena gruesa pasa el tope de 240; la columna queda en el tope y la celda corta con elipsis. -->`
- `.col-unidad` (y, si el Step 1 lo midió distinto, `.col-nombre`/`.col-categoria` en px fijos si quedaron en su piso de 160) con los anchos medidos con la celda cortada.
- En la fila de "Arena gruesa", la celda de unidad pasa a `<div class="v2-grilla-celda col-unidad"><span class="v2-celda-cortada">Metro cúbico puesto en obra con flete incluido</span></div>`.
- Si el Step 1 midió desborde (scroll horizontal), el mockup lo dibuja como la app (barra de scroll de Fluent, alto y color medidos de la captura de la app: usar `capturar-mockup` y comparar a ojo antes del GATE) y se señala en el GATE como la consecuencia de D5.

- [ ] **Step 5: Capturas y convenciones**

```bash
for p in productos productos-cargando productos-sin-permiso productos-celda-cortada; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/inventario-productos*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 8 PNG con fecha de ahora, dentro del worktree, sin error de fuentes ni desborde de `.v2-ventana`; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG: el indicador centrado donde va el vacío; "Sin permiso" en rojo peligro en lugar de la card; la elipsis en la unidad cortada.

- [ ] **Step 6: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal3c1.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 7: GATE — aprobación del usuario**

PARAR. Presentar los 8 PNG (LOS DOS TAMAÑOS) con la lista D1 a D13, R1 y los anchos medidos del ledger. Señalar explícitamente: el indicador es texto, sin spinner (D1), y solo aparece sin filas (D2); "Sin permiso" reemplaza la card (D3); el formulario no muestra indicador (D4); el tope de 240 y su consecuencia medida en el Step 1 (desborde con la celda cortada, D5), con la alternativa de 160; CÓDIGO crece por la reserva de la flecha (D6, el antes y el después en px); las columnas no se achican con una búsqueda (D7); NOMBRE y CATEGORÍA dejan de mostrar tooltip cuando el texto entra (D8); el tooltip no se mockea (D9); el catálogo de componentes no cambia (D11); `SidebarPx` 240 en el barrido (R1). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups, en `ColumnaAuto.TopePorDefecto` y sus tests (D5), y en los tests de las Tasks 5 y 6 que la fijan, y volver a presentar. Registrar en el ledger: `Mockups 3c-1: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 8: Commit**

```bash
git add docs/ui-v2/mockups/_base/base.css docs/ui-v2/mockups/inventario/productos.html docs/ui-v2/mockups/inventario/productos-cargando.html docs/ui-v2/mockups/inventario/productos-sin-permiso.html docs/ui-v2/mockups/inventario/productos-celda-cortada.html
git commit -m "docs(ui-v2): mockups aprobados de Productos cargando, sin permiso y con celda cortada"
```

## Bloque C — XAML y comparaciones (arranca solo con los mockups aprobados)

### Task 5: `v2:EstadoCargando`

**Depende de:** Task 4 (D1 y D12 aprobadas; si el usuario eligió spinner, esta task se reescribe con él antes de empezar).

**Files:**
- Create: `src/StockApp.Presentation/Controls/V2/EstadoCargando.cs`
- Modify: `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` (después del `ControlTheme` de `EstadoVacio`, L375-~410)
- Create: `tests/StockApp.Presentation.UiTests/V2/EstadoCargandoTests.cs`

**Interfaces:**
- Produces: `StockApp.Presentation.Controls.V2.EstadoCargando` (`TemplatedControl`, `Texto: string?`), parte `PART_Texto` (`TextBlock`).

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.UiTests/V2/EstadoCargandoTests.cs`:

```csharp
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.VisualTree;
using StockApp.Presentation.Controls.V2;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Indicador de carga v2: texto secundario 13/16 centrado con 32 arriba y abajo, sin redondeo de layout.</summary>
public class EstadoCargandoTests
{
    [AvaloniaFact]
    public void Texto_SecundarioDeControlCentradoConMargen32()
    {
        var ventana = MontajeV2.Montar("""<v2:EstadoCargando x:Name="Indicador" Texto="Cargando productos…" />""", ancho: 800);
        var indicador = MontajeV2.Buscar<EstadoCargando>(ventana, "Indicador");
        var texto = MontajeV2.Parte<TextBlock>(indicador, "PART_Texto");

        Assert.Equal("Cargando productos…", texto.Text);
        Assert.Equal(13, texto.FontSize);
        Assert.Equal(16, texto.LineHeight);
        Assert.Equal(MontajeV2.Hex("#57534E"), MontajeV2.ColorDe(texto.Foreground));
        Assert.Equal(HorizontalAlignment.Center, texto.HorizontalAlignment);
        Assert.Equal(new Avalonia.Thickness(0, 32), texto.Margin);
        Assert.Equal(16, texto.Bounds.Height);
        var centro = texto.TranslatePoint(new Avalonia.Point(texto.Bounds.Width / 2, 0), indicador)!.Value.X;
        Assert.Equal(indicador.Bounds.Width / 2, centro, 0.5);
    }

    [AvaloniaFact]
    public void SinRedondeoDeLayout_PorqueElTextoCentradoTieneAnchoFraccionario()
    {
        var ventana = MontajeV2.Montar("""<v2:EstadoCargando x:Name="Indicador" Texto="Cargando productos…" />""");

        Assert.False(MontajeV2.Buscar<EstadoCargando>(ventana, "Indicador").UseLayoutRounding);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~EstadoCargandoTests"` → FAIL de compilación.

- [ ] **Step 2: Control y tema**

`src/StockApp.Presentation/Controls/V2/EstadoCargando.cs`:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace StockApp.Presentation.Controls.V2;

/// <summary>
/// Indicador de carga v2: un texto ("Cargando productos…") en el lugar del estado vacío, mientras la carga corre
/// y todavía no hay nada que mostrar. Texto y no spinner: una animación no se puede fijar contra un mockup.
/// </summary>
public class EstadoCargando : TemplatedControl
{
    public static readonly StyledProperty<string?> TextoProperty =
        AvaloniaProperty.Register<EstadoCargando, string?>(nameof(Texto));

    public string? Texto
    {
        get => GetValue(TextoProperty);
        set => SetValue(TextoProperty, value);
    }
}
```

En `ComponentesV2.axaml`, después del cierre del `ControlTheme` de `EstadoVacio`:

```xml
    <!-- Indicador de carga (espejo: .v2-cargando en base.css). UseLayoutRounding False en el tema: el texto va
         centrado y su ancho es fraccionario; Avalonia redondea el origen y Chromium no (mismo caso que
         EstadoVacio, que lo lleva por instancia porque en el catálogo de componentes movía su línea base). -->
    <ControlTheme x:Key="{x:Type v2:EstadoCargando}" TargetType="v2:EstadoCargando">
        <Setter Property="UseLayoutRounding" Value="False" />
        <Setter Property="Template">
            <ControlTemplate>
                <TextBlock x:Name="PART_Texto"
                           Text="{TemplateBinding Texto}"
                           HorizontalAlignment="Center"
                           Margin="0,32"
                           FontSize="{DynamicResource V2FuenteControlTamano}"
                           LineHeight="{DynamicResource V2FuenteControlAltoLinea}"
                           Foreground="{DynamicResource V2TextoSecundarioBrush}" />
            </ControlTemplate>
        </Setter>
    </ControlTheme>
```

Run: `dotnet build StockApp.sln`; el test de Step 1 → PASS; `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`; `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar` (exit 0); `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~CatalogoComponentes"` (sin cambios: D11).

- [ ] **Step 3: Mutación (i)** — quitar el `Setter` de `UseLayoutRounding` → rojo en `SinRedondeoDeLayout_…`. Pegar. Restaurar.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Presentation/Controls/V2/EstadoCargando.cs src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml tests/StockApp.Presentation.UiTests/V2/EstadoCargandoTests.cs
git commit -m "feat(ui-v2): indicador de carga v2 EstadoCargando"
```

### Task 6: Productos con estado de carga, sin permiso y columnas medidas, comparado

**Depende de:** Tasks 1 a 5 (mockups aprobados; leer del ledger las D# cambiadas).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` (el `.axaml.cs` NO cambia)
- Modify: `tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GrillasSinRecorteBarridoTests.cs` (L30-44, L82, L186-197)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs`

**Interfaces:**
- Consumes: `ViewModelBase.Cargando`, `SinPermiso`, `MensajeSinPermiso` (Task 1); `ColumnaAuto`, `TooltipSiSeCorta` (Tasks 2-3); `EstadoCargando` (Task 5); `DatosProductos.Cargando()`, `SinPermisoListado()`, `ConCeldaCortada()` (Task 3); mockups de la Task 4.
- Produces: partes nombradas de `ProductoListView`: `IndicadorCarga` (`EstadoCargando`), `EstadoSinPermiso` (`EstadoVacio`), y las existentes `Tarjeta`, `EstadoSinProductos`, `Grilla`, `PieConteo`.

- [ ] **Step 1: UiTests de la vista (rojo)**

En `ProductoListViewV2Tests.cs`:

1. `ProductosFake` gana un servicio que puede quedar esperando o fallar:

```csharp
        public TaskCompletionSource<IReadOnlyList<ProductoDto>>? Pendiente { get; set; }
        public Exception? Falla { get; set; }
```

y `BuscarAsync`/`BuscarPorTextoAsync` pasan a:

```csharp
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Responder(Lista.ToList());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Responder(Lista.Where(p => string.IsNullOrEmpty(texto) || p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());

        private Task<IReadOnlyList<ProductoDto>> Responder(List<ProductoDto> resultado) =>
            Falla is not null ? Task.FromException<IReadOnlyList<ProductoDto>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<ProductoDto>>(resultado);
```

2. `Montar` acepta el fake ya configurado: agregar la sobrecarga

```csharp
    private static Montaje Montar(ProductosFake servicio)
    {
        var navegacion = new NavigationRecorderFake();
        var vm = new ProductoListViewModel(servicio, navegacion, new ConfirmacionServiceFake());
        var vista = new ProductoListView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }
```

y la existente `Montar(params ProductoDto[] productos)` pasa a `=> Montar(new ProductosFake { Lista = productos.ToList() });`.

3. Helpers:

```csharp
    private static bool Visible(Montaje m, string nombre) =>
        ArbolVisual.EsVisibleEnArbol(m.Vista.GetVisualDescendants().OfType<Control>().First(c => c.Name == nombre));

    private static DataGridColumn Columna(DataGrid grilla, string header) => grilla.Columns.Single(c => Equals(c.Header, header));

    /// <summary>El TextBlock de la celda de <paramref name="item"/> que está bajo el header de la columna.</summary>
    private static TextBlock CeldaDe(DataGrid grilla, object item, string header)
    {
        var h = grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Single(x => Equals(x.Content, header));
        var centro = h.TranslatePoint(new Point(h.Bounds.Width / 2, 0), grilla)!.Value.X;
        var fila = grilla.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, item));
        var celda = fila.GetVisualDescendants().OfType<DataGridCell>().Single(c =>
        {
            var x0 = c.TranslatePoint(default, grilla)!.Value.X;
            return x0 <= centro && centro <= x0 + c.Bounds.Width;
        });
        return celda.GetVisualDescendants().OfType<TextBlock>().First(t => t.IsEffectivelyVisible && t.FindAncestorOfType<Insignia>() is null);
    }

    private static bool TooltipSeCancela(TextBlock tb)
    {
        var e = new Avalonia.Interactivity.CancelRoutedEventArgs(ToolTip.ToolTipOpeningEvent, tb);
        tb.RaiseEvent(e);
        return e.Cancel;
    }
```

4. Tests nuevos:

```csharp
    [AvaloniaFact]
    public void Cargando_SinFilas_MuestraElIndicadorYNiElVacioNiLaGrilla()
    {
        var m = Montar(new ProductosFake { Pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>() });

        Assert.True(m.Vm.Cargando);
        Assert.True(Visible(m, "IndicadorCarga"));
        Assert.Equal("Cargando productos…", MontajeV2.Parte<EstadoCargando>(m.Vista, "IndicadorCarga").Texto);
        Assert.False(Visible(m, "EstadoSinProductos"));
        Assert.False(Visible(m, "Grilla"));
        Assert.False(Visible(m, "PieConteo"));
        Assert.False(Visible(m, "EstadoSinPermiso"));
    }

    [AvaloniaFact]
    public void AlTerminarLaCargaSinProductos_ElIndicadorDaPasoAlEstadoVacio()
    {
        var pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var m = Montar(new ProductosFake { Pendiente = pendiente });

        pendiente.SetResult([]);
        Dispatcher.UIThread.RunJobs();

        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "EstadoSinProductos"));
    }

    [AvaloniaFact]
    public void Recarga_ConFilasEnPantalla_NoMuestraElIndicadorYLaGrillaSigue()
    {
        var m = Montar(Activo, Negativo);
        m.Servicio.Pendiente = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(m.Vm.Cargando);
        Assert.False(Visible(m, "IndicadorCarga"));
        Assert.True(Visible(m, "Grilla"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnLaCarga_MuestraSinPermisoEnLugarDeLaCardYNuncaElVacio()
    {
        var m = Montar(new ProductosFake { Falla = new UnauthorizedAccessException("403") });

        Assert.True(Visible(m, "EstadoSinPermiso"));
        var estado = MontajeV2.Parte<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(estado.EsError);
        Assert.Equal("Sin permiso", estado.Titulo);
        Assert.Equal(ProductoListViewModel.MensajeSinPermisoListado, estado.Mensaje);
        Assert.False(Visible(m, "Tarjeta"));
        Assert.False(Visible(m, "EstadoSinProductos"));
        Assert.False(Visible(m, "IndicadorCarga"));
    }

    [AvaloniaFact]
    public void SinPermiso_EnUnaBusqueda_ReemplazaLaGrillaPorSinPermiso()
    {
        var m = Montar(Activo);
        m.Servicio.Falla = new UnauthorizedAccessException("403");

        Clickear(m.Ventana, MontajeV2.Parte<Button>(m.Vista, "BotonBuscar"));

        Assert.True(Visible(m, "EstadoSinPermiso"));
        Assert.False(Visible(m, "Tarjeta"));
    }

    [AvaloniaFact]
    public void Scroll_MasDeMilFilas_HastaElFinal_NoMueveNingunaColumna()
    {
        // 1200 productos; los valores más anchos de CÓDIGO, COSTO y UNIDAD están en la ÚLTIMA fila, fuera de pantalla.
        var productos = Enumerable.Range(1, 1199).Select(i => P(i, $"Producto {i}", i)).ToList();
        productos.Add(new ProductoDto(1200, "MAT-CONSTRUCCION-0001", null, "Último", null, 1, "Construcción", null, 1,
            "Metro cúbico", 98765432.10m, 3m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc)));
        var m = Montar(productos.ToArray());
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        grilla.ScrollIntoView(productos[^1], null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(grilla.GetVisualDescendants().OfType<DataGridRow>(), r => ReferenceEquals(r.DataContext, productos[^1]));
        var despues = grilla.Columns.Select(c => c.ActualWidth).ToArray();
        for (var i = 0; i < antes.Length; i++)
            Assert.True(Math.Abs(antes[i] - despues[i]) <= 0.5,
                $"La columna {grilla.Columns[i].Header} pasó de {antes[i]} a {despues[i]} al llegar a la fila 1200.");
        foreach (var header in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
            Assert.False(TooltipSiSeCorta.EstaCortado(CeldaDe(grilla, productos[^1], header)), $"{header} quedó cortado sin pasar el tope.");
    }

    [AvaloniaFact]
    public void Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo()
    {
        var m = Montar(Activo, Negativo, Inactivo, InactivoNegativo);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");
        var antes = grilla.Columns.Select(c => c.ActualWidth).ToArray();

        foreach (var header in grilla.GetVisualDescendants().OfType<DataGridColumnHeader>().Where(h => h.Content is string s && s != "ESTADO").ToList())
        {
            Clickear(m.Ventana, header);
            var titulo = header.GetVisualDescendants().OfType<TextBlock>().First(t => !string.IsNullOrEmpty(t.Text));
            var natural = new TextBlock { Text = titulo.Text, FontSize = titulo.FontSize, FontWeight = titulo.FontWeight,
                FontFamily = titulo.FontFamily, LetterSpacing = titulo.LetterSpacing, FontFeatures = titulo.FontFeatures };
            natural.Measure(Size.Infinity);
            Assert.True(titulo.Bounds.Width >= natural.DesiredSize.Width - 0.5, $"{titulo.Text} ordenado quedó cortado.");
            Assert.Equal(antes, grilla.Columns.Select(c => c.ActualWidth).ToArray());
        }
    }

    [AvaloniaFact]
    public void Paridad_ElTextoMedidoEsElTextoPintado()
    {
        var m = Montar(Activo, Negativo, Inactivo, InactivoNegativo);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        foreach (var p in new[] { Activo, Negativo, Inactivo, InactivoNegativo })
            foreach (var header in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
                Assert.Equal(CeldaDe(grilla, p, header).Text, ColumnaAuto.Texto(Columna(grilla, header), p));
    }

    [AvaloniaFact]
    public void CeldaCortada_MuestraElipsisYTooltip_YLasQueEntranNo()
    {
        var largo = P(5, "Arena gruesa", 45m) with { UnidadMedidaNombre = "Metro cúbico puesto en obra con flete incluido" };
        var m = Montar(Activo, largo);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        // Ventana de 1040 (la vista a 1280 sin el sidebar): UNIDAD pide 240 y cede al espacio libre (145).
        Assert.Equal(145, Columna(grilla, "UNIDAD").ActualWidth, 1);
        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);
        var cortada = CeldaDe(grilla, largo, "UNIDAD");
        Assert.True(TooltipSiSeCorta.EstaCortado(cortada));
        Assert.False(TooltipSeCancela(cortada));
        Assert.Equal(largo.UnidadMedidaNombre, ToolTip.GetTip(cortada));
        foreach (var header in new[] { "CÓDIGO", "NOMBRE", "COSTO", "UNIDAD", "CATEGORÍA" })
            Assert.True(TooltipSeCancela(CeldaDe(grilla, Activo, header)), $"{header} de una celda que entra abre tooltip.");
    }

    [AvaloniaTheory]
    [InlineData(1040, 145)]
    [InlineData(1126, 231)]
    public void CeldaCortada_LaGrillaNoScrollea_YUnidadTomaElEspacioLibre(double ancho, double unidad)
    {
        // La vista a 1280 y a 1366 sin el sidebar de 240, con los datos del mockup de la celda cortada.
        var m = Montar(DatosProductosV2.ProductosConUnidadLarga.ToArray(), ancho);
        var grilla = MontajeV2.Parte<DataGrid>(m.Vista, "Grilla");

        Assert.Equal(unidad, Columna(grilla, "UNIDAD").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "NOMBRE").ActualWidth, 1);
        Assert.Equal(160, Columna(grilla, "CATEGORÍA").ActualWidth, 1);
        Assert.DoesNotContain(grilla.GetVisualDescendants().OfType<ScrollBar>(),
            s => s.Orientation == Orientation.Horizontal && s.IsEffectivelyVisible);
    }
```

(`Montar` gana un parámetro opcional `double ancho = 1040` para la ventana; `DatosProductosV2.ProductosConUnidadLarga` son los 12 productos de `DatosProductos.ConCeldaCortada()` copiados en UiTests, que no referencia VisualTests: copiar la lista de `ColumnaAutoTopeAdaptableTests.ConCeldaCortada()`.)

5. En `ListaVacia_MuestraEstadoVacio_YAlLlegarDatosVuelveLaGrilla`, después del primer bloque de asserts: `Assert.False(Visible(m, "IndicadorCarga"));`.

Agregar `using StockApp.Presentation.Behaviors;` arriba.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ProductoListViewV2Tests"`
Expected: FAIL (no existen `IndicadorCarga` ni `EstadoSinPermiso`; las columnas no miden).

- [ ] **Step 2: `ProductoListView.axaml`**

1. Raíz: agregar `xmlns:b="using:StockApp.Presentation.Behaviors"`. Comentario L15-16: agregar "Mientras carga sin filas: EstadoCargando; tras un 403: Sin permiso en lugar de la card; el vacío solo con la carga terminada. CÓDIGO, COSTO y UNIDAD miden su contenido sobre todas las filas (ColumnaAuto)."

2. Entre `</v2:EncabezadoPagina>` y `<v2:TarjetaContenido x:Name="Tarjeta">`:

```xml
        <!-- Sin permiso (403 en la carga o en una búsqueda): reemplaza a la card, como en el formulario. El margen
             del encabezado y el del estado se suman igual en el mockup (.pagina es flex: no colapsa). -->
        <v2:EstadoVacio x:Name="EstadoSinPermiso"
                        DockPanel.Dock="Top"
                        UseLayoutRounding="False"
                        Titulo="Sin permiso"
                        Mensaje="{Binding MensajeSinPermiso}"
                        EsError="True"
                        IsVisible="{Binding SinPermiso}" />
```

3. `<v2:TarjetaContenido x:Name="Tarjeta">` → `<v2:TarjetaContenido x:Name="Tarjeta" IsVisible="{Binding !SinPermiso}">`.

4. Antes de `EstadoSinProductos`:

```xml
                <!-- Cargando y todavía sin filas: dice que carga en vez de afirmar que no hay productos. Con filas en
                     pantalla (una búsqueda o una recarga) la grilla queda hasta que llegan los resultados. -->
                <v2:EstadoCargando x:Name="IndicadorCarga" DockPanel.Dock="Top" Texto="Cargando productos…">
                    <v2:EstadoCargando.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" />
                            <Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoCargando.IsVisible>
                </v2:EstadoCargando>
```

5. `EstadoSinProductos`: borrar `IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.EsCero}}"` y agregar:

```xml
                    <v2:EstadoVacio.IsVisible>
                        <MultiBinding Converter="{x:Static BoolConverters.And}">
                            <Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="SinPermiso" Converter="{x:Static BoolConverters.Not}" />
                            <Binding Path="Items.Count" Converter="{x:Static conv:ConteoConverter.EsCero}" />
                        </MultiBinding>
                    </v2:EstadoVacio.IsVisible>
```

(`BoolConverters` de `Avalonia.Data.Converters` resuelve sin prefijo en el namespace por defecto, como en `InicioView.axaml` L77.)

6. Comentario de las columnas (L55-59): reemplazar la última oración ("ESTADO fija 78 …") por: "CÓDIGO, COSTO y UNIDAD miden su texto sobre TODAS las filas al cargar (b:ColumnaAuto), con un tope; lo que lo pasa se corta con elipsis y se lee en el tooltip, que solo abre si el texto está cortado. ESTADO fija 78 (la insignia es un conjunto cerrado) y STOCK 220."

7. Columnas:
- CÓDIGO: `<DataGridTemplateColumn Header="CÓDIGO" Width="Auto" SortMemberPath="Codigo" b:ColumnaAuto.Medir="True">` y su `TextBlock`: `<TextBlock Text="{Binding Codigo}" Classes.inactivo="{Binding !Activo}" TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding Codigo}" b:TooltipSiSeCorta.Activo="True" VerticalAlignment="Center" />`
- NOMBRE: agregar `b:TooltipSiSeCorta.Activo="True"` al `TextBlock` (D8).
- COSTO: `<DataGridTemplateColumn Header="COSTO" Width="Auto" MinWidth="100" CellStyleClasses="num" SortMemberPath="PrecioCosto" b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">` y su `TextBlock` gana `TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding PrecioCosto, Converter={x:Static conv:MonedaConverter.Instance}}" b:TooltipSiSeCorta.Activo="True"`.
- UNIDAD: `<DataGridTemplateColumn Header="UNIDAD" Width="Auto" SortMemberPath="UnidadMedidaNombre" b:ColumnaAuto.Medir="True">` y su `TextBlock`: `TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding UnidadMedidaNombre}" b:TooltipSiSeCorta.Activo="True"`.
- CATEGORÍA: agregar `b:TooltipSiSeCorta.Activo="True"` (D8).

Run: `dotnet build StockApp.sln`
Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ProductoListViewV2Tests"` → PASS.

- [ ] **Step 3: Barrido de recortes (U2 y R1)**

En `GrillasSinRecorteBarridoTests.cs`:
1. `internal const double SidebarPx = 232;` → `240`; summary L34-35: "ventana menos el sidebar v2 de 240px. 1366 (la resolución típica de las PC municipales) -> 1126; 1920 -> 1680."
2. En `Escenarios`, después de "Productos" (L82):

```csharp
        // Una unidad más larga que el espacio de su columna: se corta con elipsis y tooltip a propósito (no es recorte).
        new("Productos (unidad que pasa el tope)", () => new ProductoListView(), _ => Muchos<object>(i =>
            Producto(i, 10 + i) with { UnidadMedidaNombre = i == 0 ? "Metro cúbico puesto en obra con flete incluido" : "Metro cuadrado" })),
```

3. En `Recortes`, dentro del `foreach (var tb in celda…)`, antes del `if (tb.Bounds.Width < …)`: `if (EsCorteAdmitido(grid, col, tb)) continue;` y el método:

```csharp
    /// <summary>
    /// Columna medida (ColumnaAuto) que CEDIÓ al espacio libre de la grilla (ocupa menos de lo que midió) o que
    /// midió el tope: el texto que no entra se corta con elipsis y se lee entero en el tooltip, que abre solo si
    /// está cortado. Es el criterio común de columnas Auto, no un recorte. Una medida que ni cedió ni llegó al tope
    /// tiene que mostrar su texto entero: si lo corta, es un error de medición y el barrido lo marca.
    /// </summary>
    private static bool EsCorteAdmitido(DataGrid grid, DataGridColumn col, TextBlock tb) =>
        ColumnaAuto.GetMedir(col)
        && DataGridColumnasAutoBehavior.AnchoMedido(grid, col) is { } medido
        && (col.ActualWidth < medido - 0.5 || medido >= ColumnaAuto.GetTope(col) - 0.5)
        && tb.TextTrimming != Avalonia.Media.TextTrimming.None
        && TooltipSiSeCorta.GetActivo(tb)
        && Equals(ToolTip.GetTip(tb), tb.Text);
```

(con `using StockApp.Presentation.Behaviors;`).

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~GrillasSinRecorteBarridoTests|FullyQualifiedName~GrillasEspacioDerechoBarridoTests"`
Expected: PASS. Si falla un escenario que NO es de Productos por el cambio a 240: PARAR según R1 (ledger + consulta).

- [ ] **Step 4: Comparaciones (rojo, verde y diagnóstico)**

En `ProductosComparacionTests.cs`, después de `ProductosVacio_IgualAlMockup_1366x768`:

```csharp
    [AvaloniaFact]
    public void ProductosCargando_IgualAlMockup() => Comparar(DatosProductos.Cargando(), "inventario/productos-cargando.html");

    [AvaloniaFact]
    public void ProductosCargando_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Cargando(), "inventario/productos-cargando.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosSinPermiso_IgualAlMockup() => Comparar(DatosProductos.SinPermisoListado(), "inventario/productos-sin-permiso.html");

    [AvaloniaFact]
    public void ProductosSinPermiso_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinPermisoListado(), "inventario/productos-sin-permiso.html", TamanoLienzo.Municipio);

    // La unidad cortada con elipsis es a propósito (la columna cede al espacio libre): se declara como recorte permitido.
    private static readonly string[] UnidadCortada = [DatosProductos.UnidadLarga];

    [AvaloniaFact]
    public void ProductosCeldaCortada_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosProductos.ConCeldaCortada(), "inventario/productos-celda-cortada.html",
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar, recortesPermitidos: UnidadCortada);

    [AvaloniaFact]
    public void ProductosCeldaCortada_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), DatosProductos.ConCeldaCortada(), "inventario/productos-celda-cortada.html",
            tamano: TamanoLienzo.Municipio, ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar, recortesPermitidos: UnidadCortada);
```

(`recortesPermitidos` es `IReadOnlyCollection<string>?` en `ComparacionVisual.CompararConMockup`, L39.)

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~ProductosComparacionTests"` y `scripts/ui-v2/comparar.sh Productos`.
Expected: las 18 PASS (las 12 de la 3a, con `Productos_IgualAlMockup*` ahora contra el mockup re-medido, y las 6 nuevas); anotar el número de teselas densas de cada una en el ledger. Abrir `artifacts/ui-v2/comparacion/index.html` y MIRAR cada fila nueva: el indicador centrado, "Sin permiso" en lugar de la card, la elipsis en la unidad. Si una fila supera el umbral: `teselas.py`/`tinta.py` (plan de la 3a) sobre esa pantalla ANTES de tocar nada; geometría se corrige en XAML o mockup (≤ 4 px no visible se anota, mayor vuelve al usuario); tono se presenta al usuario con cada tesela (U4 de la 3a). La elipsis puede cortar en otro carácter en Avalonia y en Chromium: si es la única diferencia, mostrarla al usuario con la tesela y decidir (no subir el umbral sin su OK).

- [ ] **Step 5: Regresión de grillas**

Correr el filtro "Regresión de grillas" de "Comandos de verificación". Expected: PASS (en particular `DataGridColumnasAutoAjusteTests.Productos_AlLlegarLosDatos_LaColumnaStockMideEfectivamente220`, `G_ProductosEnShellA1366_NombreYCategoriaNoBajanDelPiso`, `DataGridSortClickTests`, `DataGridCeldaNumericaOrdenTests`, `ProductoListViewStockOrdenTests`, `Scroll_HastaLaPrimeraFilaInactiva_NoMueveNingunaColumna`).

- [ ] **Step 6: Mutaciones sobre la View real (QUITANDO)**

- (j) Quitar `b:ColumnaAuto.Medir="True"` de UNIDAD → rojo en `Scroll_MasDeMilFilas_…`, `CeldaCortada_MuestraElipsis…` y `CeldaCortada_LaGrillaNoScrollea_…`. Pegar. Restaurar.
- (k) Quitar `<Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />` del `MultiBinding` de `EstadoSinProductos` → rojo en `Cargando_SinFilas_…` (el vacío aparece durante la carga). Restaurar.
- (l) Quitar `IsVisible="{Binding !SinPermiso}"` de `Tarjeta` → rojo en `SinPermiso_EnLaCarga_…` y `SinPermiso_EnUnaBusqueda_…`. Restaurar.
- (m) Quitar `b:TooltipSiSeCorta.Activo="True"` del `TextBlock` de CÓDIGO → rojo en `CeldaCortada_MuestraElipsis…` (la celda de CÓDIGO que entra abre tooltip). Restaurar.
- (n) Quitar `b:ColumnaAuto.Conversor="…"` de COSTO → rojo en `Paridad_ElTextoMedidoEsElTextoPintado`. Restaurar.
- (o) En `GrillasSinRecorteBarridoTests`, quitar `if (EsCorteAdmitido(grid, col, tb)) continue;` → rojo en el escenario "Productos (unidad que pasa el tope)". Restaurar.
- (p) En `ColumnaAuto.Medir`, quitar `celda.Padding.Left + celda.Padding.Right` del ancho del contenido → la columna mide 16 px de menos, no cede ni llega al tope y su texto se corta: rojo en el barrido en el escenario "Productos" (la excepción NO la exime). Restaurar.

`dotnet build StockApp.sln` después de cada restauración y `git diff --no-index` vacío.

- [ ] **Step 7: Suite de la task y commit**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: todo verde, `tokens --verificar` exit 0. La review de esta task corre los mismos seis comandos.

```bash
git add src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs tests/StockApp.Presentation.UiTests/GrillasSinRecorteBarridoTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs
git commit -m "feat(ui-v2): Productos con estado de carga, sin permiso y columnas medidas sobre todas las filas, comparados"
```

## Bloque D — Cierre de la fase

### Task 7: Suite completa, verificación orgánica, aprobación final, herramientas y spec

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 18 a 20; la 14 reescrita)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 3c-1 cerrada en `feat/ui-v2` (sin merge a `main`).

- [x] **Step 1: Build, suite, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos de fixtures `.ods` (confirmar por nombre); los dos `--verificar` exit 0; TODAS las filas de `artifacts/ui-v2/comparacion/index.html` APROBADAS a los dos tamaños (las de las Fases 0 a 3b sin cambios: esta fase no tocó estilos compartidos salvo el `ControlTheme` nuevo; si una se mueve, diagnosticarla). Anotar el total de tests en el ledger.

- [x] **Step 2: Verificación orgánica (spec §6.4) — app real contra la base**

Preparación:
- `docker start stockapp-pg` (y dejarlo andando al terminar); API levantada (anotar su URL, p. ej. `http://localhost:5043`, y confirmarla contra `~/.config/GestionMunicipal/conexion.json`); credencial de desarrollo `admin` / `Admin1234`.
- Operador de permisos MIXTOS (reutilizar `f3bmixto` o el de la 3a; anotar su id): `catalogo.productos` y `movimientos.registrar`, sin finanzas ni reportes.
- Catálogo grande: si la base tiene menos de 1000 productos, PREGUNTAR al usuario antes de crear datos; propuesta: crear 1000 productos de prueba con código `PRUEBA-3C1-0001`… por la API (`POST` de productos con el token del admin), uno de ellos con la unidad más larga disponible o un código de 40+ caracteres. Anotar el rango de ids en el ledger.
- WSLg y `scripts/gui-verificacion/` (ventana "Gestión Municipal"). MIRAR cada captura. Tipear ANTES de cada diálogo modal; si después hace falta teclado, reiniciar la app.

Todo a **1366×768**; los pasos 1, 2 y 4 se repiten a 1280×760. Capturar cada pantalla.

1. **Cargando:** con la app en Inicio, pausar SOLO el proceso de la API (`kill -STOP $(pgrep -f 'StockApp.Api' | head -1)`, acotado por nombre; nunca un comando de alcance global) y entrar a Productos → "Cargando productos…" centrado donde va el vacío, sin "No hay productos" ni grilla. Capturar. `kill -CONT` del mismo pid → aparece la grilla. Comparar a ojo con `productos-cargando.html`.
2. **Catálogo grande:** scrollear de punta a punta (rueda y arrastre del pulgar) y ordenar por CÓDIGO, COSTO y UNIDAD: ninguna columna cambia de ancho (capturas al principio y al final para comparar con `comparar-png`). Pasar el mouse por una celda cortada: tooltip con el texto completo; por una que entra: sin tooltip.
3. **Búsqueda:** tipear un nombre: la grilla no parpadea al indicador mientras busca (D2); una búsqueda sin resultados muestra "No hay productos para mostrar" (recién al terminar).
4. **Sin permiso:** iniciar sesión como el operador mixto, ir a Productos. En una terminal, con el token del admin (`curl -s -X POST <url>/auth/login -H 'Content-Type: application/json' -d '{"nombreUsuario":"admin","contrasena":"Admin1234"}' | jq -r .token`), guardar los permisos del operador (`GET <url>/usuarios/<id>/permisos`) y quitarle `catalogo.productos` (`PUT <url>/usuarios/<id>/permisos` con el resto). En la app, "Buscar" → aviso global y "Sin permiso / No tenés permiso para ver los productos." en lugar de la card, nunca "No hay productos". Capturar. Restaurar los permisos guardados con el mismo `PUT` y verificarlo con el `GET`.
5. **Arrastre:** arrastrar el borde de UNIDAD; buscar algo que traiga unidades más largas: la columna no se mueve.
6. Mantenimiento (Admin): "Cargando backups..." sigue apareciendo al entrar y desaparece al cargar (el cambio de `Cargando` a la base no lo rompió).
7. Datos de prueba: dar de baja los productos `PRUEBA-3C1-*` desde la app solo si son pocos; si son 1000, listar el rango de ids y PREGUNTAR al usuario si los borra (comando SQL listo, acotado por ese rango y el prefijo de código, SOLO contra el contenedor local `stockapp-pg`; nunca el VPS). El contenedor queda andando.

Si un paso no se puede hacer por el entorno (WSLg caído o ventana congelada, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa y se retoma cuando diga.

- [x] **Step 3: Aprobación final del usuario**

PARAR. Presentar: el `index.html` (filas de Productos), las capturas de la verificación orgánica junto a los mockups, los rojos de las mutaciones (a) a (o) y los cambios respecto de los mockups aprobados (si hubo). La aprobación final es del usuario mirando la app (spec §6.4). Registrar en el ledger: `Fase 3c-1: aprobada por <quién> <fecha>`.

- [x] **Step 4: `herramientas.md` y spec**

`docs/ui-v2/herramientas.md`, sección "Convenciones de la convivencia (hasta la Fase 9)", después de la 17: "Convenciones que agrega la Fase 3c-1 (patrones comunes)" con la 18, 19 y 20 de este plan (con las D# como quedaron aprobadas: tope, reserva, monotonía, indicador). En la 14, reemplazar la última oración ("Las `Auto` de contenido libre (CÓDIGO, COSTO, UNIDAD) no se fijan: truncarían datos reales.") por: "Las `Auto` de contenido libre miden su contenido sobre todas las filas (convención 19); en el mockup se copia el ancho que mide el componente con los datos fijos."

Spec:
1. Línea `**Estado:**`: agregar "; Fase 3c-1 (patrones comunes + Productos) APROBADA por el usuario el <fecha>" antes de "(merge a main al final de todas las fases)", y "3c-1" a la lista de fases implementadas.
2. §7: en "**Fase 3 partida en 3a, 3b y 3c**", agregar: "La 3c se parte a su vez en 3c-1 (patrones comunes de carga y de columnas `Auto`, aplicados a Productos), 3c-2 (Historial de movimientos) y 3c-3 (Ingreso por factura y el patrón de modal v2) (decisión del usuario, 2026-10-08)."
3. §7, después de "Estado de la Fase 3b": "**Estado de la Fase 3c-1 (aprobada el <fecha>).** Estado de carga (`ViewModelBase.Cargando`, `v2:EstadoCargando`) y estado sin permiso en el listado de Productos; columnas `Auto` de contenido libre medidas sobre todas las filas (`ColumnaAuto`, tope adaptable min(240, espacio libre) sin scroll horizontal, reserva de la flecha de orden, solo crecen mientras vive la grilla) con tooltip solo si se corta (`TooltipSiSeCorta`). Comparado en <N> filas a los dos tamaños (<máximo de teselas densas>). Verificación orgánica a 1366×768 y 1280×760: carga lenta (API pausada), catálogo de <n> productos, 403 en caliente, arrastre. Decisiones del usuario: <D1 a D13 aprobadas o cambiadas>; excepción U1 a §3 (ya asentada en §3)."
4. "Deuda abierta tras la Fase 3a": tachar con `~~…~~` y agregar "Cerrada (Fase 3c-1): …" en: el estado vacío durante la carga/403 (cerrada por U1); las columnas `Auto` de contenido libre (cerrada por U2, con el test de 1200 filas); `GrillasSinRecorteBarridoTests` con `SidebarPx` 232 (cerrada por R1, o "sigue abierta" con el motivo si R1 se paró); `UseLayoutRounding="False"` por instancia en `EstadoVacio` (evaluada: el control nuevo lo lleva en su tema; `EstadoVacio` sigue por instancia para no mover el catálogo).
5. Agregar "**Deuda abierta tras la Fase 3c-1:**" con lo que haya surgido; como mínimo: los ~20 ViewModels con `catch (UnauthorizedAccessException)` propio (no usan el helper: no tienen `Cargando`; cada uno se migra al llegar su listado); la carrera de resultados de dos búsquedas superpuestas (la más vieja puede pisar a la más nueva; preexistente, el contador solo arregla el flag); el formulario de Productos sin indicador (D4); cualquier `UmbralAprobado` registrado (pantalla y número); la suscripción del behavior a la fuente que retiene la grilla mientras viva la colección del VM (si la review la marca).

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 3c-1 (patrones comunes de carga y columnas Auto)"
```

No se pushea ni se mergea.

---

## Fuera de alcance

- **3c-2 y 3c-3 (U3):** `MovimientoHistorialView`, `HistorialPorProductoView`, `IngresoPorFacturaView`, el patrón de modal v2. El componente queda listo para ellos (incluye `DataGridTextColumn`), pero no se aplica a ninguna otra vista.
- **ViewModels:** solo los tres de la Global Constraint. Los ~20 VMs con su propio `catch (UnauthorizedAccessException)` no se migran (no tienen `Cargando` hasta que se migre su listado). La carrera de resultados de búsquedas superpuestas en Productos (preexistente).
- El indicador en el catálogo de componentes (D11) y en el formulario (D4). El sidebar que corta el último ítem a 1366×768 (deuda de la 3a, diferida por el usuario). Modo oscuro, back-stack, cambios de API (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| `Cargando` de la base choca con el `[ObservableProperty] _cargando` de `MantenimientoViewModel` (CS0108; su vista dejaría de ver la carga). | Task 1 Step 3 migra Mantenimiento a la base y re-notifica `MostrarListaVacia`; `grep -c CS0108` en 0; tests de Mantenimiento (VM y View) en la misma task y paso 6 de la verificación orgánica. |
| Una carga que lanza otra excepción deja `Cargando` en true para siempre (indicador colgado). | `finally` con `TerminarCarga()`; test `Cargando_SeApagaAunqueLaCargaLanceOtraExcepcion`; mutación (a). |
| La sonda mide con otra tipografía que la celda (sin `-calt`, sin `+tnum`) y la columna queda 1-2 px corta: elipsis donde no debería. | La sonda copia `FontFeatures` y el resto del `TextBlock` de una celda realizada; datos con "2x1,5" y `$ 98.765.432,10` en `ConMilFilas_…`; `Paridad_…` en grilla de test y en la vista; los asserts de "no cortado" en `Scroll_MasDeMilFilas_…`. |
| Medir 5000+ filas en el hilo de UI congela la pantalla al cargar. | Dedupe por texto, una sola sonda reusada, `CincoMilFilas_MedirNoAgregaCostoPerceptible`; si da rojo se PARA y se consulta (Task 2 Step 3), sin subir el margen a ciegas. |
| La medición pelea con el reparto del behavior viejo (loop de layout) o lo hace pasar por `DataGridColumn.Resize`. | Un solo `LayoutUpdated`: se mide antes del reparto, dentro de `Reparando`, con el mismo `Asignar` (ida y vuelta por `Star`); en píxeles la columna es "fija" para el reparto; regresión completa de `DataGridColumnasAutoAjusteTests` (incluida la guarda anti-loop) en las Tasks 2 y 6. |
| El tope deja a Productos en scroll horizontal cuando una UNIDAD lo alcanza a 1280 (columnas no proporcionales ~719 + pisos 320 > ~950). | RESUELTO por D5 (tope adaptable, GATE 2026-10-08): la columna medida cede al espacio libre (UNIDAD 145 a 1280, 231 a 1366) y el mockup `productos-celda-cortada` ya no tiene scroll. Queda scroll solo si ni con las medidas en su título entran las fijas y los pisos (Productos: grilla interna < 886, ventana de ~1218). |
| La reserva de la flecha cambia el ancho de CÓDIGO y la comparación de `productos.html` aprobada en 3a pasa a rojo. | `productos.html` se re-mide en la Task 4 con el componente y se re-aprueba en el GATE (D6). |
| La elipsis corta en un carácter distinto en Avalonia y en Chromium (teselas densas en `productos-celda-cortada`). | `recortesPermitidos` para el texto; diagnóstico con `teselas.py`/`tinta.py` y decisión del usuario por tesela; nunca subir el umbral sin su OK. |
| El barrido de recortes contradice U2 (un corte en el tope es a propósito) o, con `SidebarPx` 240, destapa recortes reales en vistas viejas. | Excepción acotada con su escenario y mutación (o); R1: si se pone rojo una vista que no es Productos, se PARA y se consulta. |
| La suscripción del behavior a `CollectionChanged` retiene la grilla mientras viva la colección del VM. | VM y vista se recrean en cada navegación (DI transitorio, `ViewLocator`): el ciclo vive lo mismo que la pantalla; se desuscribe al cambiar `ItemsSource` o al apagar el behavior; anotar en la deuda si la review lo marca. |
| `TooltipSiSeCorta` depende de `ToolTipOpening` (cancelable desde Avalonia 11.1): una actualización que lo cambie deja tooltips siempre visibles o nunca. | Tests de efecto (`TextoQueEntra_NoAbreElTooltip`, `TextoCortado_…`) y mutación (h). |
| Provocar una carga lenta y un 403 en la app real (paso 1 y 4 de la verificación orgánica). | `kill -STOP/-CONT` acotado al pid de `StockApp.Api`; permisos quitados y restaurados por la API con el token del admin, guardando antes el estado; todo local. |
| WSLg pierde el teclado tras un modal o congela la ventana al relanzar; los scripts salen con exit 0. | Mirar cada captura; tipear antes de los modales; reiniciar la app si hace falta; capturas idénticas con procesos nuevos = ventana congelada. |
| Un script del skill o una captura queda fuera del worktree. | `cd <worktree> &&` y `ls -l --time-style=full-iso` del archivo resultante (Global Constraints; Task 4 Step 5). |
