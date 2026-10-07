# UI v2 — Fase 3a Productos (listado y formulario) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar `ProductoListView` (listado §5.1: encabezado, buscador, grilla con stock negativo e inactivos, pie de conteo, estado vacío) y `ProductoFormView` (formulario §5.2: breadcrumb, card de 760 con secciones, campos a 2 columnas, botones al pie, error y sin permiso) al sistema v2, comparadas contra sus mockups aprobados a 1280×760 y 1366×768, sin tocar sus ViewModels.

**Architecture:** Primero se fijan los datos de ejemplo que comparten mockups y tests y se miden en headless los anchos reales de las columnas `Auto` (Bloque A). Con eso se escriben los mockups del listado (2 estados) y del formulario (4 estados) y el usuario los aprueba en dos GATE (Bloque B). Recién ahí se reescribe el XAML de cada vista en su mismo archivo (code-behind intacto), con lo que la grilla necesita dentro de la misma task (clases `inactivo`/`negativo` en `DataGridV2.axaml`, conversor de conteo, `TextBox` deshabilitado), se adaptan los tests heredados que miraban tipos del sistema viejo y se comparan las pantallas dentro del shell real con el sidebar oculto (Bloque C). Cierra la suite completa, la verificación orgánica a 1366×768 y la aprobación final (Bloque D).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Optris.Icons.Avalonia 12.0.7 (prefijo `mdi`), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`). Python 3 con Pillow (solo diagnóstico de teselas, nunca commiteado).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado; §2 tokens; §3 enfoque; §4.4 breadcrumb; §5.1 listado; §5.2 formulario; §6 fidelidad, §6.5 calibración y compensaciones; §7 fila "3 Inventario" y deuda abierta; §8.1; §9; §10). Herramienta: `docs/ui-v2/herramientas.md`, `scripts/ui-v2/comparar.sh`. Planes de los que hereda formato y convenciones: `docs/superpowers/plans/2026-10-01-ui-v2-fase-1-shell-y-acceso.md` (convenciones 1 a 5) y `docs/superpowers/plans/2026-10-06-ui-v2-fase-2-inicio.md` (convenciones 6 a 9).

Todos los comandos se corren desde la raíz del worktree: `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base `593069a`). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código; nunca el índice de otro checkout. Para markdown (spec, planes, `herramientas.md`) y HTML/CSS de mockups, Read directo: CodeGraph no indexa prosa.

## Decisiones del usuario (2026-10-06)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **La Fase 3 se parte en tres:** 3a Productos (listado + formulario), 3b Entrada/Salida + `MovimientoFormControl`, 3c Ingreso por factura + Historial de movimientos. Cada una con su ciclo mockups → GATE → XAML → comparación → verificación orgánica → aprobación. **Este plan cubre SOLO 3a.** | `MovimientoFormControl`, `EntradaRegistroView`, `SalidaRegistroView`, `IngresoPorFacturaView` y `MovimientoHistorialView` no se tocan (siguen en el tema viejo y en `VistasDeLaTanda`). | todas |
| U2 | **Lo que la spec no fija del diseño lo propone el mockup aplicando §5.1/§5.2 y lo decide el usuario en el GATE.** | Lista D1 a D18 de "Decisiones que se confirman al aprobar los mockups". Si cambia alguna, se ajusta el mockup y lo que dependa de él (anchos, textos, tests escritos en este plan) en la misma task del mockup, antes del XAML. | 2, 3 |
| U3 | **Producto inactivo = `Insignia` "Inactiva" + texto de la fila en color terciario por token. Se elimina el uso de `Opacity` por celda (`ActivoOpacidadConverter`) en la vista migrada.** | Clase `inactivo` en cada `TextBlock` de celda (`DataGridV2.axaml`: `Foreground` = `V2TextoTerciarioBrush`); la insignia conserva su color pleno. `ActivoOpacidadConverter` NO se borra (lo usan otras vistas del tema viejo). Test por efecto con la View real: `ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad`. | 4 |
| U4 | **Grillas densas: datos de mockup realistas; si las teselas densas pasan de 11, se presenta CADA tesela al usuario y se usa `UmbralAprobado` solo con su OK. Nunca subir umbrales sin aprobación.** | Doce productos con nombres, unidades y categorías reales de un depósito municipal (Task 1). Procedimiento de umbral con GATE dentro de las Tasks 4 y 5 (Step "Umbral"). | 1, 4, 5 |
| U5 | **Sin deuda extra en 3a:** NO entran `capturar.sh` fail-closed, ni un guardián de `Opacity` literal, ni una task separada de preparación de `DataGridV2`. Lo que `DataGridV2` necesite para Productos (columna de insignia, selección de fila, scroll horizontal con `MinWidth`, texto atenuado de inactivos) va dentro de las tasks de la pantalla. | Las clases `inactivo`/`negativo` y su espejo CSS entran en la Task 4; el `TextBox` deshabilitado, en la Task 5. Ver "Fuera de alcance". | 4, 5 |
| U6 | **El patrón de modal v2 (para Ingreso por factura) queda para 3c.** | Fuera de alcance. La confirmación de baja de Productos sigue usando `IConfirmacionService` (diálogo `ConfirmacionDialog`, ya v2 desde la Fase 1). | — |
| U7 | **Ejecución subagent-driven:** implementador por task + reviewer + re-review de fixes; reviews de diseño/diagnóstico en Opus; los GATE devuelven el control al usuario. | GATE en Tasks 2, 3, 4 (Step de umbral si hace falta), 5 (ídem) y 6 (aprobación final). | todas |
| U8 | **Stock negativo:** `Insignia` peligro "Stock negativo" + número en `#B91C1C` (spec §5.1). **No se implementa stock bajo.** | El número negativo usa la clase `negativo` (`V2PeligroTextoBrush`, `#B91C1C`), NO `SignoNegativoBrushConverter` (devuelve `DangerBrush` = `#DC2626`, ver "Decisiones de diseño"). `StockMinimo` no se expone. | 4 |

## Decisiones que se confirman al aprobar los mockups (Tasks 2 y 3)

No bloquean el arranque: son propuestas del plan sobre lo que la spec no fija (U2). El GATE las presenta numeradas; las que el usuario cambie se aplican en el mockup y en los tests de este plan que las fijan (indicados entre paréntesis), en la misma task del mockup.

**Listado (Task 2):**

- **D1 — Acciones.** Slot de `EncabezadoPagina`: "Editar" (`secundario`), "Eliminar" (`peligro`), "Nuevo" (`primario`, a la derecha, el único primario). `BarraFiltros`: buscador + "Buscar" (`secundario`) en su slot de filtros. Sin acciones por fila. "Editar" y "Eliminar" quedan deshabilitados sin selección o con un producto inactivo seleccionado (regla actual del VM, `PuedeOperarSobreSeleccionado`). Hoy los cuatro botones van en el `HeaderVista`. (Tests: `UnSoloPrimario_YEsNuevo`, `EditarYEliminar_*`.)
- **D2 — Marca de agua del buscador.** El texto real "Buscar por nombre, SKU o código de barras..." (~300 px a 13 px) no entra en el buscador de 280 de `BarraFiltros` (área de texto 254): el guardián de recorte lo rechazaría. Propuesta: **"Nombre, SKU o código de barras"**. Alternativa: ensanchar el buscador solo en esta pantalla (cambia `BarraFiltros`).
- **D3 — Sin la etiqueta "Buscar".** `BarraFiltros` no tiene etiqueta y el patrón aprobado (`direccion-visual-aprobada.html`) tampoco la muestra. `EtiquetadoDeCamposTests.ProductoListView_FiltroBusqueda_TieneEtiquetaCampoFormulario` se reemplaza por uno que exige `BarraFiltros` con marca de agua.
- **D4 — Pie de conteo:** "12 productos" / "1 producto", con separador de miles es-UY; cuenta lo que muestra la grilla (después de la búsqueda). Se oculta con la lista vacía.
- **D5 — Estado vacío:** `EstadoVacio` "No hay productos para mostrar" / "Probá con otra búsqueda o creá uno con Nuevo." en lugar de la grilla y del pie; el mismo texto para catálogo vacío y búsqueda sin resultados (el VM no distingue los casos y no se toca).
- **D6 — Columnas.** Las 7 actuales en el orden actual, headers en MAYÚSCULAS (CÓDIGO, NOMBRE, COSTO, STOCK, UNIDAD, CATEGORÍA, ESTADO) y los anchos actuales (Código `Auto`, Nombre `2*`, Costo `Auto` mín. 100, Stock 220, Unidad `Auto`, Categoría `*`, Estado `Auto`). Los `Auto` se miden en headless (Task 1) y el mockup copia los píxeles. Estimado: a 1280 y a 1366 la grilla entra sin scroll horizontal; Categoría queda en su piso de 160.
- **D7 — Stock negativo en la celda de Stock** (insignia a la izquierda, número a la derecha), como hoy, no en la columna Estado. Un producto inactivo con stock negativo conserva el número en rojo.
- **D8 — Inactivo:** insignia neutra **"Inactiva"** (texto real actual; alternativa "Inactivo", que concuerda con "producto") + texto de la fila en terciario `#78716C`. La insignia no se atenúa.
- **D9 — La grilla ocupa todo el alto de la card**; el mockup muestra 12 filas sin scroll vertical (a 1280×760 entran 12) y ninguna fila seleccionada (por eso "Editar" y "Eliminar" se ven deshabilitados).
- **D10 — Listado sin permiso: queda como hoy.** `CargarAsync` traga el `UnauthorizedAccessException` y el aviso global lo da `App` ("Tus permisos cambiaron..."). Sin estado propio ni mockup (cambiarlo exigiría tocar el VM).
- **D11 — Sin subtítulo ni eyebrow** en el encabezado del listado: título "Productos"; el conteo va en el pie. Hoy hay eyebrow "CATÁLOGO".

**Formulario (Task 3):**

- **D12 — Secciones y columnas.** Card de ancho 760 alineada a la izquierda, padding 24. Sección "Datos del producto": SKU | Código de barras; Nombre (ancho completo); Descripción (ancho completo). Separador de 1 px `#F0EFED` con 20 arriba y abajo. Sección "Costo y clasificación": Precio costo | Unidad de medida; Categoría | (vacío). Títulos de sección 13/600 (`subtitulo-formulario`). Hoy: una columna de 480 sin secciones.
- **D13 — Descripción multilínea de 88 de alto** (la medida del token del Reset); hoy 60.
- **D14 — SKU deshabilitado en edición** con fondo `#F5F5F4`, borde `#E7E5E4` y texto `#A8A29E` (tokens de deshabilitado ya usados por los botones). Hoy toma el deshabilitado de Fluent.
- **D15 — Botones al pie, a la derecha:** "Cancelar" (`secundario`) y "Guardar" (`primario`), con una línea `#F0EFED` arriba (24 de margen, 16 de padding), como `patrones-1.html`. Hoy "Guardar" va primero, a la izquierda. El texto "Guardar" se conserva (no "Guardar producto").
- **D16 — Error de guardado como `Alerta` error** encima del pie, con el texto real del servicio (ej.: "Ya existe un producto con el código 'MAT-0142'."). Hoy es un `TextBlock` rojo.
- **D17 — Breadcrumb "Productos / Nuevo producto"** (o "/ Editar producto"); el primer tramo ejecuta `CancelarCommand` (vuelve sin guardar). Título = el `Titulo` actual del VM.
- **D18 — Sin permiso:** `EstadoVacio` "Sin permiso" con el mensaje real ("No tenés permiso para cargar los datos de este producto.") EN LUGAR de la card; el encabezado con breadcrumb queda (se puede volver). Hoy el estado se superpone a la card.

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10).
- Fondo app `#F7F7F5` · Superficie `#FFFFFF` · Borde `#E7E5E4` · Borde input `#D6D3D1` · Separador de grilla `#F0EFED` · Header de grilla `#FAFAF9`.
- Acento `#1D4ED8` · Acento hover `#1E40AF` · Acento suave `#DBEAFE` · Acento oscuro `#1E3A8A`.
- Texto primario `#1C1917` · secundario `#57534E` · terciario `#78716C` · etiqueta `#44403C` · grupo `#A8A29E`.
- Peligro texto `#B91C1C` · Borde error `#DC2626`.
- Insignias (fondo/texto): Neutro `#F5F5F4`/`#44403C` · Advertencia `#FEF3C7`/`#92400E` · Peligro `#FEE2E2`/`#991B1B` · Éxito `#DCFCE7`/`#14532D`.
- Alertas (fondo/borde/texto): Advertencia `#FEF3C7`/`#FDE68A`/`#92400E` · Error `#FEE2E2`/`#FCA5A5`/`#991B1B` · Info `#DBEAFE`/`#DBEAFE`/`#1E3A8A`.
- Radios: 8 (inputs, botones, alertas, borde de la grilla), 10 (`TarjetaContenido`), 999 (insignias). Sombra de card `0 1px 2px rgba(0,0,0,0.05)`, la única.
- Alturas: input y botón 36; fila de grilla 40 (RowHeight 39 + 1 de separador, §6.5); header de grilla 36.
- Espaciado base 4 (excepción: padding vertical 2 de la `Insignia`; compensaciones medidas en spec §6.5). Anchos fijos: sidebar 240 (a 1280 quedan 1040 para la vista, a 1366 quedan 1126); card de formulario máx. 760.
- Tipografía: título de página 22/600 alto de línea 26 · texto de controles y celda 13 · subtítulo de sección de formulario 13/600 · etiqueta de campo 12/500 · insignia 12/500 padding 2×8 · header de columna 11/600 MAYÚSCULAS letter-spacing 0.6 terciario sobre `#FAFAF9` · pie de conteo en terciario. Grillas sin zebra; numéricos a la derecha con cifras tabulares.
- Productos (§5.1): conserva sus columnas (Código, Nombre, Costo, Stock, Unidad, Categoría, Estado) y el tratamiento de stock negativo (`Insignia` peligro "Stock negativo" + número `#B91C1C`). No se implementa stock bajo.
- Formulario (§5.2): breadcrumb; card máx. 760 con secciones tituladas; 2 columnas donde los campos lo permitan; cada campo un `CampoFormulario`; un solo primario; botones al pie a la derecha; textos reales ("Guardar").
- **ViewModels intactos** (`ProductoListViewModel`, `ProductoFormViewModel`): esta fase no los modifica. No cambian `ViewLocator`, `INavigationService`, el seam `ErrorConverter` ni la lógica de permisos.
- **Code-behind intacto:** `ProductoListView.axaml.cs` (`DataContextChanged` → `CargarAsync`) y `ProductoFormView.axaml.cs` (`DataContextChanged` → `InicializarAsync`) NO se tocan: las vistas de Avalonia no se autoinicializan. Ninguna de las dos vistas se suscribe a `Cambiado` (verificado): no hay desuscripción que conservar.
- **No se renombran ni se mueven** `ProductoListView.axaml(.cs)` ni `ProductoFormView.axaml(.cs)` (`ReflexionVistaViewModelTests`, `ViewLocatorTests`, `ViewLocator` por convención de nombre).
- `Classes="ui2"` en la RAÍZ de cada vista migrada (ninguna de las dos hospeda vistas no migradas). Con `ui2` aplica `Themes/V2/DataGridV2.axaml` ENCIMA del tema viejo `Themes/DataGrid.axaml`, que sigue dando `DataGridColumnasAutoBehavior` (piso 160 de las columnas `*`), `MinColumnWidth` y el resize; el fix `3548397` (MinWidth por columna + scroll horizontal) se conserva: `MinWidth` por columna y `ScrollViewer.HorizontalScrollBarVisibility="Auto"` siguen en la grilla. No se declaran anchos de peor caso nuevos ni se repiten esos setters en la vista.
- Dentro de una vista v2, todo `TextBlock` lleva una clase tipográfica v2 o sus propiedades tipográficas desde recursos `V2*` (o hereda de un control v2 que las fija: celda de grilla, `ComboBox`). Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`; tokens `V2*`; variables CSS `--v2-*`. Cada clase `v2-*` de `base.css` replica la geometría de su estilo: si cambia uno, cambia el otro en el mismo commit.
- Formato es-UY: todo número, importe y fecha de una vista v2 con `FormatoEsUy` o sus converters actuales (`MonedaConverter`, `CantidadConverter`, `DecimalConverter`). Nunca un `StringFormat` suelto en el XAML. "Precio costo" conserva `DecimalConverter` + `EntradaNumericaBehavior.EsDecimal`.
- Errores de entrada (convención 3): `GuardarCommand` sigue creado con `ComandoDeGuardado` (no se toca el VM); el botón NO lleva `ToolTip.Tip` ni `ShowOnDisabled` a mano. `ComandosDeGuardadoGuardianTests` ya reconoce `Classes="primario"` (L31, L161) y exime `NuevoCommand` como navegación (L73-75).
- Mockups (§6.1): `docs/ui-v2/mockups/inventario/<pantalla>.html`, el mismo HTML a 1280×760 y a 1366×768, `../_base/tokens.css` + `../_base/base.css`, todo dentro de UN `<div class="v2-ventana">`, datos fijos idénticos a los de `DatosProductos` (Task 1). Solo colores sólidos, bordes de 1px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. Celdas numéricas con un único `<span>` (`.v2-num > span`; lo custodia `MockupsDelRepo_RespetanLaConvencion`). Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (11 teselas densas, 1,68%) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado` registrado tras revisar CADA tesela densa con el usuario (U4); 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto. Antes de proponer un umbral: separar geometría (perfiles normalizados, `teselas.py`) de tono (Skia vs Chromium) y verificar familia, tamaño, peso, color y alto de línea del mockup contra el XAML (sección "Herramientas de diagnóstico").
- Diferencia de tono aceptada (spec §6.5): texto de la app entre 7% y 26% más claro que en Chromium según estilo (texto 400 sobre `#1C1917` de 13-14 px: 23,5-26%). Lo que se exige idéntico es la geometría.
- **Convención 6:** las dos pantallas se comparan DENTRO del shell real (`ShellMainView` con `CurrentContent` = el VM, resuelto por `ViewLocator`) con el sidebar oculto en ambos renders (`prepararApp` / `ocultarEnMockup: [".v2-sidebar"]`). Un guardián visual mide la View REAL dentro de `.ui2` y del shell real: nada de grillas sintéticas para custodiar estilos de esta pantalla.
- **Gates de permisos:** dentro de `ProductoListView` y `ProductoFormView` NO hay gates (verificado: ningún `IsVisible` de permiso en los dos `.axaml`; los VM no reciben `ICurrentSession`). El gate de la pantalla es el ítem "Productos" del sidebar y el acceso de Inicio (`GestionarProductos`), ya custodiados por `ShellMainViewGatesTests` e `InicioViewV2Tests` con usuarios mixtos. La verificación orgánica (Task 6) se hace con un usuario de permisos MIXTOS además de Admin (Admin cortocircuita y no prueba nada; un test de VM no custodia un gate de UI).
- **Tests de flujo disparan el comando real** (click real en el botón o en el breadcrumb, con `NavigationRecorderFake`), nunca llaman `Navegar` a mano.
- **Verificación por mutación QUITANDO.** `export SCRATCH=~/.cache/stockapp-ui-v2-fase-3a && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`). Procedimiento: `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla (Edit o un `python3` que reemplace el texto exacto; `sd` no está instalado); `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO lo quitado (una mutación que no se aplica deja verde en silencio); se corre el test y se PEGA el rojo real en el reporte (no razonarlo); se restaura con `cp "$SCRATCH/<nombre>.bak" <archivo>`; `git diff --no-index` vacío; se ve el verde. Nunca se agrega un efecto destructivo para mutar. NUNCA `git checkout --`.
- Tests que esperan un `Task` del VM: `Assert.True(task.IsCompleted)` antes de leer el resultado; para comandos que navegan con `Task.Run` (`NuevoCommand`, `EditarCommand`), esperar con `SpinWait.SpinUntil(..., TimeSpan.FromSeconds(5))` y afirmar el `true` (si no, una mutación cuelga el runner o pasa por carrera).
- Los comentarios de `.axaml` y `.csproj` NO pueden contener `--` (AVLN1001, cascadea a MSB4025). En vistas con `x:DataType`, un binding mal escrito es error de build.
- **Build obligatorio** tras cada cambio de XAML: `dotnet build StockApp.sln`. Testear con la solución o con UN `.csproj`; nunca varios `.csproj` en un solo `dotnet build` (falso verde). Versiones de paquetes SOLO en `Directory.Packages.props` (esta fase no agrega paquetes).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos (confirmar por nombre).
- Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test, y repetir.
- **Sin referencias al plan en comentarios del código** (nada de "Task 4", "D7", "Fase 3a, Step 2" en `.cs`/`.axaml`/`.css`/`.html`): los comentarios explican el porqué con la regla o la medición, no el número de task. Los mensajes de commit y la spec sí pueden citar el plan.
- La ventana de la app se llama "Gestión Municipal" (la buscan los scripts de `scripts/gui-verificacion/`). `capturar.sh` NO es fail-closed con WSLg caído (devuelve exit 0 capturando el escritorio de Windows): mirar CADA captura antes de usarla; si WSLg falla, la verificación orgánica la hace el usuario.
- Commits: conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). No se pushea. `feat/ui-v2` no se mergea a `main` hasta terminar todas las fases; si entra algo a `main` durante la fase, se mergea `main` → `feat/ui-v2` antes del Bloque C.
- Subagentes: prohibido `git reflog expire`, `git gc`, `git prune` y `git checkout --` sobre trabajo ajeno.
- Las referencias de línea (`L123`) de este plan son las del commit `593069a`. Después de que una task edita un archivo, ubicar el lugar por el texto citado, no por el número.

## Convenciones que heredan las fases siguientes (3b a 8)

Siguen vigentes las cinco de la Fase 1 (1366×768, formato es-UY, errores de entrada, grillas, toolkit GUI) y las cuatro de la Fase 2 (6: pantallas de contenido dentro del shell real; 7: `GrillaUniformeV2`; 8: reloj inyectable; 9: `V2TemaFilaPanel`). Esta fase agrega (las que dependen de una D# quedan como la apruebe el usuario):

10. **Listado v2.** `EncabezadoPagina` (acciones secundarias y UN primario a la derecha) + `TarjetaContenido` con `BarraFiltros`, grilla que ocupa el resto del alto y pie de conteo. El conteo es `{Binding Items.Count, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='singular|plural'}`; el estado vacío y la visibilidad de grilla y pie usan `ConteoConverter.EsCero` / `ConteoConverter.HayElementos` sobre `Items.Count`. Bindear la COLECCIÓN entera (o `ColeccionVaciaConverter`) no se re-evalúa al hacer `Clear`/`Add`; `Count` sí, porque `ObservableCollection` lo notifica.
11. **Fila inactiva y número negativo por token, nunca por `Opacity`.** Cada `TextBlock` de celda lleva `Classes.inactivo="{Binding !Activo}"` (texto terciario) y la columna de estado una `Insignia` neutra; un número negativo lleva `Classes.negativo` (`#B91C1C`), que gana sobre `inactivo`. `SignoNegativoBrushConverter` y `ActivoOpacidadConverter` son del tema viejo: no se usan en vistas v2. Espejo: `.v2-grilla-fila--inactiva` y `.v2-texto-peligro` en `base.css`.
12. **Formulario v2.** Breadcrumb al comando de vuelta que el VM ya expone (en Productos, `CancelarCommand`; en otros, `Volver`), card de máx. 760 a la izquierda (`Grid` con una columna `*` de `MaxWidth` 760), padding 24, secciones con `subtitulo-formulario` y separador de 1 px `#F0EFED` con 20 de margen, `Alerta` error para el error de guardado, pie con borde superior y botones a la derecha (Cancelar `secundario`, Guardar `primario`). El estado sin permiso reemplaza la card, no se superpone. `TextBox` deshabilitado con los tokens de deshabilitado.
13. **Tests heredados que buscaban tipos del sistema viejo** (`BadgeEstado`, `Controls.CampoFormulario`, `Classes="secondary"`, headers en minúscula) se adaptan para aceptar AMBOS sistemas mientras convivan (Fase 9 limpia): `GrillaStockHelpers`, `GuardarBloqueadoConErroresDeEntradaTests.CajaDeCampo`, `CatalogoFormAnchoYCancelarTests.BotonCancelar`. Nunca se borra un test heredado sin reemplazarlo por uno que custodie lo mismo sobre la View v2.
14. **Columnas `Auto` en el mockup:** se miden en headless con el tema v2 y los datos fijos ANTES de escribir el mockup, y el mockup las copia como `flex: 0 0 Npx`. Si tras el XAML la comparación muestra una columna corrida, se re-mide y se corrige el mockup (un cambio ≤ 4 px no visible al ojo se anota en el ledger; uno mayor vuelve al usuario).

## Review Focus

1. **Producto inactivo que se ve activo, o con la insignia atenuada.** Con la migración el inactivo deja `Opacity` 0,55 por celda; si la clase `inactivo` no se aplica, el inactivo se ve igual que uno activo; si alguien vuelve a atenuar la fila, la insignia "Inactiva" queda ilegible. Esperado: todas las celdas de texto del inactivo en `#78716C`, ninguna `Opacity` < 1 entre la celda y la grilla, la insignia con su color pleno. Test: `ProductoListViewV2Tests.ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad` (Task 4) y la comparación `Productos_IgualAlMockup`.
2. **Stock negativo en el rojo equivocado o tapado por la insignia tras ordenar.** `SignoNegativoBrushConverter` pinta `#DC2626`, no el `#B91C1C` de la spec; y al ordenar por COSTO o STOCK las celdas recicladas pueden dejar el número corrido o pisado por la insignia. Esperado: número `#B91C1C` (también en un inactivo), insignia peligro solo en la fila negativa, números alineados y sin recorte tras cada orden. Tests: `StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto`, `InactivoConStockNegativo_ElNumeroSigueEnRojo` (Task 4) y `ProductoListViewStockOrdenTests` adaptado (headers en mayúsculas, `Insignia`).
3. **Editar o Eliminar sobre un inactivo o sin selección.** La regla vive en el VM (`PuedeOperarSobreSeleccionado`); si el XAML bindea otro comando o pierde el `Command`, el botón queda habilitado o muerto. Esperado: deshabilitados sin selección y con un inactivo; habilitados con un activo; el click real de "Eliminar" pide confirmación y da de baja ESE producto; "Editar" abre el formulario con ESE producto. Tests: `EditarYEliminar_SinSeleccion_Deshabilitados`, `EditarYEliminar_ConInactivoSeleccionado_Deshabilitados`, `Eliminar_ClickReal_ConfirmaYDaDeBajaElSeleccionado`, `Editar_ClickReal_AbreElFormularioConElSeleccionado` (Task 4).
4. **Lista vacía o búsqueda sin resultados que deja una grilla vacía muda, o un pie que miente.** Con `Items.Clear()` el estado tiene que aparecer, y al volver los datos tiene que irse. Esperado: sin productos, `EstadoVacio` visible y grilla y pie ocultos; al llegar datos, grilla visible y pie "N productos" con miles es-UY. Tests: `ListaVacia_MuestraEstadoVacio_YAlLlegarDatosVuelveLaGrilla`, `PieDeConteo_SingularYPluralConMilesEsUy` (Task 4), `ConteoConverterTests` y la comparación `ProductosVacio_IgualAlMockup`.
5. **El breadcrumb o "Cancelar" guardan, o "Guardar" con error navega igual.** Esperado: el primer tramo del breadcrumb y "Cancelar" vuelven al listado sin llamar al servicio; "Guardar" con un error de dominio muestra la `Alerta` con el texto real y NO navega; con permiso denegado se ve "Sin permiso" y no la card. Tests: `Breadcrumb_ClickReal_VuelveAlListadoSinGuardar`, `Guardar_ConErrorDelServicio_MuestraLaAlertaYNoNavega`, `SinPermiso_MuestraElEstadoEnLugarDeLaTarjeta` (Task 5) y `CatalogoFormAnchoYCancelarTests` adaptado.

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia |
|---|---|---|
| ViewModels sin cambios | Todo lo que la vista v2 necesita ya existe: `Items`, `ItemsView`, `ItemSeleccionado`, `FiltroBusqueda`, `BuscarCommand`, `NuevoCommand`, `EditarCommand`, `BajaCommand` (lista); `Titulo`, `EsEdicion`, `MensajeError`, `SinPermiso`, `MensajeSinPermiso`, `GuardarCommand`, `CancelarCommand` (formulario). | `ProductoListViewModel.cs` L38 (`_filtroBusqueda`), L40-43 (`ItemSeleccionado` notifica Baja/Editar), L45, L54, L122, L138-140, L146-147, L155-163, L171-189. `ProductoFormViewModel.cs` L65-67 (`EsEdicion` → `Titulo`), L58 (`MensajeError`), L87 (`Titulo`), L134-162 (`EjecutarCargaProtegidaAsync` con "No tenés permiso para cargar los datos de este producto."), L176, L223-224 (`Cancelar`). Spec §3 ("VMs intactos salvo lo mínimo"). |
| Breadcrumb del formulario | `BreadcrumbComando="{Binding CancelarCommand}"`. **Contradicción menor con la spec**: §4.4 y el comentario de `EncabezadoPagina` dicen "el evento `Volver` que el VM ya expone"; `ProductoFormViewModel` no tiene `Volver`, tiene `Cancelar` con la misma navegación (`Navegar<ProductoListViewModel>()`). Se usa el existente; no se agrega nada al VM. | `EncabezadoPagina.cs` L8-12; `ComponentesV2.axaml` L144-149 (`PART_BreadcrumbOrigen` con `Command="{TemplateBinding BreadcrumbComando}"`); `ProductoFormViewModel.cs` L223-224. `TareaFormViewModel.cs` L267 y `DocumentoFormViewModel.cs` L349 sí tienen `Volver`. |
| Número negativo | Clase `negativo` → `V2PeligroTextoBrush` (`#B91C1C`). **Contradicción con el estado actual**: hoy el número sale en `#DC2626` (`SignoNegativoBrushConverter` → `DangerBrush` → `ColorDanger`); la spec pide `#B91C1C`. El converter NO se toca: lo usan las vistas del tema viejo. | `SignoNegativoBrushConverter.cs` L36, L43; `Themes/Tokens.axaml` L86, L150; spec §2.1 (Peligro texto `#B91C1C`) y §5.1. `Tokens.axaml` (V2) tiene `V2PeligroTextoBrush`. |
| Inactivo sin `Opacity` | Clase `inactivo` en cada `TextBlock` de celda; estilos `.ui2 DataGridCell TextBlock.inactivo` y `.negativo` en `DataGridV2.axaml`, `negativo` DESPUÉS (a igual especificidad gana el último declarado). La `Insignia` dibuja su texto en su plantilla (sin la clase), así que no se atenúa. | `ProductoListView.axaml` L36-42 (comentario: la opacidad se aplicaba por celda para no atenuar la insignia), L55, L64, L75, L96, L111, L120; `ActivoOpacidadConverter.cs` (0,55). `ComponentesV2.axaml` L13-54 (plantilla de `Insignia` con `PART_Texto`). |
| `DataGridV2` | Se "pasa" a v2 solo con `ui2` en la raíz: `DataGridV2.axaml` aplica bajo `.ui2` ENCIMA del tema viejo, que sigue incluido y aporta `DataGridColumnasAutoBehavior`, `MinColumnWidth` y resize. **Precisión sobre el brief** ("pasa de `Themes/DataGrid.axaml` a `DataGridV2.axaml`"): no hay nada que desenganchar del tema viejo. La selección de fila (`AcentoSuave`), el hover y las celdas `num` ya están en `DataGridV2`. | `App.axaml` L40 y L45; `DataGridV2.axaml` L4-7, L13-24, L42-45, L152-157; `Themes/DataGrid.axaml` L115 (`AjustarColumnasAuto`). |
| Pie de conteo y estado vacío | `ConteoConverter` nuevo (texto, `EsCero`, `HayElementos`) sobre `Items.Count`; sin cambio de VM. `ColeccionVaciaConverter` no sirve: bindeado a la colección no se re-evalúa al cambiar su contenido. | `ColeccionVaciaConverter.cs` (convierte el objeto colección); `ObservableCollection<T>` notifica `Count`; `FormatoEsUy.Cantidad` (`FormatoEsUy.cs` L54) para miles es-UY. |
| Celda de Stock | Se conserva la `Grid` de ancho fijo 220 con la insignia a la izquierda y el número a la derecha (fix del reciclado de celdas al ordenar). `BadgeEstado` → `v2:Insignia`. La celda lleva `CellStyleClasses="num"` (cifras tabulares, número a la derecha). | `ProductoListView.axaml` L80-106 (comentario del fix); `ProductoListViewStockOrdenTests.cs` L40-72; `DataGridV2.axaml` L152-157. |
| Headers en MAYÚSCULAS | Rompen los tests que buscan el header por texto: `ProductoListViewStockOrdenTests` ("Costo", "Stock"), `DataGridColumnasAutoAjusteTests.Productos_*` ("Stock", "Código", "Costo", "Unidad"). Se actualizan los textos. | `GrillaStockHelpers.cs` L28-29 (`Equals(h.Content, headerTexto)`); `DataGridColumnasAutoAjusteTests.cs` L98-99, L213-222; `herramientas.md` (headers en mayúsculas a mano). |
| `Insignia` en lugar de `BadgeEstado` | Rompe `GrillaStockHelpers.AssertBadgeNoPisaElNumero` (busca `BadgeEstado`: daría "no hay ninguna celda con badge visible") y `SignoNegativoBadgeTests` de Productos. El helper acepta los dos tipos (lo usan también Historial, Valorización, Stock por categoría e Historial por producto, que siguen viejos). | `GrillaStockHelpers.cs` L99-124 (L112 `OfType<BadgeEstado>()`); `SignoNegativoBadgeTests.cs` L58-75. |
| `CampoFormulario` v2 | Rompe `ProductoFormPrecioCostoFormatoTests` (usa `Controls.CampoFormulario`) y `GuardarBloqueadoConErroresDeEntradaTests.CajaDeCampo` (compartido con Entrada/Salida, que siguen viejos). | `ProductoFormPrecioCostoFormatoTests.cs` L10, L38; `GuardarBloqueadoConErroresDeEntradaTests.cs` L16, L61-63. |
| Botón Cancelar v2 | `CatalogoFormAnchoYCancelarTests.BotonCancelar` busca `Classes.Contains("secondary")`: se acepta también `secundario`. `ProductoFormView` está solo en `CasosSoloCancelar` (no en el test de ancho). | `CatalogoFormAnchoYCancelarTests.cs` L103-114, L140-142. |
| Etiqueta del buscador | El test que exige `CampoFormulario Etiqueta="Buscar"` en el listado se reemplaza (D3). | `EtiquetadoDeCamposTests.cs` L70-84; `BarraFiltros.cs` (sin etiqueta); `ComponentesV2.axaml` L213-225 (buscador `Width="280"`). |
| Guardián de patrón | `ProductoListView` y `ProductoFormView` salen de las filas `[InlineData]` de `Vista_TieneHeaderVistaConElTituloEsperado` y de `VistasDeLaTanda`, y entran a `VistasMigradasAV2`. Ahí ya corren `VistaV2_NoTieneOpacidadesLiterales` y `VistaV2_BotonEstiradoEstiraSuContenido` (sin VM: no ven las plantillas de celda; lo de las celdas lo custodian los tests de la Task 4). | `GuardianDePatronTests.cs` L52, L54, L111, L113, L221-230, L320-335, L375-377. |
| `NavigationRecorderFake` | Se le agrega `UltimoInicializador` (`Delegate?`) para comprobar que "Editar" navega con ESE producto (hoy solo guarda el de `TareaFormViewModel`). Cambio aditivo en un fake compartido. | `TareaFakes.cs` L126-149. |
| Datos de la comparación | `DatosProductos` en `VisualTests/Pantallas/Inventario/` con Moq (patrón `DatosInicio`), shell real vía `DatosShell.Crear(usuario, permisos, "Productos", contenido)`. `ProductoFormViewModel` no tiene ítem propio de sidebar: el shell conserva "Productos" como sección. | `DatosShell.cs` L30; `ShellMainViewModel.cs` L356 (`[typeof(ProductoListViewModel)] = "Productos"`); `Pantallas/Inicio/DatosInicio.cs`. |
| Ancho de la card del formulario | `Grid` con `ColumnDefinition Width="*" MaxWidth="760"`: la card mide 760 a la izquierda cuando hay lugar y se achica si no. `HorizontalAlignment="Left"` + `MaxWidth` en la card mediría el ancho DESEADO del contenido (menor a 760). Lo fija `Tarjeta_Mide760YQuedaALaIzquierda`. | `Tokens.axaml` (V2) `V2AnchoFormularioMax` = 760; spec §2.2. |
| Vista sin `Cambiado` | Ninguna de las dos vistas se suscribe a `INavigationService.Cambiado`: nada que desuscribir. | `ProductoListView.axaml.cs` L15-19; `ProductoFormView.axaml.cs` L16-20. |

## Herramientas de diagnóstico (compartidas por las Tasks 4 y 5)

Se usan SOLO si una comparación supera el umbral calibrado. Viven en `$SCRATCH/diag/` (nunca se commitean).

`$SCRATCH/diag/teselas.py` (el de la Fase 2: separa geometría de tono por tesela):

```python
"""Para cada tesela densa (misma métrica que spec 6.5: canal > 32 fuerte, > 4 leve; 16x16; densa si
>= 25% fuertes o >= 90% leves) busca el corrimiento (dx, dy) en [-2, 2] que mejor alinea la app con el
mockup en una ventana de 24x24 centrada en la tesela. Corrimiento (0, 0) = tono; otro = geometría candidata."""
import sys
from PIL import Image

carpeta = sys.argv[1]
app = Image.open(f"{carpeta}/app.png").convert("L")
mock = Image.open(f"{carpeta}/mockup.png").convert("L")
appc = Image.open(f"{carpeta}/app.png").convert("RGB")
mockc = Image.open(f"{carpeta}/mockup.png").convert("RGB")
W, H = app.size
T = 16

def densa(x0, y0):
    fuertes = leves = total = 0
    for y in range(y0, min(y0 + T, H)):
        for x in range(x0, min(x0 + T, W)):
            a, b = appc.getpixel((x, y)), mockc.getpixel((x, y))
            d = max(abs(a[i] - b[i]) for i in range(3))
            total += 1
            fuertes += d > 32
            leves += d > 4
    return fuertes >= 0.25 * total or leves >= 0.9 * total

def costo(x0, y0, dx, dy):
    s = 0
    for y in range(max(2, y0 - 4), min(H - 2, y0 + T + 4)):
        for x in range(max(2, x0 - 4), min(W - 2, x0 + T + 4)):
            s += abs(app.getpixel((x, y)) - mock.getpixel((x + dx, y + dy)))
    return s

for y0 in range(0, H, T):
    for x0 in range(0, W, T):
        if not densa(x0, y0):
            continue
        base = costo(x0, y0, 0, 0)
        mejor = min(((costo(x0, y0, dx, dy), dx, dy) for dx in range(-2, 3) for dy in range(-2, 3)))
        tipo = "tono" if (mejor[1], mejor[2]) == (0, 0) or mejor[0] > 0.8 * base else f"GEOMETRIA dx={mejor[1]} dy={mejor[2]}"
        print(f"tesela ({x0},{y0}): costo base {base}, mejor {mejor[0]} -> {tipo}")
```

Uso: `(cd "$SCRATCH/diag" && python3 teselas.py "$OLDPWD/artifacts/ui-v2/comparacion/<pantalla>")`.

`$SCRATCH/diag/tinta.py` (proporción de tinta app/mockup y bordes de tinta por fila de una región; tono = proporción estable y bordes iguales):

```python
"""Uso: tinta.py <carpeta> x0 y0 x1 y1. Imprime, por fila de la región, la tinta (255 - luminancia,
sumada) de app y mockup, su proporción y la primera/última columna con tinta > 24 en cada uno."""
import sys
from PIL import Image

carpeta, x0, y0, x1, y1 = sys.argv[1], *map(int, sys.argv[2:6])
app = Image.open(f"{carpeta}/app.png").convert("L")
mock = Image.open(f"{carpeta}/mockup.png").convert("L")
fondo = app.getpixel((x0, y0))
for y in range(y0, y1):
    ta = [fondo - app.getpixel((x, y)) for x in range(x0, x1)]
    tm = [fondo - mock.getpixel((x, y)) for x in range(x0, x1)]
    sa, sm = sum(max(0, v) for v in ta), sum(max(0, v) for v in tm)
    if sa == 0 and sm == 0:
        continue
    ba = [i for i, v in enumerate(ta) if v > 24]
    bm = [i for i, v in enumerate(tm) if v > 24]
    print(f"y={y}: app {sa} mockup {sm} prop {sa / sm if sm else float('inf'):.3f} "
          f"bordes app {(ba[0], ba[-1]) if ba else None} mockup {(bm[0], bm[-1]) if bm else None}")
```

Antes de clasificar una tesela como tono, verificar para el texto que la contiene: familia (Inter), tamaño, peso, color y alto de línea del CSS del mockup contra el XAML (o la plantilla del componente). Proporciones de referencia por estilo en spec §6.5 (texto 400 sobre `#1C1917` 0,740-0,765; título 22/600 0,891; título de sección 15/600 0,83; terciario 12/400 0,93).

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs` | Datos fijos de los mockups de Productos (12 productos, unidades, categorías) y los `ShellMainViewModel` de cada estado. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs` | Textos formateados (costo, stock) de los datos fijos = los del mockup. |
| `docs/ui-v2/mockups/inventario/productos.html`, `productos-vacio.html` | Mockups del listado. |
| `docs/ui-v2/mockups/inventario/producto-nuevo.html`, `producto-editar.html`, `producto-error.html`, `producto-sin-permiso.html` | Mockups del formulario. |
| `src/StockApp.Presentation/Converters/ConteoConverter.cs` | Texto del pie de conteo y visibilidad por `Count`. |
| `tests/StockApp.Presentation.Tests/Converters/ConteoConverterTests.cs` | Tests del conversor. |
| `tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs` | Listado v2 con la View real: estructura, inactivo, stock negativo, acciones, flujos, vacío. |
| `tests/StockApp.Presentation.UiTests/V2/ProductoFormViewV2Tests.cs` | Formulario v2 con la View real: breadcrumb, layout, edición, error, sin permiso, flujos. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs` | 6 estados × 2 tamaños. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` | Reescritura v2 (code-behind sin cambios). |
| `src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml` | Reescritura v2 (code-behind sin cambios). |
| `src/StockApp.Presentation/Themes/V2/DataGridV2.axaml` | Clases `inactivo` y `negativo` de texto de celda. |
| `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` | `TextBox` deshabilitado (D14). |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-grilla-fila--inactiva`, `.v2-texto-peligro`, `.v2-input--deshabilitado`. |
| `tests/StockApp.Presentation.UiTests/TareaFakes.cs` | `NavigationRecorderFake.UltimoInicializador`. |
| `tests/StockApp.Presentation.UiTests/GrillaStockHelpers.cs` | Insignia v2 además de `BadgeEstado`. |
| `tests/StockApp.Presentation.UiTests/ProductoListViewStockOrdenTests.cs`, `DataGridColumnasAutoAjusteTests.cs`, `SignoNegativoBadgeTests.cs`, `EtiquetadoDeCamposTests.cs`, `GuardianDePatronTests.cs` | Adaptación al listado v2. |
| `tests/StockApp.Presentation.UiTests/ProductoFormPrecioCostoFormatoTests.cs`, `GuardarBloqueadoConErroresDeEntradaTests.cs`, `CatalogoFormAnchoYCancelarTests.cs` | Adaptación al formulario v2. |
| `docs/ui-v2/herramientas.md` | Convenciones 10 a 14. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | Estado, §7 (estado de la Fase 3a y decisiones), §6.5 si aparece una compensación nueva. |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln`
- VM y converters: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`
- Un test: agregar `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh <filtro>` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger de la fase: `.superpowers/sdd/2026-10-06-ui-v2-fase-3a-productos/progress.md` (crearlo en la Task 1; `.superpowers/sdd/` está en `.gitignore`: local, no se commitea).

---

## Bloque A — Datos fijos y medición (antes de cualquier mockup)

### Task 1: Datos fijos de Productos y anchos medidos de las columnas `Auto`

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs`
- Create (local, no se commitea): `.superpowers/sdd/2026-10-06-ui-v2-fase-3a-productos/progress.md`

**Interfaces:**
- Consumes: `DatosShell.Crear(UsuarioSesion usuario, string[] permisos, string seccionActiva, ViewModelBase? contenido = null)`; constructores `ProductoListViewModel(IProductoService, INavigationService, IConfirmacionService)` y `ProductoFormViewModel(IProductoService, IUnidadMedidaService, ICategoriaService, INavigationService)`; `ProductoDto` (record posicional de 15 campos, `Dtos.cs` L11-26).
- Produces: `DatosProductos.Productos` (`IReadOnlyList<ProductoDto>`, 12 filas), `DatosProductos.Unidades`, `DatosProductos.Categorias`, `DatosProductos.UsuarioAdmin`, `DatosProductos.ConProductos()`, `SinProductos()`, `Nuevo()`, `Editar()`, `ConError()`, `SinPermiso()` (todas `ShellMainViewModel`), `DatosProductos.Listado(IReadOnlyList<ProductoDto>)` y `DatosProductos.Formulario(bool sinPermiso = false)`. Anchos medidos de CÓDIGO, COSTO, UNIDAD y ESTADO a 1280 y a 1366, anotados en el ledger para la Task 2.

- [ ] **Step 1: Test de los textos (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs`:

```csharp
using StockApp.Domain.Formato;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Productos escriben estos textos a mano: si un dato fijo cambia y el mockup no, la
/// comparación falla por un número distinto y el diagnóstico no lo dice. Esta tabla es el contrato.
/// </summary>
public class DatosProductosTests
{
    [Fact]
    public void Productos_TextosFormateados_SonLosDelMockup()
    {
        var esperado = new (string Codigo, string Nombre, string Costo, string Stock, string Unidad, string Categoria, bool Activo)[]
        {
            ("MAT-0142", "Cemento portland 50 kg", "$ 480,00", "320", "Bolsa", "Construcción", true),
            ("MAT-0198", "Arena gruesa", "$ 890,00", "45", "Metro cúbico", "Construcción", true),
            ("PLO-0231", "Caño PVC 110 mm", "$ 650,00", "12", "Unidad", "Plomería", true),
            ("PIN-0305", "Pintura látex blanca 20 L", "$ 2.340,00", "6", "Balde", "Pintura", true),
            ("SEG-0356", "Guantes de trabajo", "$ 95,00", "180", "Par", "Seguridad", true),
            ("ELE-0402", "Lámpara LED 30 W", "$ 410,00", "8", "Unidad", "Electricidad", true),
            ("ELE-0418", "Cable 2x1,5 mm", "$ 38,00", "1.250", "Metro", "Electricidad", true),
            ("LIM-0455", "Hipoclorito de sodio 5 L", "$ 210,00", "-3", "Bidón", "Limpieza", true),
            ("LIM-0470", "Bolsas de residuo 80x110", "$ 175,00", "60", "Paquete", "Limpieza", true),
            ("HER-0512", "Pala de punta", "$ 1.150,00", "14", "Unidad", "—", true),
            ("VIA-0533", "Pintura vial amarilla 4 L", "$ 1.890,50", "22,5", "Lata", "Pintura", true),
            ("HER-0560", "Carretilla reforzada", "$ 3.200,00", "0", "Unidad", "Herramientas", false),
        };

        var real = DatosProductos.Productos.Select(p => (p.Codigo, p.Nombre, FormatoEsUy.Moneda(p.PrecioCosto),
            FormatoEsUy.Cantidad(p.StockActual), p.UnidadMedidaNombre, p.CategoriaNombre ?? "—", p.Activo)).ToArray();

        Assert.Equal(esperado, real);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosProductosTests"`
Expected: FAIL de compilación (`The name 'DatosProductos' does not exist`).

- [ ] **Step 2: `DatosProductos`**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/*.html (los textos formateados están fijados en
/// DatosProductosTests). Un depósito municipal realista: un stock negativo, un decimal, un producto sin
/// categoría y uno inactivo. Doce filas: a 1280x760 entran sin scroll vertical.
/// </summary>
public static class DatosProductos
{
    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");

    private static readonly DateTime Alta = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

    private static ProductoDto P(int id, string codigo, string nombre, decimal costo, decimal stock,
        int unidadId, string unidad, int? categoriaId, string? categoria, bool activo = true) =>
        new(id, codigo, null, nombre, null, categoriaId, categoria, null, unidadId, unidad, costo, stock, 0m, activo, Alta);

    public static readonly IReadOnlyList<ProductoDto> Productos =
    [
        P(1, "MAT-0142", "Cemento portland 50 kg", 480m, 320m, 2, "Bolsa", 1, "Construcción"),
        P(2, "MAT-0198", "Arena gruesa", 890m, 45m, 3, "Metro cúbico", 1, "Construcción"),
        P(3, "PLO-0231", "Caño PVC 110 mm", 650m, 12m, 1, "Unidad", 6, "Plomería"),
        P(4, "PIN-0305", "Pintura látex blanca 20 L", 2340m, 6m, 4, "Balde", 5, "Pintura"),
        P(5, "SEG-0356", "Guantes de trabajo", 95m, 180m, 5, "Par", 7, "Seguridad"),
        P(6, "ELE-0402", "Lámpara LED 30 W", 410m, 8m, 1, "Unidad", 2, "Electricidad"),
        P(7, "ELE-0418", "Cable 2x1,5 mm", 38m, 1250m, 6, "Metro", 2, "Electricidad"),
        P(8, "LIM-0455", "Hipoclorito de sodio 5 L", 210m, -3m, 7, "Bidón", 4, "Limpieza"),
        P(9, "LIM-0470", "Bolsas de residuo 80x110", 175m, 60m, 8, "Paquete", 4, "Limpieza"),
        P(10, "HER-0512", "Pala de punta", 1150m, 14m, 1, "Unidad", null, null),
        P(11, "VIA-0533", "Pintura vial amarilla 4 L", 1890.5m, 22.5m, 9, "Lata", 5, "Pintura"),
        P(12, "HER-0560", "Carretilla reforzada", 3200m, 0m, 1, "Unidad", 3, "Herramientas", activo: false),
    ];

    public static readonly IReadOnlyList<UnidadMedida> Unidades =
    [
        new() { Id = 1, Nombre = "Unidad", Abreviatura = "u" },
        new() { Id = 2, Nombre = "Bolsa", Abreviatura = "bol" },
        new() { Id = 6, Nombre = "Metro", Abreviatura = "m" },
        new() { Id = 10, Nombre = "Litro", Abreviatura = "l" },
    ];

    public static readonly IReadOnlyList<Categoria> Categorias =
    [
        new() { Id = 1, Nombre = "Construcción" },
        new() { Id = 2, Nombre = "Electricidad" },
        new() { Id = 3, Nombre = "Herramientas" },
        new() { Id = 4, Nombre = "Limpieza" },
        new() { Id = 5, Nombre = "Pintura" },
        new() { Id = 6, Nombre = "Plomería" },
        new() { Id = 7, Nombre = "Seguridad" },
    ];

    /// <summary>El producto que abre el formulario en modo edición (el primero, con código de barras y descripción).</summary>
    public static readonly ProductoDto ProductoEditado = Productos[0] with
    {
        CodigoBarras = "7730123456789",
        Descripcion = "Bolsa de 50 kg para obras de mantenimiento urbano.",
    };

    public static ProductoListViewModel Listado(IReadOnlyList<ProductoDto> productos)
    {
        var servicio = new Mock<IProductoService>();
        servicio.Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>())).ReturnsAsync(productos);
        servicio.Setup(s => s.BuscarPorTextoAsync(It.IsAny<string?>())).ReturnsAsync(productos);
        return new ProductoListViewModel(servicio.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    /// <summary>sinPermiso: la carga de unidades responde 403 (UnauthorizedAccessException), como ApiErrores.</summary>
    public static ProductoFormViewModel Formulario(bool sinPermiso = false)
    {
        var unidades = new Mock<IUnidadMedidaService>();
        if (sinPermiso)
        {
            unidades.Setup(u => u.GarantizarUnidadPorDefectoAsync()).ThrowsAsync(new UnauthorizedAccessException("403"));
        }
        else
        {
            unidades.Setup(u => u.GarantizarUnidadPorDefectoAsync()).ReturnsAsync(Unidades[0]);
        }

        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(Unidades);
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(Categorias);
        return new ProductoFormViewModel(Mock.Of<IProductoService>(), unidades.Object, categorias.Object, Mock.Of<INavigationService>());
    }

    private static ShellMainViewModel EnShell(ViewModelBase contenido) =>
        DatosShell.Crear(UsuarioAdmin, [], "Productos", contenido);

    public static ShellMainViewModel ConProductos() => EnShell(Listado(Productos));

    public static ShellMainViewModel SinProductos() => EnShell(Listado([]));

    public static ShellMainViewModel Nuevo() => EnShell(Formulario());

    public static ShellMainViewModel Editar()
    {
        var vm = Formulario();
        vm.CargarParaEditar(ProductoEditado);
        return EnShell(vm);
    }

    /// <summary>Alta que el servidor rechazó por código repetido: el texto es el de ProductoService.AltaAsync.</summary>
    public static ShellMainViewModel ConError()
    {
        var vm = Formulario();
        vm.Codigo = "MAT-0142";
        vm.Nombre = "Cemento portland 50 kg";
        vm.MensajeError = "Ya existe un producto con el código 'MAT-0142'.";
        return EnShell(vm);
    }

    public static ShellMainViewModel SinPermiso() => EnShell(Formulario(sinPermiso: true));
}
```

(Unidades del listado como "Metro cúbico", "Balde" o "Par" son solo texto del DTO; la lista `Unidades` es la del combo del formulario.)

- [ ] **Step 3: Verde**

Run: el filtro del Step 1.
Expected: PASS. Si falla un texto (por ejemplo `-3` sale `-3` y no otro formato), el que manda es `FormatoEsUy`: corregir la TABLA del test y anotarlo, porque los mockups se escriben con la tabla.

- [ ] **Step 4: Medir los anchos de las columnas `Auto` (scratch, no se commitea)**

Crear `tests/StockApp.Presentation.UiTests/V2/MedicionColumnasProductosScratch.cs` (se BORRA en el Step 5):

```csharp
using System;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Xunit;
using static StockApp.Presentation.UiTests.V2.MontajeV2;

namespace StockApp.Presentation.UiTests.V2;

public class MedicionColumnasProductosScratch(ITestOutputHelper salida)
{
    // Grilla con las columnas de D6 dentro de .ui2 (DataGridV2 + tema viejo con DataGridColumnasAutoBehavior),
    // con los textos más anchos de DatosProductos. Ancho útil: 984 (1280) y 1070 (1366) menos 2x16 de
    // padding y 2 de borde de la card.
    private const string Grilla = """
        <DataGrid x:Name="G" AutoGenerateColumns="False" Height="560" IsReadOnly="True" CanUserResizeColumns="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="CÓDIGO" Width="Auto" Binding="{ReflectionBinding Codigo}" />
                <DataGridTextColumn Header="NOMBRE" Width="2*" Binding="{ReflectionBinding Nombre}" />
                <DataGridTextColumn Header="COSTO" Width="Auto" MinWidth="100" CellStyleClasses="num" Binding="{ReflectionBinding Costo}" />
                <DataGridTextColumn Header="STOCK" Width="220" MinWidth="100" CellStyleClasses="num" Binding="{ReflectionBinding Stock}" />
                <DataGridTextColumn Header="UNIDAD" Width="Auto" Binding="{ReflectionBinding Unidad}" />
                <DataGridTextColumn Header="CATEGORÍA" Width="*" Binding="{ReflectionBinding Categoria}" />
                <DataGridTemplateColumn Header="ESTADO" Width="Auto">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate><v2:Insignia Texto="Inactiva" /></DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>
        """;

    public sealed record Fila(string Codigo, string Nombre, string Costo, string Stock, string Unidad, string Categoria);

    [AvaloniaTheory]
    [InlineData(950)]
    [InlineData(1036)]
    public void Medir(double anchoGrilla)
    {
        var ventana = Montar($"<Border Width=\"{anchoGrilla}\">{Grilla}</Border>", ancho: anchoGrilla + 40, alto: 700);
        var grilla = Buscar<DataGrid>(ventana, "G");
        grilla.ItemsSource = new DataGridCollectionView(new[]
        {
            new Fila("MAT-0142", "Pintura látex blanca 20 L", "$ 3.200,00", "1.250", "Metro cúbico", "Construcción"),
            new Fila("HER-0560", "Bolsas de residuo 80x110", "$ 1.890,50", "22,5", "Paquete", "Electricidad"),
        });
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        foreach (var c in grilla.Columns)
            salida.WriteLine($"{anchoGrilla}: {c.Header} = {c.ActualWidth}");
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MedicionColumnasProductosScratch" --logger "console;verbosity=detailed"`
Expected: 7 anchos por tamaño (950 = grilla a 1280, 1036 = a 1366: 1126 − 56 − 34). Estimado: CÓDIGO ~78, COSTO 100 (mínimo), STOCK 220, UNIDAD ~96, ESTADO ~78, CATEGORÍA 160 (piso), NOMBRE el resto (~216 a 1280, ~302 a 1366), y la suma ≤ el ancho (sin scroll horizontal). (`ITestOutputHelper` está en el namespace `Xunit` en xUnit v3; si la salida no aparece en consola, terminar el test con un `Assert.Fail` que liste los anchos y leerlos del mensaje del rojo.) Si la suma supera el ancho, la grilla desborda: anotarlo, porque el mockup tiene que mostrar la barra horizontal y el GATE de la Task 2 tiene que decirlo (convención 1).

- [ ] **Step 5: Ledger y limpieza**

Borrar `MedicionColumnasProductosScratch.cs` (`git status` no lo muestra). Crear `.superpowers/sdd/2026-10-06-ui-v2-fase-3a-productos/progress.md` con: base `593069a`, la tabla de anchos medidos (columna × 1280/1366) y si la grilla entra o desborda a cada tamaño.

- [ ] **Step 6: Commit**

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosProductosTests.cs
git commit -m "test(ui-v2): datos fijos de Productos para mockups y comparaciones"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes del XAML)

Los mockups de contenido dibujan el sidebar (para que el usuario apruebe la pantalla entera); la comparación lo oculta (convención 6). El sidebar es el `<aside class="v2-sidebar">…</aside>` de `docs/ui-v2/mockups/shell/sidebar-admin.html` copiado TAL CUAL (ya trae "Productos" activo, los SVG insertados y el pulgar de scroll). En los HTML de abajo se escribe el marcador `SIDEBAR_ADMIN`, que NO es texto del archivo: se reemplaza por ese bloque. Comprobar al final de cada task: `grep -c 'SIDEBAR_' docs/ui-v2/mockups/inventario/*.html` imprime `0` en todos y `grep -c 'v2-item-sidebar--activo' docs/ui-v2/mockups/inventario/*.html` imprime `1` en todos.

### Task 2: Mockups del listado (con datos y vacío)

**Decisiones aplicadas:** U3, U8, D1 a D11.

**Files:**
- Create: `docs/ui-v2/mockups/inventario/productos.html`
- Create: `docs/ui-v2/mockups/inventario/productos-vacio.html`
- Modify: `docs/ui-v2/mockups/_base/base.css` (`.v2-grilla-fila--inactiva`, `.v2-texto-peligro`)

**Interfaces:**
- Consumes: `base.css` (`.v2-encabezado*`, `.v2-titulo-pagina`, `.v2-boton--*`, `.v2-tarjeta`, `.v2-barra-filtros`, `.v2-busqueda`, `.v2-input*`, `.v2-grilla*`, `.v2-num`, `.v2-insignia--*`, `.v2-pie-conteo`, `.v2-vacio*`, `.v2-sidebar*`); anchos medidos del ledger (Task 1); `DatosProductos.Productos`.
- Produces: pantallas `inventario-productos` e `inventario-productos-vacio` para la Task 4; clases `.v2-grilla-fila--inactiva` y `.v2-texto-peligro` (espejo de `TextBlock.inactivo` y `TextBlock.negativo` de la Task 4).

- [ ] **Step 1: Espejo CSS de inactivo y negativo**

Al final de `docs/ui-v2/mockups/_base/base.css`:

```css
/* Fila inactiva (DataGridV2.axaml, TextBlock.inactivo): el texto de cada celda pasa a terciario por token,
   sin opacidad; la insignia conserva su color (lo fija .v2-insignia). */
.v2-grilla-fila--inactiva .v2-grilla-celda { color: var(--v2-color-texto-terciario); }
/* Número negativo en celda (DataGridV2.axaml, TextBlock.negativo): peligro texto. Gana sobre la fila inactiva. */
.v2-grilla-celda .v2-texto-peligro { color: var(--v2-color-peligro-texto); }
```

- [ ] **Step 2: `productos.html`**

Reemplazar `ANCHO_CODIGO`, `ANCHO_COSTO`, `ANCHO_UNIDAD`, `ANCHO_ESTADO` y `ANCHO_CATEGORIA` por los píxeles medidos a 1280 (ledger, Task 1); si a 1366 algún ancho difiere, agregar su valor en el `@media (min-width: 1366px)` del final del `<style>` (por ejemplo, la categoría o el nombre si salen del piso). Ninguno de esos marcadores queda en el archivo (`grep -c 'ANCHO_' docs/ui-v2/mockups/inventario/*.html` → `0`).

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Inventario: Productos (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  /* Área de contenido del shell: a la derecha del sidebar de 240 (ShellMainView, Grid 240,*). */
  .contenido { position: absolute; left: var(--v2-ancho-sidebar); top: 0; right: 0; bottom: 0; overflow: hidden; }
  /* DockPanel Margin 28,24 de ProductoListView.axaml: encabezado arriba, card con el resto del alto. */
  .pagina { position: absolute; inset: 0; display: flex; flex-direction: column; padding: 24px 28px; }
  .pagina > .v2-tarjeta { flex: 1 1 0; min-height: 0; display: flex; flex-direction: column; }
  .pagina .v2-grilla { flex: 1 1 0; min-height: 0; }
  .pie { margin-top: 12px; }
  /* Anchos de columna medidos en headless con el tema v2 y estos datos (Auto), o los de la vista (220, *, 2*). */
  .col-codigo { flex: 0 0 ANCHO_CODIGOpx; }
  .col-nombre { flex: 2 1 0; min-width: 0; }
  .col-costo { flex: 0 0 ANCHO_COSTOpx; }
  .col-stock { flex: 0 0 220px; }
  .col-unidad { flex: 0 0 ANCHO_UNIDADpx; }
  .col-categoria { flex: 0 0 ANCHO_CATEGORIApx; }
  .col-estado { flex: 0 0 ANCHO_ESTADOpx; }
  /* Celda de Stock: Grid con la insignia a la izquierda y el número a la derecha (no es .v2-num: lleva
     dos elementos); cifras tabulares y ancho del número redondeado hacia arriba como un TextBlock. */
  .celda-stock { font-variant-numeric: tabular-nums; }
  .celda-stock > .valor { margin-left: auto; width: calc-size(fit-content, round(up, size, 1px)); }
</style>
</head>
<body>
<!-- Datos: DatosProductos.ConProductos(). Ninguna fila seleccionada: Editar y Eliminar deshabilitados.
     El sidebar está para aprobar la pantalla entera; la comparación lo oculta (tiene la suya). -->
<div class="v2-ventana">
  SIDEBAR_ADMIN
  <div class="contenido">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-encabezado-fila">
          <div><div class="v2-titulo-pagina">Productos</div></div>
          <div class="v2-acciones">
            <span class="v2-boton v2-boton--secundario v2-boton--deshabilitado">Editar</span>
            <span class="v2-boton v2-boton--peligro v2-boton--deshabilitado">Eliminar</span>
            <span class="v2-boton v2-boton--primario">Nuevo</span>
          </div>
        </div>
      </div>
      <div class="v2-tarjeta">
        <div class="v2-barra-filtros">
          <div class="v2-input v2-busqueda"><span class="v2-input-placeholder">Nombre, SKU o código de barras</span></div>
          <span class="v2-boton v2-boton--secundario">Buscar</span>
        </div>
        <div class="v2-grilla">
          <div class="v2-grilla-header">
            <div class="v2-grilla-celda col-codigo">CÓDIGO</div>
            <div class="v2-grilla-celda col-nombre">NOMBRE</div>
            <div class="v2-grilla-celda col-costo">COSTO</div>
            <div class="v2-grilla-celda col-stock">STOCK</div>
            <div class="v2-grilla-celda col-unidad">UNIDAD</div>
            <div class="v2-grilla-celda col-categoria">CATEGORÍA</div>
            <div class="v2-grilla-celda col-estado">ESTADO</div>
          </div>
          FILAS
        </div>
        <div class="v2-pie-conteo pie">12 productos</div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

`FILAS` NO es texto del archivo: son las 12 filas de `DatosProductos.Productos`, en ese orden, con los textos de la tabla de `DatosProductosTests`. Plantilla de una fila activa con stock positivo:

```html
          <div class="v2-grilla-fila">
            <div class="v2-grilla-celda col-codigo">MAT-0142</div>
            <div class="v2-grilla-celda col-nombre">Cemento portland 50 kg</div>
            <div class="v2-grilla-celda col-costo v2-num"><span>$ 480,00</span></div>
            <div class="v2-grilla-celda col-stock celda-stock"><span class="valor">320</span></div>
            <div class="v2-grilla-celda col-unidad">Bolsa</div>
            <div class="v2-grilla-celda col-categoria">Construcción</div>
            <div class="v2-grilla-celda col-estado"></div>
          </div>
```

Fila con stock negativo (LIM-0455):

```html
            <div class="v2-grilla-celda col-stock celda-stock"><span class="v2-insignia v2-insignia--peligro">Stock negativo</span><span class="valor v2-texto-peligro">-3</span></div>
```

Fila inactiva (HER-0560): `<div class="v2-grilla-fila v2-grilla-fila--inactiva">` y en Estado `<span class="v2-insignia">Inactiva</span>`. Sin categoría (HER-0512): la celda dice `—`.

- [ ] **Step 3: `productos-vacio.html`**

El mismo archivo completo (no por diferencia), con `<title>Inventario: Productos sin resultados (UI v2)</title>`, el comentario `<!-- Datos: DatosProductos.SinProductos(): la búsqueda no devolvió nada; la grilla y el pie se ocultan y se ve el estado vacío. -->`, y dentro de la `v2-tarjeta`, en lugar de la `v2-grilla` y del pie:

```html
        <div class="v2-vacio">
          <div class="v2-vacio-titulo">No hay productos para mostrar</div>
          <div class="v2-vacio-mensaje">Probá con otra búsqueda o creá uno con Nuevo.</div>
        </div>
```

- [ ] **Step 4: Sidebar, capturas y convenciones**

Reemplazar `SIDEBAR_ADMIN` por el `<aside>` de `shell/sidebar-admin.html` en los dos archivos. Después:

```bash
for p in productos productos-vacio; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
grep -c 'SIDEBAR_\|FILAS\|ANCHO_' docs/ui-v2/mockups/inventario/*.html
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"
```

Expected: 4 PNG sin error de fuentes, desborde ni `.v2-ventana`; el `grep` imprime `0` para cada archivo; el test de convención en verde (la celda de Stock NO lleva `v2-num`, por eso pasa). Mirar cada PNG: 12 filas visibles sin cortar a los dos tamaños, el placeholder entero en el buscador, la grilla hasta el pie.

- [ ] **Step 5: GATE — aprobación del usuario**

PARAR. Presentar los dos HTML (abrirlos en Chromium/Edge: `calc-size()` solo funciona ahí) o los 4 PNG (LOS DOS TAMAÑOS), con la lista D1 a D11 y los anchos medidos (si la grilla entra o desborda a cada tamaño; convención 1). Señalar explícitamente: el placeholder acortado (D2) y la etiqueta "Buscar" que se va (D3), "Editar"/"Eliminar" deshabilitados sin selección (D1, D9), la insignia "Inactiva" con la fila en gris y sin opacidad (U3, D8), el número negativo en `#B91C1C` (U8). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en el mockup (y en `base.css` si toca geometría) y volver a presentar. Registrar en el ledger: `Mockups listado: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`, y las D# cambiadas con su nuevo valor (las Tasks 4 y 5 las leen de ahí).

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/inventario/productos.html docs/ui-v2/mockups/inventario/productos-vacio.html docs/ui-v2/mockups/_base/base.css
git commit -m "docs(ui-v2): mockups aprobados del listado de Productos (con datos y vacío)"
```

### Task 3: Mockups del formulario (nuevo, editar, con error y sin permiso)

**Decisiones aplicadas:** D12 a D18.

**Files:**
- Create: `docs/ui-v2/mockups/inventario/producto-nuevo.html`, `producto-editar.html`, `producto-error.html`, `producto-sin-permiso.html`
- Modify: `docs/ui-v2/mockups/_base/base.css` (`.v2-input--deshabilitado`)

**Interfaces:**
- Consumes: `base.css` (`.v2-breadcrumb*`, `.v2-campo*`, `.v2-input*`, `.v2-glifo-combo`, `.v2-subtitulo-formulario`, `.v2-alerta--error`, `.v2-vacio*`, `.v2-boton--*`); `DatosProductos.Nuevo()`, `Editar()`, `ConError()`, `SinPermiso()`.
- Produces: pantallas `inventario-producto-nuevo`, `inventario-producto-editar`, `inventario-producto-error`, `inventario-producto-sin-permiso` para la Task 5; `.v2-input--deshabilitado` (espejo de `.ui2 TextBox:disabled` de la Task 5).

- [ ] **Step 1: Espejo CSS del input deshabilitado**

Al final de `base.css`:

```css
/* TextBox deshabilitado (ControlesV2.axaml, .ui2 TextBox:disabled): tokens de deshabilitado, los mismos del botón. */
.v2-input--deshabilitado { background: var(--v2-color-deshabilitado-fondo); border-color: var(--v2-color-borde); color: var(--v2-color-deshabilitado-texto); }
```

- [ ] **Step 2: `producto-nuevo.html`**

Glifo del combo: copiar el `<svg class="v2-glifo-combo" …>…</svg>` tal cual de `docs/ui-v2/mockups/componentes/catalogo.html` (marcador `GLIFO_COMBO` abajo, que NO queda en el archivo).

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Inventario: Nuevo producto (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  .contenido { position: absolute; left: var(--v2-ancho-sidebar); top: 0; right: 0; bottom: 0; overflow: hidden; }
  /* ScrollViewer > StackPanel Margin 28,24 de ProductoFormView.axaml. */
  .pagina { padding: 24px 28px; }
  /* Grid con una columna * de MaxWidth 760: la card mide 760 a la izquierda. Padding de formulario 24. */
  .formulario { max-width: var(--v2-ancho-formulario-max); padding: 24px; }
  .subtitulo { margin-bottom: 12px; }
  .campos { display: grid; grid-template-columns: 1fr 1fr; column-gap: 16px; row-gap: 16px; align-items: start; }
  .completo { grid-column: 1 / 3; }
  .multilinea { height: 88px; align-items: flex-start; padding: 8px 12px; }
  .separador { height: 1px; background: var(--v2-color-separador-grilla); margin: 20px 0; }
  .pie-formulario { display: flex; justify-content: flex-end; gap: 8px; margin-top: 24px; padding-top: 16px; border-top: 1px solid var(--v2-color-separador-grilla); }
</style>
</head>
<body>
<!-- Datos: DatosProductos.Nuevo(). Alta: la unidad "Unidad" preseleccionada (GarantizarUnidadPorDefectoAsync),
     sin categoría, SKU y Nombre vacíos: Guardar deshabilitado (PuedeGuardar). -->
<div class="v2-ventana">
  SIDEBAR_ADMIN
  <div class="contenido">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-breadcrumb"><span class="v2-breadcrumb-origen">Productos</span><span> / </span><span>Nuevo producto</span></div>
        <div class="v2-encabezado-fila"><div><div class="v2-titulo-pagina">Nuevo producto</div></div></div>
      </div>
      <div class="v2-tarjeta formulario">
        <div class="v2-subtitulo-formulario subtitulo">Datos del producto</div>
        <div class="campos">
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>SKU</span><span class="v2-campo-requerido">*</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">Ej: P001</span></div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Código de barras</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">EAN (opcional)</span></div></div>
          </div>
          <div class="v2-campo completo">
            <div class="v2-campo-etiqueta"><span>Nombre</span><span class="v2-campo-requerido">*</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">Nombre del producto</span></div></div>
          </div>
          <div class="v2-campo completo">
            <div class="v2-campo-etiqueta"><span>Descripción</span></div>
            <div><div class="v2-input multilinea"><span class="v2-input-placeholder">Descripción opcional</span></div></div>
          </div>
        </div>
        <div class="separador"></div>
        <div class="v2-subtitulo-formulario subtitulo">Costo y clasificación</div>
        <div class="campos">
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Precio costo</span></div>
            <div><div class="v2-input">0</div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Unidad de medida</span></div>
            <div><div class="v2-input"><span>Unidad</span>GLIFO_COMBO</div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Categoría</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">Sin categoría</span>GLIFO_COMBO</div></div>
          </div>
        </div>
        <div class="pie-formulario">
          <span class="v2-boton v2-boton--secundario">Cancelar</span>
          <span class="v2-boton v2-boton--primario v2-boton--deshabilitado">Guardar</span>
        </div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 3: `producto-editar.html`, `producto-error.html`, `producto-sin-permiso.html`**

Cada uno es el archivo completo del Step 2 (escrito entero, no por diferencia) con estos cambios:

`producto-editar.html` — `<title>Inventario: Editar producto (UI v2)</title>`, comentario `<!-- Datos: DatosProductos.Editar(): CargarParaEditar(ProductoEditado). SKU no editable en edición (ProductoService no reasigna el código). -->`, breadcrumb y título "Editar producto", y los valores:
- SKU: `<div class="v2-input v2-input--deshabilitado">MAT-0142</div>`
- Código de barras: `<div class="v2-input">7730123456789</div>`
- Nombre: `<div class="v2-input">Cemento portland 50 kg</div>`
- Descripción: `<div class="v2-input multilinea">Bolsa de 50 kg para obras de mantenimiento urbano.</div>`
- Precio costo: `<div class="v2-input">480</div>`
- Unidad de medida: `<span>Bolsa</span>` + glifo; Categoría: `<span>Construcción</span>` + glifo
- Guardar habilitado: `<span class="v2-boton v2-boton--primario">Guardar</span>`

`producto-error.html` — `<title>Inventario: Nuevo producto con error (UI v2)</title>`, comentario `<!-- Datos: DatosProductos.ConError(): el servidor rechazó el alta por código repetido; el texto es el de ProductoService.AltaAsync. -->`, breadcrumb y título "Nuevo producto", SKU `MAT-0142`, Nombre `Cemento portland 50 kg`, el resto como en el Step 2, Guardar habilitado y, entre el último `.campos` y el `.pie-formulario`:

```html
        <div class="v2-alerta v2-alerta--error alerta-error">Ya existe un producto con el código 'MAT-0142'.</div>
```

con `.alerta-error { margin-top: 16px; }` en el `<style>`.

`producto-sin-permiso.html` — `<title>Inventario: Producto sin permiso (UI v2)</title>`, comentario `<!-- Datos: DatosProductos.SinPermiso(): la carga de datos respondió 403; el estado reemplaza a la card y el breadcrumb sigue disponible. -->`, breadcrumb y título "Nuevo producto", y en lugar de la `v2-tarjeta formulario`:

```html
      <div class="v2-vacio">
        <div class="v2-vacio-titulo v2-vacio-titulo--error">Sin permiso</div>
        <div class="v2-vacio-mensaje">No tenés permiso para cargar los datos de este producto.</div>
      </div>
```

- [ ] **Step 4: Sidebar, glifos, capturas y convenciones**

Reemplazar `SIDEBAR_ADMIN` y `GLIFO_COMBO` en los cuatro. Después:

```bash
for p in producto-nuevo producto-editar producto-error producto-sin-permiso; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
grep -c 'SIDEBAR_\|GLIFO_' docs/ui-v2/mockups/inventario/producto-*.html
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj --filter "FullyQualifiedName~MockupsDelRepo_RespetanLaConvencion"
```

Expected: 8 PNG sin error; `0` en cada archivo; convención en verde. Mirar: la card mide 760 y queda a la izquierda a los dos tamaños; el formulario entero (hasta "Guardar") entra en 760 de alto sin scroll (estimado ~640 con el error); si no entra, la vista mostraría un pulgar de scroll: avisarlo en el GATE.

- [ ] **Step 5: GATE — aprobación del usuario**

PARAR. Presentar los cuatro HTML o los 8 PNG (LOS DOS TAMAÑOS) con la lista D12 a D18. Señalar: las dos secciones y qué campos van a 2 columnas (D12), la descripción de 88 (D13), el SKU gris en edición (D14), los botones a la derecha con "Guardar" último (D15), la alerta de error (D16), el breadcrumb (D17) y el estado sin permiso en lugar de la card (D18). No seguir sin "aprobado" explícito. Registrar en el ledger: `Mockups formulario: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 6: Commit**

```bash
git add docs/ui-v2/mockups/inventario/producto-*.html docs/ui-v2/mockups/_base/base.css
git commit -m "docs(ui-v2): mockups aprobados del formulario de Productos (nuevo, editar, error y sin permiso)"
```

## Bloque C — XAML y comparaciones (cada task arranca solo con sus mockups aprobados)

### Task 4: `ProductoListView` v2 con sus dos estados comparados

**Depende de:** Tasks 1 y 2 (mockups aprobados; leer en el ledger las D# que cambiaron y ajustar los textos de este Step antes de escribir).

**Files:**
- Create: `src/StockApp.Presentation/Converters/ConteoConverter.cs`
- Create: `tests/StockApp.Presentation.Tests/Converters/ConteoConverterTests.cs`
- Modify: `src/StockApp.Presentation/Themes/V2/DataGridV2.axaml` (al final, antes de `</Styles>`)
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` (reescritura; `ProductoListView.axaml.cs` NO cambia)
- Modify: `tests/StockApp.Presentation.UiTests/TareaFakes.cs` (`NavigationRecorderFake`, L126-149)
- Create: `tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/GrillaStockHelpers.cs` (L112), `ProductoListViewStockOrdenTests.cs` (L64-71), `DataGridColumnasAutoAjusteTests.cs` (L213-222), `SignoNegativoBadgeTests.cs` (L58-75), `EtiquetadoDeCamposTests.cs` (L70-84), `GuardianDePatronTests.cs` (L52, L111, L221-230)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs`

**Interfaces:**
- Consumes: `DatosProductos.ConProductos()`, `SinProductos()` (Task 1); mockups `inventario/productos.html`, `productos-vacio.html` (Task 2); `ProductoListViewModel` tal cual; `EncabezadoPagina`, `TarjetaContenido`, `BarraFiltros(TextoBusqueda, MarcaAguaBusqueda, Filtros)`, `EstadoVacio(Titulo, Mensaje, EsError)`, `Insignia(Texto, Variante)`; `MonedaConverter`, `CantidadConverter`, `EsNegativoConverter`.
- Produces: `ConteoConverter.Instance` (`IValueConverter`, `int` + `"singular|plural"` → texto), `ConteoConverter.EsCero` y `ConteoConverter.HayElementos` (`FuncValueConverter<int, bool>`); estilos `.ui2 DataGridCell TextBlock.inactivo` y `.negativo`; `NavigationRecorderFake.UltimoInicializador` (`Delegate?`); partes nombradas de `ProductoListView`: `Encabezado`, `BotonEditar`, `BotonEliminar`, `BotonNuevo`, `Tarjeta`, `Filtros`, `BotonBuscar`, `Grilla`, `EstadoSinProductos`, `PieConteo`; `ProductosComparacionTests.OcultarSidebar(Control)` (público, lo usa la Task 5).

- [ ] **Step 1: `ConteoConverter` (rojo → verde)**

`tests/StockApp.Presentation.Tests/Converters/ConteoConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class ConteoConverterTests
{
    private static object? Convertir(object? valor, object? parametro = null) =>
        ConteoConverter.Instance.Convert(valor, typeof(string), parametro ?? "producto|productos", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(0, "0 productos")]
    [InlineData(1, "1 producto")]
    [InlineData(12, "12 productos")]
    [InlineData(1250, "1.250 productos")]
    public void Convert_SingularPluralYMilesEsUy(int n, string esperado) => Assert.Equal(esperado, Convertir(n));

    [Fact]
    public void Convert_SinParametroValido_DevuelveVacio()
    {
        Assert.Equal(string.Empty, Convertir(3, "productos"));
        Assert.Equal(string.Empty, Convertir(null));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, false)]
    public void EsCeroYHayElementos_SonOpuestos(int n, bool esCero)
    {
        Assert.Equal(esCero, ConteoConverter.EsCero.Convert(n, typeof(bool), null, CultureInfo.InvariantCulture));
        Assert.Equal(!esCero, ConteoConverter.HayElementos.Convert(n, typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ConteoConverterTests"` → FAIL (compilación).

`src/StockApp.Presentation/Converters/ConteoConverter.cs`:

```csharp
using System;
using System.Globalization;
using Avalonia.Data.Converters;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Pie de conteo de un listado v2 (spec §5.1): un int a "1 producto" / "12 productos" / "1.250 productos".
/// ConverterParameter = "singular|plural"; la cantidad sale de FormatoEsUy (miles con punto).
/// Se bindea a Items.Count, no a la colección: ObservableCollection notifica Count en cada Add/Clear,
/// mientras que un binding a la colección entera no se re-evalúa cuando cambia su contenido.
/// EsCero / HayElementos: visibilidad del estado vacío, de la grilla y del pie con el mismo binding.
/// </summary>
public sealed class ConteoConverter : IValueConverter
{
    public static readonly ConteoConverter Instance = new();

    public static readonly FuncValueConverter<int, bool> EsCero = new(n => n == 0);

    public static readonly FuncValueConverter<int, bool> HayElementos = new(n => n > 0);

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int n || parameter is not string formas || formas.Split('|') is not [var singular, var plural])
            return string.Empty;

        return $"{FormatoEsUy.Cantidad(n)} {(n == 1 ? singular : plural)}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

Run: el filtro → PASS.

- [ ] **Step 2: `NavigationRecorderFake.UltimoInicializador`**

En `TareaFakes.cs`, dentro de `NavigationRecorderFake`, agregar después de `UltimoInicializadorTareaForm`:

```csharp
    /// <summary>
    /// Inicializador de la última navegación con overload (cualquier ViewModel): permite ejecutarlo contra un
    /// ViewModel real y confirmar que se navegó con el dato correcto (por ejemplo, "Editar" con el producto
    /// seleccionado), no solo al tipo correcto.
    /// </summary>
    public Delegate? UltimoInicializador { get; private set; }
```

y en `Navegar<TVm>(Action<TVm> inicializar)`, como primera línea del cuerpo: `UltimoInicializador = inicializar;`. El resto no cambia.

- [ ] **Step 3: UiTests del listado v2 (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Listado de Productos v2 (spec §5.1) con la View REAL y el tema real: lo que se mide es el efecto (color
/// pintado, opacidad, habilitación, navegación por click real), no la configuración del XAML.
/// </summary>
public class ProductoListViewV2Tests
{
    private static readonly Color Terciario = Color.Parse("#78716C");
    private static readonly Color Etiqueta = Color.Parse("#44403C");
    private static readonly Color PeligroTexto = Color.Parse("#B91C1C");

    private sealed class ProductosFake : IProductoService
    {
        public List<ProductoDto> Lista { get; set; } = new();
        public List<int> Bajas { get; } = new();

        public Task<int> AltaAsync(Producto producto) => throw new NotSupportedException();
        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();

        public Task BajaLogicaAsync(int id)
        {
            Bajas.Add(id);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista.ToList());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) =>
            Task.FromResult<IReadOnlyList<ProductoDto>>(Lista
                .Where(p => string.IsNullOrEmpty(texto) || p.Nombre.Contains(texto, StringComparison.OrdinalIgnoreCase)).ToList());
    }

    private static ProductoDto P(int id, string nombre, decimal stock, bool activo = true) => new(
        id, $"C-{id:000}", null, nombre, null, 1, "Construcción", null, 1, "Unidad", 100m + id, stock, 0m, activo,
        new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly ProductoDto Activo = P(1, "Cemento portland 50 kg", 320m);
    private static readonly ProductoDto Negativo = P(2, "Hipoclorito de sodio 5 L", -3m);
    private static readonly ProductoDto Inactivo = P(3, "Carretilla reforzada", 4m, activo: false);
    private static readonly ProductoDto InactivoNegativo = P(4, "Pala de punta", -7m, activo: false);

    private sealed record Montaje(Window Ventana, ProductoListView Vista, ProductoListViewModel Vm, ProductosFake Servicio,
        NavigationRecorderFake Navegacion, ConfirmacionServiceFake Confirmacion);

    private static Montaje Montar(params ProductoDto[] productos)
    {
        var servicio = new ProductosFake { Lista = productos.ToList() };
        var navegacion = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var vm = new ProductoListViewModel(servicio, navegacion, confirmacion);
        var vista = new ProductoListView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await CargarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion, confirmacion);
    }

    private static T Nombrado<T>(Visual raiz, string nombre) where T : Control =>
        raiz.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static DataGridRow Fila(Montaje m, ProductoDto producto) =>
        m.Vista.GetVisualDescendants().OfType<DataGridRow>().Single(r => ReferenceEquals(r.DataContext, producto));

    /// <summary>TextBlocks de las celdas de la fila que NO son de una Insignia (la insignia pinta su propio color).</summary>
    private static List<TextBlock> TextosDeCeldas(DataGridRow fila) =>
        fila.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text) && t.FindAncestorOfType<Insignia>() is null)
            .ToList();

    private static Color ColorDe(TextBlock t) => Assert.IsAssignableFrom<ISolidColorBrush>(t.Foreground).Color;

    [AvaloniaFact]
    public void Raiz_TieneUi2YUsaLosComponentesV2()
    {
        var m = Montar(Activo);

        Assert.Contains("ui2", m.Vista.Classes);
        Assert.Equal("Productos", Nombrado<EncabezadoPagina>(m.Vista, "Encabezado").Titulo);
        Assert.NotNull(Nombrado<BarraFiltros>(m.Vista, "Filtros"));
        Assert.NotNull(Nombrado<TarjetaContenido>(m.Vista, "Tarjeta"));
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.HeaderVista>());
        Assert.Empty(m.Vista.GetVisualDescendants().OfType<StockApp.Presentation.Controls.BadgeEstado>());
    }

    [AvaloniaFact]
    public void UnSoloPrimario_YEsNuevo()
    {
        var m = Montar(Activo);
        var primarios = m.Vista.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Classes.Contains("primario") && ArbolVisual.EsVisibleEnArbol(b)).ToList();

        var nuevo = Assert.Single(primarios);
        Assert.Equal("Nuevo", nuevo.Content);
        Assert.Same(m.Vm.NuevoCommand, nuevo.Command);
    }

    [AvaloniaFact]
    public void Columnas_LasSieteActualesEnMayusculasYEnSuOrden()
    {
        var m = Montar(Activo);
        var headers = Nombrado<DataGrid>(m.Vista, "Grilla").Columns.Select(c => c.Header as string).ToArray();

        Assert.Equal(new string?[] { "CÓDIGO", "NOMBRE", "COSTO", "STOCK", "UNIDAD", "CATEGORÍA", "ESTADO" }, headers);
    }

    [AvaloniaFact]
    public void ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad()
    {
        var m = Montar(Activo, Inactivo);
        var fila = Fila(m, Inactivo);

        var textos = TextosDeCeldas(fila);
        Assert.Equal(6, textos.Count); // código, nombre, costo, stock, unidad, categoría
        Assert.All(textos, t => Assert.Equal(Terciario, ColorDe(t)));
        Assert.All(TextosDeCeldas(Fila(m, Activo)), t => Assert.Equal(Etiqueta, ColorDe(t)));

        var insignia = fila.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Inactiva");
        Assert.True(ArbolVisual.EsVisibleEnArbol(insignia));
        Assert.Equal(VarianteInsignia.Neutro, insignia.Variante);

        // Ninguna opacidad entre cada texto (o la insignia) y la grilla: U3 elimina la atenuación por Opacity.
        var grilla = Nombrado<DataGrid>(m.Vista, "Grilla");
        foreach (var v in textos.Cast<Visual>().Append(insignia))
        {
            for (Visual? a = v; a is not null && !ReferenceEquals(a, grilla); a = a.GetVisualParent())
                Assert.True(a.Opacity == 1, $"{a.GetType().Name} con Opacity {a.Opacity} en la fila inactiva");
        }
    }

    [AvaloniaFact]
    public void StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto()
    {
        var m = Montar(Activo, Negativo);

        var negativa = Fila(m, Negativo);
        var insignia = negativa.GetVisualDescendants().OfType<Insignia>().Single(i => i.Texto == "Stock negativo");
        Assert.True(ArbolVisual.EsVisibleEnArbol(insignia));
        Assert.Equal(VarianteInsignia.Peligro, insignia.Variante);
        Assert.Equal(PeligroTexto, ColorDe(TextosDeCeldas(negativa).Single(t => t.Text == "-3")));

        var positiva = Fila(m, Activo);
        Assert.DoesNotContain(positiva.GetVisualDescendants().OfType<Insignia>(),
            i => i.Texto == "Stock negativo" && ArbolVisual.EsVisibleEnArbol(i));
        Assert.Equal(Etiqueta, ColorDe(TextosDeCeldas(positiva).Single(t => t.Text == "320")));
    }

    [AvaloniaFact]
    public void InactivoConStockNegativo_ElNumeroSigueEnRojo()
    {
        var m = Montar(InactivoNegativo);
        var textos = TextosDeCeldas(Fila(m, InactivoNegativo));

        Assert.Equal(PeligroTexto, ColorDe(textos.Single(t => t.Text == "-7")));
        Assert.All(textos.Where(t => t.Text != "-7"), t => Assert.Equal(Terciario, ColorDe(t)));
    }

    [AvaloniaFact]
    public void EditarYEliminar_SinSeleccion_Deshabilitados()
    {
        var m = Montar(Activo, Inactivo);

        Assert.False(Nombrado<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(Nombrado<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void EditarYEliminar_ConActivoSeleccionado_Habilitados()
    {
        var m = Montar(Activo, Inactivo);
        Clickear(m.Ventana, Fila(m, Activo));

        Assert.Same(Activo, m.Vm.ItemSeleccionado);
        Assert.True(Nombrado<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.True(Nombrado<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void EditarYEliminar_ConInactivoSeleccionado_Deshabilitados()
    {
        var m = Montar(Activo, Inactivo);
        Clickear(m.Ventana, Fila(m, Inactivo));

        Assert.Same(Inactivo, m.Vm.ItemSeleccionado);
        Assert.False(Nombrado<Button>(m.Vista, "BotonEditar").IsEffectivelyEnabled);
        Assert.False(Nombrado<Button>(m.Vista, "BotonEliminar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Nuevo_ClickReal_NavegaAlFormulario()
    {
        var m = Montar(Activo);
        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonNuevo"));

        // NuevoAsync navega dentro de Task.Run.
        Assert.True(SpinWait.SpinUntil(() => m.Navegacion.UltimoTipoNavegado is not null, TimeSpan.FromSeconds(5)));
        Assert.Equal(typeof(ProductoFormViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Editar_ClickReal_AbreElFormularioConElSeleccionado()
    {
        var m = Montar(Activo, Negativo);
        Clickear(m.Ventana, Fila(m, Negativo));
        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonEditar"));

        Assert.True(SpinWait.SpinUntil(() => m.Navegacion.UltimoInicializador is not null, TimeSpan.FromSeconds(5)));
        var inicializar = Assert.IsType<Action<ProductoFormViewModel>>(m.Navegacion.UltimoInicializador);
        var formulario = new ProductoFormViewModel(new ProductoServiceFake(),
            new UnidadMedidaServiceFake(new List<UnidadMedida>()), new CategoriaServiceFake(new List<Categoria>()),
            new NavigationRecorderFake());
        inicializar(formulario);
        Assert.Equal(Negativo.Codigo, formulario.Codigo);
        Assert.True(formulario.EsEdicion);
    }

    [AvaloniaFact]
    public void Eliminar_ClickReal_ConfirmaYDaDeBajaElSeleccionado()
    {
        var m = Montar(Activo, Negativo);
        Clickear(m.Ventana, Fila(m, Negativo));
        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonEliminar"));

        Assert.Equal(new[] { Negativo.Id }, m.Servicio.Bajas);
    }

    [AvaloniaFact]
    public void ListaVacia_MuestraEstadoVacio_YAlLlegarDatosVuelveLaGrilla()
    {
        var m = Montar();

        Assert.True(ArbolVisual.EsVisibleEnArbol(Nombrado<EstadoVacio>(m.Vista, "EstadoSinProductos")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<DataGrid>(m.Vista, "Grilla")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<TextBlock>(m.Vista, "PieConteo")));

        m.Servicio.Lista = [Activo, Negativo];
        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonBuscar"));

        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<EstadoVacio>(m.Vista, "EstadoSinProductos")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Nombrado<DataGrid>(m.Vista, "Grilla")));
        Assert.Equal("2 productos", Nombrado<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void PieDeConteo_SingularYPluralConMilesEsUy()
    {
        var m = Montar(Activo);
        Assert.Equal("1 producto", Nombrado<TextBlock>(m.Vista, "PieConteo").Text);

        m.Servicio.Lista = Enumerable.Range(1, 1250).Select(i => P(i, $"Producto {i}", 1m)).ToList();
        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonBuscar"));
        Assert.Equal("1.250 productos", Nombrado<TextBlock>(m.Vista, "PieConteo").Text);
    }

    [AvaloniaFact]
    public void Busqueda_TipeadaEnLaBarra_LlegaAlFiltroDelVm()
    {
        var m = Montar(Activo);
        var caja = Nombrado<BarraFiltros>(m.Vista, "Filtros").GetVisualDescendants().OfType<TextBox>().Single(t => t.Name == "PART_Busqueda");

        caja.Text = "cemento";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("cemento", m.Vm.FiltroBusqueda);
        Assert.False(string.IsNullOrEmpty(caja.PlaceholderText));
    }
}
```

(`ArbolVisual`, `ConfirmacionServiceFake`, `ProductoServiceFake`, `UnidadMedidaServiceFake` y `CategoriaServiceFake` ya existen en `StockApp.Presentation.UiTests`; `FindAncestorOfType` viene de `Avalonia.VisualTree`.)

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ProductoListViewV2Tests"`
Expected: FAIL (las partes nombradas no existen; la vista vieja no tiene `ui2`, usa `HeaderVista`/`BadgeEstado` y atenúa con `Opacity` 0,55). Pegar en el reporte el rojo de `ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad` contra la vista vieja: prueba que el test ve la opacidad.

- [ ] **Step 4: Estilos de celda en `DataGridV2.axaml`**

Antes de `</Styles>` (después del bloque `.ui2 DataGridCell.num TextBlock`):

```xml
    <!-- Fila inactiva: el texto de la celda pasa a terciario por token, sin Opacity (con Opacity la insignia
         "Inactiva" de la misma fila quedaba atenuada y casi ilegible). La vista marca cada TextBlock de celda
         con Classes.inactivo bindeado a !Activo; la Insignia pinta su texto en su plantilla, sin la clase.
         Espejo: .v2-grilla-fila--inactiva en base.css. -->
    <Style Selector=".ui2 DataGridCell TextBlock.inactivo">
        <Setter Property="Foreground" Value="{DynamicResource V2TextoTerciarioBrush}" />
    </Style>
    <!-- Número negativo (stock): peligro texto #B91C1C de la spec, no el DangerBrush viejo. Va DESPUÉS de
         .inactivo a propósito: a igual especificidad gana el último, así un inactivo con stock negativo
         sigue en rojo. Espejo: .v2-texto-peligro en base.css. -->
    <Style Selector=".ui2 DataGridCell TextBlock.negativo">
        <Setter Property="Foreground" Value="{DynamicResource V2PeligroTextoBrush}" />
    </Style>
```

- [ ] **Step 5: El XAML**

Reemplazar `src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml` entero por (con las D# del ledger aplicadas; abajo, la propuesta):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:dto="using:StockApp.Application.Catalogo"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Catalogo.ProductoListView"
             x:DataType="vm:ProductoListViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §5.1). Espejo: docs/ui-v2/mockups/inventario/productos.html y productos-vacio.html.
         Editar y Eliminar se habilitan solo con un producto activo seleccionado (regla del VM). -->
    <DockPanel Margin="28,24">

        <v2:EncabezadoPagina x:Name="Encabezado" DockPanel.Dock="Top" Titulo="Productos">
            <StackPanel Orientation="Horizontal" Spacing="8">
                <Button x:Name="BotonEditar" Classes="secundario" Content="Editar" Command="{Binding EditarCommand}" />
                <Button x:Name="BotonEliminar" Classes="peligro" Content="Eliminar" Command="{Binding BajaCommand}" />
                <Button x:Name="BotonNuevo" Classes="primario" Content="Nuevo" Command="{Binding NuevoCommand}" />
            </StackPanel>
        </v2:EncabezadoPagina>

        <v2:TarjetaContenido x:Name="Tarjeta">
            <DockPanel>
                <v2:BarraFiltros x:Name="Filtros"
                                 DockPanel.Dock="Top"
                                 TextoBusqueda="{Binding FiltroBusqueda}"
                                 MarcaAguaBusqueda="Nombre, SKU o código de barras">
                    <Button x:Name="BotonBuscar" Classes="secundario" Content="Buscar" Command="{Binding BuscarCommand}" />
                </v2:BarraFiltros>

                <TextBlock x:Name="PieConteo"
                           DockPanel.Dock="Bottom"
                           Classes="pie-conteo"
                           Margin="0,12,0,0"
                           Text="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='producto|productos'}"
                           IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}" />

                <v2:EstadoVacio x:Name="EstadoSinProductos"
                                DockPanel.Dock="Top"
                                Titulo="No hay productos para mostrar"
                                Mensaje="Probá con otra búsqueda o creá uno con Nuevo."
                                IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.EsCero}}" />

                <!-- Columnas de plantilla para marcar cada TextBlock con Classes.inactivo (texto terciario por
                     token, nunca Opacity). SortMemberPath conserva el orden por click en el encabezado.
                     MinWidth por columna y scroll horizontal Auto: fix 3548397. -->
                <DataGrid x:Name="Grilla"
                          ItemsSource="{Binding ItemsView}"
                          SelectedItem="{Binding ItemSeleccionado}"
                          IsReadOnly="True"
                          CanUserSortColumns="True"
                          CanUserResizeColumns="True"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto"
                          IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="CÓDIGO" Width="Auto" SortMemberPath="Codigo">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <TextBlock Text="{Binding Codigo}" Classes.inactivo="{Binding !Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="NOMBRE" Width="2*" SortMemberPath="Nombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <TextBlock Text="{Binding Nombre}"
                                               Classes.inactivo="{Binding !Activo}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Nombre}"
                                               VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="COSTO" Width="Auto" MinWidth="100" CellStyleClasses="num" SortMemberPath="PrecioCosto">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <TextBlock Text="{Binding PrecioCosto, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Classes.inactivo="{Binding !Activo}"
                                               VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="STOCK" Width="220" MinWidth="100" CellStyleClasses="num" SortMemberPath="StockActual">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <!-- Ancho FIJO 220 y una sola Grid con la insignia a la izquierda y el número a
                                         la derecha: la grilla recicla las celdas al ordenar y un contenedor que
                                         dependía de la visibilidad de la insignia dejaba el número corrido o
                                         recortado (22 se veía "2"). 220 = peor caso "-99999,5" con la insignia. -->
                                    <Grid VerticalAlignment="Center">
                                        <v2:Insignia Texto="Stock negativo"
                                                     Variante="Peligro"
                                                     HorizontalAlignment="Left"
                                                     IsVisible="{Binding StockActual, Converter={x:Static conv:EsNegativoConverter.Instance}}" />
                                        <TextBlock Text="{Binding StockActual, Converter={x:Static conv:CantidadConverter.Instance}}"
                                                   Classes.inactivo="{Binding !Activo}"
                                                   Classes.negativo="{Binding StockActual, Converter={x:Static conv:EsNegativoConverter.Instance}}"
                                                   HorizontalAlignment="Right"
                                                   VerticalAlignment="Center" />
                                    </Grid>
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="UNIDAD" Width="Auto" SortMemberPath="UnidadMedidaNombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <TextBlock Text="{Binding UnidadMedidaNombre}" Classes.inactivo="{Binding !Activo}" VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="CATEGORÍA" Width="*" SortMemberPath="CategoriaNombre">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <TextBlock Text="{Binding CategoriaNombre, TargetNullValue='—'}"
                                               Classes.inactivo="{Binding !Activo}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding CategoriaNombre, TargetNullValue='—'}"
                                               VerticalAlignment="Center" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTemplateColumn Header="ESTADO" Width="Auto" CanUserSort="False">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:ProductoDto">
                                    <v2:Insignia Texto="Inactiva" Variante="Neutro" IsVisible="{Binding !Activo}" />
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

Run: `dotnet build StockApp.sln` → 0 errores (los compiled bindings validan `Items.Count`, `!Activo` y cada propiedad de `ProductoDto`).

- [ ] **Step 6: Tests heredados**

1. `GrillaStockHelpers.cs`, en `AssertBadgeNoPisaElNumero` (L112), reemplazar la búsqueda del badge por:

```csharp
            // BadgeEstado (vistas del tema viejo) o Insignia (vistas v2): mientras convivan, los dos.
            var badge = celda.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(b => (b is BadgeEstado || b is StockApp.Presentation.Controls.V2.Insignia) && b.IsVisible);
```

y actualizar el `<summary>` de la clase: "El badge es `BadgeEstado` en las vistas viejas e `Insignia` en las v2". `NumerosDeColumna` no cambia: el número sigue siendo el ÚLTIMO `TextBlock` de la celda (la insignia va primero en la `Grid`).

2. `ProductoListViewStockOrdenTests.cs`: `"Stock"` → `"STOCK"` y `"Costo"` → `"COSTO"` en las llamadas de L60-71; en el `<summary>`, "badge" → "insignia (Insignia v2)".

3. `DataGridColumnasAutoAjusteTests.cs`, `Productos_AlLlegarLosDatos_LaColumnaStockMideEfectivamente220`: `Columna(grid, "STOCK")`, `AssertSinRecorte(grid, window, "carga", "CÓDIGO", "COSTO", "UNIDAD")`, `AssertBadgeNoPisaElNumero(grid, window, "STOCK", "carga")`. Si D6 cambió el ancho de Stock, el 220 de la aserción pasa al valor aprobado.

4. `SignoNegativoBadgeTests.cs`, `Montar_ProductoListViewConStockNegativo_MuestraBadgeStockNegativoSoloEnLaFilaNegativa`, reemplazar las dos últimas líneas por:

```csharp
        // Productos es v2: la palabra la pone una Insignia peligro (las demás vistas siguen con BadgeEstado).
        var insignias = window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.Insignia>()
            .Where(i => i.Texto == "Stock negativo").ToList();

        var visible = Assert.Single(insignias, i => i.IsVisible);
        Assert.Equal(StockApp.Presentation.Controls.V2.VarianteInsignia.Peligro, visible.Variante);
```

5. `EtiquetadoDeCamposTests.cs`: reemplazar `ProductoListView_FiltroBusqueda_TieneEtiquetaCampoFormulario` (con su `<summary>`) por:

```csharp
    /// <summary>
    /// ProductoListView (UI v2): el buscador vive en v2:BarraFiltros, que no tiene etiqueta (patrón de listado
    /// aprobado); lo que explica el campo es la marca de agua, así que no puede faltar.
    /// </summary>
    [Fact]
    public void ProductoListView_Busqueda_EnBarraFiltrosConMarcaDeAgua()
    {
        var axaml = LeerAxaml("StockApp.Presentation/Views/Catalogo/ProductoListView.axaml");
        Assert.Matches(@"<v2:BarraFiltros\b[^>]*\bMarcaAguaBusqueda=""[^""]+""", axaml);
        Assert.Matches(@"<v2:BarraFiltros\b[^>]*\bTextoBusqueda=""\{Binding FiltroBusqueda\}""", axaml);
    }
```

(Si en el GATE el usuario pidió conservar la etiqueta, D3 cambió: este reemplazo no aplica y la task suma la etiqueta que se haya acordado.)

6. `GuardianDePatronTests.cs`: borrar `[InlineData(typeof(ProductoListView), "Productos", "CATÁLOGO")]` (L52); sacar `typeof(ProductoListView),` de `VistasDeLaTanda` (L111); agregar `typeof(ProductoListView),` a `VistasMigradasAV2` (L221-230).

- [ ] **Step 7: Comparaciones (rojo → verde)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Productos (spec §5.1 y §5.2) dentro del shell real, con la vista resuelta por ViewLocator. El sidebar
/// tiene su propia comparación (ShellComparacionTests): se oculta en los dos renders sin mover el layout.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class ProductosComparacionTests
{
    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    public static void OcultarSidebar(Control vista) =>
        ((ShellMainView)vista).FindControl<Border>("Sidebar")!.IsVisible = false;

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar);

    [AvaloniaFact]
    public void Productos_IgualAlMockup() => Comparar(DatosProductos.ConProductos(), "inventario/productos.html");

    [AvaloniaFact]
    public void Productos_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.ConProductos(), "inventario/productos.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup() => Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html");

    [AvaloniaFact]
    public void ProductosVacio_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinProductos(), "inventario/productos-vacio.html", TamanoLienzo.Municipio);
}
```

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`, `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj` y `scripts/ui-v2/comparar.sh Productos`.
Expected: UiTests y VM en verde (incluidos `ProductoListViewV2Tests`, `ProductoListViewStockOrdenTests`, `DataGridColumnasAutoAjusteTests`, `SignoNegativoBadgeTests`, `EtiquetadoDeCamposTests`, `GrillasSinRecorteBarridoTests`, `GuardianDePatronTests` con `VistaV2_NoTieneOpacidadesLiterales(ProductoListView)`, `ComandosDeGuardadoGuardianTests`, `CargaProtegidaSinPermisoGuardianTests`); las 4 comparaciones de `ProductosComparacionTests` APROBADAS con los valores calibrados y sin recortes, o el Step 8.

Si una comparación no pasa: abrir su página y diagnosticar ANTES de pensar en umbral (sección "Herramientas de diagnóstico"). Causas esperables: (1) una columna `Auto` con otro ancho → re-medir `ActualWidth` en la app (un `Assert.Fail` temporal con los anchos en `ProductoListViewV2Tests`, que no se commitea) y corregir el mockup (convención 14); (2) la última fila o el pie corridos 1 px por alto impar de la card → compensación entera documentada en §6.5; (3) la insignia "Stock negativo" o "Inactiva" con otra posición vertical → comparar con `catalogo.html`, que ya aprobó una insignia en celda. Nunca subir el umbral para tapar geometría.

- [ ] **Step 8: Umbral (solo si, con la geometría ya idéntica, las teselas densas pasan de 11) — GATE**

1. Correr `teselas.py` sobre `inventario-productos` y `inventario-productos@1366x768`: TODAS las teselas tienen que salir "tono". Una "GEOMETRIA" vuelve al Step 7.
2. Para cada tesela, `tinta.py` sobre su región: proporción estable para el estilo (texto de celda 13/400 `#44403C`, header 11/600, insignia 12/500) y bordes iguales. Verificar familia/tamaño/peso/color/alto de línea mockup vs XAML.
3. Mutación pálida (exigida si el umbral propuesto es 16 o más): QUITAR `Variante="Peligro"` de la insignia "Stock negativo" (pasa a neutra), correr la comparación, anotar cuántas teselas da y restaurar. Tiene que superar el umbral propuesto; si no, la métrica no ve ese defecto en esta pantalla: PARAR y decírselo al usuario (no se aprueba un umbral que lo tape).
4. PARAR. Presentar al usuario la página de comparación de cada tamaño con CADA tesela densa (contorno magenta), la clasificación y las proporciones. Solo con su OK explícito, registrar en `ProductosComparacionTests`:

```csharp
    private const string Tono = "solo tono de texto (celdas 13/400, headers 11/600, insignias 12/500), geometría igual";

    private static readonly UmbralAprobado ProductosEstandar = new(<teselas medidas>, "Martin", "<yyyy-MM-dd>",
        "Productos 1280x760: <teselas> teselas, " + Tono,
        MutacionPalidaEnRojo: "Insignia Stock negativo sin Variante=Peligro: <n> teselas");
```

(una constante por pantalla y tamaño que lo necesite, con los números medidos), y pasarla como `aprobado:` en su test. Las pantallas que pasan con lo calibrado NO llevan `UmbralAprobado`. Anotar en el ledger cada registro.

- [ ] **Step 9: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml "$SCRATCH/ProductoListView.axaml.bak"
cp src/StockApp.Presentation/Themes/V2/DataGridV2.axaml "$SCRATCH/DataGridV2.axaml.bak"
```

Cada una: quitar, `git diff --no-index` contra su `.bak` (solo esa línea o bloque), `dotnet build StockApp.sln`, correr, PEGAR el rojo, restaurar con `cp`, diff vacío.

(a) Quitar `Classes.inactivo="{Binding !Activo}"` del `TextBlock` de NOMBRE → rojo en `ProductoInactivo_TextoTerciarioInsigniaPlenaYSinOpacidad` ("Nombre" en `#44403C`).
(b) En `DataGridV2.axaml`, quitar el estilo `.ui2 DataGridCell TextBlock.inactivo` entero → rojo en `ProductoInactivo_…` e `InactivoConStockNegativo_…`, y en `Productos_IgualAlMockup` si la fila inactiva supera el umbral (anotar si queda verde: entonces la custodia de este estilo es SOLO el UiTest).
(c) En `DataGridV2.axaml`, quitar el estilo `.negativo` → rojo en `StockNegativo_InsigniaPeligroYNumeroEnPeligroTexto` e `InactivoConStockNegativo_ElNumeroSigueEnRojo`.
(d) Quitar `Variante="Peligro"` de la insignia "Stock negativo" → rojo en `StockNegativo_…` (y la cuenta de teselas del Step 8.3).
(e) Quitar `IsVisible="{Binding Items.Count, Converter={x:Static conv:ConteoConverter.EsCero}}"` del `EstadoVacio` → rojo en `ListaVacia_…` cuando llegan datos (el estado queda visible) y en `Productos_IgualAlMockup`.
(f) Quitar `Command="{Binding BajaCommand}"` de `BotonEliminar` → rojo en `Eliminar_ClickReal_ConfirmaYDaDeBajaElSeleccionado` y `EditarYEliminar_ConActivoSeleccionado_Habilitados`.
(g) Quitar `Classes="ui2"` de la raíz → rojo en `Raiz_TieneUi2YUsaLosComponentesV2` y en las 4 comparaciones.
(h) Quitar `SelectedItem="{Binding ItemSeleccionado}"` → rojo en `EditarYEliminar_ConActivoSeleccionado_Habilitados` y `Editar_ClickReal_…`.

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Presentation/Converters/ConteoConverter.cs tests/StockApp.Presentation.Tests/Converters/ConteoConverterTests.cs src/StockApp.Presentation/Themes/V2/DataGridV2.axaml src/StockApp.Presentation/Views/Catalogo/ProductoListView.axaml tests/StockApp.Presentation.UiTests/TareaFakes.cs tests/StockApp.Presentation.UiTests/V2/ProductoListViewV2Tests.cs tests/StockApp.Presentation.UiTests/GrillaStockHelpers.cs tests/StockApp.Presentation.UiTests/ProductoListViewStockOrdenTests.cs tests/StockApp.Presentation.UiTests/DataGridColumnasAutoAjusteTests.cs tests/StockApp.Presentation.UiTests/SignoNegativoBadgeTests.cs tests/StockApp.Presentation.UiTests/EtiquetadoDeCamposTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs
git commit -m "feat(ui-v2): listado de Productos v2 con inactivos por token, stock negativo y estado vacío comparados"
```

(Si el Step 7 corrigió el mockup, `docs/ui-v2/mockups/inventario/productos*.html` entra en este commit y el reporte lo dice.)

### Task 5: `ProductoFormView` v2 con sus cuatro estados comparados

**Depende de:** Tasks 1, 3 (mockups aprobados) y 4 (`ProductosComparacionTests.OcultarSidebar`).

**Files:**
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` (después de `.ui2 TextBox:error`, L224-226)
- Modify: `src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml` (reescritura; `ProductoFormView.axaml.cs` NO cambia)
- Create: `tests/StockApp.Presentation.UiTests/V2/ProductoFormViewV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/ProductoFormPrecioCostoFormatoTests.cs` (L10, L38), `GuardarBloqueadoConErroresDeEntradaTests.cs` (L61-63), `CatalogoFormAnchoYCancelarTests.cs` (L140-142), `GuardianDePatronTests.cs` (L54, L113, `VistasMigradasAV2`)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs`

**Interfaces:**
- Consumes: `DatosProductos.Nuevo()`, `Editar()`, `ConError()`, `SinPermiso()`; mockups `inventario/producto-*.html`; `ProductoFormViewModel` tal cual; `EncabezadoPagina(Titulo, BreadcrumbOrigen, BreadcrumbComando, BreadcrumbActual)`, `CampoFormulario(Etiqueta, Requerido)`, `Alerta(Texto, Variante)`, `EstadoVacio`, `TarjetaContenido`; `NavigationRecorderFake.UltimoTipoNavegado`.
- Produces: estilo `.ui2 TextBox:disabled`; partes nombradas de `ProductoFormView`: `Encabezado`, `EstadoSinPermiso`, `ColumnaFormulario` (Grid), `Tarjeta`, `CampoSku`, `CampoCodigoBarras`, `CampoNombre`, `CampoDescripcion`, `CampoPrecioCosto`, `CampoUnidad`, `CampoCategoria`, `AlertaError`, `PieFormulario`, `BotonCancelar`, `BotonGuardar`.

- [ ] **Step 1: UiTests del formulario v2 (rojo)**

`tests/StockApp.Presentation.UiTests/V2/ProductoFormViewV2Tests.cs`:

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
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.Views.Catalogo;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Formulario de producto v2 (spec §4.4 y §5.2) con la View REAL, clicks reales y el tema real.</summary>
public class ProductoFormViewV2Tests
{
    private sealed class ProductosGrabador : IProductoService
    {
        public Exception? FallaAlGuardar { get; set; }
        public List<Producto> Altas { get; } = new();

        public Task<int> AltaAsync(Producto producto)
        {
            if (FallaAlGuardar is not null) throw FallaAlGuardar;
            Altas.Add(producto);
            return Task.FromResult(99);
        }

        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto) => throw new NotSupportedException();
    }

    private sealed class UnidadesSinPermiso : IUnidadMedidaService
    {
        public Task<UnidadMedida> GarantizarUnidadPorDefectoAsync() => throw new UnauthorizedAccessException("403");
        public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => throw new UnauthorizedAccessException("403");
        public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => throw new NotSupportedException();
        public Task<int> AltaAsync(UnidadMedida unidadMedida) => throw new NotSupportedException();
        public Task ModificarAsync(UnidadMedida unidadMedida) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
    }

    private static readonly UnidadMedida Unidad = new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };

    private static readonly ProductoDto Existente = new(7, "MAT-0142", "7730123456789", "Cemento portland 50 kg", null,
        null, null, null, 1, "Unidad", 480m, 320m, 0m, true, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private sealed record Montaje(Window Ventana, ProductoFormView Vista, ProductoFormViewModel Vm, ProductosGrabador Servicio,
        NavigationRecorderFake Navegacion);

    private static Montaje Montar(bool edicion = false, IUnidadMedidaService? unidades = null)
    {
        var servicio = new ProductosGrabador();
        var navegacion = new NavigationRecorderFake();
        var vm = new ProductoFormViewModel(servicio, unidades ?? new UnidadMedidaServiceFake(new List<UnidadMedida> { Unidad }),
            new CategoriaServiceFake(new List<Categoria>()), navegacion);
        if (edicion) vm.CargarParaEditar(Existente);
        var vista = new ProductoFormView { DataContext = vm };
        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion);
    }

    private static T Nombrado<T>(Visual raiz, string nombre) where T : Control =>
        raiz.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static void Clickear(Window ventana, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var p = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), ventana)!.Value;
        ventana.MouseMove(p);
        ventana.MouseDown(p, MouseButton.Left);
        ventana.MouseUp(p, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Rect EnVentana(Window ventana, Control c) =>
        new(c.TranslatePoint(new Point(0, 0), ventana)!.Value, c.Bounds.Size);

    [AvaloniaFact]
    public void Raiz_TieneUi2_YBreadcrumbConElTituloDelVm()
    {
        var alta = Montar();
        var encabezado = Nombrado<EncabezadoPagina>(alta.Vista, "Encabezado");

        Assert.Contains("ui2", alta.Vista.Classes);
        Assert.Equal("Productos", encabezado.BreadcrumbOrigen);
        Assert.Equal("Nuevo producto", encabezado.BreadcrumbActual);
        Assert.Equal("Nuevo producto", encabezado.Titulo);
        Assert.Same(alta.Vm.CancelarCommand, encabezado.BreadcrumbComando);

        Assert.Equal("Editar producto", Nombrado<EncabezadoPagina>(Montar(edicion: true).Vista, "Encabezado").Titulo);
    }

    [AvaloniaFact]
    public void Breadcrumb_ClickReal_VuelveAlListadoSinGuardar()
    {
        var m = Montar();
        m.Vm.Codigo = "X-1";
        m.Vm.Nombre = "Algo";
        var origen = Nombrado<EncabezadoPagina>(m.Vista, "Encabezado").GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_BreadcrumbOrigen");

        Clickear(m.Ventana, origen);

        Assert.Equal(typeof(ProductoListViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.Empty(m.Servicio.Altas);
    }

    [AvaloniaFact]
    public void Tarjeta_Mide760YQuedaALaIzquierda()
    {
        var m = Montar();
        var tarjeta = EnVentana(m.Ventana, Nombrado<TarjetaContenido>(m.Vista, "Tarjeta"));

        Assert.Equal(760, tarjeta.Width);
        Assert.Equal(28, tarjeta.X);
    }

    [AvaloniaFact]
    public void Campos_EnDosColumnasDondeCorresponde()
    {
        var m = Montar();
        Rect R(string nombre) => EnVentana(m.Ventana, Nombrado<CampoFormulario>(m.Vista, nombre));

        Assert.Equal(R("CampoSku").Y, R("CampoCodigoBarras").Y);
        Assert.True(R("CampoCodigoBarras").X > R("CampoSku").Right);
        Assert.Equal(R("CampoSku").X, R("CampoNombre").X);
        Assert.Equal(R("CampoCodigoBarras").Right, R("CampoNombre").Right);
        Assert.Equal(R("CampoNombre").Width, R("CampoDescripcion").Width);
        Assert.Equal(R("CampoPrecioCosto").Y, R("CampoUnidad").Y);
        Assert.Equal(R("CampoPrecioCosto").X, R("CampoCategoria").X);
        Assert.True(R("CampoCategoria").Y > R("CampoPrecioCosto").Bottom);
    }

    [AvaloniaFact]
    public void Requeridos_SoloSkuYNombre()
    {
        var m = Montar();
        var requeridos = m.Vista.GetVisualDescendants().OfType<CampoFormulario>().Where(c => c.Requerido).Select(c => c.Etiqueta).ToList();

        Assert.Equal(new string?[] { "SKU", "Nombre" }, requeridos);
    }

    [AvaloniaFact]
    public void Edicion_SkuDeshabilitadoConTokensDeDeshabilitado()
    {
        var edicion = Montar(edicion: true);
        var sku = Nombrado<CampoFormulario>(edicion.Vista, "CampoSku").GetVisualDescendants().OfType<TextBox>().First();

        Assert.False(sku.IsEffectivelyEnabled);
        Assert.Equal("MAT-0142", sku.Text);
        var borde = sku.GetVisualDescendants().OfType<Border>().First(b => b.Name == "PART_BorderElement");
        Assert.Equal(Color.Parse("#F5F5F4"), Assert.IsAssignableFrom<ISolidColorBrush>(borde.Background).Color);
        Assert.Equal(Color.Parse("#E7E5E4"), Assert.IsAssignableFrom<ISolidColorBrush>(borde.BorderBrush).Color);

        var alta = Montar();
        Assert.True(Nombrado<CampoFormulario>(alta.Vista, "CampoSku").GetVisualDescendants().OfType<TextBox>().First().IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Pie_CancelarSecundarioYGuardarUnicoPrimarioALaDerecha()
    {
        var m = Montar();
        var cancelar = Nombrado<Button>(m.Vista, "BotonCancelar");
        var guardar = Nombrado<Button>(m.Vista, "BotonGuardar");

        Assert.Contains("secundario", cancelar.Classes);
        Assert.Equal("Guardar", guardar.Content);
        Assert.Same(m.Vm.GuardarCommand, guardar.Command);
        Assert.True(EnVentana(m.Ventana, guardar).X > EnVentana(m.Ventana, cancelar).Right);
        Assert.Equal(EnVentana(m.Ventana, Nombrado<TarjetaContenido>(m.Vista, "Tarjeta")).Right - 1 - 24,
            EnVentana(m.Ventana, guardar).Right, 0);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
    }

    [AvaloniaFact]
    public void Guardar_ClickReal_ConDatosValidos_DaDeAltaYVuelveAlListado()
    {
        var m = Montar();
        m.Vm.Codigo = "NUE-0001";
        m.Vm.Nombre = "Producto nuevo";
        Dispatcher.UIThread.RunJobs();

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonGuardar"));

        var alta = Assert.Single(m.Servicio.Altas);
        Assert.Equal("NUE-0001", alta.Codigo);
        Assert.Equal(typeof(ProductoListViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Guardar_ConErrorDelServicio_MuestraLaAlertaYNoNavega()
    {
        var m = Montar();
        m.Servicio.FallaAlGuardar = new ReglaDeNegocioException("Ya existe un producto con el código 'MAT-0142'.");
        m.Vm.Codigo = "MAT-0142";
        m.Vm.Nombre = "Cemento portland 50 kg";
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<Alerta>(m.Vista, "AlertaError")));

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonGuardar"));

        var alerta = Nombrado<Alerta>(m.Vista, "AlertaError");
        Assert.True(ArbolVisual.EsVisibleEnArbol(alerta));
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("Ya existe un producto con el código 'MAT-0142'.", alerta.Texto);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void SinPermiso_MuestraElEstadoEnLugarDeLaTarjeta()
    {
        var m = Montar(unidades: new UnidadesSinPermiso());

        Assert.True(m.Vm.SinPermiso);
        var estado = Nombrado<EstadoVacio>(m.Vista, "EstadoSinPermiso");
        Assert.True(ArbolVisual.EsVisibleEnArbol(estado));
        Assert.True(estado.EsError);
        Assert.Equal("No tenés permiso para cargar los datos de este producto.", estado.Mensaje);
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<TarjetaContenido>(m.Vista, "Tarjeta")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Nombrado<EncabezadoPagina>(m.Vista, "Encabezado")));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ProductoFormViewV2Tests"`
Expected: FAIL (no existen las partes nombradas; la vista vieja usa `HeaderVista`, `Controls.CampoFormulario` y la card de 480).

- [ ] **Step 2: `TextBox` deshabilitado**

En `ControlesV2.axaml`, después del estilo `.ui2 TextBox:error /template/ Border#PART_BorderElement`:

```xml
    <!-- TextBox deshabilitado (el SKU en edición): los mismos tokens de deshabilitado que los botones, en vez
         del deshabilitado de Fluent. Espejo: .v2-input--deshabilitado en base.css. -->
    <Style Selector=".ui2 TextBox:disabled /template/ Border#PART_BorderElement">
        <Setter Property="Background" Value="{DynamicResource V2DeshabilitadoFondoBrush}" />
        <Setter Property="BorderBrush" Value="{DynamicResource V2BordeBrush}" />
    </Style>
    <Style Selector=".ui2 TextBox:disabled">
        <Setter Property="Foreground" Value="{DynamicResource V2DeshabilitadoTextoBrush}" />
    </Style>
```

Si la plantilla de Fluent pinta el texto deshabilitado con su propio recurso en una parte (por ejemplo `TextPresenter` con `TextControlForegroundDisabled`) y el render no sale `#A8A29E`, agregar el setter sobre esa parte (`/template/ TextPresenter#PART_TextPresenter`) y anotarlo en el reporte con la medición; `Producto…Editar_IgualAlMockup` lo detecta.

- [ ] **Step 3: El XAML**

Reemplazar `src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml` entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Catalogo"
             xmlns:ent="using:StockApp.Domain.Entities"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Catalogo.ProductoFormView"
             x:DataType="vm:ProductoFormViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §4.4 y §5.2). Espejo: docs/ui-v2/mockups/inventario/producto-*.html. El breadcrumb vuelve
         con CancelarCommand (sin back-stack, no guarda). Sin permiso, el estado reemplaza a la card. -->
    <ScrollViewer x:Name="Desplazamiento" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="28,24">

            <v2:EncabezadoPagina x:Name="Encabezado"
                                 Titulo="{Binding Titulo}"
                                 BreadcrumbOrigen="Productos"
                                 BreadcrumbComando="{Binding CancelarCommand}"
                                 BreadcrumbActual="{Binding Titulo}" />

            <v2:EstadoVacio x:Name="EstadoSinPermiso"
                            Titulo="Sin permiso"
                            Mensaje="{Binding MensajeSinPermiso}"
                            EsError="True"
                            IsVisible="{Binding SinPermiso}" />

            <!-- Columna * con MaxWidth 760: la card mide 760 a la izquierda (con HorizontalAlignment Left y
                 MaxWidth en la card mediría solo lo que pide su contenido). -->
            <Grid x:Name="ColumnaFormulario" IsVisible="{Binding !SinPermiso}">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                </Grid.ColumnDefinitions>

                <v2:TarjetaContenido x:Name="Tarjeta" Padding="24">
                    <StackPanel>
                        <TextBlock Classes="subtitulo-formulario" Text="Datos del producto" Margin="0,0,0,12" />
                        <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,16,Auto,16,Auto">
                            <!-- En edición el SKU no es editable: ProductoService.ModificarAsync no reasigna
                                 el código, así que tipearlo no tendría efecto. -->
                            <v2:CampoFormulario x:Name="CampoSku" Grid.Row="0" Grid.Column="0" Etiqueta="SKU" Requerido="True">
                                <TextBox Text="{Binding Codigo}" IsEnabled="{Binding !EsEdicion}" PlaceholderText="Ej: P001" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoCodigoBarras" Grid.Row="0" Grid.Column="2" Etiqueta="Código de barras">
                                <TextBox Text="{Binding CodigoBarras}" PlaceholderText="EAN (opcional)" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoNombre" Grid.Row="2" Grid.Column="0" Grid.ColumnSpan="3" Etiqueta="Nombre" Requerido="True">
                                <TextBox Text="{Binding Nombre}" PlaceholderText="Nombre del producto" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoDescripcion" Grid.Row="4" Grid.Column="0" Grid.ColumnSpan="3" Etiqueta="Descripción">
                                <TextBox Text="{Binding Descripcion}"
                                         PlaceholderText="Descripción opcional"
                                         AcceptsReturn="True"
                                         TextWrapping="Wrap"
                                         Height="88" />
                            </v2:CampoFormulario>
                        </Grid>

                        <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />

                        <TextBlock Classes="subtitulo-formulario" Text="Costo y clasificación" Margin="0,0,0,12" />
                        <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,16,Auto">
                            <v2:CampoFormulario x:Name="CampoPrecioCosto" Grid.Row="0" Grid.Column="0" Etiqueta="Precio costo">
                                <TextBox Text="{Binding PrecioCosto, Converter={x:Static conv:DecimalConverter.Instance}}"
                                         beh:EntradaNumericaBehavior.EsDecimal="True" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoUnidad" Grid.Row="0" Grid.Column="2" Etiqueta="Unidad de medida">
                                <ComboBox ItemsSource="{Binding UnidadesMedida}"
                                          SelectedItem="{Binding UnidadMedidaSeleccionada}"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:UnidadMedida">
                                            <TextBlock Text="{Binding Nombre}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoCategoria" Grid.Row="2" Grid.Column="0" Etiqueta="Categoría">
                                <ComboBox ItemsSource="{Binding Categorias}"
                                          SelectedItem="{Binding CategoriaSeleccionada}"
                                          PlaceholderText="Sin categoría"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:Categoria">
                                            <TextBlock Text="{Binding Nombre}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                        </Grid>

                        <v2:Alerta x:Name="AlertaError"
                                   Margin="0,16,0,0"
                                   Variante="Error"
                                   Texto="{Binding MensajeError}"
                                   IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                        <Border x:Name="PieFormulario"
                                Margin="0,24,0,0"
                                Padding="0,16,0,0"
                                BorderThickness="0,1,0,0"
                                BorderBrush="{DynamicResource V2SeparadorGrillaBrush}">
                            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                                <Button x:Name="BotonCancelar" Classes="secundario" Content="Cancelar" Command="{Binding CancelarCommand}" />
                                <Button x:Name="BotonGuardar" Classes="primario" Content="Guardar" Command="{Binding GuardarCommand}" />
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </v2:TarjetaContenido>
            </Grid>
        </StackPanel>
    </ScrollViewer>

</UserControl>
```

(Los `TextBlock` de las plantillas de los combos heredan tamaño y color del `ComboBox` v2; no llevan clase. `V2AnchoFormularioMax` es un `x:Double` estático de `Tokens.axaml`: si `MaxWidth` de `ColumnDefinition` no acepta el `StaticResource`, usar `760` literal con el comentario "V2AnchoFormularioMax" y anotarlo.)

Run: `dotnet build StockApp.sln` → 0 errores.

- [ ] **Step 4: Tests heredados**

1. `ProductoFormPrecioCostoFormatoTests.cs`: `using StockApp.Presentation.Controls;` → `using StockApp.Presentation.Controls.V2;` (L10). La búsqueda `OfType<CampoFormulario>().Single(c => c.Etiqueta == "Precio costo")` (L38) no cambia de texto: ahora resuelve el tipo v2.
2. `GuardarBloqueadoConErroresDeEntradaTests.cs`, `CajaDeCampo` (L61-63), reemplazar por:

```csharp
    // CampoFormulario viejo (Entrada/Salida, todavía en el tema viejo) o v2 (Productos): mientras convivan, los dos.
    private static TextBox CajaDeCampo(Window window, string etiqueta)
        => window.GetVisualDescendants().OfType<Control>()
            .Single(c => (c as CampoFormulario)?.Etiqueta == etiqueta
                         || (c as StockApp.Presentation.Controls.V2.CampoFormulario)?.Etiqueta == etiqueta)
            .GetVisualDescendants().OfType<TextBox>().First();
```

3. `CatalogoFormAnchoYCancelarTests.cs`, `BotonCancelar` (L140-142): aceptar `secondary` o `secundario`:

```csharp
    // "secondary" en los formularios del tema viejo, "secundario" en los v2 (ProductoFormView).
    private static Button? BotonCancelar(Control vista)
        => vista.GetVisualDescendants().OfType<Button>()
            .FirstOrDefault(b => (b.Classes.Contains("secondary") || b.Classes.Contains("secundario"))
                                 && (b.Content as string) == "Cancelar");
```

(y el texto del error de L200: `Classes=\"secondary\" (o \"secundario\")`).
4. `GuardianDePatronTests.cs`: borrar `[InlineData(typeof(ProductoFormView), null, null)]` (L54); sacar `typeof(ProductoFormView),` de `VistasDeLaTanda` (L113); agregar `typeof(ProductoFormView),` a `VistasMigradasAV2`. Actualizar el comentario de L41-47 que nombra `ProductoFormView` como caso P3 (queda solo `MovimientoFormControl`).

- [ ] **Step 5: Comparaciones (rojo → verde)**

En `ProductosComparacionTests.cs`, agregar:

```csharp
    [AvaloniaFact]
    public void ProductoNuevo_IgualAlMockup() => Comparar(DatosProductos.Nuevo(), "inventario/producto-nuevo.html");

    [AvaloniaFact]
    public void ProductoNuevo_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Nuevo(), "inventario/producto-nuevo.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoEditar_IgualAlMockup() => Comparar(DatosProductos.Editar(), "inventario/producto-editar.html");

    [AvaloniaFact]
    public void ProductoEditar_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.Editar(), "inventario/producto-editar.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoError_IgualAlMockup() => Comparar(DatosProductos.ConError(), "inventario/producto-error.html");

    [AvaloniaFact]
    public void ProductoError_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.ConError(), "inventario/producto-error.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void ProductoSinPermiso_IgualAlMockup() => Comparar(DatosProductos.SinPermiso(), "inventario/producto-sin-permiso.html");

    [AvaloniaFact]
    public void ProductoSinPermiso_IgualAlMockup_1366x768() =>
        Comparar(DatosProductos.SinPermiso(), "inventario/producto-sin-permiso.html", TamanoLienzo.Municipio);
```

Run: `dotnet build StockApp.sln`, `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`, `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj` y `scripts/ui-v2/comparar.sh Producto` (los 12 tests de la clase, listado incluido).
Expected: UiTests y VM en verde (incluidos `ProductoFormViewV2Tests`, `ProductoFormPrecioCostoFormatoTests`, `GuardarBloqueadoConErroresDeEntradaTests` — Productos y Entrada/Salida —, `CatalogoFormAnchoYCancelarTests`, `GuardianDePatronTests`, `ComandosDeGuardadoGuardianTests`, `CargaProtegidaSinPermisoGuardianTests`); las 8 comparaciones del formulario APROBADAS con lo calibrado y sin recortes, o el Step 6. Causas esperables si no pasan: el combo (glifo con `margin-right: -3px` de §6.5 ya resuelto), el `TextBox` deshabilitado (Step 2), un medio píxel por alto impar de la card (compensación entera en §6.5, como el Login), el pulgar de scroll si el contenido no entra en 760 (replicarlo en el mockup y volver al usuario).

- [ ] **Step 6: Umbral (solo si, con la geometría ya idéntica, las teselas densas pasan de 11) — GATE**

Mismo procedimiento que la Task 4 Step 8 (sección "Herramientas de diagnóstico": `teselas.py`, `tinta.py`, verificación de estilo), por pantalla y tamaño. Mutación pálida por pantalla, cuando el umbral propuesto sea 16 o más:
- `producto-error`: QUITAR `Variante="Error"` de `AlertaError` (pasa a info).
- `producto-nuevo`, `producto-editar`, `producto-sin-permiso`: no tienen relleno pálido de color propio. Si una de ellas necesitara 16 o más, PARAR y presentárselo al usuario: `MutacionPalidaEnRojo` no se puede registrar honestamente y no se inventa una.
PARAR, presentar CADA tesela, y solo con OK explícito registrar los `UmbralAprobado` (constantes con números medidos, como en la Task 4) en `ProductosComparacionTests`.

- [ ] **Step 7: Mutación quitando**

```bash
cp src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml "$SCRATCH/ProductoFormView.axaml.bak"
cp src/StockApp.Presentation/Themes/V2/ControlesV2.axaml "$SCRATCH/ControlesV2.axaml.bak"
```

(a) Quitar `BreadcrumbComando="{Binding CancelarCommand}"` → rojo en `Breadcrumb_ClickReal_VuelveAlListadoSinGuardar` y `Raiz_TieneUi2_YBreadcrumbConElTituloDelVm`.
(b) Quitar `IsVisible="{Binding !SinPermiso}"` de `ColumnaFormulario` → rojo en `SinPermiso_MuestraElEstadoEnLugarDeLaTarjeta` y `ProductoSinPermiso_IgualAlMockup`.
(c) Quitar `Grid.ColumnSpan="3"` de `CampoNombre` → rojo en `Campos_EnDosColumnasDondeCorresponde` y `ProductoNuevo_IgualAlMockup`.
(d) Quitar `MaxWidth="{StaticResource V2AnchoFormularioMax}"` de la columna → rojo en `Tarjeta_Mide760YQuedaALaIzquierda` y en las comparaciones del formulario.
(e) Quitar `IsEnabled="{Binding !EsEdicion}"` del SKU → rojo en `Edicion_SkuDeshabilitadoConTokensDeDeshabilitado` y `ProductoEditar_IgualAlMockup`.
(f) En `ControlesV2.axaml`, quitar el estilo `.ui2 TextBox:disabled /template/ Border#PART_BorderElement` → rojo en `Edicion_SkuDeshabilitadoConTokensDeDeshabilitado` (y anotar si `ProductoEditar_IgualAlMockup` también lo ve).
(g) Quitar `Variante="Error"` de `AlertaError` → rojo en `Guardar_ConErrorDelServicio_MuestraLaAlertaYNoNavega` y `ProductoError_IgualAlMockup`.
(h) Quitar `Classes="ui2"` de la raíz → rojo en `Raiz_TieneUi2_…` y en las 8 comparaciones del formulario.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Themes/V2/ControlesV2.axaml src/StockApp.Presentation/Views/Catalogo/ProductoFormView.axaml tests/StockApp.Presentation.UiTests/V2/ProductoFormViewV2Tests.cs tests/StockApp.Presentation.UiTests/ProductoFormPrecioCostoFormatoTests.cs tests/StockApp.Presentation.UiTests/GuardarBloqueadoConErroresDeEntradaTests.cs tests/StockApp.Presentation.UiTests/CatalogoFormAnchoYCancelarTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/ProductosComparacionTests.cs
git commit -m "feat(ui-v2): formulario de Productos v2 con secciones, breadcrumb, error y sin permiso comparados"
```

## Bloque D — Cierre de la fase

### Task 6: Suite completa, comparación de toda la UI v2, verificación orgánica, aprobación final, herramientas y spec

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 10 a 14)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, §6.5 si hubo compensaciones)
- Modify (local, no se commitea): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 3a cerrada en `feat/ui-v2` (sin merge a `main`).

- [ ] **Step 1: Build y suite completa**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
```

Expected: 0 errores; todo verde salvo los falsos conocidos de fixtures `.ods` en `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` (confirmar por nombre). Anotar el total en el ledger.

- [ ] **Step 2: Herramientas**

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
```

Expected: exit 0 (esta fase no cambia `tokens.json`).

- [ ] **Step 3: Comparación de toda la UI v2**

```bash
scripts/ui-v2/comparar.sh
```

Expected: TODAS las filas de `artifacts/ui-v2/comparacion/index.html` APROBADAS a los dos tamaños: las de las Fases 0 a 2 sin cambios (`DataGridV2.axaml` y `ControlesV2.axaml` cambiaron: ninguna pantalla anterior tiene celdas `inactivo`/`negativo` ni `TextBox` deshabilitado, así que no deberían moverse; si una se mueve, diagnosticarla) y las 6 pantallas de Productos (12 filas), con `UmbralAprobado` solo donde el usuario lo aprobó.

- [ ] **Step 4: Verificación orgánica (spec §6.4) — app real contra la base**

Preparación:
- `docker start stockapp-pg` (y dejarlo andando).
- `~/.config/GestionMunicipal/conexion.json` apuntando a `localhost`.
- Credencial de desarrollo: `admin` / `Admin1234`.
- Un Operador de permisos MIXTOS (Administración → Usuarios, crear o reutilizar): `GestionarProductos` y `RegistrarMovimientos`, sin `VerFinanzas` ni `VerReportes`.
- WSLg levantado; herramientas de `scripts/gui-verificacion/` (xdotool sin root; capturas con PowerShell; la ventana se llama "Gestión Municipal"). `capturar.sh` NO es fail-closed: MIRAR cada captura (si muestra el escritorio de Windows, no vale). Popups (combos) con teclado y `Escape` antes de capturar.

Todo con la ventana a **1366×768**; los pasos 1 y 2 se repiten al tamaño por defecto. Capturar cada pantalla.

1. Administrador → Productos: grilla con los productos reales, headers en mayúsculas, pie "N productos". Un inactivo (si no hay, dar de baja uno de prueba en el paso 4) en gris con "Inactiva" legible; un stock negativo (si existe) con la insignia y el número rojo oscuro. Ordenar por COSTO (dos clicks) y por STOCK: números alineados y sin cortar.
2. Buscar algo que no exista: estado vacío; borrar la búsqueda: vuelve la grilla. Seleccionar un activo: "Editar" y "Eliminar" se habilitan; seleccionar un inactivo: se deshabilitan.
3. "Nuevo": breadcrumb "Productos / Nuevo producto", card de 760 a la izquierda, SKU y Nombre con asterisco, "Unidad" preseleccionada. Escribir "5.4" en Precio costo: error del campo y "Guardar" bloqueado con tooltip; corregir a "5,4". Crear un producto de prueba ("PRUEBA UI V2 3a") y guardar: vuelve al listado y aparece.
4. Editarlo: "Editar producto", SKU gris no editable, datos precargados (unidad y categoría elegidas). Cambiar el nombre y guardar. Volver a entrar y salir con el breadcrumb "Productos": no guarda nada. Darlo de baja con "Eliminar" (confirmación): queda inactivo en gris.
5. Crear otro con el SKU de uno existente: alerta de error con el texto del servidor; no navega.
6. Operador mixto: ve "Productos" en el sidebar y el acceso en Inicio; repetir 3 y 4 con él (puede crear, editar y dar de baja). Un usuario SIN `GestionarProductos` no ve "Productos" (ni en el sidebar ni en Inicio).
7. Al terminar, dar de baja los productos de prueba (la base local queda limpia de "PRUEBA UI V2 3a" activos).

Si algún paso no se puede hacer por el entorno (WSLg caído, contenedor que no arranca), PARAR y avisar al usuario con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada: la verificación orgánica la hace el usuario.

- [ ] **Step 5: Aprobación final del usuario**

PARAR. Presentar: el `index.html` de comparación (las 12 filas de Productos), las capturas de la verificación orgánica junto a los mockups y la lista de cambios respecto de los mockups aprobados (si hubo, por la convención 14 o por compensaciones). La aprobación final es del usuario mirando la app (spec §6.4). Registrar en el ledger: `Fase 3a: aprobada por <quién> <fecha>`.

- [ ] **Step 6: `herramientas.md` y spec**

`docs/ui-v2/herramientas.md`, sección "Convenciones de la convivencia (hasta la Fase 9)": agregar las convenciones 10 a 14 (texto de "Convenciones que heredan las fases siguientes", con las D# como quedaron aprobadas) y, en "Test por pantalla", el ejemplo de `ProductosComparacionTests.Comparar` (shell real, solo el sidebar oculto).

Spec:
1. Línea `**Estado:**`: `**Estado:** aprobado por el usuario; Fases 0, 1, 2 y 3a implementadas en feat/ui-v2; Fase 1 APROBADA el 2026-10-05; Fase 2 APROBADA el 2026-10-06; Fase 3a (Productos) APROBADA el <fecha> (merge a main al final de todas las fases)`.
2. §7, después del párrafo "Estado de la Fase 2": "**Fase 3 partida en 3a, 3b y 3c (decisión del usuario, 2026-10-06):** 3a Productos (listado y formulario), 3b Entrada/Salida + `MovimientoFormControl`, 3c Ingreso por factura + Historial de movimientos; cada una con su ciclo completo. El patrón de modal v2 entra en 3c." y "**Estado de la Fase 3a (aprobada el <fecha>).** Listado y formulario de Productos v2, comparados en 6 estados a los dos tamaños; inactivos por token sin `Opacity` y stock negativo en `#B91C1C`; verificación orgánica a 1366×768 con Administrador y operador mixto. Decisiones del usuario: <lista de D# aprobadas o cambiadas>."
3. Deuda abierta: agregar "- `GrillasSinRecorteBarridoTests` mide con el sidebar viejo de 232 (`SidebarPx`); el v2 mide 240 (8 px menos para la vista)." y "- `SignoNegativoBrushConverter` y `ActivoOpacidadConverter` siguen en las vistas del tema viejo (se borran en la Fase 9 cuando no quede ninguna)." Si en esta fase se registró algún `UmbralAprobado`, anotarlo con su pantalla y número.
4. §6.5: cada compensación nueva medida en las Tasks 4 o 5 (si hubo), con su antes y después.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 3a (Productos)"
```

No se pushea ni se mergea.

---

## Fuera de alcance

- **3b y 3c (U1):** `MovimientoFormControl`, `EntradaRegistroView`, `SalidaRegistroView`, `IngresoPorFacturaView`, `MovimientoHistorialView` y el patrón de modal v2 (U6).
- **Deuda que U5 deja afuera:** `capturar.sh` fail-closed (sigue abierta en la spec); un guardián genérico de `Opacity` literal (ya existe `VistaV2_NoTieneOpacidadesLiterales` sobre `VistasMigradasAV2`, que pasa a cubrir estas dos vistas sin sus plantillas de celda; no se amplía); una task separada de preparación de `DataGridV2`.
- **ViewModels:** ninguno cambia. Quedan como están: `CargarAsync` del listado que traga el 403 (D10), `EjecutarBusquedaAsync` sin protección contra `UnauthorizedAccessException` (preexistente), la navegación dentro de `Task.Run` de `Nuevo`/`Editar`.
- **Converters del tema viejo:** `SignoNegativoBrushConverter` (`#DC2626`) y `ActivoOpacidadConverter` no se tocan ni se borran (los usan otras vistas; Fase 9).
- Stock bajo / `StockMinimo`, modo oscuro, back-stack, cambios de API (spec §10).
- `GrillasSinRecorteBarridoTests.SidebarPx` = 232 (deuda anotada en la spec, Task 6).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| La grilla densa supera las 11 teselas solo por tono (12 filas × 7 columnas de texto 13/400), como Inicio (hasta 46). | U4: diagnóstico con `teselas.py`/`tinta.py`, cada tesela al usuario, `UmbralAprobado` con su OK y mutación pálida en ESA pantalla (Task 4 Step 8). Nunca un número suelto. |
| Una pantalla de formulario sin relleno pálido necesita 16 o más teselas: `MutacionPalidaEnRojo` no se puede registrar. | Task 5 Step 6: PARAR y presentárselo al usuario; no se inventa la mutación. |
| Los anchos `Auto` de la app no coinciden con los del mockup (medidos sobre una grilla de texto, no la de plantillas). | Task 1 Step 4 los mide con el tema v2; convención 14 para re-medir tras el XAML (≤ 4 px se anota, más vuelve al usuario). |
| El `DataGrid` selecciona sola la primera fila al cargar y el render no coincide con el mockup "sin selección" (y habilita Editar/Eliminar). | `EditarYEliminar_SinSeleccion_Deshabilitados` lo detecta en la View real; si pasa, se corrige en el XAML sin tocar el VM (por ejemplo `SelectedIndex="-1"` tras la carga no sirve: diagnosticar y reportar antes de elegir). |
| A igual especificidad, el orden de `.inactivo` y `.negativo` decide el color de un inactivo con stock negativo; reordenar los estilos lo rompe en silencio. | `InactivoConStockNegativo_ElNumeroSigueEnRojo` + mutación (c) de la Task 4. |
| `.ui2 DataGridCell.num TextBlock` (alinea a la derecha) también alcanza el `TextBlock` de la plantilla de la `Insignia` de la celda de Stock. | La insignia mide lo que pide su texto: alinear su `TextBlock` interno no la mueve. `AssertBadgeNoPisaElNumero` y la comparación lo verifican; si se corre, la celda de Stock deja `CellStyleClasses="num"` y pone `FontFeatures="+tnum"` local. |
| Los tests heredados que buscan tipos viejos fallan en vistas que siguen viejas si se adaptan mal. | Helpers que aceptan los dos tipos (convención 13); la suite completa de la Task 6 corre los de Historial, Valorización, Stock por categoría y Entrada/Salida. |
| `ColumnDefinition.MaxWidth` con `StaticResource` o el reparto de la columna `*` no dan 760. | `Tarjeta_Mide760YQuedaALaIzquierda`; alternativa `760` literal comentada (Task 5 Step 3). |
| El formulario con la alerta de error no entra en 760 de alto y aparece un pulgar de scroll que el mockup no tiene. | Task 3 Step 4 lo revisa al capturar; Task 5 Step 5 indica replicarlo en el mockup y volver al usuario. |
| Durante el EstadoVacio inicial (antes de que `CargarAsync` termine) la pantalla muestra "No hay productos" un instante. | Aceptado (la carga es una llamada HTTP corta); se ve en la verificación orgánica; si molesta, es un cambio de VM fuera de esta fase. |
| WSLg caído o `capturar.sh` devolviendo el escritorio de Windows con exit 0. | Mirar cada captura; Task 6 Step 4 PARA con evidencia y la verificación la hace el usuario. |
| `.axaml` stale en `obj/` durante las mutaciones. | Global Constraints: `dotnet build-server shutdown` + `rm -rf` de `obj/bin`. |
| Una mutación que no se aplica (texto no encontrado) deja verde en silencio. | `git diff --no-index` contra el backup ANTES de correr cada mutación. |
