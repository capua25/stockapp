using Moq;
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
                pagosVm = new PagosGastoViewModel(pagosSvc.Object, pagosNav.Object, pagosConfirm.Object, pagosAdjuntosPanel);
                cb(pagosVm);
            });

        await vm.RegistrarPagoCommand.ExecuteAsync(vm.Vencidas[0]);

        Assert.NotNull(pagosVm);
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
}
