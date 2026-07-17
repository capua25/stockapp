using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class CalendarioPagosViewModelTests
{
    private static (CalendarioPagosViewModel vm, Mock<IFinanzasVistasService> svcMock,
                     Mock<IGastoService> gastoSvcMock, Mock<INavigationService> navMock)
        Crear(CalendarioPagosDto? calendario = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        var gastoSvc = new Mock<IGastoService>();
        var nav = new Mock<INavigationService>();

        var vm = new CalendarioPagosViewModel(svc.Object, gastoSvc.Object, nav.Object);
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
}
