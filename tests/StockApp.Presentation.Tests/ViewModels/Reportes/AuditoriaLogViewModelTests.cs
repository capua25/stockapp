using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auditoria;
using StockApp.Application.Exportacion;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class AuditoriaLogViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static AuditoriaItemDto CrearItem(int entidadId = 1)
        => new AuditoriaItemDto(
            Fecha: new DateTime(2026, 1, 15),
            NombreUsuario: "admin",
            Accion: AccionAuditada.CambioPrecio,
            Entidad: "Producto",
            EntidadId: entidadId,
            Detalle: "5 -> 8");

    private static (
        AuditoriaLogViewModel vm,
        Mock<IAuditoriaQueryService> servicioMock,
        Mock<ICsvExporter> exporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock)
        Crear(IReadOnlyList<AuditoriaItemDto>? items = null)
    {
        var servicioMock = new Mock<IAuditoriaQueryService>();
        var exporterMock = new Mock<ICsvExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();

        servicioMock
            .Setup(s => s.ObtenerLogAsync(
                It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()))
            .ReturnsAsync(items ?? new List<AuditoriaItemDto>());

        var vm = new AuditoriaLogViewModel(servicioMock.Object, exporterMock.Object, guardadoMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuscarCommand_LlamaObtenerLogAsync_ConFiltros()
    {
        var items = new List<AuditoriaItemDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _) = Crear(items);

        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        vm.UsuarioId = 9;
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerLogAsync(9, desde, hasta), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConItems()
    {
        var items = new List<AuditoriaItemDto> { CrearItem() };
        var (vm, _, exporterMock, guardadoMock) = Crear(items);

        var esperado = new[]
        {
            "Fecha", "NombreUsuario", "Accion", "Entidad", "EntidadId", "Detalle"
        };

        const string csvResultante = "csv-generado";
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<AuditoriaItemDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns(csvResultante);

        await vm.BuscarCommand.ExecuteAsync(null);
        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(c => c.SequenceEqual(esperado))),
            Times.Once);

        guardadoMock.Verify(g => g.GuardarTextoAsync(csvResultante, "auditoria.csv"), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 2, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Never);
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
        servicioMock.Verify(s => s.ObtenerLogAsync(
            It.IsAny<int?>(), It.IsAny<DateTime?>(), It.IsAny<DateTime?>()), Times.Once);
    }
}
