using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class MovimientoRegistroViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (
        MovimientoRegistroViewModel vm,
        Mock<IMovimientoStockService> svcMock,
        Mock<IProductoService> productoMock,
        Mock<INavigationService> navMock,
        Mock<IConfirmacionService> confirmMock)
        Crear()
    {
        var svcMock     = new Mock<IMovimientoStockService>();
        var productoMock = new Mock<IProductoService>();
        var navMock     = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();

        productoMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(new List<Producto>());

        var vm = new MovimientoRegistroViewModel(
            svcMock.Object,
            productoMock.Object,
            navMock.Object,
            confirmMock.Object);

        return (vm, svcMock, productoMock, navMock, confirmMock);
    }

    // ── D2 — CanExecute tests ─────────────────────────────────────────────────

    [Fact]
    public void RegistrarCommand_SinProducto_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        // ProductoSeleccionado = null (default), Cantidad = 0 (default)

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_SoloCantidadSeteada_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.Cantidad = 5m;

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_SoloProductoSeteado_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_ProductoYCantidad_EstaHabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };
        vm.Cantidad = 10m;

        Assert.True(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_CantidadCero_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };
        vm.Cantidad = 0m;

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }
}
