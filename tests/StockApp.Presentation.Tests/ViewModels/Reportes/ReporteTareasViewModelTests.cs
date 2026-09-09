using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Reportes;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ReporteTareasViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ReporteTareasViewModel vm, Mock<IReporteTareasService> servicioMock)
        Crear(ReporteTareasDto? respuesta = null)
    {
        var servicioMock = new Mock<IReporteTareasService>();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ReturnsAsync(respuesta ?? new ReporteTareasDto(new List<FilaReporteTareas>(), 0));

        var vm = new ReporteTareasViewModel(servicioMock.Object);
        return (vm, servicioMock);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultRango_EsElAnioEnCurso()
    {
        var (vm, _) = Crear();

        Assert.Equal(new DateTime(DateTime.Now.Year, 1, 1), vm.FechaDesde);
        Assert.Equal(new DateTime(DateTime.Now.Year, 12, 31), vm.FechaHasta);
    }

    [Fact]
    public void Constructor_DefaultAgrupadorEsZona_YCriterioEsCreacion()
    {
        var (vm, _) = Crear();

        Assert.Equal(AgrupadorTareas.Zona, vm.AgrupadorSeleccionado.Valor);
        Assert.True(vm.EsCriterioCreacion);
        Assert.False(vm.EsCriterioCierre);
    }

    [Fact]
    public void EsCriterioCierre_AlPonerloEnTrue_CambiaCriterioSeleccionado_YDesmarcaElOtro()
    {
        var (vm, _) = Crear();

        vm.EsCriterioCierre = true;

        Assert.Equal(CriterioFechaTareas.Cierre, vm.CriterioSeleccionado);
        Assert.True(vm.EsCriterioCierre);
        Assert.False(vm.EsCriterioCreacion);
    }

    [Fact]
    public async Task CargarAsync_PasaTalCualLasFilasYElTotalGeneral_SinCalcularNada()
    {
        // D23: el ViewModel no calcula nada -- verifica passthrough exacto (misma instancia).
        var filas = new List<FilaReporteTareas>
        {
            new("Centro", 1, 0, 2, 0, 3),
            new("(sin asignar)", 0, 0, 0, 1, 1),
        };
        var dto = new ReporteTareasDto(filas, 4);
        var (vm, _) = Crear(dto);

        await vm.CargarAsync();

        Assert.Same(filas, vm.Items);
        Assert.Equal(4, vm.TotalGeneral);
    }

    [Fact]
    public async Task CargarAsync_ConvierteFechasLocalesAUtc_AntesDeLlamarAlServicio()
    {
        var (vm, servicioMock) = Crear();
        var desde = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var hasta = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.CargarAsync();

        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteTareas>(f =>
            f.Desde == desde - offsetDesde && f.Hasta == hasta - offsetHasta)), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock) = Crear();
        vm.FechaDesde = new DateTime(2026, 12, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConFechaDesdeNula_NoLlamaAlServicioYSeteaMensajeError()
    {
        // D18: el rango es obligatorio -- si el usuario borra una fecha del CalendarDatePicker
        // (FechaDesde queda null), el ViewModel rechaza ACÁ, nunca manda null al servicio.
        var (vm, servicioMock) = Crear();
        vm.FechaDesde = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConFechaHastaNula_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock) = Crear();
        vm.FechaHasta = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        // bugfix "pantalla muda ante un 403": mismo criterio que el resto de los reportes.
        var (vm, servicioMock) = Crear();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task CargarAsync_UsaElAgrupadorYCriterioSeleccionados()
    {
        var (vm, servicioMock) = Crear();
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorTareas.Expediente);
        vm.EsCriterioCierre = true;

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteTareas>(f =>
            f.Agrupador == AgrupadorTareas.Expediente && f.Criterio == CriterioFechaTareas.Cierre)), Times.Once);
    }
}
