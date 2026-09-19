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
        Mock<ICsvExporter> exporterMock,
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
        var exporterMock = new Mock<ICsvExporter>();
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
            servicioMock.Object, exporterMock.Object, guardadoMock.Object, confirmMock.Object,
            pdfExporterMock.Object, aperturaMock.Object, sessionMock.Object);
        return (vm, servicioMock, exporterMock, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, sessionMock);
    }

    // ── tests ──────────────────────────────────────────────────────────────

    // ── bugfix "pantalla muda ante un 403": CargarAsync no debe escalar un 403/401, y debe dejar
    // un estado bindeable para que la vista muestre EstadoVacio. ──
    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        var (vm, servicioMock, _, _, _, _, _, _) = Crear();
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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear(items, totales);

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
        var (vm, servicioMock, _, _, _, _, _, _) = Crear(items, totales);

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerValorizacionAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Same(items, vm.Items);
        Assert.NotNull(vm.Totales);
        Assert.Equal(100m, vm.Totales!.TotalValorCosto);
    }

    [Fact]
    public async Task ExportarCommand_LlamaExportarConOrdenColumnasFijo()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, exporterMock, guardadoMock, _, _, _, _) = Crear(items);

        var esperado = new[]
        {
            "ProductoId", "Codigo", "Nombre", "Categoria",
            "StockActual", "PrecioCosto", "ValorCosto"
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
        var (vm, _, exporterMock, guardadoMock, _, _, _, _) = Crear(new List<ValorizacionItemDto>());

        await vm.ExportarCommand.ExecuteAsync(null);

        exporterMock.Verify(
            e => e.Exportar(It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);
        guardadoMock.Verify(
            g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, exporterMock, guardadoMock, confirmMock, _, _, _) = Crear(items);
        exporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(),
                It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv-generado");
        guardadoMock
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.BuscarCommand.ExecuteAsync(null);

        // No debe propagar: AsyncRelayCommand no observa excepciones no capturadas.
        await vm.ExportarCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── Export PDF (spec 2026-09-18) ────────────────────────────────────────

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        // Literal, NO ValorizacionViewModel.ColumnasPdf: comparar la constante contra sí misma
        // sería tautológico (nunca se pondría rojo si alguien cambia el valor de la constante).
        // Estas son las 6 columnas que muestra la grilla, en su orden, SIN ProductoId (el CSV
        // tiene 7 e incluye el ID interno de Postgres, que no va al PDF).
        var esperado = new[] { "Codigo", "Nombre", "Categoria", "StockActual", "PrecioCosto", "ValorCosto" };

        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Items,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<ValorizacionItemDto>());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(CrearItem).ToList();
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<ValorizacionItemDto> { CrearItem(1) };
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
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
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
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
        var (vm, _, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
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
        var (vm, _, _, guardadoMock, _, pdfExporterMock, _, sessionMock) = Crear(items);
        await vm.BuscarCommand.ExecuteAsync(null);
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ValorizacionItemDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), "valorizacion.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ValorizacionItemDto>>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Valorización de inventario" &&
                m.DescripcionFiltros == "Sin filtros aplicados." &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }
}
