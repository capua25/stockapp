using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

/// <summary>
/// Tests comunes a toda subclase de <see cref="MovimientoRegistroViewModelBase"/>
/// (Entrada/Salida). Cada derivada concreta implementa <see cref="CrearVm"/> para instanciar
/// su VM concreto; xUnit ejecuta estos [Fact] heredados en cada clase derivada.
/// </summary>
public abstract class MovimientoRegistroViewModelTestsBase
{
    /// <summary>Crea la instancia concreta del VM bajo test con los mocks provistos.</summary>
    protected abstract MovimientoRegistroViewModelBase CrearVm(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion);

    // ── helpers ──────────────────────────────────────────────────────────────

    private static ProductoDto CrearProductoDto(int id, string nombre, decimal stockActual = 0m)
        => new ProductoDto(
            Id: id, Codigo: $"SKU{id}", CodigoBarras: null, Nombre: nombre, Descripcion: null,
            CategoriaId: null, CategoriaNombre: null, ProveedorId: null, UnidadMedidaId: 1,
            UnidadMedidaNombre: "Unidad", PrecioCosto: 0m, PrecioVenta: 0m, StockActual: stockActual,
            StockMinimo: 0m, Activo: true, FechaAlta: default);

    private (
        MovimientoRegistroViewModelBase vm,
        Mock<IMovimientoStockService> svcMock,
        Mock<IProductoService> productoMock,
        Mock<INavigationService> navMock,
        Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<ProductoDto>? productos = null)
    {
        var svcMock      = new Mock<IMovimientoStockService>();
        var productoMock = new Mock<IProductoService>();
        var navMock      = new Mock<INavigationService>();
        var confirmMock  = new Mock<IConfirmacionService>();

        productoMock
            .Setup(s => s.BuscarAsync(It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(productos ?? new List<ProductoDto>());

        var vm = CrearVm(svcMock.Object, productoMock.Object, navMock.Object, confirmMock.Object);

        return (vm, svcMock, productoMock, navMock, confirmMock);
    }

    // ── CanExecute ───────────────────────────────────────────────────────────

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
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_ProductoYCantidad_EstaHabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");
        vm.Cantidad = 10m;

        Assert.True(vm.RegistrarCommand.CanExecute(null));
    }

    [Fact]
    public void RegistrarCommand_CantidadCero_EstaDeshabilitado()
    {
        var (vm, _, _, _, _) = Crear();
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");
        vm.Cantidad = 0m;

        Assert.False(vm.RegistrarCommand.CanExecute(null));
    }

    // ── RegistrarAsync: 4 caminos ─────────────────────────────────────────────

    [Fact]
    public async Task RegistrarAsync_Exitoso_NavegaAHistorial()
    {
        var (vm, svcMock, _, navMock, _) = Crear();
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");
        vm.Cantidad = 5m;

        var dto = new MovimientoRegistradoDto(
            MovimientoId: 1,
            ProductoId: 1,
            Tipo: vm.Tipo,
            Motivo: vm.Motivo,
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
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar", stockActual: 3m);
        vm.Cantidad = 10m;

        var ex = new StockInsuficienteException(productoId: 1, stockActual: 3m, cantidadSolicitada: 10m);

        svcMock
            .SetupSequence(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(ex);

        var dtoForzado = new MovimientoRegistradoDto(
            MovimientoId: 2,
            ProductoId: 1,
            Tipo: vm.Tipo,
            Motivo: vm.Motivo,
            Cantidad: 10m,
            PrecioUnitario: 5m,
            StockAnterior: 3m,
            StockNuevo: -7m,
            Fecha: DateTime.UtcNow);

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), true))
            .ReturnsAsync(dtoForzado);

        // Solo confirma la pregunta de "¿confirmar la salida igual?" — el motivo por defecto
        // de EntradaRegistroViewModel es Compra, que tras el registro forzado dispara además
        // la pregunta (distinta) de "¿asociar factura?"; este test cubre solo el flujo de
        // stock insuficiente, no el vínculo con Finanzas (ver EntradaRegistroFacturaTests).
        confirmMock
            .Setup(c => c.PreguntarAsync(It.Is<string>(m => m.Contains("Confirmar la salida"))))
            .ReturnsAsync(true);

        await vm.RegistrarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), true), Times.Once);
        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public async Task RegistrarAsync_StockInsuficiente_Rechaza_NoReinvocaServicio()
    {
        var (vm, svcMock, _, navMock, confirmMock) = Crear();
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar", stockActual: 3m);
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
        vm.ProductoSeleccionado = CrearProductoDto(1, "Azúcar");
        vm.Cantidad = 5m;

        svcMock
            .Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), false))
            .ThrowsAsync(new ReglaDeNegocioException("Producto inactivo, no se permiten movimientos."));

        await vm.RegistrarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        Assert.Contains("Producto inactivo", vm.MensajeError);
        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Never);
    }

    // ── InicializarAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task InicializarAsync_CargaProductos()
    {
        var productos = new List<ProductoDto>
        {
            CrearProductoDto(1, "Activo"),
            CrearProductoDto(2, "Inactivo") with { Activo = false },
        };

        var (vm, _, _, _, _) = Crear(productos);

        await vm.InicializarAsync();

        // Solo productos activos: IProductoService.BuscarAsync no filtra por Activo,
        // así que el filtro tiene que aplicarse en el VM tras traerlos.
        Assert.Single(vm.Productos);
        Assert.Equal("Activo", vm.Productos[0].Nombre);
    }
}
