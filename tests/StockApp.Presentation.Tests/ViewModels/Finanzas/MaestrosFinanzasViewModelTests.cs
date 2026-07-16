using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class MaestrosFinanzasViewModelTests
{
    [Fact]
    public async Task CargarAsync_CargaLasTresSubListas()
    {
        var fuentesSvc = new Mock<IFuenteFinanciamientoService>();
        fuentesSvc.Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "Literal B" } });
        var rubrosSvc = new Mock<IRubroGastoService>();
        rubrosSvc.Setup(s => s.ListarTodosAsync())
            .ReturnsAsync(new List<RubroGasto> { new() { Id = 1, Codigo = 1, Nombre = "Sueldos" } });
        var lineasSvc = new Mock<ILineaPoaService>();
        lineasSvc.Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<LineaPoa> { new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 } });

        var nav = new Mock<INavigationService>().Object;
        var confirm = new Mock<IConfirmacionService>().Object;

        var vm = new MaestrosFinanzasViewModel(
            new FuenteFinanciamientoListViewModel(fuentesSvc.Object, nav, confirm),
            new RubroGastoListViewModel(rubrosSvc.Object, nav, confirm),
            new LineaPoaListViewModel(lineasSvc.Object, nav, confirm));

        await vm.CargarAsync();

        Assert.Single(vm.FuentesVm.Items);
        Assert.Single(vm.RubrosVm.Items);
        Assert.Single(vm.LineasPoaVm.Items);
    }
}
