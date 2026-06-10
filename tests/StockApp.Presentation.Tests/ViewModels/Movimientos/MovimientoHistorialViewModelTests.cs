using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Movimientos;
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
            UsuarioId: 1);

    private static (
        MovimientoHistorialViewModel vm,
        Mock<IMovimientoStockService> svcMock,
        Mock<INavigationService> navMock)
        Crear(IReadOnlyList<MovimientoHistorialDto>? items = null)
    {
        var svcMock = new Mock<IMovimientoStockService>();
        var navMock = new Mock<INavigationService>();

        svcMock
            .Setup(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()))
            .ReturnsAsync(items ?? new List<MovimientoHistorialDto>());

        var vm = new MovimientoHistorialViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    // ── D4 tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var lista = new List<MovimientoHistorialDto> { CrearDto(1), CrearDto(2) };
        var (vm, svcMock, _) = Crear(lista);

        await vm.CargarAsync();

        svcMock.Verify(s => s.ObtenerHistorialAsync(It.IsAny<HistorialMovimientoFiltro>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task CargarAsync_SinResultados_ItemsVacio()
    {
        var (vm, _, _) = Crear(new List<MovimientoHistorialDto>());

        await vm.CargarAsync();

        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task BuscarAsync_ConFiltros_DelegaAlServiceConFiltrosConstruidos()
    {
        var (vm, svcMock, _) = Crear();
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
        var (vm, svcMock, _) = Crear();

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
        var (vm, svcMock, _) = Crear(lista);
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
        var (vm, svcMock, _) = Crear();
        vm.ProductoIdParaRecalcular = null;

        await vm.RecalcularCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.RecalcularStockAsync(It.IsAny<int>()), Times.Never);
    }
}
