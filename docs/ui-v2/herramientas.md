# Herramientas de la UI v2

## Una vez por máquina

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- instalar-navegador
```

Instala el Chromium que usa Microsoft.Playwright 1.63.0 (rev 1243) en `~/.cache/ms-playwright`.

## Comando único de comparación

```bash
scripts/ui-v2/comparar.sh            # todas las pantallas
scripts/ui-v2/comparar.sh Productos  # solo los tests cuyo nombre contiene "Productos"
scripts/ui-v2/comparar.sh 1366x768   # solo las corridas a 1366x768 (los gemelos _1366x768)
```

Abre `artifacts/ui-v2/comparacion/index.html`: una fila por pantalla y tamaño (1280x760 y
1366x768, agrupadas por pantalla, el estándar primero) con el porcentaje de píxeles distintos, las
teselas densas y el veredicto. Cada pantalla tiene su página con mockup, app y
diferencias lado a lado (rojo = diferencia fuerte; naranja = diferencia leve de color; contorno
magenta = tesela densa de 16x16: 25% o más de diferencias fuertes, o 90% o más de leves).

Un filtro que no coincide con ningún test (p. ej. un typo en el nombre de pantalla) hace que el
script termine con exit distinto de cero y el mensaje "ningún test coincide con el filtro", en vez
de mostrar el link a un `index.html` viejo como si fuera un resultado válido.

## Tokens

`docs/ui-v2/tokens.json` es la única fuente. Después de editarlo:

```bash
dotnet run --project tools/StockApp.UiV2.Herramientas -- tokens
```

Regenera `src/StockApp.Presentation/Themes/V2/Tokens.axaml` y `docs/ui-v2/mockups/_base/tokens.css`.
Nunca se editan a mano: `ArchivosGeneradosSincronizadosTests` falla si difieren del JSON.

## Mockup de una pantalla

1. `docs/ui-v2/mockups/<modulo>/<pantalla>.html`, con `../_base/tokens.css` y `../_base/base.css`,
   todo adentro de `<div class="v2-ventana">`, datos fijos. El mismo HTML se aprueba a 1280x760 y a
   1366x768: `.v2-ventana` mide `var(--v2-ventana-ancho, 1280px)` x `var(--v2-ventana-alto, 760px)`
   y el capturador define esas variables según el tamaño pedido. Abierto a mano en el navegador
   mide 1280x760. Lo que tenga ancho fijo adentro no se estira solo: a 1366 se ajusta pantalla por
   pantalla al aprobar el mockup.
   La captura exige exactamente un `<div class="v2-ventana">` que mida el lienzo pedido (1280×760 o
   1366×768) en (0,0); si falta, si hay dos o si un estilo de la página le pisa el ancho o le pone
   margen, falla.
2. Íconos: `dotnet run --project tools/StockApp.UiV2.Herramientas -- icono-svg mdi-<nombre>` y pegar el SVG.
3. Solo técnicas de la spec §6.1. Las clases `v2-*` de `base.css` replican la geometría de los
   componentes de `Controls/V2`.
4. Celdas numéricas de grilla: la celda lleva `v2-num` y el valor va SIEMPRE dentro de un `<span>`
   (`.v2-num > span`), que hace de TextBlock de la celda:

   ```html
   <div class="v2-grilla-celda v2-num"><span>12.50</span></div>
   ```

   `.v2-num` alinea a la derecha con cifras tabulares; el `span` redondea su ancho hacia arriba
   como Avalonia (el TextBlock mide el ceil del ancho del texto y lo dibuja pegado a su izquierda).
   Sin el `span` la cifra queda hasta 1px corrida a la derecha. El redondeo usa `calc-size()`, que
   solo funciona en Chromium (el navegador de la comparación).

   La convención la custodia `VerificadorConvencionesTests.MockupsDelRepo_RespetanLaConvencion` (todos
   los HTML de `docs/ui-v2/mockups/` salvo `referencia/`).

## Test por pantalla

En `tests/StockApp.Presentation.VisualTests`, con `[Trait("Categoria", "ComparacionVisual")]`:

```csharp
[AvaloniaFact]
public void Productos_IgualAlMockup() =>
    ComparacionVisual.CompararConMockup(new ProductoListView(), vmConLosMismosDatosDelMockup, "inventario/productos.html");

[AvaloniaFact]
public void Productos_IgualAlMockup_1366x768() =>
    ComparacionVisual.CompararConMockup(new ProductoListView(), vmConLosMismosDatosDelMockup, "inventario/productos.html",
        tamano: TamanoLienzo.Municipio);
```

Toda pantalla tiene los dos tests, con el MISMO umbral por defecto (spec §6.1/§6.2: varias PCs del
municipio son de 1366x768). Un `UmbralAprobado` es POR TAMAÑO: se registra solo en el test del tamaño
que lo necesitó tras revisar sus teselas con el usuario (precedentes: Ingreso por factura, con Renglones (13) y Diferencia (12) en la
3c-3 y Guardado (19) en la 4a, todos aprobados a 1280x760 y con el umbral por defecto a 1366x768); el otro tamaño sigue con el
calibrado. `tamano` es opcional (sin él, `TamanoLienzo.Estandar`, 1280x760) y se pasa
siempre con argumento nombrado. La corrida a 1366x768 deja su página en
`artifacts/ui-v2/comparacion/<pantalla>@1366x768/` y no pisa la del tamaño estándar. Si una
pantalla pasa a 1280x760 y no a 1366x768, se diagnostica (¿un ancho fijo en el mockup?); el umbral
no se sube para 1366.

El umbral por defecto es el calibrado en la Fase 0 (spec §6.5). No se sube por pantalla sin
aprobación del usuario, y la aprobación queda en el código:

1. Abrir la página de comparación de la pantalla y revisar con el usuario CADA tesela densa
   (contorno magenta): tiene que ser solo tono de texto, sin nada corrido ni de otro color.
2. Registrar el valor aprobado (pasar `teselasDensasMax` mayor a 11 o `umbral` mayor a 1.68 sin
   esto falla antes de renderizar). Si también hay que superar el porcentaje, se agrega
   `PorcentajeMaximo: <valor>` al mismo registro:

   ```csharp
   ComparacionVisual.CompararConMockup(new ProductoListView(), vm, "inventario/productos.html",
       aprobado: new UmbralAprobado(14, "Martin", "2026-10-02", "grilla de 12 filas: 14 teselas, todas tono de texto"));
   ```

3. Si el valor es 16 o más (lo que dio M2, relleno pálido equivocado, en el catálogo), antes hay
   que correr en esa pantalla la mutación equivalente a M2 (quitar la variante de color de un
   relleno pálido), ver el rojo y registrarlo en el quinto parámetro, `MutacionPalidaEnRojo`
   (qué se quitó y cuántas teselas dio). Sin eso falla. (El último parámetro es `PorcentajeMaximo`,
   del paso 2.)

Además de los píxeles, `CompararConMockup` falla si algún `TextBlock` visible no se ve entero:
pide más lugar del que tiene, o cae fuera de un ancestro con `ClipToBounds` o de la ventana (un
glifo que falta no supera el ruido de píxeles). El mensaje lista cada texto. El viewport de un
`ScrollViewer` no cuenta en el eje en que se puede scrollear, y lo mismo el `DataGrid` (la última
fila parcial o una columna fuera de vista no se reportan; una celda angosta con texto largo sí). `TextTrimming` no exime: si el
mockup usa `text-overflow: ellipsis`, el test declara esos textos en `recortesPermitidos`. También mira el valor y el placeholder de los `TextBox` (incluidos los internos de `NumericUpDown`, `CalendarDatePicker` y `ComboBox` editable), los `TextBlock` con `Inlines` y los cortados por `MaxLines`. Un `TextBox` multilínea relaja el eje vertical (se lee scrolleando); ya no hace falta `recortesPermitidos` para un motivo largo en un campo de alto fijo.

Diferencia de tono aceptada: el texto de la app sale más claro que en el mockup (rasterización de
Skia frente a Chromium) con la geometría idéntica: entre 7% y 18% en el catálogo (títulos 600 y
texto secundario), y entre 23,5% y 26% en texto 400 sobre `#1C1917` de 13 a 14 px (filas de tarea de
Inicio, cuerpo del diálogo de confirmación de la Fase 1; spec §6.5). El usuario aceptó la del
catálogo el 2026-09-30 y la del diálogo al aprobar la Fase 1 (2026-10-05). Es el grueso del ruido que absorbe el umbral: en la página de comparación se ve como
naranja y rojo fino en los bordes de los glifos. Lo que se exige igual es la geometría.

Diálogos (`Window`): el contenido va en un `Border x:Name="Contenido" Classes="ui2"` (no en el
`Window`), y el test compara su área cliente con `ContenidoDeDialogo.Extraer(dialogo)` (y su
gemelo `_1366x768`, como toda pantalla):

    ComparacionVisual.CompararConMockup(ContenidoDeDialogo.Extraer(new ConfirmacionDialog("…")), null, "dialogos/confirmacion.html");

El mockup es el área cliente (sin barra de título) con el ancho del `Window`, centrada sobre fondo app.

Pantallas de contenido (Inicio y las que vienen): se comparan DENTRO del shell real. El test
renderiza `ShellMainView` con `CurrentContent` = el VM de la pantalla (lo resuelve `ViewLocator`,
como en la app; `DatosShell.Crear(usuario, permisos, seccionActiva, contenido)`) y oculta el
sidebar en AMBOS renders: en la app con `prepararApp` (`Border#Sidebar.IsVisible = false`, la
columna de 240 queda en fondo app) y en el mockup con `ocultarEnMockup: [".v2-sidebar"]`
(`visibility: hidden`). El sidebar se compara en `ShellComparacionTests`; el mockup de cada pantalla
igual lo dibuja, para que el usuario apruebe la pantalla entera. `prepararApp` corre con la ventana
abierta después del primer layout y antes de capturar (`RenderizadorVista.RenderizarAPng`,
parámetro `preparar`), y se pasa siempre con argumento nombrado. Si la parte que oculta no existe,
lanza: la comparación no corre sobre una vista que perdió lo que excluye.

    private static readonly string[] OcultarEnMockup = [".v2-sidebar", ".marca-agua"];

    public static void OcultarSidebarYMarcaDeAgua(Control vista)
    {
        var shell = (ShellMainView)vista;
        shell.FindControl<Border>("Sidebar")!.IsVisible = false;
        var inicio = shell.GetVisualDescendants().OfType<InicioView>().Single();
        inicio.FindControl<Image>("MarcaAgua")!.IsVisible = false;
    }

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebarYMarcaDeAgua);

Fechas en mockups con reloj inyectable: un VM que muestra "hoy" recibe un `TimeProvider` opcional
(default `TimeProvider.System`); los datos fijos de los tests le pasan un reloj fijo con zona UTC
(`DatosInicio.Ahora`, martes 6 de octubre de 2026 13:00), así la pantalla no cambia con el día ni con
la zona de la máquina.

Ejemplo de la Fase 3a (Productos, shell real con solo el sidebar oculto; `ProductosComparacionTests`):

    private static readonly string[] OcultarEnMockup = [".v2-sidebar"];

    public static void OcultarSidebar(Control vista) =>
        ((ShellMainView)vista).FindControl<Border>("Sidebar")!.IsVisible = false;

    private static void Comparar(ShellMainViewModel vm, string mockup, TamanoLienzo? tamano = null, UmbralAprobado? aprobado = null) =>
        ComparacionVisual.CompararConMockup(new ShellMainView(), vm, mockup, tamano: tamano, aprobado: aprobado,
            ocultarEnMockup: OcultarEnMockup, prepararApp: OcultarSidebar);

    [AvaloniaFact]
    public void Productos_IgualAlMockup() => Comparar(DatosProductos.ConProductos(), "inventario/productos.html");

## Otros comandos

- `tokens --verificar`, `fuentes`, `fuentes --verificar`
- `capturar-mockup <html> <png> [--tamano 1366x768]`: captura suelta de un mockup, a 1280x760 o al
  tamaño pedido (`1280x760` o `1366x768`; otro valor sale con código 2 y lista los válidos).
- `comparar-png <referencia> <candidata> <diff>`: diff suelto de dos PNG del mismo tamaño (sirve
  para comparar capturas de la app real antes y después).

## Convenciones de la convivencia (hasta la Fase 9)

- Estilos base v2 solo bajo `.ui2`: la RAÍZ de una vista migrada lleva `Classes="ui2"`. Nunca en un
  contenedor que hospede vistas no migradas.
- Componentes v2: `xmlns:v2="using:StockApp.Presentation.Controls.V2"`.
- Tokens v2: claves `V2*` y variables `--v2-*`.
- Headers de `DataGridColumn` en MAYÚSCULAS (spec §2.3): Avalonia no tiene `text-transform`, así que
  el `Header` de cada columna se escribe ya en mayúsculas a mano (en el mockup, `--v2-*-transformacion:
  uppercase` sí lo hace vía CSS).
- Gates y ubicación de `ui2` se custodian en `StockApp.Presentation.UiTests` con la View real, no con la
  comparación de píxeles: un header de sección de más agrega píxeles pero queda
  bajo el umbral calibrado, y un `ui2` mal ubicado puede no cambiar ningún píxel del estado comparado.
- Tarjetas en columnas iguales: `GrillaUniformeV2`, nunca `UniformGrid`: `UniformGrid` redondea el
  ANCHO de la celda y acumula el error (a 1366 la tercera tarjeta se corre 1 px y se pasa 1 px del
  borde); `GrillaUniformeV2` redondea cada borde por separado y salta los hijos invisibles (gates).
  Espejo en el mockup: `class="v2-grilla-uniforme v2-grilla-uniforme--3"` (o `--2`) de `base.css` y
  `--v2-grilla-espaciado` igual al `Espaciado` (`var(--v2-espacio-12)`). Esas clases ya traen la receta
  correcta: columnas de ancho entero `round(nearest, k * celda, 1px) - round(nearest, (k - 1) * celda, 1px)`
  y la del medio `minmax(0, 1fr)` (spec §6.5, compensación B). NO usar `repeat(N, minmax(0, 1fr))`:
  deja los bordes interiores en x fraccionaria y el texto de esas columnas no coincide con la app.
  Para otro N, armar la misma receta en una clase nueva de `base.css`.
- Fila clickeable de panel: `V2TemaFilaPanel` (`Theme` de `Button`), con su espejo `.v2-fila-panel`.
- Convenciones que agrega la Fase 3a (Productos) y heredan las fases siguientes:
  10. **Listado v2.** `EncabezadoPagina` (acciones secundarias y UN primario a la derecha) + `TarjetaContenido` con `BarraFiltros`, grilla que ocupa el resto del alto y pie de conteo. El conteo es `{Binding Items.Count, Converter={x:Static conv:ConteoConverter.Instance}, ConverterParameter='singular|plural'}`; el estado vacío y la visibilidad de grilla y pie usan `ConteoConverter.EsCero` / `ConteoConverter.HayElementos` sobre `Items.Count`. Bindear la COLECCIÓN entera (o `ColeccionVaciaConverter`) no se re-evalúa al hacer `Clear`/`Add`; `Count` sí, porque `ObservableCollection` lo notifica. Un listado sin búsqueda libre usa `BarraFiltros MostrarBusqueda="False"` y pone sus filtros con etiqueta (`CampoFormulario`) en la ranura.
  11. **Fila inactiva y número negativo por token, nunca por `Opacity`.** Cada `TextBlock` de celda lleva `Classes.inactivo="{Binding !Activo}"` (texto terciario) y la columna de estado una `Insignia` neutra ("Inactivo", masculino); un número negativo lleva `Classes.negativo` (`#B91C1C`), que gana sobre `inactivo`. `SignoNegativoBrushConverter` y `ActivoOpacidadConverter` son del tema viejo: no se usan en vistas v2. Espejo: `.v2-grilla-fila--inactiva` y `.v2-texto-peligro` en `base.css`. La celda compuesta insignia + número (Stock) no lleva `v2-num` en el mockup: su `.valor` replica el ancho redondeado con `calc-size` y `tabular-nums` (la regla del único `<span>` existe por ese redondeo).
  12. **Formulario v2.** Breadcrumb al comando de vuelta que el VM ya expone (en Productos, `CancelarCommand`; en otros, `Volver`), card de máx. 760 a la izquierda (`Grid` con una columna `*` de `MaxWidth` 760), padding 24, secciones con `subtitulo-formulario` y separador de 1 px `#F0EFED` con 20 de margen, `Alerta` error para el error de guardado (solo en la variante con error; lleva 8 de margen superior, variante compactada), pie con borde superior y 20 de margen superior (rige en las 4 variantes del formulario), y botones a la derecha (Cancelar `secundario`, Guardar `primario`). El estado sin permiso reemplaza la card, no se superpone. `TextBox` deshabilitado con los tokens de deshabilitado.
  13. **Tests heredados que buscaban tipos del sistema viejo** (`BadgeEstado`, `Controls.CampoFormulario`, `Classes="secondary"`, headers en minúscula) se adaptan para aceptar AMBOS sistemas mientras convivan (Fase 9 limpia): `GrillaStockHelpers`, `GuardarBloqueadoConErroresDeEntradaTests.CajaDeCampo`, `CatalogoFormAnchoYCancelarTests.BotonCancelar`. Excepción: `GuardarBloqueadoConErroresDeEntradaTests.CajaDeCampo` acepta solo v2 desde 75c83ae (Ruling F4), porque todas las vistas que monta ese archivo ya son v2 y no conviven sistemas; cuando un archivo de test queda solo con vistas v2, el helper se pasa a solo v2. Nunca se borra un test heredado sin reemplazarlo por uno que custodie lo mismo sobre la View v2.
  14. **Columnas `Auto` en el mockup:** se miden en headless con el tema v2 y los datos fijos ANTES de escribir el mockup, y el mockup las copia como `flex: 0 0 Npx`. Si tras el XAML la comparación muestra una columna corrida, se re-mide y se corrige el mockup (un cambio ≤ 4 px no visible al ojo se anota; uno mayor vuelve al usuario). Una columna `Auto` crece cuando entra en pantalla (virtualización) una fila más ancha y mueve a las demás: si su contenido es un conjunto cerrado y conocido (la insignia "Inactivo" de ESTADO = 78) se fija el ancho medido, y un UiTest con la View real scrollea hasta esa fila y exige que ningún ancho cambie (`ProductoListViewV2Tests.Scroll_HastaLaPrimeraFilaInactiva_NoMueveNingunaColumna`). Las `Auto` de contenido libre miden su contenido sobre todas las filas (convención 19); en el mockup se copia el ancho que mide el componente con los datos fijos.
- Convenciones que agrega la Fase 3b (Entrada/Salida):
  15. **Campo condicional en un formulario v2:** el espacio entre filas va en el `Margin` superior de cada campo (no en filas espaciadoras de `Grid` ni en `Spacing`), para que al ocultarse el campo no quede un hueco doble. Espejo en el mockup: el campo se omite y el `row-gap` de `.campos` da el mismo 16.
  16. **Formulario de pantalla de sidebar** (no un alta que vuelve a un listado; desvíos aprobados D1 y D3 de §4.4 y §5.2): `EncabezadoPagina` sin breadcrumb, una sola sección sin subtítulo, misma card y pie que la convención 12, y solo los botones que el VM expone (en Entrada/Salida, un único "Registrar").
  17. **Diálogo de confirmación con textos propios:** `IConfirmacionService.PreguntarAsync(mensaje, textoNegativo, textoAfirmativo)` cuando "Cancelar" confunde porque la operación ya se hizo; los demás diálogos siguen con "Cancelar" / "Sí, continuar". Se mide en headless que el botón no recorta el texto y que la fila termina dentro del ancho útil del diálogo (`DialogosV2Tests`).
- Convenciones que agrega la Fase 3c-1 (patrones comunes de carga y columnas `Auto`):
  18. **Estado de carga de un listado v2.** `ViewModelBase.Cargando` es un contador de cargas en curso: `EjecutarCargaProtegidaAsync` llama `EmpezarCarga()` al entrar y `TerminarCarga()` en un `finally`, y notifica solo en las transiciones 0 a 1 y 1 a 0 (dos cargas superpuestas no se pisan). Toda carga que llena el listado pasa por ese helper, que además marca `SinPermiso` ante un 403 (la búsqueda con debounce y la recarga tras Eliminar de Productos también). La vista muestra `v2:EstadoCargando` con `Cargando && Count == 0`, el `EstadoVacio` de "no hay" SOLO con `!Cargando && !SinPermiso && !SinConexion && Count == 0` (`MultiBinding` con `BoolConverters.And`: cada parte notifica sola, sin propiedades calculadas en el VM) y el `EstadoVacio` "Sin permiso" (`EsError`) en lugar de la card. **Sin servidor** (`ServidorNoDisponibleException`: API caída, pausada o con timeout): el VM que opta con `protected override bool AvisaSinConexionEnLinea => true` deja `SinConexion` en true en vez de dejar escapar la excepción (sin modal global: el aviso en línea lo reemplaza, como en la convención 22), y la vista muestra DENTRO de la card, con los filtros a mano, `v2:EstadoVacio x:Name="EstadoSinConexion"` con `EsError`, título "No se pudo conectar con el servidor", mensaje "Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar." y un `Button` secundario "Reintentar" (`ReintentarCargaCommand` de la base, que repite la carga que falló); la grilla y el pie se ocultan con `!SinConexion` (mostrarían filas de otra consulta), y el VM además vacía `Items` al quedar sin conexión (gancho `AlPerderConexion` de la base, solo para la consulta vigente): si no, durante el reintento (hasta ~15 s de timeout) `SinConexion` vuelve a false y la grilla mostraría las filas de la consulta anterior. **Reintentar busca con el filtro que está en pantalla en ese momento**, no con el de la consulta que falló (Historial lo sobrescribe con `ReintentarAsync`; en Productos el filtro y la pantalla siempre coinciden por el debounce). Un éxito de la consulta vigente suelta la carga guardada para reintentar. Una consulta que ya no es la última (`ConsultaVigente`) no marca `SinConexion`, igual que con el 403. Un VM que no opta conserva el comportamiento anterior (la excepción escapa al modal global): no se activa sin que su vista muestre el estado, o la pantalla quedaría muda. Hoy optan Productos e Historial de movimientos. Espejo: `.v2-cargando` en `base.css`. `MantenimientoViewModel` usa el `Cargando` de la base (se borró su campo propio).
  19. **Columnas `Auto` de contenido libre (`ColumnaAuto`).** `Width="Auto"` + `b:ColumnaAuto.Medir="True"` (`Ruta` por defecto = `SortMemberPath`; `b:ColumnaAuto.Conversor` = el MISMO conversor de la celda; una `DataGridTextColumn` usa su `Binding`). El ancho = `ceil(max(texto más ancho de TODA la fuente + padding de celda, título + padding + 20 de la flecha de orden))`, acotado por `MinWidth` y por el tope adaptable `min(medido, 240, espacio libre)`; solo crece mientras vive la grilla (un orden o un filtro no la re-miden hacia abajo). Si lo que piden las columnas no entra, cada medida cede hasta el espacio libre en este orden: primero las `*` bajan a su piso, después las medidas se nivelan hacia abajo hasta su título. La grilla hace scroll horizontal solo con la ventana menor a ~1218 px (886 px internos en Productos); nunca por una columna medida. El `TextBlock` de la celda lleva `TextTrimming="CharacterEllipsis"`, `ToolTip.Tip` con el mismo texto y `b:TooltipSiSeCorta.Activo="True"`. Columnas con insignia (STOCK, ESTADO) y las `*` no se miden: quedan fijas o proporcionales. **Arrastre manual:** la columna que el usuario arrastra guarda el ancho que le dio (`AnchosUsuario`): no se vuelve a medir ni cede, persiste ante recargas, búsquedas y cambios de ventana, y el reparto sigue vigente alrededor de ella (las `*` que el arrastre apretó vuelven a su piso; si ya no entra, scroll horizontal en vez de una columna recortada). Un UiTest de la vista scrollea más de 1000 filas y exige que ninguna columna se mueva, otro exige que el texto medido sea el texto pintado (paridad), y `GrillasSinRecorteBarridoTests` admite el corte de una columna medida que cedió o llegó al tope. Si ni con las medidas en su título y las `*` en su piso entra, las medidas vuelven a su ancho medido (texto entero) y la grilla scrollea: nunca se corta una columna medida si igual va a haber scroll. Una columna de formato cerrado que no debe ceder nunca (FECHA del historial) se marca `b:ColumnaAuto.NoCede="True"`.
  20. **Tooltip solo si se corta:** `b:TooltipSiSeCorta.Activo="True"` sobre un `TextBlock` con `TextTrimming` y `ToolTip.Tip`; cancela la apertura si ninguna línea está cortada (`TextLine.HasCollapsed`).
  21. **Permiso revocado en caliente.** El 403 pasa por `ManejadorAccesoRevocado`: refresca el cache de permisos, notifica a la UI (`ShellViewModel`/`ShellMainViewModel.NotificarPermisosActualizados` recalcula las `Puede*` y el sidebar y reenvía a la pantalla abierta por un hook virtual de `ViewModelBase`, sin suscripciones a eventos; la base hace `OnPropertyChanged(string.Empty)`, que re-evalúa todos los bindings del VM, así que ninguna pantalla común necesita override propio; salvo un VM host de un panel embebido, que sí lo sobrescribe para reenviar a su hijo (convención 29), o una pantalla con un recálculo propio, como `GastosViewModel.NotificarPermisosActualizados`, que re-evalúa el `CanExecute` de los exportes y vacía `Filas`) y recién después muestra el aviso, que bloquea hasta que el usuario lo cierra. Los botones de acción de un listado se gatean con `IsVisible` sobre la propiedad `Puede*` del VM (en Productos, `PuedeGestionarProductos` para Nuevo, Editar y Eliminar). Probalo con un Operador de permisos mixtos (no Admin, que cortocircuita) y la View real dentro de `ShellMainView`: ver `AccesoRevocadoPantallasFinanzasTests`.
  22. **Carga inicial de un formulario que puede fallar por servidor caído:** el VM expone el mensaje (`ErrorCargaProductos` / `HayErrorCargaProductos`) y un comando de reintento, y la vista muestra `v2:Alerta` de error en línea con un botón "Reintentar" en lugar del modal global (Registrar entrada y Registrar salida, form compartido `MovimientoFormControl`). En un listado v2 el mismo caso va por el estado `SinConexion` de la base (convención 18).
- Convenciones que agrega la Fase 3c-2 (Historial de movimientos):
  23. **Solo la última consulta decide lo que muestra un listado.** Cada método que reemplaza `Items` con el resultado del servidor pide un turno (`var turno = _consultas.Empezar();`, `ConsultaVigente` por VM) ANTES de llamar al servicio, pasa el turno a `EjecutarCargaProtegidaAsync(cargar, mensaje, turno)` y, después del `await`, aplica el resultado solo si `turno.EsLaUltima`. Una consulta vieja que termina tarde no toca `Items` ni marca `SinPermiso`; `Cargando` sigue siendo el contador de la convención 18 (no cambia). Se prueba con dos `TaskCompletionSource` que se completan en orden inverso (la nueva primero) y se verifica por mutación quitando el `if`.
  24. **Diálogo v2 con búsqueda:** `Window` sin bindings (acceso por `x:Name`, §5.5) del patrón de `PedirTextoDialog` (400 de ancho, `Border` raíz con `Classes="ui2"` y padding 24), servido por un servicio de diálogo propio (`IRecalcularStockDialogService`, como `IClasificacionTareaDialogService`) que recibe el buscador del VM (`Func<string?, CancellationToken, Task<IEnumerable<object>>>`) y devuelve el DTO elegido o null. El botón afirmativo se habilita solo con una selección, y editar el texto después de elegir la deshabilita (Avalonia no limpia `SelectedItem`: se compara `Text == SelectedItem.Nombre`). Se compara con `ContenidoDeDialogo.Extraer` y entra a `GuardianDePatronTests.DialogosV2`.
- Convenciones que agrega la Fase 3c-3 (Ingreso por factura y patrón de modal v2):
  25. **Todo modal v2 es una `Window` con servicio; prohibidos los overlays en pantalla.** Un modal de una pantalla (alta rápida, confirmación previa al guardado, etc.) nunca es un `Border` semitransparente sobre la vista con `IsVisible` ligado a un flag del VM (`Mostrando*`): es una `Window` con `WindowStartupLocation="CenterOwner"`, `CanResize="False"`, `ShowInTaskbar="False"` y `SizeToContent="Height"`, contenido en `Border x:Name="Contenido" Classes="ui2"`, sin bindings (acceso por `x:Name`, §5.5, convención 24), con Escape que cancela y Enter que confirma (`IsCancel` / `IsDefault`; con un `ComboBox` en el diálogo `IsDefault` no alcanza, porque el combo cerrado se come el Enter para abrir el desplegable: el Enter de un combo con valor y cerrado confirma con `EnterEjecutaComando` asignado desde el code-behind, `SetComando(combo, comandoConfirmar)`, y vacío sigue abriendo el desplegable) y el foco atrapado dentro de la `Window`. La abre un servicio propio con el mecanismo de `RecalcularStockDialogService` (`ShowDialog` con owner `MainWindow` y valor seguro si no hay `App`), que recibe del VM los datos ya cargados y devuelve el resultado o null si se cancela; el VM espera al servicio en vez de prender un flag. Primeros casos: `AltaProductoRapidaDialog` (`IAltaProductoRapidaDialogService`, devuelve `ProductoNuevoDto?`) y `ConfirmacionPreciosDialog` (`IConfirmacionPreciosDialogService`, devuelve la lista con los tildes) de Ingreso por factura. La custodia `GuardianDePatronTests` sobre las vistas v2: ningún `#AA000000` ni binding `Mostrando*` a `IsVisible`. Los 3 overlays de actualización del shell (`CoordinadorActualizacion`, Fase 1) no son modales de pantalla y quedan fuera.
  26. **Enter en una fila de carga:** `beh:EnterEjecutaComando.Comando="{Binding XCommand}"` en cada campo de la fila (nunca en un contenedor: el túnel se comería el Enter de sus descendientes). Va en túnel (un `ComboBox` cerrado abre su desplegable con Enter y marca el evento antes que cualquier manejador de burbuja); en un `ComboBox` solo actúa con un ítem elegido y el desplegable cerrado; ejecuta solo si `CanExecute` (un campo en rojo bloquea igual que el botón) y marca el Enter como manejado. La acción frecuente que dispara es `secundario`: el único primario de la vista sigue siendo el de guardar, y ningún botón de una pantalla con fila de carga lleva `IsDefault`. Primer caso: "Agregar artículo" de Ingreso por factura (Producto con valor, Cantidad y Precio).
  27. **Pie fijo de formulario largo:** `Grid RowDefinitions="*,Auto,Auto"`: el `ScrollViewer` con encabezado y card, la `v2:Alerta` del error general y el pie (`Border` con borde superior y fondo superficie) FUERA del scroll; lo que aparece al final del contenido tras una acción (la card de adjuntos) lleva `beh:TraerALaVista.AlMostrarse="True"`. Lo custodian `IngresoPorFacturaViewV2Tests.Pie_ConCincuentaRenglones_…` (el borde inferior del scroll no pasa el superior del pie) y `Guardado_LaCardDeAdjuntosQuedaALaVista`.
- Convenciones que agrega la Fase 4a (Adjuntos, Calendario de pagos y Control POA):
  28. **Visibilidad de una columna de `DataGrid` por permiso:** una `DataGridColumn` no recibe el `DataContext` (no está en el árbol lógico), así que un `IsVisible="{Binding Puede…}"` en la columna no se reevalúa. La vista sincroniza `Columna.IsVisible` desde el code-behind al cambiar el `DataContext` (desuscribiendo el VM anterior) y en cada `PropertyChanged` del VM con nombre vacío (el de `NotificarPermisosActualizados`) o el del gate, y el control de la celda conserva su propio `IsVisible` sobre el gate. Primer caso: "Registrar pago" del Calendario de pagos (`CalendarioPagosView.axaml.cs`). Lo custodia `CalendarioPagosViewV2Tests` con un Operador de permisos mixtos y el 403 por el camino real del shell.
  29. **Panel embebido con gates:** un VM hijo que vive en una propiedad del host (`AdjuntosPanel`) no se entera del `OnPropertyChanged(string.Empty)` del host (relee la misma instancia); el host sobrescribe `NotificarPermisosActualizados` y lo reenvía al hijo. Los gates del hijo son getters sobre la sesión, nunca un campo calculado una vez. Hoy reenvían `GastoFormViewModel`, `PagosGastoViewModel` e `IngresoPorFacturaViewModel` (`NotificarPermisosActualizados_ReenviaAlPanelDeAdjuntos`); todo host nuevo que embeba el panel (4b a 4e) hace lo mismo.
  - Nota de la verificación orgánica de la 4a: el alto 36 de un `NumericUpDown` v2 va en la caja (`/template/ ButtonSpinner`), no en el control, porque en Fluent el `DataValidationErrors` vive dentro de la plantilla y con `Height` en el control el mensaje de error achicaba la caja (custodia: `ControlPoaViewV2Tests.EjercicioInvalido_ElMensajeVaDebajoDeLaCaja_…`). Y `beh:TraerALaVista.AlMostrarse` también vuelve a traer el control a la vista cuando, ya visible, crece (cada adjunto agrega una fila a la card de Ingreso por factura; custodia: `Guardado_TrasAgregarUnAdjunto_LaCardEnteraSigueALaVista`).
- Convenciones que agrega la Fase 4b (Ingresos de caja):
  30. **Filtro de un listado en el cliente:** cuando la API devuelve el listado entero y no filtra, el VM filtra con `DataGridCollectionView.Filter` (un predicado `EsVisible` del VM) y llama `Refresh()` al cambiar un filtro; los contadores del pie se recalculan con el MISMO predicado (nunca se cuenta `Items`), y si el filtro oculta la fila seleccionada la selección se limpia. La búsqueda de texto es "contiene" sin mayúsculas ni tildes (`CultureInfo.InvariantCulture.CompareInfo.IndexOf` con `IgnoreCase | IgnoreNonSpace`). `IgnoreNonSpace` también iguala ñ con n ("ano" encuentra "año"): es aceptado y depende de que el desktop no use `InvariantGlobalization`. El vacío tiene dos variantes: sin datos (`Items.Count == 0`) y sin resultados (`Items.Count > 0 && CantidadVisible == 0`). Primer caso: Ingresos de caja.
  31. **Errores por campo en un formulario v2:** el VM implementa `INotifyDataErrorInfo` (no `ObservableValidator`: `ViewModelBase` ya es `ObservableObject`); `ErrorDe(propiedad)` devuelve el error solo si el campo está "tocado" (cambió su valor, perdió el foco o se intentó guardar) y `ErrorsChanged` se levanta con el NOMBRE DE LA PROPIEDAD BINDEADA. El mensaje lo dibuja el `DataValidationErrors` del propio control dentro de `CampoFormulario`; un `TextBox` en error prende `HayErroresDeEntrada` vía `ErroresDeEntradaBehavior` y el Guardar de `ComandoDeGuardado` se bloquea con su tooltip. Los errores del servidor siguen en la `Alerta` de `MensajeError`. Primer caso: el formulario de Ingresos de caja.
- Convenciones que agrega la Fase 4c (Libro caja):
  32. **Lo que se ve es lo cargado:** en una pantalla con período o modo que recarga con debounce, el VM guarda el resultado de la última consulta aplicada (`LibroMes` / `LibroAnual` en Libro caja, `_ejercicioCargado` en Control POA) y la vista muestra sus bloques, sus totales y su exporte desde ESE resultado, no desde los filtros; el exporte rotula con el período del resultado. El vacío se calcula sobre el período cargado (`SinMovimientosEnElPeriodo`), es false sin nada cargado y no se deduce de un `Count` cuando el servidor devuelve filas en cero. Primer caso: Libro caja.
  33. **Número negativo fuera de una grilla:** `Classes.negativo` sobre el `TextBlock` (estilo `.ui2 TextBlock.negativo`, peligro texto por token) más una `Insignia` `Peligro` con la palabra; espejo `.v2-texto-peligro` en `base.css`. Un campo de filtro que se oculta por un modo descarta su texto inválido al ocultarse, para no bloquear en silencio los comandos de guardado. Primer caso: los saldos y el campo Mes de Libro caja.
  - `UmbralAprobado` con geometría: el de `LibroCajaSaldoNegativo` (16) es el primer umbral aprobado cuyas teselas densas son de corrimiento de 1 px (12-13 teselas), no de tono, aunque el campo se llame `TeselasDeTono`. Un rojo futuro ahí se diagnostica por geometría antes que por tono.
  - `ArbolVisual.EsVisibleEnArbol(null)` falla fuerte desde `89202a4` (antes devolvía true y daba falsos verdes). Los helpers `boton is not null && EsVisibleEnArbol(boton)` de `AccesoRevocadoSidebarTests`, `ShellMainViewGatesTests` e `IngresoPorFacturaViewV2Tests` siguen sin distinguir "removido del árbol" de "oculto" (ver deuda de la 4c en la spec).
- Convenciones que agrega la Fase 4d (Gastos y facturas):
  34. **Recarga con debounce (`RecargaConDebounce`):** todo filtro o campo que recarga solo usa el helper (`ViewModels/RecargaConDebounce.cs`): `Programar(recarga)` cancela y libera la espera pendiente y programa otra; la espera sale de un `Func<TimeSpan>` leído en cada `Programar`. El VM conserva `internal TimeSpan EsperaDebounce` (300 ms por defecto, los tests la bajan) e `internal Task _tareaDebounce` (lo que devuelve `Programar`, para esperarlo en los tests), y crea el helper perezoso: `private RecargaConDebounce Recarga => field ??= new RecargaConDebounce(() => EsperaDebounce);`. El helper solo hace el debounce: el turno de `ConsultaVigente` lo pide el método de carga del VM, que comparten los caminos con y sin debounce (carga inicial, Reintentar, Actualizar, Buscar). Primeros casos: Productos, Control POA, Libro caja y Gastos.
  35. **Armado de la recarga automática:** los filtros que se ponen antes de la carga inicial (preselección por navegación, `FiltrarPorLineaPoa`) o durante el remapeo de los combos no programan consulta; la recarga se arma después de cargar los combos y antes de pedir el listado inicial, así un cambio durante la espera de ese listado gana por turno (P8-A). "Limpiar" repone los filtros y no consulta por su cuenta: los cambios se juntan en una sola recarga.
  36. **Formulario de solo lectura por permiso o por estado del registro:** `SoloLectura` (sin el permiso de escritura o con el registro anulado) es un getter sobre la sesión más un flag del registro, así la revocación en caliente lo re-evalúa; la vista deshabilita un contenedor con los campos (no la card: el panel de adjuntos queda afuera y su "Ver" sigue), oculta Guardar y muestra la `Insignia` "Solo lectura" (y "Anulado" con el registro anulado). Primer caso: el formulario de gasto.
  - Nota de la verificación orgánica de la 4d: un `ToolTip` hereda `UseLayoutRounding` del contenedor del popup y el texto del tooltip de Guardar bloqueado se cortaba. El arreglo vive en el estilo global de `ToolTip` (`Themes/V2/ControlesV2.axaml`) y lo custodia `VisualTests/TooltipBloqueoCompletoTests.cs` (`67663a3`); todo tooltip nuevo lo hereda.
