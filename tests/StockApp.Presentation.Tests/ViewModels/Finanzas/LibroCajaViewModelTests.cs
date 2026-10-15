using Avalonia.Collections;
using Moq;
using System.IO;
using System.Threading;
using StockApp.ApiClient;
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
        Crear(TimeProvider? reloj = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        var guardado = new Mock<IServicioGuardadoArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var pdfExporter = new Mock<IPdfExporter>();
        var apertura = new Mock<IServicioAperturaArchivo>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var vm = new LibroCajaViewModel(
            svc.Object, guardado.Object, confirm.Object, pdfExporter.Object, apertura.Object, session.Object, reloj);
        vm.EsperaDebounce = Timeout.InfiniteTimeSpan;
        return (vm, svc, guardado, confirm, pdfExporter, apertura, session);
    }

    private static MovimientoCajaDto Mov(int dia, decimal ingreso, decimal egreso, decimal corrido) =>
        new(new DateOnly(2026, 10, dia), ingreso > 0m ? "Ingreso" : "Egreso", $"Movimiento {dia}",
            null, null, null, null, ingreso, egreso, corrido);

    private static LibroCajaMesDto MesDe(int anio, int mes, decimal saldoInicial, decimal saldoFinal, params MovimientoCajaDto[] movimientos) =>
        new(anio, mes, saldoInicial, saldoFinal, movimientos, new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>());

    private static LibroCajaAnualDto AnioDe(int anio, params TotalMensualDto[] meses) =>
        new(anio, meses, new List<TotalPorClaveDto>());

    private static TotalMensualDto[] DoceMeses(params TotalMensualDto[] conMovimiento) =>
        Enumerable.Range(1, 12)
            .Select(m => conMovimiento.FirstOrDefault(t => t.Mes == m) ?? new TotalMensualDto(m, 0m, 0m, 0m))
            .ToArray();

    /// <summary>El modo año también consulta enero: su saldo inicial es el saldo inicial del año.</summary>
    private static void ConEnero(Mock<IFinanzasVistasService> svc, decimal saldoInicial = 0m) =>
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), 1))
            .ReturnsAsync((int anio, int _) => MesDe(anio, 1, saldoInicial, saldoInicial));

    private sealed class RelojFijo(DateTimeOffset ahora, TimeZoneInfo? zona = null) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
        public override TimeZoneInfo LocalTimeZone => zona ?? TimeZoneInfo.Utc;
    }

    private static readonly TimeProvider Octubre2026 = new RelojFijo(new DateTimeOffset(2026, 10, 6, 13, 0, 0, TimeSpan.Zero));

    [Fact]
    public void AnioYMesIniciales_SalenDelReloj_YNoConsulta()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public void MesInicial_SaleDeLaHoraLocal_NoDeLaUtc()
    {
        // 31/10 a las 22:00 en Uruguay (UTC-3) ya es 01/11 en UTC: el libro tiene que abrir octubre.
        var uruguay = TimeZoneInfo.CreateCustomTimeZone("UY-3", TimeSpan.FromHours(-3), "UY-3", "UY-3");
        var reloj = new RelojFijo(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero), uruguay);

        var (vm, _, _, _, _, _, _) = Crear(reloj);

        Assert.Equal(2026, vm.Anio);
        Assert.Equal(10, vm.Mes);
    }

    [Fact]
    public async Task CambiarMes_RecargaSoloConElMesNuevo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 0m));

        vm.Mes = 9;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Once);
        Assert.Equal(9, vm.LibroMes!.Mes);
    }

    [Fact]
    public async Task CambiarAnio_EnModoMes_RecargaEseMesDelAnioNuevo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2025, 10)).ReturnsAsync(MesDe(2025, 10, 0m, 0m));

        vm.Anio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2025, 10), Times.Once);
    }

    [Fact]
    public async Task PasarAAnioCompleto_RecargaElAnual()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.Zero;
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 1, 0m, 0m));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses()));

        vm.ModoSeleccionado = LibroCajaViewModel.ModoAnioCompleto;
        await vm._tareaDebounce;

        Assert.True(vm.VerAnioCompleto);
        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(2026), Times.Once);
        Assert.NotNull(vm.LibroAnual);
    }

    [Fact]
    public async Task CambiarAnio_EnAnioCompleto_RecargaElAnualDeEseAnio()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2025, 1, 0m, 0m));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2025, DoceMeses()));
        vm.VerAnioCompleto = true;
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.Anio = 2025;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(2025), Times.Once);
    }

    [Fact]
    public async Task CambiarMes_EnAnioCompleto_NoRecarga()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.VerAnioCompleto = true;
        vm._tareaDebounce = Task.CompletedTask;   // pasar a año completo dejó un debounce infinito pendiente: no es lo que se mide
        vm.EsperaDebounce = TimeSpan.Zero;

        vm.Mes = 3;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()), Times.Never);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiosRapidos_SoloConsultaElUltimo()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        vm.EsperaDebounce = TimeSpan.FromMilliseconds(50);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 8, 0m, 0m));

        vm.Mes = 9;
        vm.Mes = 8;
        await vm._tareaDebounce;

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Never);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 8), Times.Once);
    }

    [Fact]
    public void ModoSeleccionado_TraduceAVerAnioCompleto_YLoNotifica()
    {
        var (vm, _, _, _, _, _, _) = Crear(Octubre2026);
        var notificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        Assert.Equal(new[] { "Mes", "Año completo" }, LibroCajaViewModel.OpcionesModo);
        Assert.Equal(LibroCajaViewModel.ModoMes, vm.ModoSeleccionado);

        vm.VerAnioCompleto = true;

        Assert.Equal(LibroCajaViewModel.ModoAnioCompleto, vm.ModoSeleccionado);
        Assert.Contains(nameof(LibroCajaViewModel.ModoSeleccionado), notificadas);
    }

    [Fact]
    public async Task Reintentar_ConsultaElPeriodoQueEstaEnPantalla()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ThrowsAsync(new ServidorNoDisponibleException());
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 0m));
        await vm.CargarAsync();
        vm.Mes = 9;   // el debounce infinito deja el cambio en pantalla sin consultarlo

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 9), Times.Once);
        Assert.False(vm.SinConexion);
        Assert.Equal(9, vm.LibroMes!.Mes);
    }

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var pendiente = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(MesDe(2026, 10, 0m, 0m));
        await carga;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(LibroCajaViewModel.MensajeSinPermisoLibroCaja, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLoCargado()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(MesDe(2026, 10, 0m, 500m, Mov(1, 500m, 0m, 500m)))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Movimientos);
        Assert.Null(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
        Assert.False(vm.SinMovimientosEnElPeriodo);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(MesDe(2026, 10, 0m, 500m, Mov(1, 500m, 0m, 500m)));
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Single(vm.Movimientos);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var vieja = new TaskCompletionSource<LibroCajaMesDto>();
        var nueva = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).Returns(vieja.Task);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(nueva.Task);
        vm.Anio = 2026;
        vm.Mes = 9;

        var primera = vm.CargarAsync();
        vm.Mes = 10;
        var segunda = vm.CargarAsync();
        nueva.SetResult(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        await segunda;
        vieja.SetResult(MesDe(2026, 9, 0m, 99m, Mov(9, 99m, 0m, 99m)));
        await primera;

        Assert.Equal(10, vm.LibroMes!.Mes);
        Assert.Equal(new DateOnly(2026, 10, 2), Assert.Single(vm.Movimientos).Fecha);
    }

    [Fact]
    public async Task RespuestaMensualVieja_TrasPasarAAnio_NoPisaElAnual()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        ConEnero(svc);
        var mensual = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).Returns(mensual.Task);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        vm.Anio = 2026;
        vm.Mes = 10;

        var primera = vm.CargarAsync();
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();
        mensual.SetResult(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        await primera;

        Assert.NotNull(vm.LibroAnual);
        Assert.Null(vm.LibroMes);
        Assert.Empty(vm.Movimientos);
    }

    [Fact]
    public async Task RespuestaAnualVieja_TrasVolverAMes_NoPisaElMes()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        var anual = new TaskCompletionSource<LibroCajaAnualDto>();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).Returns(anual.Task);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 0m, 10m, Mov(2, 10m, 0m, 10m)));
        vm.Anio = 2026;
        vm.Mes = 10;
        vm.VerAnioCompleto = true;

        var primera = vm.CargarAsync();
        vm.VerAnioCompleto = false;
        await vm.CargarAsync();
        anual.SetResult(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        await primera;

        Assert.NotNull(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
        Assert.Single(vm.Movimientos);
    }

    [Fact]
    public async Task CargarAnual_TrasUnMes_QuitaLoDelMes()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(new LibroCajaMesDto(
            2026, 10, 0m, 10m, new List<MovimientoCajaDto> { Mov(2, 10m, 0m, 10m) },
            new List<TotalPorClaveDto> { new("Combustibles", 5m) }, new List<TotalPorClaveDto> { new("Rentas", 10m) }));
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 100m, 0m, 100m))));
        await vm.CargarAsync();

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        Assert.Null(vm.LibroMes);
        Assert.NotNull(vm.LibroAnual);
        Assert.Empty(vm.Movimientos);
        Assert.Empty(vm.TotalesPorRubro);
        Assert.Empty(vm.TotalesPorFuente);
    }

    [Fact]
    public void SinNadaCargado_NoEsSinMovimientos()
    {
        var (vm, _, _, _, _, _, _) = Crear();

        Assert.False(vm.SinMovimientosEnElPeriodo);
        Assert.Null(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
    }

    [Fact]
    public async Task Mes_SinMovimientos_EsSinMovimientosEnElPeriodo()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 300m, 300m));

        await vm.CargarAsync();

        Assert.True(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(300m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_DoceMesesEnCero_EsSinMovimientos()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        ConEnero(svc);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2026, DoceMeses()));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.Equal(12, vm.LibroAnual!.TotalesPorMes.Count);
        Assert.True(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(0, vm.MesesConMovimientos);
    }

    [Fact]
    public async Task Anual_SoloEgresosEnTodoElAnio_NoEsSinMovimientos()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        ConEnero(svc);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(3, 0m, 250m, -250m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.False(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(1, vm.MesesConMovimientos);
    }

    [Fact]
    public async Task Anual_MesSoloConEgresos_NoEsSinMovimientos_YCuentaComoMesConMovimientos()
    {
        var (vm, svc, _, _, _, _, _) = Crear();
        ConEnero(svc);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>())).ReturnsAsync(AnioDe(2026, DoceMeses(
            new TotalMensualDto(3, 0m, 250m, -250m), new TotalMensualDto(7, 1000m, 0m, 1000m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.False(vm.SinMovimientosEnElPeriodo);
        Assert.Equal(2, vm.MesesConMovimientos);
    }

    [Fact]
    public async Task Anual_SaldoInicialEsElDeEnero_YElFinalSumaLosNetos()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 812_430.55m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses(
            new TotalMensualDto(1, 1_250_000m, 980_200m, 269_800m),
            new TotalMensualDto(3, 1_250_000m, 1_322_750m, -72_750m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        svc.Verify(s => s.ObtenerLibroCajaMesAsync(2026, 1), Times.Once);
        Assert.Equal(812_430.55m, vm.SaldoInicial);
        Assert.Equal(1_009_480.55m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_SaldoFinalNegativo_SiLosNetosSuperanElInicial()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 50_000m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026))
            .ReturnsAsync(AnioDe(2026, DoceMeses(new TotalMensualDto(2, 0m, 80_000m, -80_000m))));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.Equal(-30_000m, vm.SaldoFinal);
    }

    [Fact]
    public async Task Anual_403EnLaConsultaDeEnero_MarcaSinPermiso()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses()));
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 1)).ThrowsAsync(new UnauthorizedAccessException("403"));
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
    }

    [Fact]
    public async Task Anual_RespuestaViejaDeEnero_TrasVolverAMes_NoPisaElMes()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        var enero = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(AnioDe(2026, DoceMeses()));
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 1)).Returns(enero.Task);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 7m, 17m, Mov(2, 10m, 0m, 17m)));
        vm.VerAnioCompleto = true;
        var anual = vm.CargarAsync();
        vm.VerAnioCompleto = false;
        await vm.CargarAsync();

        enero.SetResult(MesDe(2026, 1, 999m, 999m));
        await anual;

        Assert.NotNull(vm.LibroMes);
        Assert.Null(vm.LibroAnual);
        Assert.Equal(7m, vm.SaldoInicial);
        Assert.Equal(17m, vm.SaldoFinal);
    }

    [Fact]
    public async Task SinConexion_TrasUnMesCargado_DejaLosSaldosEnCero()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.SetupSequence(s => s.ObtenerLibroCajaMesAsync(2026, 10))
            .ReturnsAsync(MesDe(2026, 10, 100m, 150m, Mov(1, 50m, 0m, 150m)))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();
        Assert.Equal(150m, vm.SaldoFinal);

        await vm.CargarAsync();

        Assert.True(vm.SinConexion);
        Assert.Equal(0m, vm.SaldoInicial);
        Assert.Equal(0m, vm.SaldoFinal);
    }

    [Fact]
    public async Task CargarAnual_TrasUnMes_NoDejaLosSaldosDelMes_SiLaCargaAnualFalla()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 100m, 150m, Mov(1, 50m, 0m, 150m)));
        await vm.CargarAsync();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ThrowsAsync(new ServidorNoDisponibleException());
        vm.VerAnioCompleto = true;

        await vm.CargarAsync();

        Assert.Equal(0m, vm.SaldoInicial);
        Assert.Equal(0m, vm.SaldoFinal);
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
        ConEnero(svc);
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
        ConEnero(svc);
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

    // ── export PDF (spec 2026-09-18) ────────────────────────────────────────

    private static MovimientoCajaDto CrearMovimiento()
        => new(new DateOnly(2026, 7, 5), "Ingreso", "Partida", null, null, "Literal B", null, 500m, 0m, 500m);

    [Fact]
    public async Task ExportarPdfCommand_ConAnioCompletoSinMovimientos_NoExporta()
    {
        var (vm, svc, _, _, pdfExporterMock, _, _) = Crear();
        ConEnero(svc);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(2026, DoceMeses(), new List<TotalPorClaveDto>()));
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<FilaPdfMensual>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    private static LibroCajaAnualDto Anio2026ConDeficit() => new(2026,
        DoceMeses(
            new TotalMensualDto(1, 1_250_000m, 980_200m, 269_800m),
            new TotalMensualDto(3, 1_250_000m, 1_322_750m, -72_750m)),
        new List<TotalPorClaveDto> { new("Obras viales", 2_302_950m) });

    private static async Task<(LibroCajaViewModel vm, Mock<IFinanzasVistasService> svc, Mock<IServicioGuardadoArchivo> guardado,
        Mock<IPdfExporter> pdf)> AnualCargadoAsync(LibroCajaAnualDto anual)
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        ConEnero(svc, saldoInicial: 812_430.55m);
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(2026)).ReturnsAsync(anual);
        vm.VerAnioCompleto = true;
        await vm.CargarAsync();
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<FilaPdfMensual>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);
        return (vm, svc, guardado, pdf);
    }

    [Fact]
    public async Task ExportarPdf_Anual_CuatroColumnas_MesConNombre_TituloYNombreDelAnio()
    {
        var esperado = new[]
        {
            new ColumnaPdf("Mes", "Mes"), new ColumnaPdf("Ingresos", "Ingresos"),
            new ColumnaPdf("Egresos", "Egresos"), new ColumnaPdf("Neto", "Neto"),
        };
        var anual = Anio2026ConDeficit();
        var (vm, _, guardado, pdf) = await AnualCargadoAsync(anual);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(
            It.Is<IEnumerable<FilaPdfMensual>>(f => f.Count() == 12
                && f.First().Equals(new FilaPdfMensual("Enero", 1_250_000m, 980_200m, 269_800m))
                && f.ElementAt(2).Equals(new FilaPdfMensual("Marzo", 1_250_000m, 1_322_750m, -72_750m))
                && f.ElementAt(8).Mes == "Setiembre"),
            It.Is<IReadOnlyList<ColumnaPdf>>(c => c.SequenceEqual(esperado)),
            It.Is<MetadatosDocumento>(m => m.Titulo == "Libro caja" && m.DescripcionFiltros == "Año completo: 2026."),
            It.IsAny<ResumenPdf>()), Times.Once);
        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdf_Anual_ResumenConSaldosDelAnio_MesesConDeficit_YRubros_SinFuentes()
    {
        var (vm, _, _, pdf) = await AnualCargadoAsync(Anio2026ConDeficit());

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<FilaPdfMensual>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(),
            It.Is<ResumenPdf>(r =>
                r.Totales.Count == 2
                && r.Totales[0].Etiqueta == "Saldo inicial" && Equals(r.Totales[0].Valor, 812_430.55m)
                && r.Totales[1].Etiqueta == "Saldo final" && Equals(r.Totales[1].Valor, 1_009_480.55m)
                && r.Secciones.Count == 2
                && r.Secciones[0].Titulo == "Meses con déficit"
                && r.Secciones[0].Filas.Count == 1
                && r.Secciones[0].Filas[0].Etiqueta == "Marzo" && Equals(r.Secciones[0].Filas[0].Valor, -72_750m)
                && r.Secciones[1].Titulo == "Totales por rubro"
                && r.Secciones[1].Filas[0].Etiqueta == "Obras viales"
                && r.Secciones.All(s => s.Titulo != "Totales por fuente"))), Times.Once);
    }

    [Fact]
    public void ResumenAnual_SinDeficit_NoAgregaLaSeccion()
    {
        var anual = new LibroCajaAnualDto(2026, DoceMeses(new TotalMensualDto(1, 10m, 5m, 5m)), new List<TotalPorClaveDto>());

        var resumen = LibroCajaViewModel.ArmarResumenAnual(anual, 0m, 5m);

        Assert.Equal(new[] { "Totales por rubro" }, resumen.Secciones.Select(s => s.Titulo));
    }

    [Fact]
    public void Exportar_SinNadaCargado_NoSePuede()
    {
        var (vm, _, _, _, _, _, _) = Crear(Octubre2026);

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_MesSinMovimientos_NoSePuede()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 10m, 10m));

        await vm.CargarAsync();

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_AnioSinMovimientos_NoSePuede()
    {
        var (vm, _, _, _) = await AnualCargadoAsync(new LibroCajaAnualDto(2026, DoceMeses(), new List<TotalPorClaveDto>()));

        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_AnioConMovimientos_SePuede()
    {
        var (vm, _, _, _) = await AnualCargadoAsync(Anio2026ConDeficit());

        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        Assert.True(vm.PuedeExportar);
    }

    [Fact]
    public async Task Exportar_ConSinPermiso_NoSePuede()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.NotNull(vm.LibroMes);   // un 403 no limpia lo cargado: lo que bloquea es SinPermiso
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task Exportar_MientrasCarga_NoSePuede_YAlTerminarNotificaYVuelve()
    {
        var (vm, svc, _, _, _, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        var pendiente = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(pendiente.Task);
        var avisos = 0;
        vm.ExportarPdfCommand.CanExecuteChanged += (_, _) => avisos++;

        var carga = vm.CargarAsync();
        Assert.False(vm.ExportarPdfCommand.CanExecute(null));
        var avisosAlEmpezar = avisos;

        pendiente.SetResult(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await carga;

        Assert.True(avisosAlEmpezar > 0);
        Assert.True(avisos > avisosAlEmpezar);
        Assert.True(vm.ExportarPdfCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExportarPdf_ConMesCambiadoSinRecargar_ExportaElMesCargado()
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 9)).ReturnsAsync(MesDe(2026, 9, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        vm.Mes = 9;
        await vm.CargarAsync();
        vm.Mes = 10;   // el debounce infinito deja octubre en el filtro y setiembre en pantalla
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.Is<MetadatosDocumento>(m => m.DescripcionFiltros == "Mes: 09/2026."), It.IsAny<ResumenPdf>()), Times.Once);
        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-09.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
    }

    [Fact]
    public async Task ExportarPdf_RecienPasadoAAnioSinRecargar_ExportaElMesCargado()
    {
        var (vm, svc, guardado, _, pdf, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(2026, 10)).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        vm.VerAnioCompleto = true;   // sin recargar: en pantalla sigue octubre
        pdf.Setup(e => e.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>())).Returns(new byte[] { 1 });
        guardado.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>(), "pdf", "application/pdf"))
            .ReturnsAsync(true);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        guardado.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "libro-caja-2026-10.pdf", It.IsAny<CancellationToken>(),
            "pdf", "application/pdf"), Times.Once);
        pdf.Verify(e => e.Exportar(It.IsAny<IEnumerable<FilaPdfMensual>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(),
            It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
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
    public async Task ExportarPdfCommand_EjecutadoDirectoMientrasCarga_NoExporta()
    {
        var (vm, svc, _, _, pdfExporterMock, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        var pendiente = new TaskCompletionSource<LibroCajaMesDto>();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).Returns(pendiente.Task);
        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        await vm.ExportarPdfCommand.ExecuteAsync(null);   // ExecuteAsync directo no consulta CanExecute

        pendiente.SetResult(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await carga;
        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_EjecutadoDirectoConSinPermiso_NoExporta()
    {
        var (vm, svc, _, _, pdfExporterMock, _, _) = Crear(Octubre2026);
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(MesDe(2026, 10, 0m, 5m, Mov(1, 5m, 0m, 5m)));
        await vm.CargarAsync();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>())).ThrowsAsync(new UnauthorizedAccessException("403"));
        await vm.CargarAsync();
        Assert.True(vm.SinPermiso);
        Assert.NotNull(vm.LibroMes);

        await vm.ExportarPdfCommand.ExecuteAsync(null);

        pdfExporterMock.Verify(e => e.Exportar(
            It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<ColumnaPdf>>(), It.IsAny<MetadatosDocumento>(), It.IsAny<ResumenPdf>()), Times.Never);
    }

    [Fact]
    public async Task ExportarPdfCommand_ConMes_UsaLasDiezColumnasDeLaGrilla()
    {
        // Literal, NO LibroCajaViewModel.ColumnasPdf: comparar la constante contra sí misma sería
        // tautológico. Son 10 columnas -- dispara A4 apaisado (>6 columnas).
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
