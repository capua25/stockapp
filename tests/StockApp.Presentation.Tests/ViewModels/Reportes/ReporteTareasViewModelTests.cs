using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ReporteTareasViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (
        ReporteTareasViewModel vm,
        Mock<IReporteTareasService> servicioMock,
        Mock<ICsvExporter> csvExporterMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<ICurrentSession> sessionMock)
        Crear(ReporteTareasDto? respuesta = null)
    {
        var servicioMock = new Mock<IReporteTareasService>();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ReturnsAsync(respuesta ?? new ReporteTareasDto(new List<FilaReporteTareas>(), 0));

        var csvExporterMock = new Mock<ICsvExporter>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new ReporteTareasViewModel(
            servicioMock.Object, csvExporterMock.Object, pdfExporterMock.Object,
            guardadoMock.Object, aperturaMock.Object, confirmMock.Object, sessionMock.Object);
        return (vm, servicioMock, csvExporterMock, pdfExporterMock, guardadoMock, aperturaMock, confirmMock, sessionMock);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultRango_EsElAnioEnCurso()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();

        Assert.Equal(new DateTime(DateTime.Now.Year, 1, 1), vm.FechaDesde);
        Assert.Equal(new DateTime(DateTime.Now.Year, 12, 31), vm.FechaHasta);
    }

    [Fact]
    public void Constructor_DefaultAgrupadorEsZona_YCriterioEsCreacion()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();

        Assert.Equal(AgrupadorTareas.Zona, vm.AgrupadorSeleccionado.Valor);
        Assert.True(vm.EsCriterioCreacion);
        Assert.False(vm.EsCriterioCierre);
    }

    [Fact]
    public void EsCriterioCierre_AlPonerloEnTrue_CambiaCriterioSeleccionado_YDesmarcaElOtro()
    {
        var (vm, _, _, _, _, _, _, _) = Crear();

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
        var (vm, _, _, _, _, _, _, _) = Crear(dto);

        await vm.CargarAsync();

        Assert.Same(filas, vm.Items);
        Assert.Equal(4, vm.TotalGeneral);
    }

    [Fact]
    public async Task CargarAsync_ConvierteFechasLocalesAUtc_AntesDeLlamarAlServicio()
    {
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
        vm.FechaDesde = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConFechaHastaNula_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
        vm.FechaHasta = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        // bugfix "pantalla muda ante un 403": mismo criterio que el resto de los reportes.
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorTareas.Expediente);
        vm.EsCriterioCierre = true;

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteTareas>(f =>
            f.Agrupador == AgrupadorTareas.Expediente && f.Criterio == CriterioFechaTareas.Cierre)), Times.Once);
    }

    // ── Export CSV/PDF (spec 2026-09-18): única pantalla del alcance sin ninguna exportación
    // previa. Columnas FIJAS: el agrupador cambia filas y contenido, nunca columnas. ──────────

    private static FilaReporteTareas Fila(string clasificador) => new(clasificador, 1, 0, 2, 0, 3);

    [Fact]
    public async Task ExportarCsvCommand_UsaLasSeisColumnasFijasDeLaMatriz()
    {
        var dto = new ReporteTareasDto(new List<FilaReporteTareas> { Fila("Centro") }, 3);
        var (vm, _, csvExporterMock, _, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        csvExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        guardadoMock.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        // Lista literal hardcodeada -- nunca comparar contra ReporteTareasViewModel.ColumnasCsv
        // (sería tautológico: mutar el VALOR de la constante nunca pondría este assert en rojo
        // porque el código de producción y el assert leerían la misma referencia estática).
        var esperado = new[] { "Clasificador", "Pendientes", "EnCurso", "Terminadas", "Canceladas", "Total" };
        csvExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado) && cols.Count == 6)),
            Times.Once);
    }

    [Fact]
    public async Task ExportarCsvCommand_SinItems_NoExporta()
    {
        var (vm, _, csvExporterMock, _, _, _, _, _) = Crear();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        csvExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_UsaLasSeisColumnasFijasDeLaMatriz()
    {
        var dto = new ReporteTareasDto(new List<FilaReporteTareas> { Fila("Centro") }, 3);
        var (vm, _, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        var esperado = new[] { "Clasificador", "Pendientes", "EnCurso", "Terminadas", "Canceladas", "Total" };
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteTareas>>(),
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado) && cols.Count == 6),
            It.IsAny<MetadatosDocumento>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, pdfExporterMock, _, _, _, _) = Crear();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_AgregaFilaDeTotalGeneralAlFinalSinAlterarItems()
    {
        // TotalGeneral (spec 2026-09-18): un reporte estadístico impreso sin su cierre está
        // incompleto. Se agrega SOLO al export (PDF), nunca a Items -- la grilla en pantalla no
        // se toca (D23: el ViewModel no calcula nada sobre lo que ve el usuario).
        var filas = new List<FilaReporteTareas>
        {
            new("Centro", 1, 0, 2, 0, 3),
            new("(sin asignar)", 2, 1, 0, 1, 4),
        };
        var dto = new ReporteTareasDto(filas, 7);
        var (vm, _, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        IEnumerable<FilaReporteTareas>? itemsCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<FilaReporteTareas>, IReadOnlyList<string>, MetadatosDocumento>((items, _, _) => itemsCapturados = items)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(itemsCapturados);
        var lista = itemsCapturados!.ToList();
        Assert.Equal(3, lista.Count); // 2 filas + 1 de cierre
        var filaCierre = lista[^1];
        Assert.Equal("Total general", filaCierre.Clasificador);
        Assert.Equal(3, filaCierre.Pendientes); // 1 + 2
        Assert.Equal(1, filaCierre.EnCurso);    // 0 + 1
        Assert.Equal(2, filaCierre.Terminadas); // 2 + 0
        Assert.Equal(1, filaCierre.Canceladas); // 0 + 1
        Assert.Equal(7, filaCierre.Total);      // TotalGeneral, no recalculado
        // Items (lo que ve la grilla) no se toca.
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosDiceAgrupadorCriterioYRango()
    {
        var dto = new ReporteTareasDto(new List<FilaReporteTareas> { Fila("Centro") }, 3);
        var (vm, _, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorTareas.Expediente);
        vm.EsCriterioCierre = true;
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 12, 31);
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteTareas>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<FilaReporteTareas>, IReadOnlyList<string>, MetadatosDocumento>((_, _, m) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Equal("Estadística de tareas", metadatosCapturados!.Titulo);
        Assert.Contains("Expediente", metadatosCapturados.DescripcionFiltros);
        Assert.Contains("cierre", metadatosCapturados.DescripcionFiltros, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("01/01/2026", metadatosCapturados.DescripcionFiltros);
        Assert.Contains("31/12/2026", metadatosCapturados.DescripcionFiltros);
    }
}
