# UI v2 — Fase 3c-2 Historial de movimientos Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar el Historial de movimientos a la UI v2 con el patrón de listado (§5.1) y los patrones de la 3c-1 (estado de carga, sin permiso, columnas `Auto` medidas, tooltip solo si se corta), llevar "Recalcular stock" a un diálogo v2, cerrar con un patrón común la carrera de búsquedas superpuestas (Historial y Productos) y comparar todo contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** Tres piezas comunes y una vista. (a) `ConsultaVigente` (contador de turnos) + una sobrecarga de `EjecutarCargaProtegidaAsync` que recibe el turno: una consulta que ya no es la última no toca `Items` ni marca `SinPermiso`; se aplica a Productos y a Historial (U3). (b) Ajustes de componentes que pide Historial y decide el GATE: `BarraFiltros` sin buscador de texto, las columnas medidas vuelven a su ancho medido cuando ni cediendo entran (D5), dos conversores de celda y un guardián nuevo de piezas del tema viejo. (c) `RecalcularStockDialog` (Window v2 del patrón de la Fase 1, sin bindings) servido por `IRecalcularStockDialogService`, como `ClasificacionTareaDialogService` (U1). (d) `MovimientoHistorialView` reescrita en v2. Los mockups se aprueban en un GATE (Bloque B) antes de tocar componentes de UI y XAML (Bloque C).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3 VMs intactos y la excepción `Cargando`; §5.1 listado; §5.5 diálogos; §6 fidelidad y §6.5 compensaciones; §7 "Estado de la Fase 3c-1" y "Deuda abierta tras la Fase 3c-1"; §8.1 red de seguridad: `MovimientoHistorialGatesTests` y `CargaProtegidaSinPermisoGuardianTests` se mantienen). Convenciones 1 a 22: `docs/ui-v2/herramientas.md` (en especial 10, 13, 14, 18 a 22). Plantilla y receta: `docs/superpowers/plans/2026-10-08-ui-v2-fase-3c1-patrones-comunes.md` (Task 4: medición temporal y GATE; Task 6: vista, barrido, comparaciones, mutaciones). Ledger de la 3c-1 (estilo de trabajo, rulings F1 a F8): `.superpowers/sdd/2026-10-08-ui-v2-fase-3c1-patrones-comunes/progress.md`.

Todos los comandos se corren desde la raíz del worktree, con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>` (rama `feat/ui-v2`, base `dd4ac7a`). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Si CodeGraph devuelve un archivo como "changed on disk" sin fuente (al escribir este plan pasó con `MovimientoHistorialViewModel.cs`, `ProductoListViewModel.cs` y `ConfirmacionDialog.axaml.cs`), leerlo con Read y decirlo en el reporte.

## Decisiones del usuario (2026-10-09, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Recalcular stock:** en el encabezado v2 queda SOLO un botón secundario "Recalcular stock", con el mismo gate (`PuedeRecalcularStock`). Abre un diálogo v2 con el buscador de producto y la confirmación. | `RecalcularStockDialog` (Window v2, patrón de `PedirTextoDialog`) + `IRecalcularStockDialogService`; `MovimientoHistorialViewModel` gana `AbrirRecalcularCommand`. NO adelanta el modal v2 de la 3c-3 (ver "Decisiones de diseño del plan", fila "Diálogo de Recalcular"). | 4, 6, 7 |
| U2 | **El volumen sin límite NO se resuelve en la 3c-2.** Va como deuda en la spec con un umbral orientativo y el motivo. No se toca la API. | Task 8 Step 4 (deuda en §7 con umbral de 5000 filas). La verificación orgánica mide el tiempo de carga con el volumen de prueba y lo anota. | 8 |
| U3 | **La carrera de búsquedas superpuestas se cierra en esta fase con un patrón común**, en Historial Y en Productos, con un test que la reproduzca (rojo antes) y verificación por mutación. Convención nueva si sale limpio. | `ConsultaVigente` + sobrecarga de `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` (Task 1); Productos (Task 1) e Historial (Task 2). Convención 23 en `herramientas.md` (Task 8). Excepción a "VMs intactos" asentada en §3 (Task 1). | 1, 2, 8 |
| U4 | **Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE** (criterio de 3a, 3b y 3c-1). | D1 a D15 abajo. Si el usuario cambia una, se ajustan mockups y los tests que la fijan en la Task 4, antes de las Tasks 5 a 7. | 4 |

## Decisiones que se confirman al aprobar los mockups (Task 4)

- **D1 — Encabezado:** `EncabezadoPagina` con título "Historial de movimientos", sin subtítulo ni breadcrumb (es pantalla de sidebar, como Productos). Acciones: SOLO el botón secundario "Recalcular stock" (U1), visible con `PuedeRecalcularStock`. Sin botón primario: la pantalla no tiene una acción principal (convención 10 pide "a lo sumo uno").
- **D2 — Barra de filtros:** `BarraFiltros` SIN el buscador de texto de 280 (propiedad nueva `MostrarBusqueda="False"`; Historial no tiene búsqueda libre) y en su ranura cuatro campos con etiqueta arriba (`v2:CampoFormulario`): Producto (`AutoCompleteBox` de 280, marca de agua "Todos los productos"), Tipo (`ComboBox` de 140), Desde y Hasta (`CalendarDatePicker` de 140, marca de agua "dd/mm/aaaa", con `NormalizarFechaTipeada`), y el botón "Buscar" **secundario** (hoy es `primary`) alineado abajo con los campos. Motivo de las etiquetas: con dos fechas elegidas las marcas de agua desaparecen y "Desde"/"Hasta" ya no se distinguen; "Todos" del combo de Tipo sin etiqueta no dice de qué. Alternativa: sin etiquetas, como Productos (más baja, ambigua).
- **D3 — Filtro de Producto oculto** (`PuedeFiltrarPorProducto` false: sin `catalogo.productos`, o tras un 403 en su búsqueda): el campo desaparece y los demás corren a la izquierda, sin hueco (mismo criterio que la convención 15). No se mockea: se custodia con UiTest (Task 7) y se ve en la verificación orgánica (D12).
- **D4 — Columnas:** las 10 actuales, en su orden, con headers en mayúsculas: FECHA, PRODUCTO, TIPO, MOTIVO, CANTIDAD, PRECIO, STOCK ANT., STOCK NUEVO, USUARIO, COMENTARIO. Medidas con `ColumnaAuto` (convención 19): FECHA, TIPO, MOTIVO, CANTIDAD, PRECIO, STOCK ANT. y USUARIO. STOCK NUEVO fija 220 (insignia + número, como STOCK de Productos). PRODUCTO `2*` y COMENTARIO `3*`, con piso 160. **Consecuencia:** a 1280 y a 1366 las 10 columnas NO entran (hoy tampoco: `DataGridColumnasAutoAjusteTests.G_HistorialEnShellA1366_ProductoYComentarioNoBajanDelPiso_YScrollea` lo fija). Estimación a confirmar en la Task 4 Step 1: medidas ≈ 690, fija 220, pisos 320 ≈ 1230 contra 948 (1280) y 1034 (1366) internos; aun con las medidas en su título no baja de ≈ 1160. La grilla scrollea en horizontal y el mockup dibuja la barra. Alternativa B (se mide en el mismo Step 1 y se muestra en el GATE): fusionar STOCK ANT. y STOCK NUEVO en una sola columna "STOCK" con "anterior → nuevo" e insignia; ahorra ≈ 100 px y sigue sin entrar a 1280.
- **D5 — Si ni cediendo entra, las columnas medidas vuelven a su ancho medido** (texto entero) y la grilla scrollea. Cambia la regla aprobada en la 3c-1 para ese caso (hoy se quedan en el ancho de su título Y hay scroll: deuda abierta "Por debajo de 886 px internos…", que la spec dejó "a consultar con el usuario"). Historial cae en ese caso en sus dos tamaños de trabajo: con la regla actual FECHA se vería "09/10/…" a 1280 y además habría scroll. Cuando ceder SÍ alcanza (Productos a 1280 y 1366, y cualquier grilla que entre), no cambia nada. Lo implementa `DataGridColumnasAutoBehavior.OnLayoutUpdated` (Task 5); cambia el test `ColumnaAutoTopeAdaptableTests.TanAngostaQueNiCediendoEntra_…`. Alternativa: dejar la regla de la 3c-1 (fechas cortadas con scroll).
- **D6 — Stock negativo** (convención 11, 3a): STOCK NUEVO con `v2:Insignia` peligro "Stock negativo" a la izquierda y el número con `Classes.negativo` (`#B91C1C`) a la derecha, en una sola `Grid` (como STOCK de Productos). STOCK ANT. negativo: solo el número en rojo, sin insignia (hoy lo pinta rojo `SignoNegativoBrushConverter`). Alternativa: insignia también en STOCK ANT.
- **D7 — PRECIO "—" cuando el precio es 0.** El servicio guarda 0 cuando el precio no aplica (`MovimientoStockService.cs` L55: toda salida y las entradas sin precio), y hoy la celda dice "$ 0,00", que se lee como "costó cero". Conversor nuevo `MonedaOGuionConverter` (0 → "—", si no `FormatoEsUy.Moneda`), usado por la celda y por `ColumnaAuto.Conversor`. Alternativa: "$ 0,00" como hoy.
- **D8 — Tooltip solo si se corta** (convención 20) en todas las columnas de texto: PRODUCTO y COMENTARIO dejan el tooltip "siempre" y pasan a `TooltipSiSeCorta`; USUARIO y las medidas lo ganan (una medida puede ceder y cortarse cuando la ventana es más ancha que 1366 pero no alcanza).
- **D9 — Textos:** indicador "Cargando movimientos…"; vacío título "No hay movimientos" y mensaje "Probá con otros filtros o con otras fechas."; sin permiso título "Sin permiso" y mensaje "No tenés permiso para ver el historial de movimientos." (el literal de hoy, que pasa a la constante `MovimientoHistorialViewModel.MensajeSinPermisoHistorial`); pie de conteo `movimiento|movimientos` (es-UY, miles con punto).
- **D10 — Diálogo de Recalcular** (U1): `Window` v2 de 400 de ancho, `Classes="ui2"` en el `Border` raíz, padding 24, `StackPanel` con separación 16 (el patrón de `PedirTextoDialog`, Fase 1). Título de ventana "Recalcular stock"; cuerpo "Elegí el producto. Su stock se recalcula a partir de todos sus movimientos."; `AutoCompleteBox` a todo el ancho con marca de agua "Buscar producto…" (búsqueda en el servidor con debounce de 300, como hoy); botones "Cancelar" (secundario) y "Recalcular" (primario, deshabilitado hasta elegir un producto). Al confirmar: recalcula y avisa "Stock recalculado." con el `MensajeDialog` de siempre. Alternativa: overlay dentro de la vista (adelanta el patrón de la 3c-3, ver Riesgos).
- **D11 — Tras recalcular, el listado se recarga con los filtros vigentes.** Hoy `RecalcularAsync` llama `CargarAsync()` (sin filtros): la grilla pasa a mostrar todo el historial mientras los controles siguen mostrando el filtro (hallazgo de este plan). Pasa a `BuscarAsync()`. Alternativa: conservar.
- **D12 — Estados a mockear (a 1280×760 y 1366×768):** `historial` (12 movimientos con una fila de stock negativo y un comentario largo), `historial-cargando`, `historial-sin-permiso`, `historial-vacio`, `historial-celda-cortada` (un producto y un usuario más largos que su columna) y `dialogos/recalcular-stock` (sin producto elegido). No se mockean el tooltip ni el desplegable del `AutoCompleteBox` (popups fuera del área comparada: los custodian UiTests y la verificación orgánica).
- **D13 — Datos fijos:** `DatosHistorial.Movimientos` (12 filas, fechas de setiembre y octubre de 2026, 4 salidas, 2 ajustes, una salida que deja STOCK NUEVO en −3, el comentario "Reparación del alumbrado de la plaza Independencia y del pasaje peatonal frente a la escuela 12." y usuarios "María Pérez" y "Operador de Depósito Central"). Las fechas se construyen como hora LOCAL fija convertida a UTC, así `FechaHoraLocalConverter` muestra lo mismo en cualquier huso (ver Riesgos).
- **D14 — 403 en el buscador del diálogo:** devuelve la lista vacía sin dejar escapar la excepción (hoy `BuscarProductosInternalAsync` no la atrapa y queda sin observar dentro del `AutoCompleteBox`); el aviso global del 403 ya lo explica. Afecta al operador con `stock.recalcular` sin `catalogo.productos` (combinación válida: `RecalcularStock` depende de `RegistrarMovimientos`, no de `GestionarProductos`; preexistente). Alternativa: además un texto en el diálogo.
- **D15 — El catálogo de componentes no cambia:** `MostrarBusqueda` no entra a `componentes/catalogo.html` (movería la línea base de calibración, §6.5).

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10). Tokens de la spec §2: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, texto primario `#1C1917`, texto secundario `#57534E`, peligro texto `#B91C1C`; control 13/400 con alto de línea 16; sidebar 240 (a 1280 quedan 1040 para la vista, a 1366 quedan 1126; grilla interna de Productos: 948 y 1034).
- Listado (§5.1, convención 10): `EncabezadoPagina` + `TarjetaContenido` con `BarraFiltros`, grilla y pie de conteo; conteo, vacío y visibilidad por `Items.Count` con `ConteoConverter` (bindear `Count`, nunca la colección).
- **ViewModels: solo lo que piden U1 y U3** (y la constante de D9 y la recarga de D11). Cambian `ViewModelBase` (sobrecarga de `EjecutarCargaProtegidaAsync` con turno), `ProductoListViewModel` (turnos en carga y búsqueda) y `MovimientoHistorialViewModel` (turnos, búsqueda por el helper, `MensajeSinPermisoHistorial`, `AbrirRecalcularCommand`, recarga con filtros, 403 del buscador). Ningún otro VM. No cambian `ViewLocator`, `INavigationService`, `ErrorConverter`, la lógica de permisos ni ningún code-behind de vista (`MovimientoHistorialView.axaml.cs` sigue con `DataContextChanged → InicializarAsync`). El code-behind del diálogo NUEVO sí existe (los diálogos no tienen bindings, §5.5).
- **`CargaProtegidaSinPermisoGuardianTests` no se toca** (ruling F1 de la 3c-1): reconoce la protección por la llamada literal `EjecutarCargaProtegidaAsync(` en el método de carga o en un método local sin argumentos que llame (`await X()`), y exige cuerpo de bloque. Todo método de carga nuevo se escribe con cuerpo `{ … }` y llama al helper de forma literal.
- **`calt` de Inter apagado en toda la UI v2**: todo `FontFeatures` propio incluye `-calt` (la sonda de `ColumnaAuto` copia los de la celda).
- **`tokens.json` es la fuente de `Tokens.axaml` (generado): nunca editar `Tokens.axaml` a mano.** Esta fase no prevé tokens nuevos. Toda task (y su review) que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- **TDD en cada task:** test primero, correrlo y PEGAR el rojo real, código mínimo, verde.
- **Gates de UI con la View real** (convención 21 y memoria "test de VM no custodia gate de UI"): todo `IsVisible` de un gate se prueba montando la vista real (o el diálogo real), nunca solo con un test de VM. **Gates de permisos con un Operador de permisos MIXTOS** (`SesionFake(RolUsuario.Operador, …)`), nunca con Admin (cortocircuita el OR).
- **La comparación por teselas no ve un glifo ni un borde de 1 px.** Las reglas clave se custodian con UiTests sobre la View real, verificados por mutación QUITANDO: `export SCRATCH=~/.cache/stockapp-ui-v2-fase-3c2 && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`); `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO lo quitado; se corre el test y se PEGA el rojo real en el reporte; se restaura con `cp`; `git diff --no-index` vacío; verde. Nunca se agrega un efecto destructivo para mutar. **NUNCA `git checkout --` ni `git restore`**.
- **Stubs fieles al DTO real:** todo fake/stub de `IMovimientoStockService`, `IProductoService` o un endpoint devuelve los tipos reales (`MovimientoHistorialDto` con sus 13 campos posicionales, `ProductoDto`, `RecalculoResultadoDto`) y lanza las excepciones que lanza `ApiErrores` (`UnauthorizedAccessException` para 401/403). Antes de escribir uno, abrir `src/StockApp.Application/Movimientos/Dtos.cs` y confirmar los campos.
- **Compensaciones conocidas Chromium/Avalonia** (spec §6.5): Chromium colapsa márgenes verticales entre bloques y Avalonia los suma (entre ítems flex NO hay colapso); un `Grid` con `MaxWidth` y `UseLayoutRounding` da +1 px; texto centrado con ancho fraccionario pide `UseLayoutRounding="False"` (los `EstadoVacio` de la vista lo llevan por instancia, como en Productos); texto con alto de línea fijo toma `LineHeight` de un token.
- Mockups (§6.1): `docs/ui-v2/mockups/inventario/historial*.html` y `docs/ui-v2/mockups/dialogos/recalcular-stock.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosHistorial`. Solo colores sólidos, bordes de 1 px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. Sin scroll vertical de página. Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (11 teselas densas, 1,68 %) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado` tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto. Diagnóstico con `teselas.py` y `tinta.py` (plan de la 3a, "Herramientas de diagnóstico"; copiarlos a `$SCRATCH/diag/`, nunca se commitean).
- **Convención 6:** las pantallas se comparan DENTRO del shell real (`ShellMainView`, `CurrentContent` = el VM, resuelto por `ViewLocator`) con el sidebar oculto en ambos renders; los diálogos, con `ContenidoDeDialogo.Extraer` (como `DialogosComparacionTests`).
- Formato es-UY: importes por `FormatoEsUy.Moneda`, cantidades por `CantidadConverter`, fechas "dd/MM/yyyy HH:mm" con `CultureInfo.InvariantCulture` sobre la hora local. La medición usa el MISMO conversor de la celda (`ColumnaAuto.Conversor`).
- Los comentarios de `.axaml` y `.csproj` NO pueden contener `--` (AVLN1001, cascadea a MSB4025). En vistas con `x:DataType` un binding mal escrito es error de build; los `DataTemplate` de celda llevan `x:DataType="dto:MovimientoHistorialDto"`.
- **Build obligatorio** tras cada cambio de XAML o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Testear con la solución o con UN `.csproj` por comando. **Nunca dos `dotnet test` a la vez** (comparten `obj/` y el servidor de build: rojos falsos).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos (confirmar por nombre). Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 3", "D5", "U3" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits, la spec y `herramientas.md` sí pueden citar el plan.
- **Scripts del skill de ejecución** (`task-brief`, `review-package`): se corren con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y se verifica con `ls -l --time-style=full-iso` que el archivo quedó DENTRO del worktree y con fecha de ahora.
- Verificación GUI: la ventana se llama "Gestión Municipal". `capturar.sh` NO es fail-closed: mirar CADA captura. WSLg pierde el teclado tras un diálogo modal de la app; `escribir.sh` no manda Tab/Enter/Escape ni borra texto (usar clicks); capturas idénticas con procesos nuevos = ventana congelada. **Si el usuario necesita la PC, la verificación GUI se pausa.**
- Commits: conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). Prohibido push, merge, tocar `main`, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` no se mergea a `main` hasta terminar todas las fases.
- **Línea base de la suite: 5350 tests** (`dotnet test StockApp.sln` en `dd4ac7a`). Cada task anota en el ledger el total tras su commit; el total solo sube (un test heredado adaptado no se borra, convención 13).
- Las referencias de línea (`L123`) son las del commit `dd4ac7a`. Después de que una task edita un archivo, ubicar el lugar por el texto citado.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell real), 10 (listado v2), 11 (negativo por token, nunca `Opacity`), 13 (tests heredados se adaptan sin borrarse), 14 (anchos `Auto` medidos en headless antes del mockup), 17 (diálogo con textos propios), 18 (estado de carga), 19 (`ColumnaAuto`), 20 (`TooltipSiSeCorta`), 21 (permisos en caliente, probado con operador mixto y `ShellMainView`), 22 (carga que falla: `Reintentar`; NO aplica a Historial: un listado con carga fallida por servidor caído muestra el modal global como Productos, ver "Fuera de alcance").

## Convenciones que agrega esta fase (para `herramientas.md`, Task 8)

23. **Solo la última consulta decide lo que muestra un listado.** Cada método que reemplaza `Items` con el resultado del servidor pide un turno (`var turno = _consultas.Empezar();`, `ConsultaVigente` por VM) ANTES de llamar al servicio, pasa el turno a `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` y, después del `await`, aplica el resultado solo si `turno.EsLaUltima`. Una consulta vieja que termina tarde no toca `Items` ni marca `SinPermiso`; `Cargando` sigue siendo el contador de la convención 18 (no cambia). Se prueba con dos `TaskCompletionSource` que se completan en orden inverso (la nueva primero) y se verifica por mutación quitando el `if`.
24. **Diálogo v2 con búsqueda:** `Window` sin bindings (acceso por `x:Name`, §5.5) del patrón de `PedirTextoDialog` (400 de ancho, `Border` raíz con `Classes="ui2"` y padding 24), servido por un servicio de diálogo propio (`IRecalcularStockDialogService`, como `IClasificacionTareaDialogService`) que recibe el buscador del VM (`Func<string?, CancellationToken, Task<IEnumerable<object>>>`) y devuelve el DTO elegido o null. El botón afirmativo se habilita solo con una selección. Se compara con `ContenidoDeDialogo.Extraer` y entra a `GuardianDePatronTests.DialogosV2`.
- La 19 gana una frase (D5): "Si ni con las medidas en su título y las `*` en su piso entra, las medidas vuelven a su ancho medido (texto entero) y la grilla scrollea: nunca se corta una columna medida si igual va a haber scroll."
- La 10 gana una frase (D2): "Un listado sin búsqueda libre usa `BarraFiltros MostrarBusqueda="False"` y pone sus filtros con etiqueta (`CampoFormulario`) en la ranura."

## Review Focus

1. **La búsqueda vieja pisa a la nueva.** Con un historial grande, el usuario entra (carga completa lenta), filtra por un producto y aprieta Buscar: la búsqueda filtrada vuelve primero y la carga completa, al llegar, la pisa; la grilla muestra todo con el filtro a la vista. Lo mismo en Productos (debounce contra la recarga tras Eliminar). Esperado: gana la última consulta pedida, sin importar el orden de llegada; una vieja que termina en 403 no deja "Sin permiso" sobre resultados buenos. Tests: `ConsultaVigenteTests` (Task 1), `CargaYBusquedaSuperpuestas_LaBusquedaNuevaGanaAunqueLaCargaVuelvaDespues` y `Un403DeUnaConsultaVieja_NoTapaLosResultadosDeLaNueva` (Productos, Task 1; Historial, Task 2), y `BuscarConLaCargaInicialEnCurso_LaGrillaQuedaConLaBusqueda` sobre la View real (Task 7).
2. **"No hay movimientos" mientras carga, tras un 403 o tras una búsqueda que falla.** Esperado: "Cargando movimientos…" sin grilla ni vacío durante la carga sin filas; "Sin permiso" en lugar de la card tras un 403 en la carga O en Buscar (hoy un 403 en Buscar escapa al manejador global); el vacío solo con la carga terminada bien y sin filas. Tests: `Buscar_SiElServicioLanzaUnauthorized_NoPropagaYMarcaSinPermiso` (Task 2), `Cargando_SinFilas_…`, `SinPermiso_EnLaCarga_…`, `SinPermiso_EnUnaBusqueda_…`, `AlTerminarLaCargaSinMovimientos_…` (Task 7).
3. **Una columna se mueve o una fecha se corta con miles de movimientos.** Esperado: con 1200 movimientos y el valor más ancho de cada columna medida en la última fila, ningún ancho cambia al scrollear hasta el final ni al ordenar por cada columna; a 1280 y 1366 FECHA, CANTIDAD, PRECIO y USUARIO se leen enteros y la grilla scrollea en horizontal (D4/D5). Tests: `Scroll_MasDeMilMovimientos_HastaElFinal_NoMueveNingunaColumna`, `Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo`, `A1280y1366_LasMedidasSeLeenEnterasYLaGrillaScrollea` (Task 7), `TanAngostaQueNiCediendoEntra_LasMedidasVuelvenASuAnchoMedido_…` (Task 5).
4. **Un operador sin `stock.recalcular` ve "Recalcular stock", o uno al que se lo quitan en caliente lo sigue viendo.** Esperado: botón oculto sin el permiso (Operador mixto), visible con él, y oculto tras `NotificarPermisosActualizados` sin recrear la vista; el diálogo no se abre si el VM no puede recalcular. Tests: `MovimientoHistorialGatesTests` adaptados (Task 7), `RevocarRecalcularStock_EnCaliente_OcultaElBotonSinRecrearLaVista` (Task 7), `AbrirRecalcular_SinPermiso_NoAbreElDialogo` (Task 6).
5. **El diálogo recalcula algo que el usuario no eligió, o se cierra sin decir nada.** Esperado: "Recalcular" deshabilitado hasta elegir un producto; Cancelar no recalcula; al confirmar recalcula ESE producto, recarga con los filtros vigentes (D11) y avisa "Stock recalculado."; un 403 en la búsqueda del diálogo no deja una excepción sin observar (D14). Tests: `RecalcularStockDialogTests` (Task 6), `AbrirRecalcular_EligeUnProducto_RecalculaEseYRecargaConLosFiltros`, `AbrirRecalcular_Cancela_NoRecalcula`, `BuscarProductos_Un403_DevuelveVacioSinPropagar` (Task 6).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit `dd4ac7a`) |
|---|---|---|
| Diálogo de Recalcular: ¿adelanta el modal v2 de la 3c-3? | **No.** "El patrón de modal v2" de la 3c-3 es el de los dos overlays DENTRO de `IngresoPorFacturaView` (alta rápida de producto y confirmación de precios: `Border` con fondo `#AA000000` sobre la vista, bindeado a `MostrandoAltaProducto`/`MostrandoConfirmacionPrecios` del VM), que no tienen mockup ni versión v2. Los diálogos `Window` ya son v2 desde la Fase 1 (`ConfirmacionDialog`, `MensajeDialog`, `PedirTextoDialog`: `Classes="ui2"`, mockups `docs/ui-v2/mockups/dialogos/*.html`, `DialogosComparacionTests`, `GuardianDePatronTests.DialogosV2`). Recalcular reusa ese patrón (Window + servicio, como `ClasificacionTareaDialogService`) y no define nada del overlay. Lo que SÍ estrena: el primer diálogo v2 con un `AutoCompleteBox` y con un servicio propio en v2 (convención 24). Si en la 3c-3 se decide pasar los overlays a `Window`, este diálogo es el precedente; si se decide un overlay v2, este no cambia. | `IngresoPorFacturaView.axaml` L269-320; `PedirTextoDialog.axaml` L1-38; `ClasificacionTareaDialogService.cs` L20-64; spec §7 ("3c-3 (Ingreso por factura y el patrón de modal v2)"), §5.5. |
| Diálogo sin bindings | `RecalcularStockDialog` recibe el buscador por constructor y lo asigna en code-behind a `AutoCompleteBox.AsyncPopulator`; el resultado sale por `Close(producto)`. Mantiene la regla §5.5 ("Los diálogos siguen sin bindings") y evita un VM de diálogo. | spec §5.5; `ConfirmacionDialog.axaml.cs` L22-40. |
| Cambio de VM por U1 | Aditivo: `AbrirRecalcularCommand` abre el diálogo, asigna `ProductoSeleccionadoParaRecalcular` y ejecuta el `RecalcularCommand` de siempre. `RecalcularAsync`, `ProductoIdParaRecalcular` y su `CanExecute` quedan (los usan 12 tests de VM). Constructor gana `IRecalcularStockDialogService` (6.º parámetro): 2 sitios de test lo construyen (`MovimientoHistorialViewModelTests.Crear` L79, `MovimientoHistorialGatesTests.Montar` L104); DI lo resuelve solo (`App.axaml.cs` L332 transient; el servicio se registra singleton junto a L291). | `MovimientoHistorialViewModel.cs` L57-68, L166-185, L332, L348-367. |
| Carrera: por qué un turno y no un `CancellationToken` | Los servicios (`IProductoService.BuscarPorTextoAsync`, `IMovimientoStockService.ObtenerHistorialAsync`) no reciben token: cancelar no corta el HTTP y cambiar las interfaces toca la API y los clientes (fuera de alcance). Lo que importa es no APLICAR un resultado viejo: un contador de turnos por VM alcanza, sin tocar interfaces. El debounce de Productos (L109-133) solo cancela su `Task.Delay`: no cubre la carrera contra la carga inicial ni contra la recarga tras Eliminar (L200). | `ProductoListViewModel.cs` L93-153, L200; `MovimientoHistorialViewModel.cs` L272-309. |
| Carrera: 403 de una consulta vieja | Se descarta (la sobrecarga lo atrapa con `when (!turno.EsLaUltima)`): si no, una consulta vieja rechazada pondría "Sin permiso" sobre los resultados buenos de la nueva. El aviso global del 403 ya salió por `AuthTokenHandler` y los permisos se refrescan igual (B3). | `ViewModelBase.cs` L182-198. |
| Carrera y el guardián | La sobrecarga se llama igual (`EjecutarCargaProtegidaAsync`), así la regex `\bEjecutarCargaProtegidaAsync\s*\(` del guardián la reconoce sin tocarlo (F1 de la 3c-1). En Historial, `InicializarAsync` llama `await CargarAsync();` (local sin argumentos, el guardián lo sigue) y `CargarAsync` llama al helper literal. | `CargaProtegidaSinPermisoGuardianTests.cs` L109, L130-133, L168-186. |
| `BuscarAsync` de Historial por la carga protegida | **Sí.** Hoy un 403 en Buscar escapa del `AsyncRelayCommand` al manejador global, la grilla queda con los datos viejos y no hay `Cargando`. Por el helper gana `Cargando` (convención 18), `SinPermiso` (la card se reemplaza) y el turno (U3). Sigue siendo `ComandoDeGuardado` (filtra con lo tipeado: fechas). | `MovimientoHistorialViewModel.cs` L295-309; `ViewModelBase.cs` L26-29 (regla de `ComandoDeGuardado`). |
| Columnas que hoy son `DataGridTextColumn` | Pasan a `DataGridTemplateColumn` con `SortMemberPath` (como Productos): la convención 19/20 pide `TextTrimming`, `ToolTip.Tip` y `TooltipSiSeCorta` en el `TextBlock` de la celda, que una `DataGridTextColumn` no expone. FECHA ordena por `Fecha` (DateTime: orden cronológico, igual que hoy) y muestra `FechaHoraLocalConverter` (string "dd/MM/yyyy HH:mm" de la hora local); hoy es `FechaUtcALocalConverter` (devuelve DateTime) + `StringFormat`, que `ColumnaAuto.Ruta`+`Conversor` no puede reproducir. | `MovimientoHistorialView.axaml` L133-151, L184; `FechaUtcALocalConverter.cs` L30-33; `ColumnaAuto.cs` (`Evaluador`). |
| Ancho de la grilla de Historial | No entra a 1366 hoy (test `G_HistorialEnShellA1366_…_YScrollea`). Por eso D4 y D5. La Task 4 Step 1 mide con el componente a los dos tamaños y deja los números en el ledger antes de dibujar. | `DataGridColumnasAutoAjusteTests.cs` L511-523. |
| D5 en el behavior | En `OnLayoutUpdated`, después de `CederMedidas` y de calcular `encaja`: si no encaja, los objetivos de las medidas vuelven a `Math.Min(c.MaxWidth, estado.AnchosMedidos[c])`. Depende solo de lo medido y del ancho de la grilla (sin loops, igual que hoy). | `DataGridColumnasAutoBehavior.cs` L470-492, L494-592 (`CederMedidas`, `encaja`). |
| Guardián de piezas del tema viejo | Nuevo `GuardianDePatronTests.VistaV2_NoUsaPiezasDelTemaViejo` sobre el TEXTO del `.axaml` de cada vista de `VistasMigradasAV2` (como `DialogoV2_NoTieneOpacidadLiteralEnSuXaml`): sin `SignoNegativoBrushConverter`, `ActivoOpacidadConverter`, `c:BadgeEstado`, `c:HeaderVista` ni `c:EstadoVacio`. Ve las plantillas de celda (el guardián de opacidad por árbol visual no: monta sin VM). Hoy las 7 vistas v2 con `.axaml` dan 0 coincidencias (verificado con grep). | `GuardianDePatronTests.cs` L207-221, L300-325, L477-505. |
| Tests heredados que buscan la vista vieja | Se adaptan (convención 13): headers en mayúsculas en `MovimientoHistorialViewStockOrdenTests`, `DataGridColumnasAutoAjusteTests`, `DataGridCeldaNumericaOrdenTests`, `GrillasSinRecorteBarridoTests`; `BadgeEstado` → `Insignia` en `SignoNegativoBadgeTests`; "Producto a recalcular:" sale de `MovimientoHistorialGatesTests` (el campo vive ahora en el diálogo, su gate es el del botón). `GuardianDePatronTests`: Historial sale de las `[InlineData]` de `Vista_TieneHeaderVistaConElTituloEsperado` (L48) y de `VistasDeLaTanda` (L102) y entra a `VistasMigradasAV2`. | archivos citados. |
| `GrillaEspacioDerechoVistaRealTests` | Monta Historial "como la monta el shell, SIN `.ui2`" para custodiar `Themes/DataGrid.axaml` (tema viejo). Con Historial en v2 esa premisa cae: el guardián se muda a `HistorialPorProductoView` (v1, misma clase de grilla), columna "Comentario" (`Width="*"`, plantilla con texto), conservando sus asserts y topes. Si esa columna no corta con elipsis (precondición del propio test), PARAR y consultar antes de elegir otra vista. | `GrillaEspacioDerechoVistaRealTests.cs` L20-60; `HistorialPorProductoView.axaml` L80-125. |
| Datos de prueba y huso horario | `FechaHoraLocalConverter` pasa a hora local: el mockup no puede depender del huso de la máquina que corre los tests. `DatosHistorial` arma cada `Fecha` como `new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Local).ToUniversalTime()`, así la celda siempre dice "07/10/2026 14:35". | `FechaUtcALocalConverter.cs` L30-33. |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/ViewModels/ConsultaVigente.cs` | Turnos de consulta: solo la última aplica su resultado (U3). |
| `src/StockApp.Presentation/Converters/FechaHoraLocalConverter.cs` | UTC → texto "dd/MM/yyyy HH:mm" de la hora local. |
| `src/StockApp.Presentation/Converters/MonedaOGuionConverter.cs` | 0 → "—"; otro → `FormatoEsUy.Moneda` (D7). |
| `src/StockApp.Presentation/Services/IRecalcularStockDialogService.cs`, `RecalcularStockDialogService.cs` | Abre el diálogo de Recalcular sobre la ventana principal. |
| `src/StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml` (+ `.axaml.cs`) | Diálogo v2 con buscador de producto (D10). |
| `tests/StockApp.Presentation.Tests/ViewModels/ConsultaVigenteTests.cs` | El contador de turnos. |
| `tests/StockApp.Presentation.Tests/Converters/FechaHoraLocalConverterTests.cs`, `MonedaOGuionConverterTests.cs` | Conversores. |
| `tests/StockApp.Presentation.UiTests/V2/RecalcularStockDialogTests.cs` | El diálogo real: botones, habilitación, resultado. |
| `tests/StockApp.Presentation.UiTests/V2/MovimientoHistorialViewV2Tests.cs` | La vista real: estados, columnas, scroll, orden, carrera, gates en caliente. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorial.cs`, `DatosHistorialTests.cs`, `HistorialComparacionTests.cs` | Datos fijos y comparaciones. |
| `docs/ui-v2/mockups/inventario/historial.html`, `historial-cargando.html`, `historial-sin-permiso.html`, `historial-vacio.html`, `historial-celda-cortada.html`; `docs/ui-v2/mockups/dialogos/recalcular-stock.html` | Mockups (D12). |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/ViewModelBase.cs` | Sobrecarga `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)`. |
| `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` | Turnos en `CargarAsync` y `EjecutarBusquedaAsync`. |
| `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs` | Turnos, búsqueda protegida, `MensajeSinPermisoHistorial`, `AbrirRecalcularCommand`, recarga con filtros, 403 del buscador. |
| `src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs` | D5: las medidas vuelven a su ancho si no encaja. |
| `src/StockApp.Presentation/Controls/V2/BarraFiltros.cs`, `ComponentesV2.axaml` | `MostrarBusqueda` (D2). |
| `src/StockApp.Presentation/Views/Movimientos/MovimientoHistorialView.axaml` | Reescrita en v2 (el `.axaml.cs` NO cambia). |
| `src/StockApp.Presentation/App.axaml.cs` | Registro de `IRecalcularStockDialogService`. |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-barra-campos` (espejo de la barra con etiquetas) y `.v2-grilla-scroll-h` (barra de scroll horizontal de la grilla), si el Step 1 de la Task 4 confirma el scroll. |
| Tests heredados | `MovimientoHistorialViewModelTests`, `ProductoViewModelTests`, `MovimientoHistorialGatesTests`, `MovimientoHistorialViewStockOrdenTests`, `DataGridColumnasAutoAjusteTests`, `DataGridCeldaNumericaOrdenTests`, `DataGridSortClickTests` (si busca headers por texto), `GrillasSinRecorteBarridoTests`, `SignoNegativoBadgeTests`, `GuardianDePatronTests`, `ColumnaAutoTopeAdaptableTests`, `BarraFiltros`/componentes, `DialogosComparacionTests`, `VisualTests/GrillaEspacioDerechoVistaRealTests`. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | §3 excepción U3 y U1 (Tasks 1 y 6); Estado, §7 y deuda (Task 8). |
| `docs/ui-v2/herramientas.md` | Convenciones 23 y 24; frases nuevas en 10 y 19 (Task 8). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de grillas (correr entera tras las Tasks 5 y 7): `--filter "FullyQualifiedName~DataGridColumnasAutoAjusteTests|FullyQualifiedName~DataGridSortClickTests|FullyQualifiedName~DataGridCeldaNumericaOrdenTests|FullyQualifiedName~DataGridV2|FullyQualifiedName~GrillasSinRecorteBarridoTests|FullyQualifiedName~GrillasEspacioDerechoBarridoTests|FullyQualifiedName~DataGridColumnasResizeDeshabilitadoGuardTests|FullyQualifiedName~ColumnaAuto|FullyQualifiedName~ProductoListView|FullyQualifiedName~MovimientoHistorial|FullyQualifiedName~SignoNegativoBadgeTests"`
- Comparación con página: `scripts/ui-v2/comparar.sh Historial` (y `RecalcularStock`) y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-09-ui-v2-fase-3c2-historial/progress.md` (lo crea este plan con BASE `dd4ac7a`; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque A — Patrones comunes y ViewModels, sin tocar vistas

### Task 1: `ConsultaVigente` y Productos sin carrera de búsquedas

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/ConsultaVigente.cs`
- Create: `tests/StockApp.Presentation.Tests/ViewModels/ConsultaVigenteTests.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ViewModelBase.cs` (después de `EjecutarCargaProtegidaAsync`, L182-198)
- Modify: `src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs` (L35, L93-102, L144-153)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/ViewModelBaseCargaProtegidaTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs` (clase `ProductoListViewModelTests`)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3, excepción U3)

**Interfaces:**
- Consumes: `ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task>, string)`, `SinPermiso`, `Cargando`.
- Produces: `public sealed class ConsultaVigente { public Turno Empezar(); public readonly struct Turno { public bool EsLaUltima { get; } } }`; `protected Task ViewModelBase.EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso, ConsultaVigente.Turno turno)`.

- [ ] **Step 1: Tests del contador (rojo)**

`tests/StockApp.Presentation.Tests/ViewModels/ConsultaVigenteTests.cs`:

```csharp
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ConsultaVigenteTests
{
    [Fact]
    public void UnaSolaConsulta_EsLaUltima()
    {
        var consultas = new ConsultaVigente();

        var turno = consultas.Empezar();

        Assert.True(turno.EsLaUltima);
    }

    [Fact]
    public void AlEmpezarOtra_LaAnteriorDejaDeSerLaUltima_YLaNuevaLoEs()
    {
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();

        var nueva = consultas.Empezar();

        Assert.False(vieja.EsLaUltima);
        Assert.True(nueva.EsLaUltima);
    }

    [Fact]
    public void DosContadores_NoSeMezclan()
    {
        var a = new ConsultaVigente();
        var b = new ConsultaVigente();
        var turnoA = a.Empezar();

        b.Empezar();

        Assert.True(turnoA.EsLaUltima);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ConsultaVigenteTests"`
Expected: FAIL de compilación (`The type or namespace name 'ConsultaVigente' could not be found`). Pegar el rojo en el reporte.

- [ ] **Step 2: `ConsultaVigente`**

`src/StockApp.Presentation/ViewModels/ConsultaVigente.cs`:

```csharp
using System.Threading;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Turnos de las consultas que reemplazan el contenido de un listado. Dos consultas pueden superponerse (la carga
/// inicial de un historial grande y una búsqueda filtrada; el debounce de la búsqueda y la recarga tras eliminar)
/// y el servidor puede responder en otro orden: sin esto, la más vieja pisa a la más nueva y la grilla muestra
/// algo que no es lo que el usuario pidió. Cada consulta pide su turno ANTES de llamar al servicio y, al volver,
/// aplica su resultado solo si ninguna otra empezó después. Los servicios no reciben CancellationToken: lo que se
/// descarta es el resultado, no el pedido HTTP.
/// </summary>
public sealed class ConsultaVigente
{
    private int _ultima;

    /// <summary>Empieza una consulta: desde ahora las anteriores dejan de ser la última.</summary>
    public Turno Empezar() => new(this, Interlocked.Increment(ref _ultima));

    /// <summary>El turno de una consulta.</summary>
    public readonly struct Turno
    {
        private readonly ConsultaVigente _consultas;
        private readonly int _numero;

        internal Turno(ConsultaVigente consultas, int numero)
        {
            _consultas = consultas;
            _numero = numero;
        }

        /// <summary>True si ninguna consulta empezó después de esta: su resultado es el que hay que mostrar.</summary>
        public bool EsLaUltima => Volatile.Read(ref _consultas._ultima) == _numero;
    }
}
```

Run: el comando del Step 1. Expected: PASS (3).

- [ ] **Step 3: Tests de la sobrecarga del helper (rojo)**

Al final de `ViewModelBaseCargaProtegidaTests` (su `VmDePrueba` expone `EjecutarAsync(Func<Task>, string)`; agregarle `public Task EjecutarAsync(Func<Task> cargar, string mensaje, ConsultaVigente.Turno turno) => EjecutarCargaProtegidaAsync(cargar, mensaje, turno);`):

```csharp
    [Fact]
    public async Task ConTurno_Un403DeUnaConsultaVieja_NoMarcaSinPermiso()
    {
        var vm = new VmDePrueba();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        consultas.Empezar();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "x", vieja);

        Assert.False(vm.SinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task ConTurno_Un403DeLaUltima_MarcaSinPermiso()
    {
        var vm = new VmDePrueba();
        var turno = new ConsultaVigente().Empezar();

        await vm.EjecutarAsync(() => throw new UnauthorizedAccessException(), "No tenés permiso.", turno);

        Assert.True(vm.SinPermiso);
        Assert.Equal("No tenés permiso.", vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ConTurno_OtraExcepcion_SePropagaAunqueLaConsultaSeaVieja()
    {
        var vm = new VmDePrueba();
        var consultas = new ConsultaVigente();
        var vieja = consultas.Empezar();
        consultas.Empezar();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            vm.EjecutarAsync(() => throw new InvalidOperationException("bug"), "x", vieja));
        Assert.False(vm.Cargando);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ViewModelBaseCargaProtegidaTests"`
Expected: FAIL de compilación (`No overload for method 'EjecutarCargaProtegidaAsync' takes 3 arguments`).

- [ ] **Step 4: Sobrecarga en `ViewModelBase`**

Después de `EjecutarCargaProtegidaAsync(Func<Task>, string)` (cierra en L198):

```csharp
    /// <summary>
    /// Igual que <see cref="EjecutarCargaProtegidaAsync(Func{Task}, string)"/> para una consulta que reemplaza el
    /// contenido de un listado (ver <see cref="ConsultaVigente"/>): un 403 de una consulta que ya no es la última se
    /// descarta (si no, "Sin permiso" taparía los resultados buenos de la consulta nueva; el aviso global del 403
    /// ya salió por AuthTokenHandler). El que llama sigue siendo responsable de no aplicar un resultado viejo:
    /// después del await al servicio, <c>if (!turno.EsLaUltima) return;</c>.
    /// </summary>
    protected async Task EjecutarCargaProtegidaAsync(Func<Task> cargar, string mensajeSinPermiso, ConsultaVigente.Turno turno)
    {
        await EjecutarCargaProtegidaAsync(async () =>
        {
            try
            {
                await cargar();
            }
            catch (UnauthorizedAccessException) when (!turno.EsLaUltima)
            {
            }
        }, mensajeSinPermiso);
    }
```

Run: el comando del Step 3. Expected: PASS (los 3 nuevos y los 10 de la 3c-1).

- [ ] **Step 5: La carrera en Productos (rojo ANTES de tocar el VM)**

En `ProductoListViewModelTests` (helper `CrearProductoDto(id, codigo, nombre)` existente; constructor de 4 parámetros):

```csharp
    [Fact]
    public async Task CargaYBusquedaSuperpuestas_LaBusquedaNuevaGanaAunqueLaCargaVuelvaDespues()
    {
        var carga = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var busqueda = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarAsync(null, null, null)).Returns(carga.Task);
        svc.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>())).Returns(busqueda.Task);
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<StockApp.Application.Interfaces.ICurrentSession>());

        var t1 = vm.CargarAsync();
        var t2 = vm.BuscarCommand.ExecuteAsync(null);
        busqueda.SetResult(new List<ProductoDto> { CrearProductoDto(9, "P009", "Buscado") });
        await t2;
        carga.SetResult(new List<ProductoDto> { CrearProductoDto(1, "P001", "Uno"), CrearProductoDto(2, "P002", "Dos") });
        await t1;

        Assert.Equal(new[] { 9 }, vm.Items.Select(p => p.Id));
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Un403DeUnaConsultaVieja_NoTapaLosResultadosDeLaNueva()
    {
        var carga = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarAsync(null, null, null)).Returns(carga.Task);
        svc.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>()))
            .ReturnsAsync(new List<ProductoDto> { CrearProductoDto(9, "P009", "Buscado") });
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<StockApp.Application.Interfaces.ICurrentSession>());

        var t1 = vm.CargarAsync();
        await vm.BuscarCommand.ExecuteAsync(null);
        carga.SetException(new UnauthorizedAccessException("403"));
        await t1;

        Assert.False(vm.SinPermiso);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task DosBusquedasEnOrdenInverso_GanaLaUltimaPedida()
    {
        var primera = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<ProductoDto>>();
        var svc = new Mock<IProductoService>();
        svc.Setup(s => s.BuscarPorTextoAsync("ce")).Returns(primera.Task);
        svc.Setup(s => s.BuscarPorTextoAsync("cemento")).Returns(segunda.Task);
        var vm = new ProductoListViewModel(svc.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>(),
            Mock.Of<StockApp.Application.Interfaces.ICurrentSession>());

        vm.FiltroBusqueda = "ce";
        var t1 = vm.BuscarCommand.ExecuteAsync(null);
        vm.FiltroBusqueda = "cemento";
        var t2 = vm.BuscarCommand.ExecuteAsync(null);
        segunda.SetResult(new List<ProductoDto> { CrearProductoDto(1, "P001", "Cemento portland") });
        await t2;
        primera.SetResult(new List<ProductoDto> { CrearProductoDto(1, "P001", "Cemento portland"), CrearProductoDto(2, "P002", "Cerámica") });
        await t1;

        Assert.Equal(new[] { 1 }, vm.Items.Select(p => p.Id));
    }
```

Nota para el implementador: `BuscarCommand` es `ComandoDeGuardado` (un `AsyncRelayCommand` sin ejecuciones concurrentes): si `t2` no llega a ejecutar porque `t1` sigue en curso, `DosBusquedasEnOrdenInverso_…` se escribe disparando la segunda por el debounce (`vm.FiltroBusqueda = "cemento"; await vm._tareaDebounce;` ya es `internal`) en vez del comando, y se anota en el reporte. Asignar `FiltroBusqueda` también arranca un debounce: los `Setup` por texto exacto hacen que esas llamadas extra devuelvan la misma tarea; no cambian el resultado.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ProductoListViewModelTests"`
Expected: FAIL en los TRES nuevos (la carga vieja deja 2 ítems; el 403 viejo marca `SinPermiso`; la primera búsqueda deja 2 ítems). **Pegar los tres rojos**: son la reproducción de U3.

- [ ] **Step 6: Turnos en `ProductoListViewModel`**

1. Campo, después de `_debounceCts` (L35): `private readonly ConsultaVigente _consultas = new();`
2. `CargarAsync` (L93-102):

```csharp
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.BuscarAsync(null, null, null);
            if (!turno.EsLaUltima)
                return;
            Items.Clear();
            foreach (var p in resultados)
                Items.Add(p);
        }, MensajeSinPermisoListado, turno);
    }
```

3. `EjecutarBusquedaAsync` (L144-153): la misma forma, con `_service.BuscarPorTextoAsync(FiltroBusqueda)`.
4. Agregar al `<summary>` de las dos: "Pide su turno antes de consultar: si mientras espera empezó otra consulta (búsqueda, recarga), su resultado se descarta (ver ConsultaVigente)."

Run: el comando del Step 5 y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CargaProtegida|FullyQualifiedName~ProductoListView"`
Expected: PASS (incluidos `CargaProtegidaSinPermisoGuardianTests` y los 3 nuevos).

- [ ] **Step 7: Mutaciones (QUITANDO)**

- (a) En `ProductoListViewModel.CargarAsync`, quitar las dos líneas `if (!turno.EsLaUltima) return;` → rojo en `CargaYBusquedaSuperpuestas_…`. Pegar. Restaurar.
- (b) En `ProductoListViewModel.EjecutarBusquedaAsync`, quitar las dos líneas `if (!turno.EsLaUltima) return;` → rojo en `DosBusquedasEnOrdenInverso_…`. Pegar. Restaurar.
- (c) En la sobrecarga de `ViewModelBase`, quitar el `try`/`catch … when` (dejar `await cargar();` solo dentro de la lambda) → rojo en `ConTurno_Un403DeUnaConsultaVieja_…` y `Un403DeUnaConsultaVieja_NoTapaLosResultadosDeLaNueva`. Pegar. Restaurar.

- [ ] **Step 8: Excepción U3 en la spec §3**

En §3, al final del bullet "Se reescribe el XAML de cada vista…" agregar: "; y la carrera de consultas (decisión del usuario 2026-10-09, Fase 3c-2): cada listado que se migra pide un turno a `ConsultaVigente` antes de consultar y descarta un resultado que ya no es el último (Productos e Historial)".

- [ ] **Step 9: Suite del proyecto y commit**

Run: `dotnet build StockApp.sln` y `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`. Expected: 0 errores; verde.

```bash
git add src/StockApp.Presentation/ViewModels/ConsultaVigente.cs src/StockApp.Presentation/ViewModels/ViewModelBase.cs src/StockApp.Presentation/ViewModels/Catalogo/ProductoListViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/ConsultaVigenteTests.cs tests/StockApp.Presentation.Tests/ViewModels/ViewModelBaseCargaProtegidaTests.cs tests/StockApp.Presentation.Tests/ViewModels/Catalogo/ProductoViewModelTests.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "feat(ui-v2): solo la última consulta actualiza el listado de Productos"
```

### Task 2: Historial: búsqueda protegida, turnos y mensaje sin permiso

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs` (L30-36, L272-309)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoHistorialViewModelTests.cs`

**Interfaces:**
- Consumes: `ConsultaVigente`, `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` (Task 1).
- Produces: `public const string MovimientoHistorialViewModel.MensajeSinPermisoHistorial = "No tenés permiso para ver el historial de movimientos.";`; `CargarAsync()` (sin filtros) y `BuscarAsync()` (filtros vigentes), las dos protegidas y con turno.

- [ ] **Step 1: Tests (rojo)**

En `MovimientoHistorialViewModelTests` (helpers `CrearDto(id, productoId)` y `Crear(items, rol, permisos)` existentes; para las tareas pendientes se arma el mock a mano con el mismo `Crear` y un `Setup` posterior, que en Moq gana sobre el anterior):

```csharp
    [Fact]
    public async Task Buscar_SiElServicioLanzaUnauthorized_NoPropagaYMarcaSinPermiso()
    {
        var (vm, svcMock, _, _, _) = Crear();
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));

        var ex = await Record.ExceptionAsync(() => vm.BuscarCommand.ExecuteAsync(null));

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.Equal(MovimientoHistorialViewModel.MensajeSinPermisoHistorial, vm.MensajeSinPermiso);
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Buscar_MientrasEsperaAlServicio_CargandoEsTrue()
    {
        var (vm, svcMock, _, _, _) = Crear();
        var pendiente = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>())).Returns(pendiente.Task);

        var t = vm.BuscarCommand.ExecuteAsync(null);
        Assert.True(vm.Cargando);

        pendiente.SetResult(new List<MovimientoHistorialDto> { CrearDto(1) });
        await t;
        Assert.False(vm.Cargando);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task CargaYBusquedaSuperpuestas_LaBusquedaNuevaGanaAunqueLaCargaVuelvaDespues()
    {
        var (vm, svcMock, _, _, _) = Crear();
        var completa = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        var filtrada = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f => f.Tipo == null))).Returns(completa.Task);
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f => f.Tipo == TipoMovimiento.Salida))).Returns(filtrada.Task);

        var carga = vm.InicializarAsync();
        vm.TipoFiltroSeleccionado = vm.TiposDisponibles.Single(t => t.Valor == TipoMovimiento.Salida);
        var busqueda = vm.BuscarCommand.ExecuteAsync(null);
        filtrada.SetResult(new List<MovimientoHistorialDto> { CrearDto(7) });
        await busqueda;
        completa.SetResult(new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2), CrearDto(3) });
        await carga;

        Assert.Equal(new[] { 7 }, vm.Items.Select(i => i.MovimientoId));
        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task Un403DeUnaConsultaVieja_NoTapaLosResultadosDeLaNueva()
    {
        var (vm, svcMock, _, _, _) = Crear();
        var completa = new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>();
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f => f.Tipo == null))).Returns(completa.Task);
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f => f.Tipo == TipoMovimiento.Salida)))
            .ReturnsAsync(new List<MovimientoHistorialDto> { CrearDto(7) });

        var carga = vm.InicializarAsync();
        vm.TipoFiltroSeleccionado = vm.TiposDisponibles.Single(t => t.Valor == TipoMovimiento.Salida);
        await vm.BuscarCommand.ExecuteAsync(null);
        completa.SetException(new UnauthorizedAccessException("403"));
        await carga;

        Assert.False(vm.SinPermiso);
        Assert.Single(vm.Items);
    }

    [Fact]
    public async Task InicializarAsync_Un403_UsaElMensajeDelHistorial()
    {
        var (vm, svcMock, _, _, _) = Crear();
        svcMock.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.InicializarAsync();

        Assert.Equal(MovimientoHistorialViewModel.MensajeSinPermisoHistorial, vm.MensajeSinPermiso);
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MovimientoHistorialViewModelTests"`
Expected: FAIL de compilación (`MensajeSinPermisoHistorial`). Comentar temporalmente las dos líneas que lo usan, correr y PEGAR los rojos de comportamiento (`Buscar_SiElServicioLanzaUnauthorized_…` propaga; `Buscar_MientrasEspera…` con `Cargando` false; `CargaYBusqueda…` con 3 ítems; `Un403DeUnaConsultaVieja_…` con `SinPermiso`); descomentar.

- [ ] **Step 2: VM**

1. Después de la apertura de la clase (L31): 

```csharp
    /// <summary>Texto del estado "Sin permiso" cuando el servidor rechaza la carga o la búsqueda del historial (403).</summary>
    public const string MensajeSinPermisoHistorial = "No tenés permiso para ver el historial de movimientos.";

    private readonly ConsultaVigente _consultas = new();
```

2. `InicializarAsync` (L272-284): reemplazar la última línea (`await EjecutarCargaProtegidaAsync(CargarAsync, "No tenés permiso…");`) por `await CargarAsync();` y el comentario de L280-282 por: "// CargarAsync va por EjecutarCargaProtegidaAsync (fix "pantalla muda ante un 403"): el guardián sigue esta llamada local."
3. `CargarAsync` (L286-293):

```csharp
    /// <summary>
    /// Historial completo, sin filtros. Protegida (un 403 deja SinPermiso en vez de escapar) y con turno: si
    /// mientras espera el usuario busca, gana la búsqueda (ver ConsultaVigente).
    /// </summary>
    public async Task CargarAsync()
    {
        var turno = _consultas.Empezar();
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var resultados = await _service.ObtenerHistorialAsync(new HistorialMovimientoFiltro());
            if (!turno.EsLaUltima)
                return;
            Items.Clear();
            foreach (var item in resultados)
                Items.Add(item);
        }, MensajeSinPermisoHistorial, turno);
    }
```

4. `BuscarAsync` (L297-309): misma forma, con el filtro de hoy (`new HistorialMovimientoFiltro(ProductoId: FiltroProductoId, Tipo: FiltroTipo, FechaDesde: ALocalAUtc(FechaDesde), FechaHasta: ALocalAUtc(FechaHasta))`) armado ANTES de `Empezar()`, y el `<summary>`: "Historial con los filtros vigentes. Mismo camino protegido y con turno que la carga."

Run: el comando del Step 1, y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~CargaProtegida|FullyQualifiedName~MovimientoHistorial"`
Expected: PASS (los 5 nuevos, los 35 existentes de VM, `CargaProtegidaSinPermisoGuardianTests`, `MovimientoHistorialGatesTests`).

- [ ] **Step 3: Mutaciones (QUITANDO)**

- (d) En `BuscarAsync`, quitar el envoltorio (dejar el cuerpo con `await _service.ObtenerHistorialAsync(filtro)` y el llenado) → rojo en `Buscar_SiElServicioLanzaUnauthorized_…`. Restaurar.
- (e) En `CargarAsync`, quitar `if (!turno.EsLaUltima) return;` → rojo en `CargaYBusquedaSuperpuestas_…`. Restaurar.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoHistorialViewModelTests.cs
git commit -m "feat(ui-v2): el historial busca por la carga protegida y la última consulta gana"
```

### Task 3: Datos fijos del Historial

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorial.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorialTests.cs`

**Interfaces:**
- Consumes: `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`, `DatosProductos.UsuarioAdmin`, `MovimientoHistorialViewModel` (con el constructor de 5 parámetros; la Task 6 le agrega el 6.º y actualiza `Listado`).
- Produces: `DatosHistorial.Movimientos` (`IReadOnlyList<MovimientoHistorialDto>`, 12), `MovimientosConTextosLargos`, `ProductoLargo`, `UsuarioLargo`, `ComentarioLargo`, `Fecha(int mes, int dia, int hora, int minuto)`; `ConMovimientos()`, `SinMovimientos()`, `Cargando()`, `SinPermiso()`, `ConCeldaCortada()` (`ShellMainViewModel`); `Listado(...)`.

- [ ] **Step 1: Tests (rojo)**

`DatosHistorialTests.cs`:

```csharp
using System;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

public class DatosHistorialTests
{
    [Fact]
    public void Movimientos_DoceConUnStockNegativoYUnComentarioLargo()
    {
        var m = DatosHistorial.Movimientos;

        Assert.Equal(12, m.Count);
        Assert.Single(m, x => x.StockNuevo < 0);
        Assert.Equal(-3m, m.Single(x => x.StockNuevo < 0).StockNuevo);
        Assert.Contains(m, x => x.Comentario == DatosHistorial.ComentarioLargo);
        Assert.Equal(4, m.Count(x => x.Tipo == TipoMovimiento.Salida));
        Assert.All(m.Where(x => x.Tipo == TipoMovimiento.Salida), x => Assert.Equal(0m, x.PrecioUnitario));
    }

    [Fact]
    public void Fecha_EsUnaHoraLocalFija_EnCualquierHuso()
    {
        var utc = DatosHistorial.Fecha(10, 7, 14, 35);

        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(new DateTime(2026, 10, 7, 14, 35, 0), utc.ToLocalTime());
    }

    [Fact]
    public void ConTextosLargos_SoloCambianProductoYUsuarioDeLaFila3()
    {
        var cambiadas = DatosHistorial.MovimientosConTextosLargos
            .Zip(DatosHistorial.Movimientos, (nuevo, viejo) => (nuevo, viejo))
            .Where(p => p.nuevo != p.viejo).ToList();

        var (n, v) = Assert.Single(cambiadas);
        Assert.Equal(v with { ProductoNombre = DatosHistorial.ProductoLargo, UsuarioNombre = DatosHistorial.UsuarioLargo }, n);
    }

    [Fact]
    public void Cargando_LaCargaNoTermina()
    {
        var vm = (MovimientoHistorialViewModel)DatosHistorial.Cargando().CurrentContent!;

        var carga = vm.InicializarAsync();

        Assert.False(carga.IsCompleted);
        Assert.True(vm.Cargando);
    }

    [Fact]
    public async Task SinPermiso_LaCargaMarcaSinPermisoConElMensajeDelHistorial()
    {
        var vm = (MovimientoHistorialViewModel)DatosHistorial.SinPermiso().CurrentContent!;

        await vm.InicializarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(MovimientoHistorialViewModel.MensajeSinPermisoHistorial, vm.MensajeSinPermiso);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosHistorialTests"` → FAIL de compilación.

- [ ] **Step 2: `DatosHistorial`**

Los productos y precios son los de `DatosProductos.Productos`; el orden es el del servidor (más nuevo primero). Antes de escribirlo, confirmar con `Read` de `src/StockApp.Application/Movimientos/Dtos.cs` L34-47 el orden de los 13 campos y con CodeGraph que `MotivoMovimiento.Merma` existe.

```csharp
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/historial*.html: doce movimientos de un depósito municipal, con una
/// salida que deja el stock en negativo, un comentario largo, salidas y ajustes sin precio, y dos usuarios.
/// </summary>
public static class DatosHistorial
{
    public const string ComentarioLargo =
        "Reparación del alumbrado de la plaza Independencia y del pasaje peatonal frente a la escuela 12.";
    public const string ProductoLargo = "Caño de PVC reforzado de 110 mm por 4 metros con junta elástica";
    public const string UsuarioLargo = "Operador de Depósito Central de Obras Públicas";

    /// <summary>Hora LOCAL fija de 2026 convertida a UTC: la celda (UTC a local) muestra lo mismo en cualquier huso.</summary>
    public static DateTime Fecha(int mes, int dia, int hora, int minuto) =>
        new DateTime(2026, mes, dia, hora, minuto, 0, DateTimeKind.Local).ToUniversalTime();

    private const string Maria = "María Pérez";
    private const string Deposito = "Operador de Depósito Central";

    private static MovimientoHistorialDto M(int id, int productoId, string producto, TipoMovimiento tipo, MotivoMovimiento motivo,
        decimal cantidad, decimal precio, decimal anterior, decimal nuevo, string? comentario, DateTime fecha, int usuarioId, string usuario) =>
        new(id, productoId, producto, tipo, motivo, cantidad, precio, anterior, nuevo, comentario, fecha, usuarioId, usuario);

    public static readonly IReadOnlyList<MovimientoHistorialDto> Movimientos =
    [
        M(112, 1, "Cemento portland 50 kg", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 40m, 480m, 280m, 320m, "Remito 0042-00001234", Fecha(10, 7, 14, 35), 1, Maria),
        M(111, 7, "Cable 2x1,5 mm", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 25.5m, 0m, 1275.5m, 1250m, "Alumbrado de la plaza", Fecha(10, 6, 9, 10), 2, Deposito),
        M(110, 3, "Caño PVC 110 mm", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 12m, 650m, 0m, 12m, null, Fecha(10, 3, 11, 0), 1, Maria),
        M(109, 4, "Pintura látex blanca 20 L", TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 2m, 0m, 4m, 6m, "Diferencia de conteo del inventario de setiembre.", Fecha(10, 1, 16, 45), 1, Maria),
        M(108, 8, "Hipoclorito de sodio 5 L", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 5m, 0m, 2m, -3m, "Limpieza del gimnasio municipal", Fecha(9, 30, 8, 20), 2, Deposito),
        M(107, 5, "Guantes de trabajo", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 100m, 95m, 80m, 180m, null, Fecha(9, 26, 10, 15), 1, Maria),
        M(106, 6, "Lámpara LED 30 W", TipoMovimiento.Salida, MotivoMovimiento.UsoOConsumo, 4m, 0m, 12m, 8m, ComentarioLargo, Fecha(9, 24, 15, 5), 2, Deposito),
        M(105, 9, "Bolsas de residuo 80x110", TipoMovimiento.Salida, MotivoMovimiento.Merma, 10m, 0m, 70m, 60m, "Bolsas rotas en el depósito", Fecha(9, 19, 12, 30), 2, Deposito),
        M(104, 11, "Pintura vial amarilla 4 L", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 22.5m, 1890.5m, 0m, 22.5m, null, Fecha(9, 15, 9, 0), 1, Maria),
        M(103, 2, "Arena gruesa", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 45m, 890m, 0m, 45m, "Remito 0042-00001190", Fecha(9, 10, 13, 40), 1, Maria),
        M(102, 10, "Pala de punta", TipoMovimiento.Entrada, MotivoMovimiento.Ajuste, 2m, 0m, 12m, 14m, null, Fecha(9, 4, 11, 25), 1, Maria),
        M(101, 12, "Carretilla reforzada", TipoMovimiento.Entrada, MotivoMovimiento.Compra, 1m, 3200m, 0m, 1m, null, Fecha(9, 1, 8, 0), 1, Maria),
    ];

    /// <summary>Fila 3 (índice 2) con un producto y un usuario más largos que sus columnas.</summary>
    public static readonly IReadOnlyList<MovimientoHistorialDto> MovimientosConTextosLargos =
        Movimientos.Select((m, i) => i == 2 ? m with { ProductoNombre = ProductoLargo, UsuarioNombre = UsuarioLargo } : m).ToList();

    public static MovimientoHistorialViewModel Listado(Func<IReadOnlyList<MovimientoHistorialDto>>? responder = null,
        Task<IReadOnlyList<MovimientoHistorialDto>>? pendiente = null, Exception? falla = null)
    {
        var servicio = new Mock<IMovimientoStockService>();
        var historial = servicio.Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()));
        if (falla is not null)
            historial.ThrowsAsync(falla);
        else if (pendiente is not null)
            historial.Returns(pendiente);
        else
            historial.ReturnsAsync(() => (responder ?? (() => []))());
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new MovimientoHistorialViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IProductoService>(),
            Mock.Of<IConfirmacionService>(), sesion.Object);
    }

    private static ShellMainViewModel EnShell(ViewModelBase contenido) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "HistorialMovimientos", contenido);

    public static ShellMainViewModel ConMovimientos() => EnShell(Listado(() => Movimientos));
    public static ShellMainViewModel SinMovimientos() => EnShell(Listado(() => []));
    public static ShellMainViewModel Cargando() => EnShell(Listado(pendiente: new TaskCompletionSource<IReadOnlyList<MovimientoHistorialDto>>().Task));
    public static ShellMainViewModel SinPermiso() => EnShell(Listado(falla: new UnauthorizedAccessException("403")));
    public static ShellMainViewModel ConCeldaCortada() => EnShell(Listado(() => MovimientosConTextosLargos));
}
```

Run: el comando del Step 1. Expected: PASS (5).

- [ ] **Step 3: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorial.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorialTests.cs
git commit -m "test(ui-v2): datos fijos del historial de movimientos"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes de componentes y XAML)

### Task 4: Medición de anchos y mockups del Historial y del diálogo

**Decisiones aplicadas:** D1 a D15.

**Files:**
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `docs/ui-v2/mockups/inventario/historial.html`, `historial-cargando.html`, `historial-sin-permiso.html`, `historial-vacio.html`, `historial-celda-cortada.html`
- Create: `docs/ui-v2/mockups/dialogos/recalcular-stock.html`

**Interfaces:**
- Consumes: `ColumnaAuto` (3c-1), `DatosHistorial` (Task 3), `MovimientoHistorialView` actual (v1) como banco de medición.
- Produces: pantallas `inventario-historial`, `-cargando`, `-sin-permiso`, `-vacio`, `-celda-cortada` y `dialogos-recalcular-stock`; clases `.v2-barra-campos` y `.v2-grilla-scroll-h` en `base.css`; en el ledger, los anchos medidos.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal3c2.cs` (se borra en el Step 6) siguiendo `MedicionTemporal3c1` del plan de la 3c-1 (Task 4 Step 1): monta `ShellMainView` con `DatosHistorial.ConMovimientos()` y `ConCeldaCortada()` a 1280×760 y 1366×768, oculta el sidebar, y sobre la grilla de la vista v1 (headers en minúscula) marca `ColumnaAuto.SetMedir(c, true)` en Fecha, Tipo, Motivo, Cantidad, Precio, Stock ant. y Usuario (Precio con `SetConversor(MonedaConverter.Instance)` y Cantidad/Stock ant. con `CantidadConverter.Instance`; Fecha queda con su `Binding`, que `ColumnaAuto` evalúa con `StringFormat`), reasigna la fuente pasando por null y escribe por cada tamaño: ancho interno de la grilla, `ActualWidth` de cada columna, `DataGridColumnasAutoBehavior.AnchoMedido` de cada medida, el ancho del título + 36 de cada medida, y si hay scroll horizontal. Además, para la alternativa B de D4, la suma con STOCK ANT. y STOCK NUEVO reemplazados por una columna de 260.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal3c2" --logger "console;verbosity=detailed"`

Anotar las líneas en el ledger. Esperado: no entra a ningún tamaño (D4); si a 1366 ENTRA con las medidas cedidas, anotarlo y señalarlo en el GATE (D5 solo afecta a 1280). **Este banco mide con la regla vieja de cesión (la de D5 llega en la Task 5): para el mockup se usan los anchos MEDIDOS de cada columna (no los cedidos) y el piso 160 de las `*`.**

- [ ] **Step 2: Espejos CSS**

Al final de `base.css`:

```css
/* Barra de filtros sin buscador, con campos etiquetados (BarraFiltros MostrarBusqueda False + CampoFormulario):
   fila con separación 8 y el botón alineado con el borde inferior de los controles. */
.v2-barra-campos { display: flex; gap: 8px; align-items: flex-end; margin-bottom: 16px; }
/* Barra de scroll horizontal de la grilla cuando las columnas no entran: alto, color y radio medidos de la
   captura de la app (DataGrid con el tema v2); se dibuja en el borde inferior del área de filas. */
.v2-grilla-scroll-h { /* alto, fondo, radio y margen: se reemplaza por los px medidos en el Step 5 */ }
```

Los valores de `.v2-grilla-scroll-h` se miden de la captura de la app (Step 5) y se escriben en px enteros antes del GATE; nada de comentario-placeholder en el commit.

- [ ] **Step 3: Mockups del listado**

Partir de `inventario/productos.html` (encabezado, card, grilla, pie) y `productos-cargando.html` / `productos-sin-permiso.html` / `productos-vacio.html` (estados). Cada uno:
- `<title>Inventario: Historial de movimientos[ estado] (UI v2)</title>` y un comentario `<!-- Datos: DatosHistorial.<Método>(): … -->`.
- Encabezado con "Historial de movimientos" y SOLO el botón secundario "Recalcular stock" (D1).
- Barra `.v2-barra-campos` con los cuatro campos etiquetados y "Buscar" secundario (D2), copiando `.v2-campo`/`.v2-etiqueta` y `.v2-input` de `producto-nuevo.html` y el ícono de calendario de `componentes/catalogo.html`.
- Grilla con los headers de D4 y los anchos MEDIDOS del Step 1 como `flex: 0 0 Npx`; PRODUCTO y COMENTARIO `flex: 0 0 160px` cuando hay scroll; el contenedor de filas con `overflow: hidden` y la barra `.v2-grilla-scroll-h` (D4).
- Celdas: FECHA "dd/MM/yyyy HH:mm"; números con `v2-num`; PRECIO "—" en salidas y ajustes sin precio (D7); fila de stock negativo con la insignia peligro "Stock negativo" y el número con `.v2-texto-peligro` (D6), como STOCK de `productos.html`.
- Pie "12 movimientos".
- `historial-cargando`: sin grilla ni pie, `<div class="v2-cargando">Cargando movimientos…</div>`.
- `historial-sin-permiso`: la card reemplazada por el bloque `v2-vacio` con `v2-vacio-titulo--error` "Sin permiso" y "No tenés permiso para ver el historial de movimientos." (el encabezado conserva "Recalcular stock": el Admin de los datos lo tiene).
- `historial-vacio`: card con barra y `v2-vacio` "No hay movimientos" / "Probá con otros filtros o con otras fechas." (D9).
- `historial-celda-cortada`: como `historial.html` con la fila 3 de `MovimientosConTextosLargos`: PRODUCTO cortado con `<span class="v2-celda-cortada">` y USUARIO con su ancho medido (o cortado si pasa el tope de 240).

- [ ] **Step 4: Mockup del diálogo y capturas**

`dialogos/recalcular-stock.html` copiando `dialogos/pedir-texto.html` (D10): cuerpo "Elegí el producto. Su stock se recalcula a partir de todos sus movimientos.", un `v2-input` con `v2-input-placeholder` "Buscar producto…", botones "Cancelar" (secundario) y "Recalcular" (`v2-boton--primario` con el estado deshabilitado de `base.css`, si existe; si no, PARAR y medir el tono de un `Button.primario` deshabilitado en headless antes de dibujarlo).

```bash
for p in historial historial-cargando historial-sin-permiso historial-vacio historial-celda-cortada; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/dialogos/recalcular-stock.html artifacts/ui-v2/mockups/dialogos-recalcular-stock.png
dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/dialogos/recalcular-stock.html artifacts/ui-v2/mockups/dialogos-recalcular-stock@1366x768.png --tamano 1366x768
ls -l --time-style=full-iso artifacts/ui-v2/mockups/inventario-historial*.png artifacts/ui-v2/mockups/dialogos-recalcular-stock*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 12 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 5: Datos de la barra de scroll**

Con `MedicionTemporal3c2` (todavía presente), renderizar a PNG la grilla de la vista v1 a 1280 con `ComparacionVisual` o `capturar` headless y medir alto, color y radio del pulgar y del carril de la barra horizontal; escribirlos en `.v2-grilla-scroll-h` y volver a capturar los mockups que la usan. Si la app v1 no muestra el mismo `ScrollBar` que mostrará la v2 (otro tema), anotarlo y repetir este paso en la Task 7 antes de las comparaciones (riesgo conocido).

- [ ] **Step 6: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal3c2.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 7: GATE — aprobación del usuario**

PARAR. Presentar los 12 PNG (LOS DOS TAMAÑOS), la lista D1 a D15 y los anchos medidos del ledger. Señalar explícitamente: la grilla scrollea en horizontal a 1280 y 1366 (D4, igual que hoy) y la alternativa B con su número; D5 cambia una regla aprobada en la 3c-1 (con el antes y el después de FECHA a 1280); "Recalcular stock" es el único botón y abre un `Window` v2, no un overlay (D10, y por qué no adelanta la 3c-3); PRECIO "—" (D7); la recarga tras recalcular con filtros (D11, hallazgo); el 403 del buscador del diálogo (D14). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups y en los tests de las Tasks 5 a 7 que la fijan, y volver a presentar. Registrar en el ledger: `Mockups 3c-2: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 8: Commit**

```bash
git add docs/ui-v2/mockups/_base/base.css docs/ui-v2/mockups/inventario/historial*.html docs/ui-v2/mockups/dialogos/recalcular-stock.html
git commit -m "docs(ui-v2): mockups aprobados del historial de movimientos y del diálogo de recalcular stock"
```

## Bloque C — Componentes, diálogo y XAML (arranca solo con los mockups aprobados)

### Task 5: Piezas comunes que pide Historial

**Depende de:** Task 4 (D2, D5, D7 aprobadas; si el usuario cambió una, esta task se reescribe con ella antes de empezar).

**Files:**
- Modify: `src/StockApp.Presentation/Controls/V2/BarraFiltros.cs`, `src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml` (L213-226)
- Modify: `src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs` (L84-101 summary, L536-541)
- Create: `src/StockApp.Presentation/Converters/FechaHoraLocalConverter.cs`, `MonedaOGuionConverter.cs`
- Create: `tests/StockApp.Presentation.Tests/Converters/FechaHoraLocalConverterTests.cs`, `MonedaOGuionConverterTests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/V2/ColumnaAutoTopeAdaptableTests.cs` (L300-327), el test de componentes de `BarraFiltros` (ubicarlo con `grep -rln "BarraFiltros" tests/StockApp.Presentation.UiTests/V2`), `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`

**Interfaces:**
- Produces: `BarraFiltros.MostrarBusquedaProperty` (`bool`, default `true`); `FechaHoraLocalConverter.Instance` (`DateTime` UTC → `string` "dd/MM/yyyy HH:mm" local; null → ""); `MonedaOGuionConverter.Instance` (`0m` → "—"; otro `decimal` → `FormatoEsUy.Moneda`; null → ""); `GuardianDePatronTests.VistaV2_NoUsaPiezasDelTemaViejo(Type)`.

- [ ] **Step 1: Conversores (rojo, verde)**

```csharp
public class FechaHoraLocalConverterTests
{
    [Fact]
    public void UtcAHoraLocalConFormatoFijo()
    {
        var utc = new DateTime(2026, 10, 7, 14, 35, 0, DateTimeKind.Local).ToUniversalTime();

        Assert.Equal("07/10/2026 14:35", FechaHoraLocalConverter.Instance.Convert(utc, typeof(string), null, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void Null_DaVacio() =>
        Assert.Equal(string.Empty, FechaHoraLocalConverter.Instance.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
}

public class MonedaOGuionConverterTests
{
    [Theory]
    [InlineData(0, "—")]
    [InlineData(480, "$ 480,00")]
    [InlineData(12345.6, "$ 12.345,60")]
    public void Convierte(decimal valor, string esperado) =>
        Assert.Equal(esperado, MonedaOGuionConverter.Instance.Convert(valor, typeof(string), null, CultureInfo.InvariantCulture));
}
```

(El valor esperado de 480 se confirma contra `MonedaConverterTests` antes de escribirlo: si `FormatoEsUy.Moneda` usa otro separador o espacio, copiar el de ese test.) Rojo de compilación, implementar (los dos con `Instance`, `ConvertBack` → `NotSupportedException`; `FechaHoraLocalConverter` = `DateTime.SpecifyKind(fecha, DateTimeKind.Utc).ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)`), verde.

- [ ] **Step 2: `BarraFiltros.MostrarBusqueda` (rojo, verde)**

Test (en el archivo de tests de componentes v2 que ya monta `BarraFiltros`, con `MontajeV2.Montar`):

```csharp
    [AvaloniaFact]
    public void SinBusqueda_NoMuestraElBuscadorYLosFiltrosArrancanALaIzquierda()
    {
        var ventana = MontajeV2.Montar("""
            <v2:BarraFiltros x:Name="Barra" MostrarBusqueda="False"><Button x:Name="Filtro" Content="Buscar" /></v2:BarraFiltros>
            """, ancho: 800);
        var barra = MontajeV2.Buscar<BarraFiltros>(ventana, "Barra");

        Assert.False(MontajeV2.Parte<TextBox>(barra, "PART_Busqueda").IsVisible);
        var filtro = MontajeV2.Buscar<Button>(ventana, "Filtro");
        Assert.Equal(0, filtro.TranslatePoint(default, barra)!.Value.X, 0.5);
    }

    [AvaloniaFact]
    public void PorDefecto_MuestraElBuscador()
    {
        var ventana = MontajeV2.Montar("""<v2:BarraFiltros x:Name="Barra" />""");

        Assert.True(MontajeV2.Parte<TextBox>(MontajeV2.Buscar<BarraFiltros>(ventana, "Barra"), "PART_Busqueda").IsVisible);
    }
```

Implementación: `StyledProperty<bool> MostrarBusquedaProperty` (default `true`) en `BarraFiltros.cs`; en el `ControlTemplate` de `ComponentesV2.axaml` L218, `IsVisible="{TemplateBinding MostrarBusqueda}"` en `PART_Busqueda` (el `StackPanel` con `Spacing` no deja hueco por un hijo invisible). Correr además `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~CatalogoComponentes|FullyQualifiedName~ProductosComparacionTests"` (sin cambios: D15).

- [ ] **Step 3: D5 en el behavior (rojo, verde)**

En `ColumnaAutoTopeAdaptableTests`, renombrar `TanAngostaQueNiCediendoEntra_LasMedidasNoCortanSuTitulo_LasEstrellasQuedanEnSuPiso_YHayScroll` a `TanAngostaQueNiCediendoEntra_LasMedidasVuelvenASuAnchoMedido_LasEstrellasQuedanEnSuPiso_YHayScroll` y reemplazar el assert `UNIDAD < 145` y el bucle de títulos por:

```csharp
        foreach (var titulo in new[] { "CÓDIGO", "COSTO", "UNIDAD" })
        {
            var col = Col(m.Grilla, titulo);
            var medido = DataGridColumnasAutoBehavior.AnchoMedido(m.Grilla, col);
            Assert.True(medido is not null, $"{titulo} no se midió");
            Assert.Equal(medido!.Value, col.ActualWidth, 1);
        }
        Assert.False(TooltipSiSeCorta.EstaCortado(CeldaBajoHeader(m.Grilla, ConCeldaCortada()[0], "CÓDIGO")),
            "Con scroll, una columna medida no se corta.");
```

(Ajustar el ítem de `CeldaBajoHeader` al que tiene el texto más largo de CÓDIGO en `ConCeldaCortada()`; si la unidad larga pasa el tope de 240, UNIDAD queda en 240 = su medido, y se afirma eso.) Rojo: hoy quedan en su título.

Implementación en `OnLayoutUpdated`, inmediatamente después de `var encaja = …;` (L537):

```csharp
        // Si ni con las medidas en su título y las * en su piso entra, igual va a haber scroll: cortar una medida no
        // evita nada, así que vuelve a su ancho medido y se lee entera.
        if (!encaja)
            foreach (var c in medidas)
                objetivos[c] = Math.Min(c.MaxWidth, estado.AnchosMedidos[c]);
```

Y en el `<summary>` de la clase (L95-96), el ítem 3 pasa a: "Si ni así entra (…), las `*` NO bajan de su piso y las medidas VUELVEN a su ancho medido: con scroll de todos modos, cortarlas no gana nada y deja fechas e importes ilegibles. Queda el modo desborde de arriba, con scroll."

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ColumnaAuto"` y el filtro "Regresión de grillas". Expected: PASS (si `G_PisoDeclaradoPorColumna_…` u otro test de desborde cambia de ancho por D5, es la consecuencia esperada: anotarlo por nombre en el reporte y ajustar SOLO el número de la medida, con el motivo en el commit).

- [ ] **Step 4: Guardián de piezas del tema viejo (rojo, verde)**

En `GuardianDePatronTests`, junto a `DialogoV2_NoTieneOpacidadLiteralEnSuXaml`:

```csharp
    private static readonly Regex PiezaDelTemaViejo =
        new(@"SignoNegativoBrushConverter|ActivoOpacidadConverter|<c:BadgeEstado\b|<c:HeaderVista\b|<c:EstadoVacio\b", RegexOptions.Compiled);

    /// <summary>Ruta desde src del .axaml de una vista: el namespace StockApp.Presentation.X.Y es la carpeta X/Y.</summary>
    private static string RutaAxaml(Type vista) =>
        "StockApp.Presentation/" + vista.Namespace!["StockApp.Presentation.".Length..].Replace('.', '/') + "/" + vista.Name + ".axaml";

    public static TheoryData<Type> VistasV2ConAxaml => new(VistasMigradasAV2.OrderBy(t => t.Name));

    /// <summary>
    /// Una vista v2 no usa los conversores ni los controles del tema viejo (convención 11): se lee el TEXTO del
    /// .axaml, así cuenta también lo que vive en plantillas de celda, que el montaje sin ViewModel no realiza.
    /// </summary>
    [Theory]
    [MemberData(nameof(VistasV2ConAxaml))]
    public void VistaV2_NoUsaPiezasDelTemaViejo(Type vista) =>
        Assert.DoesNotMatch(PiezaDelTemaViejo, LeerDesdeSrc(RutaAxaml(vista)));

    [Fact]
    public void DetectorDePiezasViejas_VeElConversorYElControl()
    {
        Assert.Matches(PiezaDelTemaViejo, """<TextBlock Foreground="{Binding X, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />""");
        Assert.Matches(PiezaDelTemaViejo, """<c:BadgeEstado Texto="Stock negativo" />""");
        Assert.DoesNotMatch(PiezaDelTemaViejo, """<v2:Insignia Texto="Stock negativo" />""");
    }
```

Si `RutaAxaml` no resuelve alguna vista de `VistasMigradasAV2` (archivo en otra carpeta), el test lo dice por nombre: corregir el cálculo, nunca sacar la vista de la lista. Rojo: correr con `MovimientoHistorialView` agregada TEMPORALMENTE a `VistasMigradasAV2` (sale el rojo real por `SignoNegativoBrushConverter`), pegar, quitarla (entra de verdad en la Task 7). Verde con la lista actual.

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (f) Quitar el bloque `if (!encaja) foreach …` → rojo en `TanAngostaQueNiCediendoEntra_LasMedidasVuelvenASuAnchoMedido_…`. Restaurar.
- (g) Quitar `IsVisible="{TemplateBinding MostrarBusqueda}"` → rojo en `SinBusqueda_NoMuestraElBuscador…`. Restaurar.
- (h) En `MonedaOGuionConverter`, quitar la rama del 0 → rojo en `Convierte(0, "—")`. Restaurar.
- (i) Quitar `SignoNegativoBrushConverter|` de la regex → rojo en `DetectorDePiezasViejas_…`. Restaurar.

- [ ] **Step 6: Suite de la task y commit**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

```bash
git add src/StockApp.Presentation/Controls/V2/BarraFiltros.cs src/StockApp.Presentation/Controls/V2/ComponentesV2.axaml src/StockApp.Presentation/Behaviors/DataGridColumnasAutoBehavior.cs src/StockApp.Presentation/Converters/FechaHoraLocalConverter.cs src/StockApp.Presentation/Converters/MonedaOGuionConverter.cs tests/
git commit -m "feat(ui-v2): barra de filtros sin buscador, medidas enteras cuando hay scroll, conversores de fecha y precio, guardián de piezas viejas"
```

(`git add tests/` solo después de `git status --short tests/` mostrando únicamente los archivos de esta task.)

### Task 6: Diálogo de Recalcular stock

**Depende de:** Task 4 (D10, D11, D14 aprobadas).

**Files:**
- Create: `src/StockApp.Presentation/Services/IRecalcularStockDialogService.cs`, `RecalcularStockDialogService.cs`
- Create: `src/StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml`, `RecalcularStockDialog.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (junto a L291)
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs` (constructor L166-185, `BuscarProductosInternalAsync` L204-208, `RecalcularAsync` L357)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoHistorialViewModelTests.cs` (`Crear` L79), `tests/StockApp.Presentation.UiTests/MovimientoHistorialGatesTests.cs` (`Montar` L104), `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorial.cs` (`Listado`)
- Create: `tests/StockApp.Presentation.UiTests/V2/RecalcularStockDialogTests.cs`
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs`, `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`DialogosV2`, L479-484)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3: el cambio de VM de U1)

**Interfaces:**
- Produces: `public interface IRecalcularStockDialogService { Task<ProductoDto?> ElegirProductoAsync(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar); }`; `RecalcularStockDialog(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar)` con partes `MensajeText`, `BuscadorProducto` (`AutoCompleteBox`), `CancelarButton`, `RecalcularButton`; `MovimientoHistorialViewModel.AbrirRecalcularCommand` (`IAsyncRelayCommand`); constructor `(service, navigation, productoService, confirmacion, session, IRecalcularStockDialogService dialogoRecalcular)`.

- [ ] **Step 1: Tests de VM (rojo)**

`Crear` gana un `Mock<IRecalcularStockDialogService>` que devuelve null por defecto y lo devuelve en la tupla (6.º elemento: actualizar las deconstrucciones existentes con un descarte `_`). Tests:

```csharp
    [Fact]
    public async Task AbrirRecalcular_EligeUnProducto_RecalculaEseYRecargaConLosFiltros()
    {
        var (vm, svcMock, _, _, confirmacion, dialogo) = Crear(permisos: [Permisos.RegistrarMovimientos, Permisos.RecalcularStock], rol: RolUsuario.Operador);
        dialogo.Setup(d => d.ElegirProductoAsync(It.IsAny<Func<string?, CancellationToken, Task<IEnumerable<object>>>>()))
            .ReturnsAsync(CrearProducto(42, "Cemento portland 50 kg"));
        svcMock.Setup(s => s.RecalcularStockAsync(42)).ReturnsAsync(new RecalculoResultadoDto(42, 10m, 12m, 3));
        vm.TipoFiltroSeleccionado = vm.TiposDisponibles.Single(t => t.Valor == TipoMovimiento.Salida);

        await vm.AbrirRecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(42), Times.Once);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f => f.Tipo == TipoMovimiento.Salida)), Times.Once);
        confirmacion.Verify(c => c.InformarAsync("Stock recalculado."), Times.Once);
    }

    [Fact]
    public async Task AbrirRecalcular_Cancela_NoRecalcula()
    {
        var (vm, svcMock, _, _, _, dialogo) = Crear();
        dialogo.Setup(d => d.ElegirProductoAsync(It.IsAny<Func<string?, CancellationToken, Task<IEnumerable<object>>>>()))
            .ReturnsAsync((ProductoDto?)null);

        await vm.AbrirRecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task AbrirRecalcular_SinPermiso_NoAbreElDialogo()
    {
        var (vm, _, _, _, _, dialogo) = Crear(rol: RolUsuario.Operador, permisos: [Permisos.RegistrarMovimientos]);

        await vm.AbrirRecalcularCommand.ExecuteAsync(null);

        dialogo.Verify(d => d.ElegirProductoAsync(It.IsAny<Func<string?, CancellationToken, Task<IEnumerable<object>>>>()), Times.Never);
    }

    [Fact]
    public async Task AbrirRecalcular_PasaElBuscadorDelVm()
    {
        var (vm, _, _, _, _, dialogo) = Crear();

        await vm.AbrirRecalcularCommand.ExecuteAsync(null);

        dialogo.Verify(d => d.ElegirProductoAsync(vm.BuscarProductosAsync), Times.Once);
    }

    [Fact]
    public async Task BuscarProductos_Un403_DevuelveVacioSinPropagar()
    {
        var (vm, _, _, productos, _, _) = Crear();
        productos.Setup(p => p.BuscarPorTextoAsync(It.IsAny<string?>())).ThrowsAsync(new UnauthorizedAccessException("403"));

        var resultado = await vm.BuscarProductosAsync("cem", CancellationToken.None);

        Assert.Empty(resultado);
    }
```

El mensaje "Stock recalculado." se confirma contra `RecalcularAsync` (L358) antes de fijarlo. Run → FAIL de compilación. Pegar.

- [ ] **Step 2: VM**

1. Campo `private readonly IRecalcularStockDialogService _dialogoRecalcular;`, parámetro 6.º del constructor y su asignación.
2. Comando:

```csharp
    /// <summary>
    /// "Recalcular stock" del encabezado: abre el diálogo con el buscador de producto (el mismo de siempre) y, si el
    /// usuario elige uno, recalcula con RecalcularCommand. Sin el permiso no abre nada (el botón ya está oculto; esto
    /// cubre una ejecución directa del comando).
    /// </summary>
    [RelayCommand]
    private async Task AbrirRecalcularAsync()
    {
        if (!PuedeRecalcularStock)
            return;
        var producto = await _dialogoRecalcular.ElegirProductoAsync(BuscarProductosAsync);
        if (producto is null)
            return;
        ProductoSeleccionadoParaRecalcular = producto;
        await RecalcularCommand.ExecuteAsync(null);
    }
```

3. `RecalcularAsync` L357: `await CargarAsync();` → `await BuscarAsync();` y en su `<summary>`: "Recarga con los filtros vigentes: la grilla tiene que seguir mostrando lo que dicen los filtros."
4. `BuscarProductosInternalAsync` (L204-208): envolver en `try { … } catch (UnauthorizedAccessException) { return Array.Empty<object>(); }` y reemplazar la frase "Sin catch de UnauthorizedAccessException acá…" del `<summary>` de `BuscarProductosAsync` (L147-152) por: "Un 403 (quien tiene stock.recalcular puede no tener catalogo.productos, que exige la búsqueda) devuelve la lista vacía: el aviso global del 403 ya lo explica y una excepción quedaría sin observar dentro del AutoCompleteBox. No toca PuedeFiltrarPorProducto (otro campo)."
5. Actualizar los dos sitios de construcción de test (Files) y `DatosHistorial.Listado` con `Mock.Of<IRecalcularStockDialogService>()`.

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~MovimientoHistorial"` → PASS.

- [ ] **Step 3: Diálogo real (rojo, verde)**

`RecalcularStockDialogTests.cs` (patrón de `DialogosV2Tests`: `Abrir<T>` con dueño, `Clic`):

```csharp
public class RecalcularStockDialogTests
{
    private static ProductoDto P(int id, string nombre) => new(id, $"C-{id}", null, nombre, null, 1, "Construcción", null, 1,
        "Unidad", 100m, 5m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly Func<string?, CancellationToken, Task<IEnumerable<object>>> SinResultados =
        (_, _) => Task.FromResult<IEnumerable<object>>(Array.Empty<object>());

    [AvaloniaFact]
    public void SinProductoElegido_RecalcularDeshabilitado()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        Abrir<ProductoDto?>(dialogo);

        Assert.False(dialogo.FindControl<Button>("RecalcularButton")!.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void ConProductoElegido_RecalcularDevuelveEseProducto()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        var resultado = Abrir<ProductoDto?>(dialogo);
        var elegido = P(42, "Cemento portland 50 kg");

        dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.SelectedItem = elegido;
        Dispatcher.UIThread.RunJobs();
        Clic(dialogo, "RecalcularButton");

        Assert.True(resultado.IsCompleted);
        Assert.Same(elegido, resultado.Result);
    }

    [AvaloniaFact]
    public void Cancelar_DevuelveNull()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        var resultado = Abrir<ProductoDto?>(dialogo);
        dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.SelectedItem = P(1, "Uno");

        Clic(dialogo, "CancelarButton");

        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void ElBuscadorUsaElDelVm()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);

        Assert.Same(SinResultados, dialogo.FindControl<AutoCompleteBox>("BuscadorProducto")!.AsyncPopulator);
    }

    [AvaloniaFact]
    public void Textos_YBotonesSinRecorteDentroDelAnchoUtil()
    {
        var dialogo = new RecalcularStockDialog(SinResultados);
        Abrir<ProductoDto?>(dialogo);

        Assert.Equal("Recalcular stock", dialogo.Title);
        Assert.Equal("Elegí el producto. Su stock se recalcula a partir de todos sus movimientos.", dialogo.FindControl<TextBlock>("MensajeText")!.Text);
        foreach (var nombre in new[] { "CancelarButton", "RecalcularButton" })
        {
            var b = dialogo.FindControl<Button>(nombre)!;
            var borde = b.TranslatePoint(new Point(b.Bounds.Width, 0), dialogo)!.Value.X;
            Assert.True(borde <= dialogo.Width - 24 + 0.5, $"{nombre} termina en {borde:F1}");
        }
    }
}
```

(`Abrir`/`Clic` copiados de `DialogosV2Tests` L18-31; si ya son `internal static`, reusarlos.)

`RecalcularStockDialog.axaml`: copia de `PedirTextoDialog.axaml` con `Title="Recalcular stock"`, `MensajeText` (`Classes="cuerpo"`, el texto de D10), un `AutoCompleteBox x:Name="BuscadorProducto"` (`PlaceholderText="Buscar producto…"`, `FilterMode="None"`, `MinimumPopulateDelay="0:0:0.3"`, `ItemTemplate` con el `Nombre` de `cat:ProductoDto` y `ValueMemberBinding` a `Nombre`, como el de hoy en `MovimientoHistorialView.axaml` L34-46) en lugar del `TextBox`, y los botones `CancelarButton` ("Cancelar", `secundario`) y `RecalcularButton` ("Recalcular", `primario`, `IsEnabled="False"`). Comentario: "UI v2 (spec §5.5). Ver ConfirmacionDialog.axaml. El buscador lo pone el VM (RecalcularStockDialogService)."

`.axaml.cs`: constructor sin parámetros (`InitializeComponent()`; lo pide el diseñador) y constructor `(buscar)` que asigna `BuscadorProducto.AsyncPopulator = buscar;` y engancha `BuscadorProducto.SelectionChanged += (_, _) => RecalcularButton.IsEnabled = BuscadorProducto.SelectedItem is ProductoDto;`; `OnRecalcularClick` → `Close(BuscadorProducto.SelectedItem as ProductoDto)`; `OnCancelarClick` → `Close(null)`.

Servicio: `RecalcularStockDialogService` con el mismo cuerpo defensivo de `ClasificacionTareaDialogService.PedirClasificacionAsync`/`MostrarDialogoAsync` (sin `Application.Current` o sin `MainWindow` → null; `Dispatcher.UIThread.InvokeAsync`; `new RecalcularStockDialog(buscar).ShowDialog<ProductoDto?>(owner)`). Registro: `services.AddSingleton<IRecalcularStockDialogService, RecalcularStockDialogService>();` junto a `App.axaml.cs` L291.

`GuardianDePatronTests.DialogosV2` gana `"StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml"`. `Guardian_CubreTodasLasVistasDelEnsamblado` tiene que seguir verde (si el diálogo nuevo cae como "huérfano", agregarlo donde están los otros diálogos de `Views/Dialogs`, mirando cómo están cubiertos `PedirTextoDialog` y `ConfirmacionDialog`).

Run: `dotnet build StockApp.sln`; `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~RecalcularStockDialogTests|FullyQualifiedName~GuardianDePatronTests|FullyQualifiedName~DialogosV2Tests|FullyQualifiedName~MovimientoHistorialGatesTests"` → PASS. Correr también `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ComposicionDI"` (el VM se sigue resolviendo por DI).

- [ ] **Step 4: Comparación del diálogo**

En `DialogosComparacionTests`:

```csharp
    private static Control RecalcularStock() =>
        ContenidoDeDialogo.Extraer(new RecalcularStockDialog((_, _) => Task.FromResult<IEnumerable<object>>(Array.Empty<object>())));

    [AvaloniaFact]
    public void RecalcularStock_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(RecalcularStock(), null, "dialogos/recalcular-stock.html");

    [AvaloniaFact]
    public void RecalcularStock_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(RecalcularStock(), null, "dialogos/recalcular-stock.html", tamano: TamanoLienzo.Municipio);
```

Run: `scripts/ui-v2/comparar.sh RecalcularStock`. Expected: 2 PASS; anotar teselas densas en el ledger; MIRAR la fila en `index.html`. Si supera el umbral, diagnóstico con `teselas.py`/`tinta.py` antes de tocar nada (el `AutoCompleteBox` v2 nunca se comparó: ver Riesgos).

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (j) En `RecalcularStockDialog.axaml.cs`, quitar el enganche de `SelectionChanged` → rojo en `ConProductoElegido_…` (el botón sigue deshabilitado y el click no cierra). Restaurar.
- (k) En `AbrirRecalcularAsync`, quitar `if (!PuedeRecalcularStock) return;` → rojo en `AbrirRecalcular_SinPermiso_…`. Restaurar.
- (l) En `RecalcularAsync`, quitar la línea `await BuscarAsync();` → rojo en `AbrirRecalcular_EligeUnProducto_…` (no recarga). Restaurar.
- (m) En `BuscarProductosInternalAsync`, quitar el `try`/`catch` → rojo en `BuscarProductos_Un403_…`. Restaurar.

- [ ] **Step 6: Spec §3 y commit**

En §3, mismo bullet que la Task 1: "; y el diálogo de Recalcular stock (decisión del usuario 2026-10-09, Fase 3c-2): `MovimientoHistorialViewModel` recibe `IRecalcularStockDialogService` y expone `AbrirRecalcularCommand`, que reusa el `RecalcularCommand` de siempre".

```bash
git add src/StockApp.Presentation/Services/IRecalcularStockDialogService.cs src/StockApp.Presentation/Services/RecalcularStockDialogService.cs src/StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml src/StockApp.Presentation/Views/Dialogs/RecalcularStockDialog.axaml.cs src/StockApp.Presentation/App.axaml.cs src/StockApp.Presentation/ViewModels/Movimientos/MovimientoHistorialViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/Movimientos/MovimientoHistorialViewModelTests.cs tests/StockApp.Presentation.UiTests/MovimientoHistorialGatesTests.cs tests/StockApp.Presentation.UiTests/V2/RecalcularStockDialogTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosHistorial.cs docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "feat(ui-v2): diálogo v2 de recalcular stock con buscador de producto"
```

### Task 7: Historial v2: vista, estados, columnas y comparaciones

**Depende de:** Tasks 1 a 6 (leer del ledger las D# cambiadas).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Movimientos/MovimientoHistorialView.axaml` (reescrita; el `.axaml.cs` NO cambia)
- Create: `tests/StockApp.Presentation.UiTests/V2/MovimientoHistorialViewV2Tests.cs`
- Modify (convención 13, adaptar sin borrar): `MovimientoHistorialGatesTests.cs`, `MovimientoHistorialViewStockOrdenTests.cs`, `DataGridColumnasAutoAjusteTests.cs` (headers del Historial en mayúsculas en L193-700 y el `G_HistorialEnShellA1366_…`), `DataGridCeldaNumericaOrdenTests.cs` (L81-100), `DataGridSortClickTests.cs` (si busca "Cantidad"/"Producto" por texto), `GrillasSinRecorteBarridoTests.cs` (L72), `SignoNegativoBadgeTests.cs` (L233-252: `BadgeEstado` → `Insignia` peligro, como el de Productos L74-79), `GuardianDePatronTests.cs` (L48, L102, `VistasMigradasAV2`)
- Modify: `tests/StockApp.Presentation.VisualTests/GrillaEspacioDerechoVistaRealTests.cs` (se muda a `HistorialPorProductoView`)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/HistorialComparacionTests.cs`

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: partes nombradas de `MovimientoHistorialView`: `Encabezado`, `BotonRecalcular`, `EstadoSinPermiso`, `Tarjeta`, `Filtros`, `CampoProducto`, `BotonBuscar`, `IndicadorCarga`, `EstadoSinMovimientos`, `Grilla`, `PieConteo`.

- [ ] **Step 1: UiTests de la vista (rojo)**

`MovimientoHistorialViewV2Tests.cs` con un `HistorialFake : IMovimientoStockService` (`Lista`, `Pendiente`, `Falla`, y `PorFiltro: Func<HistorialMovimientoFiltro, Task<IReadOnlyList<MovimientoHistorialDto>>>?` para la carrera; `RecalcularStockAsync` devuelve `new RecalculoResultadoDto(id, 0m, 0m, 0)`; `RegistrarAsync` lanza `NotSupportedException`: confirmar la interfaz completa con CodeGraph antes de escribirlo), un `ProductosFake` mínimo, `Montar(fake, ancho = 1040, sesion = Operador con RegistrarMovimientos + RecalcularStock + GestionarProductos)` y los helpers `Visible`, `Columna`, `CeldaDe`, `Clickear` copiados de `ProductoListViewV2Tests` (L83-140; si la 3c-1 los dejó `internal static`, reusarlos). Tests (cada uno con su cuerpo completo en el commit; los nombres y lo que afirman son el contrato):

1. `Raiz_TieneUi2YUsaLosComponentesV2` — `Classes` contiene `ui2`; hay `EncabezadoPagina`, `TarjetaContenido`, `BarraFiltros` con `MostrarBusqueda` false; ningún `BadgeEstado`, `HeaderVista` ni `c:EstadoVacio`.
2. `Encabezado_SoloRecalcularStockSecundario` — un único `Button` en el encabezado, `Content` "Recalcular stock", clase `secundario`; ningún `primario` en toda la vista.
3. `Columnas_LasDiezEnMayusculasYEnSuOrden` — `FECHA … COMENTARIO`.
4. `Cargando_SinFilas_MuestraElIndicadorYNiElVacioNiLaGrilla` — "Cargando movimientos…".
5. `AlTerminarLaCargaSinMovimientos_ElIndicadorDaPasoAlEstadoVacio` — título "No hay movimientos" y el mensaje de D9.
6. `Buscar_ConFilasEnPantalla_NoMuestraElIndicadorYLaGrillaSigue`.
7. `SinPermiso_EnLaCarga_MuestraSinPermisoEnLugarDeLaCardYNuncaElVacio` — mensaje `MensajeSinPermisoHistorial`; el encabezado sigue.
8. `SinPermiso_EnUnaBusqueda_ReemplazaLaGrillaPorSinPermiso`.
9. `BuscarConLaCargaInicialEnCurso_LaGrillaQuedaConLaBusqueda` — `PorFiltro`: sin filtro → pendiente; Tipo Salida → 1 fila; elegir Salida en el `ComboBox` real, click real en "Buscar", completar la carga con 3 filas: la grilla muestra 1 fila y el pie "1 movimiento".
10. `PieDeConteo_SingularYPluralConMilesEsUy` — 1 → "1 movimiento"; 1500 → "1.500 movimientos".
11. `StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto` — en STOCK NUEVO, `Insignia` peligro visible solo en la fila negativa y el número en `#B91C1C`; STOCK ANT. negativo en `#B91C1C` sin insignia.
12. `Precio_CeroSeMuestraComoGuion` — salida con precio 0 → "—"; compra 480 → "$ 480,00".
13. `Fecha_HoraLocalConFormatoFijo` — con `DatosHistorial.Fecha(10, 7, 14, 35)` la celda dice "07/10/2026 14:35".
14. `Scroll_MasDeMilMovimientos_HastaElFinal_NoMueveNingunaColumna` — 1200 movimientos, la última con la fecha, cantidad, precio, stock y usuario más anchos; `ScrollIntoView` hasta el final; anchos iguales ±0,5.
15. `Ordenar_PorCadaColumna_NoMueveAnchosNiCortaElTitulo` — click real en cada header ordenable.
16. `A1280y1366_LasMedidasSeLeenEnterasYLaGrillaScrollea` — `[AvaloniaTheory]` 1040 y 1126 de vista: ninguna celda de las medidas cortada (`TooltipSiSeCorta.EstaCortado` false), PRODUCTO y COMENTARIO en 160, scroll horizontal visible (D4/D5; si el GATE eligió la alternativa B, este test cambia con ella).
17. `Paridad_ElTextoMedidoEsElTextoPintado` — para cada columna medida y cada fila, `ColumnaAuto.Texto(col, item)` == texto del `TextBlock` bajo su header.
18. `ComentarioLargo_SeCortaYElTooltipAbre_YElQueEntraNo` — con el `ComentarioLargo` y otro corto (patrón de `CeldaCortada_MuestraElipsisYTooltip_…` de Productos y su `TooltipSeCancela`).
19. `SinGestionarProductos_ElFiltroDeProductoNoEstaYTipoArrancaALaIzquierda` — operador `RegistrarMovimientos` solo: `CampoProducto` no visible y el campo Tipo en x = 0 de la barra (D3).
20. `RevocarRecalcularStock_EnCaliente_OcultaElBotonSinRecrearLaVista` — `Mixto(RegistrarMovimientos, RecalcularStock)`, `EstablecerPermisos` sin `RecalcularStock`, `NotificarPermisosActualizados`: `BotonRecalcular` oculto (patrón `RevocarGestionarProductos_EnCaliente_…`).
21. `Un403_ConElHistorialAbierto_OcultaRecalcularDesdeElShell` — con `ShellMainView`, `ViewLocator` y `ManejadorAccesoRevocado.ManejarAsync` (patrón `Un403_ConLaPantallaDeProductosAbierta_…`).
22. `Recalcular_ClickReal_AbreElDialogoDelServicio` — un `IRecalcularStockDialogService` fake que registra la llamada: click real en `BotonRecalcular` → 1 llamada.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MovimientoHistorialViewV2Tests"` → FAIL (la vista es v1). Pegar el resumen de rojos.

- [ ] **Step 2: `MovimientoHistorialView.axaml` v2**

Reescribir siguiendo `ProductoListView.axaml` (raíz con `Classes="ui2"`, `Background="{DynamicResource V2FondoBrush}"`, `DockPanel Margin="28,24"`, `xmlns:v2`, `xmlns:b`, `xmlns:conv`, `xmlns:dto`, `xmlns:cat`), con:
1. `v2:EncabezadoPagina x:Name="Encabezado" Titulo="Historial de movimientos"` y en sus acciones `Button x:Name="BotonRecalcular" Classes="secundario" Content="Recalcular stock" Command="{Binding AbrirRecalcularCommand}" IsVisible="{Binding PuedeRecalcularStock}"`.
2. `v2:EstadoVacio x:Name="EstadoSinPermiso"` (`UseLayoutRounding="False"`, `EsError`, `Mensaje="{Binding MensajeSinPermiso}"`, `IsVisible="{Binding SinPermiso}"`) y `v2:TarjetaContenido x:Name="Tarjeta" IsVisible="{Binding !SinPermiso}"`; se borra el `c:EstadoVacio` superpuesto (L203-207) y el `Grid` que lo envolvía.
3. `v2:BarraFiltros x:Name="Filtros" MostrarBusqueda="False"` con un `StackPanel` horizontal (`Spacing="8"`) de: `v2:CampoFormulario x:Name="CampoProducto" Etiqueta="Producto" IsVisible="{Binding PuedeFiltrarPorProducto}"` con el `AutoCompleteBox` de hoy (L71-83, `Width="280"`, `PlaceholderText="Todos los productos"`); `CampoFormulario Etiqueta="Tipo"` con el `ComboBox` (L88-96, `Width="140"`, `ItemTemplate` con `x:DataType="vm:OpcionTipoMovimiento"`); `Etiqueta="Desde"` y `Etiqueta="Hasta"` con los `CalendarDatePicker` de hoy (L101-114, `Width="140"`); `Button x:Name="BotonBuscar" Classes="secundario" Content="Buscar" Command="{Binding BuscarCommand}" VerticalAlignment="Bottom"`. Las propiedades exactas de `CampoFormulario` (contenido, márgenes) se confirman en `ComponentesV2.axaml` antes de escribir.
4. Pie, indicador y vacío con las condiciones de la convención 18, copiadas de `ProductoListView.axaml` L49-88, con los textos de D9 (`ConverterParameter='movimiento|movimientos'`, `x:Name="EstadoSinMovimientos"`).
5. `DataGrid x:Name="Grilla"` (`ItemsSource="{Binding ItemsView}"`, `IsReadOnly`, `CanUserSortColumns`, `CanUserResizeColumns`, `ScrollViewer.HorizontalScrollBarVisibility="Auto"`, `IsVisible` por `HayElementos`; sin `GridLinesVisibility`: el tema v2 dibuja los separadores) con 10 `DataGridTemplateColumn` (`x:DataType="dto:MovimientoHistorialDto"` en cada `DataTemplate`), cada `TextBlock` de texto con `TextTrimming="CharacterEllipsis"`, `ToolTip.Tip` = el mismo texto, `b:TooltipSiSeCorta.Activo="True"`, `VerticalAlignment="Center"`:
   - FECHA `Width="Auto" SortMemberPath="Fecha" b:ColumnaAuto.Medir="True" b:ColumnaAuto.Conversor="{x:Static conv:FechaHoraLocalConverter.Instance}"`, texto `{Binding Fecha, Converter={x:Static conv:FechaHoraLocalConverter.Instance}}`.
   - PRODUCTO `Width="2*" SortMemberPath="ProductoNombre"`.
   - TIPO `Auto`, `SortMemberPath="TipoNombre"`, medida. MOTIVO `Auto`, `SortMemberPath="MotivoNombre"`, medida.
   - CANTIDAD `Auto`, `CellStyleClasses="num"`, `SortMemberPath="Cantidad"`, medida con `CantidadConverter`.
   - PRECIO `Auto`, `num`, `SortMemberPath="PrecioUnitario"`, medida con `MonedaOGuionConverter` (D7).
   - STOCK ANT. `Auto`, `num`, `SortMemberPath="StockAnterior"`, medida con `CantidadConverter`, `Classes.negativo` por `EsNegativoConverter` (D6).
   - STOCK NUEVO `Width="220" MinWidth="100"`, `num`, `SortMemberPath="StockNuevo"`: `Grid` con `v2:Insignia Texto="Stock negativo" Variante="Peligro" HorizontalAlignment="Left"` + `TextBlock` con `Classes.negativo`, copiado de STOCK de `ProductoListView.axaml` L141-160 (sin `Classes.inactivo`).
   - USUARIO `Auto`, `SortMemberPath="UsuarioNombre"`, medida.
   - COMENTARIO `Width="3*" SortMemberPath="Comentario"`.
   Comentarios del XAML: el porqué de cada decisión (scroll horizontal porque las 10 columnas no entran; PRECIO "—" porque 0 es "no aplica"; FECHA como plantilla para poder cortar y medir), sin `--` y sin citar el plan.

Run: `dotnet build StockApp.sln` y el comando del Step 1 → PASS.

- [ ] **Step 3: Tests heredados (convención 13)**

Adaptar, sin borrar ninguno, los archivos de **Files**. En `MovimientoHistorialGatesTests`: los dos tests de `PuedeRecalcularStock` pasan a afirmar solo el botón "Recalcular stock" (el campo "Producto a recalcular:" se mudó al diálogo; su gate es el del botón y el guard del VM, `AbrirRecalcular_SinPermiso_…`), con el nombre ajustado (`…_BotonVisible` / `…_BotonOculto`) y el `<summary>` explicándolo; el filtro "Producto" se ubica por la etiqueta del `CampoFormulario`. En `GuardianDePatronTests`: borrar `[InlineData(typeof(MovimientoHistorialView), "Historial de movimientos", "MOVIMIENTOS")]` (L48), sacar `typeof(MovimientoHistorialView),` de `VistasDeLaTanda` (L102) y agregarla a `VistasMigradasAV2`. `GrillaEspacioDerechoVistaRealTests`: mudar a `HistorialPorProductoView`, columna "Comentario", con la misma precondición de corte; si no corta, PARAR y consultar.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj` y `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~GrillaEspacioDerecho"`. Expected: PASS. Todo cambio de un número en un test heredado se anota en el reporte con el antes, el después y el motivo.

- [ ] **Step 4: Comparaciones (rojo, verde y diagnóstico)**

`HistorialComparacionTests.cs` (copia de la estructura de `ProductosComparacionTests`, mismo `OcultarEnMockup`/`OcultarSidebar`): `Historial_IgualAlMockup` y `_1366x768` (`ConMovimientos`, `historial.html`), `HistorialCargando_…`, `HistorialSinPermiso_…`, `HistorialVacio_…` y `HistorialCeldaCortada_…` (con `recortesPermitidos: [DatosHistorial.ProductoLargo]` y el usuario si se corta). 10 tests.

Run: `scripts/ui-v2/comparar.sh Historial`. Expected: 10 PASS; anotar las teselas densas de cada una en el ledger; MIRAR cada fila en `index.html` (la barra de scroll, la insignia, el "—", la elipsis). Si una supera el umbral: `teselas.py`/`tinta.py` ANTES de tocar nada; geometría se corrige en XAML o mockup (≤ 4 px no visible se anota, mayor vuelve al usuario); tono se presenta al usuario por tesela.

- [ ] **Step 5: Barridos y regresión de grillas**

Correr el filtro "Regresión de grillas". Expected: PASS, en particular `GrillasSinRecorteBarridoTests` (escenario "Historial de movimientos" con la vista v2: si marca un recorte en una medida que cedió o llegó al tope, es la excepción `EsCorteAdmitido`; si es otra cosa, es un defecto real) y `GrillasEspacioDerechoBarridoTests`.

- [ ] **Step 6: Mutaciones sobre la View real (QUITANDO)**

- (n) Quitar `<Binding Path="Cargando" Converter="{x:Static BoolConverters.Not}" />` del vacío → rojo en `Cargando_SinFilas_…`. Restaurar.
- (o) Quitar `IsVisible="{Binding !SinPermiso}"` de `Tarjeta` → rojo en `SinPermiso_EnLaCarga_…` y `SinPermiso_EnUnaBusqueda_…`. Restaurar.
- (p) Quitar `IsVisible="{Binding PuedeRecalcularStock}"` de `BotonRecalcular` → rojo en `RevocarRecalcularStock_EnCaliente_…` y en el gate adaptado `PuedeRecalcularStock_SinPermiso_BotonOculto`. Restaurar.
- (q) Quitar `IsVisible="{Binding PuedeFiltrarPorProducto}"` de `CampoProducto` → rojo en `SinGestionarProductos_…` y `PuedeFiltrarPorProducto_SinGestionarProductos_ComboOculto`. Restaurar.
- (r) Quitar `b:ColumnaAuto.Medir="True"` de FECHA → rojo en `Scroll_MasDeMilMovimientos_…` y/o `A1280y1366_…`. Restaurar.
- (s) Quitar `Classes.negativo` del número de STOCK NUEVO → rojo en `StockNegativo_…`. Restaurar.
- (t) Quitar `b:TooltipSiSeCorta.Activo="True"` de COMENTARIO → rojo en `ComentarioLargo_…`. Restaurar.
- (u) Quitar `b:ColumnaAuto.Conversor="…"` de PRECIO → rojo en `Paridad_…`. Restaurar.
- (v) En `MovimientoHistorialViewModel.CargarAsync` (la consulta vieja de ese escenario es la carga), quitar `if (!turno.EsLaUltima) return;` → rojo en `BuscarConLaCargaInicialEnCurso_LaGrillaQuedaConLaBusqueda`. Restaurar.

`dotnet build StockApp.sln` después de cada restauración y `git diff --no-index` vacío.

- [ ] **Step 7: Suite de la task y commit**

Los seis comandos de la Task 5 Step 6. Expected: todo verde, `tokens --verificar` exit 0.

```bash
git add src/StockApp.Presentation/Views/Movimientos/MovimientoHistorialView.axaml tests/
git commit -m "feat(ui-v2): historial de movimientos v2 con carga, sin permiso, columnas medidas y stock negativo, comparado"
```

(`git status --short` antes del `add`: solo los archivos de esta task.)

## Bloque D — Cierre de la fase

### Task 8: Suite completa, verificación orgánica, aprobación final, herramientas y spec

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 23 y 24; frases en 10 y 19)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 3c-2 cerrada en `feat/ui-v2` (sin push ni merge).

- [ ] **Step 1: Build, suite, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos de fixtures `.ods` (confirmar por nombre); total ≥ 5350 + los nuevos (anotarlo); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios; si una se mueve, diagnosticarla: D5 y `MostrarBusqueda` no deberían mover ninguna).

- [ ] **Step 2: Verificación orgánica (spec §6.4) — app real contra la base local**

Preparación:
- `docker start stockapp-pg` (queda andando al terminar); API del worktree levantada (anotar la URL, p. ej. `http://localhost:5043`, y confirmarla contra `~/.config/GestionMunicipal/conexion.json`); `admin` / `Admin1234`.
- Operador de permisos MIXTOS: reutilizar `f3c1mixto` (id 23, `catalogo.productos` + `movimientos.registrar`, sin `stock.recalcular`). Guardar sus permisos con `GET /usuarios/23/permisos` antes de tocarlos.
- **Volumen:** PREGUNTAR al usuario antes de crear datos. Propuesta: 1500 movimientos por la API (`POST /movimientos` con el token del admin) sobre los productos `PRUEBA-3C1-*` que siguen en la base (ids 34 a 1033): entradas por Compra con precio y salidas por Uso o consumo, uno con un comentario de 300 caracteres, y una salida con `"forzar": true` que deje un stock negativo. Anotar el rango de ids en el ledger.
- WSLg y `scripts/gui-verificacion/`. MIRAR cada captura. Clicks en vez de teclas especiales.

Todo a **1366×768**; los pasos 1 a 4 se repiten a 1280×760. Capturar cada pantalla.

1. **Cargando:** desde Inicio, `kill -STOP` SOLO del pid de `StockApp.Api` (acotado por nombre) y entrar a Historial → "Cargando movimientos…" sin vacío ni grilla. `kill -CONT` → aparece la grilla. Anotar cuánto tarda la carga de los ~1500 movimientos (cronómetro desde el click hasta la grilla, dos veces) para la deuda U2.
2. **Volumen y columnas:** scrollear de punta a punta en vertical y en horizontal y ordenar por FECHA, CANTIDAD, PRECIO y USUARIO: ningún ancho cambia (capturas al principio y al final, `comparar-png` del header). FECHA, CANTIDAD y PRECIO enteros; las salidas con "—"; la fila negativa con la insignia y el número rojo. El comentario largo cortado con tooltip completo; uno corto sin tooltip.
3. **Carrera:** entrar a Historial y, mientras carga, elegir Tipo "Salida" y apretar Buscar: al terminar, la grilla muestra solo salidas y el pie coincide. Si la carga completa es demasiado rápida para superponerse, repetir con la API pausada (`STOP` antes de entrar, elegir el filtro y Buscar con la API pausada, `CONT`) y anotar el orden en que llegaron; si igual no se puede provocar, anotarlo (lo custodian los tests de las Tasks 2 y 7).
4. **Sin permiso y filtro:** como `f3c1mixto`: no ve "Recalcular stock"; ve el filtro Producto. Quitarle SOLO `catalogo.productos` por `PUT`, buscar un producto en el filtro → aviso global y el filtro desaparece (D3); después restaurar. Para "Sin permiso" en la card: quitarle `movimientos.registrar` con la pantalla abierta y apretar Buscar → aviso y "Sin permiso / No tenés permiso para ver el historial de movimientos." en lugar de la card. Restaurar los permisos guardados y verificarlo con el `GET`.
5. **Recalcular:** como admin, "Recalcular stock" → diálogo; "Recalcular" deshabilitado; buscar "PRUEBA" (rueda del mouse para la lista), elegir uno → habilitado; Recalcular → "Stock recalculado." y la grilla conserva el filtro aplicado antes (D11). Cancelar no hace nada. Con `f3c1mixto` + `stock.recalcular` temporal y SIN `catalogo.productos`: el diálogo abre, buscar devuelve nada y sale el aviso global del 403 (D14); anotar si el aviso aparece sobre el diálogo o detrás. Restaurar.
6. **Productos (U3):** en Productos, tipear rápido un nombre y borrar, y eliminar un producto de prueba mientras una búsqueda está en curso: la grilla termina con lo último pedido.
7. Datos de prueba: listar los ids creados y PREGUNTAR al usuario si se borran (SQL listo, acotado por rango de ids y prefijo, SOLO contra `stockapp-pg` local, nunca el VPS). El contenedor queda andando.

Si un paso no se puede hacer por el entorno, PARAR y avisar con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa.

- [ ] **Step 3: Aprobación final del usuario**

PARAR. Presentar: `index.html` (filas de Historial y del diálogo), las capturas de la verificación orgánica junto a los mockups, los rojos de las mutaciones (a) a (v), el tiempo de carga medido y los cambios respecto de los mockups aprobados. Registrar: `Fase 3c-2: aprobada por <quién> <fecha>`.

- [ ] **Step 4: `herramientas.md` y spec**

`herramientas.md`, después de la 22: "Convenciones que agrega la Fase 3c-2 (Historial de movimientos)" con la 23 y la 24 de este plan, y las frases nuevas en la 10 y la 19 (como quedaron aprobadas).

Spec:
1. `**Estado:**`: "; Fase 3c-2 (Historial de movimientos) APROBADA por el usuario el <fecha>" y "3c-2" en la lista de fases implementadas.
2. §7, después de "Estado de la Fase 3c-1": "**Estado de la Fase 3c-2 (aprobada el <fecha>).** Historial de movimientos v2 (listado §5.1 con barra de filtros etiquetada sin buscador libre, 10 columnas con scroll horizontal a 1280 y 1366, 7 medidas, STOCK NUEVO con insignia, PRECIO "—" cuando no aplica), diálogo v2 de Recalcular stock (Window del patrón de la Fase 1, no el modal de la 3c-3), carrera de consultas cerrada con `ConsultaVigente` en Historial y Productos, y las medidas vuelven a su ancho cuando igual hay scroll (cambia la regla de la 3c-1 para ese caso). Comparado en <N> filas a los dos tamaños (<máximo de teselas densas>). Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D15 aprobadas o cambiadas>; excepciones a §3 (U1 y U3, ya asentadas en §3)."
3. "Deuda abierta tras la Fase 3c-1": tachar con `~~…~~` y agregar "Cerrada (Fase 3c-2): …" en la carrera de búsquedas de Productos y en "Por debajo de 886 px internos…" (cerrada por D5 si se aprobó).
4. "**Deuda abierta tras la Fase 3c-2:**" con, como mínimo: **(U2) Historial carga TODO el historial sin límite**: `CargarAsync` pide `new HistorialMovimientoFiltro()` y `ColumnaAuto` mide toda la fuente en el hilo de UI en cada carga; probado hasta 5000 filas (`ColumnaAutoTests.CincoMilFilas_MedirNoAgregaCostoPerceptible`); umbral orientativo **5000 movimientos** (a ~15-20 movimientos por día hábil, unos 250 días hábiles por año: entre uno y un año y medio de uso); por encima, evaluar un rango por defecto (p. ej. últimos 90 días) o paginado en la API; medido en la verificación orgánica: <tiempo> con <n> movimientos. Además: el 403 del buscador del diálogo para quien tiene `stock.recalcular` sin `catalogo.productos` (D14, preexistente; lo que se vio en el Step 2.5); cualquier `UmbralAprobado`; los ~20 ViewModels con `catch (UnauthorizedAccessException)` propio que siguen sin `ConsultaVigente` ni `Cargando`; los minors diferidos del ledger.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 3c-2 (Historial de movimientos)"
```

- [ ] **Step 5: Review final de la rama**

Review de todo el rango `dd4ac7a..HEAD` (skill de ejecución: review de rama completa en el modelo más capaz), con la suite y las comparaciones ya verdes. Los hallazgos se resuelven en commits propios o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- **3c-3:** `IngresoPorFacturaView` y sus dos overlays (el patrón de modal v2). Este plan no define nada del overlay.
- **U2:** límite, rango por defecto o paginado del historial; cambios de API.
- `HistorialPorProductoView` (Reportes): sigue v1; solo recibe el guardián de hueco que se muda (Task 7 Step 3).
- Alerta con "Reintentar" para la carga del Historial con el servidor caído (convención 22 es de formularios; el listado sigue con el modal global, como Productos).
- Los ~20 VMs con `catch` propio; el formulario de Productos sin indicador; el sidebar que corta el último ítem a 1366×768 (diferido por el usuario); modo oscuro, back-stack (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| El diálogo se lee como "el modal v2" y la 3c-3 hereda una decisión que no tomó. | La tabla de diseño explica con evidencia por qué es el patrón `Window` de la Fase 1 y no el overlay; D10 lo presenta en el GATE con la alternativa overlay; la spec §7 lo deja dicho al cierre. **Si el usuario elige overlay en el GATE, este plan adelanta el patrón de la 3c-3 y cambia su alcance: PARAR, avisar y re-planificar la Task 6 (y anotar en la spec que la 3c-3 hereda el overlay de Recalcular).** |
| D5 cambia una regla aprobada en la 3c-1 y rompe tests o pantallas que la asumían. | Se decide en el GATE con el antes/después; afecta solo al caso "ni cediendo entra" (Productos a 1280 y 1366 no cae ahí); `ColumnaAutoTopeAdaptableTests` se adapta con nombre nuevo y mutación (f); regresión completa de grillas en las Tasks 5 y 7; todo número de test heredado que cambie se reporta con motivo. |
| La grilla de Historial no entra a 1280 ni 1366: scroll horizontal con COMENTARIO fuera de vista. | Es el estado de hoy (test `G_HistorialEnShellA1366_…`); D4 lo hace explícito y ofrece la alternativa B medida; el mockup dibuja la barra con valores medidos de la app (Task 4 Step 5) y la comparación la ve. |
| La barra de scroll medida en la vista v1 no es la que pinta la vista v2 (tema distinto) y la comparación da rojo por la barra. | Task 4 Step 5 lo anticipa; se re-mide en la Task 7 antes de las comparaciones; si la diferencia es solo de la barra, se presenta al usuario por tesela (nunca subir el umbral sin su OK). |
| El `AutoCompleteBox` v2 (filtro y diálogo) nunca se comparó contra un mockup: su estilo v2 solo fija fuente y color. | El mockup copia el `v2-input`; si la comparación muestra otra altura, borde o padding, se agrega un estilo `.ui2 AutoCompleteBox /template/ TextBox` en `ControlesV2.axaml` con su test (y se corre el catálogo: D15). |
| `FechaHoraLocalConverter` depende del huso de la máquina: mockup y test podrían dar otra hora en CI o en otra PC. | `DatosHistorial.Fecha` arma hora local fija → UTC (test `Fecha_EsUnaHoraLocalFija_EnCualquierHuso`); los UiTests usan el mismo helper. |
| `BuscarCommand` de Productos no admite ejecuciones concurrentes y el test de dos búsquedas no llega a superponerlas. | Task 1 Step 5 indica la variante por debounce y exige reportarla. |
| El constructor nuevo del VM rompe la composición DI o un sitio de construcción olvidado. | Solo 2 sitios en tests (grep en el plan) + `DatosHistorial`; `ComposicionDIApiTests` en la Task 6. |
| Un operador con `stock.recalcular` sin `catalogo.productos` ve el aviso global de 403 al buscar en el diálogo, quizás detrás del modal (WSLg pierde foco). | D14 evita la excepción sin observar; el comportamiento del aviso se observa en la verificación orgánica (paso 5) y queda en la deuda con lo visto. |
| Crear ~1500 movimientos cambia stock de productos de prueba y ensucia la base local. | Se pregunta antes; solo sobre `PRUEBA-3C1-*`; ids anotados; borrado con SQL acotado y con OK del usuario; nunca el VPS. |
| `GrillaEspacioDerechoVistaRealTests` pierde su vista v1 de referencia. | Se muda a `HistorialPorProductoView` con la misma precondición; si no corta, se para y se consulta. |
| Scripts o capturas fuera del worktree; WSLg congelado; `capturar.sh` no fail-closed. | `cd <worktree> &&` y `ls -l --time-style=full-iso`; mirar cada captura; capturas idénticas con procesos nuevos = congelada. |
