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
            ValorCosto: 50m);

    private static (
        ValorizacionViewModel vm,
        Mock<IReporteStockService> servicioMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(
            IReadOnlyList<ValorizacionItemDto>? items = null,
            ValorizacionTotalesDto? totales = null)
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
            .Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(
                items ?? new List<ValorizacionItemDto>(),
                totales ?? new ValorizacionTotalesDto(0m)));

        var vm = new ValorizacionViewModel(
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
        servicioMock.Setup(s => s.ObtenerValorizacionAsync()).ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task BuscarCommand_LlamaObtenerValorizacionAsync_YPopulaItems()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1), CrearItem(2) };
        var totales = new ValorizacionTotalesDto(TotalValorCosto: 100m);
        var (vm, servicioMock, _, _, _, _, _) = Crear(items, totales);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
        Assert.NotNull(vm.Totales);
        Assert.Equal(100m, vm.Totales!.TotalValorCosto);
    }

    [Fact]
    public async Task CargarAsync_LlamaObtenerValorizacion_YPopulaItemsYTotales()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1), CrearItem(2) };
        var totales = new ValorizacionTotalesDto(TotalValorCosto: 100m);
        var (vm, servicioMock, _, _, _, _, _) = Crear(items, totales);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
        Assert.NotNull(vm.Totales);
        Assert.Equal(100m, vm.Totales!.TotalValorCosto);
    }

    // ── Export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        // Literal, NO ValorizacionViewModel.ColumnasPdf: comparar la constante contra sí misma
        // sería tautológico (nunca se pondría rojo si alguien cambia el valor de la constante).
        // Estas son las 6 columnas que muestra la grilla, en su orden, SIN ProductoId (ID
        // interno de Postgres, que no va al PDF).
        var esperado = new[]
        {
            new ColumnaPdf("Codigo", "Código"),
            new ColumnaPdf("Nombre", "Nombre"),
            new ColumnaPdf("Categoria", "Categoría"),
            new ColumnaPdf("StockActual", "Stock"),
            new ColumnaPdf("PrecioCosto", "P. Costo"),
            new ColumnaPdf("ValorCosto", "Valor Costo"),
        };

        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(),
                It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, pdfExporterMock, _, _) = Crear(new List<ValorizacionItemDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
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
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
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
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("valorizacion.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloFiltrosYUsuarioEmisor()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, guardadoMock, _, pdfExporterMock, _, sessionMock) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Valorización de inventario" &&
                m.DescripcionFiltros == "Sin filtros aplicados." &&
                m.UsuarioEmisor == "Juan Pérez"),
            It.IsAny<ResumenPdf>()),
            Times.Once);
    }

    // ── Resumen de totales (spec de totales/resumen, 2026-09-22) ───────────────────────────

    /// <summary>
    /// Guardián del resumen: antes de esta spec el PDF de Valorización mostraba
    /// <see cref="ValorizacionViewModel.Totales"/> en pantalla pero exportaba <c>Items</c> pelado
    /// (bug de la auditoría) -- el total de la grilla se perdía en el papel. Se verifica el
    /// ARGUMENTO REAL que recibe el exportador (no <c>It.IsAny&lt;ResumenPdf&gt;()</c>, que
    /// aceptaría cualquier cosa o directamente <c>null</c> y no probaría nada).
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_PasaElResumenConElTotalValorCosto()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var totales = new ValorizacionTotalesDto(TotalValorCosto: 12345.67m);
        var (vm, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items, totales);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(),
            It.Is<ResumenPdf>(r =>
                r.Totales.Count == 1 &&
                r.Totales[0].Etiqueta == "Total Valor Costo" &&
                Equals(r.Totales[0].Valor, 12345.67m) &&
                r.Secciones.Count == 0)),
            Times.Once);
    }

    // ── Fase 6: estados v2, consulta vigente y vuelta al hub ───────────────────────────────

    private static (ValorizacionViewModel vm, Mock<IReporteStockService> servicio, Mock<IPdfExporter> pdf, Mock<INavigationService> nav)
        Armar(bool conNavegacion = true)
    {
        var servicio = new Mock<IReporteStockService>();
        var pdf = new Mock<IPdfExporter>();
        var nav = new Mock<INavigationService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        servicio.Setup(s => s.ObtenerValorizacionAsync())
            .ReturnsAsync(new ValorizacionReporteDto(new List<ValorizacionItemDto> { CrearItem(1) }, new ValorizacionTotalesDto(50m)));
        var vm = new ValorizacionViewModel(
            servicio.Object, Mock.Of<IServicioGuardadoArchivo>(), Mock.Of<IConfirmacionService>(), pdf.Object,
            Mock.Of<IServicioAperturaArchivo>(), session.Object, conNavegacion ? nav.Object : null);
        return (vm, servicio, pdf, nav);
    }

    private static void PonerFalla(Mock<IReporteStockService> servicio, Exception ex)
        => servicio.Setup(s => s.ObtenerValorizacionAsync()).ThrowsAsync(ex);

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
        Assert.Null(vm.Totales);
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
        vm.Items = new List<ValorizacionItemDto>();
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

        Assert.Equal("No hay productos para valorizar", vm.TituloVacio);
        Assert.Equal("Cuando haya productos activos, su valor va a aparecer acá.", vm.MensajeVacio);
    }

    [Fact]
    public async Task DosActualizaciones_LaPrimeraTerminaTarde_NoPisaLaSegunda()
    {
        var (vm, servicio, _, _) = Armar();
        var primera = new TaskCompletionSource<ValorizacionReporteDto>();
        var segunda = new TaskCompletionSource<ValorizacionReporteDto>();
        servicio.SetupSequence(s => s.ObtenerValorizacionAsync()).Returns(primera.Task).Returns(segunda.Task);

        var vieja = vm.CargarAsync();
        var nueva = vm.CargarAsync();
        var filasNuevas = new List<ValorizacionItemDto> { CrearItem(2) };
        segunda.SetResult(new ValorizacionReporteDto(filasNuevas, new ValorizacionTotalesDto(1m)));
        await nueva;
        primera.SetResult(new ValorizacionReporteDto(new List<ValorizacionItemDto> { CrearItem(1) }, new ValorizacionTotalesDto(9m)));
        await vieja;

        Assert.Same(filasNuevas, vm.Items);
        Assert.Equal(1m, vm.Totales!.TotalValorCosto);
    }

    [Fact]
    public void ExportarPdf_AlAsignarFilas_SeHabilitaYAvisaElCambio()
    {
        var (vm, _, _, _) = Armar();
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        vm.Items = new List<ValorizacionItemDto> { CrearItem() };

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(avisos > 0);
    }
}
