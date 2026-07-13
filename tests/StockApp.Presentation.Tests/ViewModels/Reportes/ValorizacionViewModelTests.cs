using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Reportes;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ValorizacionViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ValorizacionItemDto CrearItem(int id = 1)
        => new ValorizacionItemDto(
            ProductoId: id,
            Codigo: $"P{id:000}",
            Nombre: "Azúcar",
            Categoria: "Almacén",
            StockActual: 10m,
            PrecioCosto: 5m,
            PrecioVenta: 8m,
            ValorCosto: 50m,
            ValorVenta: 80m);

    private static (
        ValorizacionViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock)
        Crear(
            IReadOnlyList<ValorizacionItemDto>? items = null,
            ValorizacionTotalesDto? totales = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();

        servicioMock
            .Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(
                items ?? new List<ValorizacionItemDto>(),
                totales ?? new ValorizacionTotalesDto(0m, 0m)));

        var vm = new ValorizacionViewModel(servicioMock.Object, exporterMock.Object, guardadoMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerValorizacionAsync_YPopulaItems()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1), CrearItem(2) };
        var totales = new ValorizacionTotalesDto(TotalValorCosto: 100m, TotalValorVenta: 160m);
        var (vm, servicioMock, _, _) = Crear(items, totales);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
        Assert.NotNull(vm.Totales);
        Assert.Equal(100m, vm.Totales!.TotalValorCosto);
        Assert.Equal(160m, vm.Totales.TotalValorVenta);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerValorizacion_YPopulaItemsYTotales()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1), CrearItem(2) };
        var totales = new ValorizacionTotalesDto(TotalValorCosto: 100m, TotalValorVenta: 160m);
        var (vm, servicioMock, _, _) = Crear(items, totales);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
        Assert.NotNull(vm.Totales);
        Assert.Equal(100m, vm.Totales!.TotalValorCosto);
        Assert.Equal(160m, vm.Totales.TotalValorVenta);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConOrdenColumnasFijo()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, exporterMock, guardadoMock) = Crear(items);

        var esperado = new[]
        {
            "ProductoId", "Codigo", "Nombre", "Categoria",
            "StockActual", "PrecioCosto", "PrecioVenta", "ValorCosto", "ValorVenta"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        // Poblamos Items vía BuscarCommand (no exponemos el setter).
        await vm.BuscarCommand.ExecuteAsync(null);

        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "valorizacion.csv"), Times.Once);
    }

    [Fact]
    public async Task ExportarCommand_SinItems_NoExporta()
    {
        var (vm, _, exporterMock, guardadoMock) = Crear(new List<ValorizacionItemDto>());

        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(
            e => e.Exportar(It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
        guardadoMock.Verify(
            g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }
}
