using Avalonia.Collections;
using Moq;
using System.IO;
using System.Linq;
using System.Threading;
using StockApp.ApiClient;
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
        Crear(IReadOnlyList<ControlPoaLineaDto>? lineas = null, TimeProvider? reloj = null)
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
            pdfExporter.Object, apertura.Object, session.Object, reloj);
        return (vm, svc, nav, csv, guardado, confirm, pdfExporter, apertura, session);
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }

    private static readonly TimeProvider Octubre2026 = new RelojFijo(new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.Zero));

    private static ControlPoaLineaDto Linea(int id, int ejercicio) =>
        new(id, $"Línea {id}", "Obras", ejercicio, 1000m, 400m, 600m, 40m, false);

    [Fact]
    public void EjercicioInicial_SaleDelReloj_YNoConsulta()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);

        Assert.Equal(2026, vm.Ejercicio);
        svc.Verify(s => s.ObtenerControlPoaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarEjercicio_RecargaSoloConElEjercicioNuevo()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2025) });

        vm.Ejercicio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
    }

    [Fact]
    public async Task CambiosRapidos_SoloConsultaElUltimoEjercicio()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);

        vm.Ejercicio = 2024;
        vm.Ejercicio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerControlPoaAsync(2024), Times.Never);
        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        var vieja = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        var nueva = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(vieja.Task);
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).Returns(nueva.Task);
        vm.EsperaDebounce = TimeSpan.Zero;

        var primera = vm.CargarAsync();
        vm.Ejercicio = 2025;
        nueva.SetResult(new List<ControlPoaLineaDto> { Linea(2, 2025) });
        await vm._tareaDebounce;
        vieja.SetResult(new List<ControlPoaLineaDto> { Linea(1, 2026) });
        await primera;

        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
    }

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        var pendiente = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);
        pendiente.SetResult(new List<ControlPoaLineaDto>());
        await carga;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(ControlPoaViewModel.MensajeSinPermisoControlPoa, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_MarcaSinConexionYVaciaLasFilas()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.SetupSequence(s => s.ObtenerControlPoaAsync(2026))
            .ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2026) })
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Filas);
    }

    [Fact]
    public async Task Reintentar_ConsultaElEjercicioQueEstaEnPantalla()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(reloj: Octubre2026);
        svc.Setup(s => s.ObtenerControlPoaAsync(2026)).ThrowsAsync(new ServidorNoDisponibleException());
        svc.Setup(s => s.ObtenerControlPoaAsync(2025)).ReturnsAsync(new List<ControlPoaLineaDto> { Linea(1, 2025) });
        await vm.CargarAsync();
        vm.EsperaDebounce = Timeout.InfiniteTimeSpan;   // el cambio queda en pantalla sin que el debounce lo consulte
        vm.Ejercicio = 2025;

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        svc.Verify(s => s.ObtenerControlPoaAsync(2025), Times.Once);
        Assert.False(vm.SinConexion);
        Assert.Equal(2025, Assert.Single(vm.Filas).Ejercicio);
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
        var esperado = new[]
        {
            new ColumnaPdf("Nombre", "Línea"),
            new ColumnaPdf("Programa", "Programa"),
            new ColumnaPdf("Presupuesto", "Presupuesto"),
            new ColumnaPdf("Gastado", "Gastado"),
            new ColumnaPdf("Saldo", "Saldo"),
            new ColumnaPdf("PorcentajeEjecucion", "% Ejecución"),
        };

        var items = new List<ControlPoaLineaDto> { CrearLinea(1) };
        var (vm, _, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(items);
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(),
                It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Filas,
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado)),
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
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
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
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()),
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
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
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
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
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
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
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
                It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(
                It.IsAny<Stream>(), $"control-poa-{vm.Ejercicio}.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<ControlPoaLineaDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Control POA" &&
                m.DescripcionFiltros == "Ejercicio: 2025." &&
                m.UsuarioEmisor == "Juan Pérez")),
            Times.Once);
    }

    // ── Exportes: 100% cliente, serializan Filas; no pueden salir con filas de otra consulta ──

    [Fact]
    public async Task Exportar_ConSinPermiso_NoSePuedeEjecutar()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(new List<ControlPoaLineaDto> { CrearLinea(1) });
        await vm.CargarAsync();
        Assert.True(vm.ExportarCsvCommand.CanExecute(null));
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));

        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException("403"));
        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.NotEmpty(vm.Filas);
        Assert.False(vm.ExportarCsvCommand.CanExecute(null));
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_MientrasCarga_NoSePuedeEjecutar_YVuelveAlTerminar()
    {
        var (vm, svc, _, _, _, _, _, _, _) = Crear(new List<ControlPoaLineaDto> { CrearLinea(1) });
        await vm.CargarAsync();
        var pendiente = new TaskCompletionSource<IReadOnlyList<ControlPoaLineaDto>>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).Returns(pendiente.Task);

        var carga = vm.CargarAsync();

        Assert.True(vm.Cargando);
        Assert.False(vm.ExportarCsvCommand.CanExecute(null));
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));

        pendiente.SetResult(new List<ControlPoaLineaDto> { CrearLinea(2) });
        await carga;

        Assert.True(vm.ExportarCsvCommand.CanExecute(null));
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_TrasCambiarElEjercicioSinRecargar_RotulaConElEjercicioDeLasFilasCargadas()
    {
        var (vm, svc, _, _, guardadoMock, _, pdfExporterMock, _, _) = Crear(new List<ControlPoaLineaDto> { CrearLinea(1) });
        vm.EsperaDebounce = TimeSpan.FromMinutes(10);
        vm.Ejercicio = 2025;
        await vm.CargarAsync();
        var cargado = vm.Ejercicio;
        vm.Ejercicio = cargado - 1; // el debounce todavía no recargó: en pantalla siguen las filas de `cargado`
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>()))
            .Returns(new byte[] { 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);
        await vm.ExportarCsvCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros == $"Ejercicio: {cargado}.")), Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), $"control-poa-{cargado}.pdf",
            It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
        guardadoMock.Verify(g => g.GuardarTextoAsync(It.IsAny<string>(), $"control-poa-{cargado}.csv"), Times.Once);
    }
}
