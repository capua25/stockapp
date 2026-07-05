using System;
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

public class MasMovidosViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static MasMovidoDto CrearItem(int id = 1)
        => new MasMovidoDto(
            ProductoId: id,
            Codigo: $"P{id:000}",
            Nombre: "Azúcar",
            CantidadMovimientos: 12,
            VolumenTotal: 340m);

    private static (
        MasMovidosViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock)
        Crear(IReadOnlyList<MasMovidoDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();

        servicioMock
            .Setup(s => s.ObtenerMasMovidosAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(items ?? new List<MasMovidoDto>());

        var vm = new MasMovidosViewModel(servicioMock.Object, exporterMock.Object, guardadoMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerMasMovidosAsync_ConTopN()
    {
        var items = new List<MasMovidoDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _) = Crear(items);

        // TopN arranca en 20 por default — verificamos que se respeta.
        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.Equal(20, vm.TopN);
        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(desde, hasta, 20), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<MasMovidoDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock) = Crear(items);

        var esperado = new[]
        {
            "ProductoId", "Codigo", "Nombre", "CantidadMovimientos", "VolumenTotal"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "mas-movidos.csv"), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 2, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConRangoValido_LimpiaMensajeError()
    {
        var (vm, servicioMock, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrEmpty(vm.MensajeError));
        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Once);
    }
}
