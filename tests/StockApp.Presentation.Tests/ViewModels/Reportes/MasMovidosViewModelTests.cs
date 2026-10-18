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
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<MasMovidoDto>? items = null)
    {
        var servicioMock = new Mock<IReporteStockService>();
        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmMock = new Mock<IConfirmacionService>();
        var pdfExporterMock = new Mock<IPdfExporter>();
        var aperturaMock = new Mock<IServicioAperturaArchivo>();
        var sessionMock = new Mock<ICurrentSession>();
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        servicioMock
            .Setup(s => s.ObtenerMasMovidosAsync(
                It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(items ?? new List<MasMovidoDto>());

        var vm = new MasMovidosViewModel(
            servicioMock.Object, guardadoMock.Object, confirmMock.Object,
            pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, sessionMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    // ── bugfix "pantalla muda ante un 403": CargarAsync no debe escalar un 403/401, y debe dejar
    // un estado bindeable para que la vista muestre EstadoVacio. ──
    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        servicioMock
            .Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task BuscarCommand_LlamaObtenerMasMovidosAsync_ConTopN()
    {
        var items = new List<MasMovidoDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _, _) = Crear(items);

        // TopN arranca en 20 por default — verificamos que se respeta.
        var desde = new DateTime(2026, 1, 1);
        var hasta = new DateTime(2026, 1, 31);
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.BuscarCommand.ExecuteAsync(null);

        // BUG DE HUSO HORARIO: desde/hasta vienen en hora LOCAL del CalendarDatePicker; el VM
        // debe convertirlas a UTC antes de delegar al servicio (que compara contra
        // MovimientoStock.Fecha, persistida en UTC). Offset calculado desde TimeZoneInfo.Local
        // para no acoplar el test a la TZ del entorno.
        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        Assert.Equal(20, vm.TopN);
        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            desde - offsetDesde, hasta - offsetHasta, 20), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    /// <summary>
    /// Reproduce el bug reportado por el usuario (Argentina, UTC-3): sin la conversión, un
    /// movimiento de las 23:00 hora local caía fuera del filtro "hasta hoy".
    /// </summary>
    [Fact]
    public async Task BuscarCommand_ConFechaLocal_ConvierteAUtcAntesDeDelegarAlServicio()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();
        var fechaLocal = new DateTime(2026, 6, 10, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = fechaLocal;

        await vm.BuscarCommand.ExecuteAsync(null);

        var offset = TimeZoneInfo.Local.GetUtcOffset(fechaLocal);
        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            fechaLocal - offset, null, 20), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerMasMovidosAsync_YPopulaItems()
    {
        var items = new List<MasMovidoDto> { CrearItem(1), CrearItem(2) };
        var (vm, servicioMock, _, _, _, _, _) = Crear(items);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock, _, _, _, _, _) = Crear();

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
        var (vm, servicioMock, _, _, _, _, _) = Crear();

        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);

        await vm.BuscarCommand.ExecuteAsync(null);

        Assert.True(string.IsNullOrEmpty(vm.MensajeError));
        servicioMock.Verify(s => s.ObtenerMasMovidosAsync(
            It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()), Times.Once);
    }

    // ── Export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        // Literal, NO MasMovidosViewModel.ColumnasPdf: comparar la constante contra sí misma
        // sería tautológico. Estas son las 4 columnas que muestra la grilla, SIN ProductoId (ID
        // interno de Postgres, que no va al PDF).
        var esperado = new[]
        {
            new ColumnaPdf("Codigo", "Código"),
            new ColumnaPdf("Nombre", "Nombre"),
            new ColumnaPdf("CantidadMovimientos", "Movimientos"),
            new ColumnaPdf("VolumenTotal", "Volumen Total"),
        };

        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(),
                It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, pdfExporterMock, _, _) = Crear(new List<MasMovidoDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(CrearItem).ToList();
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
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
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("mas-movidos.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_LaDescripcionDeFiltrosIncluyeElPeriodoYElTopN()
    {
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        vm.FechaDesde = new DateTime(2026, 1, 1);
        vm.FechaHasta = new DateTime(2026, 1, 31);
        vm.TopN = 15;
        await vm.BuscarCommand.ExecuteAsync(null);
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<MasMovidoDto>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((_, _, m, _) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Contains("01/01/2026", metadatosCapturados!.DescripcionFiltros);
        Assert.Contains("31/01/2026", metadatosCapturados.DescripcionFiltros);
        Assert.Contains("15", metadatosCapturados.DescripcionFiltros);
    }

    /// <summary>
    /// Si el usuario no tocó los filtros de fecha (quedan en null) y dejó el TopN por defecto,
    /// el documento igual tiene que aclarar el universo de datos exportado -- un PDF que no dice
    /// "todo el histórico, top 20" no es auditable (spec 2026-09-18).
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_SinFiltrosDeFecha_LaDescripcionDiceTodoElHistoricoConTopNPorDefecto()
    {
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        MetadatosDocumento? metadatosCapturados = null;
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Callback<IEnumerable<MasMovidoDto>, IReadOnlyList<ColumnaPdf>, MetadatosDocumento, ResumenPdf?>((_, _, m, _) => metadatosCapturados = m)
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        Assert.NotNull(metadatosCapturados);
        Assert.Contains("Todo el histórico", metadatosCapturados!.DescripcionFiltros);
        Assert.Contains("20", metadatosCapturados.DescripcionFiltros);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaTituloYUsuarioEmisorConFallbackNombreCompleto()
    {
        var items = new List<MasMovidoDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, sessionMock) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "mas-movidos.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MasMovidoDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Productos más movidos" &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }

    // ── Fase 6: estados v2, consulta vigente, PDF de lo cargado y vuelta al hub ─────────────

    private static (MasMovidosViewModel vm, Mock<IReporteStockService> servicio, Mock<IPdfExporter> pdf, Mock<INavigationService> nav)
        Armar(bool conNavegacion = true)
    {
        var servicio = new Mock<IReporteStockService>();
        var pdf = new Mock<IPdfExporter>();
        var nav = new Mock<INavigationService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MasMovidoDto> { CrearItem(1) });
        var vm = new MasMovidosViewModel(
            servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), pdf.Object,
            Mock.Of<IServicioAperturaArchivo>(), session.Object, conNavegacion ? nav.Object : null);
        return (vm, servicio, pdf, nav);
    }

    [Fact]
    public async Task SinConexion_MarcaSinConexion_VaciaLasFilas_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        await vm.CargarAsync();
        Assert.Single(vm.Items);
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new ServidorNoDisponibleException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Un403_MarcaSinPermiso_YNoPropaga()
    {
        var (vm, servicio, _, _) = Armar();
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task ExportarPdf_SinFilas_EstaDeshabilitado()
    {
        var (vm, servicio, _, _) = Armar();
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MasMovidoDto>());
        await vm.CargarAsync();

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
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException());
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

        Assert.Equal("No hubo movimientos en el período", vm.TituloVacio);
        Assert.Equal("Probá con otro período.", vm.MensajeVacio);
    }

    [Fact]
    public async Task DosBusquedas_LaPrimeraTerminaTarde_NoPisaLaSegunda()
    {
        var (vm, servicio, _, _) = Armar();
        var primera = new TaskCompletionSource<IReadOnlyList<MasMovidoDto>>();
        var segunda = new TaskCompletionSource<IReadOnlyList<MasMovidoDto>>();
        servicio.SetupSequence(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .Returns(primera.Task).Returns(segunda.Task);

        var busquedaVieja = vm.CargarAsync();
        var busquedaNueva = vm.CargarAsync();
        var filasNuevas = new List<MasMovidoDto> { CrearItem(2) };
        segunda.SetResult(filasNuevas);
        await busquedaNueva;
        primera.SetResult(new List<MasMovidoDto> { CrearItem(1), CrearItem(3) });
        await busquedaVieja;

        Assert.Same(filasNuevas, vm.Items);
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
            It.IsAny<IEnumerable<MasMovidoDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros.Contains("01/09/2026") && !m.DescripcionFiltros.Contains("01/08/2026")),
            It.IsAny<ResumenPdf?>()), Times.Once);
    }

    [Fact]
    public async Task SinConexion_Reintentar_ConsultaConLosFiltrosDePantalla()
    {
        var (vm, servicio, _, _) = Armar();
        vm.TopN = 20;
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);
        servicio.Setup(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<MasMovidoDto> { CrearItem(1) });
        vm.TopN = 5;

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        servicio.Verify(s => s.ObtenerMasMovidosAsync(It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), 5), Times.Once);
    }

    [Fact]
    public void ExportarPdf_AlAsignarFilas_SeHabilitaYAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        vm.Items = new List<MasMovidoDto> { CrearItem() };

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }
}
