using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Presentation.Navigation;
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ReporteProyectosViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (
        ReporteProyectosViewModel vm,
        Mock<IReporteProyectosService> servicioMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<ICurrentSession> sessionMock)
        Crear(ReporteProyectosDto? respuesta = null)
    {
        var servicioMock = new Mock<IReporteProyectosService>();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()))
            .ReturnsAsync(respuesta ?? new ReporteProyectosDto(new List<FilaReporteProyectos>(), Total(0)));

        var pdfExporterMock = new Mock<IPdfExporter>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new ReporteProyectosViewModel(
            servicioMock.Object, pdfExporterMock.Object,
            guardadoMock.Object, aperturaMock.Object, confirmMock.Object, sessionMock.Object);
        return (vm, servicioMock, pdfExporterMock, guardadoMock, aperturaMock, confirmMock, sessionMock);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultRango_EsElAnioEnCurso()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.Equal(new DateTime(DateTime.Now.Year, 1, 1), vm.FechaDesde);
        Assert.Equal(new DateTime(DateTime.Now.Year, 12, 31), vm.FechaHasta);
    }

    [Fact]
    public void Constructor_DefaultAgrupadorEsZona_YCriterioEsCreacion()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.Equal(AgrupadorProyectos.Zona, vm.AgrupadorSeleccionado.Valor);
        Assert.True(vm.EsCriterioCreacion);
        Assert.False(vm.EsCriterioCierre);
    }

    [Fact]
    public void EsCriterioCierre_AlPonerloEnTrue_CambiaCriterioSeleccionado_YDesmarcaElOtro()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        vm.EsCriterioCierre = true;

        Assert.Equal(CriterioFechaProyectos.Cierre, vm.CriterioSeleccionado);
        Assert.True(vm.EsCriterioCierre);
        Assert.False(vm.EsCriterioCreacion);
    }

    [Fact]
    public async Task CargarAsync_PasaTalCualLasFilasYElTotalGeneral_SinCalcularNada()
    {
        // D23: el ViewModel no calcula nada -- verifica passthrough exacto (misma instancia).
        var filas = new List<FilaReporteProyectos>
        {
            new("Centro", 1, 0, 2, 0, 3),
            new("(sin asignar)", 0, 0, 0, 1, 1),
        };
        var dto = new ReporteProyectosDto(filas, Total(4));
        var (vm, _, _, _, _, _, _) = Crear(dto);

        await vm.CargarAsync();

        Assert.Same(filas, vm.Items);
        Assert.Equal(4, vm.TotalGeneral);
    }

    [Fact]
    public async Task CargarAsync_ConvierteFechasLocalesAUtc_AntesDeLlamarAlServicio()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        var desde = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var hasta = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.CargarAsync();

        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteProyectos>(f =>
            f.Desde == desde - offsetDesde && f.Hasta == hasta - offsetHasta)), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        vm.FechaDesde = new DateTime(2026, 12, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConFechaDesdeNula_NoLlamaAlServicioYSeteaMensajeError()
    {
        // D18: el rango es obligatorio -- si el usuario borra una fecha del CalendarDatePicker
        // (FechaDesde queda null), el ViewModel rechaza ACÁ, nunca manda null al servicio.
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        vm.FechaDesde = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task BuscarCommand_ConFechaHastaNula_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        vm.FechaHasta = null;

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        // bugfix "pantalla muda ante un 403": mismo criterio que el resto de los reportes.
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task CargarAsync_UsaElAgrupadorYCriterioSeleccionados()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorProyectos.OrganismoResponsable);
        vm.EsCriterioCierre = true;

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteProyectos>(f =>
            f.Agrupador == AgrupadorProyectos.OrganismoResponsable && f.Criterio == CriterioFechaProyectos.Cierre)), Times.Once);
    }

    // ── Export PDF (spec 2026-09-18): única pantalla del alcance sin ninguna exportación
    // previa. Columnas FIJAS: el agrupador cambia filas y contenido, nunca columnas. ──────────

    private static FilaReporteProyectos Total(int total) => new("Total general", 0, 0, 0, 0, total);

    private static FilaReporteProyectos Fila(string clasificador) => new(clasificador, 1, 0, 2, 0, 3);

    [Fact]
    public async Task ExportarPdfCommand_UsaLasSeisColumnasFijasDeLaMatriz()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        var esperado = new[]
        {
            new ColumnaPdf("Clasificador", "Clasificador"),
            new ColumnaPdf("Pendientes", "Pendientes"),
            new ColumnaPdf("EnCurso", "En curso"),
            new ColumnaPdf("Terminados", "Terminados"),
            new ColumnaPdf("Cancelados", "Cancelados"),
            new ColumnaPdf("Total", "Total"),
        };
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteProyectos>>(),
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado) && cols.Count == 6),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConNombreCompleto_UsaNombreCompletoComoUsuarioEmisor()
    {
        // Tarea 21: el helper Crear() siempre construia UsuarioSesion con NombreCompleto null,
        // asi que ningun test ejercitaba la rama principal del fallback
        // (_session.UsuarioActual?.NombreCompleto ?? ... ?? "Sistema") -- ni siquiera la de
        // "admin" (NombreUsuario), este VM no tenia ningun test de UsuarioEmisor.
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, _, _, sessionMock) = Crear(dto);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, "Ana Pérez"));
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Callback<IEnumerable<FilaReporteProyectos>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((_, _, m, _) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal("Ana Pérez", metadatosCapturados!.UsuarioEmisor);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, pdfExporterMock, _, _, _, _) = Crear();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
            Times.Never);
    }

    /// <summary>
    /// Los cuatro tests que siguen son el set estándar de export PDF que ya tenían las otras 8
    /// pantallas y a esta le faltaba entero (review final, Importante 4) -- justo la única del
    /// alcance con lógica propia no compartida (la fila "Total general"). Mismo patrón y mismos
    /// nombres que los de <c>ControlPoaViewModelTests</c>, para que el set se lea igual en las 9.
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var filas = Enumerable.Range(1, 600).Select(i => Fila($"Zona {i}")).ToList();
        var dto = new ReporteProyectosDto(filas, Total(filas.Count * 3));
        var (vm, _, pdfExporterMock, _, _, confirmMock, _) = Crear(dto);
        await vm.CargarAsync();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, _, confirmMock, _) = Crear(dto);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoExitoso_OfreceAbrirElArchivo()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, aperturaMock, confirmMock, _) = Crear(dto);
        await vm.CargarAsync();
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "reporte-proyectos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("reporte-proyectos.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, aperturaMock, confirmMock, _) = Crear(dto);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "reporte-proyectos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    /// <summary>
    /// Migrado al mecanismo genérico de resumen (spec de totales/resumen, 2026-09-22): antes esta
    /// pantalla fabricaba una fila SINTÉTICA "Total general" del mismo DTO
    /// (<c>ConstruirFilasConTotalGeneral</c>, ya eliminado) y la mezclaba en la colección que
    /// recibía el exportador -- un parche, porque esa fila no es un item real y un consumidor
    /// futuro de <c>Items</c> no tiene forma de distinguirla. Ahora <c>Items</c> viaja TAL CUAL
    /// (mismo criterio D23 que antes) y el total va aparte, en <see cref="ResumenPdf"/>.
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_PasaLosItemsSinFilaSinteticaYElTotalGeneralEnElResumen()
    {
        var filas = new List<FilaReporteProyectos>
        {
            new("Centro", 1, 0, 2, 0, 3),
            new("(sin asignar)", 2, 1, 0, 1, 4),
        };
        var dto = new ReporteProyectosDto(filas, Total(7));
        var (vm, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        IEnumerable<FilaReporteProyectos>? itemsCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Callback<IEnumerable<FilaReporteProyectos>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((items, _, _, _) => itemsCapturados = items)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        // Items viaja SIN fila sintética -- las 2 filas reales, ni una más.
        Assert.NotNull(itemsCapturados);
        var lista = itemsCapturados!.ToList();
        Assert.Equal(2, lista.Count);
        Assert.Same(vm.Items, itemsCapturados);
        // La grilla en pantalla tampoco se toca (D23).
        Assert.Equal(2, vm.Items.Count);

        // El total general va en el resumen, con el ARGUMENTO REAL (no It.IsAny<ResumenPdf>()).
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteProyectos>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(),
            It.Is<ResumenPdf>(r =>
                r.Totales.Count == 1 &&
                r.Totales[0].Etiqueta == "Total general" &&
                Equals(r.Totales[0].Valor, 7) &&
                r.Secciones.Count == 0)),
            Times.Once);
    }

    [Fact]
    public void AgrupadoresDisponibles_SinExpediente()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.Equal(
            new[]
            {
                AgrupadorProyectos.Zona, AgrupadorProyectos.DimensionTematica,
                AgrupadorProyectos.OrganismoResponsable, AgrupadorProyectos.OrigenFinanciamiento,
            },
            vm.AgrupadoresDisponibles.Select(o => o.Valor).ToArray());
        Assert.Equal(4, vm.AgrupadoresDisponibles.Count);
    }

    [Fact]
    public async Task ExportarPdf_TituloEstadisticaDeProyectos()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        await vm.CargarAsync();
        MetadatosDocumento? capturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Callback<IEnumerable<FilaReporteProyectos>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((_, _, m, _) => capturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.Equal("Estadística de proyectos", capturados!.Titulo);
    }

    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosDiceAgrupadorCriterioYRango()
    {
        var dto = new ReporteProyectosDto(new List<FilaReporteProyectos> { Fila("Centro") }, Total(3));
        var (vm, _, pdfExporterMock, guardadoMock, _, _, _) = Crear(dto);
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorProyectos.OrganismoResponsable);
        vm.EsCriterioCierre = true;
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 12, 31);
        await vm.CargarAsync();
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Callback<IEnumerable<FilaReporteProyectos>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((_, _, m, _) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Equal("Estadística de proyectos", metadatosCapturados!.Titulo);
        Assert.Contains("Organismo responsable", metadatosCapturados.DescripcionFiltros);
        Assert.Contains("cierre", metadatosCapturados.DescripcionFiltros, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("01/01/2026", metadatosCapturados.DescripcionFiltros);
        Assert.Contains("31/12/2026", metadatosCapturados.DescripcionFiltros);
    }

    // ── Fase 6: estados v2, consulta vigente, PDF de lo cargado, vuelta al hub y criterio como combo ──

    private static FilaReporteProyectos CrearFila(string clasificador = "Zona Norte")
        => new FilaReporteProyectos(clasificador, 1, 2, 3, 4, 10);

    private static (ReporteProyectosViewModel vm, Mock<IReporteProyectosService> servicio, Mock<IPdfExporter> pdf, Mock<INavigationService> nav)
        Armar(bool conNavegacion = true)
    {
        var servicio = new Mock<IReporteProyectosService>();
        var pdf = new Mock<IPdfExporter>();
        var nav = new Mock<INavigationService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        servicio.Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()))
            .ReturnsAsync(new ReporteProyectosDto(new List<FilaReporteProyectos> { CrearFila() }, Total(10)));
        var vm = new ReporteProyectosViewModel(
            servicio.Object, pdf.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IServicioAperturaArchivo>(),
            Mock.Of<IConfirmacionService>(), session.Object, conNavegacion ? nav.Object : null);
        return (vm, servicio, pdf, nav);
    }

    private static void PonerFalla(Mock<IReporteProyectosService> servicio, Exception ex)
        => servicio.Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>())).ThrowsAsync(ex);

    [Fact]
    public async Task SinConexion_MarcaSinConexion_VaciaLasFilas_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.CargarAsync();
        Assert.NotEmpty(vm.Items);
        PonerFalla(servicio, new ServidorNoDisponibleException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
        Assert.Equal(0, vm.TotalGeneral);
    }

    [Fact]
    public async Task Un403_MarcaSinPermiso_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        PonerFalla(servicio, new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task ExportarPdf_SinFilas_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Armar();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        await vm.CargarAsync();
        vm.Items = new List<FilaReporteProyectos>();
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportarPdf_ConFilas_SeHabilita_YSeAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        await vm.CargarAsync();

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }

    [Fact]
    public async Task ExportarPdf_TrasUn403_EstaDeshabilitado()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.CargarAsync();
        PonerFalla(servicio, new UnauthorizedAccessException());
        await vm.CargarAsync();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public void VolverCommand_NavegaAlHub()
    {
        var (vm, _, _, nav) = Armar();

        vm.VolverCommand.Execute(null);

        nav.Verify(n => n.Navegar<ReportesViewModel>(), Times.Once);
    }

    [Fact]
    public void VolverCommand_SinNavegacion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Armar(conNavegacion: false);

        Assert.False(vm.VolverCommand.CanExecute(null));
    }

    [Fact]
    public void TextosDelVacio_SonLosAprobados()
    {
        var (vm, _, _, _) = Armar();

        Assert.Equal("No hay proyectos en el período", vm.TituloVacio);
        Assert.Equal("Probá con otro período o criterio.", vm.MensajeVacio);
    }

    [Fact]
    public async Task DosBusquedas_LaPrimeraTerminaTarde_NoPisaLaSegunda()
    {
        var (vm, servicio, _, _) = Armar();
        var primera = new TaskCompletionSource<ReporteProyectosDto>();
        var segunda = new TaskCompletionSource<ReporteProyectosDto>();
        servicio.SetupSequence(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>())).Returns(primera.Task).Returns(segunda.Task);

        var vieja = vm.CargarAsync();
        var nueva = vm.CargarAsync();
        var filasNuevas = new List<FilaReporteProyectos> { CrearFila("Zona Sur") };
        segunda.SetResult(new ReporteProyectosDto(filasNuevas, Total(5)));
        await nueva;
        primera.SetResult(new ReporteProyectosDto(new List<FilaReporteProyectos> { CrearFila() }, Total(99)));
        await vieja;

        Assert.Same(filasNuevas, vm.Items);
        Assert.Equal(5, vm.TotalGeneral);
    }

    [Fact]
    public async Task ExportarPdf_ConFechasCambiadasSinBuscar_RotulaElPeriodoCargado()
    {
        var (vm, _, pdf, _) = Armar();
        vm.FechaDesde = new DateTime(2026, 9, 1);
        vm.FechaHasta = new DateTime(2026, 9, 30);
        await vm.CargarAsync();
        vm.FechaDesde = new DateTime(2026, 8, 1);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaReporteProyectos>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros.Contains("01/09/2026") && !m.DescripcionFiltros.Contains("01/08/2026")),
            It.IsAny<ResumenPdf?>()), Times.Once);
    }

    [Fact]
    public async Task SinConexion_Reintentar_ConsultaConLosFiltrosDePantalla()
    {
        var (vm, servicio, _, _) = Armar();
        PonerFalla(servicio, new ServidorNoDisponibleException());
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);
        servicio.Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteProyectos>()))
            .ReturnsAsync(new ReporteProyectosDto(new List<FilaReporteProyectos> { CrearFila() }, Total(10)));
        vm.CriterioOpcionSeleccionada = vm.CriteriosDisponibles[1];

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        servicio.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteProyectos>(f => f.Criterio == CriterioFechaProyectos.Cierre)), Times.Once);
    }

    [Fact]
    public void CriteriosDisponibles_SonCreacionYCierre()
    {
        var (vm, _, _, _) = Armar();

        Assert.Equal(
            new[] { ("Fecha de creación", CriterioFechaProyectos.Creacion), ("Fecha de cierre", CriterioFechaProyectos.Cierre) },
            vm.CriteriosDisponibles.Select(o => (o.Nombre, o.Valor)));
        Assert.Equal(CriterioFechaProyectos.Creacion, vm.CriterioOpcionSeleccionada.Valor);
    }

    [Fact]
    public void ElegirCierreEnElCombo_CambiaElCriterio_YEsCriterioCierre()
    {
        var (vm, _, _, _) = Armar();

        vm.CriterioOpcionSeleccionada = vm.CriteriosDisponibles[1];

        Assert.Equal(CriterioFechaProyectos.Cierre, vm.CriterioSeleccionado);
        Assert.True(vm.EsCriterioCierre);
        Assert.False(vm.EsCriterioCreacion);
    }

    [Fact]
    public void CambiarCriterioSeleccionado_ActualizaElCombo()
    {
        var (vm, _, _, _) = Armar();

        vm.CriterioSeleccionado = CriterioFechaProyectos.Cierre;

        Assert.Equal(CriterioFechaProyectos.Cierre, vm.CriterioOpcionSeleccionada.Valor);
    }

    [Fact]
    public void ExportarPdf_AlAsignarFilas_SeHabilitaYAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        vm.Items = new List<FilaReporteProyectos> { CrearFila() };

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }
}
