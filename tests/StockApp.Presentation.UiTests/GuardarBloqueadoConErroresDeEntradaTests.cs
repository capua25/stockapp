using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Controls;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.Views.Catalogo;
using StockApp.Presentation.Views.Movimientos;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Integridad de datos (bug 2026-10-01): un campo numérico con texto inválido muestra el error
/// en la View, pero el binding NUNCA llega al ViewModel, que conserva el valor ANTERIOR válido.
/// Con Guardar/Registrar habilitados, el usuario escribía "5.4", veía el error, apretaba el botón
/// y se guardaba en silencio el valor viejo. Desde la centralización del formato es-UY "5.4" se
/// rechaza a propósito, así que el caso pasa a ser frecuente.
///
/// Mecanismo compartido (ver <see cref="StockApp.Presentation.Behaviors.ErroresDeEntradaBehavior"/>):
/// todo TextBox del tema reporta su error de conversión al ViewModel dueño de la vista
/// (<see cref="ViewModelBase.HayErroresDeEntrada"/>), y el CanExecute del comando de guardar lo
/// consulta. Estos tests usan las Views REALES y clicks reales: un test de VM no custodia lo que
/// pasa en el XAML (verificado por mutación en este proyecto).
/// </summary>
public class GuardarBloqueadoConErroresDeEntradaTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────

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

    private static void Tipear(TextBox caja, string texto)
    {
        caja.Focus();
        caja.Text = texto;
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox CajaDeCampo(Window window, string etiqueta)
        => window.GetVisualDescendants().OfType<CampoFormulario>().Single(c => c.Etiqueta == etiqueta)
            .GetVisualDescendants().OfType<TextBox>().First();

    private static Button BotonPorCommand(Window window, object comando)
        => window.GetVisualDescendants().OfType<Button>().First(b => ReferenceEquals(b.Command, comando));

    /// <summary>El botón deshabilitado explica por qué: tooltip visible aun deshabilitado.</summary>
    private static void AssertExplicaElBloqueo(Button boton)
    {
        Assert.Equal(ViewModelBase.MensajeErroresDeEntrada, ToolTip.GetTip(boton));
        Assert.True(ToolTip.GetShowOnDisabled(boton));
    }

    /// <summary>El mensaje del campo se ve en pantalla (no solo vive en DataValidationErrors.Errors).</summary>
    private static void AssertMensajeDelCampoVisible(TextBox caja, string mensaje)
        => Assert.Contains(caja.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == mensaje && ArbolVisual.EsVisibleEnArbol(t));

    // ── Producto: "Precio costo" ───────────────────────────────────────────

    private static (Window Window, ProductoFormViewModel Vm, ProductoServiceGrabador Servicio) MontarProducto()
    {
        var unidad = new UnidadMedida { Id = 1, Nombre = "Unidad", Abreviatura = "u", Activo = true };
        var servicio = new ProductoServiceGrabador();
        var vm = new ProductoFormViewModel(
            servicio,
            new UnidadMedidaServiceFake(new List<UnidadMedida> { unidad }),
            new CategoriaServiceFake(new List<Categoria>()),
            new NavigationRecorderFake());

        var window = new Window { Width = 800, Height = 1000, Content = new ProductoFormView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        vm.Codigo = "PRUEBA-1";
        vm.Nombre = "PRUEBA GUARDAR";
        vm.UnidadMedidaSeleccionada = unidad;
        Dispatcher.UIThread.RunJobs();
        return (window, vm, servicio);
    }

    [AvaloniaFact]
    public void Producto_PrecioCostoInvalido_DeshabilitaGuardar_YAlCorregirSeHabilita()
    {
        var (window, vm, _) = MontarProducto();
        var guardar = BotonPorCommand(window, vm.GuardarCommand);
        var caja = CajaDeCampo(window, "Precio costo");
        Assert.True(guardar.IsEffectivelyEnabled);

        Tipear(caja, "5.4");

        Assert.False(guardar.IsEffectivelyEnabled);
        Assert.False(vm.GuardarCommand.CanExecute(null));
        AssertExplicaElBloqueo(guardar);
        AssertMensajeDelCampoVisible(caja, "Usá coma para los decimales: 5,4");

        Tipear(caja, "5,4");

        Assert.True(guardar.IsEffectivelyEnabled);
        Assert.True(vm.GuardarCommand.CanExecute(null));
        Assert.Null(ToolTip.GetTip(guardar));
    }

    [AvaloniaFact]
    public void Producto_PrecioCostoInvalido_ClickEnGuardar_NoGuardaElValorViejo()
    {
        var (window, vm, servicio) = MontarProducto();
        var caja = CajaDeCampo(window, "Precio costo");
        Tipear(caja, "7");
        Assert.Equal(7m, vm.PrecioCosto);

        Tipear(caja, "5.4");
        Clickear(window, BotonPorCommand(window, vm.GuardarCommand));

        Assert.Empty(servicio.Altas);

        Tipear(caja, "5,4");
        Clickear(window, BotonPorCommand(window, vm.GuardarCommand));

        var alta = Assert.Single(servicio.Altas);
        Assert.Equal(5.4m, alta.PrecioCosto);
    }

    // ── Registrar Entrada / Salida (MovimientoFormControl compartido) ───────

    private static ProductoDto Producto(int id, string nombre) => new(
        id, $"COD{id}", null, nombre, null, null, null, null, 1, "Unidad",
        100m, 10m, 1m, true, DateTime.Today);

    private static (Window Window, MovimientoRegistroViewModelBase Vm, MovimientoServiceGrabador Servicio) MontarMovimiento(bool salida = false)
    {
        var producto = Producto(1, "PRUEBA GUARDAR pala");
        var servicio = new MovimientoServiceGrabador();
        var productos = new ProductoServiceIngresoFake(new[] { producto });
        MovimientoRegistroViewModelBase vm;
        UserControl vista;
        if (salida)
        {
            var svm = new SalidaRegistroViewModel(servicio, productos, new NavigationServiceFake(), new ConfirmacionServiceFake());
            vm = svm;
            vista = new SalidaRegistroView { DataContext = svm };
        }
        else
        {
            var evm = new EntradaRegistroViewModel(servicio, productos, new NavigationServiceFake(), new ConfirmacionServiceFake());
            vm = evm;
            vista = new EntradaRegistroView { DataContext = evm };
        }

        var window = new Window { Width = 800, Height = 1000, Content = vista };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();

        vm.ProductoSeleccionado = vm.Productos.Single();
        Dispatcher.UIThread.RunJobs();
        return (window, vm, servicio);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Movimiento_CantidadInvalida_DeshabilitaRegistrar_YAlCorregirSeHabilita(bool salida)
    {
        var (window, vm, _) = MontarMovimiento(salida);
        var registrar = BotonPorCommand(window, vm.RegistrarCommand);
        var cantidad = CajaDeCampo(window, "Cantidad");
        Tipear(cantidad, "3");
        Assert.True(registrar.IsEffectivelyEnabled);

        Tipear(cantidad, "5.4");

        Assert.False(registrar.IsEffectivelyEnabled);
        Assert.False(vm.RegistrarCommand.CanExecute(null));
        AssertExplicaElBloqueo(registrar);

        Tipear(cantidad, "5,4");

        Assert.True(registrar.IsEffectivelyEnabled);
        Assert.Equal(5.4m, vm.Cantidad);
    }

    [AvaloniaFact]
    public void Entrada_PrecioUnitarioInvalido_DeshabilitaRegistrar_YAlCorregirSeHabilita()
    {
        var (window, vm, _) = MontarMovimiento();
        var registrar = BotonPorCommand(window, vm.RegistrarCommand);
        Tipear(CajaDeCampo(window, "Cantidad"), "3");
        var precio = CajaDeCampo(window, "Precio unitario");

        Tipear(precio, "12.50");
        Assert.False(registrar.IsEffectivelyEnabled);

        Tipear(precio, "");
        Assert.True(registrar.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public void Entrada_CantidadInvalida_ClickEnRegistrar_NoRegistraElValorViejo()
    {
        var (window, vm, servicio) = MontarMovimiento();
        var cantidad = CajaDeCampo(window, "Cantidad");
        Tipear(cantidad, "3");

        Tipear(cantidad, "5.4");
        Clickear(window, BotonPorCommand(window, vm.RegistrarCommand));

        Assert.Empty(servicio.Registrados);

        Tipear(cantidad, "5,4");
        Clickear(window, BotonPorCommand(window, vm.RegistrarCommand));

        Assert.Equal(5.4m, Assert.Single(servicio.Registrados).Cantidad);
    }

    // ── Fakes grabadores ─────────────────────────────────────────────────────

    private sealed class ProductoServiceGrabador : IProductoService
    {
        public List<Producto> Altas { get; } = new();

        public Task<int> AltaAsync(Producto producto)
        {
            Altas.Add(producto);
            return Task.FromResult(Altas.Count);
        }

        public Task ModificarAsync(Producto producto) => throw new NotSupportedException();
        public Task BajaLogicaAsync(int id) => throw new NotSupportedException();
        public Task CambiarPrecioAsync(int id, decimal precioCosto) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
            => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());

        public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
            => Task.FromResult<IReadOnlyList<ProductoDto>>(Array.Empty<ProductoDto>());
    }

    private sealed class MovimientoServiceGrabador : IMovimientoStockService
    {
        public List<RegistrarMovimientoDto> Registrados { get; } = new();

        /// <summary>Graba y queda pendiente: alcanza para saber QUÉ se mandó, sin ejercitar el
        /// post-registro (diálogos/navegación) que no es parte de este bug.</summary>
        public Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
        {
            Registrados.Add(dto);
            return new TaskCompletionSource<MovimientoRegistradoDto>().Task;
        }

        public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
            => throw new NotSupportedException();

        public Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
            => throw new NotSupportedException();
    }
}
