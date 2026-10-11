# Fase 3c-3 — Ingreso por factura y patrón de modal v2 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar "Ingreso por factura" a la UI v2 (formulario de tres secciones con pie fijo, Enter en la fila de carga y un solo primario) y reemplazar sus dos overlays por diálogos `Window` v2 servidos por servicios, sin cambios funcionales, comparado contra mockups aprobados a 1280×760 y 1366×768.

**Architecture:** Cuatro piezas. (a) Dos diálogos `Window` sin bindings (`AltaProductoRapidaDialog`, `ConfirmacionPreciosDialog`) con su servicio, del mecanismo de `RecalcularStockDialogService` (convención 25). (b) `IngresoPorFacturaViewModel` pierde los flags de los overlays y espera a esos servicios. (c) Un comportamiento adjunto `EnterEjecutaComando` (Enter en la fila de carga ejecuta "Agregar artículo", en túnel, sin `IsDefault` en la pantalla) y otro `TraerALaVista` (la card de adjuntos aparece a la vista tras guardar). (d) `IngresoPorFacturaView` reescrita en v2 con el pie FUERA del scroll. Los mockups de la pantalla y de los dos diálogos se aprueban en un GATE (Bloque B) antes de todo XAML de diálogo o de pantalla (Bloque C).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Avalonia.Controls.DataGrid 12.0.1, Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md`, sección "Diseño de la Fase 3c-3" (L394-500: secciones 1, 2 y 3 y los desvíos), §3 (la excepción a "VMs intactos" del patrón de modal, L106), §5.2 formulario, §5.5 diálogos, §6 fidelidad y §6.5 compensaciones, §7 "Deuda abierta tras la Fase 3c-2" (L512-529). Convenciones 1 a 25: `docs/ui-v2/herramientas.md` (en especial 12, 13, 15, 16, 17, 18, 19, 21, 24 y 25). Molde de formato y de receta: `docs/superpowers/plans/2026-10-09-ui-v2-fase-3c2-historial.md` (Task 4: medición y GATE; Task 6: diálogo v2 con servicio; Task 7: vista, comparaciones y mutaciones; Task 8: cierre) y `docs/superpowers/plans/2026-10-07-ui-v2-fase-3b-movimientos.md` (Task 1: datos fijos de un formulario; Task 3: formulario v2).

Todos los comandos se corren desde la raíz del worktree, con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <comando>` (rama `feat/ui-v2`, base: el commit de este plan). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Si CodeGraph devuelve un archivo como "changed on disk" sin fuente (al escribir este plan pasó con `IngresoPorFacturaViewModel.cs`, `AdjuntosPanelViewModel.cs` y varios VMs de Finanzas), leerlo con Read y decirlo en el reporte.

## Decisiones del usuario (2026-10-11, vinculantes)

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Todo modal v2 es una `Window` con servicio; prohibidos los overlays en pantalla** (convención 25, ya escrita en `herramientas.md`). Los dos overlays de Ingreso por factura pasan a `AltaProductoRapidaDialog` (`IAltaProductoRapidaDialogService`, devuelve `ProductoNuevoDto?`) y `ConfirmacionPreciosDialog` (`IConfirmacionPreciosDialogService`, devuelve los tildes o null). | Tasks 4 y 5 (diálogos y servicios), Task 6 (VM sin flags), Task 9 (guardián). Excepción a "VMs intactos" ya asentada en §3. | 4, 5, 6, 9 |
| U2 | **"Agregar artículo" es SECUNDARIO; Enter en la fila de carga lo dispara** (Cantidad, Precio, y Producto cuando tiene valor). Un solo primario en la pantalla: Guardar. Enter nunca dispara Guardar ni envía el formulario. | `EnterEjecutaComando` (Task 2), ningún `IsDefault` en la vista (Task 8), test de "un solo primario" sobre la vista entera (Task 8). | 2, 8 |
| U3 | **Alcance SOLO visual y patrón de modal; sin cambios funcionales.** Validaciones, `PuedeGuardar`, navegación (Cancelar y Finalizar van a Gastos y facturas), carga por `DataContextChanged` y gate del sidebar no cambian. | Global Constraints; todo hallazgo funcional va a la deuda (Task 10), salvo que el usuario decida otra cosa en el GATE (H1). | todas |
| U4 | **Lo que la spec no fija lo propone el mockup y lo decide el usuario en el GATE** (criterio de 3a a 3c-2). | D1 a D16 abajo. Si el usuario cambia una, se ajustan mockups y los tests que la fijan en la Task 3, antes de las Tasks 4 a 8. | 3 |

**Orden de los bloques.** El usuario sugirió servicios, diálogos y VM en el Bloque A. Los mockups de los diálogos van al GATE ANTES de su XAML, y el VM no puede perder los overlays sin que los servicios reales estén registrados en DI (la pantalla dejaría de abrir entre commits). Por eso: el Bloque A solo tiene lo que no depende del GATE (datos fijos y `EnterEjecutaComando`), el Bloque B es el GATE de los cinco mockups, y el Bloque C hace diálogos, servicios, VM y pantalla en ese orden; cada commit deja la app usable.

## Decisiones que se confirman al aprobar los mockups (Task 3)

- **D1 — Encabezado:** `EncabezadoPagina` sin breadcrumb (hereda D1 de la 3b: pantalla del sidebar), `Titulo="Ingreso por factura"` (el texto del ítem del sidebar; hoy dice "Ingreso de stock por factura" con eyebrow "MOVIMIENTOS") y `Subtitulo="Cargá la factura del proveedor y los artículos que entran al stock."`. Sin acciones en el encabezado.
- **D2 — Una sola `TarjetaContenido` a todo el ancho útil** (1040 − 56 = 984 a 1280; 1070 a 1366), padding 24, con las tres secciones de la spec separadas por el separador de la convención 12 (`Border Height="1" Margin="0,20"` en `V2SeparadorGrillaBrush`) y cada una con su `subtitulo-formulario` ("Datos de la factura", "Cargar artículo", "Artículos cargados"). No usa la card de 760 de la convención 12: la fila de carga y la grilla no entran. Alternativa: tres cards separadas.
- **D3 — "Datos de la factura"** en `Grid ColumnDefinitions="*,16,*,16,*"`, filas con 16 de separación en el `Margin` superior (convención 15): Proveedor* | N.º factura | Orden de compra; Fecha | Detalle* | Destino; Fuente de financiamiento* | Rubro de gasto* | Línea POA; "Compra a crédito" (CheckBox) | Fecha de vencimiento (solo con crédito). Asterisco (`Requerido="True"`) SOLO en lo que exige `PuedeGuardar` (Proveedor, Detalle, Fuente, Rubro); los opcionales van sin asterisco y con los placeholders de hoy ("Ej.: A-0001234", "Ej.: OC-77", "Ej.: Corralón municipal", "Sin línea POA"). Fecha sin asterisco: arranca en hoy y el VM no la exige.
- **D4 — "Cargar artículo" en una fila:** Producto* (`*`), Cantidad* (120), Precio unitario (140, sin asterisco: 0 es válido), "Actualizar precio costo", "Producto nuevo" y "Agregar artículo" (los dos `secundario`, alineados abajo con los controles). **Regla de medición:** si a 1280 la columna Producto queda por debajo de 240 (se mide en la Task 3 Step 1), el tilde y "Producto nuevo" bajan a una segunda línea alineada a la izquierda y se presenta en el GATE como desvío de la sección 2 de la spec. En modo producto nuevo, el combo se reemplaza por el nombre y un botón `ghost` "Cambiar" (comportamiento de hoy).
- **D5 — Error de carga:** `v2:Alerta` Error debajo de la fila, con 8 de margen superior (variante compactada de la convención 12).
- **D6 — "Artículos cargados":** grilla v2 de solo lectura, `CanUserSortColumns="False"`, columnas PRODUCTO (`*`, piso 160), CANTIDAD (`Auto` medida, `CantidadConverter`), PRECIO UNITARIO y SUBTOTAL (`Auto` medidas, `MonedaConverter`: "$ 480,00"; hoy muestran "480,00" sin símbolo), ACT. COSTO (120, `v2:Insignia` neutra "Sí" solo si se actualiza) y una columna sin título con "Quitar" `ghost`. La grilla crece con los renglones (no tiene scroll propio): scrollea la página y el pie queda fijo. Vacío: `v2:EstadoVacio` "Todavía no cargaste artículos" / "Elegí un producto, completá la cantidad y el precio, y apretá Enter.".
- **D7 — Pie fijo:** banda a todo el ancho de la vista, FUERA del `ScrollViewer`, fondo superficie, borde superior `V2BordeBrush`, padding 28×16: a la izquierda Suma de renglones, Total de factura* (el `TextBox` de `MontoTotalTexto`, 160 de ancho, placeholder "0,00") y Diferencia; a la derecha Cancelar (`secundario`) y Guardar (`primario`). Etiquetas `etiqueta-campo`, valores `titulo-seccion` (15/600). Diferencia neutra (texto primario) en 0 y `advertencia` (`#92400E`, el texto de la familia advertencia de §2.1) si no; no bloquea el guardado. El error general va en una `v2:Alerta` Error ENTRE el scroll y el pie (también fuera del scroll).
- **D8 — Después de guardar:** la card se deshabilita (`IsEnabled=!GuardadoExitoso`), aparece debajo una segunda `TarjetaContenido` "Adjuntá la factura" con el texto "La factura quedó guardada. Adjuntá el PDF o la foto si corresponde." que envuelve `AdjuntosPanelView` v1 (excepción documentada hasta la Fase 4), y la página scrollea hasta ella (`TraerALaVista`): con el pie fijo y muchos renglones, quedaría fuera de la vista. El pie conserva los totales y muestra solo "Finalizar" (`primario`). Alternativa: card arriba de las secciones.
- **D9 — Diálogo de alta (`dialogos/alta-producto.html`):** `Window` de 440, título "Producto nuevo", cuerpo `cuerpo` "El producto se da de alta junto con la factura, al guardar."; Código* y Nombre* a todo el ancho (placeholders "Ej.: MAT-0601" y "Ej.: Carretilla de obra 90 L"); Categoría ("Sin categoría") | Unidad de medida* ("Elegí la unidad") en `*,16,*`; `v2:Alerta` Error oculta; botones "Cancelar" (`secundario`, `IsCancel`) y "Confirmar" (`primario`, `IsDefault`; el texto de hoy). Foco inicial en Código. La alerta nombra lo que falta, en el orden de los campos: "Falta completar: Código, Nombre y Unidad de medida." (uno: "Falta completar: Nombre."; dos: "… Nombre y Unidad de medida.").
- **D10 — Diálogo de precios (`dialogos/confirmacion-precios.html`):** `Window` de 520, título "Cambios de precio de costo", cuerpo "Estos productos se cargaron con un precio distinto del costo actual. Tildá los que querés actualizar."; lista en `Grid "*,16,Auto,16,Auto"` con encabezados `etiqueta-campo` "Producto", "Precio actual", "Precio nuevo" y por fila un `CheckBox` cuyo contenido es el nombre (con ajuste de línea) y los dos precios en es-UY a la derecha; la lista va en un `ScrollViewer` de alto máximo 320; botones "Cancelar" (`secundario`, `IsCancel`) y "Confirmar y guardar" (`primario`, `IsDefault`; el texto de hoy). Los tildes arrancan como vienen (`Confirmado`, hoy siempre false). Foco inicial en "Confirmar y guardar". Enter con el foco en un tilde confirma (no lo cambia); Espacio lo cambia.
- **D11 — Foco después del alta confirmada: Cantidad** (pulso `SolicitarFocoEnCantidad` con `FocoBehavior`, como `SolicitarFocoEnProductoCombo`): se sigue con el teclado (cantidad, precio, Enter). Tras cancelar el alta o la confirmación, lo nativo (vuelve al botón que abrió el diálogo). Alternativa: lo nativo en los dos casos.
- **D12 — Estados a mockear (a 1280×760 y 1366×768):** `inventario/ingreso-factura.html` (vacío: sin cabecera salvo la fecha, sin renglones), `ingreso-factura-renglones.html` (cabecera completa y 6 renglones, diferencia 0), `ingreso-factura-diferencia.html` (compra a crédito con vencimiento, total que no coincide, diferencia en advertencia y error de carga a la vista), `ingreso-factura-guardado.html` (card de adjuntos y pie con solo Finalizar); `dialogos/alta-producto.html`, `dialogos/alta-producto-faltantes.html` (solo el código: "Falta completar: Nombre y Unidad de medida.") y `dialogos/confirmacion-precios.html` (3 cambios, el primero tildado). No se mockean desplegables ni tooltips.
- **D13 — Datos fijos** (`DatosIngresoFactura`, Task 1): proveedor "Barraca Carmelo S.A.", factura "A-0004521", orden "OC-2026-118", fecha 06/10/2026, detalle "Materiales para la reparación de la rambla", destino "Corralón municipal", fuente "Fondo de Desarrollo del Interior", rubro "Materiales de construcción", línea POA "Mantenimiento de espacios públicos"; renglones al precio de costo de `DatosProductos` (sin cambio de precio): Cemento portland 50 kg 40 × 480 (actualiza costo), Arena gruesa 6 × 890, Caño PVC 110 mm 12 × 650, Cable 2x1,5 mm 100 × 38, Guantes de trabajo 20 × 95 y el producto nuevo "Carretilla de obra 90 L" (MAT-0601, Herramientas, Unidad) 2 × 3.450: suma $ 44.940,00. Diferencia: total "45.000,00" → "$ 60,00", crédito con vencimiento 05/11/2026, error de carga "La cantidad debe ser mayor a cero.".
- **D14 — La interna v1 de la card de adjuntos no se compara:** `AdjuntosPanelView` v1 se oculta en los dos renders del estado guardado (app: `Opacity = 0` en `prepararApp`, que conserva el layout; mockup: un bloque `.panel-adjuntos-v1` con el alto medido en la Task 3 y `visibility: hidden`). Lo custodian la verificación orgánica y sus tests v1.
- **D15 — Hallazgo H1 (preexistente, funcional):** el tilde "Actualizar precio costo" de la fila de carga se pisa en la confirmación: la lista arranca destildada (`ItemConfirmacionPrecioVm.Confirmado` en false) y al confirmar `Fila.ActualizarPrecioCosto = tilde`. Con U3, **se conserva** y va a la deuda. Alternativa (cambio funcional, solo con OK del usuario): que cada tilde arranque con el `ActualizarPrecioCosto` de su renglón.
- **D16 — `GuardianDePatronTests` L48:** la fila `[InlineData(typeof(IngresoPorFacturaView), "Ingreso de stock por factura", "MOVIMIENTOS")]` custodia el `HeaderVista` v1; la vista v2 no lo tiene. Sale, junto con `VistasDeLaTanda` L101, y la vista entra a `VistasMigradasAV2`; el título nuevo y la ausencia de eyebrow los custodia `Raiz_TieneUi2_EncabezadoSinBreadcrumb_YTresSecciones` (convención 13: se reemplaza por un test que custodia lo mismo sobre la View v2).

## Global Constraints

- Tema claro fijo; tokens de §2.1 sin valores nuevos: fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, borde input `#D6D3D1`, separador `#F0EFED`, acento `#1D4ED8`, texto primario `#1C1917`, texto etiqueta `#44403C`, advertencia texto `#92400E`, borde error `#DC2626`. Alturas: input y botón 36, fila de grilla 40, header de grilla 36. Espaciado múltiplo de 4. Sidebar 240: la vista mide 1040 a 1280 y 1126 a 1366.
- **`tokens.json` es la fuente de `Tokens.axaml`; nunca editar `Tokens.axaml` a mano.** Esta fase no prevé tokens nuevos. Toda task que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`.
- **Formato es-UY** (decisión del usuario, 2026-10-01): importes con `FormatoEsUy.Moneda` / `MonedaConverter` ("$ 1.500,50"), cantidades con `CantidadConverter` / `FormatoEsUy.Cantidad` ("1.500,5"), entrada con `DecimalConverter` + `EntradaNumericaBehavior.EsDecimal` (coma decimal; "5.4" se rechaza y no se lee como miles). Los tests que miran textos corren también con la cultura ambiente en `en-US`.
- **`x:DataType` / compiled bindings:** un binding mal escrito es error de build, no un null silencioso; cada `DataTemplate` lleva su `x:DataType` (`vm:FilaRenglonFacturaVm`, `cat:ProductoDto`, `ent:Proveedor`, `ent:FuenteFinanciamiento`, `ent:RubroGasto`, `ent:LineaPoa`, `ent:Categoria`, `ent:UnidadMedida`).
- **Sin comentarios con `--` en `.axaml` ni `.csproj`** (AVLN1001, cascadea a MSB4025).
- **Diálogos sin bindings** (§5.5, convención 24): acceso por `x:Name` en code-behind; el `ItemTemplate` de un `ComboBox` con `{Binding Nombre}` sobre su ítem es lo único admitido (como `RecalcularStockDialog`).
- **Convención 25:** `Window` con `WindowStartupLocation="CenterOwner"`, `CanResize="False"`, `ShowInTaskbar="False"`, `SizeToContent="Height"`, contenido en `Border x:Name="Contenido" Classes="ui2"` con padding 24, Escape cancela y Enter confirma (`IsCancel` / `IsDefault`), el foco queda dentro de la `Window`; servicio con `ShowDialog` sobre `MainWindow` y valor seguro (null) sin `App` o sin ventana.
- **Un solo primario por vista** (§3.3, §5.2), contado sobre la vista ENTERA y no por zona; ningún botón de `IngresoPorFacturaView` lleva `IsDefault` (Enter nunca guarda).
- **VM: solo lo de U1 y D11.** `IngresoPorFacturaViewModel` pierde `MostrandoAltaProducto`, `MostrandoConfirmacionPrecios`, `GuardarEsAccionPrincipal`, `_montoConfirmadoPendiente`, `ConfirmarAltaProductoCommand`, `CancelarAltaProductoCommand`, `ConfirmarPreciosYGuardarCommand` y `CancelarConfirmacionPreciosCommand`; gana dos parámetros de constructor y `SolicitarFocoEnCantidad`. Ningún otro VM. No cambian `ViewLocator`, `INavigationService`, `ErrorConverter`, la lógica de permisos ni el code-behind de la vista (`IngresoPorFacturaView.axaml.cs` sigue con `DataContextChanged → InicializarAsync`). Los code-behind de los diálogos NUEVOS sí existen.
- **Gates de UI con la View real y gates de permisos con un Operador de permisos MIXTOS** (`new SesionFake(RolUsuario.Operador, …)`), nunca con Admin (cortocircuita el OR). El gate de esta pantalla es `PuedeIngresarPorFactura` (cuatro permisos: `movimientos.registrar`, `finanzas.gastos`, `finanzas.ver`, `catalogo.productos`).
- **TDD en cada task:** test primero, correrlo y PEGAR el rojo real, código mínimo, verde.
- **Mutaciones QUITANDO, nunca agregando:** `export SCRATCH=~/.cache/stockapp-ui-v2-fase-3c3 && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`); `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO lo quitado; se corre el test y se PEGA el rojo; se restaura con `cp`; `git diff --no-index` vacío; verde. **NUNCA `git checkout --` ni `git restore`.**
- **Stubs fieles al contrato real:** `ProductoNuevoDto(string Codigo, string Nombre, int? CategoriaId, int UnidadMedidaId)`, `IngresoPorFacturaResultadoDto(int GastoId, IReadOnlyList<int> MovimientoIds, decimal SumaRenglones, decimal DiferenciaConTotal)`, `AdjuntoDto(int Id, string NombreArchivo, string ContentType, long TamanoBytes, int? GastoId, int? PagoGastoId, DateTime FechaAltaUtc)`; los fakes lanzan lo que lanza `ApiErrores` (`UnauthorizedAccessException` para 401/403, `ServidorNoDisponibleException` sin servidor). Antes de escribir uno, confirmar con `src/StockApp.Application/Movimientos/IngresoPorFacturaDtos.cs` y `src/StockApp.Application/Finanzas/AdjuntoDto.cs`.
- Mockups (§6.1): `docs/ui-v2/mockups/inventario/ingreso-factura*.html` y `docs/ui-v2/mockups/dialogos/{alta-producto,alta-producto-faltantes,confirmacion-precios}.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosIngresoFactura`. Solo colores sólidos, bordes de 1 px, radios, una `BoxShadow`, Inter, px enteros, múltiplos de 4. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (`TeselasDensasPorDefecto = 11`, `PorcentajePorDefecto = 1.68`) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado(TeselasDeTono, AprobadoPor, Fecha "yyyy-MM-dd", Motivo)` tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto. Diagnóstico con `teselas.py` y `tinta.py` (plan de la 3a, "Herramientas de diagnóstico"; copiarlos a `$SCRATCH/diag/`, nunca se commitean).
- Convención 6: la pantalla se compara DENTRO del shell real (`ShellMainView`, `CurrentContent` = el VM, resuelto por `ViewLocator`, `DatosShell.Crear(usuario, permisos, "IngresoPorFactura", vm)`) con el sidebar oculto en ambos renders; los diálogos, con `ContenidoDeDialogo.Extraer`.
- Compensaciones conocidas Chromium/Avalonia (§6.5): Chromium colapsa márgenes verticales y Avalonia los suma; un `Grid` con `MaxWidth` y `UseLayoutRounding` da +1 px; texto centrado con ancho fraccionario pide `UseLayoutRounding="False"` (los `EstadoVacio` lo llevan por instancia); texto con alto de línea fijo toma `LineHeight` de un token. Una compensación nueva se mide, se anota con su antes y después y entra a §6.5 en la Task 10.
- **Build obligatorio** tras cada cambio de XAML o de un `.cs` de `Presentation`: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). **Tests con `dotnet test StockApp.sln` o con UN `.csproj` por comando, nunca varios `.csproj` juntos.** Nunca dos `dotnet test` a la vez (comparten `obj/`: rojos falsos).
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos (confirmar por nombre). Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 4", "D7", "U2" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits, la spec y `herramientas.md` sí pueden citar el plan.
- **Línea base de la suite:** se mide en la Task 1 Step 1 con `dotnet test StockApp.sln` en el commit de este plan (la última anotada en el ledger de la 3c-2 fue 5460 en `a6ed0e1`, antes de los fixes de deuda). Cada task anota en el ledger el total tras su commit; el total solo sube salvo los tests heredados que se REEMPLAZAN (convención 13), que se listan por nombre con su reemplazo.
- Las referencias de línea (`L123`) son las del commit de este plan. Después de que una task edita un archivo, ubicar el lugar por el texto citado.
- **Scripts del skill de ejecución** (`task-brief`, `review-package`): `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y verificar con `ls -l --time-style=full-iso` que el archivo quedó DENTRO del worktree y con fecha de ahora.
- Verificación GUI: ventana "Gestión Municipal" (proceso `GestionMunicipal`, que `/proc/<pid>/comm` trunca a 15 caracteres: `pgrep -x GestionMunicipa`). `capturar.sh` NO es fail-closed: mirar CADA captura y comparar `md5sum` entre capturas sucesivas (dos iguales con la pantalla cambiada = WSLg congelado). Si el usuario necesita la PC, la verificación GUI se pausa.
- **Commits:** conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). **No mergear a `main`**; prohibido push, merge, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` se mergea a `main` una sola vez, al final de todas las fases.

## Convenciones heredadas que aplican (de `herramientas.md`)

- 6 (comparación dentro del shell real), 10 y 11 (piezas de listado: `ConteoConverter` sobre `Renglones.Count`, número por token y nunca `Opacity`), 12 (formulario v2: separador, `Alerta` compactada, pie con borde), 13 (tests heredados se adaptan sin borrarse; un test se reemplaza solo por otro que custodie lo mismo sobre la View v2), 14 y 19 (columnas `Auto` medidas en headless antes del mockup; `ColumnaAuto` con el mismo conversor de la celda), 15 (campo condicional sin hueco: el vencimiento), 16 (pantalla de sidebar sin breadcrumb), 17 (diálogo con textos propios, medido sin recorte en `DialogosV2Tests`), 21 (permisos en caliente con operador mixto y `ShellMainView`), 24 y 25 (diálogo v2 con servicio; prohibidos los overlays).
- NO aplican: 18 y 23 (no es un listado con carga del servidor: los combos se cargan en `InicializarAsync`, que ya traga el 403) y 22 (el formulario no tiene alerta de carga con "Reintentar"; ver "Fuera de alcance").

## Convenciones que agrega esta fase (para `herramientas.md`, Task 10)

26. **Enter en una fila de carga:** `beh:EnterEjecutaComando.Comando="{Binding XCommand}"` en cada campo de la fila. Va en túnel (un `ComboBox` cerrado abre su desplegable con Enter y marca el evento antes que cualquier manejador de burbuja); en un `ComboBox` solo actúa con un ítem elegido y el desplegable cerrado; ejecuta solo si `CanExecute` (un campo en rojo bloquea igual que el botón) y marca el Enter como manejado. La acción frecuente que dispara es `secundario`: el único primario de la vista sigue siendo el de guardar, y ningún botón de una pantalla con fila de carga lleva `IsDefault`.
27. **Pie fijo de formulario largo:** `Grid RowDefinitions="*,Auto,Auto"`: el `ScrollViewer` con encabezado y card, la `v2:Alerta` del error general y el pie (`Border` con borde superior y fondo superficie) FUERA del scroll; lo que aparece al final del contenido tras una acción (la card de adjuntos) lleva `beh:TraerALaVista.AlMostrarse="True"`.

## Review Focus

1. **Enter en la fila de carga hace algo distinto de agregar.** Casos: Producto vacío (agregaría con error o abriría Guardar), desplegable abierto (agregaría en vez de elegir), Cantidad inválida "5.4" (agregaría con la cantidad VIEJA), Enter que se escapa a un botón por defecto (guardaría la factura entera). Esperado: Enter en Cantidad, Precio o Producto con valor agrega y devuelve el foco al combo; Enter en Producto vacío conserva lo nativo (abre el desplegable) sin agregar ni mostrar el error; con el desplegable abierto elige el ítem sin agregar; con un campo en rojo no agrega; nunca llega a Guardar. Tests: `EnterEjecutaComandoTests` (Task 2: los siete); `Enter_EnCantidadYEnPrecio_AgregaYElFocoVuelveAlProducto`, `Enter_EnProductoConValor_Agrega`, `Enter_EnProductoVacio_NoAgregaNiMuestraError`, `Enter_ConCantidadInvalida_NoAgregaConElValorViejo`, `Enter_EnLaFilaDeCarga_NuncaGuarda` (Task 8).
2. **Teclado de los diálogos.** Casos: Escape con el combo de Unidad o de Categoría abierto cierra el diálogo entero (y pierde lo tipeado); Enter con el foco en un tilde lo destilda en vez de confirmar; Enter en Código con campos faltantes cierra el alta; el foco arranca fuera del diálogo o un Tab lo saca. Esperado: el primer Escape cierra solo el desplegable y el segundo cancela; Enter con foco en un tilde confirma sin cambiarlo; Enter con faltantes muestra la alerta y no cierra; el foco arranca en Código / en "Confirmar y guardar" y Tab no sale de la `Window`. Tests: `AltaProductoRapidaDialogTests` (Task 4: `Escape_ConElComboAbierto_PrimeroCierraElDesplegable`, `Enter_ConFaltantes_NoCierraYMuestraLaAlerta`, `Abre_ConElFocoEnCodigo`, `Tab_NoSacaElFocoDelDialogo`), `ConfirmacionPreciosDialogTests` (Task 5: `Enter_ConElFocoEnUnTilde_ConfirmaSinCambiarlo`, `Abre_ConElFocoEnConfirmarYGuardar_YEnterConfirma`).
3. **Guardado doble o mezclado.** Casos: doble click en Guardar mientras la confirmación de precios está abierta (dos lotes iguales en el servidor); producto nuevo + cambio de precio en el mismo guardado (el nuevo aparece en la confirmación o pierde su alta, o el tilde cae en el renglón equivocado). Esperado: mientras espera al diálogo `GuardarCommand.CanExecute` es false y el servicio se llama UNA vez; la confirmación lista solo los productos existentes con precio distinto; el DTO lleva el renglón nuevo con su `ProductoNuevoDto` y el tilde aplicado al renglón existente que corresponde. Tests: `Guardar_MientrasLaConfirmacionEstaAbierta_NoSePuedeVolverAGuardar`, `Guardar_ProductoNuevoYCambioDePrecio_ConfirmaSoloElExistenteYElDtoLlevaLosDos` (Task 6).
4. **El servidor falla después de confirmar** (caído, 403, regla de negocio). Casos: la pantalla queda "guardada" a medias, el error queda escondido detrás del pie fijo o debajo de 50 renglones, o se pierden los renglones. Esperado: `GuardadoExitoso` false, `GastoIdCreado` null, renglones intactos, `GuardarCommand` vuelve a poder ejecutarse, y el error visible en la `Alerta` entre el scroll y el pie a 1280×760 con 50 renglones. Tests: `Guardar_ServidorCaidoTrasConfirmar_MuestraElErrorYConservaTodo`, `Guardar_403TrasConfirmar_MuestraElMensajeDeSesion` (Task 6), `ErrorDeGuardado_ConCincuentaRenglones_LaAlertaSeVeArribaDelPie` (Task 8).
5. **Pie y foco con la ventana real.** Casos: con 50 renglones a 1280×760, 1366×768 o una ventana chica el pie o Guardar quedan fuera de la vista; tras guardar, la card de adjuntos queda debajo del pliegue; tras confirmar el alta el foco no queda en Cantidad (y el siguiente Enter no hace nada). WSLg pierde el foco de la ventana principal tras un modal (artefacto conocido del toolkit, deuda de la 3b): un bug REAL lo muestra el UiTest headless; si el headless está verde y en WSLg el teclado no responde, se recupera con `xdotool windowminimize` + `windowmap` y, si después del recupero el foco está en Cantidad, es artefacto. Tests: `Pie_ConCincuentaRenglones_QuedaFijoAbajoYGuardarSeVe` (Theory 1040×760, 1126×768 y 784×600), `Guardado_LaCardDeAdjuntosQuedaALaVista`, `AltaConfirmada_MuestraElNombre_YElFocoVaACantidad` (Task 8), `TraerALaVistaTests` (Task 7).

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit de este plan) |
|---|---|---|
| Enter en la fila: túnel y no burbuja | `EnterEjecutaComando` se engancha con `AddHandler(KeyDownEvent, …, RoutingStrategies.Tunnel)`. `ComboBox.OnKeyDown` abre el desplegable con Enter y marca `Handled` antes que un manejador de burbuja de instancia; en túnel el comportamiento decide primero. `LoginView` usa burbuja porque solo tiene `TextBox`. | `LoginView.axaml.cs` L13-29; `LoginViewV2Tests.cs` L95-125 (un Enter sin manejar llega al `IsDefault`). |
| Enter respeta `CanExecute` | `AgregarArticuloCommand` es `ComandoDeGuardado`: con un campo en rojo su `CanExecute` es false; el comportamiento lo consulta como el botón, así no agrega con el valor viejo. | `IngresoPorFacturaViewModel.cs` L300; `ViewModelBase.cs` L58-70; test heredado `ZonaDeCarga_CantidadInvalida_ClickEnAgregar_NoAgregaConLaCantidadVieja`. |
| Enter nunca guarda | Ninguna `IsDefault` en la vista y el comportamiento marca el Enter manejado; hoy tampoco hay `IsDefault` (el v1 no tiene). | `IngresoPorFacturaView.axaml` L244-247. |
| Alta: devolver ids y no entidades | El diálogo devuelve `ProductoNuevoDto` (spec); el VM vuelve a las entidades de sus listas (`CategoriasDisponibles.FirstOrDefault(c => c.Id == …)`, `UnidadesMedidaDisponibles.First(…)`) y deja `AgregarArticulo` intacto, que lee `NuevaCategoriaSeleccionada?.Id` y `NuevaUnidadSeleccionada!.Id`. | `IngresoPorFacturaViewModel.cs` L109-112, L329-336. |
| Alta cancelada | Hoy `AbrirAltaProducto` limpia `NuevoProducto*` ANTES de abrir el overlay: cancelar estando en modo producto nuevo dejaba la zona a medias. Con el servicio, nada se toca hasta confirmar (lo pide la spec: "Alta cancelada: la carga queda intacta"). | `IngresoPorFacturaViewModel.cs` L239-247, L268-269. |
| Precios: tildes por índice | El servicio recibe `CambiosDePrecio` y devuelve `IReadOnlyList<bool>?` en el mismo orden; el VM escribe `Confirmado` y `Fila.ActualizarPrecioCosto`. El diálogo no muta el VM (sin bindings). Cancelar devuelve null y no guarda (hoy `CancelarConfirmacionPrecios` solo cierra). | `IngresoPorFacturaViewModel.cs` L385-420; `ItemConfirmacionPrecioVm.cs`. |
| Doble guardado | `GuardarCommand` es un `AsyncRelayCommand` sin ejecuciones concurrentes (`ComandoDeGuardado`): mientras `GuardarAsync` espera al diálogo, `CanExecute` es false y el botón no ejecuta. Además el `ShowDialog` deshabilita la ventana dueña. | `ViewModelBase.cs` L62-66. |
| Enter en un tilde | `Button.OnKeyDown` (base de `CheckBox`) hace click con Enter y lo marca manejado: el `IsDefault` nunca se enteraría y el tilde cambiaría. El diálogo de precios intercepta en túnel el Enter cuyo origen es un `CheckBox` y confirma. Se verifica con el rojo del Step 1 de la Task 5 (sin el manejador el tilde cambia y el diálogo no cierra). | comportamiento de `Button` de Avalonia 12; se confirma por el rojo. |
| Pie fuera del scroll | Hoy el pie es una card DENTRO del `ScrollViewer` (con 50 renglones queda abajo de todo). La spec funcional pide "un pie fijo con suma de renglones, total de factura y diferencia". | `IngresoPorFacturaView.axaml` L27, L222-247; `docs/superpowers/specs/2026-07-31-ingreso-por-factura-design.md`. |
| `ReflexionVistaViewModelTests` | `IngresoPorFacturaViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista` exige un control por cada propiedad pública del VM. `NuevoProductoCodigo`, `NuevaCategoriaSeleccionada`, `NuevaUnidadSeleccionada`, `CategoriasDisponibles`, `UnidadesMedidaDisponibles` y `CambiosDePrecio` pasan al diálogo por el servicio y dejan de tener control: van a sus exclusiones con el motivo (Task 6). `SolicitarFocoEnCantidad` se bindea en la vista. | `ReflexionVistaViewModelTests.cs` L111-136, L209-237. |
| Botón en celda | `DataGridCeldaConControlPaddingPropioGuardTests` exige `CellStyleClasses="sin-padding-vertical"` en toda columna con un `Button` en su `CellTemplate`. En v2 la celda ya tiene padding vertical 0 (`DataGridV2.axaml` L136, "12,0,4,0"): la columna de "Quitar" conserva la clase (el guardián la exige) y la vista v2 NO redeclara el estilo local de v1. | `DataGridCeldaConControlPaddingPropioGuardTests.cs` L41-43, L60-95; `IngresoPorFacturaView.axaml` L13-24. |
| Constructor del VM | Gana `IAltaProductoRapidaDialogService dialogoAlta` e `IConfirmacionPreciosDialogService dialogoPrecios` al FINAL (después de `adjuntosPanel`). Sitios: `IngresoPorFacturaViewModelTests` L74, L237, L269; `IngresoPorFacturaViewTests.Montar` L79; `IngresoPorFacturaGrillaRenglonesTests.MontarConUnRenglon` L82; `DatosIngresoFactura.Vm` (Task 1). DI: `App.axaml.cs` L334 transient; los servicios se registran singleton junto a L292. `ComandosDeGuardadoGuardianTests.Construir` arma el VM por reflexión con mocks (no se toca). | grep de `new IngresoPorFacturaViewModel(`; `App.axaml.cs` L291-292, L334. |
| Overlays de actualización | `CoordinadorActualizacion` no usa `#AA000000` ni `Mostrando*` (grep en todos los `.axaml`: solo `IngresoPorFacturaView`); el guardián de la convención 25 puede barrer TODAS las vistas sin excepción. | grep `AA000000\|Mostrando` en `src/StockApp.Presentation/**/*.axaml`. |

## File Structure

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Behaviors/EnterEjecutaComando.cs` | Enter en un campo ejecuta un comando (túnel, `CanExecute`, combo con valor y cerrado). |
| `src/StockApp.Presentation/Behaviors/TraerALaVista.cs` | Al hacerse visible, el control pide `BringIntoView` (la card de adjuntos tras guardar). |
| `src/StockApp.Presentation/Converters/DistintoDeCeroConverter.cs` | `decimal` ≠ 0 → true (diferencia en advertencia). |
| `src/StockApp.Presentation/Services/IAltaProductoRapidaDialogService.cs`, `AltaProductoRapidaDialogService.cs` | Abre el alta rápida sobre la ventana principal; devuelve `ProductoNuevoDto?`. |
| `src/StockApp.Presentation/Services/IConfirmacionPreciosDialogService.cs`, `ConfirmacionPreciosDialogService.cs` | Abre la confirmación de precios; devuelve los tildes o null. |
| `src/StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml` (+ `.axaml.cs`) | Diálogo v2 de alta rápida (D9). |
| `src/StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml` (+ `.axaml.cs`) | Diálogo v2 de confirmación de precios (D10). |
| `tests/StockApp.Presentation.UiTests/V2/EnterEjecutaComandoTests.cs` | El comportamiento con controles reales y teclado real. |
| `tests/StockApp.Presentation.UiTests/V2/TraerALaVistaTests.cs` | El comportamiento dentro de un `ScrollViewer` real. |
| `tests/StockApp.Presentation.UiTests/V2/AltaProductoRapidaDialogTests.cs`, `ConfirmacionPreciosDialogTests.cs` | Los diálogos reales: teclado, validación, es-UY, un primario, sin recorte, foco. |
| `tests/StockApp.Presentation.UiTests/V2/IngresoPorFacturaViewV2Tests.cs` | La vista real: secciones, un primario, Enter, pie fijo, diferencia, foco, guardado, operador mixto. |
| `tests/StockApp.Presentation.Tests/Converters/DistintoDeCeroConverterTests.cs` | El conversor. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs`, `DatosIngresoFacturaTests.cs`, `IngresoFacturaComparacionTests.cs` | Datos fijos y comparaciones de la pantalla. |
| `docs/ui-v2/mockups/inventario/ingreso-factura.html`, `ingreso-factura-renglones.html`, `ingreso-factura-diferencia.html`, `ingreso-factura-guardado.html` | Mockups de la pantalla (D12). |
| `docs/ui-v2/mockups/dialogos/alta-producto.html`, `alta-producto-faltantes.html`, `confirmacion-precios.html` | Mockups de los diálogos (D12). |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs` | Sin flags ni comandos de overlay; `AbrirAltaProductoAsync` y `GuardarAsync` esperan a los servicios; `SolicitarFocoEnCantidad`. |
| `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml` | Task 6: sin overlays (v1 transitorio). Task 8: reescrita en v2 (el `.axaml.cs` NO cambia). |
| `src/StockApp.Presentation/App.axaml.cs` | Registro de los dos servicios (junto a L292). |
| `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` | `.ui2 TextBlock.advertencia` (después de `titulo-seccion`). |
| `docs/ui-v2/mockups/_base/base.css` | `.v2-texto-advertencia` (espejo) y lo que pidan los mockups (se declara en el GATE). |
| `tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs` | Fakes de los dos servicios de diálogo. |
| Tests heredados (convención 13) | `IngresoPorFacturaViewModelTests`, `IngresoPorFacturaViewTests`, `IngresoPorFacturaGrillaRenglonesTests`, `ReflexionVistaViewModelTests` (exclusiones), `GuardianDePatronTests` (L48, L101, `VistasMigradasAV2`, `DialogosV2`, guardián nuevo), `GrillasSinRecorteBarridoTests` (escenario "Ingreso por factura" L87 y L276), `DialogosComparacionTests`. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | Estado, §7, deuda y §6.5 (Task 10). |
| `docs/ui-v2/herramientas.md` | Convenciones 26 y 27 (Task 10). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Regresión de la pantalla (correr entera tras las Tasks 6 y 8): `--filter "FullyQualifiedName~IngresoPorFactura|FullyQualifiedName~ReflexionVista|FullyQualifiedName~GuardianDePatron|FullyQualifiedName~DataGridCeldaConControlPaddingPropio|FullyQualifiedName~GrillasSinRecorteBarrido|FullyQualifiedName~ShellMainViewGates|FullyQualifiedName~DialogosV2"`
- Comparación con página: `scripts/ui-v2/comparar.sh IngresoFactura` (y `AltaProducto`, `ConfirmacionPrecios`) y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-11-ui-v2-fase-3c3-ingreso-factura/progress.md` (lo crea este plan con BASE = el commit del plan; `.superpowers/` está en `.gitignore`). Cada task agrega su línea al cerrar; nadie lo recrea ni lo pisa.

---

## Bloque A — Datos fijos y teclado, sin XAML de diálogo ni de pantalla

### Task 1: Datos fijos de Ingreso por factura

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFacturaTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `DatosProductos.Productos`, `DatosProductos.Categorias`, `DatosProductos.Unidades`, `DatosProductos.UsuarioAdmin`; `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; constructor actual de `IngresoPorFacturaViewModel` (11 parámetros; la Task 6 le agrega 2 y actualiza `Vm()`).
- Produces: `DatosIngresoFactura.Renglon` y `Estado` (records); `Vacio`, `ConRenglones`, `ConDiferencia`, `Guardado` (`Estado`); `Proveedores`, `Fuentes`, `Rubros`, `Lineas`, `ProductoNuevoNombre`, `Fecha`, `Vencimiento`; `Vm()`, `Aplicar(IngresoPorFacturaViewModel, Estado)`, `EnShell()` → `(ShellMainViewModel Shell, IngresoPorFacturaViewModel Vm)`.

- [ ] **Step 1: Línea base de la suite y ledger**

Run: `dotnet test StockApp.sln` y anotar el total (y los falsos `.ods` por nombre) en el ledger como "Línea base 3c-3".

- [ ] **Step 2: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFacturaTests.cs`:

```csharp
using StockApp.Domain.Formato;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Ingreso por factura escriben estos textos a mano: si un dato cambia y el mockup no, la comparación
/// falla por un texto distinto y el diagnóstico no lo dice. El estado se aplica después de la carga de los combos.
/// </summary>
public class DatosIngresoFacturaTests
{
    [Fact]
    public async Task ConRenglones_SumaYDiferencia_SonLasDelMockup()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConRenglones);

        Assert.Equal(6, vm.Renglones.Count);
        Assert.Equal("$ 44.940,00", FormatoEsUy.Moneda(vm.SumaRenglones));
        Assert.Equal("$ 0,00", FormatoEsUy.Moneda(vm.DiferenciaConTotal));
        Assert.Equal(DatosIngresoFactura.ProductoNuevoNombre, vm.Renglones[5].NombreMostrado);
        Assert.True(vm.Renglones[5].EsProductoNuevo);
        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
    }

    [Fact]
    public async Task ConDiferencia_DiferenciaCreditoYErrorDeCarga()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConDiferencia);

        Assert.Equal("$ 60,00", FormatoEsUy.Moneda(vm.DiferenciaConTotal));
        Assert.True(vm.EsCredito);
        Assert.Equal(DatosIngresoFactura.Vencimiento, vm.FechaVencimientoSeleccionada);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
    }

    [Fact]
    public async Task Guardado_QuedaGuardadoSinPedirConfirmacionDePrecios()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.Guardado);

        Assert.True(vm.GuardadoExitoso);
        Assert.Equal(4521, vm.GastoIdCreado);
        Assert.Single(vm.AdjuntosPanel.Items);
    }

    [Fact]
    public async Task Vacio_SoloLaFechaFija()
    {
        var vm = DatosIngresoFactura.Vm();
        await vm.InicializarAsync();

        DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.Vacio);

        Assert.Empty(vm.Renglones);
        Assert.Null(vm.ProveedorSeleccionado);
        Assert.Equal(DatosIngresoFactura.Fecha, vm.FechaSeleccionada);
    }

    [Fact]
    public void Aplicar_SinLaCarga_FallaEnVezDeDejarLosCombosVacios()
    {
        var vm = DatosIngresoFactura.Vm();

        Assert.Throws<InvalidOperationException>(() => DatosIngresoFactura.Aplicar(vm, DatosIngresoFactura.ConRenglones));
    }
}
```

`AdjuntosPanelViewModel.Items`: confirmar el nombre de la colección con CodeGraph (`AdjuntosPanelViewModel Items`) antes de escribir el assert; si se llama distinto, usar ese nombre y anotarlo.

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosIngresoFacturaTests"`
Expected: FAIL de compilación (`The name 'DatosIngresoFactura' does not exist in the current context`). Pegar.

- [ ] **Step 3: `DatosIngresoFactura`**

```csharp
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/ingreso-factura*.html (textos fijados en DatosIngresoFacturaTests).
/// Los renglones entran por AgregarArticuloCommand, como los carga el usuario, así suma y diferencia las calcula el VM.
/// Los precios son los de costo de DatosProductos: el guardado no pide confirmación de precios.
/// </summary>
public static class DatosIngresoFactura
{
    public sealed record Renglon(string Producto, decimal Cantidad, decimal Precio, bool ActualizarCosto = false);

    public sealed record Estado(bool ConCabecera, IReadOnlyList<Renglon> Renglones, string? Total,
        bool Credito = false, string? ErrorCarga = null, bool Guardado = false);

    public const string ProductoNuevoNombre = "Carretilla de obra 90 L";
    public const string ProductoNuevoCodigo = "MAT-0601";
    public static readonly DateTime Fecha = new(2026, 10, 6);
    public static readonly DateTime Vencimiento = new(2026, 11, 5);

    public static readonly IReadOnlyList<Proveedor> Proveedores =
    [
        new() { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true },
        new() { Id = 2, Nombre = "Ferretería Industrial del Este", Activo = true },
    ];

    public static readonly IReadOnlyList<FuenteFinanciamiento> Fuentes =
    [
        new() { Id = 1, Nombre = "Fondo de Desarrollo del Interior", Activo = true },
        new() { Id = 2, Nombre = "Rentas generales", Activo = true },
    ];

    public static readonly IReadOnlyList<RubroGasto> Rubros =
    [
        new() { Id = 1, Codigo = 231, Nombre = "Materiales de construcción", Activo = true },
    ];

    public static readonly IReadOnlyList<LineaPoa> Lineas =
    [
        new() { Id = 1, Nombre = "Mantenimiento de espacios públicos", Programa = "Obras", Ejercicio = 2026, Activo = true },
    ];

    private static readonly IReadOnlyList<Renglon> Seis =
    [
        new("Cemento portland 50 kg", 40m, 480m, ActualizarCosto: true),
        new("Arena gruesa", 6m, 890m),
        new("Caño PVC 110 mm", 12m, 650m),
        new("Cable 2x1,5 mm", 100m, 38m),
        new("Guantes de trabajo", 20m, 95m),
        new(ProductoNuevoNombre, 2m, 3450m),
    ];

    public static readonly Estado Vacio = new(false, [], null);
    public static readonly Estado ConRenglones = new(true, Seis, "44.940,00");
    public static readonly Estado ConDiferencia = new(true, Seis, "45.000,00", Credito: true,
        ErrorCarga: "La cantidad debe ser mayor a cero.");
    public static readonly Estado Guardado = new(true, Seis, "44.940,00", Guardado: true);

    public static IngresoPorFacturaViewModel Vm()
    {
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(DatosProductos.Productos);
        var categorias = new Mock<ICategoriaService>();
        categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(DatosProductos.Categorias.ToList());
        var unidades = new Mock<IUnidadMedidaService>();
        unidades.Setup(u => u.ListarActivasAsync()).ReturnsAsync(DatosProductos.Unidades.ToList());
        var proveedores = new Mock<IProveedorService>();
        proveedores.Setup(p => p.ListarActivasAsync()).ReturnsAsync(Proveedores.ToList());
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(Fuentes.ToList());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(Rubros.ToList());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(Lineas.ToList());

        var servicio = new Mock<IIngresoPorFacturaService>();
        servicio.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(4521, [1, 2, 3, 4, 5, 6], 44940m, 0m));
        var adjuntos = new Mock<IAdjuntoService>();
        adjuntos.Setup(a => a.ListarPorGastoAsync(4521)).ReturnsAsync(
        [
            new AdjuntoDto(1, "factura-A-0004521.pdf", "application/pdf", 182_400, 4521, null,
                new DateTime(2026, 10, 6, 15, 20, 0, DateTimeKind.Utc)),
        ]);
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(StockApp.Domain.Enums.RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var panel = new AdjuntosPanelViewModel(adjuntos.Object, Mock.Of<IServicioSeleccionArchivo>(),
            Mock.Of<IServicioAperturaArchivo>(), Mock.Of<IConfirmacionService>(), sesion.Object);

        return new IngresoPorFacturaViewModel(servicio.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object, Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>(), panel);
    }

    public static void Aplicar(IngresoPorFacturaViewModel vm, Estado estado)
    {
        if (vm.ProductosDisponibles.Count == 0 || vm.ProveedoresDisponibles.Count == 0)
            throw new InvalidOperationException("La carga de los combos no corrió antes de aplicar el estado.");

        vm.FechaSeleccionada = Fecha;
        if (estado.ConCabecera)
        {
            vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
            vm.NumeroFactura = "A-0004521";
            vm.NumeroOrden = "OC-2026-118";
            vm.Detalle = "Materiales para la reparación de la rambla";
            vm.Destino = "Corralón municipal";
            vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
            vm.RubroSeleccionado = vm.RubrosDisponibles[0];
            vm.LineaPoaSeleccionada = vm.LineasPoaDisponibles[0];
        }
        vm.EsCredito = estado.Credito;
        vm.FechaVencimientoSeleccionada = estado.Credito ? Vencimiento : null;

        foreach (var r in estado.Renglones)
        {
            if (r.Producto == ProductoNuevoNombre)
            {
                vm.EsProductoNuevoEnCarga = true;
                vm.NuevoProductoCodigo = ProductoNuevoCodigo;
                vm.NuevoProductoNombre = ProductoNuevoNombre;
                vm.NuevaCategoriaSeleccionada = vm.CategoriasDisponibles.Single(c => c.Nombre == "Herramientas");
                vm.NuevaUnidadSeleccionada = vm.UnidadesMedidaDisponibles.Single(u => u.Nombre == "Unidad");
            }
            else
            {
                vm.ProductoEnCarga = vm.ProductosDisponibles.Single(p => p.Nombre == r.Producto);
            }
            vm.CantidadEnCarga = r.Cantidad;
            vm.PrecioUnitarioEnCarga = r.Precio;
            vm.ActualizarPrecioCostoEnCarga = r.ActualizarCosto;
            vm.AgregarArticuloCommand.Execute(null);
        }

        vm.MontoTotalTexto = estado.Total ?? string.Empty;
        vm.MensajeErrorCarga = estado.ErrorCarga;
        if (estado.Guardado)
            vm.GuardarCommand.ExecuteAsync(null).GetAwaiter().GetResult();
    }

    public static (ShellMainViewModel Shell, IngresoPorFacturaViewModel Vm) EnShell()
    {
        var vm = Vm();
        return (DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "IngresoPorFactura", vm), vm);
    }
}
```

Antes de escribirlo, confirmar con CodeGraph los nombres de método de los servicios de catálogo y finanzas (`ListarActivasAsync` / `ListarActivosAsync` y sus tipos de retorno: si devuelven `IReadOnlyList<T>`, el `.ToList()` sobra pero compila) y que `DatosProductos.Categorias` contiene "Herramientas" y `Unidades` "Unidad" (verificado al escribir el plan: ids 3 y 1).

Run: el comando del Step 2. Expected: PASS (5). Si `FormatoEsUy` da otro texto, manda `FormatoEsUy`: corregir el TEST y anotarlo en el ledger (los mockups se escriben con esos textos).

- [ ] **Step 4: Ledger y commit**

Ledger: la tabla de los 4 estados (cabecera, renglones, total, suma, diferencia, crédito, error de carga, guardado) tal como los muestra la app.

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFacturaTests.cs
git commit -m "test(ui-v2): datos fijos de Ingreso por factura para mockups y comparaciones"
```

### Task 2: `EnterEjecutaComando`

**Files:**
- Create: `src/StockApp.Presentation/Behaviors/EnterEjecutaComando.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/EnterEjecutaComandoTests.cs`

**Interfaces:**
- Consumes: nada de la fase.
- Produces: `public static class EnterEjecutaComando { public static readonly AttachedProperty<ICommand?> ComandoProperty; public static ICommand? GetComando(InputElement); public static void SetComando(InputElement, ICommand?); }` (XAML: `beh:EnterEjecutaComando.Comando="{Binding …}"`).

- [ ] **Step 1: Tests (rojo)**

```csharp
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Enter en la fila de carga de Ingreso por factura agrega el artículo y nunca guarda. Banco: un TextBox y un ComboBox
/// con el comportamiento, y un botón IsDefault que cuenta los Enter que se escapan (en la pantalla real no hay
/// IsDefault; acá hace de detector).
/// </summary>
public class EnterEjecutaComandoTests
{
    private sealed class Banco
    {
        public Window Ventana = null!;
        public TextBox Caja = null!;
        public ComboBox Combo = null!;
        public RelayCommand Comando = null!;
        public int Ejecuciones;
        public int EnterQueSeEscapan;
        public bool Puede = true;
    }

    private static Banco Montar(string? elegido = null)
    {
        var b = new Banco();
        b.Comando = new RelayCommand(() => b.Ejecuciones++, () => b.Puede);
        b.Caja = new TextBox();
        b.Combo = new ComboBox { ItemsSource = new[] { "Cemento portland 50 kg", "Arena gruesa" }, SelectedItem = elegido };
        var porDefecto = new Button { Content = "Guardar", IsDefault = true };
        porDefecto.Click += (_, _) => b.EnterQueSeEscapan++;
        EnterEjecutaComando.SetComando(b.Caja, b.Comando);
        EnterEjecutaComando.SetComando(b.Combo, b.Comando);
        b.Ventana = new Window { Width = 400, Height = 300, Content = new StackPanel { Children = { b.Caja, b.Combo, porDefecto } } };
        b.Ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return b;
    }

    private static void Tecla(Banco b, InputElement foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        b.Ventana.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void Detector_SinElComportamiento_ElEnterDelCampoLlegaAlBotonPorDefecto()
    {
        // Control positivo: si esto no da 1, el banco no ve un Enter que se escapa y los demás tests no custodian nada.
        var b = Montar();
        EnterEjecutaComando.SetComando(b.Caja, null);

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.Equal(1, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnCampoDeTexto_Ejecuta_YNoSeEscapa()
    {
        var b = Montar();

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(1, b.Ejecuciones);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnComboConValor_Ejecuta_NoAbreElDesplegable_YNoSeEscapa()
    {
        var b = Montar(elegido: "Arena gruesa");

        Tecla(b, b.Combo, PhysicalKey.Enter);

        Assert.Equal(1, b.Ejecuciones);
        Assert.False(b.Combo.IsDropDownOpen);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void EnUnComboVacio_NoEjecuta_YConservaLoNativo()
    {
        var b = Montar();

        Tecla(b, b.Combo, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.True(b.Combo.IsDropDownOpen, "Lo nativo del ComboBox cerrado con Enter es abrir el desplegable.");
    }

    [AvaloniaFact]
    public void ConElDesplegableAbierto_ElEnterEligeYNoEjecuta()
    {
        var b = Montar(elegido: "Arena gruesa");
        b.Combo.Focus();
        b.Combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        // RaiseEvent y no KeyPressQwerty: con el desplegable abierto el foco del teclado está en el popup.
        b.Combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, b.Ejecuciones);
        Assert.False(b.Combo.IsDropDownOpen);
    }

    [AvaloniaFact]
    public void ConElComandoBloqueado_NoEjecuta_YTampocoSeEscapa()
    {
        var b = Montar();
        b.Puede = false;
        b.Comando.NotifyCanExecuteChanged();

        Tecla(b, b.Caja, PhysicalKey.Enter);

        Assert.Equal(0, b.Ejecuciones);
        Assert.Equal(0, b.EnterQueSeEscapan);
    }

    [AvaloniaFact]
    public void OtraTecla_NoEjecuta()
    {
        var b = Montar(elegido: "Arena gruesa");

        Tecla(b, b.Caja, PhysicalKey.A);
        Tecla(b, b.Combo, PhysicalKey.Tab);

        Assert.Equal(0, b.Ejecuciones);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~EnterEjecutaComandoTests"`
Expected: FAIL de compilación (`The name 'EnterEjecutaComando' does not exist`). Pegar. Si, ya implementado, `Detector_…` no da `EnterQueSeEscapan == 1`, el `IsDefault` no reacciona al teclado headless: PARAR y reportar (el banco no custodiaría "Enter nunca guarda"). Si `EnUnComboVacio_…` muestra que el `ComboBox` de Avalonia 12 NO abre el desplegable con Enter, cambiar SOLO ese assert por lo nativo observado, mantener `Ejecuciones == 0` y anotarlo en el reporte.

- [ ] **Step 2: Implementación**

```csharp
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Enter dentro de un campo ejecuta un comando: en Ingreso por factura, Enter en la fila de carga agrega el artículo
/// para cargar muchos renglones seguidos sin el mouse. Va en túnel: un ComboBox cerrado abre su desplegable con Enter
/// y marca el evento antes de que lo vea un manejador de burbuja. En un ComboBox solo actúa con un ítem elegido y el
/// desplegable cerrado (vacío, Enter abre el desplegable como siempre; abierto, Enter elige el ítem). Ejecuta solo si
/// el comando puede (un campo en rojo lo bloquea igual que al botón) y marca el Enter como manejado, así no llega a
/// ningún botón por defecto.
/// </summary>
public static class EnterEjecutaComando
{
    public static readonly AttachedProperty<ICommand?> ComandoProperty =
        AvaloniaProperty.RegisterAttached<InputElement, InputElement, ICommand?>("Comando");

    static EnterEjecutaComando()
    {
        ComandoProperty.Changed.AddClassHandler<InputElement>(AlCambiarComando);
    }

    public static ICommand? GetComando(InputElement elemento) => elemento.GetValue(ComandoProperty);

    public static void SetComando(InputElement elemento, ICommand? valor) => elemento.SetValue(ComandoProperty, valor);

    private static void AlCambiarComando(InputElement elemento, AvaloniaPropertyChangedEventArgs e)
    {
        elemento.RemoveHandler(InputElement.KeyDownEvent, AlPresionarTecla);
        if (e.NewValue is ICommand)
            elemento.AddHandler(InputElement.KeyDownEvent, AlPresionarTecla, RoutingStrategies.Tunnel);
    }

    private static void AlPresionarTecla(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Handled || sender is not InputElement elemento)
            return;
        if (elemento is ComboBox combo && (combo.SelectedItem is null || combo.IsDropDownOpen))
            return;
        var comando = GetComando(elemento);
        if (comando is null)
            return;
        if (comando.CanExecute(null))
            comando.Execute(null);
        e.Handled = true;
    }
}
```

Run: el comando del Step 1. Expected: PASS (7).

- [ ] **Step 3: Mutaciones (QUITANDO)**

- (a) Quitar `e.Handled = true;` → rojo en `EnUnCampoDeTexto_Ejecuta_YNoSeEscapa` (`EnterQueSeEscapan` 1). Pegar. Restaurar.
- (b) Quitar `|| combo.IsDropDownOpen` → rojo en `ConElDesplegableAbierto_…` (ejecuta). Pegar. Restaurar.
- (c) Quitar `combo.SelectedItem is null ||` → rojo en `EnUnComboVacio_…`. Pegar. Restaurar.
- (d) Quitar `if (comando.CanExecute(null))` (dejar `comando.Execute(null);`) → rojo en `ConElComandoBloqueado_…`. Pegar. Restaurar.
- (e) Quitar el argumento `, RoutingStrategies.Tunnel` (queda la estrategia por defecto, burbuja) → rojo en `EnUnComboConValor_…` (el combo abre el desplegable). Pegar. Restaurar.

- [ ] **Step 4: Build y commit**

Run: `dotnet build StockApp.sln` → 0 errores.

```bash
git add src/StockApp.Presentation/Behaviors/EnterEjecutaComando.cs tests/StockApp.Presentation.UiTests/V2/EnterEjecutaComandoTests.cs
git commit -m "feat(ui-v2): Enter en un campo ejecuta un comando sin llegar al botón por defecto"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes de todo XAML de diálogo o de pantalla)

### Task 3: Medición y mockups de la pantalla y de los dos diálogos

**Decisiones aplicadas:** D1 a D16.

**Files:**
- Modify: `docs/ui-v2/mockups/_base/base.css`
- Create: `docs/ui-v2/mockups/inventario/ingreso-factura.html`, `ingreso-factura-renglones.html`, `ingreso-factura-diferencia.html`, `ingreso-factura-guardado.html`
- Create: `docs/ui-v2/mockups/dialogos/alta-producto.html`, `alta-producto-faltantes.html`, `confirmacion-precios.html`

**Interfaces:**
- Consumes: `DatosIngresoFactura` (Task 1), `IngresoPorFacturaView` actual (v1) como banco de medición de la grilla, `ColumnaAuto` (3c-1).
- Produces: pantallas `inventario-ingreso-factura`, `-renglones`, `-diferencia`, `-guardado`, `dialogos-alta-producto`, `-alta-producto-faltantes`, `-confirmacion-precios`; `.v2-texto-advertencia` en `base.css`; en el ledger, los anchos medidos y el alto del panel de adjuntos v1.

- [ ] **Step 1: Medir en headless (archivo temporal, no se commitea)**

Crear `tests/StockApp.Presentation.VisualTests/MedicionTemporal3c3.cs` siguiendo `MedicionTemporal3c2` (plan de la 3c-2, Task 4 Step 1): monta `ShellMainView` con `DatosIngresoFactura.EnShell()` a 1280×760 y 1366×768, oculta el sidebar, aplica `ConRenglones` y escribe por tamaño, con `--logger "console;verbosity=detailed"`:
1. Grilla de renglones (v1): marcar `ColumnaAuto.SetMedir` en Cantidad (`CantidadConverter`), Precio unitario y Subtotal (`MonedaConverter`), reasignar la fuente pasando por null y anotar `DataGridColumnasAutoBehavior.AnchoMedido` de cada una, más el ancho de "Quitar" con el botón `ghost` v2.
2. Fila de carga: con un `Grid` de prueba `*,12,120,12,140,12,Auto,12,Auto,12,Auto` bajo `.ui2` dentro del ancho interno de la card (984 − 48 a 1280; 1070 − 48 a 1366) con un `CheckBox` "Actualizar precio costo" y dos `Button.secundario` "Producto nuevo" y "Agregar artículo", el ancho real de la columna `*` (regla de D4: < 240 a 1280 → dos líneas).
3. Estado guardado: el alto de `AdjuntosPanelView` v1 con un adjunto, dentro de la card v2 (D14).

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~MedicionTemporal3c3" --logger "console;verbosity=detailed"`. Anotar las líneas en el ledger.

- [ ] **Step 2: Espejo CSS**

Al final de `base.css`:

```css
/* Valor en advertencia (Diferencia del pie de Ingreso por factura): mismo texto que la familia advertencia de la
   insignia y la alerta. Espejo de .ui2 TextBlock.advertencia. */
.v2-texto-advertencia { color: var(--v2-color-insignia-advertencia-texto); }
```

Confirmar el nombre exacto de la variable en `docs/ui-v2/mockups/_base/tokens.css` (`grep -n "advertencia" docs/ui-v2/mockups/_base/tokens.css`) antes de escribirla. Si un mockup necesita otra clase de geometría nueva (pie fijo, fila de carga), se agrega con su espejo en el XAML y se declara en el GATE.

- [ ] **Step 3: Mockups de la pantalla**

Partir de `inventario/entrada-compra.html` (encabezado sin breadcrumb, card, campos, `Alerta`) y `inventario/historial.html` (grilla con `v2-num`). Cada uno:
- `<title>Inventario: Ingreso por factura[ estado] (UI v2)</title>` y `<!-- Datos: DatosIngresoFactura.<Estado>: … -->`.
- Contenido scrolleable en un bloque con `overflow: hidden` de alto `calc(var(--v2-ventana-alto) - <alto del pie>)`, encabezado (D1), card de todo el ancho (D2) con las tres secciones y sus subtítulos, campos de D3, fila de carga de D4 (con la regla medida en el Step 1), grilla de D6 con los anchos MEDIDOS del Step 1 como `flex: 0 0 Npx` y PRODUCTO flexible; vacío en `ingreso-factura.html`.
- Pie fijo al fondo del lienzo (D7): totales a la izquierda (Diferencia con `.v2-texto-advertencia` en `-diferencia`), Cancelar y Guardar a la derecha; en `-guardado` solo "Finalizar".
- `-diferencia`: Alerta de error de carga debajo de la fila (D5) y vencimiento visible.
- `-guardado`: card deshabilitada (los colores de deshabilitado de `base.css`) y card "Adjuntá la factura" con el bloque `.panel-adjuntos-v1` de alto medido y `visibility: hidden` (D14), a la vista (D8).

- [ ] **Step 4: Mockups de los diálogos y capturas**

`dialogos/alta-producto.html` y `-faltantes.html` copiando `dialogos/recalcular-stock.html` (D9: 440 de ancho, campos `v2-campo`/`v2-etiqueta` con asterisco de `producto-nuevo.html`, combos con el glifo de `componentes/catalogo.html`); `dialogos/confirmacion-precios.html` (D10: 520, encabezados, tres filas con `v2-tilde` (el primero tildado) y precios con `v2-num`).

```bash
for p in ingreso-factura ingreso-factura-renglones ingreso-factura-diferencia ingreso-factura-guardado; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
for p in alta-producto alta-producto-faltantes confirmacion-precios; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/dialogos/$p.html artifacts/ui-v2/mockups/dialogos-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/dialogos/$p.html artifacts/ui-v2/mockups/dialogos-$p@1366x768.png --tamano 1366x768
done
ls -l --time-style=full-iso artifacts/ui-v2/mockups/inventario-ingreso-factura*.png artifacts/ui-v2/mockups/dialogos-alta-producto*.png artifacts/ui-v2/mockups/dialogos-confirmacion-precios*.png
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 14 PNG con fecha de ahora dentro del worktree; Herramientas.Tests verde (incluido `MockupsDelRepo_RespetanLaConvencion`); `tokens --verificar` exit 0. MIRAR cada PNG.

- [ ] **Step 5: Borrar la medición temporal**

`rm tests/StockApp.Presentation.VisualTests/MedicionTemporal3c3.cs` y `git status --short tests/` (no debe listarlo).

- [ ] **Step 6: GATE — aprobación del usuario**

PARAR. Presentar los 14 PNG (LOS DOS TAMAÑOS), la lista D1 a D16 y los anchos medidos del ledger. Señalar explícitamente: un solo primario y Enter en la fila (U2); el pie fijo y la alerta fuera del scroll (D7); la regla de una o dos líneas de la fila de carga con el ancho medido (D4); precios con símbolo "$" en la grilla (D6, cambio visual); el scroll automático a la card de adjuntos (D8); el foco en Cantidad tras el alta (D11); el hallazgo H1 (D15) con la recomendación de no cambiarlo; y que el interior v1 de adjuntos no se compara (D14). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups y en los tests de las Tasks 4 a 8 que la fijan, y volver a presentar. Registrar en el ledger: `Mockups 3c-3: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">`.

- [ ] **Step 7: Commit**

```bash
git add docs/ui-v2/mockups/_base/base.css docs/ui-v2/mockups/inventario/ingreso-factura*.html docs/ui-v2/mockups/dialogos/alta-producto*.html docs/ui-v2/mockups/dialogos/confirmacion-precios.html
git commit -m "docs(ui-v2): mockups aprobados de Ingreso por factura y de sus dos diálogos"
```

## Bloque C — Diálogos, VM y pantalla (arranca solo con los mockups aprobados)

### Task 4: `AltaProductoRapidaDialog` y su servicio

**Depende de:** Task 3 (D9 aprobada).

**Files:**
- Create: `src/StockApp.Presentation/Services/IAltaProductoRapidaDialogService.cs`, `AltaProductoRapidaDialogService.cs`
- Create: `src/StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml`, `AltaProductoRapidaDialog.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/AltaProductoRapidaDialogTests.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (junto a L292), `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`DialogosV2`, L479-485), `tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs`

**Interfaces:**
- Consumes: `ProductoNuevoDto`, `Categoria`, `UnidadMedida`; patrón de `RecalcularStockDialogService`.
- Produces: `public interface IAltaProductoRapidaDialogService { Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades); }`; `AltaProductoRapidaDialog(IReadOnlyList<Categoria>, IReadOnlyList<UnidadMedida>)` con partes `MensajeText`, `CodigoTextBox`, `NombreTextBox`, `CategoriaCombo`, `UnidadCombo`, `AlertaFaltantes`, `CancelarButton`, `ConfirmarButton`; `internal static string AltaProductoRapidaDialog.TextoFaltantes(IReadOnlyList<string>)`.

- [ ] **Step 1: Tests del diálogo real (rojo)**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Alta rápida de producto (convención 25): el diálogo real, sin VM. Un IsCancel o un IsDefault perdido cambia lo que
/// hace el teclado; una validación perdida da de alta un producto sin unidad.
/// </summary>
public class AltaProductoRapidaDialogTests
{
    private static readonly IReadOnlyList<Categoria> Categorias =
        [new() { Id = 1, Nombre = "Construcción" }, new() { Id = 3, Nombre = "Herramientas" }];
    private static readonly IReadOnlyList<UnidadMedida> Unidades =
        [new() { Id = 1, Nombre = "Unidad", Abreviatura = "u" }, new() { Id = 2, Nombre = "Bolsa", Abreviatura = "bol" }];

    private static (AltaProductoRapidaDialog Dialogo, Task<ProductoNuevoDto?> Resultado) Abrir()
    {
        var dialogo = new AltaProductoRapidaDialog(Categorias, Unidades);
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<ProductoNuevoDto?>(duenio);
        Dispatcher.UIThread.RunJobs();
        return (dialogo, resultado);
    }

    private static T Parte<T>(Window d, string nombre) where T : Control => d.FindControl<T>(nombre)!;

    private static void Clic(Window d, string boton)
    {
        Parte<Button>(d, boton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Tecla(Window d, Control foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        d.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Completar(Window d, string codigo, string nombre, int? categoriaId, int? unidadId)
    {
        Parte<TextBox>(d, "CodigoTextBox").Text = codigo;
        Parte<TextBox>(d, "NombreTextBox").Text = nombre;
        Parte<ComboBox>(d, "CategoriaCombo").SelectedItem = Categorias.SingleOrDefault(c => c.Id == categoriaId);
        Parte<ComboBox>(d, "UnidadCombo").SelectedItem = Unidades.SingleOrDefault(u => u.Id == unidadId);
        Dispatcher.UIThread.RunJobs();
    }

    private static IInputElement? Foco(Window d) => d.FocusManager!.GetFocusedElement();

    [AvaloniaFact]
    public void Completo_ConfirmarDevuelveElProductoNuevo()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Clic(d, "ConfirmarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Equal(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1), resultado.Result);
    }

    [AvaloniaFact]
    public void SinCategoria_DevuelveCategoriaNull()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", null, 1);

        Clic(d, "ConfirmarButton");

        Assert.Null(resultado.Result!.CategoriaId);
    }

    [AvaloniaTheory]
    [InlineData("", "Carretilla", 1, "Falta completar: Código.")]
    [InlineData("MAT-0601", "   ", 1, "Falta completar: Nombre.")]
    [InlineData("MAT-0601", "Carretilla", null, "Falta completar: Unidad de medida.")]
    [InlineData("MAT-0601", "", null, "Falta completar: Nombre y Unidad de medida.")]
    [InlineData("", "", null, "Falta completar: Código, Nombre y Unidad de medida.")]
    public void ConFaltantes_NoConfirma_YLaAlertaLosNombra(string codigo, string nombre, int? unidadId, string esperado)
    {
        var (d, resultado) = Abrir();
        Completar(d, codigo, nombre, null, unidadId);

        Clic(d, "ConfirmarButton");

        Assert.False(resultado.IsCompleted);
        var alerta = Parte<Alerta>(d, "AlertaFaltantes");
        Assert.True(alerta.IsVisible);
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal(esperado, alerta.Texto);
    }

    [AvaloniaFact]
    public void SinFaltantes_LaAlertaNoSeVe() =>
        Assert.False(Parte<Alerta>(Abrir().Dialogo, "AlertaFaltantes").IsVisible);

    [AvaloniaFact]
    public void Cancelar_DevuelveNullAunqueEsteCompleto()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Clic(d, "CancelarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_Cancela()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Tecla(d, Parte<TextBox>(d, "NombreTextBox"), PhysicalKey.Escape);

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_ConElComboAbierto_PrimeroCierraElDesplegable()
    {
        var (d, resultado) = Abrir();
        var combo = Parte<ComboBox>(d, "UnidadCombo");
        combo.Focus();
        combo.IsDropDownOpen = true;
        Dispatcher.UIThread.RunJobs();

        combo.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Dispatcher.UIThread.RunJobs();

        Assert.False(combo.IsDropDownOpen);
        Assert.False(resultado.IsCompleted, "El primer Escape cerró el diálogo entero y perdió lo tipeado.");

        Tecla(d, combo, PhysicalKey.Escape);
        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Enter_EnUnCampoConTodoCompleto_Confirma()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "Carretilla de obra 90 L", 3, 1);

        Tecla(d, Parte<TextBox>(d, "NombreTextBox"), PhysicalKey.Enter);

        Assert.True(resultado.IsCompleted);
        Assert.Equal("MAT-0601", resultado.Result!.Codigo);
    }

    [AvaloniaFact]
    public void Enter_ConFaltantes_NoCierraYMuestraLaAlerta()
    {
        var (d, resultado) = Abrir();
        Completar(d, "MAT-0601", "", null, null);

        Tecla(d, Parte<TextBox>(d, "CodigoTextBox"), PhysicalKey.Enter);

        Assert.False(resultado.IsCompleted);
        Assert.True(Parte<Alerta>(d, "AlertaFaltantes").IsVisible);
    }

    [AvaloniaFact]
    public void Abre_ConElFocoEnCodigo()
    {
        var (d, _) = Abrir();

        Assert.Same(Parte<TextBox>(d, "CodigoTextBox"), Foco(d));
    }

    [AvaloniaFact]
    public void Tab_NoSacaElFocoDelDialogo()
    {
        var (d, _) = Abrir();
        var contenido = Parte<Border>(d, "Contenido");

        for (var i = 0; i < 8; i++)
        {
            d.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            var foco = Assert.IsAssignableFrom<Visual>(Foco(d));
            Assert.True(foco == contenido || contenido.IsVisualAncestorOf(foco), $"Tab {i + 1}: el foco salió del diálogo.");
        }
    }

    [AvaloniaFact]
    public void Estructura_Ui2EnElBorderRaiz_UnSoloPrimario_YCancelarConfirmarConTeclado()
    {
        var d = new AltaProductoRapidaDialog(Categorias, Unidades);

        var contenido = Assert.IsType<Border>(d.Content);
        Assert.Equal("Contenido", contenido.Name);
        Assert.Contains("ui2", contenido.Classes);
        Assert.Equal("Producto nuevo", d.Title);
        Assert.Equal(440, d.Width);
        Assert.Equal(SizeToContent.Height, d.SizeToContent);
        Assert.False(d.CanResize);
        Assert.False(d.ShowInTaskbar);
        Assert.Equal(WindowStartupLocation.CenterOwner, d.WindowStartupLocation);
        var botones = contenido.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario")).ToList();
        Assert.Same(Parte<Button>(d, "ConfirmarButton"), Assert.Single(botones));
        Assert.True(Parte<Button>(d, "CancelarButton").IsCancel);
        Assert.True(Parte<Button>(d, "ConfirmarButton").IsDefault);
        Assert.Equal("Cancelar", Parte<Button>(d, "CancelarButton").Content);
        Assert.Equal("Confirmar", Parte<Button>(d, "ConfirmarButton").Content);
    }

    [AvaloniaFact]
    public void Requeridos_ConAsteriscoYCategoriaOpcional()
    {
        var d = new AltaProductoRapidaDialog(Categorias, Unidades);
        bool Requerido(Control control) => control.FindAncestorOfType<CampoFormulario>()!.Requerido;

        Assert.True(Requerido(Parte<TextBox>(d, "CodigoTextBox")));
        Assert.True(Requerido(Parte<TextBox>(d, "NombreTextBox")));
        Assert.True(Requerido(Parte<ComboBox>(d, "UnidadCombo")));
        Assert.False(Requerido(Parte<ComboBox>(d, "CategoriaCombo")));
    }

    [AvaloniaFact]
    public void Botones_SinRecorteDentroDelAnchoUtil()
    {
        var (d, _) = Abrir();
        var contenido = Parte<Border>(d, "Contenido");
        foreach (var b in new[] { Parte<Button>(d, "CancelarButton"), Parte<Button>(d, "ConfirmarButton") })
        {
            var texto = b.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == (string)b.Content!);
            texto.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var util = b.Bounds.Width - b.Padding.Left - b.Padding.Right - b.BorderThickness.Left - b.BorderThickness.Right;
            Assert.True(texto.DesiredSize.Width <= util + 0.5, $"{b.Content}: pide {texto.DesiredSize.Width} y tiene {util}.");
            var derecha = b.TranslatePoint(new Point(b.Bounds.Width, 0), contenido)!.Value.X;
            Assert.True(derecha <= contenido.Bounds.Width - contenido.Padding.Right + 0.5, $"{b.Content} termina en {derecha:F1}.");
        }
    }

    [AvaloniaFact]
    public async Task Servicio_SinVentanaPrincipal_DevuelveNullSinAbrir()
    {
        // TestApp no es un IClassicDesktopStyleApplicationLifetime: no hay MainWindow, valor seguro.
        var resultado = await new AltaProductoRapidaDialogService().PedirProductoNuevoAsync(Categorias, Unidades);

        Assert.Null(resultado);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~AltaProductoRapidaDialogTests"` → FAIL de compilación. Pegar. (`Window.FocusManager`: si en Avalonia 12 el acceso es `TopLevel.GetTopLevel(d)!.FocusManager`, usar ese y anotarlo.)

- [ ] **Step 2: Diálogo y servicio**

`AltaProductoRapidaDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        xmlns:v2="using:StockApp.Presentation.Controls.V2"
        xmlns:ent="clr-namespace:StockApp.Domain.Entities;assembly=StockApp.Domain"
        mc:Ignorable="d" d:DesignWidth="440" d:DesignHeight="360"
        x:Class="StockApp.Presentation.Views.Dialogs.AltaProductoRapidaDialog"
        Title="Producto nuevo"
        Width="440"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        ShowInTaskbar="False"
        Background="{DynamicResource V2SuperficieBrush}">

    <!-- UI v2 (spec §5.5). Modal de pantalla como Window con servicio, nunca un overlay sobre la vista. Sin bindings:
         el servicio le pasa las categorías y unidades que la pantalla ya cargó. Escape cancela y Enter confirma. -->
    <Border x:Name="Contenido"
            Classes="ui2"
            Background="{DynamicResource V2SuperficieBrush}"
            Padding="24">
        <StackPanel Spacing="16">

            <TextBlock x:Name="MensajeText" Classes="cuerpo" TextWrapping="Wrap"
                       Text="El producto se da de alta junto con la factura, al guardar." />

            <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,Auto,Auto">
                <v2:CampoFormulario Grid.Row="0" Grid.ColumnSpan="3" Etiqueta="Código" Requerido="True">
                    <TextBox x:Name="CodigoTextBox" PlaceholderText="Ej.: MAT-0601" />
                </v2:CampoFormulario>
                <v2:CampoFormulario Grid.Row="1" Grid.ColumnSpan="3" Margin="0,16,0,0" Etiqueta="Nombre" Requerido="True">
                    <TextBox x:Name="NombreTextBox" PlaceholderText="Ej.: Carretilla de obra 90 L" />
                </v2:CampoFormulario>
                <v2:CampoFormulario Grid.Row="2" Grid.Column="0" Margin="0,16,0,0" Etiqueta="Categoría">
                    <ComboBox x:Name="CategoriaCombo" PlaceholderText="Sin categoría" HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate x:DataType="ent:Categoria">
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                </v2:CampoFormulario>
                <v2:CampoFormulario Grid.Row="2" Grid.Column="2" Margin="0,16,0,0" Etiqueta="Unidad de medida" Requerido="True">
                    <ComboBox x:Name="UnidadCombo" PlaceholderText="Elegí la unidad" HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate x:DataType="ent:UnidadMedida">
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>
                </v2:CampoFormulario>
            </Grid>

            <v2:Alerta x:Name="AlertaFaltantes" Variante="Error" IsVisible="False" />

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,8,0,0">
                <Button x:Name="CancelarButton" Classes="secundario" Content="Cancelar" IsCancel="True" Click="OnCancelarClick" />
                <Button x:Name="ConfirmarButton" Classes="primario" Content="Confirmar" IsDefault="True" Click="OnConfirmarClick" />
            </StackPanel>

        </StackPanel>
    </Border>

</Window>
```

Las propiedades exactas de `CampoFormulario` (contenido, `Requerido`) se confirman en `ComponentesV2.axaml` y en `MovimientoFormControl.axaml` L49-69 antes de escribir.

`AltaProductoRapidaDialog.axaml.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Alta rápida de un producto nuevo dentro de Ingreso por factura. "Confirmar" devuelve el
/// <see cref="ProductoNuevoDto"/> si están Código, Nombre y Unidad de medida (Categoría es opcional, como en el
/// servicio); si falta algo, no cierra y la alerta nombra lo que falta. "Cancelar", Escape o cerrar la ventana
/// devuelven null. Usá <see cref="Window.ShowDialog{TResult}"/> con TResult=ProductoNuevoDto?.
/// </summary>
public partial class AltaProductoRapidaDialog : Window
{
    public AltaProductoRapidaDialog()
    {
        InitializeComponent();
    }

    public AltaProductoRapidaDialog(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades) : this()
    {
        CategoriaCombo.ItemsSource = categorias;
        UnidadCombo.ItemsSource = unidades;
        Opened += (_, _) => CodigoTextBox.Focus();
    }

    /// <summary>"Falta completar: Código, Nombre y Unidad de medida." con los que falten, en el orden de los campos.</summary>
    internal static string TextoFaltantes(IReadOnlyList<string> faltantes) =>
        "Falta completar: "
        + (faltantes.Count == 1 ? faltantes[0] : string.Join(", ", faltantes.Take(faltantes.Count - 1)) + " y " + faltantes[^1])
        + ".";

    private List<string> Faltantes()
    {
        var faltantes = new List<string>();
        if (string.IsNullOrWhiteSpace(CodigoTextBox.Text))
            faltantes.Add("Código");
        if (string.IsNullOrWhiteSpace(NombreTextBox.Text))
            faltantes.Add("Nombre");
        if (UnidadCombo.SelectedItem is not UnidadMedida)
            faltantes.Add("Unidad de medida");
        return faltantes;
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e)
    {
        var faltantes = Faltantes();
        if (faltantes.Count > 0)
        {
            AlertaFaltantes.Texto = TextoFaltantes(faltantes);
            AlertaFaltantes.IsVisible = true;
            return;
        }

        Close(new ProductoNuevoDto(CodigoTextBox.Text!, NombreTextBox.Text!,
            (CategoriaCombo.SelectedItem as Categoria)?.Id, ((UnidadMedida)UnidadCombo.SelectedItem!).Id));
    }

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

`IAltaProductoRapidaDialogService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abre el diálogo de alta rápida de producto de Ingreso por factura con las categorías y unidades que la pantalla ya
/// cargó. Devuelve el producto nuevo, o null si se cancela (o si no hay ventana donde mostrarlo).
/// </summary>
public interface IAltaProductoRapidaDialogService
{
    Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades);
}
```

`AltaProductoRapidaDialogService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>Implementación real: mismo mecanismo defensivo que RecalcularStockDialogService.</summary>
public class AltaProductoRapidaDialogService : IAltaProductoRapidaDialogService
{
    public Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<ProductoNuevoDto?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarAsync(categorias, unidades));
    }

    private static async Task<ProductoNuevoDto?> MostrarAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        var owner = (AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return null;

        return await new AltaProductoRapidaDialog(categorias, unidades).ShowDialog<ProductoNuevoDto?>(owner);
    }
}
```

Registro en `App.axaml.cs`, después de L292: `services.AddSingleton<IAltaProductoRapidaDialogService, AltaProductoRapidaDialogService>();`.

`GuardianDePatronTests.DialogosV2` gana `"StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml"`. `Guardian_CubreTodasLasVistasDelEnsamblado` tiene que seguir verde (si el diálogo cae como huérfano, agregarlo donde están cubiertos `RecalcularStockDialog` y `PedirTextoDialog`).

Run: `dotnet build StockApp.sln`; el comando del Step 1 y `--filter "FullyQualifiedName~GuardianDePatronTests|FullyQualifiedName~DialogosV2Tests"` → PASS.

- [ ] **Step 3: Comparación del diálogo**

En `DialogosComparacionTests`:

```csharp
    private static Control AltaProducto() =>
        ContenidoDeDialogo.Extraer(new AltaProductoRapidaDialog(DatosProductos.Categorias, DatosProductos.Unidades));

    private static Control AltaProductoFaltantes()
    {
        var dialogo = new AltaProductoRapidaDialog(DatosProductos.Categorias, DatosProductos.Unidades);
        dialogo.FindControl<TextBox>("CodigoTextBox")!.Text = "MAT-0601";
        dialogo.FindControl<Button>("ConfirmarButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        return ContenidoDeDialogo.Extraer(dialogo);
    }

    [AvaloniaFact]
    public void AltaProducto_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(AltaProducto(), null, "dialogos/alta-producto.html");

    [AvaloniaFact]
    public void AltaProducto_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(AltaProducto(), null, "dialogos/alta-producto.html", tamano: TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void AltaProductoFaltantes_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(AltaProductoFaltantes(), null, "dialogos/alta-producto-faltantes.html");

    [AvaloniaFact]
    public void AltaProductoFaltantes_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(AltaProductoFaltantes(), null, "dialogos/alta-producto-faltantes.html", tamano: TamanoLienzo.Municipio);
```

(`using Avalonia.Interactivity;` y `using StockApp.Presentation.VisualTests.Pantallas.Inventario;` si faltan.)

Run: `scripts/ui-v2/comparar.sh AltaProducto`. Expected: 4 PASS; anotar teselas densas en el ledger; MIRAR las filas en `index.html`. Si supera el umbral: `teselas.py`/`tinta.py` ANTES de tocar nada.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (f) Quitar `IsCancel="True"` → rojo en `Escape_Cancela` y en `Estructura_…`. Restaurar.
- (g) Quitar `IsDefault="True"` → rojo en `Enter_EnUnCampoConTodoCompleto_Confirma`. Restaurar.
- (h) En `OnConfirmarClick`, quitar el bloque `if (faltantes.Count > 0) { … }` → rojo en `ConFaltantes_NoConfirma_…` (devuelve un DTO con unidad nula o lanza). Restaurar.
- (i) Quitar `Opened += (_, _) => CodigoTextBox.Focus();` → rojo en `Abre_ConElFocoEnCodigo`. Restaurar.

`dotnet build StockApp.sln` después de cada restauración y `git diff --no-index` vacío.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Services/IAltaProductoRapidaDialogService.cs src/StockApp.Presentation/Services/AltaProductoRapidaDialogService.cs src/StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml src/StockApp.Presentation/Views/Dialogs/AltaProductoRapidaDialog.axaml.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.UiTests/V2/AltaProductoRapidaDialogTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs
git commit -m "feat(ui-v2): diálogo v2 de alta rápida de producto con validación en línea y teclado"
```

### Task 5: `ConfirmacionPreciosDialog` y su servicio

**Depende de:** Task 3 (D10 aprobada).

**Files:**
- Create: `src/StockApp.Presentation/Services/IConfirmacionPreciosDialogService.cs`, `ConfirmacionPreciosDialogService.cs`
- Create: `src/StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml`, `ConfirmacionPreciosDialog.axaml.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/ConfirmacionPreciosDialogTests.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`, `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` (`DialogosV2`), `tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs`

**Interfaces:**
- Consumes: `ItemConfirmacionPrecioVm` (`Fila`, `ProductoNombre`, `PrecioActual`, `PrecioNuevo`, `Confirmado`), `FormatoEsUy.Moneda`.
- Produces: `public interface IConfirmacionPreciosDialogService { Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios); }`; `ConfirmacionPreciosDialog(IReadOnlyList<ItemConfirmacionPrecioVm>)` con partes `MensajeText`, `Lista` (`ScrollViewer`), `Filas` (`Grid`; fila 0 = encabezados, una fila por cambio con un `CheckBox` en la columna 0 y los precios en las columnas 2 y 4), `CancelarButton`, `ConfirmarButton`.

- [ ] **Step 1: Tests del diálogo real (rojo)**

```csharp
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Dialogs;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Confirmación de cambios de precio de costo (convención 25): el diálogo real, sin VM. Devuelve los tildes en el orden
/// de la lista, o null si se cancela.
/// </summary>
public class ConfirmacionPreciosDialogTests
{
    private static ItemConfirmacionPrecioVm Cambio(string nombre, decimal actual, decimal nuevo, bool tildado = false) =>
        new() { Fila = new FilaRenglonFacturaVm(), ProductoNombre = nombre, PrecioActual = actual, PrecioNuevo = nuevo, Confirmado = tildado };

    private static IReadOnlyList<ItemConfirmacionPrecioVm> Tres() =>
    [
        Cambio("Cemento portland 50 kg", 480m, 510m, tildado: true),
        Cambio("Arena gruesa", 890m, 920m),
        Cambio("Cable 2x1,5 mm", 38m, 41.5m),
    ];

    private static (ConfirmacionPreciosDialog Dialogo, Task<IReadOnlyList<bool>?> Resultado) Abrir(IReadOnlyList<ItemConfirmacionPrecioVm>? cambios = null)
    {
        var dialogo = new ConfirmacionPreciosDialog(cambios ?? Tres());
        var duenio = new Window();
        duenio.Show();
        var resultado = dialogo.ShowDialog<IReadOnlyList<bool>?>(duenio);
        Dispatcher.UIThread.RunJobs();
        return (dialogo, resultado);
    }

    private static T Parte<T>(Window d, string nombre) where T : Control => d.FindControl<T>(nombre)!;

    private static List<CheckBox> Tildes(Window d) => Parte<Grid>(d, "Filas").Children.OfType<CheckBox>().ToList();

    private static void Clic(Window d, string boton)
    {
        Parte<Button>(d, boton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Tecla(Window d, Control foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        d.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void MuestraCadaCambioConPreciosEnEsUy_ConCulturaAmbienteEnUs()
    {
        var anterior = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
        try
        {
            var (d, _) = Abrir([Cambio("Pala punta cuadrada", 1500.5m, 2000m)]);

            var textos = d.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("Pala punta cuadrada", textos);
            Assert.Contains("$ 1.500,50", textos);
            Assert.Contains("$ 2.000,00", textos);
        }
        finally
        {
            CultureInfo.CurrentCulture = anterior;
        }
    }

    [AvaloniaFact]
    public void LosTildesArrancanComoVienen() =>
        Assert.Equal(new bool?[] { true, false, false }, Tildes(Abrir().Dialogo).Select(t => t.IsChecked));

    [AvaloniaFact]
    public void Confirmar_DevuelveLosTildesEnOrden()
    {
        var (d, resultado) = Abrir();
        Tildes(d)[2].IsChecked = true;

        Clic(d, "ConfirmarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Equal(new[] { true, false, true }, resultado.Result);
    }

    [AvaloniaFact]
    public void Cancelar_DevuelveNull()
    {
        var (d, resultado) = Abrir();

        Clic(d, "CancelarButton");

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Escape_Cancela()
    {
        var (d, resultado) = Abrir();

        Tecla(d, Tildes(d)[1], PhysicalKey.Escape);

        Assert.True(resultado.IsCompleted);
        Assert.Null(resultado.Result);
    }

    [AvaloniaFact]
    public void Enter_ConElFocoEnUnTilde_ConfirmaSinCambiarlo()
    {
        var (d, resultado) = Abrir();

        Tecla(d, Tildes(d)[1], PhysicalKey.Enter);

        Assert.True(resultado.IsCompleted, "Enter sobre un tilde lo cambió y no confirmó.");
        Assert.Equal(new[] { true, false, false }, resultado.Result);
    }

    [AvaloniaFact]
    public void Abre_ConElFocoEnConfirmarYGuardar_YEnterConfirma()
    {
        var (d, resultado) = Abrir();

        Assert.Same(Parte<Button>(d, "ConfirmarButton"), d.FocusManager!.GetFocusedElement());
        d.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();

        Assert.True(resultado.IsCompleted);
    }

    [AvaloniaFact]
    public void Estructura_Ui2_UnSoloPrimario_YCancelarConfirmarConTeclado()
    {
        var d = new ConfirmacionPreciosDialog(Tres());

        var contenido = Assert.IsType<Border>(d.Content);
        Assert.Equal("Contenido", contenido.Name);
        Assert.Contains("ui2", contenido.Classes);
        Assert.Equal("Cambios de precio de costo", d.Title);
        Assert.Equal(520, d.Width);
        Assert.Equal(SizeToContent.Height, d.SizeToContent);
        Assert.False(d.CanResize);
        Assert.False(d.ShowInTaskbar);
        var primarios = contenido.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("primario")).ToList();
        Assert.Same(Parte<Button>(d, "ConfirmarButton"), Assert.Single(primarios));
        Assert.True(Parte<Button>(d, "CancelarButton").IsCancel);
        Assert.True(Parte<Button>(d, "ConfirmarButton").IsDefault);
        Assert.Equal("Confirmar y guardar", Parte<Button>(d, "ConfirmarButton").Content);
    }

    [AvaloniaFact]
    public void VeinticincoCambios_LaListaScrollea_YLosBotonesSiguenDentroDelDialogo()
    {
        var muchos = Enumerable.Range(1, 25).Select(i => Cambio($"Producto de prueba {i}", 100m, 100m + i)).ToList();
        var (d, _) = Abrir(muchos);

        var lista = Parte<ScrollViewer>(d, "Lista");
        Assert.True(lista.Extent.Height > lista.Viewport.Height + 0.5, "La lista no scrollea: el diálogo crece sin tope.");
        Assert.True(lista.Bounds.Height <= 320 + 0.5);
        var confirmar = Parte<Button>(d, "ConfirmarButton");
        var abajo = confirmar.TranslatePoint(new Point(0, confirmar.Bounds.Height), d)!.Value.Y;
        Assert.True(abajo <= d.Bounds.Height + 0.5, $"Confirmar termina en {abajo:F1} y el diálogo mide {d.Bounds.Height:F1}.");
    }

    [AvaloniaFact]
    public void NombreLargo_EnvuelveYNoSeSaleDelAncho()
    {
        var (d, _) = Abrir([Cambio("Caño de PVC reforzado de 110 mm por 4 metros con junta elástica y aro de goma", 650m, 700m)]);

        var tilde = Tildes(d)[0];
        var derecha = tilde.TranslatePoint(new Point(tilde.Bounds.Width, 0), Parte<Grid>(d, "Filas"))!.Value.X;
        var texto = tilde.GetVisualDescendants().OfType<TextBlock>().First();
        Assert.True(texto.Bounds.Height > 20, "El nombre largo no envolvió.");
        Assert.True(derecha <= Parte<Grid>(d, "Filas").Bounds.Width + 0.5);
    }

    [AvaloniaFact]
    public async Task Servicio_SinVentanaPrincipal_DevuelveNullSinAbrir() =>
        Assert.Null(await new ConfirmacionPreciosDialogService().ConfirmarCambiosAsync(Tres()));
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~ConfirmacionPreciosDialogTests"` → FAIL de compilación. Pegar. Después del Step 2, correr `Enter_ConElFocoEnUnTilde_…` con el `AddHandler` del túnel COMENTADO una vez y pegar el rojo (debe mostrar el tilde cambiado y el diálogo abierto): es la evidencia del comportamiento nativo que motiva el manejador; descomentar.

- [ ] **Step 2: Diálogo y servicio**

`ConfirmacionPreciosDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
        xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
        mc:Ignorable="d" d:DesignWidth="520" d:DesignHeight="360"
        x:Class="StockApp.Presentation.Views.Dialogs.ConfirmacionPreciosDialog"
        Title="Cambios de precio de costo"
        Width="520"
        SizeToContent="Height"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        ShowInTaskbar="False"
        Background="{DynamicResource V2SuperficieBrush}">

    <!-- UI v2 (spec §5.5). Modal de pantalla como Window con servicio. Sin bindings: las filas se arman en el
         code-behind con los cambios que recibe. La lista tiene tope de alto: con muchos cambios scrollea y los
         botones siguen a la vista. -->
    <Border x:Name="Contenido"
            Classes="ui2"
            Background="{DynamicResource V2SuperficieBrush}"
            Padding="24">
        <StackPanel Spacing="16">

            <TextBlock x:Name="MensajeText" Classes="cuerpo" TextWrapping="Wrap"
                       Text="Estos productos se cargaron con un precio distinto del costo actual. Tildá los que querés actualizar." />

            <ScrollViewer x:Name="Lista" MaxHeight="320" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
                <Grid x:Name="Filas" ColumnDefinitions="*,16,Auto,16,Auto" RowDefinitions="Auto">
                    <TextBlock Grid.Column="0" Classes="etiqueta-campo" Text="Producto" />
                    <TextBlock Grid.Column="2" Classes="etiqueta-campo" Text="Precio actual" HorizontalAlignment="Right" />
                    <TextBlock Grid.Column="4" Classes="etiqueta-campo" Text="Precio nuevo" HorizontalAlignment="Right" />
                </Grid>
            </ScrollViewer>

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,8,0,0">
                <Button x:Name="CancelarButton" Classes="secundario" Content="Cancelar" IsCancel="True" Click="OnCancelarClick" />
                <Button x:Name="ConfirmarButton" Classes="primario" Content="Confirmar y guardar" IsDefault="True" Click="OnConfirmarClick" />
            </StackPanel>

        </StackPanel>
    </Border>

</Window>
```

`ConfirmacionPreciosDialog.axaml.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using StockApp.Domain.Formato;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Confirmación de los cambios de precio de costo antes de guardar una factura. Una fila por cambio: tilde con el
/// nombre del producto y los dos precios en es-UY. "Confirmar y guardar" devuelve los tildes en el orden recibido;
/// "Cancelar", Escape o cerrar la ventana devuelven null. Usá <see cref="Window.ShowDialog{TResult}"/> con
/// TResult=IReadOnlyList&lt;bool&gt;?.
/// </summary>
public partial class ConfirmacionPreciosDialog : Window
{
    public ConfirmacionPreciosDialog()
    {
        InitializeComponent();
    }

    public ConfirmacionPreciosDialog(IReadOnlyList<ItemConfirmacionPrecioVm> cambios) : this()
    {
        for (var i = 0; i < cambios.Count; i++)
            AgregarFila(i + 1, cambios[i]);

        // Enter sobre un tilde: Avalonia lo trata como un click (cambia el tilde y marca el evento), así que el botón
        // por defecto nunca se entera. Se intercepta en túnel y se confirma; el tilde se cambia con Espacio.
        AddHandler(KeyDownEvent, EnterEnUnTildeConfirma, RoutingStrategies.Tunnel);
        Opened += (_, _) => ConfirmarButton.Focus();
    }

    private void AgregarFila(int fila, ItemConfirmacionPrecioVm cambio)
    {
        Filas.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var tilde = new CheckBox
        {
            IsChecked = cambio.Confirmado,
            Margin = new Thickness(0, 8, 0, 0),
            Content = new TextBlock { Text = cambio.ProductoNombre, TextWrapping = TextWrapping.Wrap },
        };
        Grid.SetRow(tilde, fila);
        Filas.Children.Add(tilde);
        Filas.Children.Add(Precio(cambio.PrecioActual, fila, 2));
        Filas.Children.Add(Precio(cambio.PrecioNuevo, fila, 4));
    }

    private static TextBlock Precio(decimal valor, int fila, int columna)
    {
        var texto = new TextBlock
        {
            Text = FormatoEsUy.Moneda(valor),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Grid.SetRow(texto, fila);
        Grid.SetColumn(texto, columna);
        return texto;
    }

    private IReadOnlyList<bool> TildesElegidos() =>
        Filas.Children.OfType<CheckBox>().Select(t => t.IsChecked == true).ToList();

    private void EnterEnUnTildeConfirma(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.Source is not CheckBox)
            return;
        e.Handled = true;
        Close(TildesElegidos());
    }

    private void OnConfirmarClick(object? sender, RoutedEventArgs e) => Close(TildesElegidos());

    private void OnCancelarClick(object? sender, RoutedEventArgs e) => Close(null);
}
```

Las cifras: confirmar en el GATE si los precios llevan cifras tabulares (`FontFeatures` con `tnum` y `-calt`, como las celdas numéricas); si el mockup las usa, el `TextBlock` de `Precio` lleva la misma clase que una celda `num` y se anota.

`IConfirmacionPreciosDialogService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abre la confirmación de cambios de precio de costo de Ingreso por factura. Devuelve un tilde por cambio, en el mismo
/// orden, o null si se cancela (o si no hay ventana donde mostrarla).
/// </summary>
public interface IConfirmacionPreciosDialogService
{
    Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios);
}
```

`ConfirmacionPreciosDialogService.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Dialogs;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>Implementación real: mismo mecanismo defensivo que RecalcularStockDialogService.</summary>
public class ConfirmacionPreciosDialogService : IConfirmacionPreciosDialogService
{
    public Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<IReadOnlyList<bool>?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => MostrarAsync(cambios));
    }

    private static async Task<IReadOnlyList<bool>?> MostrarAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        var owner = (AvaloniaApp.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null)
            return null;

        return await new ConfirmacionPreciosDialog(cambios).ShowDialog<IReadOnlyList<bool>?>(owner);
    }
}
```

Registro: `services.AddSingleton<IConfirmacionPreciosDialogService, ConfirmacionPreciosDialogService>();` junto al de la Task 4. `DialogosV2` gana `"StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml"`.

Run: `dotnet build StockApp.sln`; el comando del Step 1 → PASS (11).

- [ ] **Step 3: Comparación del diálogo**

```csharp
    private static Control ConfirmacionPrecios() =>
        ContenidoDeDialogo.Extraer(new ConfirmacionPreciosDialog(
        [
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Cemento portland 50 kg", PrecioActual = 480m, PrecioNuevo = 510m, Confirmado = true },
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Arena gruesa", PrecioActual = 890m, PrecioNuevo = 920m },
            new ItemConfirmacionPrecioVm { Fila = new FilaRenglonFacturaVm(), ProductoNombre = "Cable 2x1,5 mm", PrecioActual = 38m, PrecioNuevo = 41.5m },
        ]));

    [AvaloniaFact]
    public void ConfirmacionPrecios_IgualAlMockup() =>
        ComparacionVisual.CompararConMockup(ConfirmacionPrecios(), null, "dialogos/confirmacion-precios.html");

    [AvaloniaFact]
    public void ConfirmacionPrecios_IgualAlMockup_1366x768() =>
        ComparacionVisual.CompararConMockup(ConfirmacionPrecios(), null, "dialogos/confirmacion-precios.html", tamano: TamanoLienzo.Municipio);
```

Run: `scripts/ui-v2/comparar.sh ConfirmacionPrecios`. Expected: 2 PASS; anotar teselas; MIRAR.

- [ ] **Step 4: Mutaciones (QUITANDO)**

- (j) Quitar `IsCancel="True"` → rojo en `Escape_Cancela`. Restaurar.
- (k) Quitar `IsDefault="True"` → rojo en `Abre_ConElFocoEnConfirmarYGuardar_YEnterConfirma` si el foco no estuviera en el botón; si queda verde (Enter sobre el botón enfocado lo clickea de forma nativa), anotarlo y verificar el rojo en `Estructura_…`. Restaurar.
- (l) Quitar la línea `AddHandler(KeyDownEvent, EnterEnUnTildeConfirma, RoutingStrategies.Tunnel);` → rojo en `Enter_ConElFocoEnUnTilde_…`. Restaurar.
- (m) Quitar `MaxHeight="320"` → rojo en `VeinticincoCambios_…`. Restaurar.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/Services/IConfirmacionPreciosDialogService.cs src/StockApp.Presentation/Services/ConfirmacionPreciosDialogService.cs src/StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml src/StockApp.Presentation/Views/Dialogs/ConfirmacionPreciosDialog.axaml.cs src/StockApp.Presentation/App.axaml.cs tests/StockApp.Presentation.UiTests/V2/ConfirmacionPreciosDialogTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Dialogos/DialogosComparacionTests.cs
git commit -m "feat(ui-v2): diálogo v2 de confirmación de cambios de precio de costo"
```

### Task 6: El VM espera a los servicios y la vista v1 pierde los overlays

**Depende de:** Tasks 4 y 5 (servicios registrados: la pantalla sigue abriendo en la app después de este commit).

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs` (L39-41, L75-78, L106-131, L133-157, L239-269, L375-420)
- Modify: `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml` (v1 transitorio: L155, L245, L269-319)
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs` (`Crear` L20-80, L237, L269, L392-491)
- Modify: `tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs`, `IngresoPorFacturaViewTests.cs` (`Montar` L56-90, L144-155, L343-404, L641-686, L759-781, L796-848), `IngresoPorFacturaGrillaRenglonesTests.cs` (L82-84), `ReflexionVistaViewModelTests.cs` (L126-134)
- Modify: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs` (`Vm()`)

**Interfaces:**
- Consumes: `IAltaProductoRapidaDialogService` (Task 4), `IConfirmacionPreciosDialogService` (Task 5).
- Produces: constructor `IngresoPorFacturaViewModel(IIngresoPorFacturaService service, IProductoService productoService, ICategoriaService categoriaService, IUnidadMedidaService unidadMedidaService, IProveedorService proveedorService, IFuenteFinanciamientoService fuenteService, IRubroGastoService rubroService, ILineaPoaService lineaService, INavigationService navigation, IConfirmacionService confirmacion, AdjuntosPanelViewModel adjuntosPanel, IAltaProductoRapidaDialogService dialogoAlta, IConfirmacionPreciosDialogService dialogoPrecios)`; `AbrirAltaProductoCommand` (`IAsyncRelayCommand`); `public bool SolicitarFocoEnCantidad { get; set; }` (`[ObservableProperty]`); fakes `AltaProductoRapidaDialogServiceFake` (`Respuesta`, `Llamadas`, `CategoriasRecibidas`, `UnidadesRecibidas`) y `ConfirmacionPreciosDialogServiceFake` (`Responder`, `Pendiente`, `Llamadas`, `Recibidos`) en UiTests.

- [ ] **Step 1: Tests de VM (rojo)**

En `IngresoPorFacturaViewModelTests`, `Crear()` pasa a delegar en un helper nuevo que además devuelve los dos mocks (así las deconstrucciones existentes de 3 elementos no cambian):

```csharp
    private static (IngresoPorFacturaViewModel vm,
                    Mock<IIngresoPorFacturaService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var (vm, svc, nav, _, _) = CrearConDialogos();
        return (vm, svc, nav);
    }

    /// <summary>
    /// Los diálogos de alta y de precios se piden a servicios (convención 25): por defecto los dos devuelven null
    /// (cancelan). Un test que necesita otra respuesta hace Setup sobre el mock que devuelve.
    /// </summary>
    private static (IngresoPorFacturaViewModel vm,
                    Mock<IIngresoPorFacturaService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IAltaProductoRapidaDialogService> altaMock,
                    Mock<IConfirmacionPreciosDialogService> preciosMock)
        CrearConDialogos()
    {
        // Acá van, movidas sin cambios, las líneas L25-L72 del Crear() de hoy (desde "var svc = new Mock<IIngresoPorFacturaService>();"
        // hasta el cierre de "var adjuntosPanel = new AdjuntosPanelViewModel(…);"), con la categoría de abajo.
        var alta = new Mock<IAltaProductoRapidaDialogService>();
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync((ProductoNuevoDto?)null);
        var precios = new Mock<IConfirmacionPreciosDialogService>();
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync((IReadOnlyList<bool>?)null);

        var vm = new IngresoPorFacturaViewModel(
            svc.Object, productos.Object, categorias.Object, unidades.Object,
            proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, adjuntosPanel, alta.Object, precios.Object);

        return (vm, svc, nav, alta, precios);
    }
```

(El comentario de las líneas movidas es una instrucción de este plan: en el código no queda ese comentario, quedan las líneas.) `categorias` gana una categoría para poder mapear el alta: `categorias.Setup(c => c.ListarActivasAsync()).ReturnsAsync(new List<Categoria> { new() { Id = 3, Nombre = "Herramientas", Activo = true } });`. Los dos `new IngresoPorFacturaViewModel(` de L237 y L269 ganan `Mock.Of<IAltaProductoRapidaDialogService>(), Mock.Of<IConfirmacionPreciosDialogService>()` al final.

Tests nuevos y reescritos (los cuatro de L392-491 se REEMPLAZAN por estos con el mismo nombre o uno que diga lo mismo, convención 13):

```csharp
    [Fact]
    public async Task AbrirAltaProducto_PasaLasCategoriasYUnidadesYaCargadas()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await vm.InicializarAsync();

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        alta.Verify(a => a.PedirProductoNuevoAsync(
            It.Is<IReadOnlyList<Categoria>>(c => c.SequenceEqual(vm.CategoriasDisponibles)),
            It.Is<IReadOnlyList<UnidadMedida>>(u => u.SequenceEqual(vm.UnidadesMedidaDisponibles))), Times.Once);
    }

    [Fact]
    public async Task AbrirAltaProducto_Cancelada_LaZonaDeCargaQuedaIntacta()
    {
        var (vm, _, _, _, _) = CrearConDialogos();
        await vm.InicializarAsync();
        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        vm.CantidadEnCarga = 3m;
        vm.PrecioUnitarioEnCarga = 12m;

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        Assert.Same(vm.ProductosDisponibles[0], vm.ProductoEnCarga);
        Assert.False(vm.EsProductoNuevoEnCarga);
        Assert.Null(vm.NuevoProductoCodigo);
        Assert.Equal(3m, vm.CantidadEnCarga);
        Assert.False(vm.SolicitarFocoEnCantidad);
    }

    [Fact]
    public async Task AbrirAltaProducto_Confirmada_QuedaComoProductoNuevoEnCargaYPideElFocoEnCantidad()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await vm.InicializarAsync();
        vm.ProductoEnCarga = vm.ProductosDisponibles[0];
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1));

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);

        Assert.True(vm.EsProductoNuevoEnCarga);
        Assert.Null(vm.ProductoEnCarga);
        Assert.Equal("MAT-0601", vm.NuevoProductoCodigo);
        Assert.Equal("Carretilla de obra 90 L", vm.NuevoProductoNombre);
        Assert.Equal(3, vm.NuevaCategoriaSeleccionada!.Id);
        Assert.Equal(1, vm.NuevaUnidadSeleccionada!.Id);
        Assert.True(vm.SolicitarFocoEnCantidad);
    }

    [Fact]
    public async Task AltaEnLinea_NoDescartaLosRenglonesYaCargados()
    {
        var (vm, _, _, alta, _) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 2m, precioUnitario: 30m);
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("SKU-INLINE", "Producto cargado en línea", null, 1));

        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);
        vm.CantidadEnCarga = 1m;
        vm.PrecioUnitarioEnCarga = 40m;
        vm.AgregarArticuloCommand.Execute(null);

        Assert.Equal(2, vm.Renglones.Count);
        Assert.Equal(2m, vm.Renglones[0].Cantidad);
        Assert.Equal(30m, vm.Renglones[0].PrecioUnitario);
        var filaNueva = vm.Renglones[1];
        Assert.True(filaNueva.EsProductoNuevo);
        Assert.Equal("SKU-INLINE", filaNueva.ProductoNuevoCodigo);
        Assert.Equal("Producto cargado en línea", filaNueva.ProductoNuevoNombre);
        Assert.Null(filaNueva.ProductoNuevoCategoriaId);
        Assert.Equal(1, filaNueva.ProductoNuevoUnidadMedidaId);
        Assert.False(vm.EsProductoNuevoEnCarga);
    }

    [Fact]
    public async Task Guardar_ListaSoloLosProductosCuyoPrecioCostoDifiere()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.Is<IReadOnlyList<ItemConfirmacionPrecioVm>>(
            l => l.Count == 1 && l[0].PrecioActual == 10m && l[0].PrecioNuevo == 15m)), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_PreciosConfirmados_AplicaLosTildesYGuarda()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1, 2 }, 35m, 0m));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync(new[] { true, false });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 20m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.Renglones[0].ActualizarPrecioCosto);
        Assert.False(vm.Renglones[1].ActualizarPrecioCosto);
        Assert.True(vm.CambiosDePrecio[0].Confirmado);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(
            d => d.Renglones[0].ActualizarPrecioCosto && !d.Renglones[1].ActualizarPrecioCosto)), Times.Once);
        Assert.True(vm.GuardadoExitoso);
    }

    [Fact]
    public async Task Guardar_PreciosCancelados_NoGuardaYConservaTodo()
    {
        var (vm, svc, _, _, _) = CrearConDialogos();
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Never);
        Assert.False(vm.GuardadoExitoso);
        Assert.Single(vm.Renglones);
        Assert.Null(vm.MensajeError);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_SinCambiosDePrecio_NoPreguntaYGuardaDirecto()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 10m, 0m));
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 10m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()), Times.Never);
        Assert.True(vm.GuardadoExitoso);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Once);
    }

    [Fact]
    public async Task Guardar_MientrasLaConfirmacionEstaAbierta_NoSePuedeVolverAGuardar()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1 }, 15m, 0m));
        var abierta = new TaskCompletionSource<IReadOnlyList<bool>?>();
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).Returns(abierta.Task);
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        var primero = vm.GuardarCommand.ExecuteAsync(null);

        Assert.False(vm.GuardarCommand.CanExecute(null), "Con la confirmación abierta, Guardar sigue habilitado.");
        abierta.SetResult(new[] { true });
        await primero;
        precios.Verify(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()), Times.Once);
    }

    [Fact]
    public async Task Guardar_ProductoNuevoYCambioDePrecio_ConfirmaSoloElExistenteYElDtoLlevaLosDos()
    {
        var (vm, svc, _, alta, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>()))
            .ReturnsAsync(new IngresoPorFacturaResultadoDto(1, new List<int> { 1, 2 }, 95m, 0m));
        alta.Setup(a => a.PedirProductoNuevoAsync(It.IsAny<IReadOnlyList<Categoria>>(), It.IsAny<IReadOnlyList<UnidadMedida>>()))
            .ReturnsAsync(new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>()))
            .ReturnsAsync(new[] { true });
        await InicializarYCompletarCabeceraAsync(vm);
        await vm.AbrirAltaProductoCommand.ExecuteAsync(null);
        vm.CantidadEnCarga = 2m;
        vm.PrecioUnitarioEnCarga = 40m;
        vm.AgregarArticuloCommand.Execute(null);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        precios.Verify(p => p.ConfirmarCambiosAsync(It.Is<IReadOnlyList<ItemConfirmacionPrecioVm>>(
            l => l.Count == 1 && l[0].ProductoNombre == "Producto Uno")), Times.Once);
        svc.Verify(s => s.RegistrarAsync(It.Is<IngresoPorFacturaDto>(d =>
            d.Renglones.Count == 2
            && d.Renglones[0].ProductoNuevo == new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", 3, 1)
            && !d.Renglones[0].ActualizarPrecioCosto
            && d.Renglones[1].ProductoId == 1 && d.Renglones[1].ActualizarPrecioCosto)), Times.Once);
    }

    [Fact]
    public async Task Guardar_ServidorCaidoTrasConfirmar_MuestraElErrorYConservaTodo()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>())).ThrowsAsync(new ServidorNoDisponibleException());
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).ReturnsAsync(new[] { true });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);
        Assert.False(vm.GuardadoExitoso);
        Assert.Null(vm.GastoIdCreado);
        Assert.Single(vm.Renglones);
        Assert.True(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Guardar_403TrasConfirmar_MuestraElMensajeDeSesion()
    {
        var (vm, svc, _, _, precios) = CrearConDialogos();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<IngresoPorFacturaDto>())).ThrowsAsync(new UnauthorizedAccessException("403"));
        precios.Setup(p => p.ConfirmarCambiosAsync(It.IsAny<IReadOnlyList<ItemConfirmacionPrecioVm>>())).ReturnsAsync(new[] { false });
        await InicializarYCompletarCabeceraAsync(vm);
        AgregarArticulo(vm, vm.ProductosDisponibles[0], cantidad: 1m, precioUnitario: 15m);

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("La sesión expiró o no tiene permiso para registrar esta factura. Vuelva a iniciar sesión e intente de nuevo.",
            vm.MensajeError);
        Assert.False(vm.GuardadoExitoso);
        Assert.Single(vm.Renglones);
    }
```

("Producto Uno" es el `ProductoDto` id 1 de `Crear()`, L29; los usings que falten: `StockApp.ApiClient` ya está; `System.Linq` para `SequenceEqual`.)

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~IngresoPorFacturaViewModelTests"`
Expected: FAIL de compilación (el constructor no acepta 13 argumentos; `SolicitarFocoEnCantidad` no existe). Pegar.

- [ ] **Step 2: VM**

1. Campos y constructor: `private readonly IAltaProductoRapidaDialogService _dialogoAlta;` y `private readonly IConfirmacionPreciosDialogService _dialogoPrecios;` después de `_adjuntosPanel` (L40); los dos parámetros al final del constructor (después de `AdjuntosPanelViewModel adjuntosPanel`) y su asignación.
2. Borrar: `[NotifyPropertyChangedFor(nameof(GuardarEsAccionPrincipal))]` de `_guardadoExitoso` (L77); `_mostrandoAltaProducto` (L106-108); `_mostrandoConfirmacionPrecios` y `_montoConfirmadoPendiente` (L114-117); `GuardarEsAccionPrincipal` con su `<summary>` (L119-129); `ConfirmarAltaProductoCommand` y `ConfirmarAltaProducto` (L249-266); `CancelarAltaProducto` (L268-269); `ConfirmarPreciosYGuardarCommand`, `ConfirmarPreciosYGuardarAsync` y `CancelarConfirmacionPrecios` (L408-420).
3. Después de `_solicitarFocoEnProductoCombo` (L104):

```csharp
    /// <summary>Mismo "pulso" que SolicitarFocoEnProductoCombo: tras confirmar el alta de un producto nuevo, el foco va
    /// a Cantidad para seguir con el teclado (cantidad, precio y Enter agregan el renglón).</summary>
    [ObservableProperty] private bool _solicitarFocoEnCantidad;
```

4. `AbrirAltaProducto` (L239-247) se reemplaza por:

```csharp
    /// <summary>
    /// "Producto nuevo": pide los datos en el diálogo de alta rápida con las categorías y unidades ya cargadas. Si se
    /// confirma, la zona de carga pasa a "modo producto nuevo" (el renglón se crea recién con AgregarArticuloCommand,
    /// que lee NuevaCategoriaSeleccionada y NuevaUnidadSeleccionada) y el foco va a Cantidad. Si se cancela, la zona de
    /// carga queda exactamente como estaba.
    /// </summary>
    [RelayCommand]
    private async Task AbrirAltaProductoAsync()
    {
        var nuevo = await _dialogoAlta.PedirProductoNuevoAsync(CategoriasDisponibles.ToList(), UnidadesMedidaDisponibles.ToList());
        if (nuevo is null)
            return;

        NuevoProductoCodigo = nuevo.Codigo;
        NuevoProductoNombre = nuevo.Nombre;
        NuevaCategoriaSeleccionada = CategoriasDisponibles.FirstOrDefault(c => c.Id == nuevo.CategoriaId);
        NuevaUnidadSeleccionada = UnidadesMedidaDisponibles.First(u => u.Id == nuevo.UnidadMedidaId);
        EsProductoNuevoEnCarga = true;
        ProductoEnCarga = null;
        SolicitarFocoEnCantidad = true;
    }
```

5. En el `<summary>` de `DescartarProductoNuevoEnCarga` (L271-279), "Cancelar" del overlay de alta NO sirve acá…" pasa a "Cancelar el diálogo de alta no sirve acá: actúa ANTES de confirmar, y el atasco ocurre DESPUÉS.".
6. En `GuardarAsync` (L398-405), el bloque `if (CambiosDePrecio.Count > 0) { … }` se reemplaza por:

```csharp
        if (CambiosDePrecio.Count > 0)
        {
            // Mientras el diálogo está abierto, GuardarCommand sigue en ejecución y no se puede volver a disparar.
            var tildes = await _dialogoPrecios.ConfirmarCambiosAsync(CambiosDePrecio.ToList());
            if (tildes is null)
                return;
            for (var i = 0; i < CambiosDePrecio.Count; i++)
            {
                CambiosDePrecio[i].Confirmado = tildes[i];
                CambiosDePrecio[i].Fila.ActualizarPrecioCosto = tildes[i];
            }
        }
```

Run: el comando del Step 1. Expected: compila el proyecto de tests de VM recién con la vista v1 sin overlays (siguiente step: el XAML v1 bindea las propiedades borradas).

- [ ] **Step 3: Vista v1 sin overlays (transitorio hasta la Task 8)**

En `IngresoPorFacturaView.axaml`:
1. Borrar los dos overlays (L269-319, desde `<!-- Overlay: alta rápida de producto (Task 9) -->` hasta el cierre del segundo `Border`).
2. L245: `Classes.primary="{Binding GuardarEsAccionPrincipal}"` → `Classes.primary="{Binding !GuardadoExitoso}"` (mismo resultado sin overlays: Guardar es el único primario hasta que se guarda).
3. L155, en el `TextBox` de Cantidad: agregar `beh:FocoBehavior.SolicitarFoco="{Binding SolicitarFocoEnCantidad, Mode=TwoWay}"`.

Run: `dotnet build StockApp.sln` → 0 errores.

- [ ] **Step 4: Fakes y tests heredados de UI (convención 13)**

`IngresoPorFacturaFakes.cs`, al final:

```csharp
/// <summary>Alta rápida sin ventana: devuelve <see cref="Respuesta"/> (null = el usuario canceló) y registra lo recibido.</summary>
internal sealed class AltaProductoRapidaDialogServiceFake : IAltaProductoRapidaDialogService
{
    public ProductoNuevoDto? Respuesta { get; set; }
    public int Llamadas { get; private set; }
    public IReadOnlyList<Categoria>? CategoriasRecibidas { get; private set; }
    public IReadOnlyList<UnidadMedida>? UnidadesRecibidas { get; private set; }

    public Task<ProductoNuevoDto?> PedirProductoNuevoAsync(IReadOnlyList<Categoria> categorias, IReadOnlyList<UnidadMedida> unidades)
    {
        Llamadas++;
        CategoriasRecibidas = categorias;
        UnidadesRecibidas = unidades;
        return Task.FromResult(Respuesta);
    }
}

/// <summary>Confirmación de precios sin ventana: responde con <see cref="Responder"/> o deja la confirmación "abierta"
/// con <see cref="Pendiente"/>.</summary>
internal sealed class ConfirmacionPreciosDialogServiceFake : IConfirmacionPreciosDialogService
{
    public Func<IReadOnlyList<ItemConfirmacionPrecioVm>, IReadOnlyList<bool>?> Responder { get; set; } = _ => null;
    public TaskCompletionSource<IReadOnlyList<bool>?>? Pendiente { get; set; }
    public int Llamadas { get; private set; }
    public IReadOnlyList<ItemConfirmacionPrecioVm>? Recibidos { get; private set; }

    public Task<IReadOnlyList<bool>?> ConfirmarCambiosAsync(IReadOnlyList<ItemConfirmacionPrecioVm> cambios)
    {
        Llamadas++;
        Recibidos = cambios;
        return Pendiente?.Task ?? Task.FromResult(Responder(cambios));
    }
}
```

`IngresoPorFacturaViewTests`:
- `Montar` gana `AltaProductoRapidaDialogServiceFake? alta = null, ConfirmacionPreciosDialogServiceFake? precios = null` y los pasa al constructor (`alta ?? new()`, `precios ?? new()`); la tupla de retorno no cambia (los tests que los necesitan crean el fake y lo pasan).
- Borrar el helper `CamposDelOverlayDeAltaDeProducto` (L144-155): los campos viven en el diálogo.
- `ClickReal_ProductoNuevoDesdeLaZonaDeCarga_AlConfirmarYAgregar_LaGrillaMuestraElNombreLegible` (L343): con `new AltaProductoRapidaDialogServiceFake { Respuesta = new ProductoNuevoDto("COD-NUEVO-1", "Carretilla reforzada", null, 1) }`; reemplazar desde `Assert.True(vm.MostrandoAltaProducto);` hasta el click de Confirmar por `Assert.Equal(1, alta.Llamadas);`; el resto de los asserts no cambia.
- `ClickReal_CancelarAltaProducto_NoModificaLaZonaDeCargaYCierraElOverlay` (L392) → `ClickReal_AltaCancelada_NoModificaLaZonaDeCarga`: fake con `Respuesta = null`; click real en "Producto nuevo"; `Assert.Equal(1, alta.Llamadas); Assert.False(vm.EsProductoNuevoEnCarga); Assert.True(ArbolVisual.EsVisibleEnArbol(ComboPorItemsSource(window, vm.ProductosDisponibles)));`.
- `Guardar_ElServicioRechazaPorArgumentException_TrasConfirmarCambioDePrecio_ElErrorQuedaVisibleEnLaVista` (L641): fake de precios con `Responder = l => l.Select(_ => true).ToList()`; borrar el paso que clickeaba "Confirmar y guardar" del overlay; el assert del error visible no cambia.
- `ConfirmacionDeCambioDePrecio_MuestraLosPreciosEnEsUy` (L759): se BORRA; lo reemplaza `ConfirmacionPreciosDialogTests.MuestraCadaCambioConPreciosEnEsUy_ConCulturaAmbienteEnUs` (Task 5), que custodia lo mismo sobre el diálogo v2 real (anotar el reemplazo en el commit y en el ledger).
- `ClickReal_ProductoNuevoConfirmado_CambiarLoDescarta_…` (L796): fake de alta con la respuesta del producto nuevo en vez del overlay (L803).

`IngresoPorFacturaGrillaRenglonesTests.MontarConUnRenglon` (L82-84): `new AltaProductoRapidaDialogServiceFake(), new ConfirmacionPreciosDialogServiceFake()` al final del constructor.

`ReflexionVistaViewModelTests.IngresoPorFacturaViewModel_TodaPropiedadYComando_TieneUnControlEnLaVista` (L126-134), en `exclusiones`:

```csharp
            // Pasan al diálogo de alta o al de precios por su servicio (convención 25: modal de pantalla como Window
            // sin bindings), no tienen control propio en la vista. CambiosDePrecio lo recibe el diálogo de precios;
            // NuevoProductoCodigo y la categoría y unidad del alta van al renglón al agregar.
            nameof(IngresoPorFacturaViewModel.NuevoProductoCodigo),
            nameof(IngresoPorFacturaViewModel.NuevaCategoriaSeleccionada),
            nameof(IngresoPorFacturaViewModel.NuevaUnidadSeleccionada),
            nameof(IngresoPorFacturaViewModel.CategoriasDisponibles),
            nameof(IngresoPorFacturaViewModel.UnidadesMedidaDisponibles),
            nameof(IngresoPorFacturaViewModel.CambiosDePrecio),
```

`DatosIngresoFactura.Vm()`: el constructor gana `Mock.Of<IAltaProductoRapidaDialogService>(), Mock.Of<IConfirmacionPreciosDialogService>()` al final.

Run, en este orden: `dotnet build StockApp.sln`; `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`; `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter` con el filtro "Regresión de la pantalla"; `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosIngresoFactura"`. Expected: PASS (incluidos `ComandosDeGuardadoGuardianTests` y `ReflexionVistaViewModelTests`). Todo número de test heredado que cambie se anota con el antes, el después y el motivo.

- [ ] **Step 5: Mutaciones (QUITANDO)**

- (n) En `GuardarAsync`, quitar `if (tildes is null) return;` → rojo en `Guardar_PreciosCancelados_NoGuardaYConservaTodo` (NullReference o guarda). Restaurar.
- (o) En `GuardarAsync`, quitar la línea `CambiosDePrecio[i].Fila.ActualizarPrecioCosto = tildes[i];` → rojo en `Guardar_PreciosConfirmados_…` y `Guardar_ProductoNuevoYCambioDePrecio_…`. Restaurar.
- (p) En `AbrirAltaProductoAsync`, quitar `if (nuevo is null) return;` → rojo en `AbrirAltaProducto_Cancelada_…`. Restaurar.
- (q) Quitar `SolicitarFocoEnCantidad = true;` → rojo en `AbrirAltaProducto_Confirmada_…`. Restaurar.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Movimientos/IngresoPorFacturaViewModel.cs src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml tests/StockApp.Presentation.Tests/ViewModels/Movimientos/IngresoPorFacturaViewModelTests.cs tests/StockApp.Presentation.UiTests/IngresoPorFacturaFakes.cs tests/StockApp.Presentation.UiTests/IngresoPorFacturaViewTests.cs tests/StockApp.Presentation.UiTests/IngresoPorFacturaGrillaRenglonesTests.cs tests/StockApp.Presentation.UiTests/ReflexionVistaViewModelTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosIngresoFactura.cs
git commit -m "feat(ui-v2): Ingreso por factura pide el alta y la confirmación de precios a diálogos v2, sin overlays"
```

(`git status --short` antes del `add`: solo los archivos de esta task.)

### Task 7: Piezas de la pantalla: `TraerALaVista`, diferencia en advertencia

**Depende de:** Task 3 (D7 y D8 aprobadas; si el GATE descartó el scroll automático de D8, `TraerALaVista` no se crea y se anota).

**Files:**
- Create: `src/StockApp.Presentation/Behaviors/TraerALaVista.cs`, `src/StockApp.Presentation/Converters/DistintoDeCeroConverter.cs`
- Create: `tests/StockApp.Presentation.UiTests/V2/TraerALaVistaTests.cs`, `tests/StockApp.Presentation.Tests/Converters/DistintoDeCeroConverterTests.cs`
- Modify: `src/StockApp.Presentation/Themes/V2/ControlesV2.axaml` (después de `.ui2 TextBlock.titulo-seccion`, L34-38)
- Modify: el archivo de tests de estilos de texto v2 que fija `titulo-seccion` (ubicarlo con `grep -rln "titulo-seccion" tests/StockApp.Presentation.UiTests/V2`)

**Interfaces:**
- Produces: `public static class TraerALaVista { public static readonly AttachedProperty<bool> AlMostrarseProperty; Get/SetAlMostrarse(Control) }`; `public static class DistintoDeCeroConverter { public static readonly FuncValueConverter<decimal, bool> Instance; }`; estilo `.ui2 TextBlock.advertencia` (Foreground `V2InsigniaAdvertenciaTextoBrush`).

- [ ] **Step 1: Tests (rojo)**

`DistintoDeCeroConverterTests.cs`:

```csharp
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

public class DistintoDeCeroConverterTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(60, true)]
    [InlineData(-0.01, true)]
    public void Convierte(decimal valor, bool esperado) =>
        Assert.Equal(esperado, DistintoDeCeroConverter.Instance.Convert(valor, typeof(bool), null, CultureInfo.InvariantCulture));
}
```

`TraerALaVistaTests.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Threading;
using StockApp.Presentation.Behaviors;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>La card de adjuntos aparece al final de un formulario largo con el pie fijo: tiene que quedar a la vista.</summary>
public class TraerALaVistaTests
{
    private static (Window Ventana, ScrollViewer Scroll, Border Final) Montar(bool conComportamiento)
    {
        var final = new Border { Height = 120, IsVisible = false };
        if (conComportamiento)
            TraerALaVista.SetAlMostrarse(final, true);
        var scroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { new Border { Height = 2000 }, final } },
        };
        var ventana = new Window { Width = 400, Height = 300, Content = scroll };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        return (ventana, scroll, final);
    }

    private static bool EntraEnLaVista(ScrollViewer scroll, Control control)
    {
        var arriba = control.TranslatePoint(new Point(0, 0), scroll)!.Value.Y;
        return arriba >= -0.5 && arriba + control.Bounds.Height <= scroll.Viewport.Height + 0.5;
    }

    [AvaloniaFact]
    public void AlMostrarse_ElScrollLoTraeALaVista()
    {
        var (_, scroll, final) = Montar(conComportamiento: true);

        final.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.True(EntraEnLaVista(scroll, final), $"Offset {scroll.Offset.Y}: la card quedó fuera de la vista.");
    }

    [AvaloniaFact]
    public void Detector_SinElComportamiento_QuedaFueraDeLaVista()
    {
        var (_, scroll, final) = Montar(conComportamiento: false);

        final.IsVisible = true;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.False(EntraEnLaVista(scroll, final));
    }
}
```

Test de estilo, en el archivo de estilos de texto v2:

```csharp
    [AvaloniaFact]
    public void Advertencia_GanaSobreTituloDeSeccion()
    {
        var ventana = MontajeV2.Montar("""
            <TextBlock x:Name="Neutro" Classes="titulo-seccion" Text="$ 0,00" />
            <TextBlock x:Name="Aviso" Classes="titulo-seccion advertencia" Text="$ 60,00" />
            """);

        Assert.Equal(MontajeV2.Hex("#1C1917"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Neutro").Foreground));
        Assert.Equal(MontajeV2.Hex("#92400E"), MontajeV2.ColorDe(MontajeV2.Buscar<TextBlock>(ventana, "Aviso").Foreground));
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~DistintoDeCeroConverterTests"` y `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~TraerALaVistaTests|FullyQualifiedName~Advertencia_GanaSobreTituloDeSeccion"` → FAIL (compilación y color). Pegar. `Detector_SinElComportamiento_QuedaFueraDeLaVista` es el control positivo y tiene que dar verde siempre (sin el comportamiento la card queda fuera); si da rojo, el banco no detecta nada: PARAR.

- [ ] **Step 2: Implementación**

`DistintoDeCeroConverter.cs`:

```csharp
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>True si el importe no es cero: la Diferencia del pie de Ingreso por factura pasa a advertencia.</summary>
public static class DistintoDeCeroConverter
{
    public static readonly FuncValueConverter<decimal, bool> Instance = new(valor => valor != 0m);
}
```

`TraerALaVista.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace StockApp.Presentation.Behaviors;

/// <summary>
/// Al hacerse visible, el control pide que el ScrollViewer que lo contiene lo traiga a la vista. En Ingreso por factura
/// la card de adjuntos aparece al final de un formulario largo después de guardar: con el pie fijo y muchos renglones
/// quedaría debajo del pliegue y el usuario no sabría que tiene que adjuntar la factura.
/// </summary>
public static class TraerALaVista
{
    public static readonly AttachedProperty<bool> AlMostrarseProperty =
        AvaloniaProperty.RegisterAttached<Control, Control, bool>("AlMostrarse");

    static TraerALaVista()
    {
        AlMostrarseProperty.Changed.AddClassHandler<Control>((control, e) =>
        {
            control.PropertyChanged -= AlCambiarPropiedad;
            if (e.GetNewValue<bool>())
                control.PropertyChanged += AlCambiarPropiedad;
        });
    }

    public static bool GetAlMostrarse(Control control) => control.GetValue(AlMostrarseProperty);

    public static void SetAlMostrarse(Control control, bool valor) => control.SetValue(AlMostrarseProperty, valor);

    private static void AlCambiarPropiedad(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != Visual.IsVisibleProperty || !e.GetNewValue<bool>() || sender is not Control control)
            return;
        // Después del layout: recién ahí el control tiene tamaño y el ScrollViewer sabe hasta dónde ir.
        Dispatcher.UIThread.Post(control.BringIntoView, DispatcherPriority.Background);
    }
}
```

`ControlesV2.axaml`, inmediatamente después del estilo `.ui2 TextBlock.titulo-seccion`:

```xml
    <!-- Valor en advertencia (Diferencia del pie de Ingreso por factura). Va después de titulo-seccion: a igual
         especificidad gana el último. Mismo texto que la familia advertencia de la insignia y la alerta. -->
    <Style Selector=".ui2 TextBlock.advertencia">
        <Setter Property="Foreground" Value="{DynamicResource V2InsigniaAdvertenciaTextoBrush}" />
    </Style>
```

Run: los comandos del Step 1 → PASS; además `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj`, `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar` y `scripts/ui-v2/comparar.sh Catalogo` (el catálogo de componentes no cambia).

- [ ] **Step 3: Mutaciones (QUITANDO)**

- (r) Quitar la línea `Dispatcher.UIThread.Post(control.BringIntoView, DispatcherPriority.Background);` → rojo en `AlMostrarse_ElScrollLoTraeALaVista`. Restaurar.
- (s) Quitar el estilo `.ui2 TextBlock.advertencia` → rojo en `Advertencia_GanaSobreTituloDeSeccion`. Restaurar.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Presentation/Behaviors/TraerALaVista.cs src/StockApp.Presentation/Converters/DistintoDeCeroConverter.cs src/StockApp.Presentation/Themes/V2/ControlesV2.axaml tests/
git commit -m "feat(ui-v2): traer a la vista al mostrarse y valor en advertencia para el pie de Ingreso por factura"
```

(`git add tests/` solo después de `git status --short tests/` mostrando únicamente los archivos de esta task.)

### Task 8: Ingreso por factura v2: vista, pie fijo y comparaciones

**Depende de:** Tasks 1 a 7 (leer del ledger las D# cambiadas en el GATE).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml` (reescrita; el `.axaml.cs` NO cambia)
- Create: `tests/StockApp.Presentation.UiTests/V2/IngresoPorFacturaViewV2Tests.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/IngresoFacturaComparacionTests.cs`
- Modify (convención 13): `IngresoPorFacturaViewTests.cs`, `IngresoPorFacturaGrillaRenglonesTests.cs` (L111-196: `secondary` → `ghost`, valores en moneda), `GrillasSinRecorteBarridoTests.cs` (L87-92, L276-300), `GuardianDePatronTests.cs` (L48, L101, `VistasMigradasAV2` L206-221)

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: partes nombradas de `IngresoPorFacturaView`: `Raiz`, `Desplazamiento`, `Encabezado`, `Tarjeta`, `CampoProveedor`, `CampoNumeroFactura`, `CampoOrdenCompra`, `CampoFecha`, `CampoDetalle`, `CampoDestino`, `CampoFuente`, `CampoRubro`, `CampoLineaPoa`, `CheckCredito`, `CampoVencimiento`, `FilaCarga`, `ComboProducto`, `ProductoNuevoEnCarga`, `BotonCambiarProducto`, `CantidadEnCarga`, `PrecioEnCarga`, `CheckActualizarPrecio`, `BotonProductoNuevo`, `BotonAgregar`, `AlertaErrorCarga`, `EstadoSinRenglones`, `GrillaRenglones`, `TarjetaAdjuntos`, `PanelAdjuntos`, `AlertaError`, `Pie`, `ValorSuma`, `TotalFactura`, `ValorDiferencia`, `BotonCancelar`, `BotonGuardar`, `BotonFinalizar`; `DatosIngresoFactura.OcultarPanelAdjuntosV1(Control)`.

- [ ] **Step 1: UiTests de la vista (rojo)**

`IngresoPorFacturaViewV2Tests.cs`:

```csharp
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
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// Ingreso por factura v2 con la View real: tres secciones, un solo primario (Guardar), Enter en la fila de carga,
/// pie fijo fuera del scroll, diferencia en advertencia, foco tras el alta y card de adjuntos a la vista.
/// </summary>
public class IngresoPorFacturaViewV2Tests
{
    private static readonly UnidadMedida Unidad = new() { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };

    private static ProductoDto P(int id, string nombre, decimal costo = 100m) => new(
        id, $"COD{id}", null, nombre, null, null, null, null, 1, "Unidad", costo, 10m, 1m, true, DateTime.Today);

    private sealed record Montaje(Window Ventana, IngresoPorFacturaView Vista, IngresoPorFacturaViewModel Vm,
        IngresoPorFacturaServiceFake Servicio, AltaProductoRapidaDialogServiceFake Alta, ConfirmacionPreciosDialogServiceFake Precios,
        NavigationRecorderFake Nav);

    private static Montaje Montar(double ancho = 1040, double alto = 760)
    {
        var (vm, servicio, alta, precios, nav) = CrearVm();
        var vista = new IngresoPorFacturaView();
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        ventana.Show();
        vista.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, alta, precios, nav);
    }

    private static (IngresoPorFacturaViewModel Vm, IngresoPorFacturaServiceFake Servicio, AltaProductoRapidaDialogServiceFake Alta,
        ConfirmacionPreciosDialogServiceFake Precios, NavigationRecorderFake Nav) CrearVm()
    {
        var servicio = new IngresoPorFacturaServiceFake();
        var alta = new AltaProductoRapidaDialogServiceFake();
        var precios = new ConfirmacionPreciosDialogServiceFake();
        var confirmacion = new ConfirmacionServiceFake();
        var adjuntos = new AdjuntosPanelViewModel(new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(),
            new ServicioAperturaArchivoFake(), confirmacion, new SesionFake(RolUsuario.Admin));
        var vm = new IngresoPorFacturaViewModel(servicio,
            new ProductoServiceIngresoFake(new[] { P(1, "Cemento portland 50 kg", 480m), P(2, "Arena gruesa", 890m) }),
            new CategoriaServiceFake(Array.Empty<Categoria>()), new UnidadMedidaServiceFake(new[] { Unidad }),
            new ProveedorServiceFake(new[] { new Proveedor { Id = 1, Nombre = "Barraca Carmelo S.A.", Activo = true } }),
            new FuenteFinanciamientoServiceFake(new[] { new FuenteFinanciamiento { Id = 1, Nombre = "Rentas generales", Activo = true } }),
            new RubroGastoServiceFake(new[] { new RubroGasto { Id = 1, Codigo = 231, Nombre = "Materiales", Activo = true } }),
            new LineaPoaServiceFake(Array.Empty<LineaPoa>()),
            nav, confirmacion, adjuntos, alta, precios);
        return (vm, servicio, alta, precios, nav);
    }

    private static T N<T>(Montaje m, string nombre) where T : Control =>
        m.Vista.GetVisualDescendants().OfType<T>().First(c => c.Name == nombre);

    private static IInputElement? Foco(Montaje m) => TopLevel.GetTopLevel(m.Vista)!.FocusManager!.GetFocusedElement();

    private static void Tecla(Montaje m, Control foco, PhysicalKey tecla)
    {
        foco.Focus();
        Dispatcher.UIThread.RunJobs();
        m.Ventana.KeyPressQwerty(tecla, RawInputModifiers.None);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Clickear(Montaje m, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var centro = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), m.Ventana)!.Value;
        m.Ventana.MouseMove(centro);
        m.Ventana.MouseDown(centro, MouseButton.Left);
        m.Ventana.MouseUp(centro, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void CompletarCabecera(IngresoPorFacturaViewModel vm, string total = "4.800,00")
    {
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Materiales para la reparación de la rambla";
        vm.MontoTotalTexto = total;
    }

    private static void Renglones(IngresoPorFacturaViewModel vm, int cuantos)
    {
        for (var i = 0; i < cuantos; i++)
        {
            vm.ProductoEnCarga = vm.ProductosDisponibles[i % 2];
            vm.CantidadEnCarga = 1m + i;
            vm.PrecioUnitarioEnCarga = vm.ProductosDisponibles[i % 2].PrecioCosto;
            vm.AgregarArticuloCommand.Execute(null);
        }
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static double Abajo(Control c, Visual respecto) => c.TranslatePoint(new Point(0, c.Bounds.Height), respecto)!.Value.Y;
    private static double Arriba(Control c, Visual respecto) => c.TranslatePoint(new Point(0, 0), respecto)!.Value.Y;

    [AvaloniaFact]
    public void Raiz_TieneUi2_EncabezadoSinBreadcrumb_YTresSecciones()
    {
        var m = Montar();

        Assert.Contains("ui2", m.Vista.Classes);
        var encabezado = N<EncabezadoPagina>(m, "Encabezado");
        Assert.Equal("Ingreso por factura", encabezado.Titulo);
        Assert.Equal("Cargá la factura del proveedor y los artículos que entran al stock.", encabezado.Subtitulo);
        Assert.Null(encabezado.BreadcrumbOrigen);
        var secciones = m.Vista.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.Classes.Contains("subtitulo-formulario") && ArbolVisual.EsVisibleEnArbol(t)).Select(t => t.Text);
        Assert.Equal(new[] { "Datos de la factura", "Cargar artículo", "Artículos cargados" }, secciones);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Control>(), c => c.GetType().Name is "HeaderVista" or "BadgeEstado");
    }

    [AvaloniaFact]
    public void UnSoloPrimarioEnTodaLaVista_YEsGuardar_SinIsDefault()
    {
        var m = Montar();

        var botones = m.Vista.GetVisualDescendants().OfType<Button>().Where(ArbolVisual.EsVisibleEnArbol).ToList();
        var primario = Assert.Single(botones, b => b.Classes.Contains("primario"));
        Assert.Same(N<Button>(m, "BotonGuardar"), primario);
        Assert.Contains("secundario", N<Button>(m, "BotonAgregar").Classes);
        Assert.Contains("secundario", N<Button>(m, "BotonProductoNuevo").Classes);
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.IsDefault);
    }

    [AvaloniaFact]
    public void TrasGuardar_ElUnicoPrimarioEsFinalizar_YLaCardQuedaDeshabilitada()
    {
        var m = Montar();
        CompletarCabecera(m.Vm, "480,00");
        Renglones(m.Vm, 1);

        Clickear(m, N<Button>(m, "BotonGuardar"));

        var botones = m.Vista.GetVisualDescendants().OfType<Button>().Where(ArbolVisual.EsVisibleEnArbol).ToList();
        Assert.Same(N<Button>(m, "BotonFinalizar"), Assert.Single(botones, b => b.Classes.Contains("primario")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(N<Button>(m, "BotonGuardar")));
        Assert.False(ArbolVisual.EsVisibleEnArbol(N<Button>(m, "BotonCancelar")));
        Assert.False(N<TarjetaContenido>(m, "Tarjeta").IsEffectivelyEnabled);
        Assert.True(ArbolVisual.EsVisibleEnArbol(N<TarjetaContenido>(m, "TarjetaAdjuntos")));
    }

    [AvaloniaFact]
    public void Requeridos_SoloLoQueExigeGuardar()
    {
        var m = Montar();

        foreach (var campo in new[] { "CampoProveedor", "CampoDetalle", "CampoFuente", "CampoRubro" })
            Assert.True(N<CampoFormulario>(m, campo).Requerido, $"{campo} sin asterisco.");
        foreach (var campo in new[] { "CampoNumeroFactura", "CampoOrdenCompra", "CampoFecha", "CampoDestino", "CampoLineaPoa" })
            Assert.False(N<CampoFormulario>(m, campo).Requerido, $"{campo} con asterisco y es opcional.");
    }

    [AvaloniaFact]
    public void Vencimiento_SoloConCompraACredito()
    {
        var m = Montar();
        Assert.False(ArbolVisual.EsVisibleEnArbol(N<CampoFormulario>(m, "CampoVencimiento")));

        m.Vm.EsCredito = true;
        Dispatcher.UIThread.RunJobs();

        Assert.True(ArbolVisual.EsVisibleEnArbol(N<CampoFormulario>(m, "CampoVencimiento")));
    }

    [AvaloniaTheory]
    [InlineData("CantidadEnCarga")]
    [InlineData("PrecioEnCarga")]
    public void Enter_EnCantidadYEnPrecio_AgregaYElFocoVuelveAlProducto(string campo)
    {
        var m = Montar();
        m.Vm.ProductoEnCarga = m.Vm.ProductosDisponibles[0];
        N<TextBox>(m, "CantidadEnCarga").Text = "40";
        N<TextBox>(m, "PrecioEnCarga").Text = "480";
        Dispatcher.UIThread.RunJobs();

        Tecla(m, N<TextBox>(m, campo), PhysicalKey.Enter);

        var fila = Assert.Single(m.Vm.Renglones);
        Assert.Equal(40m, fila.Cantidad);
        Assert.Same(N<ComboBox>(m, "ComboProducto"), Foco(m));
    }

    [AvaloniaFact]
    public void Enter_EnProductoConValor_Agrega()
    {
        var m = Montar();
        m.Vm.ProductoEnCarga = m.Vm.ProductosDisponibles[1];
        m.Vm.CantidadEnCarga = 6m;
        m.Vm.PrecioUnitarioEnCarga = 890m;
        Dispatcher.UIThread.RunJobs();

        Tecla(m, N<ComboBox>(m, "ComboProducto"), PhysicalKey.Enter);

        Assert.Single(m.Vm.Renglones);
    }

    [AvaloniaFact]
    public void Enter_EnProductoVacio_NoAgregaNiMuestraError()
    {
        var m = Montar();

        Tecla(m, N<ComboBox>(m, "ComboProducto"), PhysicalKey.Enter);

        Assert.Empty(m.Vm.Renglones);
        Assert.Null(m.Vm.MensajeErrorCarga);
        Assert.False(ArbolVisual.EsVisibleEnArbol(N<Alerta>(m, "AlertaErrorCarga")));
    }

    [AvaloniaFact]
    public void Enter_ConCantidadInvalida_NoAgregaConElValorViejo()
    {
        var m = Montar();
        m.Vm.ProductoEnCarga = m.Vm.ProductosDisponibles[0];
        m.Vm.CantidadEnCarga = 2m;
        m.Vm.PrecioUnitarioEnCarga = 480m;
        Dispatcher.UIThread.RunJobs();
        var cantidad = N<TextBox>(m, "CantidadEnCarga");
        cantidad.Focus();
        cantidad.Text = "5.4";
        Dispatcher.UIThread.RunJobs();

        Tecla(m, cantidad, PhysicalKey.Enter);

        Assert.Empty(m.Vm.Renglones);
    }

    [AvaloniaFact]
    public void Enter_EnLaFilaDeCarga_NuncaGuarda()
    {
        var m = Montar();
        CompletarCabecera(m.Vm, "480,00");
        Renglones(m.Vm, 1);
        Assert.True(m.Vm.GuardarCommand.CanExecute(null), "Precondición: Guardar podría ejecutarse.");

        Tecla(m, N<TextBox>(m, "CantidadEnCarga"), PhysicalKey.Enter);
        Tecla(m, N<TextBox>(m, "PrecioEnCarga"), PhysicalKey.Enter);
        Tecla(m, N<ComboBox>(m, "ComboProducto"), PhysicalKey.Enter);

        Assert.Empty(m.Servicio.Registrados);
        Assert.Equal(0, m.Precios.Llamadas);
        Assert.False(m.Vm.GuardadoExitoso);
    }

    [AvaloniaFact]
    public void ProductoNuevo_ClickReal_PideElAltaConLasListasCargadas()
    {
        var m = Montar();

        Clickear(m, N<Button>(m, "BotonProductoNuevo"));

        Assert.Equal(1, m.Alta.Llamadas);
        Assert.Equal(m.Vm.UnidadesMedidaDisponibles, m.Alta.UnidadesRecibidas);
    }

    [AvaloniaFact]
    public void AltaConfirmada_MuestraElNombre_YElFocoVaACantidad()
    {
        var m = Montar();
        m.Alta.Respuesta = new ProductoNuevoDto("MAT-0601", "Carretilla de obra 90 L", null, 1);

        Clickear(m, N<Button>(m, "BotonProductoNuevo"));

        Assert.False(ArbolVisual.EsVisibleEnArbol(N<ComboBox>(m, "ComboProducto")));
        Assert.True(ArbolVisual.EsVisibleEnArbol(N<Control>(m, "ProductoNuevoEnCarga")));
        Assert.Contains(m.Vista.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Carretilla de obra 90 L" && ArbolVisual.EsVisibleEnArbol(t));
        Assert.Same(N<TextBox>(m, "CantidadEnCarga"), Foco(m));
    }

    [AvaloniaFact]
    public void AltaCancelada_LaFilaDeCargaQuedaIgual()
    {
        var m = Montar();
        m.Vm.ProductoEnCarga = m.Vm.ProductosDisponibles[0];
        Dispatcher.UIThread.RunJobs();

        Clickear(m, N<Button>(m, "BotonProductoNuevo"));

        Assert.True(ArbolVisual.EsVisibleEnArbol(N<ComboBox>(m, "ComboProducto")));
        Assert.Same(m.Vm.ProductosDisponibles[0], N<ComboBox>(m, "ComboProducto").SelectedItem);
    }

    [AvaloniaFact]
    public void Diferencia_EnCeroEsNeutra_YDistintaUsaAdvertencia()
    {
        var m = Montar();
        CompletarCabecera(m.Vm, "480,00");
        Renglones(m.Vm, 1);
        var valor = N<TextBlock>(m, "ValorDiferencia");
        Assert.Equal("$ 0,00", valor.Text);
        Assert.Equal(MontajeV2.Hex("#1C1917"), MontajeV2.ColorDe(valor.Foreground));

        m.Vm.MontoTotalTexto = "540,00";
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("$ 60,00", valor.Text);
        Assert.Equal(MontajeV2.Hex("#92400E"), MontajeV2.ColorDe(valor.Foreground));
        Assert.True(m.Vm.GuardarCommand.CanExecute(null), "La diferencia no bloquea el guardado.");
    }

    [AvaloniaTheory]
    [InlineData(1040, 760)]
    [InlineData(1126, 768)]
    [InlineData(784, 600)]
    public void Pie_ConCincuentaRenglones_QuedaFijoAbajoYGuardarSeVe(double ancho, double alto)
    {
        var m = Montar(ancho, alto);
        CompletarCabecera(m.Vm);
        Renglones(m.Vm, 50);

        var pie = N<Border>(m, "Pie");
        var scroll = N<ScrollViewer>(m, "Desplazamiento");
        Assert.False(scroll.IsVisualAncestorOf(pie), "El pie quedó dentro del scroll.");
        Assert.Equal(m.Vista.Bounds.Height, Abajo(pie, m.Vista), 0.5);
        Assert.True(scroll.Extent.Height > scroll.Viewport.Height + 0.5, "Con 50 renglones la página no scrollea.");
        var guardar = N<Button>(m, "BotonGuardar");
        Assert.True(Arriba(guardar, m.Ventana) >= 0 && Abajo(guardar, m.Ventana) <= alto + 0.5, "Guardar quedó fuera de la ventana.");
        var derecha = guardar.TranslatePoint(new Point(guardar.Bounds.Width, 0), m.Ventana)!.Value.X;
        Assert.True(derecha <= ancho + 0.5, $"Guardar termina en {derecha:F1} con la ventana de {ancho}.");
    }

    [AvaloniaFact]
    public void ErrorDeGuardado_ConCincuentaRenglones_LaAlertaSeVeArribaDelPie()
    {
        var m = Montar();
        CompletarCabecera(m.Vm);
        Renglones(m.Vm, 50);
        m.Servicio.ExcepcionARelanzar = new ServidorNoDisponibleException();

        Clickear(m, N<Button>(m, "BotonGuardar"));

        var alerta = N<Alerta>(m, "AlertaError");
        Assert.True(ArbolVisual.EsVisibleEnArbol(alerta));
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, alerta.Texto);
        Assert.True(Abajo(alerta, m.Vista) <= Arriba(N<Border>(m, "Pie"), m.Vista) + 0.5, "La alerta queda debajo del pie.");
        Assert.False(N<ScrollViewer>(m, "Desplazamiento").IsVisualAncestorOf(alerta), "La alerta scrollea con el contenido.");
        Assert.Equal(50, m.Vm.Renglones.Count);
    }

    [AvaloniaFact]
    public void Guardado_LaCardDeAdjuntosQuedaALaVista()
    {
        var m = Montar();
        CompletarCabecera(m.Vm);
        Renglones(m.Vm, 30);
        var scroll = N<ScrollViewer>(m, "Desplazamiento");
        scroll.Offset = new Vector(0, 0);
        Dispatcher.UIThread.RunJobs();

        Clickear(m, N<Button>(m, "BotonGuardar"));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var card = N<TarjetaContenido>(m, "TarjetaAdjuntos");
        var arriba = Arriba(card, scroll);
        Assert.True(arriba >= -0.5 && arriba < scroll.Viewport.Height, $"La card de adjuntos quedó en y={arriba:F0}, fuera de la vista.");
    }

    [AvaloniaFact]
    public void SinRenglones_MuestraElVacio_YConUnoLaGrilla()
    {
        var m = Montar();
        var vacio = N<EstadoVacio>(m, "EstadoSinRenglones");
        Assert.True(ArbolVisual.EsVisibleEnArbol(vacio));
        Assert.Equal("Todavía no cargaste artículos", vacio.Titulo);
        Assert.False(ArbolVisual.EsVisibleEnArbol(N<DataGrid>(m, "GrillaRenglones")));

        Renglones(m.Vm, 1);

        Assert.False(ArbolVisual.EsVisibleEnArbol(vacio));
        Assert.True(ArbolVisual.EsVisibleEnArbol(N<DataGrid>(m, "GrillaRenglones")));
    }

    [AvaloniaFact]
    public void Grilla_ColumnasEnMayusculas_ImportesEnEsUy_YQuitarGhost()
    {
        var m = Montar();
        m.Vm.ProductoEnCarga = m.Vm.ProductosDisponibles[0];
        m.Vm.CantidadEnCarga = 1500.5m;
        m.Vm.PrecioUnitarioEnCarga = 1234.5m;
        m.Vm.ActualizarPrecioCostoEnCarga = true;
        m.Vm.AgregarArticuloCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var grilla = N<DataGrid>(m, "GrillaRenglones");
        Assert.Equal(new object?[] { "PRODUCTO", "CANTIDAD", "PRECIO UNITARIO", "SUBTOTAL", "ACT. COSTO", null },
            grilla.Columns.Select(c => c.Header));
        var textos = grilla.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("1.500,5", textos);
        Assert.Contains("$ 1.234,50", textos);
        Assert.Contains("$ 1.852.367,25", textos);
        var quitar = grilla.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Quitar"));
        Assert.Contains("ghost", quitar.Classes);
        Assert.Contains(grilla.GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "Sí" && ArbolVisual.EsVisibleEnArbol(i));

        Clickear(m, quitar);

        Assert.Empty(m.Vm.Renglones);
    }

    [AvaloniaFact]
    public void CancelarYFinalizar_VuelvenAGastosYFacturas_ComoHoy()
    {
        var m = Montar();
        Clickear(m, N<Button>(m, "BotonCancelar"));
        Assert.Equal(typeof(GastosViewModel), m.Nav.UltimoTipoNavegado);

        var g = Montar();
        CompletarCabecera(g.Vm, "480,00");
        Renglones(g.Vm, 1);
        Clickear(g, N<Button>(g, "BotonGuardar"));
        Clickear(g, N<Button>(g, "BotonFinalizar"));
        Assert.Equal(typeof(GastosViewModel), g.Nav.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public async Task OperadorMixtoConLosCuatroPermisos_EnElShell_VeLaPantallaV2_YAlPerderGastosElItemDesaparece()
    {
        var sesion = new SesionFake(RolUsuario.Operador, Permisos.RegistrarMovimientos, Permisos.RegistrarGastos,
            Permisos.VerFinanzas, Permisos.GestionarProductos);
        var tras = new HashSet<string> { Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarProductos };
        var shellVm = new ShellMainViewModel(sesion, new NavigationServiceFake(), new InfoAppFake(), new ConfirmacionServiceFake(),
            new AuthServiceFake(sesion, tras), new PreferenciasSidebarFake());
        shellVm.CurrentContent = CrearVm().Vm;
        var ventana = AvaloniaRuntimeXamlLoader.Parse<Window>("""
            <Window xmlns="https://github.com/avaloniaui"
                    xmlns:vistas="clr-namespace:StockApp.Presentation.Views;assembly=GestionMunicipal"
                    Width="1280" Height="760"><vistas:ShellMainView /></Window>
            """, typeof(TestApp).Assembly);
        ventana.DataTemplates.Add(new ViewLocator());
        ventana.DataContext = shellVm;
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        var vista = ventana.GetVisualDescendants().OfType<IngresoPorFacturaView>().Single();
        Assert.Contains("ui2", vista.Classes);
        Control Item() => ventana.GetVisualDescendants().OfType<Control>()
            .First(c => c is ICommandSource fuente && ReferenceEquals(fuente.Command, shellVm.NavIngresoPorFacturaCommand));
        Assert.True(ArbolVisual.EsVisibleEnArbol(Item()), "Precondición: con los cuatro permisos el ítem se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras),
            new ConfirmacionServiceFake(), shellVm.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ArbolVisual.EsVisibleEnArbol(Item()));
    }
}
```

Antes de escribirlo, confirmar con CodeGraph: cómo ubica `ShellMainViewGatesTests.EsVisible` el ítem del sidebar por identidad de `Command` (L232-250; si es `ItemSidebar` con otra propiedad que `ICommandSource.Command`, copiar ese helper) y que `ShellMainView` vive en `StockApp.Presentation.Views`; los nombres de fakes del shell (`NavigationServiceFake`, `InfoAppFake`, `AuthServiceFake(sesion, tras)`, `PreferenciasSidebarFake`) son los de `ProductoListViewV2Tests` L834-866. El subtotal 1500,5 × 1234,5 = 1.852.367,25.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~IngresoPorFacturaViewV2Tests"` → FAIL (la vista es v1: faltan las partes nombradas). Pegar el resumen de rojos.

- [ ] **Step 2: `IngresoPorFacturaView.axaml` v2**

Reescribir completo (las medidas de columnas y la línea de D4 son las del GATE; si el GATE cambió una D#, aplicarla):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Movimientos"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:cat="using:StockApp.Application.Catalogo"
             xmlns:ent="using:StockApp.Domain.Entities"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Movimientos.IngresoPorFacturaView"
             x:DataType="vm:IngresoPorFacturaViewModel"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §5.2). Espejo: docs/ui-v2/mockups/inventario/ingreso-factura*.html. Pantalla del sidebar: sin
         breadcrumb. El pie con los totales y Guardar queda FIJO abajo, fuera del scroll, igual que el error general:
         con muchos renglones siguen a la vista. Un solo primario (Guardar) y ningún botón por defecto: Enter en la
         fila de carga agrega el artículo y nunca guarda. Los modales (alta rápida, confirmación de precios) son
         ventanas que abren servicios, no capas sobre esta vista. -->
    <Grid x:Name="Raiz" RowDefinitions="*,Auto,Auto">

        <ScrollViewer x:Name="Desplazamiento" Grid.Row="0" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
            <StackPanel Margin="28,24">

                <v2:EncabezadoPagina x:Name="Encabezado"
                                     Titulo="Ingreso por factura"
                                     Subtitulo="Cargá la factura del proveedor y los artículos que entran al stock." />

                <v2:TarjetaContenido x:Name="Tarjeta" Padding="24" IsEnabled="{Binding !GuardadoExitoso}">
                    <StackPanel>

                        <TextBlock Classes="subtitulo-formulario" Text="Datos de la factura" Margin="0,0,0,12" />
                        <!-- Tres por fila, por afinidad: identificación, fecha y destino, imputación presupuestal. El 16
                             entre filas va en el Margin superior de cada campo: el vencimiento aparece y desaparece sin
                             dejar hueco. -->
                        <Grid ColumnDefinitions="*,16,*,16,*" RowDefinitions="Auto,Auto,Auto,Auto">
                            <v2:CampoFormulario x:Name="CampoProveedor" Grid.Row="0" Grid.Column="0" Etiqueta="Proveedor" Requerido="True">
                                <ComboBox ItemsSource="{Binding ProveedoresDisponibles}" SelectedItem="{Binding ProveedorSeleccionado}"
                                          PlaceholderText="Elegí el proveedor" HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:Proveedor"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoNumeroFactura" Grid.Row="0" Grid.Column="2" Etiqueta="N.º factura">
                                <TextBox Text="{Binding NumeroFactura}" PlaceholderText="Ej.: A-0001234" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoOrdenCompra" Grid.Row="0" Grid.Column="4" Etiqueta="Orden de compra">
                                <TextBox Text="{Binding NumeroOrden}" PlaceholderText="Ej.: OC-77" />
                            </v2:CampoFormulario>

                            <v2:CampoFormulario x:Name="CampoFecha" Grid.Row="1" Grid.Column="0" Margin="0,16,0,0" Etiqueta="Fecha">
                                <CalendarDatePicker SelectedDate="{Binding FechaSeleccionada}" HorizontalAlignment="Stretch"
                                                    PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom" CustomDateFormatString="dd/MM/yyyy"
                                                    beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoDetalle" Grid.Row="1" Grid.Column="2" Margin="0,16,0,0" Etiqueta="Detalle" Requerido="True">
                                <TextBox Text="{Binding Detalle}" PlaceholderText="Ej.: Materiales para la rambla" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoDestino" Grid.Row="1" Grid.Column="4" Margin="0,16,0,0" Etiqueta="Destino">
                                <TextBox Text="{Binding Destino}" PlaceholderText="Ej.: Corralón municipal" />
                            </v2:CampoFormulario>

                            <v2:CampoFormulario x:Name="CampoFuente" Grid.Row="2" Grid.Column="0" Margin="0,16,0,0" Etiqueta="Fuente de financiamiento" Requerido="True">
                                <ComboBox ItemsSource="{Binding FuentesDisponibles}" SelectedItem="{Binding FuenteSeleccionada}"
                                          PlaceholderText="Elegí la fuente" HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:FuenteFinanciamiento"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoRubro" Grid.Row="2" Grid.Column="2" Margin="0,16,0,0" Etiqueta="Rubro de gasto" Requerido="True">
                                <ComboBox ItemsSource="{Binding RubrosDisponibles}" SelectedItem="{Binding RubroSeleccionado}"
                                          PlaceholderText="Elegí el rubro" HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:RubroGasto"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoLineaPoa" Grid.Row="2" Grid.Column="4" Margin="0,16,0,0" Etiqueta="Línea POA">
                                <ComboBox ItemsSource="{Binding LineasPoaDisponibles}" SelectedItem="{Binding LineaPoaSeleccionada}"
                                          PlaceholderText="Sin línea POA" HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="ent:LineaPoa"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>

                            <CheckBox x:Name="CheckCredito" Grid.Row="3" Grid.Column="0" Margin="0,16,0,0" VerticalAlignment="Bottom"
                                      IsChecked="{Binding EsCredito}" Content="Compra a crédito" />
                            <v2:CampoFormulario x:Name="CampoVencimiento" Grid.Row="3" Grid.Column="2" Margin="0,16,0,0"
                                                Etiqueta="Fecha de vencimiento" IsVisible="{Binding EsCredito}">
                                <CalendarDatePicker SelectedDate="{Binding FechaVencimientoSeleccionada}" HorizontalAlignment="Stretch"
                                                    PlaceholderText="dd/mm/aaaa" SelectedDateFormat="Custom" CustomDateFormatString="dd/MM/yyyy"
                                                    beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                            </v2:CampoFormulario>
                        </Grid>

                        <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />

                        <TextBlock Classes="subtitulo-formulario" Text="Cargar artículo" Margin="0,0,0,12" />
                        <!-- Enter en Producto (con un producto elegido), Cantidad o Precio agrega el artículo: la pantalla
                             existe para cargar muchos renglones seguidos con el teclado. Tras agregar, el foco vuelve al
                             producto; tras el alta de un producto nuevo, a Cantidad. En modo producto nuevo el combo se
                             reemplaza por el nombre, así no puede contradecir lo cargado en el alta. -->
                        <Grid x:Name="FilaCarga" ColumnDefinitions="*,12,120,12,140,12,Auto,12,Auto,12,Auto">
                            <v2:CampoFormulario x:Name="CampoProducto" Grid.Column="0" Etiqueta="Producto" Requerido="True">
                                <Panel>
                                    <ComboBox x:Name="ComboProducto"
                                              IsVisible="{Binding !EsProductoNuevoEnCarga}"
                                              ItemsSource="{Binding ProductosDisponibles}"
                                              SelectedItem="{Binding ProductoEnCarga}"
                                              PlaceholderText="Elegí el producto"
                                              HorizontalAlignment="Stretch"
                                              beh:FocoBehavior.SolicitarFoco="{Binding SolicitarFocoEnProductoCombo, Mode=TwoWay}"
                                              beh:EnterEjecutaComando.Comando="{Binding AgregarArticuloCommand}">
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate x:DataType="cat:ProductoDto"><TextBlock Text="{Binding Nombre}" /></DataTemplate>
                                        </ComboBox.ItemTemplate>
                                    </ComboBox>
                                    <Grid x:Name="ProductoNuevoEnCarga" ColumnDefinitions="*,8,Auto" IsVisible="{Binding EsProductoNuevoEnCarga}">
                                        <TextBlock Grid.Column="0" Classes="cuerpo" Text="{Binding NuevoProductoNombre}"
                                                   VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                                                   ToolTip.Tip="{Binding NuevoProductoNombre}" beh:TooltipSiSeCorta.Activo="True" />
                                        <Button x:Name="BotonCambiarProducto" Grid.Column="2" Classes="ghost" Content="Cambiar"
                                                Command="{Binding DescartarProductoNuevoEnCargaCommand}"
                                                ToolTip.Tip="Descartar este producto nuevo y volver a elegir uno existente" />
                                    </Grid>
                                </Panel>
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoCantidad" Grid.Column="2" Etiqueta="Cantidad" Requerido="True">
                                <TextBox x:Name="CantidadEnCarga"
                                         Text="{Binding CantidadEnCarga, Converter={x:Static conv:DecimalConverter.Instance}}"
                                         beh:EntradaNumericaBehavior.EsDecimal="True"
                                         PlaceholderText="Cantidad"
                                         beh:FocoBehavior.SolicitarFoco="{Binding SolicitarFocoEnCantidad, Mode=TwoWay}"
                                         beh:EnterEjecutaComando.Comando="{Binding AgregarArticuloCommand}" />
                            </v2:CampoFormulario>
                            <v2:CampoFormulario x:Name="CampoPrecio" Grid.Column="4" Etiqueta="Precio unitario">
                                <TextBox x:Name="PrecioEnCarga"
                                         Text="{Binding PrecioUnitarioEnCarga, Converter={x:Static conv:DecimalConverter.Instance}}"
                                         beh:EntradaNumericaBehavior.EsDecimal="True"
                                         PlaceholderText="Precio unitario"
                                         beh:EnterEjecutaComando.Comando="{Binding AgregarArticuloCommand}" />
                            </v2:CampoFormulario>
                            <CheckBox x:Name="CheckActualizarPrecio" Grid.Column="6" VerticalAlignment="Bottom" Margin="0,0,0,6"
                                      IsChecked="{Binding ActualizarPrecioCostoEnCarga}" Content="Actualizar precio costo" />
                            <Button x:Name="BotonProductoNuevo" Grid.Column="8" VerticalAlignment="Bottom" Classes="secundario"
                                    Content="Producto nuevo" Command="{Binding AbrirAltaProductoCommand}" />
                            <Button x:Name="BotonAgregar" Grid.Column="10" VerticalAlignment="Bottom" Classes="secundario"
                                    Content="Agregar artículo" Command="{Binding AgregarArticuloCommand}" />
                        </Grid>

                        <v2:Alerta x:Name="AlertaErrorCarga" Margin="0,8,0,0" Variante="Error"
                                   Texto="{Binding MensajeErrorCarga}"
                                   IsVisible="{Binding MensajeErrorCarga, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                        <Border Height="1" Margin="0,20" Background="{DynamicResource V2SeparadorGrillaBrush}" />

                        <TextBlock Classes="subtitulo-formulario" Text="Artículos cargados" Margin="0,0,0,12" />

                        <v2:EstadoVacio x:Name="EstadoSinRenglones"
                                        UseLayoutRounding="False"
                                        Titulo="Todavía no cargaste artículos"
                                        Mensaje="Elegí un producto, completá la cantidad y el precio, y apretá Enter."
                                        IsVisible="{Binding Renglones.Count, Converter={x:Static conv:ConteoConverter.EsCero}}" />

                        <!-- Solo lectura: la carga se hace arriba. Crece con los renglones (scrollea la página, el pie
                             queda fijo). CANTIDAD, PRECIO UNITARIO y SUBTOTAL miden su texto sobre todas las filas con el
                             mismo conversor de la celda. La columna de Quitar conserva sin-padding-vertical: el guardián
                             de botones en celdas la exige y en v2 la celda ya no tiene padding vertical. -->
                        <DataGrid x:Name="GrillaRenglones"
                                  ItemsSource="{Binding Renglones}"
                                  IsReadOnly="True"
                                  CanUserSortColumns="False"
                                  CanUserResizeColumns="False"
                                  IsVisible="{Binding Renglones.Count, Converter={x:Static conv:ConteoConverter.HayElementos}}">
                            <DataGrid.Columns>
                                <DataGridTemplateColumn Header="PRODUCTO" Width="*" MinWidth="160">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <TextBlock Text="{Binding NombreMostrado}" VerticalAlignment="Center"
                                                       TextTrimming="CharacterEllipsis" ToolTip.Tip="{Binding NombreMostrado}"
                                                       beh:TooltipSiSeCorta.Activo="True" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="CANTIDAD" Width="Auto" CellStyleClasses="num" SortMemberPath="Cantidad"
                                                        beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:CantidadConverter.Instance}">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <TextBlock Text="{Binding Cantidad, Converter={x:Static conv:CantidadConverter.Instance}}" VerticalAlignment="Center" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="PRECIO UNITARIO" Width="Auto" CellStyleClasses="num" SortMemberPath="PrecioUnitario"
                                                        beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <TextBlock Text="{Binding PrecioUnitario, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="SUBTOTAL" Width="Auto" CellStyleClasses="num" SortMemberPath="Subtotal"
                                                        beh:ColumnaAuto.Medir="True" beh:ColumnaAuto.Conversor="{x:Static conv:MonedaConverter.Instance}">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <TextBlock Text="{Binding Subtotal, Converter={x:Static conv:MonedaConverter.Instance}}" VerticalAlignment="Center" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Header="ACT. COSTO" Width="120">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <v2:Insignia Texto="Sí" Variante="Neutro" HorizontalAlignment="Left" VerticalAlignment="Center"
                                                         IsVisible="{Binding ActualizarPrecioCosto}" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                                <DataGridTemplateColumn Width="Auto" MinWidth="96" CellStyleClasses="sin-padding-vertical">
                                    <DataGridTemplateColumn.CellTemplate>
                                        <DataTemplate x:DataType="vm:FilaRenglonFacturaVm">
                                            <Button Classes="ghost" Content="Quitar" VerticalAlignment="Center"
                                                    Command="{Binding $parent[UserControl].((vm:IngresoPorFacturaViewModel)DataContext).QuitarRenglonCommand}"
                                                    CommandParameter="{Binding}" />
                                        </DataTemplate>
                                    </DataGridTemplateColumn.CellTemplate>
                                </DataGridTemplateColumn>
                            </DataGrid.Columns>
                        </DataGrid>

                    </StackPanel>
                </v2:TarjetaContenido>

                <!-- Después de guardar: el formulario queda deshabilitado y esta card aparece al final, a la vista.
                     AdjuntosPanelView sigue en el tema viejo hasta que se migra una vez para todas sus pantallas. -->
                <v2:TarjetaContenido x:Name="TarjetaAdjuntos" Padding="24" Margin="0,16,0,0"
                                     IsVisible="{Binding GuardadoExitoso}"
                                     beh:TraerALaVista.AlMostrarse="True">
                    <StackPanel>
                        <TextBlock Classes="subtitulo-formulario" Text="Adjuntá la factura" Margin="0,0,0,4" />
                        <TextBlock Classes="texto-secundario" TextWrapping="Wrap" Margin="0,0,0,12"
                                   Text="La factura quedó guardada. Adjuntá el PDF o la foto si corresponde." />
                        <ContentControl x:Name="PanelAdjuntos" Content="{Binding AdjuntosPanel}" />
                    </StackPanel>
                </v2:TarjetaContenido>

            </StackPanel>
        </ScrollViewer>

        <v2:Alerta x:Name="AlertaError" Grid.Row="1" Margin="28,0,28,12" Variante="Error"
                   Texto="{Binding MensajeError}"
                   IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

        <Border x:Name="Pie" Grid.Row="2"
                Background="{DynamicResource V2SuperficieBrush}"
                BorderBrush="{DynamicResource V2BordeBrush}"
                BorderThickness="0,1,0,0"
                Padding="28,16">
            <Grid ColumnDefinitions="Auto,32,Auto,32,Auto,*,Auto">
                <StackPanel Grid.Column="0" Spacing="4" VerticalAlignment="Center">
                    <TextBlock Classes="etiqueta-campo" Text="Suma de renglones" />
                    <TextBlock x:Name="ValorSuma" Classes="titulo-seccion"
                               Text="{Binding SumaRenglones, Converter={x:Static conv:MonedaConverter.Instance}}" />
                </StackPanel>
                <v2:CampoFormulario x:Name="CampoTotal" Grid.Column="2" Etiqueta="Total de factura" Requerido="True"
                                    IsEnabled="{Binding !GuardadoExitoso}">
                    <TextBox x:Name="TotalFactura" Width="160" Text="{Binding MontoTotalTexto}" PlaceholderText="0,00" />
                </v2:CampoFormulario>
                <StackPanel Grid.Column="4" Spacing="4" VerticalAlignment="Center">
                    <TextBlock Classes="etiqueta-campo" Text="Diferencia" />
                    <!-- Neutra si da cero; si no, advertencia. No bloquea el guardado. -->
                    <TextBlock x:Name="ValorDiferencia" Classes="titulo-seccion"
                               Classes.advertencia="{Binding DiferenciaConTotal, Converter={x:Static conv:DistintoDeCeroConverter.Instance}}"
                               Text="{Binding DiferenciaConTotal, Converter={x:Static conv:MonedaConverter.Instance}}" />
                </StackPanel>
                <StackPanel Grid.Column="6" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
                    <Button x:Name="BotonCancelar" Classes="secundario" Content="Cancelar"
                            Command="{Binding CancelarCommand}" IsVisible="{Binding !GuardadoExitoso}" />
                    <Button x:Name="BotonGuardar" Classes="primario" Content="Guardar"
                            Command="{Binding GuardarCommand}" IsVisible="{Binding !GuardadoExitoso}" />
                    <Button x:Name="BotonFinalizar" Classes="primario" Content="Finalizar"
                            Command="{Binding FinalizarCommand}" IsVisible="{Binding GuardadoExitoso}" />
                </StackPanel>
            </Grid>
        </Border>

    </Grid>

</UserControl>
```

El `Margin="0,0,0,6"` del tilde y los anchos 120/140 se ajustan a lo medido y aprobado en la Task 3; todo número que no sea múltiplo de 4 se anota como compensación para §6.5. La propiedad `ColumnaAuto.Conversor` y la ruta por `SortMemberPath` se confirman en `src/StockApp.Presentation/Behaviors/ColumnaAuto.cs`.

Run: `dotnet build StockApp.sln` y el comando del Step 1 → PASS.

- [ ] **Step 3: Tests heredados (convención 13)**

- `GuardianDePatronTests`: borrar `[InlineData(typeof(IngresoPorFacturaView), "Ingreso de stock por factura", "MOVIMIENTOS")]` (L48), sacar `typeof(IngresoPorFacturaView),` de `VistasDeLaTanda` (L101) y agregarla a `VistasMigradasAV2` (D16; su reemplazo es `Raiz_TieneUi2_EncabezadoSinBreadcrumb_YTresSecciones`). `VistaV2_NoUsaPiezasDelTemaViejo`, `VistaV2_NoTieneOpacidadesLiterales` y `VistaV2_BotonEstiradoEstiraSuContenido` pasan a correr sobre la vista.
- `IngresoPorFacturaGrillaRenglonesTests`: `BotonQuitar_TieneClaseSecondaryDelUiKit_…` (L111) → `BotonQuitar_EsGhostDelSistemaV2_…` con `Assert.Contains("ghost", …)`; `Cantidad_PrecioUnitario_Y_Subtotal_SeRenderizanEnEsUy_EnLaMismaFila` (L134): los textos de precio y subtotal pasan a moneda ("$ …", D6); `BotonQuitar_TextoInterno_TieneAltoRenderizadoVisible_…` (L178) no cambia (si su número cambia, anotarlo).
- `IngresoPorFacturaViewTests`: los helpers que ubican por placeholder ("Cantidad", "Precio unitario") y por texto de botón siguen valiendo (la vista v2 conserva esos textos); si un test depende de la estructura v1 (por ejemplo, `LaGrillaDeRenglones_EsDeSoloLectura_SinComboBoxPropioPorFila`), se adapta a la v2 sin borrarlo. La ventana de 1000×1600 de su XAML se conserva.
- `GrillasSinRecorteBarridoTests` (L87-92, L276-300): la grilla v2 está oculta sin renglones (`IsVisible` por `Renglones.Count`); el barrido monta la vista SIN VM. Si `Montar(new IngresoPorFacturaView(), …)` encuentra la grilla invisible, el escenario monta un `IngresoPorFacturaViewModel` mínimo (el de `IngresoPorFacturaViewV2Tests.Montar`) en vez de asignar `ItemsSource` a mano; se anota el cambio. La admisión de cortes de columnas medidas (`EsCorteAdmitido`) es la de siempre.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`. Expected: PASS. Todo cambio de un número en un test heredado se anota en el reporte con el antes, el después y el motivo.

- [ ] **Step 4: Comparaciones (rojo, verde y diagnóstico)**

En `DatosIngresoFactura`, agregar:

```csharp
    /// <summary>El interior de AdjuntosPanelView sigue en el tema viejo y no se compara: se vuelve transparente sin
    /// cambiar el layout (el mockup lo reserva con el mismo alto y visibility: hidden).</summary>
    public static void OcultarPanelAdjuntosV1(Control vista)
    {
        var panel = vista.GetVisualDescendants().OfType<StockApp.Presentation.Views.Finanzas.AdjuntosPanelView>().SingleOrDefault()
            ?? throw new InvalidOperationException("No hay AdjuntosPanelView: la comparación correría sobre una vista que perdió lo que excluye.");
        panel.Opacity = 0;
    }
```

(confirmar el namespace de `AdjuntosPanelView` con CodeGraph).

`IngresoFacturaComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Ingreso por factura (spec §5.2) dentro del shell real, con la vista resuelta por ViewLocator y el sidebar oculto en
/// los dos renders. El estado se aplica en prepararApp, después de la carga de los combos.
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class IngresoFacturaComparacionTests
{
    private static void Comparar(DatosIngresoFactura.Estado estado, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null)
    {
        var (shell, vm) = DatosIngresoFactura.EnShell();
        string[] ocultar = estado.Guardado ? [".v2-sidebar", ".panel-adjuntos-v1"] : [".v2-sidebar"];
        ComparacionVisual.CompararConMockup(new ShellMainView(), shell, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: ocultar,
            prepararApp: vista =>
            {
                ProductosComparacionTests.OcultarSidebar(vista);
                DatosIngresoFactura.Aplicar(vm, estado);
                if (estado.Guardado)
                    DatosIngresoFactura.OcultarPanelAdjuntosV1(vista);
            });
    }

    [AvaloniaFact]
    public void IngresoFactura_IgualAlMockup() => Comparar(DatosIngresoFactura.Vacio, "inventario/ingreso-factura.html");

    [AvaloniaFact]
    public void IngresoFactura_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.Vacio, "inventario/ingreso-factura.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaRenglones_IgualAlMockup() => Comparar(DatosIngresoFactura.ConRenglones, "inventario/ingreso-factura-renglones.html");

    [AvaloniaFact]
    public void IngresoFacturaRenglones_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.ConRenglones, "inventario/ingreso-factura-renglones.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaDiferencia_IgualAlMockup() => Comparar(DatosIngresoFactura.ConDiferencia, "inventario/ingreso-factura-diferencia.html");

    [AvaloniaFact]
    public void IngresoFacturaDiferencia_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.ConDiferencia, "inventario/ingreso-factura-diferencia.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void IngresoFacturaGuardado_IgualAlMockup() => Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html");

    [AvaloniaFact]
    public void IngresoFacturaGuardado_IgualAlMockup_1366x768() =>
        Comparar(DatosIngresoFactura.Guardado, "inventario/ingreso-factura-guardado.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh IngresoFactura`. Expected: 8 PASS; anotar las teselas densas de cada una en el ledger; MIRAR cada fila en `index.html` (el pie, la diferencia en advertencia, la grilla con "$", el vencimiento, la card de adjuntos). Si una supera el umbral: `teselas.py`/`tinta.py` ANTES de tocar nada; geometría se corrige en XAML o mockup (≤ 4 px no visible se anota, mayor vuelve al usuario); tono se presenta al usuario por tesela. El guardián de texto recortado corre sobre las ocho (un texto cortado en la fila de carga a 1280 es un defecto, no un `recortesPermitidos`).

- [ ] **Step 5: Mutaciones sobre la View real (QUITANDO)**

- (t) Quitar `Grid.Row="2"` de `Pie` (cae en la fila del scroll) → rojo en `Pie_ConCincuentaRenglones_…` y `ErrorDeGuardado_…`. Restaurar.
- (u) Quitar `beh:EnterEjecutaComando.Comando="{Binding AgregarArticuloCommand}"` de `CantidadEnCarga` → rojo en `Enter_EnCantidadYEnPrecio_…("CantidadEnCarga")`. Restaurar.
- (v) Quitar el mismo atributo de `ComboProducto` → rojo en `Enter_EnProductoConValor_Agrega`. Restaurar.
- (w) Quitar `beh:FocoBehavior.SolicitarFoco="{Binding SolicitarFocoEnCantidad, Mode=TwoWay}"` → rojo en `AltaConfirmada_…`. Restaurar.
- (x) Quitar `Classes.advertencia="…"` de `ValorDiferencia` → rojo en `Diferencia_…`. Restaurar.
- (y) Quitar `beh:TraerALaVista.AlMostrarse="True"` de `TarjetaAdjuntos` → rojo en `Guardado_LaCardDeAdjuntosQuedaALaVista`. Restaurar.
- (z) Quitar `IsVisible="{Binding !GuardadoExitoso}"` de `BotonGuardar` → rojo en `TrasGuardar_ElUnicoPrimarioEsFinalizar_…`. Restaurar.

`dotnet build StockApp.sln` después de cada restauración y `git diff --no-index` vacío.

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
git add src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml tests/
git commit -m "feat(ui-v2): Ingreso por factura v2 con pie fijo, Enter en la fila de carga y un solo primario, comparado"
```

(`git status --short` antes del `add`: solo los archivos de esta task.)

## Bloque D — Guardianes y cierre de la fase

### Task 9: Guardián de la convención 25 y verificación por mutación de las reglas clave

**Files:**
- Modify: `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs`
- Modify (local): el ledger

**Interfaces:**
- Consumes: `LeerDesdeSrc` (L531-536) de `GuardianDePatronTests`.
- Produces: `GuardianDePatronTests.NingunaVista_TieneOverlaysDeModal()`, `DetectorDeOverlays_VeElFondoYElFlagYNoUnBindingComun()`.

- [ ] **Step 1: Test (rojo)**

En `GuardianDePatronTests`, junto a `VistaV2_NoUsaPiezasDelTemaViejo`:

```csharp
    /// <summary>
    /// Convención 25: un modal de pantalla es una Window con servicio, nunca una capa semitransparente sobre la vista
    /// ligada a un flag Mostrando* del ViewModel (no atrapa el foco ni responde a Escape y Enter). Se barre el TEXTO de
    /// todos los .axaml de Views, no solo los v2: un overlay nuevo en una vista vieja también se rechaza.
    /// </summary>
    private static readonly Regex OverlayDeModal =
        new(@"#AA000000|(\{Binding\s+!?|Path="")Mostrando\w*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void NingunaVista_TieneOverlaysDeModal()
    {
        var views = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(LeerRutaDeEsteArchivo())!, "..", "..", "src", "StockApp.Presentation", "Views"));
        var ofensores = OverlaysEn(views);

        Assert.True(Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories).Count() > 40,
            "El barrido no encontró las vistas: el guardián no custodiaría nada.");
        Assert.True(ofensores.Count == 0, "Overlay de modal en: " + string.Join(", ", ofensores)
            + ". Un modal de pantalla es una Window con servicio (convención 25 de docs/ui-v2/herramientas.md).");
    }

    [Fact]
    public void DetectorDeOverlays_VeElFondoYElFlagYNoUnBindingComun()
    {
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{Binding MostrandoAltaProducto}" Background="#AA000000">""");
        Assert.Matches(OverlayDeModal, """<Border Background="#aa000000" />""");
        Assert.Matches(OverlayDeModal, """<Border IsVisible="{Binding !MostrandoConfirmacionPrecios}">""");
        Assert.Matches(OverlayDeModal, """<Binding Path="MostrandoAlgo" />""");
        Assert.DoesNotMatch(OverlayDeModal, """<Border IsVisible="{Binding GuardadoExitoso}">""");
        Assert.DoesNotMatch(OverlayDeModal, """<Border Background="{DynamicResource V2FondoBrush}" />""");
    }

    private static List<string> OverlaysEn(string directorio) =>
        Directory.EnumerateFiles(directorio, "*.axaml", SearchOption.AllDirectories)
            .Where(a => OverlayDeModal.IsMatch(File.ReadAllText(a)))
            .Select(a => Path.GetFileName(a)!)
            .ToList();

    private static string LeerRutaDeEsteArchivo([CallerFilePath] string archivo = "") => archivo;
```

Si ya existe en el archivo un helper equivalente para la ruta del archivo de test o del directorio `Views` (ver `DataGridCeldaConControlPaddingPropioGuardTests`, que barre `src/StockApp.Presentation/Views/`), reusarlo en vez de `LeerRutaDeEsteArchivo` y anotarlo.

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~Overlay"` → FAIL de compilación (pegar). Rojo de comportamiento sin tocar `src/` (un `.axaml` suelto ahí lo compila Avalonia) ni el historial (prohibido `git checkout`/`git restore`):
1. `mkdir -p "$SCRATCH/views" && git show <commit de la Task 5>:src/StockApp.Presentation/Views/Movimientos/IngresoPorFacturaView.axaml > "$SCRATCH/views/IngresoPorFacturaView.axaml"` (la vista v1 con los dos overlays).
2. Test TEMPORAL en el mismo archivo (no se commitea): `[Fact] public void Temporal_V1ConOverlays() => Assert.Empty(OverlaysEn("<ruta absoluta de $SCRATCH>/views"));` → rojo "IngresoPorFacturaView.axaml" (pegar).
3. Borrar el test temporal; `git diff tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` muestra solo lo de este Step; verde con el árbol actual.
- [ ] **Step 2: Mutaciones (QUITANDO)**

- (aa) Quitar `#AA000000|` de la regex → rojo en `DetectorDeOverlays_…` (las dos líneas del fondo). Restaurar.
- (ab) Quitar `|Path=""` → rojo en `DetectorDeOverlays_…` (la del `MultiBinding`). Restaurar.

- [ ] **Step 3: Repaso de las mutaciones clave de la fase**

Confirmar en el ledger que quedaron PEGADOS los rojos de las reglas que pide la spec (sección 3): `IsCancel` (f, j), `IsDefault` (g, k), validación del alta (h), pie fuera del scroll (t) y Enter → Agregar (a, u, v); y las de esta fase (b, c, d, e, i, l a s, w a z, aa, ab). Una sin rojo pegado se corre ahora.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs
git commit -m "test(ui-v2): guardián de la convención 25, ninguna vista con overlays de modal"
```

### Task 10: Suite completa, verificación orgánica, docs y review final

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 26 y 27)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda, §6.5)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 3c-3 cerrada en `feat/ui-v2` (sin push ni merge).

- [ ] **Step 1: Build, suite, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos de fixtures `.ods` (confirmar por nombre); total ≥ línea base de la Task 1 + los nuevos − los reemplazados (anotar el número y los reemplazos); los dos `--verificar` exit 0; TODAS las filas de `index.html` APROBADAS a los dos tamaños (las de fases anteriores sin cambios: si una se mueve, diagnosticarla; el estilo `advertencia` es aditivo y no debería mover ninguna).

- [ ] **Step 2: Verificación orgánica (spec §6.4) — app real contra la base local, en WSLg a 1280×760**

Preparación:
- `docker start stockapp-pg` (queda andando al terminar). **Backup ANTES de crear datos:** `docker exec stockapp-pg pg_dump -U <usuario> <base> > ~/stockapp-backups-local/pre-3c3-$(date +%Y%m%d-%H%M%S).sql` (usuario y base de `~/.config/GestionMunicipal/conexion.json` o del `.env` local; verificar con `ls -l` que el archivo no está vacío). Anotar la ruta en el ledger.
- API del worktree: `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/StockApp.Api --launch-profile http` (sin la variable falla por los user-secrets; escucha en `http://localhost:5043`, confirmarlo contra `conexion.json`). App: `dotnet run --project src/StockApp.Presentation`; proceso `pgrep -x GestionMunicipa`. `admin` / `Admin1234`.
- Operador de permisos MIXTOS para el paso 6: crear `f3c3mixto` con `movimientos.registrar`, `finanzas.gastos`, `finanzas.ver` y `catalogo.productos` (los cuatro de `PuedeIngresarPorFactura`) y nada más; anotar su id.
- Toolkit `scripts/gui-verificacion/` (ventana "Gestión Municipal"). MIRAR cada captura y comparar `md5sum` con la anterior: dos iguales cuando la pantalla debía cambiar = WSLg congelado (PARAR y pedir al usuario que reinicie WSL; no hacerlo vos). `escribir.sh` no manda Tab/Enter/Escape: para el teclado de los pasos 2 y 3 usar `xdotool key --window <hwnd> Return` / `Escape` y anotar si llegó. **Tras cerrar un diálogo modal la ventana principal puede quedar sin foco de teclado en WSLg** (artefacto del toolkit, deuda de la 3b): recuperar con `xdotool windowminimize <hwnd>` + `xdotool windowmap <hwnd>` y, recién después, decidir si el foco estaba donde debía (si el UiTest headless está verde y tras el recupero el foco está en Cantidad, es artefacto). **El Aviso modal global (403, sin servidor) puede quedar DETRÁS de la ventana principal o del diálogo** (observado en la 3c-2): si la app "no responde", buscarlo con `xdotool search --name` antes de declarar un cuelgue.

Todo a **1280×760**; los pasos 1 y 4 se repiten a 1366×768. Capturar cada pantalla.

1. **Pantalla:** Admin → "Ingreso por factura" (sidebar, queda marcado): título y frase sin breadcrumb, tres secciones, pie fijo con Guardar como único primario, "Agregar artículo" secundario. Comparar a ojo con `ingreso-factura.html`.
2. **Factura con un producto existente, uno nuevo y un cambio de precio:** cabecera completa (proveedor y fuente de prueba existentes; detalle "PRUEBA-3C3 factura"). Producto existente: elegirlo, cantidad, precio DISTINTO del costo, **Enter en Precio** → renglón agregado y foco en Producto. **Enter con Producto vacío** → no agrega, sin error. "Producto nuevo" → diálogo: Enter con campos vacíos → alerta "Falta completar: Código, Nombre y Unidad de medida." y no cierra; abrir el combo de Unidad y **Escape** → se cierra el desplegable y el diálogo sigue; completar Código "PRUEBA-3C3-0001", Nombre "PRUEBA 3C3 Carretilla", Unidad → **Enter** → el nombre en la fila y el foco en Cantidad (aplicar el recupero de foco si hace falta y anotar cuál de los dos casos fue). Cantidad, precio, Enter.
3. **Confirmación de precios:** Guardar → diálogo con SOLO el producto existente (el nuevo no aparece), precios en es-UY; Espacio cambia el tilde, **Enter con el foco en un tilde** confirma sin cambiarlo; probar antes una vez **Escape** (no guarda, la pantalla sigue igual) y volver a Guardar.
4. **Muchos renglones:** antes de guardar en el paso 3 (o con una factura nueva), cargar 30 renglones con el teclado (se puede repetir el mismo producto): el pie sigue fijo y Guardar a la vista; scrollear de punta a punta. Con un total que no coincide, Diferencia en advertencia; Guardar igual guarda.
5. **Guardar y adjuntar:** tras guardar, la card "Adjuntá la factura" aparece A LA VISTA (sin scrollear), el pie muestra solo "Finalizar"; adjuntar un PDF de prueba (`~/stockapp-pruebas/factura-prueba.pdf`, creado para esto). "Finalizar" → Gastos y facturas: la factura "PRUEBA-3C3 factura" con el monto y su adjunto. Anotar ids (gasto, movimientos, producto nuevo, adjunto).
6. **Operador mixto y servidor caído:** como `f3c3mixto`, ve el ítem y la pantalla v2; cargar un renglón y, con la API detenida (`kill` SOLO del pid de `StockApp.Api`, acotado por nombre; nunca un comando de alcance global), Guardar → `Alerta` "No se pudo conectar con el servidor…" entre el contenido y el pie, renglones intactos; levantar la API y volver a Guardar → guarda. Anotar si salió además un Aviso global y dónde (delante o detrás).
7. **Limpieza:** listar los ids creados (por prefijo `PRUEBA-3C3` y detalle "PRUEBA-3C3 factura", más el usuario `f3c3mixto`) y PREGUNTAR al usuario si se borran; SQL listo, acotado por esos ids, SOLO contra el contenedor local `stockapp-pg` (nunca el VPS). Anotar en el ledger lo borrado o lo que quedó. El contenedor queda andando.

Si un paso no se puede hacer por el entorno (WSLg caído o congelado, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`, md5 de las capturas), sin declarar la fase cerrada. Si el usuario necesita la PC, se pausa.

- [x] **Step 3: Aprobación final del usuario** (aprobada por el usuario el 2026-10-11)

PARAR. Presentar: `index.html` (las 8 filas de la pantalla y las 6 de los diálogos), las capturas de la verificación orgánica junto a los mockups, los rojos de las mutaciones (a) a (ab), los tests heredados reemplazados (con su reemplazo) y los cambios respecto de los mockups aprobados. Registrar en el ledger: `Fase 3c-3: aprobada por <quién> <fecha>`.

- [ ] **Step 4: `herramientas.md` y spec**

`herramientas.md`, después de la 25: "Convenciones que agrega la Fase 3c-3 (Ingreso por factura)" con la 26 y la 27 de este plan (como quedaron aprobadas).

Spec:
1. `**Estado:**`: "; Fase 3c-3 (Ingreso por factura y patrón de modal v2) APROBADA por el usuario el <fecha>" y "3c-3" en la lista de fases implementadas; la fila "3 Inventario" de la tabla de §7.
2. §7, después de "Estado de la Fase 3c-2": "**Estado de la Fase 3c-3 (aprobada el <fecha>).** Ingreso por factura v2 (tres secciones, pie fijo fuera del scroll, un solo primario, Enter en la fila de carga con `EnterEjecutaComando`, diferencia en advertencia, card de adjuntos a la vista tras guardar) y patrón de modal v2 (`AltaProductoRapidaDialog` y `ConfirmacionPreciosDialog`, `Window` con servicio, convención 25 custodiada por `GuardianDePatronTests.NingunaVista_TieneOverlaysDeModal`). Comparado en <N> filas a los dos tamaños (<máximo de teselas densas>). Verificación orgánica: <resumen>. Decisiones del usuario: <D1 a D16 aprobadas o cambiadas>." y reemplazar la línea "Plan (se escribe después de este diseño)" del diseño por la ruta de este plan.
3. §6.5: cada compensación nueva medida en las Tasks 4, 5 y 8 (si hubo), con su antes y después.
4. "**Deuda abierta tras la Fase 3c-3:**" con, como mínimo, las cuatro de la sección 3 del diseño (categoría opcional en código y obligatoria en la spec funcional; anulación del lote sin botón; selector de producto como `ComboBox` con todo el catálogo; `AdjuntosPanelView` v1 hasta la Fase 4), H1 (D15) si se conservó, lo observado en el paso 6 (Aviso global), cualquier `UmbralAprobado`, los minors diferidos del ledger y los datos de prueba que hayan quedado.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 3c-3 (Ingreso por factura y patrón de modal v2)"
```

- [ ] **Step 5: Review final de la rama**

Review de todo el rango `<commit de este plan>..HEAD` (skill de ejecución: review de rama completa con el modelo más capaz), con la suite y las comparaciones ya verdes. Foco: los cinco puntos de "Review Focus", que ningún binding v1 haya quedado en la vista v2, que los diálogos no tengan bindings al VM y que la excepción a "VMs intactos" no exceda U1 y D11. Los hallazgos se resuelven en commits propios (TDD, con rojo pegado) o se anotan como deuda con el usuario. No se pushea ni se mergea.

---

## Fuera de alcance

- Cambios funcionales: categoría obligatoria en el alta, anulación del lote, `AutoCompleteBox` como selector de producto, H1 (salvo decisión del usuario en el GATE), alerta en línea con "Reintentar" para la carga de los combos (convención 22): hoy un 403 en `InicializarAsync` se traga y el aviso lo da el transporte; la pantalla no tiene estado "sin conexión".
- `AdjuntosPanelView` v2 (Fase 4) y los otros diálogos de la app que no son de esta pantalla.
- Los 3 overlays de actualización del shell (no son modales de pantalla, convención 25).
- Modo oscuro, back-stack, cambios de API (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| Un `ComboBox` de Avalonia 12 no abre el desplegable con Enter (o lo maneja distinto) y el comportamiento de Enter cambia de sentido. | `EnUnComboVacio_…` y `EnUnComboConValor_…` fijan lo observado con teclado real; el Step 1 de la Task 2 indica qué assert ajustar y exige reportarlo; `Enter_EnLaFilaDeCarga_NuncaGuarda` (Task 8) custodia lo esencial. |
| El headless no lleva el teclado al popup del combo, ni restaura el foco tras un `ShowDialog`. | Los tests de desplegable abierto usan `RaiseEvent` sobre el combo; el foco tras el alta se prueba con el pulso del VM (determinista) y no con el foco nativo; lo nativo queda para la verificación orgánica. |
| El pie fijo y la fila de carga no entran a 1280 (Producto queda angosto, botones cortados). | Regla de D4 medida en la Task 3 Step 1 y decidida en el GATE; el guardián de texto recortado corre en las 8 comparaciones; `Pie_ConCincuentaRenglones_…` incluye una ventana de 784×600. |
| `AdjuntosPanelView` v1 dentro de `.ui2` se ve mal (estilos v2 sobre controles v1). | Excepción documentada (spec, D14); no se compara; la verificación orgánica paso 5 la mira y lo que se vea va a la deuda de la Fase 4. |
| La DataGrid dentro del `ScrollViewer` con muchos renglones pierde virtualización y se pone lenta. | Volumen real de una factura: decenas de renglones; `Pie_…` carga 50 y el paso 4 de la verificación orgánica 30; si se nota lentitud, se anota con el número (no se resuelve en esta fase). |
| Quitar los flags rompe `ReflexionVistaViewModelTests` o `ComandosDeGuardadoGuardianTests` de formas no previstas. | Exclusiones con motivo en la Task 6; `ComandosDeGuardadoGuardianTests.Construir` arma el VM por reflexión con mocks y se corre en la Task 6 Step 4. |
| Un test heredado se borra sin reemplazo. | Convención 13: el único borrado (`ConfirmacionDeCambioDePrecio_MuestraLosPreciosEnEsUy`) tiene su reemplazo nombrado en la Task 5; L48 de `GuardianDePatronTests` tiene el suyo en la Task 8 (D16); el cierre lista los reemplazos. |
| WSLg se congela o pierde el foco y la verificación orgánica da falsos rojos. | md5 de capturas, recupero `windowminimize` + `windowmap`, distinción artefacto/bug por el UiTest headless; si se congela, PARAR y pedir el reinicio al usuario. |
| La verificación orgánica ensucia la base local. | Backup antes; prefijo `PRUEBA-3C3`; ids anotados; borrado con SQL acotado y con OK del usuario; nunca el VPS. |
| Scripts o capturas fuera del worktree; `capturar.sh` no fail-closed. | `cd <worktree> &&` y `ls -l --time-style=full-iso`; mirar cada captura y comparar md5. |
