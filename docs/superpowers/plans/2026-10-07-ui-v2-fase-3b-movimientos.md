# UI v2 — Fase 3b Entrada/Salida (`MovimientoFormControl`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrar `MovimientoFormControl` (el formulario que comparten "Registrar Entrada" y "Registrar Salida") al sistema v2 (spec §5.2: card de 760, campos a 2 columnas, un solo primario al pie, `Alerta` de error), comparado contra sus mockups aprobados a 1280×760 y 1366×768, sin tocar `MovimientoRegistroViewModelBase`, `EntradaRegistroViewModel` ni `SalidaRegistroViewModel`.

**Architecture:** Un solo control cubre dos pantallas: `EntradaRegistroView` y `SalidaRegistroView` son wrappers de `<local:MovimientoFormControl />` y no cambian (ni su `.axaml` ni su code-behind con `DataContextChanged → InicializarAsync`). Primero se fijan los datos de ejemplo (Bloque A) y se escriben los mockups de 4 estados que el usuario aprueba en un GATE (Bloque B). Recién ahí se reescribe `MovimientoFormControl.axaml` siguiendo la receta ya aprobada de `ProductoFormView`, se adaptan los tests heredados y se compara cada estado dentro del shell real con el sidebar oculto (Bloque C). Cierra la suite, la verificación orgánica con los dos diálogos y la aprobación final (Bloque D).

**Tech Stack:** .NET 10, Avalonia 12.0.5 (Headless + Skia + Fonts.Inter), Microsoft.Playwright 1.63.0 (Chromium), SkiaSharp 3.119.4, CommunityToolkit.Mvvm, Moq 4.20.72, xUnit v2 (`StockApp.Presentation.Tests`, `StockApp.UiV2.Herramientas.Tests`) y xUnit v3 + Avalonia.Headless.XUnit (`StockApp.Presentation.UiTests`, `StockApp.Presentation.VisualTests`).

**Spec:** `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (§3 VMs intactos; §4.4 breadcrumb; §5.2 formulario; §6 fidelidad, §6.5 calibración y compensaciones; §7 Estado de la Fase 3a y "Deuda abierta tras la Fase 3a"). Herramienta y convenciones 1 a 14: `docs/ui-v2/herramientas.md`. Plan del que hereda formato, receta y herramientas de diagnóstico: `docs/superpowers/plans/2026-10-06-ui-v2-fase-3a-productos.md`.

Todos los comandos se corren desde la raíz del worktree: `/home/capua25/workspace/stockapp-worktrees/ui-v2` (rama `feat/ui-v2`, base `4fb00d6`). Ese worktree tiene su propio índice `.codegraph/`: usarlo (CodeGraph MCP `codegraph_explore`) antes de grep/Read para todo lo que sea código C#; nunca el índice de otro checkout. CodeGraph NO indexa `.axaml`, markdown ni HTML/CSS: para eso, Read directo. Si CodeGraph devuelve un archivo como "changed on disk" sin fuente, leerlo con Read y decirlo en el reporte.

## Decisiones del usuario

| # | Decisión | Cómo queda en el plan | Tasks |
|---|---|---|---|
| U1 | **Fase 3 partida en tres (2026-10-06):** este plan cubre SOLO 3b: Entrada/Salida + `MovimientoFormControl`. | `IngresoPorFacturaView`, `MovimientoHistorialView` y el patrón de modal v2 no se tocan (3c). | todas |
| U2 | **Lo que la spec no fija del diseño lo propone el mockup y lo decide el usuario en el GATE (2026-10-06, reconfirmado para 3b).** | Lista D1 a D13. No son preguntas previas: si el usuario cambia una, se ajusta el mockup y los tests de este plan que la fijan, en la Task 2, antes del XAML. | 2 |
| U3 | **Dos deudas de la 3a se trasladan a la 3c**, porque 3b no tiene listados ni grillas: el criterio común para columnas `Auto` de contenido libre y el patrón del estado vacío durante la carga/403. | 3b no depende de ninguna de las dos. La Task 4 corrige la spec (hoy dice "se resuelve al migrar la 3b y la 3c" y "Decidir el patrón ANTES de la 3b"). | 4 |

## Decisiones que se confirman al aprobar los mockups (Task 2)

- **D1 — Encabezado sin breadcrumb.** `EncabezadoPagina` solo con `Titulo="{Binding Titulo}"` ("Registrar Entrada" / "Registrar Salida", con la mayúscula que trae el VM). Motivo: es pantalla de sidebar, no un alta que vuelve a un listado, y el VM no expone `Volver` ni `Cancelar` (§4.4 pide breadcrumb en "altas y ediciones"; un breadcrumb sin destino no tiene comando que bindear sin tocar el VM). (Test: `Raiz_TieneUi2_YEncabezadoSinBreadcrumbConElTituloDelVm`.)
- **D2 — Sin estados que el VM no expone** (sin permiso, cargando, éxito inline, "no hay productos"): mismo criterio que D10 de la 3a. El 403 de `InicializarAsync` se sigue tragando y el aviso global lo da `App` ("Tus permisos cambiaron..."). El gate es solo el del shell (`PuedeRegistrarEntradaSalida`). Así 3b no depende de la decisión del estado vacío durante la carga (U3).
- **D3 — Una sola sección, sin subtítulo.** Son 4 o 5 campos de un mismo tema; el título de la página ya la nombra. §5.2 dice "secciones tituladas": acá se propone no titular la única sección. Alternativa: subtítulo "Datos del movimiento" (agrega 28 px de alto).
- **D4 — Layout** (receta de `ProductoFormView`: card máx. 760 a la izquierda, padding 24, `Grid *,16,*`): Producto (ancho completo); Cantidad | Motivo; Precio unitario | (vacío), solo cuando el motivo lo muestra; Comentario (ancho completo). Alternativa: Motivo | Precio en la misma fila y Cantidad sola (deja el precio al lado del motivo que lo gobierna, pero rompe el orden actual Producto, Cantidad, Motivo, Precio). (Test: `Campos_ProductoYComentarioACompleto_CantidadYMotivoEnDosColumnas`.)
- **D5 — Sin hueco al ocultarse el precio.** El espacio de 16 entre filas va en el `Margin` superior de cada campo, no en filas espaciadoras: en Salida (y cuando no aplique) el Comentario queda a 16 de la fila Cantidad | Motivo. (Test: `SinFilaDePrecio_ElComentarioQuedaA16DeLaFilaDeCantidad`.)
- **D6 — Comentario multilínea de 88 con ajuste de línea** (la medida de Descripción de Productos); hoy 60 y sin `TextWrapping` (un comentario largo scrollea en horizontal).
- **D7 — Marca de agua del combo de producto: "Elegí un producto".** Texto nuevo (hoy el combo arranca vacío y mudo). Alternativa: sin marca de agua.
- **D8 — Cantidad arranca en "0"** (es `decimal` en el VM y `DecimalConverter` lo muestra): la marca de agua "Ingresá la cantidad" se conserva pero no se ve al entrar. Cambiarlo pide `decimal?` en el VM (fuera de alcance).
- **D9 — Pie con un solo botón, "Registrar" (`primario`), a la derecha**, con la línea `#F0EFED` arriba (20 de margen, 16 de padding) como el formulario de Productos. Hoy va estirado a todo el ancho. Sin "Cancelar": el VM no lo expone. (Test: `Pie_RegistrarEsElUnicoPrimarioYQuedaALaDerecha`.)
- **D10 — Error de registro como `Alerta` error** encima del pie, con 8 de margen superior (variante compactada de la 3a) y el texto real de la excepción (`MensajeError = ex.Message`). Hoy es un `TextBlock` rojo.
- **D11 — Estados a mockear (los cuatro a 1280×760 y 1366×768):** `entrada-compra` (al entrar: sin producto, cantidad "0", Compra, precio con asterisco y "Obligatorio", "Registrar" deshabilitado); `entrada-ajuste` (completo, precio vacío "Opcional" sin asterisco, "Registrar" habilitado); `entrada-error` (Compra completa con la alerta); `salida-uso` (Uso o consumo, sin campo de precio). **No se mockean** Salida con error (es la misma alerta del mismo control: solo cambia que falta el precio, ya cubierto por `salida-uso`) ni cantidad inválida (el error de campo v2 ya está en el catálogo de componentes; lo custodian `CantidadInvalida_MuestraElErrorBajoElCampoYNoMueveElMotivo` y `GuardarBloqueadoConErroresDeEntradaTests`).
- **D12 — Datos de ejemplo:** productos de `DatosProductos.Productos` (los 11 activos en el combo); el error es el real de servidor caído (`ServidorNoDisponibleException.MensajePorDefecto`, dos líneas en la alerta); la salida usa "Cable 2x1,5 mm" (la "x" entre dígitos es el caso de `calt`).
- **D13 — Diálogos sin cambios:** "El stock quedará en X. ¿Confirmar la salida igual?" y "Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?" ya son v2 (`ConfirmacionDialog`, Fase 1); no se mockean de nuevo. Se prueban con tests de flujo y en la verificación orgánica.

## Global Constraints

- Tema claro fijo, sin modo oscuro (spec §2, §10). Tokens y medidas de la spec §2 (copiados en el plan de la 3a, "Global Constraints"): fondo app `#F7F7F5`, superficie `#FFFFFF`, borde `#E7E5E4`, separador `#F0EFED`, texto primario `#1C1917`, peligro texto `#B91C1C`; Alerta error `#FEE2E2`/`#FCA5A5`/`#991B1B`; input y botón 36; radios 8 y 10; card de formulario máx. 760; sidebar 240 (a 1280 quedan 1040 para la vista, a 1366 quedan 1126).
- Formulario (§5.2): card máx. 760; 2 columnas donde los campos lo permitan; cada campo un `CampoFormulario` v2; un solo primario; botones al pie a la derecha; textos reales ("Registrar").
- **ViewModels intactos** (`MovimientoRegistroViewModelBase`, `EntradaRegistroViewModel`, `SalidaRegistroViewModel`): ni una línea. Tampoco cambian `ViewLocator`, `INavigationService`, `ErrorConverter` ni la lógica de permisos. Reglas que el XAML refleja sin reimplementar: el precio aplica solo a Entrada (`PrecioVisible`), es obligatorio solo en Compra (`PrecioObligatorio`, `PrecioPlaceholder`), y el VM lo pone en `null` al ocultarse; motivos Entrada: Compra, Ajuste; Salida: Uso o consumo, Merma, Ajuste.
- **Code-behind y wrappers intactos:** `EntradaRegistroView.axaml(.cs)`, `SalidaRegistroView.axaml(.cs)` y `MovimientoFormControl.axaml.cs` NO se tocan ni se renombran (`ViewLocator` por convención de nombre; `DataContextChanged → InicializarAsync` porque las vistas de Avalonia no se autoinicializan).
- **`Classes="ui2"` en la raíz de `MovimientoFormControl`**, no en los wrappers: el contenido vive en el control y varios tests lo montan solo en una `Window`. Los wrappers solo hospedan este control, así que la convención "nunca `ui2` en un contenedor que hospede vistas no migradas" se cumple.
- **`calt` de Inter apagado en toda la UI v2** (Style `:is(Control).ui2` con `-calt`, spec §2.3/§6.5): todo control que setee `FontFeatures` propio tiene que incluir `-calt`; los popups `ToolTip`/`FlyoutPresenter` ya están cubiertos; el desplegable del `ComboBox` hereda. Esta fase no setea `FontFeatures`; lo fija `Combos_ProductoConXEntreDigitos_SinCalt`.
- **`tokens.json` es la fuente de `Tokens.axaml` (generado): nunca editar `Tokens.axaml` a mano.** Esta fase no prevé cambios de tokens. Toda task (y su review) que toque `Themes/V2/`, `Controls/V2/` o `docs/ui-v2/` corre `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar` (en la 3a un token editado a mano solo apareció en la suite de cierre).
- **La comparación por teselas no ve un glifo ni un borde de 1 px.** Las reglas visuales clave (precio oculto, espaciado sin hueco, ancho 760, único primario, alerta error, `calt`) se custodian con UiTests sobre la View REAL (efecto, no declaración), verificados por mutación QUITANDO: `cp <archivo> "$SCRATCH/<nombre>.bak"`; se ELIMINA la regla; `git diff --no-index "$SCRATCH/<nombre>.bak" <archivo>` muestra SOLO lo quitado; se corre el test y se PEGA el rojo real; se restaura con `cp`; `git diff --no-index` vacío; verde. `export SCRATCH=~/.cache/stockapp-ui-v2-fase-3b && mkdir -p "$SCRATCH"` (dentro del home, nunca `/tmp`). Nunca se agrega un efecto destructivo para mutar. NUNCA `git checkout --`.
- **Compensaciones conocidas Chromium/Avalonia** (spec §6.5): Chromium colapsa márgenes verticales y Avalonia los suma (si dos márgenes se tocan, descontar uno en el XAML); un `Grid` con `MaxWidth` y `UseLayoutRounding` da +1 px (por eso `ColumnaFormulario` lleva `UseLayoutRounding="False"` y la card `True`, como en Productos); un elemento centrado con ancho fraccionario puede pedir `UseLayoutRounding="False"` por instancia; texto con alto de línea fija `LineHeight` desde token (la `Alerta` ya lo hace con `V2FuenteControlAltoLinea`).
- Mockups (§6.1): `docs/ui-v2/mockups/inventario/<pantalla>.html`, el mismo HTML a 1280×760 y 1366×768, `../_base/tokens.css` + `../_base/base.css`, UN `<div class="v2-ventana">`, datos idénticos a `DatosMovimientos`. Solo colores sólidos, bordes de 1px, radios, una `BoxShadow`, Inter, px enteros. Prohibido: gradientes, blur, transparencias apiladas, transiciones, pseudo-elementos decorativos, `text-shadow`. **Sin scroll a ninguno de los dos tamaños:** se mide el alto de la página en cada captura. Se aprueban ANTES del XAML.
- Umbral (§6.5): `CompararConMockup` con los calibrados (11 teselas densas, 1,68%) a AMBOS tamaños. Uno mayor solo con `UmbralAprobado` tras revisar CADA tesela densa con el usuario; 16 o más exige además `MutacionPalidaEnRojo` en ESA pantalla. Nunca se sube para tapar un defecto. Diagnóstico: `teselas.py` y `tinta.py` de la sección "Herramientas de diagnóstico" del plan de la 3a (copiarlos a `$SCRATCH/diag/`, nunca se commitean) y verificar familia, tamaño, peso, color y alto de línea del mockup contra el XAML.
- **Convención 6:** las cuatro pantallas se comparan DENTRO del shell real (`ShellMainView`, `CurrentContent` = el VM, resuelto por `ViewLocator`) con el sidebar oculto en ambos renders.
- **Gates de permisos:** el control no tiene gates (verificado: ningún `IsVisible` de permiso en `MovimientoFormControl.axaml`; los VM no reciben `ICurrentSession`). El gate es el del sidebar (`PuedeRegistrarEntradaSalida`, que exige `catalogo.productos` y `movimientos.registrar`, deuda preexistente anotada en la spec) y los accesos de Inicio, ya custodiados por `ShellMainViewGatesTests` e `InicioViewV2Tests`. La verificación orgánica usa un usuario de permisos MIXTOS además de Admin.
- **Tests de flujo disparan el comando real** (click real en "Registrar"), con `NavigationRecorderFake` y una confirmación guionada; nunca llaman `Navegar` a mano.
- Formato es-UY: cantidades y precios con `DecimalConverter` / `DecimalOpcionalConverter` + `EntradaNumericaBehavior.EsDecimal` (sin `StringFormat` suelto); el stock del diálogo ya sale con `FormatoEsUy.Cantidad`.
- Errores de entrada (convención 3): `RegistrarCommand` sigue creado con `ComandoDeGuardado`; el botón NO lleva `ToolTip.Tip` ni `ShowOnDisabled` a mano. `ComandosDeGuardadoGuardianTests` ya reconoce `Classes="primario"`.
- Los comentarios de `.axaml` y `.csproj` NO pueden contener `--` (AVLN1001, cascadea a MSB4025). En vistas con `x:DataType`, un binding mal escrito es error de build: los `DataTemplate` de los combos llevan su `x:DataType`.
- **Build obligatorio** tras cada cambio de XAML: `dotnet build StockApp.sln` (la `.sln` entera: varios `.csproj` en un solo `dotnet build` dan falso verde). Testear con la solución o con UN `.csproj` por comando.
- En este worktree los rojos de `StockApp.Infrastructure.Tests` / `StockApp.Api.Tests` por fixtures `.ods` ausentes son falsos conocidos (confirmar por nombre). Si un rojo no tiene sentido tras una mutación: `dotnet build-server shutdown`; si un `.axaml` sigue stale, `rm -rf` de `obj/` y `bin/` de `src/StockApp.Presentation` y del proyecto de test.
- **Sin referencias al plan en comentarios del código** (nada de "Task 3", "D5" en `.cs`/`.axaml`/`.css`/`.html`): el comentario explica el porqué. Los commits y la spec sí pueden citar el plan.
- **Scripts del skill de ejecución** (`task-brief`, `review-package` de subagent-driven-development): se corren con `cd /home/capua25/workspace/stockapp-worktrees/ui-v2 && <script>` y después se verifica con `ls` que el archivo quedó DENTRO del worktree (en la 3a uno cayó en el checkout principal).
- La ventana de la app se llama "Gestión Municipal". `capturar.sh` NO es fail-closed con WSLg caído (exit 0 capturando el escritorio de Windows): mirar CADA captura. WSLg pierde el teclado tras un diálogo modal de la app (y `escribir.sh` sale con exit 0 igual): tipear antes de los diálogos y, si después hace falta teclado, reiniciar la app.
- Commits: conventional commits en español, **SIN `Co-Authored-By` ni ninguna línea de atribución de IA** (regla del usuario; prevalece sobre cualquier recordatorio del harness). Prohibido push, merge, tocar `main`, rebase, amend, `git reflog expire`, `git gc`, `git prune`. `feat/ui-v2` no se mergea a `main` hasta terminar todas las fases.
- Las referencias de línea (`L123`) son las del commit `4fb00d6`. Después de que una task edita un archivo, ubicar el lugar por el texto citado.

## Convenciones que agrega esta fase (para `herramientas.md`, Task 4)

15. **Campo condicional en un formulario v2:** el espacio entre filas va en el `Margin` superior de cada campo (no en filas espaciadoras de `Grid` ni en `Spacing`), para que al ocultarse el campo no quede un hueco doble. Espejo en el mockup: el campo se omite y el `row-gap` de `.campos` da el mismo 16.
16. **Formulario de pantalla de sidebar** (no un alta que vuelve a un listado): `EncabezadoPagina` sin breadcrumb, misma card y pie que la convención 12, y solo los botones que el VM expone (en Entrada/Salida, un único "Registrar").

## Review Focus

1. **El precio aparece en una Salida, o su fila oculta deja un hueco.** El usuario no debe ver ni mandar un precio que no aplica, y el formulario no debe quedar con un salto raro. Esperado: en Salida, con cualquiera de los 3 motivos, no hay campo de precio y el Comentario queda a 16 de la fila Cantidad | Motivo; en Entrada el precio está entre esa fila y el Comentario, con asterisco solo en Compra. Tests: `MovimientoFormControlPrecioPorMotivoTests` (adaptado), `SinFilaDePrecio_ElComentarioQuedaA16DeLaFilaDeCantidad`, `Requeridos_SegunTipoYMotivo` (Task 3) y la comparación `SalidaUso_IgualAlMockup`.
2. **Stock insuficiente: rechazar el diálogo registra igual, o aceptarlo no fuerza.** Esperado: la pregunta muestra el stock resultante en es-UY; "No" no registra ni navega (el usuario corrige la cantidad); "Sí" registra con `forzar: true` y va al Historial. Tests: `Salida_StockInsuficiente_SiNoConfirma_NoRegistraNiNavega`, `Salida_StockInsuficiente_SiConfirma_RegistraForzandoYVaAlHistorial` (Task 3).
3. **"Asociar factura" aparece en un Ajuste, o no aparece en una Compra.** Esperado: solo tras una Entrada por Compra; "Sí" navega al formulario de gasto precargado; un Ajuste va directo al Historial sin preguntar. Tests: `Entrada_Compra_OfreceAsociarFactura_YSiAceptaVaAlGasto`, `Entrada_Ajuste_NoPreguntaPorFactura_YVaAlHistorial` (Task 3).
4. **Un error del servidor se pierde o navega igual.** Esperado: la `Alerta` error con el texto real, visible, sin navegar; el formulario conserva lo cargado. Test: `Registrar_ConErrorDelServicio_MuestraLaAlertaYNoNavega` (Task 3) y la comparación `EntradaError_IgualAlMockup`.
5. **Cantidad con punto decimal ("5.4") que registra el valor viejo o mueve el layout.** Esperado: mensaje "Usá coma para los decimales: 5,4" bajo el campo, "Registrar" bloqueado con el tooltip estándar, Motivo en su lugar; con "5,4" se habilita y registra 5,4. Tests: `CantidadInvalida_MuestraElErrorBajoElCampoYNoMueveElMotivo` (Task 3), `GuardarBloqueadoConErroresDeEntradaTests` (Entrada y Salida) y `MovimientoFormControlCantidadCulturaTests`.

---

## Decisiones de diseño del plan (verificadas, con fuente)

| Tema | Decisión | Evidencia (commit `4fb00d6`) |
|---|---|---|
| Alcance real | Todo el XAML está en `MovimientoFormControl.axaml` (L1-93, `x:DataType="vm:MovimientoRegistroViewModelBase"`); los wrappers solo tienen `<local:MovimientoFormControl />`. | `EntradaRegistroView.axaml` L11, `SalidaRegistroView.axaml` L11, `EntradaRegistroView.axaml.cs` L15-19. |
| VM sin cambios | Ya expone todo: `Titulo`, `Productos`, `ProductoSeleccionado`, `Cantidad`, `MotivosDisponibles`, `Motivo`, `PrecioUnitario`, `PrecioVisible`, `PrecioObligatorio`, `PrecioPlaceholder`, `Comentario`, `MensajeError`, `RegistrarCommand`. Sin `Volver`/`Cancelar`/`SinPermiso` (D1, D2, D9). | `MovimientoRegistroViewModelBase.cs` L33-79, L105-127 (403 tragado), L129-134 (`PuedeRegistrar`, `ComandoDeGuardado`), L136-169 (stock insuficiente), L176-180; `EntradaRegistroViewModel.cs` L28 (`Titulo`), L40-59 (factura solo en Compra); `SalidaRegistroViewModel.cs` L23. |
| La carga vacía la lista | `InicializarAsync` hace `Productos.Clear()` y vuelve a agregar: el `ComboBox` suelta la selección y el binding TwoWay la pone en `null`. Los datos de la comparación aplican el estado DESPUÉS de la carga (en `prepararApp`), y `DatosMovimientos.Aplicar` falla si la carga no corrió. | `MovimientoRegistroViewModelBase.cs` L109-113. |
| Cantidad muestra "0" | `Cantidad` es `decimal` (no nullable) y `DecimalConverter.Convert` da "0": la marca de agua no se ve al entrar (D8). `MovimientoFormControlCantidadCulturaTests` busca la caja por `PlaceholderText` (propiedad), así que sigue andando. | `DecimalConverter.cs` L33-34; `MovimientoFormControlCantidadCulturaTests.cs` L60-62. |
| Comentario | Hoy `AcceptsReturn` sin `TextWrapping` y `Height="60"` (D6). | `MovimientoFormControl.axaml` L70-75. |
| `PrecioUnitarioBox` | El nombre queda en el `TextBox` (lo buscan `MovimientoFormControlValidacionTests` y `PrecioPorMotivoTests` por `Name`); el contenido de `CampoFormulario` pertenece al namescope del control. Se conserva `DecimalOpcionalConverter` ("" → null). | `MovimientoFormControl.axaml` L50-68; `MovimientoFormControlValidacionTests.cs` L223-225. |
| Breadcrumb ausente | `EncabezadoPagina` oculta `PART_Breadcrumb` si `BreadcrumbOrigen` es vacío: no se setea nada. | `Controls/V2/ComponentesV2.axaml` L140-143; `EncabezadoPagina.cs` L21-22. |
| Sección del shell | El shell marca "RegistrarEntrada"/"RegistrarSalida" para cada VM: los mockups dibujan el sidebar del Admin con ESE ítem activo. | `ShellMainViewModel.cs` L357-358, L527-538; `shell/sidebar-admin.html` L28-30. |
| Guardián de patrón | `MovimientoFormControl`, `EntradaRegistroView` y `SalidaRegistroView` salen de las `[InlineData]` de `Vista_TieneHeaderVistaConElTituloEsperado` (L53-55) y de `VistasDeLaTanda` (L110-112) y entran a `VistasMigradasAV2` (L217-228). Pierden `Vista_NoTieneUnSegundoBotonPrimario` (solo corre en `VistasDeLaTanda`/`VistasEmbebidas`, y busca `primary`); lo reemplaza `Pie_RegistrarEsElUnicoPrimarioYQuedaALaDerecha`. Ganan `VistaV2_NoTieneOpacidadesLiterales` y `VistaV2_BotonEstiradoEstiraSuContenido`. | `GuardianDePatronTests.cs` L41-47 (comentario P3/P5), L563-578. |
| Tests heredados | `PrecioPorMotivoTests` usa `Controls.CampoFormulario` (L8, L44): pasa a v2. `GuardarBloqueadoConErroresDeEntradaTests.CajaDeCampo` (L61-66) acepta los dos tipos; tras 3b ninguna de sus vistas usa el viejo: queda solo v2. `MotivoTextoTests`, `ValidacionTests` y `CantidadCulturaTests` no buscan tipos viejos: deben seguir verdes sin cambios. | archivos citados. |
| Mensaje de error de ejemplo | `ServidorNoDisponibleException.MensajePorDefecto` es el texto que el usuario ve con la API caída (`MensajeError = ex.Message`). `VisualTests` lo ve por la referencia transitiva `Presentation → ApiClient`. | `ServidorNoDisponibleException.cs` L10-12; `StockApp.Presentation.csproj` L77. |

## Mapa de archivos

**Se crean:**

| Archivo | Responsabilidad |
|---|---|
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientos.cs` | Estados fijos de los mockups de Entrada/Salida y el shell de cada uno. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientosTests.cs` | Textos de los datos = los del mockup; el estado sobrevive a la carga. |
| `docs/ui-v2/mockups/inventario/entrada-compra.html`, `entrada-ajuste.html`, `entrada-error.html`, `salida-uso.html` | Mockups (D11). |
| `tests/StockApp.Presentation.UiTests/V2/MovimientoFormControlV2Tests.cs` | Formulario v2 con la View real: estructura, layout, precio condicional, flujos con los dos diálogos, error, `calt`. |
| `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/MovimientosComparacionTests.cs` | 4 estados × 2 tamaños. |

**Se modifican:**

| Archivo | Cambio |
|---|---|
| `src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml` | Reescritura v2 (code-behind sin cambios). |
| `tests/StockApp.Presentation.UiTests/MovimientoFormControlPrecioPorMotivoTests.cs` | `CampoFormulario` v2. |
| `tests/StockApp.Presentation.UiTests/GuardarBloqueadoConErroresDeEntradaTests.cs` | `CajaDeCampo` solo v2. |
| `tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs` | Las 3 vistas a `VistasMigradasAV2`. |
| `docs/ui-v2/herramientas.md` | Convenciones 15 y 16. |
| `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` | Estado, §7 (Fase 3b), deuda trasladada a 3c (U3). |

**Comandos de verificación:**

- Build: `dotnet build StockApp.sln`
- Suite completa: `dotnet test StockApp.sln`
- VM: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
- UI: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj`
- Visual: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj`
- Herramienta: `dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj` y `dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar`
- Un test: `--filter "FullyQualifiedName~NombreDelTest"`
- Comparación con página: `scripts/ui-v2/comparar.sh Movimientos` y abrir `artifacts/ui-v2/comparacion/index.html`

Ledger: `.superpowers/sdd/2026-10-07-ui-v2-fase-3b-movimientos/progress.md` (crearlo en la Task 1; `.superpowers/sdd/` está en `.gitignore`).

---

## Bloque A — Datos fijos

### Task 1: Estados fijos de Entrada/Salida

**Files:**
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientos.cs`
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientosTests.cs`
- Create (local): `.superpowers/sdd/2026-10-07-ui-v2-fase-3b-movimientos/progress.md`

**Interfaces:**
- Consumes: `DatosProductos.Productos` (12 `ProductoDto`, 11 activos), `DatosProductos.UsuarioAdmin`, `DatosShell.Crear(UsuarioSesion, string[], string, ViewModelBase?)`; constructores `EntradaRegistroViewModel`/`SalidaRegistroViewModel(IMovimientoStockService, IProductoService, INavigationService, IConfirmacionService)`.
- Produces: `DatosMovimientos.Estado` (record), `EntradaCompra`, `EntradaAjuste`, `EntradaError`, `SalidaUso` (`Estado`), `DatosMovimientos.Vm(bool salida)`, `DatosMovimientos.Aplicar(MovimientoRegistroViewModelBase, Estado)`, `DatosMovimientos.EnShell(Estado)` → `(ShellMainViewModel Shell, MovimientoRegistroViewModelBase Vm)`.

- [ ] **Step 1: Test (rojo)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientosTests.cs`:

```csharp
using StockApp.ApiClient;
using StockApp.Domain.Enums;
using StockApp.Domain.Formato;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Los mockups de Entrada/Salida escriben estos textos a mano: si un dato cambia y el mockup no, la comparación
/// falla por un texto distinto y el diagnóstico no lo dice. Y el estado se aplica después de la carga, porque
/// InicializarAsync vacía la lista de productos y el combo suelta la selección.
/// </summary>
public class DatosMovimientosTests
{
    [Fact]
    public void Estados_TextosFormateados_SonLosDelMockup()
    {
        Assert.Equal("0", FormatoEsUy.Cantidad(DatosMovimientos.EntradaCompra.Cantidad));
        Assert.Equal("2", FormatoEsUy.Cantidad(DatosMovimientos.EntradaAjuste.Cantidad));
        Assert.Equal("40", FormatoEsUy.Cantidad(DatosMovimientos.EntradaError.Cantidad));
        Assert.Equal("480", FormatoEsUy.Cantidad(DatosMovimientos.EntradaError.Precio!.Value));
        Assert.Equal("25,5", FormatoEsUy.Cantidad(DatosMovimientos.SalidaUso.Cantidad));
        Assert.Equal("Uso o consumo", DatosMovimientos.SalidaUso.Motivo.Nombre());
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, DatosMovimientos.EntradaError.Error);
    }

    [Fact]
    public async Task Aplicar_DespuesDeLaCarga_ConservaElProductoElegido()
    {
        var vm = DatosMovimientos.Vm(salida: true);
        await vm.InicializarAsync();

        DatosMovimientos.Aplicar(vm, DatosMovimientos.SalidaUso);

        Assert.Equal(11, vm.Productos.Count);
        Assert.Equal("Cable 2x1,5 mm", vm.ProductoSeleccionado!.Nombre);
        Assert.Equal(MotivoMovimiento.UsoOConsumo, vm.Motivo);
        Assert.Equal(25.5m, vm.Cantidad);
    }

    [Fact]
    public void Aplicar_SinLaCarga_FallaEnVezDeDejarElComboVacio()
    {
        var vm = DatosMovimientos.Vm(salida: false);

        Assert.Throws<InvalidOperationException>(() => DatosMovimientos.Aplicar(vm, DatosMovimientos.EntradaAjuste));
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj --filter "FullyQualifiedName~DatosMovimientosTests"`
Expected: FAIL de compilación (`The name 'DatosMovimientos' does not exist`).

- [ ] **Step 2: `DatosMovimientos`**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientos.cs`:

```csharp
using Moq;
using StockApp.ApiClient;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inventario/entrada-*.html y salida-uso.html (textos fijados en
/// DatosMovimientosTests). El estado se aplica DESPUÉS de InicializarAsync: la carga vacía Productos y el
/// ComboBox pone ProductoSeleccionado en null; aplicarlo antes dejaría el combo vacío en la comparación.
/// </summary>
public static class DatosMovimientos
{
    public sealed record Estado(string Seccion, bool Salida, string? Producto, decimal Cantidad, MotivoMovimiento Motivo,
        decimal? Precio, string? Comentario, string? Error);

    /// <summary>Al entrar a Registrar entrada: sin producto, cantidad 0, Compra (precio obligatorio, Registrar deshabilitado).</summary>
    public static readonly Estado EntradaCompra =
        new("RegistrarEntrada", false, null, 0m, MotivoMovimiento.Compra, null, null, null);

    /// <summary>Ajuste completo: el precio es opcional y queda vacío.</summary>
    public static readonly Estado EntradaAjuste =
        new("RegistrarEntrada", false, "Pintura látex blanca 20 L", 2m, MotivoMovimiento.Ajuste, null,
            "Diferencia de conteo del inventario de setiembre.", null);

    /// <summary>Compra completa que no llegó al servidor: el texto es el de ServidorNoDisponibleException.</summary>
    public static readonly Estado EntradaError =
        new("RegistrarEntrada", false, "Cemento portland 50 kg", 40m, MotivoMovimiento.Compra, 480m,
            "Remito 0042-00001234", ServidorNoDisponibleException.MensajePorDefecto);

    /// <summary>Salida por uso: sin campo de precio. "2x1,5" es el caso de la ligadura calt apagada.</summary>
    public static readonly Estado SalidaUso =
        new("RegistrarSalida", true, "Cable 2x1,5 mm", 25.5m, MotivoMovimiento.UsoOConsumo, null,
            "Reparación del alumbrado de la plaza Independencia.", null);

    public static MovimientoRegistroViewModelBase Vm(bool salida)
    {
        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(DatosProductos.Productos);
        var movimientos = Mock.Of<IMovimientoStockService>();
        return salida
            ? new SalidaRegistroViewModel(movimientos, productos.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>())
            : new EntradaRegistroViewModel(movimientos, productos.Object, Mock.Of<INavigationService>(), Mock.Of<IConfirmacionService>());
    }

    public static void Aplicar(MovimientoRegistroViewModelBase vm, Estado estado)
    {
        if (vm.Productos.Count == 0)
            throw new InvalidOperationException("La carga de productos no corrió antes de aplicar el estado: el combo quedaría vacío.");

        vm.Motivo = estado.Motivo;
        vm.ProductoSeleccionado = estado.Producto is null ? null : vm.Productos.Single(p => p.Nombre == estado.Producto);
        vm.Cantidad = estado.Cantidad;
        vm.PrecioUnitario = estado.Precio;
        vm.Comentario = estado.Comentario;
        vm.MensajeError = estado.Error;
    }

    public static (ShellMainViewModel Shell, MovimientoRegistroViewModelBase Vm) EnShell(Estado estado)
    {
        var vm = Vm(estado.Salida);
        return (DatosShell.Crear(DatosProductos.UsuarioAdmin, [], estado.Seccion, vm), vm);
    }
}
```

- [ ] **Step 3: Verde**

Run: el filtro del Step 1. Expected: PASS (3 tests). Si `FormatoEsUy` da otro texto, manda `FormatoEsUy`: corregir el TEST y anotarlo en el ledger (los mockups se escriben con esos textos).

- [ ] **Step 4: Ledger y commit**

Crear el ledger con: base `4fb00d6`, la tabla de los 4 estados (producto, cantidad, motivo, precio, comentario, error) tal como los muestra la app.

```bash
git add tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientos.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/DatosMovimientosTests.cs
git commit -m "test(ui-v2): datos fijos de Entrada/Salida para mockups y comparaciones"
```

## Bloque B — Mockups y aprobación del usuario (GATE antes del XAML)

Los mockups dibujan el sidebar del Admin para que el usuario apruebe la pantalla entera; la comparación lo oculta. El sidebar es el `<aside class="v2-sidebar">…</aside>` de `docs/ui-v2/mockups/shell/sidebar-admin.html` copiado TAL CUAL, con UN cambio: la clase `v2-item-sidebar--activo` pasa del ítem "Productos" (L28) al de "Registrar entrada" (L29) en los tres `entrada-*.html`, o al de "Registrar salida" (L30) en `salida-uso.html`. En el HTML de abajo, `SIDEBAR_ADMIN` y `GLIFO_COMBO` son marcadores que NO quedan en el archivo; `GLIFO_COMBO` es el `<svg class="v2-glifo-combo" …>…</svg>` de `docs/ui-v2/mockups/componentes/catalogo.html` tal cual.

### Task 2: Mockups de Entrada y Salida (4 estados)

**Decisiones aplicadas:** D1 a D13.

**Files:**
- Create: `docs/ui-v2/mockups/inventario/entrada-compra.html`, `entrada-ajuste.html`, `entrada-error.html`, `salida-uso.html`

**Interfaces:**
- Consumes: `base.css` (`.v2-encabezado*`, `.v2-titulo-pagina`, `.v2-tarjeta`, `.v2-campo*`, `.v2-input*`, `.v2-glifo-combo`, `.v2-alerta--error`, `.v2-boton--*`, `.v2-sidebar*`); los estados de `DatosMovimientos` (Task 1).
- Produces: pantallas `inventario-entrada-compra`, `inventario-entrada-ajuste`, `inventario-entrada-error`, `inventario-salida-uso` para la Task 3. Ningún cambio de `base.css` (todo lo necesario ya existe; si el mockup pide una clase nueva, es geometría nueva: agregarla con su espejo en el XAML y decirlo en el GATE).

- [ ] **Step 1: `entrada-compra.html`**

```html
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Inventario: Registrar entrada por compra (UI v2)</title>
<link rel="stylesheet" href="../_base/tokens.css">
<link rel="stylesheet" href="../_base/base.css">
<style>
  .contenido { position: absolute; left: var(--v2-ancho-sidebar); top: 0; right: 0; bottom: 0; overflow: hidden; }
  /* ScrollViewer > StackPanel Margin 28,24 de MovimientoFormControl.axaml. */
  .pagina { padding: 24px 28px; }
  /* Grid con una columna * de MaxWidth 760: la card mide 760 a la izquierda. Padding de formulario 24. */
  .formulario { max-width: var(--v2-ancho-formulario-max); padding: 24px; }
  /* El 16 entre filas es el Margin superior de cada campo en la app: un campo oculto no deja hueco. */
  .campos { display: grid; grid-template-columns: 1fr 1fr; column-gap: 16px; row-gap: 16px; align-items: start; }
  .completo { grid-column: 1 / 3; }
  .multilinea { height: 88px; align-items: flex-start; padding: 8px 12px; }
  .pie-formulario { display: flex; justify-content: flex-end; gap: 8px; margin-top: 20px; padding-top: 16px; border-top: 1px solid var(--v2-color-separador-grilla); }
  .alerta-error { margin-top: 8px; }
</style>
</head>
<body>
<!-- Datos: DatosMovimientos.EntradaCompra. Al entrar: sin producto, cantidad 0 (decimal del VM), Compra por
     defecto con el precio obligatorio; Registrar deshabilitado (PuedeRegistrar). -->
<div class="v2-ventana">
  SIDEBAR_ADMIN
  <div class="contenido">
    <div class="pagina">
      <div class="v2-encabezado">
        <div class="v2-encabezado-fila"><div><div class="v2-titulo-pagina">Registrar Entrada</div></div></div>
      </div>
      <div class="v2-tarjeta formulario">
        <div class="campos">
          <div class="v2-campo completo">
            <div class="v2-campo-etiqueta"><span>Producto</span><span class="v2-campo-requerido">*</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">Elegí un producto</span>GLIFO_COMBO</div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Cantidad</span><span class="v2-campo-requerido">*</span></div>
            <div><div class="v2-input">0</div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Motivo</span></div>
            <div><div class="v2-input"><span>Compra</span>GLIFO_COMBO</div></div>
          </div>
          <div class="v2-campo">
            <div class="v2-campo-etiqueta"><span>Precio unitario</span><span class="v2-campo-requerido">*</span></div>
            <div><div class="v2-input"><span class="v2-input-placeholder">Obligatorio</span></div></div>
          </div>
          <div class="v2-campo completo">
            <div class="v2-campo-etiqueta"><span>Comentario</span></div>
            <div><div class="v2-input multilinea"><span class="v2-input-placeholder">Comentario opcional</span></div></div>
          </div>
        </div>
        <div class="pie-formulario">
          <span class="v2-boton v2-boton--primario v2-boton--deshabilitado">Registrar</span>
        </div>
      </div>
    </div>
  </div>
</div>
</body>
</html>
```

- [ ] **Step 2: `entrada-ajuste.html`, `entrada-error.html`, `salida-uso.html`**

Cada uno es el archivo COMPLETO del Step 1 (escrito entero, no por diferencia) con estos cambios:

`entrada-ajuste.html` — `<title>Inventario: Registrar entrada por ajuste (UI v2)</title>`; comentario `<!-- Datos: DatosMovimientos.EntradaAjuste. En un Ajuste el precio es opcional: sin asterisco y vacío. -->`; valores:
- Producto: `<div class="v2-input"><span>Pintura látex blanca 20 L</span>GLIFO_COMBO</div>`
- Cantidad: `<div class="v2-input">2</div>`
- Motivo: `<span>Ajuste</span>` + glifo
- Precio unitario: etiqueta SIN `<span class="v2-campo-requerido">*</span>`; `<div class="v2-input"><span class="v2-input-placeholder">Opcional</span></div>`
- Comentario: `<div class="v2-input multilinea">Diferencia de conteo del inventario de setiembre.</div>`
- Registrar habilitado: `<span class="v2-boton v2-boton--primario">Registrar</span>`

`entrada-error.html` — `<title>Inventario: Registrar entrada con error (UI v2)</title>`; comentario `<!-- Datos: DatosMovimientos.EntradaError. La API no respondió: el texto es ServidorNoDisponibleException.MensajePorDefecto. -->`; valores: Producto `Cemento portland 50 kg`, Cantidad `40`, Motivo `Compra`, Precio unitario (con asterisco) `<div class="v2-input">480</div>`, Comentario `Remito 0042-00001234`, Registrar habilitado y, entre `.campos` y `.pie-formulario`:

```html
        <div class="v2-alerta v2-alerta--error alerta-error">No se pudo conectar con el servidor de StockApp. Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar.</div>
```

`salida-uso.html` — `<title>Inventario: Registrar salida por uso o consumo (UI v2)</title>`; comentario `<!-- Datos: DatosMovimientos.SalidaUso. En una Salida el precio no aplica nunca: el campo no está y el comentario sube a la fila siguiente. -->`; título `Registrar Salida`; el sidebar con "Registrar salida" activo; SIN el `<div class="v2-campo">` de Precio unitario; valores: Producto `Cable 2x1,5 mm`, Cantidad `25,5`, Motivo `Uso o consumo`, Comentario `Reparación del alumbrado de la plaza Independencia.`, Registrar habilitado.

- [ ] **Step 3: Sidebar, glifos, capturas y convenciones**

Reemplazar `SIDEBAR_ADMIN` y `GLIFO_COMBO` en los cuatro. Después:

```bash
for p in entrada-compra entrada-ajuste entrada-error salida-uso; do
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p.png
  dotnet run --project tools/StockApp.UiV2.Herramientas -- capturar-mockup docs/ui-v2/mockups/inventario/$p.html artifacts/ui-v2/mockups/inventario-$p@1366x768.png --tamano 1366x768
done
grep -c 'SIDEBAR_\|GLIFO_' docs/ui-v2/mockups/inventario/entrada-*.html docs/ui-v2/mockups/inventario/salida-uso.html
grep -o 'v2-item-sidebar--activo"><svg[^>]*><path[^>]*/></svg><span>[^<]*' docs/ui-v2/mockups/inventario/entrada-*.html docs/ui-v2/mockups/inventario/salida-uso.html | sed 's/:.*<span>/: /'
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
```

Expected: 8 PNG sin error de fuentes, desborde ni `.v2-ventana`; el primer `grep` imprime `0` por archivo; el segundo, una línea por archivo: "Registrar entrada" en los tres `entrada-*` y "Registrar salida" en `salida-uso` (si un archivo no aparece o aparece dos veces, el `--activo` está mal puesto); Herramientas.Tests en verde (incluido `MockupsDelRepo_RespetanLaConvencion`) y `tokens --verificar` exit 0. MIRAR cada PNG: la card mide 760 a la izquierda; el formulario entero, hasta "Registrar", entra sin scroll a los dos tamaños (medir en la captura el borde inferior de la card: estimado ~600 px en `entrada-error`, el más alto, contra 760/768); la alerta en dos líneas sin cortar.

- [ ] **Step 4: GATE — aprobación del usuario**

PARAR. Presentar los 8 PNG (LOS DOS TAMAÑOS) o los 4 HTML (abrirlos en Chromium/Edge) con la lista D1 a D13. Señalar explícitamente: sin breadcrumb y con el título del VM en mayúscula, "Registrar Entrada" (D1); sin subtítulo de sección (D3, la spec pide secciones tituladas); el orden de los campos y la alternativa Motivo | Precio (D4); la Salida sin hueco donde estaría el precio (D5); el comentario de 88 que envuelve (D6); la marca de agua nueva "Elegí un producto" (D7); la cantidad que arranca en "0" (D8); "Registrar" a la derecha en vez de estirado (D9); qué estados NO se mockean (D11). No seguir sin "aprobado" explícito. Si el usuario cambia una D#, aplicarla en los mockups y en los tests de la Task 3 que la fijan, y volver a presentar. Registrar en el ledger: `Mockups Entrada/Salida: aprobados por <quién> <yyyy-MM-dd>; cambios: <lista o "ninguno">` y las D# cambiadas con su valor nuevo.

- [ ] **Step 5: Commit**

```bash
git add docs/ui-v2/mockups/inventario/entrada-compra.html docs/ui-v2/mockups/inventario/entrada-ajuste.html docs/ui-v2/mockups/inventario/entrada-error.html docs/ui-v2/mockups/inventario/salida-uso.html
git commit -m "docs(ui-v2): mockups aprobados de Registrar entrada y salida"
```

## Bloque C — XAML y comparaciones (arranca solo con los mockups aprobados)

### Task 3: `MovimientoFormControl` v2 con sus cuatro estados comparados

**Depende de:** Tasks 1 y 2 (mockups aprobados; leer del ledger las D# cambiadas).

**Files:**
- Modify: `src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml` (reescritura; el `.axaml.cs` y los dos wrappers NO cambian)
- Create: `tests/StockApp.Presentation.UiTests/V2/MovimientoFormControlV2Tests.cs`
- Modify: `tests/StockApp.Presentation.UiTests/MovimientoFormControlPrecioPorMotivoTests.cs` (L8), `GuardarBloqueadoConErroresDeEntradaTests.cs` (L61-66), `GuardianDePatronTests.cs` (L41-47, L53-55, L110-112, `VistasMigradasAV2`)
- Create: `tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/MovimientosComparacionTests.cs`

**Interfaces:**
- Consumes: `DatosMovimientos` (Task 1); mockups `inventario/entrada-*.html`, `salida-uso.html` (Task 2); `ProductosComparacionTests.OcultarSidebar(Control)`; `EncabezadoPagina(Titulo, BreadcrumbOrigen)`, `CampoFormulario(Etiqueta, Requerido)`, `Alerta(Texto, Variante)`, `TarjetaContenido`; `NavigationRecorderFake.UltimoTipoNavegado`/`UltimoInicializador`; `ProductoServiceIngresoFake(IReadOnlyList<ProductoDto>)`; `ArbolVisual.EsVisibleEnArbol`.
- Produces: partes nombradas de `MovimientoFormControl`: `Encabezado`, `ColumnaFormulario` (Grid), `Tarjeta`, `CampoProducto`, `ComboProducto`, `CampoCantidad`, `CampoMotivo`, `ComboMotivo`, `CampoPrecio`, `PrecioUnitarioBox` (se conserva), `CampoComentario`, `AlertaError`, `PieFormulario`, `BotonRegistrar`.

- [ ] **Step 1: UiTests del control v2 (rojo)**

`tests/StockApp.Presentation.UiTests/V2/MovimientoFormControlV2Tests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Controls.V2;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>Registrar Entrada / Salida v2 (spec §5.2) con las Views REALES (los wrappers), clicks reales y el tema real.</summary>
public class MovimientoFormControlV2Tests
{
    /// <summary>Registra cada llamada; con StockActualAlFaltar, el primer intento sin forzar responde stock insuficiente.</summary>
    private sealed class MovimientosGuionado : IMovimientoStockService
    {
        public decimal? StockActualAlFaltar { get; set; }
        public Exception? Falla { get; set; }
        public List<(RegistrarMovimientoDto Dto, bool Forzar)> Llamadas { get; } = new();

        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
        {
            Llamadas.Add((dto, forzar));
            if (Falla is not null) throw Falla;
            if (StockActualAlFaltar is { } stock && !forzar)
                throw new StockInsuficienteException(dto.ProductoId, stock, dto.Cantidad);
            return Task.FromResult(new MovimientoRegistradoDto(50, dto.ProductoId, dto.Tipo, dto.Motivo, dto.Cantidad,
                dto.PrecioUnitario ?? 0m, 10m, 10m + dto.Cantidad, new DateTime(2026, 10, 7, 13, 0, 0, DateTimeKind.Utc)));
        }

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro) => throw new NotSupportedException();
        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId) => throw new NotSupportedException();
    }

    /// <summary>Responde en orden lo guionado y guarda cada pregunta; una pregunta no guionada falla.</summary>
    private sealed class ConfirmacionGuionada : IConfirmacionService
    {
        private readonly Queue<bool> _respuestas;
        public ConfirmacionGuionada(params bool[] respuestas) => _respuestas = new Queue<bool>(respuestas);
        public List<string> Preguntas { get; } = new();

        public Task<bool> PreguntarAsync(string mensaje)
        {
            Preguntas.Add(mensaje);
            return Task.FromResult(_respuestas.Dequeue());
        }

        public Task InformarAsync(string mensaje) => Task.CompletedTask;
        public Task<string?> PedirTextoAsync(string titulo, string mensaje) => throw new NotSupportedException();
    }

    private static ProductoDto P(int id, string nombre, bool activo = true) => new(id, $"C-{id:000}", null, nombre, null,
        null, null, null, 1, "Unidad", 100m, 10m, 0m, activo, new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc));

    private static readonly ProductoDto[] Catalogo = [P(1, "Cemento portland 50 kg"), P(7, "Cable 2x1,5 mm"), P(12, "Carretilla reforzada", activo: false)];

    private sealed record Montaje(Window Ventana, UserControl Vista, MovimientoRegistroViewModelBase Vm,
        MovimientosGuionado Servicio, NavigationRecorderFake Navegacion, ConfirmacionGuionada Confirmacion);

    private static Montaje Montar(bool salida = false, ConfirmacionGuionada? confirmacion = null)
    {
        var servicio = new MovimientosGuionado();
        var navegacion = new NavigationRecorderFake();
        var conf = confirmacion ?? new ConfirmacionGuionada();
        var productos = new ProductoServiceIngresoFake(Catalogo);
        MovimientoRegistroViewModelBase vm;
        UserControl vista;
        if (salida)
        {
            var s = new SalidaRegistroViewModel(servicio, productos, navegacion, conf);
            vm = s;
            vista = new SalidaRegistroView { DataContext = s };
        }
        else
        {
            var e = new EntradaRegistroViewModel(servicio, productos, navegacion, conf);
            vm = e;
            vista = new EntradaRegistroView { DataContext = e };
        }

        var ventana = new Window { Width = 1040, Height = 760, Content = vista };
        ventana.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // deja completar el await InicializarAsync() del DataContextChanged
        return new Montaje(ventana, vista, vm, servicio, navegacion, conf);
    }

    private static void Completar(Montaje m, string producto, decimal cantidad, MotivoMovimiento motivo, decimal? precio = null)
    {
        m.Vm.Motivo = motivo;
        m.Vm.ProductoSeleccionado = m.Vm.Productos.Single(p => p.Nombre == producto);
        m.Vm.Cantidad = cantidad;
        m.Vm.PrecioUnitario = precio;
        Dispatcher.UIThread.RunJobs();
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

    private static Rect Campo(Montaje m, string nombre) => EnVentana(m.Ventana, Nombrado<CampoFormulario>(m.Vista, nombre));

    [AvaloniaTheory]
    [InlineData(false, "Registrar Entrada")]
    [InlineData(true, "Registrar Salida")]
    public void Raiz_TieneUi2_YEncabezadoSinBreadcrumbConElTituloDelVm(bool salida, string titulo)
    {
        var m = Montar(salida);
        var control = m.Vista.GetVisualDescendants().OfType<MovimientoFormControl>().Single();
        var encabezado = Nombrado<EncabezadoPagina>(m.Vista, "Encabezado");

        Assert.Contains("ui2", control.Classes);
        Assert.DoesNotContain("ui2", m.Vista.Classes); // el wrapper no cambia
        Assert.Equal(titulo, encabezado.Titulo);
        Assert.Null(encabezado.BreadcrumbOrigen);
        Assert.False(ArbolVisual.EsVisibleEnArbol(encabezado.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_Breadcrumb")));
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
    public void Campos_ProductoYComentarioACompleto_CantidadYMotivoEnDosColumnas()
    {
        var m = Montar();

        Assert.Equal(Campo(m, "CampoProducto").X, Campo(m, "CampoCantidad").X);
        Assert.Equal(Campo(m, "CampoProducto").Right, Campo(m, "CampoMotivo").Right);
        Assert.Equal(Campo(m, "CampoCantidad").Y, Campo(m, "CampoMotivo").Y);
        Assert.Equal(16, Campo(m, "CampoMotivo").X - Campo(m, "CampoCantidad").Right);
        Assert.Equal(Campo(m, "CampoCantidad").X, Campo(m, "CampoPrecio").X);
        Assert.Equal(Campo(m, "CampoCantidad").Width, Campo(m, "CampoPrecio").Width);
        Assert.Equal(Campo(m, "CampoProducto").Width, Campo(m, "CampoComentario").Width);
        Assert.Equal(88, Nombrado<CampoFormulario>(m.Vista, "CampoComentario").GetVisualDescendants().OfType<TextBox>().First().Bounds.Height);
    }

    [AvaloniaFact]
    public void ConPrecio_LasFilasVanA16()
    {
        var m = Montar(); // Entrada arranca en Compra: el precio se ve
        var filaCantidad = Math.Max(Campo(m, "CampoCantidad").Bottom, Campo(m, "CampoMotivo").Bottom);

        Assert.Equal(16, Campo(m, "CampoCantidad").Y - Campo(m, "CampoProducto").Bottom);
        Assert.Equal(16, Campo(m, "CampoPrecio").Y - filaCantidad);
        Assert.Equal(16, Campo(m, "CampoComentario").Y - Campo(m, "CampoPrecio").Bottom);
    }

    [AvaloniaTheory]
    [InlineData(MotivoMovimiento.UsoOConsumo)]
    [InlineData(MotivoMovimiento.Merma)]
    [InlineData(MotivoMovimiento.Ajuste)]
    public void SinFilaDePrecio_ElComentarioQuedaA16DeLaFilaDeCantidad(MotivoMovimiento motivo)
    {
        var m = Montar(salida: true);
        m.Vm.Motivo = motivo;
        Dispatcher.UIThread.RunJobs();
        var filaCantidad = Math.Max(Campo(m, "CampoCantidad").Bottom, Campo(m, "CampoMotivo").Bottom);

        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<CampoFormulario>(m.Vista, "CampoPrecio")));
        Assert.Equal(16, Campo(m, "CampoComentario").Y - filaCantidad);
    }

    [AvaloniaFact]
    public void Requeridos_SegunTipoYMotivo()
    {
        static List<string?> Requeridos(Montaje m) => m.Vista.GetVisualDescendants().OfType<CampoFormulario>()
            .Where(c => c.Requerido && ArbolVisual.EsVisibleEnArbol(c)).Select(c => c.Etiqueta).ToList();

        var entrada = Montar();
        Assert.Equal(new string?[] { "Producto", "Cantidad", "Precio unitario" }, Requeridos(entrada));

        entrada.Vm.Motivo = MotivoMovimiento.Ajuste;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new string?[] { "Producto", "Cantidad" }, Requeridos(entrada));

        Assert.Equal(new string?[] { "Producto", "Cantidad" }, Requeridos(Montar(salida: true)));
    }

    [AvaloniaFact]
    public void Pie_RegistrarEsElUnicoPrimarioYQuedaALaDerecha()
    {
        var m = Montar();
        var registrar = Nombrado<Button>(m.Vista, "BotonRegistrar");
        var tarjeta = EnVentana(m.Ventana, Nombrado<TarjetaContenido>(m.Vista, "Tarjeta"));

        Assert.Equal("Registrar", registrar.Content);
        Assert.Same(m.Vm.RegistrarCommand, registrar.Command);
        Assert.NotEqual(HorizontalAlignment.Stretch, registrar.HorizontalAlignment);
        Assert.Equal(tarjeta.Right - 1 - 24, EnVentana(m.Ventana, registrar).Right, 0);
        Assert.Single(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primario"));
        Assert.DoesNotContain(m.Vista.GetVisualDescendants().OfType<Button>(), b => b.Classes.Contains("primary"));
    }

    [AvaloniaFact]
    public void Registrar_SinProducto_Deshabilitado_YConDatosValidos_Habilitado()
    {
        var m = Montar(salida: true);
        var registrar = Nombrado<Button>(m.Vista, "BotonRegistrar");
        Assert.False(registrar.IsEffectivelyEnabled);

        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);

        Assert.True(registrar.IsEffectivelyEnabled);
        Assert.DoesNotContain(m.Vm.Productos, p => p.Nombre == "Carretilla reforzada"); // inactivo: el VM lo filtra
    }

    [AvaloniaFact]
    public void Salida_StockInsuficiente_SiNoConfirma_NoRegistraNiNavega()
    {
        var m = Montar(salida: true, confirmacion: new ConfirmacionGuionada(false));
        m.Servicio.StockActualAlFaltar = 2m;
        Completar(m, "Cable 2x1,5 mm", 5m, MotivoMovimiento.UsoOConsumo);

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal("El stock quedará en -3. ¿Confirmar la salida igual?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.False(Assert.Single(m.Servicio.Llamadas).Forzar);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<Alerta>(m.Vista, "AlertaError")));
    }

    [AvaloniaFact]
    public void Salida_StockInsuficiente_SiConfirma_RegistraForzandoYVaAlHistorial()
    {
        var m = Montar(salida: true, confirmacion: new ConfirmacionGuionada(true));
        m.Servicio.StockActualAlFaltar = 2m;
        Completar(m, "Cable 2x1,5 mm", 5m, MotivoMovimiento.UsoOConsumo);

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal(new[] { false, true }, m.Servicio.Llamadas.Select(l => l.Forzar));
        Assert.Null(m.Servicio.Llamadas[1].Dto.PrecioUnitario);
        Assert.Equal(typeof(MovimientoHistorialViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Entrada_Compra_OfreceAsociarFactura_YSiAceptaVaAlGasto()
    {
        var m = Montar(confirmacion: new ConfirmacionGuionada(true));
        Completar(m, "Cemento portland 50 kg", 40m, MotivoMovimiento.Compra, precio: 480m);

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonRegistrar"));

        Assert.Equal("Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?", Assert.Single(m.Confirmacion.Preguntas));
        Assert.Equal(480m, Assert.Single(m.Servicio.Llamadas).Dto.PrecioUnitario);
        Assert.Equal(typeof(GastoFormViewModel), m.Navegacion.UltimoTipoNavegado);
        Assert.IsType<Action<GastoFormViewModel>>(m.Navegacion.UltimoInicializador);
    }

    [AvaloniaFact]
    public void Entrada_Ajuste_NoPreguntaPorFactura_YVaAlHistorial()
    {
        var m = Montar();
        Completar(m, "Cemento portland 50 kg", 2m, MotivoMovimiento.Ajuste);

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonRegistrar"));

        Assert.Empty(m.Confirmacion.Preguntas);
        Assert.Single(m.Servicio.Llamadas);
        Assert.Equal(typeof(MovimientoHistorialViewModel), m.Navegacion.UltimoTipoNavegado);
    }

    [AvaloniaFact]
    public void Registrar_ConErrorDelServicio_MuestraLaAlertaYNoNavega()
    {
        var m = Montar();
        m.Servicio.Falla = new ReglaDeNegocioException("No se permiten movimientos sobre productos inactivos (ProductoId=1).");
        Completar(m, "Cemento portland 50 kg", 2m, MotivoMovimiento.Ajuste);
        Assert.False(ArbolVisual.EsVisibleEnArbol(Nombrado<Alerta>(m.Vista, "AlertaError")));

        Clickear(m.Ventana, Nombrado<Button>(m.Vista, "BotonRegistrar"));

        var alerta = Nombrado<Alerta>(m.Vista, "AlertaError");
        Assert.True(ArbolVisual.EsVisibleEnArbol(alerta));
        Assert.Equal(VarianteAlerta.Error, alerta.Variante);
        Assert.Equal("No se permiten movimientos sobre productos inactivos (ProductoId=1).", alerta.Texto);
        Assert.Null(m.Navegacion.UltimoTipoNavegado);
        Assert.Equal("Cemento portland 50 kg", m.Vm.ProductoSeleccionado!.Nombre);
    }

    [AvaloniaFact]
    public void CantidadInvalida_MuestraElErrorBajoElCampoYNoMueveElMotivo()
    {
        var m = Montar(salida: true);
        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);
        var caja = Nombrado<CampoFormulario>(m.Vista, "CampoCantidad").GetVisualDescendants().OfType<TextBox>().First();
        var yMotivo = Campo(m, "CampoMotivo").Y;

        caja.Focus();
        caja.Text = "5.4";
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(caja.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "Usá coma para los decimales: 5,4" && ArbolVisual.EsVisibleEnArbol(t));
        Assert.Equal(yMotivo, Campo(m, "CampoMotivo").Y);
        Assert.False(Nombrado<Button>(m.Vista, "BotonRegistrar").IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Combos_ProductoConXEntreDigitos_SinCalt()
    {
        var m = Montar(salida: true);
        Completar(m, "Cable 2x1,5 mm", 3m, MotivoMovimiento.UsoOConsumo);

        var texto = Nombrado<ComboBox>(m.Vista, "ComboProducto").GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Cable 2x1,5 mm");
        var rasgos = texto.GetValue(TextElement.FontFeaturesProperty);

        Assert.NotNull(rasgos);
        Assert.Contains(rasgos!, f => f.Tag == "calt" && f.Value == 0);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj --filter "FullyQualifiedName~MovimientoFormControlV2Tests"`
Expected: FAIL (no existen las partes nombradas: la vista vieja usa `HeaderVista`, `Controls.CampoFormulario` y el `Border.card` de 480). Si alguno pasa en rojo por OTRA razón (un fake que no compila contra la interfaz real), corregir el test antes de seguir: el `using` o la firma, nunca el expected.

- [ ] **Step 2: El XAML**

Reemplazar `src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml` entero por:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Movimientos"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:cat="using:StockApp.Application.Catalogo"
             xmlns:enums="using:StockApp.Domain.Enums"
             xmlns:v2="using:StockApp.Presentation.Controls.V2"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1040" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Movimientos.MovimientoFormControl"
             x:DataType="vm:MovimientoRegistroViewModelBase"
             Classes="ui2"
             Background="{DynamicResource V2FondoBrush}">

    <!-- UI v2 (spec §5.2), compartido por Registrar Entrada y Registrar Salida. Espejo:
         docs/ui-v2/mockups/inventario/entrada-*.html y salida-uso.html. Es pantalla del sidebar, no un alta
         que vuelve a un listado: sin breadcrumb, y solo el botón que el ViewModel expone. -->
    <ScrollViewer x:Name="Desplazamiento" VerticalScrollBarVisibility="Auto" HorizontalScrollBarVisibility="Disabled">
        <StackPanel Margin="28,24">

            <v2:EncabezadoPagina x:Name="Encabezado" Titulo="{Binding Titulo}" />

            <!-- Columna * con MaxWidth 760: la card mide 760 a la izquierda. UseLayoutRounding False en la
                 grilla: con el redondeo Avalonia infla en 1 px una columna acotada por MaxWidth (760 sale 761);
                 la card lo recupera para su contenido. -->
            <Grid x:Name="ColumnaFormulario" UseLayoutRounding="False">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*" MaxWidth="{StaticResource V2AnchoFormularioMax}" />
                </Grid.ColumnDefinitions>

                <v2:TarjetaContenido x:Name="Tarjeta" Padding="24" UseLayoutRounding="True">
                    <StackPanel>
                        <!-- El 16 entre filas va en el Margin superior de cada campo y no en filas separadoras:
                             cuando el precio no aplica (toda Salida) su fila desaparece entera, sin hueco doble. -->
                        <Grid ColumnDefinitions="*,16,*" RowDefinitions="Auto,Auto,Auto,Auto">
                            <v2:CampoFormulario x:Name="CampoProducto" Grid.Row="0" Grid.Column="0" Grid.ColumnSpan="3"
                                                Etiqueta="Producto" Requerido="True">
                                <ComboBox x:Name="ComboProducto"
                                          ItemsSource="{Binding Productos}"
                                          SelectedItem="{Binding ProductoSeleccionado}"
                                          PlaceholderText="Elegí un producto"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="cat:ProductoDto">
                                            <TextBlock Text="{Binding Nombre}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>

                            <v2:CampoFormulario x:Name="CampoCantidad" Grid.Row="1" Grid.Column="0" Margin="0,16,0,0"
                                                Etiqueta="Cantidad" Requerido="True">
                                <TextBox Text="{Binding Cantidad, Converter={x:Static conv:DecimalConverter.Instance}}"
                                         beh:EntradaNumericaBehavior.EsDecimal="True"
                                         PlaceholderText="Ingresá la cantidad" />
                            </v2:CampoFormulario>

                            <v2:CampoFormulario x:Name="CampoMotivo" Grid.Row="1" Grid.Column="2" Margin="0,16,0,0"
                                                Etiqueta="Motivo">
                                <ComboBox x:Name="ComboMotivo"
                                          ItemsSource="{Binding MotivosDisponibles}"
                                          SelectedItem="{Binding Motivo}"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate x:DataType="enums:MotivoMovimiento">
                                            <TextBlock Text="{Binding ., Converter={x:Static conv:MotivoMovimientoConverter.Instance}}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                            </v2:CampoFormulario>

                            <!-- PrecioUnitario es decimal? y solo aplica a una Entrada (obligatorio en Compra; la regla
                                 vive en MotivoMovimientoExtensions). DecimalOpcionalConverter trata "" como null: el
                                 conversor default explota con InvalidCastException al vaciar el campo. El nombre
                                 PrecioUnitarioBox queda en el TextBox: lo buscan los tests por Name. -->
                            <v2:CampoFormulario x:Name="CampoPrecio" Grid.Row="2" Grid.Column="0" Margin="0,16,0,0"
                                                Etiqueta="Precio unitario"
                                                Requerido="{Binding PrecioObligatorio}"
                                                IsVisible="{Binding PrecioVisible}">
                                <TextBox x:Name="PrecioUnitarioBox"
                                         Text="{Binding PrecioUnitario, Converter={x:Static conv:DecimalOpcionalConverter.Instance}}"
                                         beh:EntradaNumericaBehavior.EsDecimal="True"
                                         PlaceholderText="{Binding PrecioPlaceholder}" />
                            </v2:CampoFormulario>

                            <v2:CampoFormulario x:Name="CampoComentario" Grid.Row="3" Grid.Column="0" Grid.ColumnSpan="3"
                                                Margin="0,16,0,0" Etiqueta="Comentario">
                                <TextBox Text="{Binding Comentario}"
                                         PlaceholderText="Comentario opcional"
                                         AcceptsReturn="True"
                                         TextWrapping="Wrap"
                                         Height="88" />
                            </v2:CampoFormulario>
                        </Grid>

                        <v2:Alerta x:Name="AlertaError"
                                   Margin="0,8,0,0"
                                   Variante="Error"
                                   Texto="{Binding MensajeError}"
                                   IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}" />

                        <Border x:Name="PieFormulario"
                                Margin="0,20,0,0"
                                Padding="0,16,0,0"
                                BorderThickness="0,1,0,0"
                                BorderBrush="{DynamicResource V2SeparadorGrillaBrush}">
                            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                                <Button x:Name="BotonRegistrar" Classes="primario" Content="Registrar" Command="{Binding RegistrarCommand}" />
                            </StackPanel>
                        </Border>
                    </StackPanel>
                </v2:TarjetaContenido>
            </Grid>
        </StackPanel>
    </ScrollViewer>

</UserControl>
```

(Si el compilador de bindings rechaza `x:DataType="enums:MotivoMovimiento"` con `{Binding .}`, quitar el `x:DataType` de ESE template (Avalonia lo infiere del `ItemsSource`, como hacía la vista vieja) y anotarlo en el reporte con el error textual.)

Run: `dotnet build StockApp.sln` → 0 errores. Después, el filtro del Step 1 → PASS.

- [ ] **Step 3: Tests heredados**

1. `MovimientoFormControlPrecioPorMotivoTests.cs` L8: `using StockApp.Presentation.Controls;` → `using StockApp.Presentation.Controls.V2;`. `OfType<CampoFormulario>().First(c => c.Etiqueta == "Precio unitario")` (L44-45) no cambia de texto: ahora resuelve el tipo v2.
2. `GuardarBloqueadoConErroresDeEntradaTests.cs`, `CajaDeCampo` (L61-66), reemplazar por (Productos y Entrada/Salida ya son v2; ninguna vista de este archivo usa el viejo):

```csharp
    // ProductoFormView y MovimientoFormControl ya son v2.
    private static TextBox CajaDeCampo(Window window, string etiqueta)
        => window.GetVisualDescendants().OfType<StockApp.Presentation.Controls.V2.CampoFormulario>()
            .Single(c => c.Etiqueta == etiqueta)
            .GetVisualDescendants().OfType<TextBox>().First();
```

   y borrar el `using StockApp.Presentation.Controls;` si queda sin uso (el build lo dice con una advertencia; no dejar ambigüedad de `CampoFormulario`).
3. `GuardianDePatronTests.cs`: borrar las tres `[InlineData]` de `MovimientoFormControl`, `EntradaRegistroView` y `SalidaRegistroView` de `Vista_TieneHeaderVistaConElTituloEsperado` (L53-55); sacar las tres de `VistasDeLaTanda` (L110-112); agregar `typeof(MovimientoFormControl), typeof(EntradaRegistroView), typeof(SalidaRegistroView),` al final de `VistasMigradasAV2`. En el comentario de L41-47, quitar los casos P3 (`MovimientoFormControl`) y P5 (los wrappers): ya no quedan vistas de esos patrones en el guardián viejo.
4. Sin cambios, pero tienen que seguir verdes: `MovimientoFormControlMotivoTextoTests`, `MovimientoFormControlValidacionTests`, `MovimientoFormControlCantidadCulturaTests`, `ComandosDeGuardadoGuardianTests` (`StockApp.Presentation.Tests`).

Run:

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
```

Expected: todo verde, incluidos `GuardianDePatronTests.Guardian_CubreTodasLasVistasDelEnsamblado`, `VistaV2_NoTieneOpacidadesLiterales` y `VistaV2_BotonEstiradoEstiraSuContenido` sobre las tres vistas (el botón ya no se estira), `GuardarBloqueadoConErroresDeEntradaTests` (Productos y Entrada/Salida), `ShellMainViewV2Tests` y `ShellMainViewGatesTests`.

- [ ] **Step 4: Comparaciones (rojo → verde)**

`tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/MovimientosComparacionTests.cs`:

```csharp
using Avalonia.Headless.XUnit;
using StockApp.Presentation.Views;
using StockApp.Presentation.VisualTests.Comparacion;
using StockApp.UiV2.Herramientas;

namespace StockApp.Presentation.VisualTests.Pantallas.Inventario;

/// <summary>
/// Registrar Entrada / Salida (spec §5.2) dentro del shell real, con la vista resuelta por ViewLocator y el
/// sidebar oculto en los dos renders. El estado se aplica en prepararApp, después de que InicializarAsync
/// cargó los productos (la carga vacía la lista y el combo suelta la selección).
/// </summary>
[Trait("Categoria", "ComparacionVisual")]
public class MovimientosComparacionTests
{
    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    private static void Comparar(DatosMovimientos.Estado estado, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null)
    {
        var (shell, vm) = DatosMovimientos.EnShell(estado);
        ComparacionVisual.CompararConMockup(new ShellMainView(), shell, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup,
            prepararApp: vista =>
            {
                ProductosComparacionTests.OcultarSidebar(vista);
                DatosMovimientos.Aplicar(vm, estado);
            });
    }

    [AvaloniaFact]
    public void EntradaCompra_IgualAlMockup() => Comparar(DatosMovimientos.EntradaCompra, "inventario/entrada-compra.html");

    [AvaloniaFact]
    public void EntradaCompra_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaCompra, "inventario/entrada-compra.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void EntradaAjuste_IgualAlMockup() => Comparar(DatosMovimientos.EntradaAjuste, "inventario/entrada-ajuste.html");

    [AvaloniaFact]
    public void EntradaAjuste_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaAjuste, "inventario/entrada-ajuste.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void EntradaError_IgualAlMockup() => Comparar(DatosMovimientos.EntradaError, "inventario/entrada-error.html");

    [AvaloniaFact]
    public void EntradaError_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.EntradaError, "inventario/entrada-error.html", TamanoLienzo.Municipio);

    [AvaloniaFact]
    public void SalidaUso_IgualAlMockup() => Comparar(DatosMovimientos.SalidaUso, "inventario/salida-uso.html");

    [AvaloniaFact]
    public void SalidaUso_IgualAlMockup_1366x768() =>
        Comparar(DatosMovimientos.SalidaUso, "inventario/salida-uso.html", TamanoLienzo.Municipio);
}
```

Run: `scripts/ui-v2/comparar.sh Movimientos` (los 8 tests; el filtro matchea el nombre de la clase).
Expected: las 8 APROBADAS con el umbral calibrado y sin recortes. MIRAR la página de cada una aunque pase (la comparación no ve un glifo ni un borde de 1 px): combo con el producto elegido (si sale vacío, el estado no se aplicó: `DatosMovimientos.Aplicar` debería haber lanzado), "2x1,5" con "x" y no "×", la alerta en dos líneas igual que el mockup. Causas esperables si no pasan: el combo (compensación `-3px` del glifo ya en `base.css`), la alerta de dos líneas (`LineHeight` 16 ya en la `Alerta`), un medio píxel por alto impar (compensación entera de §6.5), márgenes colapsados en Chromium que Avalonia suma (no debería haber: cada margen superior cae dentro de un `Grid`). Diagnosticar con `teselas.py`/`tinta.py` antes de tocar nada.

- [ ] **Step 5: Umbral (solo si, con la geometría ya idéntica, las teselas densas pasan de 11) — GATE**

PARAR, presentar CADA tesela densa al usuario y, solo con OK explícito, registrar `UmbralAprobado(teselas, "Martin", "<yyyy-MM-dd>", "<motivo medido>")` en ESA prueba. Si el valor propuesto es 16 o más: `entrada-error` tiene relleno pálido propio (QUITAR `Variante="Error"` de `AlertaError` es la mutación pálida, registrarla en `MutacionPalidaEnRojo`); las otras tres no lo tienen: PARAR y decírselo al usuario, no se inventa una mutación.

- [ ] **Step 6: Mutación quitando**

```bash
export SCRATCH=~/.cache/stockapp-ui-v2-fase-3b && mkdir -p "$SCRATCH"
cp src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml "$SCRATCH/MovimientoFormControl.axaml.bak"
```

Una por vez, con `git diff --no-index` antes de correr, el rojo PEGADO y la restauración verificada:
(a) Quitar `IsVisible="{Binding PrecioVisible}"` de `CampoPrecio` → rojo en `MovimientoFormControlPrecioPorMotivoTests.Salida_NuncaMuestraElCampoDePrecio`, `SinFilaDePrecio_ElComentarioQuedaA16DeLaFilaDeCantidad` y `SalidaUso_IgualAlMockup`.
(b) Quitar `Margin="0,16,0,0"` de `CampoComentario` → rojo en `ConPrecio_LasFilasVanA16`, `SinFilaDePrecio_…` y las comparaciones.
(c) Quitar `Grid.ColumnSpan="3"` de `CampoProducto` → rojo en `Campos_ProductoYComentarioACompleto_…`.
(d) Quitar `MaxWidth="{StaticResource V2AnchoFormularioMax}"` → rojo en `Tarjeta_Mide760YQuedaALaIzquierda`.
(e) Quitar `UseLayoutRounding="False"` de `ColumnaFormulario` → anotar si `Tarjeta_Mide760…` da 761 (lo que motivó la compensación en la 3a); si queda verde, anotarlo: la compensación no aplica acá y se evalúa quitarla con el usuario en el GATE final.
(f) Quitar `Requerido="{Binding PrecioObligatorio}"` → rojo en `Requeridos_SegunTipoYMotivo` (y anotar si `EntradaCompra_IgualAlMockup` lo ve: es un asterisco, puede no verlo).
(g) Quitar `Variante="Error"` de `AlertaError` → rojo en `Registrar_ConErrorDelServicio_…` y `EntradaError_IgualAlMockup`.
(h) Quitar `Command="{Binding RegistrarCommand}"` de `BotonRegistrar` → rojo en `Pie_…` y en los cuatro tests de flujo.
(i) Quitar `Classes="ui2"` de la raíz → rojo en `Raiz_TieneUi2_…` y en las 8 comparaciones.
(j) Quitar `TextWrapping="Wrap"` del comentario → no hay test de efecto (un comentario largo de prueba no está en los datos fijos): anotar que solo lo custodia la verificación orgánica (Task 4, paso 2).

- [ ] **Step 7: Verificación de la task y commit**

```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.UiTests/StockApp.Presentation.UiTests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Presentation.VisualTests/StockApp.Presentation.VisualTests.csproj
dotnet test tests/StockApp.UiV2.Herramientas.Tests/StockApp.UiV2.Herramientas.Tests.csproj
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
git status --short   # solo los archivos de esta task; ningún .bak ni scratch
```

Expected: todo verde, `tokens --verificar` exit 0 (esta task no cambia tokens). La review de esta task corre los mismos seis comandos.

```bash
git add src/StockApp.Presentation/Views/Movimientos/MovimientoFormControl.axaml tests/StockApp.Presentation.UiTests/V2/MovimientoFormControlV2Tests.cs tests/StockApp.Presentation.UiTests/MovimientoFormControlPrecioPorMotivoTests.cs tests/StockApp.Presentation.UiTests/GuardarBloqueadoConErroresDeEntradaTests.cs tests/StockApp.Presentation.UiTests/GuardianDePatronTests.cs tests/StockApp.Presentation.VisualTests/Pantallas/Inventario/MovimientosComparacionTests.cs
git commit -m "feat(ui-v2): Registrar entrada y salida v2 con precio condicional, diálogos y error comparados"
```

## Bloque D — Cierre de la fase

### Task 4: Suite completa, verificación orgánica, aprobación final, herramientas y spec

**Files:**
- Modify: `docs/ui-v2/herramientas.md` (convenciones 15 y 16)
- Modify: `docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md` (Estado, §7, deuda trasladada a 3c)
- Modify (local): el ledger

**Interfaces:**
- Consumes: todo lo anterior.
- Produces: la Fase 3b cerrada en `feat/ui-v2` (sin merge a `main`).

- [ ] **Step 1: Build, suite, herramientas y comparación de toda la UI v2**

```bash
dotnet build StockApp.sln
dotnet test StockApp.sln
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens --verificar
dotnet run --project tools/StockApp.UiV2.Herramientas -- fuentes --verificar
scripts/ui-v2/comparar.sh
```

Expected: 0 errores; todo verde salvo los falsos conocidos de fixtures `.ods` (confirmar por nombre); los dos `--verificar` exit 0; TODAS las filas de `artifacts/ui-v2/comparacion/index.html` APROBADAS a los dos tamaños (las de las Fases 0 a 3a sin cambios: esta fase no tocó estilos compartidos; si una se mueve, diagnosticarla). Anotar el total de tests en el ledger.

- [ ] **Step 2: Verificación orgánica (spec §6.4) — app real contra la base**

Preparación:
- `docker start stockapp-pg` (y dejarlo andando al terminar); API levantada; `~/.config/GestionMunicipal/conexion.json` a `localhost`; credencial de desarrollo `admin` / `Admin1234`.
- Operador de permisos MIXTOS (Administración → Usuarios, crear o reutilizar el de la 3a): `catalogo.productos` y `movimientos.registrar`, sin finanzas ni reportes.
- Producto de prueba "PRUEBA UI V2 3b" creado desde Productos (stock 0). Anotar en el ledger su id y el de cada movimiento que se registre.
- WSLg y `scripts/gui-verificacion/` (ventana "Gestión Municipal"). MIRAR cada captura (`capturar.sh` no es fail-closed). Tipear ANTES de cada diálogo: tras un diálogo modal WSLg pierde el teclado; si hace falta teclado otra vez, reiniciar la app.

Todo a **1366×768**; los pasos 1 y 5 se repiten a 1280×760. Capturar cada pantalla.

1. Admin → "Registrar entrada" (sidebar, queda marcado): "Registrar Entrada" sin breadcrumb, card de 760 a la izquierda, Compra por defecto, precio con asterisco y "Obligatorio", "Registrar" deshabilitado y a la derecha. Comparar a ojo con `entrada-compra.html`.
2. Elegir "PRUEBA UI V2 3b"; cantidad "5.4": error del campo y "Registrar" bloqueado con el tooltip estándar; corregir a "10". Precio "120,50". Comentario de tres líneas largas: envuelve dentro de los 88 y scrollea en vertical. Cambiar a Ajuste (asterisco se va, "Opcional") y volver a Compra.
3. "Registrar" → diálogo "Entrada registrada. ¿Desea asociar una factura…?" → "No" → Historial con la entrada.
4. "Registrar entrada" por Ajuste, 2 unidades → sin diálogo → Historial.
5. "Registrar salida": con Uso o consumo, Merma y Ajuste (elegidos con el mouse) nunca aparece el precio y el Comentario queda pegado a la fila Cantidad | Motivo sin hueco. Cantidad 20 (> stock 12) → "El stock quedará en -8. ¿Confirmar la salida igual?" → "No": se queda en el formulario y no se registra nada (verificarlo después en el Historial). Repetir (reiniciar la app si el teclado se perdió) → "Sí" → Historial con la salida y el stock en -8.
6. Error real: detener SOLO el proceso de la API (por su terminal o por nombre `StockApp.Api`; nunca un comando de alcance global) y registrar una entrada por Ajuste → `Alerta` error "No se pudo conectar con el servidor…" en dos líneas, sin navegar y con los datos cargados. Volver a levantar la API.
7. "Registrar entrada" por Compra → "Sí" a asociar factura → formulario de gasto precargado con el monto (cantidad × precio). Salir sin guardar.
8. Operador mixto: ve "Registrar entrada" y "Registrar salida" (sidebar y accesos de Inicio); repetir 3 y 5. Repetir 7 y ANOTAR qué pasa al aceptar "asociar factura" sin finanzas (preexistente: no se arregla en esta fase; si rompe, avisar al usuario con la evidencia).
9. Datos de prueba: dar de baja "PRUEBA UI V2 3b" desde Productos. Los movimientos no tienen baja en la app: listar sus ids en el ledger y PREGUNTAR al usuario si los borra (comando SQL listo, acotado por esos ids, SOLO contra el contenedor local `stockapp-pg`; nunca el VPS). El contenedor queda andando.

Si algún paso no se puede hacer por el entorno (WSLg caído, contenedor o API que no arrancan), PARAR y avisar con la evidencia (stderr, `docker ps -a`), sin declarar la fase cerrada: la verificación la hace el usuario.

- [ ] **Step 3: Aprobación final del usuario**

PARAR. Presentar: el `index.html` (las 8 filas de Entrada/Salida), las capturas de la verificación orgánica junto a los mockups, el resultado de la mutación (e) y (j), y la lista de cambios respecto de los mockups aprobados (si hubo). La aprobación final es del usuario mirando la app (spec §6.4). Registrar en el ledger: `Fase 3b: aprobada por <quién> <fecha>`.

- [ ] **Step 4: `herramientas.md` y spec**

`docs/ui-v2/herramientas.md`, sección "Convenciones de la convivencia (hasta la Fase 9)", después de la 14: "Convenciones que agrega la Fase 3b (Entrada/Salida)" con la 15 y la 16 de este plan (con las D# como quedaron aprobadas).

Spec:
1. Línea `**Estado:**`: agregar "; Fase 3b (Entrada/Salida) APROBADA por el usuario el <fecha>" antes de "(merge a main al final de todas las fases)", y "3b" a la lista de fases implementadas.
2. §7, después de "Estado de la Fase 3a": "**Estado de la Fase 3b (aprobada el <fecha>).** `MovimientoFormControl` v2 (Registrar entrada y salida), comparado en 4 estados a los dos tamaños (<máximo de teselas densas>); precio solo en Entrada y sin hueco en Salida; diálogos de stock insuficiente y de asociar factura verificados en la app real a 1366×768 y 1280×760 con Administrador y operador mixto. Decisiones del usuario: <D1 a D13 aprobadas o cambiadas>. Formulario de pantalla de sidebar sin breadcrumb: §4.4 rige para altas y ediciones que vuelven a un listado."
3. "Deuda abierta tras la Fase 3a" (U3):
   - En "Mientras corre la carga inicial…", reemplazar "Decidir el patrón ANTES de la 3b: se repite en cada listado." por "Se decide en la 3c (Historial de movimientos es el próximo listado): la 3b no tiene listados ni estado vacío (decisión del 2026-10-07)."
   - En "Las columnas `Auto` de contenido libre…", reemplazar "se resuelve con UN criterio común para todas las grillas al migrar la 3b y la 3c" por "se resuelve con UN criterio común para todas las grillas al migrar la 3c (la 3b no tiene grillas)".
   - En "`UseLayoutRounding="False"` va por instancia en `EstadoVacio`…", agregar "La 3b no usa `EstadoVacio`; se evalúa en la 3c."
4. Agregar "**Deuda abierta tras la Fase 3b:**" con lo que haya surgido (como mínimo: el resultado del paso 8 de la verificación orgánica si fue un problema; la cantidad que arranca en "0" sin marca de agua visible (D8, pide `decimal?` en el VM); el wrap del comentario sin test de efecto (mutación j); y cualquier `UmbralAprobado` registrado, con pantalla y número).
5. §6.5: cada compensación nueva medida en la Task 3 (si hubo), con su antes y después.

```bash
git add docs/ui-v2/herramientas.md docs/superpowers/specs/2026-09-28-ui-v2-rediseno-total-design.md
git commit -m "docs(ui-v2): cierre de la Fase 3b (Entrada/Salida)"
```

No se pushea ni se mergea.

---

## Fuera de alcance

- **3c (U1):** `IngresoPorFacturaView`, `MovimientoHistorialView`, el patrón de modal v2, el criterio común de columnas `Auto` y el patrón del estado vacío durante la carga (U3).
- **ViewModels:** ninguno cambia. Quedan como están: la cantidad `decimal` que arranca en 0 (D8), el 403 tragado de `InicializarAsync` (D2), el combo de productos sin búsqueda, la navegación a `GastoFormViewModel` para un usuario sin finanzas (se observa en la Task 4, no se arregla).
- El gate del sidebar que exige `catalogo.productos` además de `movimientos.registrar` (deuda preexistente anotada en la spec).
- Los diálogos (ya v2 desde la Fase 1, D13). Modo oscuro, back-stack, cambios de API (spec §10).

## Riesgos

| Riesgo | Mitigación en el plan |
|---|---|
| `InicializarAsync` vacía `Productos` y el combo pone `ProductoSeleccionado` en `null`: la comparación saldría con el combo vacío y pasaría el umbral (es poco texto). | El estado se aplica en `prepararApp` después de la carga y `DatosMovimientos.Aplicar` lanza si la carga no corrió (`DatosMovimientosTests`, Task 1); mirar cada página de comparación (Task 3 Step 4). |
| Las filas espaciadoras de un `Grid` dejan un hueco de 16 extra al ocultarse el precio. | D5: espacio en el `Margin` de cada campo; `SinFilaDePrecio_…` con los 3 motivos de Salida y la mutación (b). |
| Mover las tres vistas a `VistasMigradasAV2` les saca `Vista_NoTieneUnSegundoBotonPrimario`. | `Pie_RegistrarEsElUnicoPrimarioYQuedaALaDerecha` sobre la View real. |
| `x:DataType` de enum en el template de Motivo no compila con `{Binding .}`. | Fallback documentado en el Step 2 de la Task 3 (inferencia por `ItemsSource`, como la vista vieja), anotado en el reporte. |
| La alerta de dos líneas o el combo con glifo corren 1 px. | Compensaciones existentes (`LineHeight` 16 de la `Alerta`, `-3px` del glifo); diagnóstico con `teselas.py`/`tinta.py` antes de tocar umbral. |
| `UseLayoutRounding="False"` copiado de Productos puede no hacer falta en este alto de card. | Mutación (e): si queda verde, se anota y se decide con el usuario. |
| WSLg pierde el teclado tras cada diálogo modal y los scripts salen con exit 0. | Orden de pasos que tipea antes de los diálogos; reiniciar la app si hace falta teclado; mirar cada captura. |
| Los movimientos de prueba no se pueden borrar desde la app. | Ids en el ledger y borrado por SQL solo contra el contenedor local, con OK del usuario (Task 4, paso 9). |
| Un usuario sin finanzas que acepta "asociar factura" llega a un formulario de gasto que no puede cargar. | Se observa y se informa (paso 8); preexistente, fuera de alcance. |
| Un script del skill deja su archivo fuera del worktree. | `cd <worktree> &&` y `ls` del archivo resultante (Global Constraints). |
