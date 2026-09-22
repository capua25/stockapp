using Avalonia.Collections;
using Moq;
using System.IO;
using System.Threading;
using StockApp.Application.Auth;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class LibroCajaViewModelTests
{
    private static (
        LibroCajaViewModel vm,
        Mock<IFinanzasVistasService> svcMock,
        Mock<IServicioGuardadoArchivo> guardadoMock,
        Mock<IConfirmacionService> confirmMock,
        Mock<IPdfExporter> pdfExporterMock,
        Mock<IServicioAperturaArchivo> aperturaMock,
        Mock<ICurrentSession> sessionMock)
        Crear()
    {
        var svc = new Mock<IFinanzasVistasService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new LibroCajaViewModel(
            svc.Object, csv.Object, guardado.Object, confirm.Object, pdfExporter.Object, apertura.Object, session.Object);
        return (vm, svc, guardado, confirm, pdfExporter, apertura, session);
    }

    [Fact]
    public async Task CargarAsync_PorDefecto_PideElMesActual()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 100m, 100m,
                new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.Equal(100m, vm.SaldoInicial);
        Assert.Equal(100m, vm.SaldoFinal);
        Assert.Empty(vm.Movimientos);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_ConMovimientos_PopulaLaGrilla()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m,
                new List<MovimientoCajaDto>
                {
                    new(new DateOnly(2026, 7, 5), "Ingreso", "Partida", null, null, "Literal B", null, 500m, 0m, 500m),
                },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        var fila = Assert.Single(vm.Movimientos);
        Assert.Equal("Ingreso", fila.Tipo);
        Assert.Equal(500m, fila.SaldoCorrido);
    }

    // ── bugfix 2026-08-15: red de contención — CargarAsync no debe escalar un 403 ──────────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): CargarAsync la dispara la View
    // (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado escala
    // a Dispatcher.UIThread.UnhandledException (App.axaml.cs). Este método no tenía NINGÚN
    // try/catch.

    [Fact]
    public async Task CargarAsync_SiObtenerLibroCajaMesLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var excepcion = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task VerAnioCompleto_True_PideLibroCajaAnual()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(2026, new List<TotalMensualDto>(), new List<TotalPorClaveDto>()));

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()), Times.Once);
        Assert.NotNull(vm.LibroAnual);
    }

    [Fact]
    public async Task VerAnioCompleto_ExponeTotalesPorRubroDelAnio()
    {
        // spec §7.3: el toggle "Año completo" muestra "totales por mes y por rubro, sin gráficos".
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(
                2026,
                new List<TotalMensualDto> { new(7, 1000m, 400m, 600m) },
                new List<TotalPorClaveDto> { new("Combustibles", 250m) }));

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        var rubro = Assert.Single(vm.LibroAnual!.TotalesPorRubro);
        Assert.Equal("Combustibles", rubro.Clave);
        Assert.Equal(250m, rubro.Total);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.MovimientosView);
        Assert.True(vm.MovimientosView.CanSort);
    }

    [Fact]
    public async Task ExportarCsvAsync_LlamaAlExportadorYAlGuardado()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        Assert.True(true); // el mock no lanza: cubre el camino feliz de Exportar + GuardarTextoAsync
    }

    // ── bugfix 2026-08-14: falla silenciosa al guardar el CSV ──────────────────

    [Fact]
    public async Task ExportarCsvCommand_SiFallaGuardarTextoAsync_InformaYNoPropagaLaExcepcion()
    {
        var (vm, svc, guardado, confirm, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        guardado
            .Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    // ── export PDF (spec 2026-09-18) ────────────────────────────────────────

    private static MovimientoCajaDto CrearMovimiento()
        => new(new DateOnly(2026, 7, 5), "Ingreso", "Partida", null, null, "Literal B", null, 500m, 0m, 500m);

    [Fact]
    public async Task ExportarPdfCommand_ConAnioCompleto_NoExporta()
    {
        var (vm, svc, _, _, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(2026, new List<TotalMensualDto>(), new List<TotalPorClaveDto>()));
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SinItems_NoExporta()
    {
        var (vm, svc, _, _, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMes_UsaLasDiezColumnasDeLaGrilla()
    {
        // Literal, NO LibroCajaViewModel.ColumnasPdf: comparar la constante contra sí misma sería
        // tautológico. CSV y grilla coinciden (10 columnas) -- dispara A4 apaisado (>6 columnas).
        var esperado = new[]
        {
            new ColumnaPdf("Fecha", "Fecha"),
            new ColumnaPdf("Tipo", "Tipo"),
            new ColumnaPdf("Concepto", "Concepto"),
            new ColumnaPdf("ProveedorNombre", "Proveedor"),
            new ColumnaPdf("NumeroFactura", "Factura"),
            new ColumnaPdf("FuenteNombre", "Fuente"),
            new ColumnaPdf("RubroNombre", "Rubro"),
            new ColumnaPdf("Ingreso", "Ingreso"),
            new ColumnaPdf("Egreso", "Egreso"),
            new ColumnaPdf("SaldoCorrido", "Saldo corrido"),
        };

        var (vm, svc, guardadoMock, _, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m,
                new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        vm.Anio = 2026;
        vm.Mes = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            vm.Movimientos,
            It.Is<IReadOnlyList<ColumnaPdf>>(cols => cols.SequenceEqual(esperado) && cols.Count == 10),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()),
            Times.Once);
        guardadoMock.Verify(g => g.GuardarBytesAsync(
            It.IsAny<Stream>(), "libro-caja-2026-07.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMuchasFilas_PreguntaAntesDeExportar()
    {
        var items = Enumerable.Range(1, 600).Select(_ => CrearMovimiento()).ToList();
        var (vm, svc, _, confirmMock, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(2026, 7, 0m, 0m, items, new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_SiFallaGuardarBytesAsync_InformaYNoPropagaLaExcepcion()
    {
        var (vm, svc, guardadoMock, confirmMock, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m, new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new IOException("disco lleno"));

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync("No se pudo guardar el archivo. disco lleno"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoExitoso_OfreceAbrirElArchivo()
    {
        var (vm, svc, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m, new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        vm.Anio = 2026;
        vm.Mes = 7;
        await vm.CargarAsync();
        var pdfBytes = new byte[] { 9, 9 };
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(pdfBytes);
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-07.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("PDF guardado. ¿Desea abrirlo ahora?"), Times.Once);
        aperturaMock.Verify(a => a.AbrirAsync("libro-caja-2026-07.pdf", pdfBytes), Times.Once);
    }

    [Fact]
    public async Task ExportarPdfCommand_GuardadoCancelado_NoOfreceAbrir()
    {
        var (vm, svc, guardadoMock, confirmMock, pdfExporterMock, aperturaMock, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m, new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        vm.Anio = 2026;
        vm.Mes = 7;
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-07.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(false);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        aperturaMock.Verify(a => a.AbrirAsync(It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_PasaMetadatosConTituloFiltrosYUsuarioEmisor()
    {
        var (vm, svc, guardadoMock, _, pdfExporterMock, _, sessionMock) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m, new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        vm.Anio = 2026;
        vm.Mes = 7;
        await vm.CargarAsync();
        sessionMock.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "jperez", RolUsuario.Admin, "Juan Pérez"));
        pdfExporterMock
            .Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 9, 9 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-07.pdf", It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m =>
                m.Titulo == "Libro caja" &&
                m.DescripcionFiltros == "Mes: 07/2026." &&
                m.UsuarioEmisor == "Juan Pérez"),
            It.IsAny<ResumenPdf>()),
            Times.Once);
    }

    // ── Resumen de totales (spec de totales/resumen, 2026-09-22) ───────────────────────────

    /// <summary>
    /// Guardián del resumen: antes de esta spec el PDF de Libro Caja exportaba
    /// <c>Movimientos</c> pelado y los cuatro agregados de la pantalla (saldo inicial, saldo
    /// final, totales por rubro y por fuente) se perdían en el papel (bug de la auditoría). Se
    /// verifica el ARGUMENTO REAL del resumen (no <c>It.IsAny&lt;ResumenPdf&gt;()</c>).
    /// </summary>
    [Fact]
    public async Task ExportarPdfCommand_PasaElResumenConSaldosYTotalesPorRubroYFuente()
    {
        var (vm, svc, guardadoMock, _, pdfExporterMock, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 100m, 400m,
                new List<MovimientoCajaDto> { CrearMovimiento() },
                new List<TotalPorClaveDto> { new("Combustibles", 250m) },
                new List<TotalPorClaveDto> { new("Rentas Generales", 900m) }));
        await vm.CargarAsync();
        pdfExporterMock
            .Setup(e => e.Exportar(
                It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
                It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()))
            .Returns(new byte[] { 1 });
        guardadoMock
            .Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(),
            It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(),
            It.Is<ResumenPdf>(r =>
                r.Totales.Count == 2 &&
                r.Totales[0].Etiqueta == "Saldo inicial" && Equals(r.Totales[0].Valor, 100m) &&
                r.Totales[1].Etiqueta == "Saldo final" && Equals(r.Totales[1].Valor, 400m) &&
                r.Secciones.Count == 2 &&
                r.Secciones[0].Titulo == "Totales por rubro" &&
                r.Secciones[0].Filas.Count == 1 &&
                r.Secciones[0].Filas[0].Etiqueta == "Combustibles" &&
                Equals(r.Secciones[0].Filas[0].Valor, 250m) &&
                r.Secciones[1].Titulo == "Totales por fuente" &&
                r.Secciones[1].Filas.Count == 1 &&
                r.Secciones[1].Filas[0].Etiqueta == "Rentas Generales" &&
                Equals(r.Secciones[1].Filas[0].Valor, 900m))),
            Times.Once);
    }
}
