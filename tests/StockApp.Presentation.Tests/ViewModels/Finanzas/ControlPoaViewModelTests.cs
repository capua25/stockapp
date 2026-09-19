using Avalonia.Collections;
using Moq;
using System.IO;
using System.Linq;
using System.Threading;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ControlPoaViewModelTests
{
    private static (
        ControlPoaViewModel vm,
        Mock<IFinanzasVistasService> svcMock,
        Mock<INavigationService> navMock,
        Mock<ICsvExporter> csvMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear(IReadOnlyList<ControlPoaLineaDto>? lineas = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ReturnsAsync(lineas ?? new List<ControlPoaLineaDto>());
        var nav = new Mock<INavigationService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new ControlPoaViewModel(
            svc.Object, nav.Object, csv.Object, guardado.Object, confirm.Object,
            pdfExporter.Object, apertura.Object, session.Object);
        return (vm, svc, nav, csv, guardado, confirm, pdfExporter, apertura, session);
    }

    [Fact]
    public async Task CargarAsync_PopulaLasFilas()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
            new(2, "Prensa", "Comunicación", 2026, 1000m, 8915m, -7915m, 891.5m, true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.True(vm.Filas[1].Sobregirada);
    }

    // ── bugfix 2026-08-15: red de contención — CargarAsync no debe escalar un 403 ──────────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): CargarAsync la dispara la View
    // (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado escala
    // a Dispatcher.UIThread.UnhandledException (App.axaml.cs). Este método no tenía NINGÚN
    // try/catch.

    [Fact]
    public async Task CargarAsync_SiObtenerControlPoaLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException());
        var nav = new Mock<INavigationService>();
        var csv = new Mock<ICsvExporter>();
        var guardado = new Mock<IServicioGuardadoArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();

        var vm = new ControlPoaViewModel(
            svc.Object, nav.Object, csv.Object, guardado.Object, confirm.Object,
            pdfExporter.Object, apertura.Object, session.Object);

        var excepcion = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, _, _, _, _, _, _, _, _) = Crear();

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.FilasView);
        Assert.True(vm.FilasView.CanSort);
    }

    [Fact]
    public async Task AbrirGastosDeLaLinea_NavegaAGastosViewModelFiltrado()
    {
        var (vm, _, nav, _, _, _, _, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
        });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        vm.AbrirGastosDeLaLineaCommand.Execute(null);

        nav.Verify(n => n.Navegar(It.IsAny<Action<GastosViewModel>>()), Times.Once);
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCsvCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var (vm, _, _, _, guardado, confirm, _, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
        });
        guardado
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── Export PDF (spec 2026-09-18) ────────────────────────────────────────

    private static ControlPoaLineaDto CrearLinea(int id = 1)
        => new(id, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false);

    [Fact]
    public async Task ExportarPdfCommand_LlamaAlExportadorConLasColumnasDeLaGrilla()
    {
        // Literal, NO ControlPoaViewModel.ColumnasPdf: comparar la constante contra sí misma
        // sería tautológico. Estas son las 6 columnas que muestra la grilla, SIN Ejercicio ni
        // Sobregirada (el CSV tiene 8 e incluye ambas).
        var esperado = new[] { "Nombre", "Programa", "Presupuesto", "Gastado", "Saldo", "PorcentajeEjecucion" };

        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Filas,
            It.Is<IReadOnlyList<string>>(cols => cols.SequenceEqual(esperado)),
            It.IsAny<MetadatosDocumento>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinFilas_NoExporta()
    {
        var (vm, _, _, _, _, _, pdfExporterMock, _, _) = Crear(new List<ControlPoaLineaDto>());
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(CrearLinea).ToList();
        var (vm, _, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()),
            Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
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
        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.CargarAsync();
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync($"control-poa-{vm.Ejercicio}.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear(items);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloEjercicioYUsuarioEmisor()
    {
        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, _, pdfExporterMock, _, sessionMock) = Crear(items);
        vm.Ejercicio = 2025;
        await vm.CargarAsync();
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(),
            It.IsAny<IReadOnlyList<string>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Control POA" &&
                m.DescripcionFiltros == "Ejercicio: 2025." &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }
}
