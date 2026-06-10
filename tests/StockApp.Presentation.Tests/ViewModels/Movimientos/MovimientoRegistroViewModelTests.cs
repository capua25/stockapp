using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
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

    // ── D3 — RegistrarAsync: 4 caminos ───────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_Exitoso_NavegaAHistorial()
    {
        var (vm, svcMock, _, navMock, _) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };
        vm.Cantidad = 5m;

        var dto = new MovimientoRegistradoDto(
            MovimientoId: 1,
            ProductoId: 1,
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 5m,
            PrecioUnitario: 10m,
            StockAnterior: 0m,
            StockNuevo: 5m,
            Fecha: DateTime.UtcNow);

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ReturnsAsync(dto);

        await vm.RegistrarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
        Assert.Null(vm.MensajeError);
    }

    [Fact]
    public async Task RegistrarAsync_StockInsuficiente_Confirma_ReintentaConForzarTrue()
    {
        var (vm, svcMock, _, navMock, confirmMock) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar", StockActual = 3m };
        vm.Cantidad = 10m;

        var ex = new StockInsuficienteException(productoId: 1, stockActual: 3m, cantidadSolicitada: 10m);

        svcMock
            .SetupSequence(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(ex);

        var dtoForzado = new MovimientoRegistradoDto(
            MovimientoId: 2,
            ProductoId: 1,
            Tipo: TipoMovimiento.Salida,
            Motivo: MotivoMovimiento.Venta,
            Cantidad: 10m,
            PrecioUnitario: 5m,
            StockAnterior: 3m,
            StockNuevo: -7m,
            Fecha: DateTime.UtcNow);

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), true))
            .ReturnsAsync(dtoForzado);

        confirmMock
            .Setup(c => c.PreguntarAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        await vm.RegistrarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), true), Times.Once);
        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_StockInsuficiente_Rechaza_NoReinvocaServicio()
    {
        var (vm, svcMock, _, navMock, confirmMock) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar", StockActual = 3m };
        vm.Cantidad = 10m;

        var ex = new StockInsuficienteException(productoId: 1, stockActual: 3m, cantidadSolicitada: 10m);

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(ex);

        confirmMock
            .Setup(c => c.PreguntarAsync(It.IsAny<string>()))
            .ReturnsAsync(false);

        await vm.RegistrarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), true), Times.Never);
        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Never);
    }

    [Fact]
    public async Task RegistrarAsync_OtraExcepcion_SetMensajeError()
    {
        var (vm, svcMock, _, navMock, _) = Crear();
        vm.ProductoSeleccionado = new Producto { Id = 1, Nombre = "Azúcar" };
        vm.Cantidad = 5m;

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(new InvalidOperationException("Producto inactivo, no se permiten movimientos."));

        await vm.RegistrarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        Assert.Contains("Producto inactivo", vm.MensajeError);
        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Never);
    }
}
