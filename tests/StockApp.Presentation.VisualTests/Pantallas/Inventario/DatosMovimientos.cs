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
