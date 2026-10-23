using Moq;
using StockApp.ApiClient;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class CalendarioPagosViewModelTests
{
    private static (CalendarioPagosViewModel vm, Mock<IFinanzasVistasService> svcMock,
                     Mock<IGastoService> gastoSvcMock, Mock<INavigationService> navMock)
        Crear(
            CalendarioPagosDto? calendario = null,
            RolUsuario rol = RolUsuario.Admin, IEnumerable<string>? permisos = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        var gastoSvc = new Mock<IGastoService>();

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var nav = new Mock<INavigationService>();

        var vm = new CalendarioPagosViewModel(svc.Object, gastoSvc.Object, session.Object, nav.Object);
        return (vm, svc, gastoSvc, nav);
    }

    [Fact]
    public async Task CargarAsync_PopulaLasCuatroSecciones()
    {
        var (vm, _, _, _) = Crear(new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto> { new(2, "Barraca Y", "A-2", 300m, new DateOnly(2026, 7, 20), "Pendiente") },
            new List<FacturaCalendarioDto> { new(3, "Barraca Z", "A-3", 200m, new DateOnly(2026, 8, 10), "Pendiente") },
            new List<PagoRecienteDto> { new(4, "Barraca W", "A-4", new DateOnly(2026, 7, 14), 100m) }));

        await vm.CargarAsync();

        Assert.Single(vm.Vencidas);
        Assert.Single(vm.AVencer7Dias);
        Assert.Single(vm.AVencer30Dias);
        Assert.Single(vm.PagosRecientes);
    }

    // ── bugfix 2026-08-15: red de contención — CargarAsync no debe escalar un 403 ──────────
    // Mismo criterio que GastosViewModel.CargarAsync (ec0696c): CargarAsync la dispara la View
    // (DataContextChanged) fire-and-forget — un UnauthorizedAccessException no atrapado escala
    // a Dispatcher.UIThread.UnhandledException (App.axaml.cs). Este método no tenía NINGÚN
    // try/catch.

    [Fact]
    public async Task CargarAsync_SiObtenerCalendarioPagosLanzaUnauthorized_NoPropagaLaExcepcion()
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ThrowsAsync(new UnauthorizedAccessException());
        var gastoSvc = new Mock<IGastoService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        var nav = new Mock<INavigationService>();

        var vm = new CalendarioPagosViewModel(svc.Object, gastoSvc.Object, session.Object, nav.Object);

        var excepcion = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(excepcion);
    }

    [Fact]
    public async Task RegistrarPago_ObtieneElGastoYNavegaAPagosGastoViewModel()
    {
        var (vm, _, gastoSvc, nav) = Crear(new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        await vm.CargarAsync();
        gastoSvc.Setup(g => g.ObtenerPorIdAsync(1)).ReturnsAsync(new Gasto { Id = 1 });

        await vm.RegistrarPagoCommand.ExecuteAsync(vm.Vencidas[0]);

        gastoSvc.Verify(g => g.ObtenerPorIdAsync(1), Times.Once);
        nav.Verify(n => n.Navegar(It.IsAny<Action<PagosGastoViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task RegistrarPago_ConfiguraVolverAlCalendario()
    {
        var (vm, _, gastoSvc, nav) = Crear(new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        await vm.CargarAsync();
        gastoSvc.Setup(g => g.ObtenerPorIdAsync(1)).ReturnsAsync(new Gasto { Id = 1 });

        var pagosNav = new Mock<INavigationService>();
        var pagosSvc = new Mock<IGastoService>();
        var pagosConfirm = new Mock<IConfirmacionService>();
        PagosGastoViewModel? pagosVm = null;

        nav.Setup(n => n.Navegar(It.IsAny<Action<PagosGastoViewModel>>()))
            .Callback<Action<PagosGastoViewModel>>(cb =>
            {
                var pagosAdjuntosPanel = new AdjuntosPanelViewModel(
                    new Mock<IAdjuntoService>().Object,
                    new Mock<IServicioSeleccionArchivo>().Object,
                    new Mock<IServicioAperturaArchivo>().Object,
                    pagosConfirm.Object,
                    new Mock<ICurrentSession>().Object);
                var pagosSession = new Mock<ICurrentSession>();
                pagosVm = new PagosGastoViewModel(pagosSvc.Object, new Mock<IFuenteFinanciamientoService>().Object, pagosSession.Object, pagosNav.Object, pagosConfirm.Object, pagosAdjuntosPanel);
                cb(pagosVm);
            });

        await vm.RegistrarPagoCommand.ExecuteAsync(vm.Vencidas[0]);

        Assert.NotNull(pagosVm);
        Assert.Equal("Calendario de pagos", pagosVm!.TextoOrigen);   // el breadcrumb dice de dónde se viene
        pagosVm!.VolverCommand.Execute(null);

        // ConfigurarVolver captura la INavigationService del CalendarioPagosViewModel (origen),
        // no la del propio PagosGastoViewModel — por eso se verifica en "nav", no en "pagosNav".
        nav.Verify(n => n.Navegar<CalendarioPagosViewModel>(), Times.Once);
        pagosNav.Verify(n => n.Navegar<GastosViewModel>(), Times.Never);
    }

    // ── PuedeRegistrarPagos: coherencia de navegación de los botones "Registrar pago" ──────
    // (2026-08-15). No es un gap de seguridad: PagosGastoView (destino) ya gatea la acción por
    // su cuenta. Esto es UX/consistencia — que el link no ofrezca algo inalcanzable.

    [Fact]
    public void Operador_ConVerFinanzasSinRegistrarPagos_PuedeRegistrarPagos_EsFalse()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas });

        Assert.False(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Operador_ConRegistrarPagos_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _) = Crear(
            rol: RolUsuario.Operador, permisos: new[] { Permisos.VerFinanzas, Permisos.RegistrarPagos });

        Assert.True(vm.PuedeRegistrarPagos);
    }

    [Fact]
    public void Admin_PuedeRegistrarPagos_EsTrue()
    {
        var (vm, _, _, _) = Crear(rol: RolUsuario.Admin, permisos: Array.Empty<string>());

        Assert.True(vm.PuedeRegistrarPagos);
    }

    private static CalendarioPagosDto Calendario(int vencidas) => new(
        Enumerable.Range(1, vencidas).Select(i => new FacturaCalendarioDto(i, $"Proveedor {i}", $"A-{i:0000}", 100m * i,
            new DateOnly(2026, 9, 28), "Pendiente")).ToList(),
        new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>());

    [Fact]
    public async Task CargarAsync_MientrasEspera_CargandoEsTrue_YAlTerminarFalse()
    {
        var (vm, svc, _, _) = Crear();
        var pendiente = new TaskCompletionSource<CalendarioPagosDto>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).Returns(pendiente.Task);

        var carga = vm.CargarAsync();
        Assert.True(vm.Cargando);

        pendiente.SetResult(Calendario(2));
        await carga;

        Assert.False(vm.Cargando);
        Assert.Equal(2, vm.Vencidas.Count);
    }

    [Fact]
    public async Task CargarAsync_403_MarcaSinPermisoConSuMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ThrowsAsync(new UnauthorizedAccessException("403"));

        await vm.CargarAsync();

        Assert.True(vm.SinPermiso);
        Assert.Equal(CalendarioPagosViewModel.MensajeSinPermisoCalendario, vm.MensajeSinPermiso);
    }

    [Fact]
    public async Task ServidorCaido_TrasUnaCargaBuena_MarcaSinConexionYVaciaLasSecciones()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null))
            .ReturnsAsync(Calendario(2))
            .ThrowsAsync(new ServidorNoDisponibleException());
        await vm.CargarAsync();

        await vm.RecargarCommand.ExecuteAsync(null);

        Assert.True(vm.SinConexion);
        Assert.Empty(vm.Vencidas);
        Assert.Empty(vm.AVencer7Dias);
        Assert.Empty(vm.AVencer30Dias);
        Assert.Empty(vm.PagosRecientes);
    }

    [Fact]
    public async Task Reintentar_TrasUnaCaida_VuelveACargar()
    {
        var (vm, svc, _, _) = Crear();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null))
            .ThrowsAsync(new ServidorNoDisponibleException())
            .ReturnsAsync(Calendario(3));
        await vm.CargarAsync();
        Assert.True(vm.SinConexion);

        await vm.ReintentarCargaCommand.ExecuteAsync(null);

        Assert.False(vm.SinConexion);
        Assert.Equal(3, vm.Vencidas.Count);
    }

    [Fact]
    public async Task ConsultaVieja_QueTerminaDespues_NoPisaLaNueva()
    {
        var (vm, svc, _, _) = Crear();
        var vieja = new TaskCompletionSource<CalendarioPagosDto>();
        var nueva = new TaskCompletionSource<CalendarioPagosDto>();
        svc.SetupSequence(s => s.ObtenerCalendarioPagosAsync(null)).Returns(vieja.Task).Returns(nueva.Task);

        var primera = vm.CargarAsync();
        var segunda = vm.CargarAsync();
        nueva.SetResult(Calendario(3));
        await segunda;
        vieja.SetResult(Calendario(1));
        await primera;

        Assert.Equal(3, vm.Vencidas.Count);
    }
}
