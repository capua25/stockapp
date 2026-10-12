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
using StockApp.Application.Finanzas;
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

    private static Montaje Montar(double ancho = 1040, double alto = 760, IAdjuntoService? adjuntosServicio = null,
        IServicioSeleccionArchivo? seleccion = null)
    {
        var (vm, servicio, alta, precios, nav) = CrearVm(adjuntosServicio, seleccion);
        var vista = new IngresoPorFacturaView();
        var ventana = new Window { Width = ancho, Height = alto, Content = vista };
        // Con un servicio de adjuntos propio el test usa el panel: el ViewLocator lo materializa como en la app.
        if (adjuntosServicio is not null)
            ventana.DataTemplates.Add(new ViewLocator());
        ventana.Show();
        vista.DataContext = vm;
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        return new Montaje(ventana, vista, vm, servicio, alta, precios, nav);
    }

    private static (IngresoPorFacturaViewModel Vm, IngresoPorFacturaServiceFake Servicio, AltaProductoRapidaDialogServiceFake Alta,
        ConfirmacionPreciosDialogServiceFake Precios, NavigationRecorderFake Nav) CrearVm(
        IAdjuntoService? adjuntosServicio = null, IServicioSeleccionArchivo? seleccion = null)
    {
        var servicio = new IngresoPorFacturaServiceFake();
        var alta = new AltaProductoRapidaDialogServiceFake();
        var precios = new ConfirmacionPreciosDialogServiceFake();
        var confirmacion = new ConfirmacionServiceFake();
        var nav = new NavigationRecorderFake();
        var adjuntos = new AdjuntosPanelViewModel(adjuntosServicio ?? new AdjuntoServiceFake(), seleccion ?? new ServicioSeleccionArchivoFake(),
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
        Assert.Same(N<ComboBox>(m, "ComboProducto"), Foco(m));
    }

    [AvaloniaFact]
    public void Enter_SoloLoEscuchanProductoCantidadYPrecio_NuncaUnContenedor()
    {
        var m = Montar();

        var conEnter = m.Vista.GetVisualDescendants().OfType<InputElement>()
            .Where(e => StockApp.Presentation.Behaviors.EnterEjecutaComando.GetComando(e) is not null)
            .Select(e => (e as Control)?.Name).OrderBy(n => n).ToList();
        Assert.Equal(new[] { "CantidadEnCarga", "ComboProducto", "PrecioEnCarga" }, conEnter);
    }

    [AvaloniaFact]
    public void FilaCarga_DosLineas_OrdenDeTabulacionComoElFlujoDeCarga()
    {
        var m = Montar(1040, 760);

        var producto = N<ComboBox>(m, "ComboProducto");
        Assert.True(producto.Bounds.Width >= 240, $"Producto mide {producto.Bounds.Width:F0}, por debajo del piso de 240.");
        var tilde = N<CheckBox>(m, "CheckActualizarPrecio");
        Assert.True(Arriba(tilde, m.Vista) >= Abajo(producto, m.Vista), "El tilde va en la segunda línea, debajo del producto.");

        var orden = new[] { "ComboProducto", "CantidadEnCarga", "PrecioEnCarga", "CheckActualizarPrecio", "BotonProductoNuevo", "BotonAgregar" };
        producto.Focus();
        Dispatcher.UIThread.RunJobs();
        foreach (var siguiente in orden.Skip(1))
        {
            m.Ventana.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(siguiente, (Foco(m) as Control)?.Name);
        }
    }

    [AvaloniaFact]
    public void Enter_EnProductoVacio_AbreLaListaYNoAgregaNiMuestraError()
    {
        var m = Montar();

        Tecla(m, N<ComboBox>(m, "ComboProducto"), PhysicalKey.Enter);

        Assert.True(N<ComboBox>(m, "ComboProducto").IsDropDownOpen, "Con el producto vacío, Enter abre la lista como siempre.");
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
        Assert.True(Abajo(scroll, m.Vista) <= Arriba(pie, m.Vista) + 0.5, "El scroll se mete debajo del pie.");
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

    /// <summary>Agregar guarda el archivo y lo devuelve en el listado siguiente, como el servidor.</summary>
    private sealed class AdjuntosQueAgregaFake : IAdjuntoService
    {
        private readonly List<AdjuntoDto> _items = new();

        public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido)
        {
            var dto = new AdjuntoDto(_items.Count + 1, nombreArchivo, "application/pdf", contenido.Length, gastoId, null,
                new DateTime(2026, 10, 6, 13, 0, 0, DateTimeKind.Utc));
            _items.Add(dto);
            return Task.FromResult(dto);
        }

        public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido) => throw new NotSupportedException();
        public Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId) => Task.FromResult<IReadOnlyList<AdjuntoDto>>(_items.ToList());
        public Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId) => throw new NotSupportedException();
        public Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId) => throw new NotSupportedException();
        public Task QuitarAsync(int adjuntoId) => throw new NotSupportedException();
    }

    private sealed class SeleccionUnPdfFake : IServicioSeleccionArchivo
    {
        public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
            => Task.FromResult<(string NombreArchivo, byte[] Contenido)?>(("factura-0004398.pdf", new byte[2048]));

        public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoOdsAsync() => throw new NotSupportedException();
    }

    [AvaloniaFact]
    public void Guardado_TrasAgregarUnAdjunto_LaCardEnteraSigueALaVista()
    {
        var m = Montar(adjuntosServicio: new AdjuntosQueAgregaFake(), seleccion: new SeleccionUnPdfFake());
        CompletarCabecera(m.Vm);
        Renglones(m.Vm, 30);
        var scroll = N<ScrollViewer>(m, "Desplazamiento");
        Clickear(m, N<Button>(m, "BotonGuardar"));
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
        var card = N<TarjetaContenido>(m, "TarjetaAdjuntos");
        var agregar = card.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "BotonAgregar");

        Clickear(m, agregar);
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        Assert.Contains(card.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "factura-0004398.pdf");
        var abajo = Abajo(card, scroll);
        Assert.True(abajo <= scroll.Viewport.Height + 0.5,
            $"La card de adjuntos termina en y={abajo:F0} y la vista en {scroll.Viewport.Height:F0}: la fila nueva quedó tapada por el pie.");
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
        var quitar = grilla.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Quitar") && ArbolVisual.EsVisibleEnArbol(b));
        Assert.Contains("ghost", quitar.Classes);
        Assert.Contains(grilla.GetVisualDescendants().OfType<Insignia>(), i => i.Texto == "Sí" && ArbolVisual.EsVisibleEnArbol(i));

        // La grilla queda debajo del pliegue a 1040x760: el click real necesita la fila a la vista, no tapada por el pie.
        quitar.BringIntoView();
        Dispatcher.UIThread.RunJobs();
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
        bool ItemVisible()
        {
            var item = ventana.GetVisualDescendants().OfType<Control>()
                .FirstOrDefault(c => c is ICommandSource fuente && ReferenceEquals(fuente.Command, shellVm.NavIngresoPorFacturaCommand));
            return item is not null && ArbolVisual.EsVisibleEnArbol(item);
        }
        Assert.True(ItemVisible(), "Precondición: con los cuatro permisos el ítem se ve.");

        await ManejadorAccesoRevocado.ManejarAsync(sesion, new AuthServiceFake(sesion, tras),
            new ConfirmacionServiceFake(), shellVm.NotificarPermisosActualizados);
        Dispatcher.UIThread.RunJobs();

        Assert.False(ItemVisible());
    }
}
