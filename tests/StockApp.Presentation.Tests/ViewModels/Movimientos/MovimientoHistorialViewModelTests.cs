using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class MovimientoHistorialViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MovimientoHistorialDto CrearDto(int id = 1, int productoId = 1)
        => new MovimientoHistorialDto(
            MovimientoId: id,
            ProductoId: productoId,
            ProductoNombre: "Azúcar",
            Tipo: TipoMovimiento.Entrada,
            Motivo: MotivoMovimiento.Compra,
            Cantidad: 10m,
            PrecioUnitario: 5m,
            StockAnterior: 0m,
            StockNuevo: 10m,
            Comentario: null,
            Fecha: DateTime.UtcNow,
            UsuarioId: 1,
            UsuarioNombre: "Admin");

    private static Producto CrearProducto(int id, string nombre = "Producto", bool activo = true)
        => new() { Id = id, Nombre = nombre, Codigo = $"SKU{id}", Activo = activo };

    private static (
        MovimientoHistorialViewModel vm,
        Mock<IMovimientoStockService> svcMock,
        Mock<INavigationService> navMock,
        Mock<IProductoService> productoSvcMock)
        Crear(
            IReadOnlyList<MovimientoHistorialDto>? items = null,
            IReadOnlyList<Producto>? productos = null)
    {
        var svcMock = new Mock<IMovimientoStockService>();
        var navMock = new Mock<INavigationService>();
        var productoSvcMock = new Mock<IProductoService>();

        svcMock
            .Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        productoSvcMock
            .Setup(s => s.BuscarAsync(null, null, null))
            .ReturnsAsync(productos ?? new List<Producto>());

        var vm = new MovimientoHistorialViewModel(svcMock.Object, navMock.Object, productoSvcMock.Object);
        return (vm, svcMock, navMock, productoSvcMock);
    }

    // ── D4 tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2) };
        var (vm, svcMock, _, _) = Crear(lista);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task CargarAsync_SinResultados_ItemsVacio()
    {
        var (vm, _, _, _) = Crear(new List<MovimientoHistorialDto>());

        await vm.CargarAsync();

        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task BuscarAsync_ConFiltros_DelegaAlServiceConFiltrosConstruidos()
    {
        var (vm, svcMock, _, _) = Crear();
        vm.FiltroProductoId = 5;
        vm.FiltroTipo = TipoMovimiento.Salida;
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 12, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == 5 &&
            f.Tipo == TipoMovimiento.Salida &&
            f.FechaDesde == new DateTime(2026, 1, 1) &&
            f.FechaHasta == new DateTime(2026, 12, 31))), Times.Once);
    }

    [Fact]
    public async Task BuscarAsync_SinFiltros_DelegaFiltroVacio()
    {
        var (vm, svcMock, _, _) = Crear();

        await vm.BuscarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == null &&
            f.Tipo == null &&
            f.FechaDesde == null &&
            f.FechaHasta == null)), Times.Once);
    }

    [Fact]
    public async Task RecalcularAsync_LlamaRecalcularStockAsync_YActualizaLista()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1) };
        var (vm, svcMock, _, _) = Crear(lista);
        vm.ProductoIdParaRecalcular = 1;

        svcMock
            .Setup(s => s.RecalcularStockAsync(1))
            .ReturnsAsync(new RecalculoResultadoDto(
                ProductoId: 1,
                StockAnterior: 10m,
                StockNuevo: 10m,
                TotalMovimientos: 1));

        await vm.RecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(1), Times.Once);
        // Después de recalcular, recarga el historial
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
    }

    [Fact]
    public async Task RecalcularAsync_SinProductoSeleccionado_NoLlamaServicio()
    {
        var (vm, svcMock, _, _) = Crear();
        vm.ProductoIdParaRecalcular = null;

        await vm.RecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(It.IsAny<int>()), Times.Never);
    }

    // ── InicializarAsync / filtro de producto y tipo ──────────────────────────

    [Fact]
    public async Task InicializarAsync_PopulaOpcionTodosYProductosActivos()
    {
        var productos = new List<Producto>
        {
            CrearProducto(1, "Activo", activo: true),
            CrearProducto(2, "Inactivo", activo: false),
        };
        var (vm, _, _, productoSvcMock) = Crear(productos: productos);

        await vm.InicializarAsync();

        productoSvcMock.Verify(s => s.BuscarAsync(null, null, null), Times.Once);
        Assert.Equal(2, vm.Productos.Count);
        Assert.Equal("Todos", vm.Productos[0].Nombre);
        Assert.Null(vm.Productos[0].Valor);
        Assert.Equal("Activo", vm.Productos[1].Nombre);
    }

    [Fact]
    public async Task InicializarAsync_PreseleccionaOpcionTodos()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.NotNull(vm.ProductoFiltroSeleccionado);
        Assert.Null(vm.ProductoFiltroSeleccionado!.Valor);
        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public async Task InicializarAsync_TambienCargaHistorial()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1) };
        var (vm, svcMock, _, _) = Crear(items: lista);

        await vm.InicializarAsync();

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Single(vm.Items);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlAsignarProductoReal_DerivaFiltroProductoId()
    {
        var (vm, _, _, _) = Crear();
        var producto = CrearProducto(7, "Azúcar");

        vm.ProductoFiltroSeleccionado = new OpcionProducto(producto.Nombre, producto);

        Assert.Equal(7, vm.FiltroProductoId);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlSeleccionarTodos_FiltroProductoIdVuelveANull()
    {
        var (vm, _, _, _) = Crear();
        vm.ProductoFiltroSeleccionado = new OpcionProducto("Azúcar", CrearProducto(7));

        vm.ProductoFiltroSeleccionado = new OpcionProducto("Todos", null);

        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public void ProductoFiltroSeleccionado_AlAsignarNull_FiltroProductoIdVuelveANull()
    {
        var (vm, _, _, _) = Crear();
        vm.ProductoFiltroSeleccionado = new OpcionProducto("Azúcar", CrearProducto(7));

        vm.ProductoFiltroSeleccionado = null;

        Assert.Null(vm.FiltroProductoId);
    }

    [Fact]
    public void TipoFiltroSeleccionado_PorDefecto_EsTodos()
    {
        var (vm, _, _, _) = Crear();

        Assert.Null(vm.TipoFiltroSeleccionado!.Valor);
        Assert.Null(vm.FiltroTipo);
    }

    [Fact]
    public void TipoFiltroSeleccionado_AlAsignarSalida_DerivaFiltroTipo()
    {
        var (vm, _, _, _) = Crear();
        var opcionSalida = vm.TiposDisponibles.Single(o => o.Valor == TipoMovimiento.Salida);

        vm.TipoFiltroSeleccionado = opcionSalida;

        Assert.Equal(TipoMovimiento.Salida, vm.FiltroTipo);
    }
}
