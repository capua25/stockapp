using System;
using System.Collections.Generic;
using System.Linq;
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
/// </summary>
public class IngresoPorFacturaViewTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:mov="clr-namespace:StockApp.Presentation.Views.Movimientos;assembly=StockApp.Presentation"
                Width="1000" Height="1600">
            <mov:IngresoPorFacturaView />
        </Window>
        """;

    private static ProductoDto Producto(int id, string nombre, decimal precioCosto = 100m) => new(
        id, $"COD{id}", null, nombre, null, null, null, null, 1, "Unidad",
        precioCosto, precioCosto * 1.5m, 10m, 1m, true, DateTime.Today);

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
            confirmacion, new AuthorizationServiceFake(), new TareaSessionFake(RolUsuario.Admin));

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

    /// <summary>Mismo criterio que TareaFormViewTests.EsVisibleEnArbol: IsVisible propio de un
    /// control no cae en cascada, hay que caminar la cadena de ancestros para saber si de verdad
    /// esta en pantalla (necesario para el panel de vencimiento, superpuesto con IsVisible).</summary>
    private static bool EsVisibleEnArbol(Visual visual)
    {
        for (Visual? actual = visual; actual is not null; actual = actual.GetVisualParent())
        {
            if (actual is Control c && !c.IsVisible) return false;
        }
        return true;
    }

    private static Button BotonPorTexto(Window window, string texto)
        => window.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Content, texto) && EsVisibleEnArbol(b));

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
    /// tienen ItemsSource propia y única (a diferencia del ComboBox de Producto, que la COMPARTE
    /// entre todas las filas del renglón) -- identidad de referencia del ItemsSource alcanza para
    /// ubicar el combo correcto sin ambigüedad.</summary>
    private static ComboBox ComboPorItemsSource(Window window, object itemsSource)
        => window.GetVisualDescendants().OfType<ComboBox>().First(c => ReferenceEquals(c.ItemsSource, itemsSource));

    private static void Tipear(TextBox caja, string texto)
    {
        caja.Focus();
        caja.Text = texto;
        Dispatcher.UIThread.RunJobs();
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

    // ── BUG HISTÓRICO #1: elegir producto ──

    /// <summary>
    /// El bug histórico exacto: la vista no exponía control para elegir producto. Confirma que
    /// el ComboBox existe en el renglón agregado con un click real, que su ItemsSource ofrece el
    /// catálogo real, que seleccionar un producto llega al ViewModel (Vista->ViewModel), y que
    /// el texto mostrado es el NOMBRE legible del producto -- no record.ToString() (el mismo tipo
    /// de bug de "ToString() en vez del nombre" que el commit 4825caf corrigió para
    /// NuevaImportacionView con TextSearch.TextBinding).
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_AgregarRenglon_ElegirProductoDelCombo_LlegaAlViewModelConNombreLegible()
    {
        var producto = Producto(1, "Pala punta cuadrada");
        var (window, vm, _, _) = Montar(productos: new[] { producto });

        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        Assert.Single(vm.Renglones);
        var fila = vm.Renglones[0];

        var comboProducto = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => ReferenceEquals(c.DataContext, fila));
        Assert.True(EsVisibleEnArbol(comboProducto)); // EsProductoNuevo arranca en false: el combo debe estar visible.
        Assert.Contains(producto, comboProducto.ItemsSource!.Cast<ProductoDto>());

        comboProducto.SelectedItem = producto;
        Dispatcher.UIThread.RunJobs();

        Assert.Same(producto, fila.Producto);
        Assert.Equal(producto.Nombre, fila.NombreMostrado);

        var textoVisible = comboProducto.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains(producto.Nombre, textoVisible);
        Assert.DoesNotContain(textoVisible, t => t is not null && t.Contains("ProductoDto"));
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
        Assert.False(EsVisibleEnArbol(fechaVencimiento)); // Contado: el panel de vencimiento arranca oculto.

        Clickear(window, checkCredito);
        Assert.True(vm.EsCredito);
        Assert.True(EsVisibleEnArbol(fechaVencimiento));

        Clickear(window, checkCredito);
        Assert.False(vm.EsCredito);
        Assert.False(EsVisibleEnArbol(fechaVencimiento));
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

        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];
        fila.Producto = producto;
        fila.Cantidad = 1m;
        fila.PrecioUnitario = 100m;
        Dispatcher.UIThread.RunJobs();

        await vm.GuardarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        Assert.Single(servicio.Registrados);
        Assert.Equal(CondicionPago.Contado, servicio.Registrados[0].CondicionPago);
        Assert.Null(servicio.Registrados[0].FechaVencimiento);
    }

    // ── Alta de producto nuevo desde el renglón ──

    /// <summary>
    /// Item 4 del encargo: alta de producto nuevo (productoNuevo) con ActualizarPrecioCosto.
    /// Confirma que el botón "Producto nuevo" del renglón abre el overlay, que sus controles
    /// (Código/Nombre/Unidad, todos obligatorios según ConfirmarAltaProducto) funcionan con
    /// tipeo/selección real, y que al confirmar la grilla muestra el NOMBRE legible en vez del
    /// ComboBox de producto -- mismo criterio "nombre legible" que el bug histórico #1.
    /// </summary>
    [AvaloniaFact]
    public void ClickReal_ProductoNuevoDesdeElRenglon_AlConfirmar_LaGrillaMuestraElNombreLegible()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
        var (window, vm, _, _) = Montar(unidades: new[] { unidad });

        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];

        var botonProductoNuevo = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Producto nuevo") && ReferenceEquals(b.DataContext, fila));
        Clickear(window, botonProductoNuevo);
        Assert.True(vm.MostrandoAltaProducto);

        // Código/Nombre/Precio de venta son los únicos TextBox sin Watermark del árbol (todos
        // los del formulario de cabecera SÍ tienen Watermark) -- alcanza para ubicarlos sin
        // ambigüedad, en el mismo orden en que aparecen en el .axaml del overlay.
        // Código/Nombre/Precio de venta son los únicos TextBox de AUTOR sin Watermark del árbol
        // (todos los del formulario de cabecera SÍ tienen Watermark) -- TemplatedParent == null
        // descarta el PART_EditableTextBox interno que todo ComboBox trae en su template por
        // default (existe en el árbol visual aunque IsEditable="False", nunca se ve ni se usa).
        var camposDelOverlay = window.GetVisualDescendants().OfType<TextBox>()
            .Where(t => string.IsNullOrEmpty(t.Watermark))
            .Where(t => t.TemplatedParent is null)
            .Where(t => t.FindAncestorOfType<CalendarDatePicker>() is null)
            .ToList();
        Assert.Equal(3, camposDelOverlay.Count);
        Tipear(camposDelOverlay[0], "COD-NUEVO-1");
        Tipear(camposDelOverlay[1], "Carretilla reforzada");

        var comboUnidad = ComboPorItemsSource(window, vm.UnidadesMedidaDisponibles);
        comboUnidad.SelectedItem = unidad;
        Dispatcher.UIThread.RunJobs();

        Clickear(window, BotonPorCommand(window, vm.ConfirmarAltaProductoCommand));

        Assert.False(vm.MostrandoAltaProducto);
        Assert.True(fila.EsProductoNuevo);
        Assert.Equal("Carretilla reforzada", fila.NombreMostrado);

        var comboProducto = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => ReferenceEquals(c.DataContext, fila));
        Assert.False(comboProducto.IsVisible); // EsProductoNuevo=true: el combo se oculta (evita pisar el alta).

        var textosVisiblesDeLaFila = window.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => Equals(t.DataContext, fila) && t.IsVisible)
            .Select(t => t.Text);
        Assert.Contains("Carretilla reforzada", textosVisiblesDeLaFila);
    }

    [AvaloniaFact]
    public void ClickReal_CancelarAltaProducto_NoModificaLaFilaYCierraElOverlay()
    {
        var (window, vm, _, _) = Montar();
        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];

        var botonProductoNuevo = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Producto nuevo") && ReferenceEquals(b.DataContext, fila));
        Clickear(window, botonProductoNuevo);
        Assert.True(vm.MostrandoAltaProducto);

        Clickear(window, BotonPorCommand(window, vm.CancelarAltaProductoCommand));

        Assert.False(vm.MostrandoAltaProducto);
        Assert.False(fila.EsProductoNuevo);
    }

    // ── Renglones: agregar y quitar con clicks reales ──

    [AvaloniaFact]
    public void ClickReal_AgregarDosRenglonesYQuitarUno_LaGrillaQuedaConElQueNoSeQuito()
    {
        var producto1 = Producto(1, "Pala punta cuadrada");
        var producto2 = Producto(2, "Rastrillo de jardín");
        var (window, vm, _, _) = Montar(productos: new[] { producto1, producto2 });

        var botonAgregar = BotonPorTexto(window, "+ Agregar renglón");
        Clickear(window, botonAgregar);
        Clickear(window, botonAgregar);
        Assert.Equal(2, vm.Renglones.Count);

        var filaAQuitar = vm.Renglones[0];
        var filaAConservar = vm.Renglones[1];

        var botonQuitar = window.GetVisualDescendants().OfType<Button>()
            .Single(b => Equals(b.Content, "Quitar") && ReferenceEquals(b.DataContext, filaAQuitar));
        Clickear(window, botonQuitar);

        Assert.Single(vm.Renglones);
        Assert.Same(filaAConservar, vm.Renglones[0]);
    }

    // ── Total: recálculo con decimales no redondos ──

    /// <summary>
    /// Item 6 del encargo. IngresoPorFacturaLocaleDecimalTests.cs ya documentó y probó, byte a
    /// byte, que el binding real de la celda de edición de Cantidad/PrecioUnitario es un TextBox
    /// con Text bindeado vía DecimalConverter directamente a estas propiedades decimal -- y que
    /// el arnés headless de este proyecto no puede montar el DataGrid completo en modo edición
    /// (falta el recurso de tema "DataGridCellTextBoxTheme" fuera de un App.axaml real). Este
    /// test ejercita el mismo camino de código que ese binding produce (el setter de la
    /// propiedad decimal en la fila que YA está suscripta a Renglon_PropertyChanged porque se
    /// agregó con un click real a "+ Agregar renglón"), y confirma el recálculo de
    /// SumaRenglones/DiferenciaConTotal con valores no redondos y el redondeo a 2 decimales que
    /// el StringFormat='{}{0:N2}' del axaml aplica en pantalla.
    /// </summary>
    [AvaloniaFact]
    public void EditarCantidadYPrecioDeUnRenglonAgregadoPorClick_RecalculaElTotalConRedondeo()
    {
        var (window, vm, _, _) = Montar();
        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];

        fila.Cantidad = 3m;
        fila.PrecioUnitario = 12.35m;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(37.05m, fila.Subtotal);
        Assert.Equal(37.05m, vm.SumaRenglones);

        // El TextBlock de "Suma de renglones" usa StringFormat='{}{0:N2}' PLANO (sin
        // ConverterCulture fija, a diferencia de DecimalConverter en las celdas editables) --
        // el separador decimal depende de CultureInfo.CurrentCulture del proceso que corre la
        // suite. Lo que importa verificar acá es el REDONDEO a 2 decimales (N2), no un
        // separador puntual, por eso el texto esperado se arma con la misma cultura ambiente.
        var textoEsperado = 37.05m.ToString("N2", System.Globalization.CultureInfo.CurrentCulture);
        var totalTextBlock = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => ReferenceEquals(t.DataContext, vm) && Equals(t.Text, textoEsperado));
        Assert.Equal(textoEsperado, totalTextBlock.Text);
    }

    // ── Validaciones ──

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
    /// PuedeGuardar() (el gating client-side del botón) NO valida Cantidad/PrecioUnitario de los
    /// renglones -- solo Renglones.Count > 0. La validación real de "cantidad > 0" vive en
    /// IngresoPorFacturaService.RegistrarAsync (Application), que lanza ArgumentException y cuyo
    /// catch en GuardarInternoAsync la vuelca a MensajeError. Se configura el fake para relanzar
    /// EXACTAMENTE esa excepción (mismo tipo y mensaje que el servicio real) y se confirma que el
    /// error queda visible en un control real de la vista -- la alternativa que el encargo
    /// habilita explícitamente cuando el botón no se deshabilita.
    /// </summary>
    [AvaloniaFact]
    public async System.Threading.Tasks.Task RenglonConCantidadCero_ElServicioRechaza_ElErrorQuedaVisibleEnLaVista()
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

        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];
        fila.Producto = producto;
        fila.Cantidad = 0m; // inválido: mismo caso que valida IngresoPorFacturaService.RegistrarAsync.
        fila.PrecioUnitario = 100m;
        Dispatcher.UIThread.RunJobs();

        servicio.ExcepcionARelanzar = new ArgumentException(
            "La cantidad de cada renglón debe ser mayor que cero.", "Cantidad");

        var botonGuardar = BotonPorTexto(window, "Guardar");
        Assert.True(botonGuardar.IsEffectivelyEnabled); // PuedeGuardar() no mira Cantidad -- el gating es server-side.
        Clickear(window, botonGuardar);
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.GuardadoExitoso);
        var mensajeEsperado = new ArgumentException(
            "La cantidad de cada renglón debe ser mayor que cero.", "Cantidad").Message;
        Assert.Equal(mensajeEsperado, vm.MensajeError);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == mensajeEsperado);
        Assert.True(EsVisibleEnArbol(mensajeVisible));
    }

    [AvaloniaFact]
    public async System.Threading.Tasks.Task RenglonConPrecioNegativo_ElServicioRechaza_ElErrorQuedaVisibleEnLaVista()
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

        Clickear(window, BotonPorTexto(window, "+ Agregar renglón"));
        var fila = vm.Renglones[0];
        fila.Producto = producto;
        fila.Cantidad = 1m;
        fila.PrecioUnitario = -5m; // inválido: mismo caso que valida IngresoPorFacturaService.RegistrarAsync.
        Dispatcher.UIThread.RunJobs();

        servicio.ExcepcionARelanzar = new ArgumentException(
            "El precio unitario no puede ser negativo.", "PrecioUnitario");

        Clickear(window, BotonPorTexto(window, "Guardar"));
        Dispatcher.UIThread.RunJobs();

        // El precio cargado (-5) difiere del PrecioCosto del producto (100): GuardarAsync
        // detecta el cambio y muestra PRIMERO el overlay de confirmación de precio de costo
        // (mismo camino que un usuario real recorrería) -- recién al confirmar ahí se llega a
        // GuardarInternoAsync y al service que rechaza el precio negativo.
        Assert.True(vm.MostrandoConfirmacionPrecios);
        Clickear(window, BotonPorCommand(window, vm.ConfirmarPreciosYGuardarCommand));
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.GuardadoExitoso);
        var mensajeEsperado = new ArgumentException(
            "El precio unitario no puede ser negativo.", "PrecioUnitario").Message;
        Assert.Equal(mensajeEsperado, vm.MensajeError);
        var mensajeVisible = window.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == mensajeEsperado);
        Assert.True(EsVisibleEnArbol(mensajeVisible));
    }
}
