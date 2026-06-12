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

public class StockCategoriaViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static StockCategoriaDto CrearItem(string categoria = "Almacén")
        => new StockCategoriaDto(
            Categoria: categoria,
            CantidadProductos: 3,
            StockTotal: 30m,
            ValorCosto: 150m,
            ValorVenta: 240m);

    private static (
        StockCategoriaViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock)
        Crear(IReadOnlyList<StockCategoriaDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();

        servicioMock
            .Setup(s => s.ObtenerStockPorCategoriaAsync())
            .ReturnsAsync(items ?? new List<StockCategoriaDto>());

        var vm = new StockCategoriaViewModel(servicioMock.Object, exporterMock.Object, guardadoMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerStockPorCategoriaAsync_YPopulaItems()
    {
        var items = new List<StockCategoriaDto> { CrearItem("Almacén"), CrearItem("Bebidas") };
        var (vm, servicioMock, _, _) = Crear(items);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerStockPorCategoriaAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<StockCategoriaDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock) = Crear(items);

        var esperado = new[]
        {
            "Categoria", "CantidadProductos", "StockTotal", "ValorCosto", "ValorVenta"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<StockCategoriaDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "stock-categoria.csv"), Times.Once);
    }
}
