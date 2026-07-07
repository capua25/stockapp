using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Collections;
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
        var fechaDesdeLocal = new DateTime(2026, 1, 1);
        var fechaHastaLocal = new DateTime(2026, 12, 31);
        vm.FechaDesde = fechaDesdeLocal;
        vm.FechaHasta = fechaHastaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        // BUG DE HUSO HORARIO: FechaDesde/FechaHasta vienen en hora LOCAL del CalendarDatePicker;
        // el repo compara contra MovimientoStock.Fecha (persistida en UTC), así que el VM debe
        // convertir antes de delegar. Offset calculado desde TimeZoneInfo.Local, no hardcodeado,
        // para no acoplar el test a la TZ del entorno (America/Montevideo, UTC-3, en CI/dev).
        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(fechaDesdeLocal);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(fechaHastaLocal);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.ProductoId == 5 &&
            f.Tipo == TipoMovimiento.Salida &&
            f.FechaDesde == fechaDesdeLocal - offsetDesde &&
            f.FechaHasta == fechaHastaLocal - offsetHasta)), Times.Once);
    }

    /// <summary>
    /// HM-HORARIO: reproduce el bug reportado por el usuario (Argentina, UTC-3) — un
    /// movimiento de las 23:00 hora local caía fuera del filtro "hasta hoy" porque
    /// FechaHasta se comparaba cruda contra Fecha (UTC) sin convertir.
    /// </summary>
    [Fact]
    public async Task BuscarAsync_ConFechaLocal_ConvierteAUtcAntesDeDelegarAlService()
    {
        var (vm, svcMock, _, _) = Crear();
        var fechaLocal = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = fechaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        var offset = TimeZoneInfo.Local.GetUtcOffset(fechaLocal);
        svcMock.Verify(s => s.ObtenerHistorialAsync(It.Is<HistorialMovimientoFiltro>(f =>
            f.FechaDesde == fechaLocal - offset)), Times.Once);
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

    // ── ItemsView: fix de ordenamiento por click en encabezados (Avalonia 12, regresión #21129) ──

    [Fact]
    public async Task ItemsView_EsOrdenable()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2) };
        var (vm, _, _, _) = Crear(lista);

        await vm.CargarAsync();

        Assert.NotNull(vm.ItemsView);
        Assert.IsType<DataGridCollectionView>(vm.ItemsView);
        Assert.True(vm.ItemsView.CanSort);
    }

    [Fact]
    public async Task ItemsView_AlAplicarSortDescription_OrdenaLosItems()
    {
        var desordenados = new List<MovimientoHistorialDto>
        {
            CrearDto(1) with { Fecha = new DateTime(2026, 6, 15) },
            CrearDto(2) with { Fecha = new DateTime(2026, 1, 10) },
            CrearDto(3) with { Fecha = new DateTime(2026, 3, 20) },
        };
        var (vm, _, _, _) = Crear(desordenados);
        await vm.CargarAsync();

        vm.ItemsView.SortDescriptions.Add(
            DataGridSortDescription.FromPath(nameof(MovimientoHistorialDto.Fecha), ListSortDirection.Ascending));

        var ordenados = vm.ItemsView.Cast<MovimientoHistorialDto>().ToList();
        Assert.Equal(3, ordenados.Count);
        Assert.Equal(new DateTime(2026, 1, 10), ordenados[0].Fecha);
        Assert.Equal(new DateTime(2026, 3, 20), ordenados[1].Fecha);
        Assert.Equal(new DateTime(2026, 6, 15), ordenados[2].Fecha);
    }

    [Fact]
    public async Task Items_TrasRecarga_SeReflejanEnItemsView()
    {
        var (vm, svcMock, _, _) = Crear(new List<MovimientoHistorialDto> { CrearDto(1) });
        await vm.CargarAsync();
        Assert.Single(vm.ItemsView.Cast<MovimientoHistorialDto>());

        var nuevaLista = new List<MovimientoHistorialDto> { CrearDto(10), CrearDto(11), CrearDto(12) };
        svcMock
            .Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(nuevaLista);

        await vm.CargarAsync();

        Assert.Equal(3, vm.Items.Count);
        Assert.Equal(3, vm.ItemsView.Cast<MovimientoHistorialDto>().Count());
    }
}
