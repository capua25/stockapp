using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Recorrido de uso con clicks reales de IngresoPorFacturaView -- encargo puntual: cerrar el
/// hueco de auditoría del post-mortem del proyecto ("en ingreso por factura se llegó a 2023
/// tests verdes con una pantalla inutilizable: la vista no exponía control para elegir producto
/// ni para marcar crédito"). El chequeo forward+reverse por reflection ya vive en
/// ReflexionVistaViewModelTests.cs (confirma que TODO miembro tiene un control y que NINGÚN
/// binding apunta a algo inexistente); este archivo confirma el paso siguiente: que esos
/// controles funcionan de verdad contra un árbol visual real, con clicks/tipeo/selección real,
/// mismo patrón que TareaListViewTests.cs/TareaFormViewTests.cs y las grillas de
/// NuevaImportacionView (NuevaImportacionGastosGridTests.cs/NuevaImportacionCondicionCreditoTests.cs).
///
/// Encargo "carga por formulario" (2026-08-21): la edición inline de renglones desapareció --
/// ahora se carga en una ZONA DE CARGA fija arriba de la grilla (ComboBox de producto + Cantidad
/// + Precio unitario + checkbox "Actualizar precio costo" + botón "Producto nuevo" + botón
/// "Agregar artículo"), y la grilla pasa a ser de SOLO LECTURA (lista de lo ya cargado + botón
/// Quitar). Los tests de este archivo se reescribieron para ejercitar la zona de carga con
/// clicks/tipeo reales en vez del viejo "+ Agregar renglón" + edición de celda.
/// </summary>
public class IngresoPorFacturaViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=GestionMunicipal"
                Width="1000" Height="1600">
            <mov:IngresoPorFacturaView />
        </Window>
        """;

    private static ProductoDto Producto(int id, string nombre, decimal precioCosto = 100m) => new(
        id, $"COD{id}", null, nombre, null, null, null, null, 1, "Unidad",
        precioCosto, 10m, 1m, true, DateTime.Today);

    private static (Window Window, IngresoPorFacturaViewModel Vm, IngresoPorFacturaServiceFake Servicio, NavigationRecorderFake Nav) Montar(
        IReadOnlyList<Proveedor>? proveedores = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null,
        IReadOnlyList<RubroGasto>? rubros = null,
        IReadOnlyList<LineaPoa>? lineas = null,
        IReadOnlyList<ProductoDto>? productos = null,
        IReadOnlyList<Categoria>? categorias = null,
        IReadOnlyList<UnidadMedida>? unidades = null)
    {
        var servicio = new IngresoPorFacturaServiceFake();
        var productoService = new ProductoServiceIngresoFake(productos ?? Array.Empty<ProductoDto>());
        var categoriaService = new CategoriaServiceFake(categorias ?? Array.Empty<Categoria>());
        var unidadService = new UnidadMedidaServiceFake(unidades ?? Array.Empty<UnidadMedida>());
        var proveedorService = new ProveedorServiceFake(proveedores ?? Array.Empty<Proveedor>());
        var fuenteService = new FuenteFinanciamientoServiceFake(fuentes ?? Array.Empty<FuenteFinanciamiento>());
        var rubroService = new RubroGastoServiceFake(rubros ?? Array.Empty<RubroGasto>());
        var lineaService = new LineaPoaServiceFake(lineas ?? Array.Empty<LineaPoa>());
        var nav = new NavigationRecorderFake();
        var confirmacion = new ConfirmacionServiceFake();
        var adjuntosPanel = new StockApp.Presentation.ViewModels.Finanzas.AdjuntosPanelViewModel(
            new AdjuntoServiceFake(), new ServicioSeleccionArchivoFake(), new ServicioAperturaArchivoFake(),
            confirmacion, new SesionFake(RolUsuario.Admin));

        var vm = new IngresoPorFacturaViewModel(
            servicio, productoService, categoriaService, unidadService, proveedorService,
            fuenteService, rubroService, lineaService, nav, confirmacion, adjuntosPanel);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await InicializarAsync() del DataContextChanged

        return (window, vm, servicio, nav);
    }

    private static void Clickear(Window window, Control control)
    {
        Dispatcher.UIThread.RunJobs();
        var centro = new Point(control.Bounds.Width / 2, control.Bounds.Height / 2);
        var puntoEnVentana = control.TranslatePoint(centro, window) ?? centro;
        window.MouseMove(puntoEnVentana);
        window.MouseDown(puntoEnVentana, MouseButton.Left);
        window.MouseUp(puntoEnVentana, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static Button BotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, texto) && ArbolVisual.EsVisibleEnArbol(b));

    /// <summary>
    /// Varios botones de esta vista comparten texto ("Cancelar"/"Confirmar" aparecen en el
    /// formulario principal, en el overlay de alta de producto Y en el overlay de confirmación
    /// de precios) -- a diferencia de TareaFormView, acá el texto NO alcanza para desambiguar.
    /// Ubicar por identidad del Command (el objeto RelayCommand real generado por
    /// [RelayCommand]) es inequívoco sin importar cuántos "Cancelar" haya en el árbol.
    /// </summary>
    private static Button BotonPorCommand(Window window, object comando)
        => window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, comando));

    /// <summary>Header de la vista: ComboBoxes de Proveedor/Fuente/Rubro/Línea POA/Categoría/Unidad
    /// tienen ItemsSource propia y única (a diferencia del viejo ComboBox de Producto por fila).
    /// Con la zona de carga, el ComboBox de Producto también es único en todo el árbol -- identidad
    /// de referencia del ItemsSource alcanza para ubicar el combo correcto sin ambigüedad.</summary>
    private static ComboBox ComboPorItemsSource(Window window, object itemsSource)
        => window.GetVisualDescendants().OfType<ComboBox>().First(c => ReferenceEquals(c.ItemsSource, itemsSource));

    /// <summary>Las cajas de Cantidad/Precio unitario de la zona de carga son las únicas de todo
    /// el árbol con estos PlaceholderText exactos (los demás TextBox del formulario usan
    /// watermarks distintos, ej. "Ej.: A-0001234") -- alcanza para ubicarlas sin ambigüedad.</summary>
    private static TextBox CajaPorPlaceholder(Window window, string placeholder)
        => window.GetVisualDescendants().OfType<TextBox>()
            .Single(t => t.PlaceholderText == placeholder && ArbolVisual.EsVisibleEnArbol(t));

    private static void Tipear(TextBox caja, string texto)
    {
        caja.Focus();
        caja.Text = texto;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Código/Nombre son los únicos TextBox de AUTOR sin Watermark/
    /// PlaceholderText del árbol (todos los del formulario de cabecera y la zona de carga SÍ
    /// tienen uno) -- TemplatedParent == null descarta el PART_EditableTextBox interno que todo
    /// ComboBox trae en su template por default (existe en el árbol visual aunque
    /// IsEditable="False", nunca se ve ni se usa). Factorizado a un solo lugar (en vez de
    /// duplicarlo por test) para no multiplicar el warning CS0618 de leer TextBox.Watermark
    /// (obsoleto) -- ver el comentario de PlaceholderText en la vista real.</summary>
    private static List<TextBox> CamposDelOverlayDeAltaDeProducto(Window window)
        => window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => string.IsNullOrEmpty(t.Watermark) && string.IsNullOrEmpty(t.PlaceholderText))
            .Where(t => t.TemplatedParent is null)
            .Where(t => t.FindAncestorOfType<CalendarDatePicker>() is null)
            .ToList();

    /// <summary>
    /// Flujo completo de carga vía clicks/tipeo reales: elegir producto en el combo de la zona de
    /// carga, tipear cantidad y precio (pasando por el DecimalPuntoConverter real), y clickear
    /// "Agregar artículo". Reutilizado por los tests que solo necesitan un renglón cargado sin
    /// repetir la secuencia entera.
    /// </summary>
    private static void CargarArticuloPorClicksReales(Window window, ProductoDto producto, string cantidadTexto, string precioTexto)
    {
        var comboProducto = ComboPorItemsSource(window, ((IngresoPorFacturaViewModel)window.DataContext!).ProductosDisponibles);
        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        Tipear(CajaPorPlaceholder(window, "Cantidad"), cantidadTexto);
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), precioTexto);

        Clickear(window, BotonPorTexto(window, "Agregar artículo"));
    }

    // ── Sanity: la carga de combos vía DataContextChanged funciona (precondición de todo lo demás) ──

    [AvaloniaFact]
    public void Montar_CargaLosCombosDesdeElServicio_ViaDataContextChanged()
    {
        var proveedor = new Proveedor { Id = 1, Nombre = "Ferretería Central", Activo = true };
        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var rubro = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales", Activo = true };
        var producto = Producto(1, "Pala punta cuadrada");

        var (_, vm, _, _) = Montar(
            proveedores: new[] { proveedor }, fuentes: new[] { fuente }, rubros: new[] { rubro },
            productos: new[] { producto });

        Assert.Contains(proveedor, vm.ProveedoresDisponibles);
        Assert.Contains(fuente, vm.FuentesDisponibles);
        Assert.Contains(rubro, vm.RubrosDisponibles);
        Assert.Contains(producto, vm.ProductosDisponibles);
    }

    // ── BUG HISTÓRICO #1: elegir producto (ahora desde la zona de carga) ──

    /// <summary>
    /// El bug histórico exacto: la vista no exponía control para elegir producto. Confirma que el
    /// ComboBox de la ZONA DE CARGA existe, que su ItemsSource ofrece el catálogo real, que
    /// seleccionar un producto llega al ViewModel (Vista->ViewModel) y que el texto mostrado es el
    /// NOMBRE legible del producto -- no record.ToString() (mismo tipo de bug de "ToString() en
    /// vez del nombre" que el commit 4825caf corrigió para NuevaImportacionView con
    /// TextSearch.TextBinding). Además, item 3 del encargo "carga por formulario": tipear
    /// cantidad/precio con el DecimalPuntoConverter real y clickear "Agregar artículo" agrega la fila a
    /// la grilla de solo lectura, y limpia la zona de carga.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_ZonaDeCarga_ElegirProductoDelCombo_TipearYAgregar_LlegaAlViewModelConNombreLegibleYApareceEnLaGrilla()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        Assert.True(ArbolVisual.EsVisibleEnArbol(comboProducto)); // EsProductoNuevoEnCarga arranca en false: el combo debe estar visible.
        Assert.Contains(producto, comboProducto.ItemsSource!.Cast<ProductoDto>());

        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(producto, vm.ProductoEnCarga);

        var textoVisible = comboProducto.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(producto.Nombre, textoVisible);
        Assert.DoesNotContain(textoVisible, t => t is not null && t.Contains("ProductoDto"));

        Tipear(CajaPorPlaceholder(window, "Cantidad"), "2");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "10.50");

        Assert.Equal(2m, vm.CantidadEnCarga);
        Assert.Equal(10.50m, vm.PrecioUnitarioEnCarga);

        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        var fila = Assert.Single(vm.Renglones);
        Assert.Same(producto, fila.Producto);
        Assert.Equal(producto.Nombre, fila.NombreMostrado);
        Assert.Equal(2m, fila.Cantidad);
        Assert.Equal(10.50m, fila.PrecioUnitario);

        var filaEnGrilla = window.GetVisualDescendants().OfType<DataGridRow>().SingleOrDefault(r => ReferenceEquals(r.DataContext, fila));
        Assert.NotNull(filaEnGrilla);

        // La zona de carga se limpia tras agregar con éxito.
        Assert.Null(vm.ProductoEnCarga);
        Assert.Equal(0m, vm.CantidadEnCarga);
        Assert.Equal(0m, vm.PrecioUnitarioEnCarga);
        Assert.Null(vm.MensajeErrorCarga);
    }

    /// <summary>
    /// Item 5 del encargo: tras agregar con éxito, el foco vuelve al ComboBox de producto -- la
    /// secuencia tiene que ser repetible sin tocar el mouse entre un artículo y el siguiente.
    /// Verificado por MUTACIÓN (ver informe): sacar el <c>SolicitarFocoEnProductoCombo = true</c>
    /// de AgregarArticuloCommand pone este test en rojo.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_TrasAgregarArticuloConExito_ElFocoVuelveAlComboDeProducto()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        CargarArticuloPorClicksReales(window, producto, "1", "10");

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        Assert.True(comboProducto.IsFocused);
    }

    // ── BUG HISTÓRICO #2: condición de pago / crédito ──

    /// <summary>
    /// El otro bug histórico exacto: no había control para marcar crédito. A diferencia de
    /// NuevaImportacionView (donde Vencimiento se HABILITA/DESHABILITA con IsEnabled),
    /// IngresoPorFacturaView usa un panel entero con IsVisible bindeado a EsCredito -- se
    /// verifica el efecto real sobre la visibilidad del panel, no solo el valor de la propiedad.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_MarcarCredito_MuestraVencimiento_DesmarcarLoOculta()
    {
        var (window, vm, _, _) = Montar();

        var checkCredito = window.GetVisualDescendants().OfType<CheckBox>()
            .Single(c => Equals(c.Content, "Compra a crédito"));
        var fechaVencimiento = window.GetVisualDescendants().OfType<CalendarDatePicker>().ElementAt(1);

        Assert.False(vm.EsCredito);
        Assert.False(ArbolVisual.EsVisibleEnArbol(fechaVencimiento)); // Contado: el panel de vencimiento arranca oculto.

        Clickear(window, checkCredito);
        Assert.True(vm.EsCredito);
        Assert.True(ArbolVisual.EsVisibleEnArbol(fechaVencimiento));

        Clickear(window, checkCredito);
        Assert.False(vm.EsCredito);
        Assert.False(ArbolVisual.EsVisibleEnArbol(fechaVencimiento));
    }

    /// <summary>
    /// A diferencia de FilaGastoEditableVm (NuevaImportacionView), IngresoPorFacturaViewModel NO
    /// tiene un OnEsCreditoChanged que limpie FechaVencimientoSeleccionada al volver a Contado
    /// (verificado leyendo el .cs: no existe ese partial method). Esto NO es el bug histórico
    /// (que era "no hay control"), pero se documenta y verifica la consecuencia funcional: el
    /// valor viejo de vencimiento queda en memoria, pero GuardarInternoAsync lo IGNORA a
    /// propósito (`EsCredito && FechaVencimientoSeleccionada is not null`) -- así que nunca llega
    /// al servicio. Se prueba el comportamiento observable (qué llega al servicio), no la
    /// ausencia del clear en sí, que sería una aserción sobre un detalle interno irrelevante para
    /// el usuario.
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task DesmarcarCreditoConVencimientoYaCargado_NoEnviaVencimientoAlGuardar()
    {
        var proveedor = new Proveedor { Id = 1, Nombre = "Ferretería Central", Activo = true };
        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var rubro = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales", Activo = true };
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, servicio, _) = Montar(
            proveedores: new[] { proveedor }, fuentes: new[] { fuente }, rubros: new[] { rubro },
            productos: new[] { producto });

        vm.ProveedorSeleccionado = proveedor;
        vm.FuenteSeleccionada = fuente;
        vm.RubroSeleccionado = rubro;
        vm.Detalle = "Compra de materiales";
        vm.MontoTotalTexto = "150,00";
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = new DateTime(2026, 12, 31);
        vm.EsCredito = false; // el usuario se arrepiente y vuelve a Contado -- sin limpiar el picker a mano.

        CargarArticuloPorClicksReales(window, producto, "1", "100");

        await vm.GuardarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(servicio.Registrados);
        Assert.Equal(CondicionPago.Contado, servicio.Registrados[0].CondicionPago);
        Assert.Null(servicio.Registrados[0].FechaVencimiento);
    }

    // ── Alta de producto nuevo desde la zona de carga ──

    /// <summary>
    /// Item 4 del encargo: alta de producto nuevo (productoNuevo) con ActualizarPrecioCosto. A
    /// diferencia del alta en línea original (que escribía directo sobre una fila de la grilla),
    /// ahora el botón "Producto nuevo" vive en la ZONA DE CARGA (no hay fila todavía) y, al
    /// confirmar el overlay, la zona de carga queda en "modo producto nuevo" mostrando el nombre
    /// en vez del ComboBox -- recién al clickear "Agregar artículo" se crea la fila y entra a la
    /// grilla de solo lectura con el NOMBRE legible.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_ProductoNuevoDesdeLaZonaDeCarga_AlConfirmarYAgregar_LaGrillaMuestraElNombreLegible()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
        var (window, vm, _, _) = Montar(unidades: new[] { unidad });

        Clickear(window, BotonPorTexto(window, "Producto nuevo"));
        Assert.True(vm.MostrandoAltaProducto);

        var camposDelOverlay = CamposDelOverlayDeAltaDeProducto(window);
        Assert.Equal(2, camposDelOverlay.Count);
        Tipear(camposDelOverlay[0], "COD-NUEVO-1");
        Tipear(camposDelOverlay[1], "Carretilla reforzada");

        var comboUnidad = ComboPorItemsSource(window, vm.UnidadesMedidaDisponibles);
        comboUnidad.SelectedItem = unidad;
        Dispatcher.UIThread.RunJobs();

        Clickear(window, BotonPorCommand(window, vm.ConfirmarAltaProductoCommand));

        Assert.False(vm.MostrandoAltaProducto);
        Assert.True(vm.EsProductoNuevoEnCarga);
        Assert.Equal("Carretilla reforzada", vm.NuevoProductoNombre);

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        Assert.False(comboProducto.IsVisible); // EsProductoNuevoEnCarga=true: el combo se oculta (evita pisar el alta).

        var nombreVisibleEnLaZonaDeCarga = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.IsVisible)
            .Select(t => t.Text);
        Assert.Contains("Carretilla reforzada", nombreVisibleEnLaZonaDeCarga);

        Tipear(CajaPorPlaceholder(window, "Cantidad"), "1");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "40");
        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        var fila = Assert.Single(vm.Renglones);
        Assert.True(fila.EsProductoNuevo);
        Assert.Equal("Carretilla reforzada", fila.NombreMostrado);
        Assert.Equal("COD-NUEVO-1", fila.ProductoNuevoCodigo);

        var filaEnGrilla = window.GetVisualDescendants().OfType<DataGridRow>().SingleOrDefault(r => ReferenceEquals(r.DataContext, fila));
        Assert.NotNull(filaEnGrilla);

        // La zona de carga vuelve a modo "producto existente" (combo visible) tras agregar.
        Assert.False(vm.EsProductoNuevoEnCarga);
        Assert.True(ArbolVisual.EsVisibleEnArbol(ComboPorItemsSource(window, vm.ProductosDisponibles)));
    }

    [AvaloniaFact]
    public void ClickReal_CancelarAltaProducto_NoModificaLaZonaDeCargaYCierraElOverlay()
    {
        var (window, vm, _, _) = Montar();

        Clickear(window, BotonPorTexto(window, "Producto nuevo"));
        Assert.True(vm.MostrandoAltaProducto);

        Clickear(window, BotonPorCommand(window, vm.CancelarAltaProductoCommand));

        Assert.False(vm.MostrandoAltaProducto);
        Assert.False(vm.EsProductoNuevoEnCarga);
    }

    // ── Renglones: agregar (por la zona de carga) y quitar con clicks reales ──

    [AvaloniaFact]
    public void ClickReal_AgregarDosArticulosYQuitarUno_LaGrillaQuedaConElQueNoSeQuito()
    {
        var producto1 = Producto(1, "Pala punta cuadrada");
        var producto2 = Producto(2, "Rastrillo de jardín");
        var (window, vm, _, _) = Montar(productos: new[] { producto1, producto2 });

        CargarArticuloPorClicksReales(window, producto1, "1", "10");
        CargarArticuloPorClicksReales(window, producto2, "1", "20");
        Assert.Equal(2, vm.Renglones.Count);

        var filaAQuitar = vm.Renglones[0];
        var filaAConservar = vm.Renglones[1];

        var botonQuitar = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Quitar") && ReferenceEquals(b.DataContext, filaAQuitar));
        Clickear(window, botonQuitar);

        Assert.Single(vm.Renglones);
        Assert.Same(filaAConservar, vm.Renglones[0]);
    }

    /// <summary>
    /// Item 3 del encargo: la grilla queda de SOLO LECTURA. Confirma que ninguna columna de
    /// edición inline sobrevive (Producto ya no es un DataGridTemplateColumn con ComboBox propio
    /// por fila) -- el único control interactivo que queda dentro de la grilla es el botón
    /// "Quitar".
    /// </summary>
    [AvaloniaFact]
    public void LaGrillaDeRenglones_EsDeSoloLectura_SinComboBoxPropioPorFila()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        CargarArticuloPorClicksReales(window, producto, "1", "10");

        var fila = Assert.Single(vm.Renglones);
        var grid = window.GetVisualDescendants().OfType<DataGrid>().Single(g => ReferenceEquals(g.ItemsSource, vm.Renglones));
        Assert.True(grid.IsReadOnly);

        // El único ComboBox de todo el árbol sigue siendo el de la zona de carga (ItemsSource
        // compartido con vm.ProductosDisponibles) -- ninguno nuevo se generó por fila.
        var combosDeProducto = window.GetVisualDescendants().OfType<ComboBox>()
            .Where(c => ReferenceEquals(c.ItemsSource, vm.ProductosDisponibles))
            .ToList();
        Assert.Single(combosDeProducto);

        // La fila en la grilla no tiene un ComboBox propio con su DataContext.
        Assert.DoesNotContain(
            window.GetVisualDescendants().OfType<ComboBox>(),
            c => ReferenceEquals(c.DataContext, fila));
    }

    // ── Total: recálculo con decimales no redondos ──

    /// <summary>
    /// Item 6 del encargo. IngresoPorFacturaLocaleDecimalTests.cs ya documentó y probó, byte a
    /// byte, la cultura fija (Invariant, punto decimal) del DecimalPuntoConverter (independiente de
    /// esta vista). Este test
    /// ejercita el mismo camino de código a través de la ZONA DE CARGA real (Cantidad/Precio
    /// unitario con DecimalPuntoConverter, tipeo real, botón "Agregar artículo") y confirma el
    /// recálculo de SumaRenglones/DiferenciaConTotal con valores no redondos y el redondeo a 2
    /// decimales que el StringFormat='{}{0:N2}' del axaml aplica en pantalla.
    /// </summary>
    [AvaloniaFact]
    public void CargarArticuloConDecimalesNoRedondosPorLaZonaDeCarga_RecalculaElTotalConRedondeo()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        CargarArticuloPorClicksReales(window, producto, "3", "12.35");

        var fila = Assert.Single(vm.Renglones);
        Assert.Equal(37.05m, fila.Subtotal);
        Assert.Equal(37.05m, vm.SumaRenglones);

        // El TextBlock de "Suma de renglones" usa StringFormat='{}{0:N2}' PLANO (sin
        // ConverterCulture fija, a diferencia de DecimalPuntoConverter en la zona de carga) -- el
        // separador decimal depende de CultureInfo.CurrentCulture del proceso que corre la suite.
        // Lo que importa verificar acá es el REDONDEO a 2 decimales (N2), no un separador puntual,
        // por eso el texto esperado se arma con la misma cultura ambiente.
        var textoEsperado = 37.05m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);
        var totalTextBlock = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => ReferenceEquals(t.DataContext, vm) && Equals(t.Text, textoEsperado));
        Assert.Equal(textoEsperado, totalTextBlock.Text);
    }

    // ── Validaciones de la zona de carga (item 2 del encargo): la fila NO entra a la grilla y el
    // mensaje aparece visible junto a la zona de carga. ──

    [AvaloniaFact]
    public void ClickReal_AgregarArticulo_SinProductoSeleccionado_NoAgregaYMuestraMensajeVisible()
    {
        var (window, vm, _, _) = Montar();

        Tipear(CajaPorPlaceholder(window, "Cantidad"), "1");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "10");
        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        Assert.Empty(vm.Renglones);
        Assert.Equal("Debe seleccionar un producto o cargar uno nuevo.", vm.MensajeErrorCarga);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "Debe seleccionar un producto o cargar uno nuevo.");
        Assert.True(ArbolVisual.EsVisibleEnArbol(mensajeVisible));
    }

    [AvaloniaFact]
    public void ClickReal_AgregarArticulo_CantidadCero_NoAgregaYMuestraMensajeVisible()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        Tipear(CajaPorPlaceholder(window, "Cantidad"), "0");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "10");
        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        Assert.Empty(vm.Renglones);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "La cantidad debe ser mayor a cero.");
        Assert.True(ArbolVisual.EsVisibleEnArbol(mensajeVisible));
    }

    [AvaloniaFact]
    public void ClickReal_AgregarArticulo_CantidadNegativa_NoAgregaYMuestraMensajeVisible()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        // DecimalPuntoConverter permite signo (NumberStyles.AllowLeadingSign): "-3" parsea a -3m.
        Tipear(CajaPorPlaceholder(window, "Cantidad"), "-3");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "10");
        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        Assert.Empty(vm.Renglones);
        Assert.Equal("La cantidad debe ser mayor a cero.", vm.MensajeErrorCarga);
    }

    [AvaloniaFact]
    public void ClickReal_AgregarArticulo_PrecioNegativo_NoAgregaYMuestraMensajeVisible()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        Tipear(CajaPorPlaceholder(window, "Cantidad"), "1");
        Tipear(CajaPorPlaceholder(window, "Precio unitario"), "-5");
        Clickear(window, BotonPorTexto(window, "Agregar artículo"));

        Assert.Empty(vm.Renglones);
        Assert.Equal("El precio unitario no puede ser negativo.", vm.MensajeErrorCarga);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "El precio unitario no puede ser negativo.");
        Assert.True(ArbolVisual.EsVisibleEnArbol(mensajeVisible));
    }

    // ── Validaciones a nivel Guardar (gating client-side del botón + errores del servicio) ──

    [AvaloniaFact]
    public void SinRenglones_ElBotonGuardarQuedaDeshabilitado()
    {
        var proveedor = new Proveedor { Id = 1, Nombre = "Ferretería Central", Activo = true };
        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var rubro = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales", Activo = true };
        var (window, vm, _, _) = Montar(
            proveedores: new[] { proveedor }, fuentes: new[] { fuente }, rubros: new[] { rubro });

        vm.ProveedorSeleccionado = proveedor;
        vm.FuenteSeleccionada = fuente;
        vm.RubroSeleccionado = rubro;
        vm.Detalle = "Compra de materiales";
        vm.MontoTotalTexto = "150,00";
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.GuardarCommand.CanExecute(null));
        var botonGuardar = BotonPorTexto(window, "Guardar");
        Assert.False(botonGuardar.IsEffectivelyEnabled);
    }

    /// <summary>
    /// Con el gate client-side nuevo (AgregarArticuloCommand), un renglón con cantidad &lt;= 0 ya
    /// NO puede llegar a la grilla por la UI -- por eso este test, que antes reproducía "el
    /// servicio rechaza una fila inválida", ya no puede construir esa fila desde clicks reales.
    /// La garantía que sigue custodiando (que un rechazo del SERVICIO, por el motivo que sea,
    /// queda visible en la vista -- ej. una regla de negocio server-side sin espejo client-side, o
    /// una condición de carrera) se preserva inyectando la excepción directo en el fake, que
    /// siempre la relanzó sin mirar el contenido del DTO (ver IngresoPorFacturaServiceFake).
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Guardar_ElServicioRechazaPorArgumentException_ElErrorQuedaVisibleEnLaVista()
    {
        var proveedor = new Proveedor { Id = 1, Nombre = "Ferretería Central", Activo = true };
        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var rubro = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales", Activo = true };
        var producto = Producto(1, "Pala punta cuadrada"); // PrecioCosto = 100m
        var (window, vm, servicio, _) = Montar(
            proveedores: new[] { proveedor }, fuentes: new[] { fuente }, rubros: new[] { rubro },
            productos: new[] { producto });

        vm.ProveedorSeleccionado = proveedor;
        vm.FuenteSeleccionada = fuente;
        vm.RubroSeleccionado = rubro;
        vm.Detalle = "Compra de materiales";
        vm.MontoTotalTexto = "150,00";

        // Precio == PrecioCosto (100): no dispara el overlay de confirmación de precio, va directo
        // a GuardarInternoAsync -- mismo camino que el test original.
        CargarArticuloPorClicksReales(window, producto, "1", "100");

        servicio.ExcepcionARelanzar = new ArgumentException(
            "La cantidad de cada renglón debe ser mayor que cero.", "Cantidad");

        var botonGuardar = BotonPorTexto(window, "Guardar");
        Assert.True(botonGuardar.IsEffectivelyEnabled);
        Clickear(window, botonGuardar);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.GuardadoExitoso);
        var mensajeEsperado = new ArgumentException(
            "La cantidad de cada renglón debe ser mayor que cero.", "Cantidad").Message;
        Assert.Equal(mensajeEsperado, vm.MensajeError);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == mensajeEsperado);
        Assert.True(ArbolVisual.EsVisibleEnArbol(mensajeVisible));
    }

    /// <summary>
    /// Misma adaptación que el test anterior, pero preservando ADEMÁS la secuencia de la
    /// confirmación de cambio de precio de costo antes de llegar al servicio (precio 50 != costo
    /// 100 del producto, dispara el overlay igual que el escenario original con precio -5).
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task Guardar_ElServicioRechazaPorArgumentException_TrasConfirmarCambioDePrecio_ElErrorQuedaVisibleEnLaVista()
    {
        var proveedor = new Proveedor { Id = 1, Nombre = "Ferretería Central", Activo = true };
        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Rentas Generales", Activo = true };
        var rubro = new RubroGasto { Id = 1, Codigo = 10, Nombre = "Materiales", Activo = true };
        var producto = Producto(1, "Pala punta cuadrada"); // PrecioCosto = 100m
        var (window, vm, servicio, _) = Montar(
            proveedores: new[] { proveedor }, fuentes: new[] { fuente }, rubros: new[] { rubro },
            productos: new[] { producto });

        vm.ProveedorSeleccionado = proveedor;
        vm.FuenteSeleccionada = fuente;
        vm.RubroSeleccionado = rubro;
        vm.Detalle = "Compra de materiales";
        vm.MontoTotalTexto = "150,00";

        // Precio (50) difiere del PrecioCosto del producto (100): dispara el overlay de
        // confirmación de precio de costo antes de llegar al service.
        CargarArticuloPorClicksReales(window, producto, "1", "50");

        servicio.ExcepcionARelanzar = new ArgumentException(
            "El precio unitario no puede ser negativo.", "PrecioUnitario");

        Clickear(window, BotonPorTexto(window, "Guardar"));
        Dispatcher.UIThread.RunJobs();

        Assert.True(vm.MostrandoConfirmacionPrecios);
        Clickear(window, BotonPorCommand(window, vm.ConfirmarPreciosYGuardarCommand));
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.GuardadoExitoso);
        var mensajeEsperado = new ArgumentException(
            "El precio unitario no puede ser negativo.", "PrecioUnitario").Message;
        Assert.Equal(mensajeEsperado, vm.MensajeError);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == mensajeEsperado);
        Assert.True(ArbolVisual.EsVisibleEnArbol(mensajeVisible));
    }

    // ── Cultura decimal de la ZONA DE CARGA (encargo 2026-08-21, hueco 1) ──
    //
    // IngresoPorFacturaLocaleDecimalTests.cs quedó custodiando un XAML SINTÉTICO ("montar
    // producto propio, independiente de la vista") que dejó de ser donde vive el riesgo:
    // Cantidad y Precio unitario se mudaron de las celdas del DataGrid a los TextBox reales de
    // la zona de carga (ver el comentario de esa clase y el Border "Cargar artículo" del axaml).
    // El riesgo de cultura (es-UY: coma decimal, DecimalPuntoConverter) se mudó con ellos, y ese test
    // nunca mira los controles reales. Por eso la cobertura nueva va ACÁ, en
    // IngresoPorFacturaViewTests.cs, que ya monta la vista real vía Montar() y ya tiene los
    // helpers (CajaPorPlaceholder, ComboPorItemsSource) para tocar los controles reales de la
    // zona de carga -- extender el archivo sintético hubiera seguido probando el binding
    // equivocado. La cobertura vieja NO se borra: sigue custodiando el binding crudo (sin
    // converter) que usaba la vieja celda de DataGrid, un caso que ya no ocurre en esta vista
    // pero documenta el bug de fondo para cualquier otra grilla editable del proyecto.

    /// <summary>
    /// Tipea con PUNTO decimal (formato único que <see cref="StockApp.Presentation.Converters.DecimalPuntoConverter"/>
    /// exige desde el fix "todo va con punto" del 2026-08-24) bajo una cultura AMBIENTE hostil
    /// (es-UY: coma = decimal, punto = separador de miles). Antes de ese fix este mismo test
    /// tipeaba con COMA (es-UY, formato viejo del converter) bajo cultura ambiente en-US -- se
    /// invirtió a propósito: prueba lo mismo (que el converter ignora la cultura ambiente y usa
    /// su propio formato fijo), pero con el formato nuevo. Si los TextBox reales de
    /// "Cantidad"/"Precio unitario" en la zona de carga no tuvieran el converter cableado, el
    /// binding caería en el converter por defecto de Avalonia con la cultura AMBIENTE (es-UY) y
    /// <c>NumberStyles.Number</c> (incluye AllowThousands) -- que interpretaría el punto como
    /// separador de miles. Cubre los 3 puntos del encargo: Cantidad, Precio unitario, y el
    /// Subtotal de la fila resultante en la grilla.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_ZonaDeCarga_PuntoDecimal_CulturaAmbienteHostilEsUy_LlegaCorrectoAlViewModelYElSubtotalDeLaGrillaEsCorrecto()
    {
        var culturaOriginal = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("es-UY");
        try
        {
            var producto = Producto(1, "Pala punta cuadrada");
            var (window, vm, _, _) = Montar(productos: new[] { producto });

            var comboProducto = ComboPorItemsSource(window, vm.ProductosDisponibles);
            comboProducto.SelectedItem = producto;
            Dispatcher.UIThread.RunJobs();

            Tipear(CajaPorPlaceholder(window, "Cantidad"), "3.5");
            Tipear(CajaPorPlaceholder(window, "Precio unitario"), "12.35");

            // Ni truncado (3/12), ni interpretado como miles (3500/1235), ni dividido por 100.
            Assert.Equal(3.5m, vm.CantidadEnCarga);
            Assert.Equal(12.35m, vm.PrecioUnitarioEnCarga);

            Clickear(window, BotonPorTexto(window, "Agregar artículo"));

            var fila = Assert.Single(vm.Renglones);
            Assert.Equal(3.5m, fila.Cantidad);
            Assert.Equal(12.35m, fila.PrecioUnitario);
            Assert.Equal(43.225m, fila.Subtotal);

            var filaEnGrilla = window.GetVisualDescendants().OfType<DataGridRow>()
                .Single(r => ReferenceEquals(r.DataContext, fila));
            Assert.Equal(43.225m, ((FilaRenglonFacturaVm)filaEnGrilla.DataContext!).Subtotal);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = culturaOriginal;
        }
    }

    // ── Salida del modo "producto nuevo" en la zona de carga (encargo 2026-08-21, hueco 2) ──

    /// <summary>
    /// Callejón sin salida reportado por el propio implementador: una vez confirmado un producto
    /// nuevo en la zona de carga, EsProductoNuevoEnCarga queda en true y no había forma de volver
    /// a elegir un producto existente sin agregar la fila -- el ComboBox de productos existentes
    /// quedaba oculto para siempre y "Cancelar" del overlay solo cierra el overlay (no aplica acá:
    /// ese botón actúa ANTES de confirmar, el atasco es DESPUÉS). Fix: un botón "Cambiar" junto al
    /// nombre del producto nuevo (DescartarProductoNuevoEnCargaCommand) que vuelve al modo
    /// "producto existente" y limpia TODOS los campos de alta (código/nombre/categoría/unidad)
    /// para que no quede residuo que se cuele en el siguiente renglón.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_ProductoNuevoConfirmado_CambiarLoDescarta_VuelveAlComboYElSiguienteRenglonEntraSinResiduoDelDescartado()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
        var productoExistente = Producto(2, "Rastrillo de jardín");
        var (window, vm, _, _) = Montar(unidades: new[] { unidad }, productos: new[] { productoExistente });

        Clickear(window, BotonPorTexto(window, "Producto nuevo"));
        Assert.True(vm.MostrandoAltaProducto);

        var camposDelOverlay = CamposDelOverlayDeAltaDeProducto(window);
        Assert.Equal(2, camposDelOverlay.Count);
        Tipear(camposDelOverlay[0], "COD-NUEVO-1");
        Tipear(camposDelOverlay[1], "Carretilla reforzada");

        var comboUnidad = ComboPorItemsSource(window, vm.UnidadesMedidaDisponibles);
        comboUnidad.SelectedItem = unidad;
        Dispatcher.UIThread.RunJobs();

        Clickear(window, BotonPorCommand(window, vm.ConfirmarAltaProductoCommand));

        Assert.True(vm.EsProductoNuevoEnCarga);
        var comboProductoOculto = ComboPorItemsSource(window, vm.ProductosDisponibles);
        Assert.False(ArbolVisual.EsVisibleEnArbol(comboProductoOculto));

        // El usuario se arrepiente: descarta el producto nuevo desde la zona de carga.
        Clickear(window, BotonPorCommand(window, vm.DescartarProductoNuevoEnCargaCommand));

        Assert.False(vm.EsProductoNuevoEnCarga);
        Assert.Null(vm.NuevoProductoNombre);
        Assert.Null(vm.NuevoProductoCodigo);
        Assert.Null(vm.NuevaCategoriaSeleccionada);
        Assert.Null(vm.NuevaUnidadSeleccionada);

        var comboProductoVisible = ComboPorItemsSource(window, vm.ProductosDisponibles);
        Assert.True(ArbolVisual.EsVisibleEnArbol(comboProductoVisible));

        // Vuelve a elegir un producto EXISTENTE y agrega -- la fila no debe llevar rastro del
        // producto nuevo descartado.
        CargarArticuloPorClicksReales(window, productoExistente, "1", "10");

        var fila = Assert.Single(vm.Renglones);
        Assert.False(fila.EsProductoNuevo);
        Assert.Same(productoExistente, fila.Producto);
        Assert.Null(fila.ProductoNuevoNombre);
        Assert.Null(fila.ProductoNuevoCodigo);
        Assert.Equal(productoExistente.Nombre, fila.NombreMostrado);
    }
}
