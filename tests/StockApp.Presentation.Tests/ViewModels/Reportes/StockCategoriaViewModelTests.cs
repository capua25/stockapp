using System.Collections.Generic;
using System.IO;
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
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<StockCategoriaDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        servicioMock
            .Setup(s => s.ObtenerStockPorCategoriaAsync())
            .ReturnsAsync(items ?? new List<StockCategoriaDto>());

        var vm = new StockCategoriaViewModel(
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    // ── bugfix "pantalla muda ante un 403": CargarAsync no debe escalar un 403/401, y debe dejar
    // un estado bindeable para que la vista muestre EstadoVacio. ──
    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        var (vm, servicioMock, _, _, _) = Crear();
        servicioMock.Setup(s => s.ObtenerStockPorCategoriaAsync()).ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task BuscarCommand_LlamaObtenerStockPorCategoriaAsync_YPopulaItems()
    {
        var items = new List<StockCategoriaDto> { CrearItem("Almacén"), CrearItem("Bebidas") };
        var (vm, servicioMock, _, _, _) = Crear(items);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerStockPorCategoriaAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerStockPorCategoriaAsync_YPopulaItems()
    {
        var items = new List<StockCategoriaDto> { CrearItem("Almacén"), CrearItem("Bebidas") };
        var (vm, servicioMock, _, _, _) = Crear(items);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerStockPorCategoriaAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<StockCategoriaDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, _) = Crear(items);

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

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<StockCategoriaDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock, confirmMock) = Crear(items);
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<StockCategoriaDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv-generado");
        guardadoMock
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }
}
