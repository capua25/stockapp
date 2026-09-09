using System.Collections.Generic;
using Moq;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Catalogo;

public class CatalogosTareaViewModelTests
{
    [Fact]
    public void Constructor_ExponeLosCuatroSubViewModelsInyectados()
    {
        var zonasVm = TestFactories.ZonaListViewModelVacio();
        var dimensionesVm = TestFactories.DimensionTematicaListViewModelVacio();
        var organismosVm = TestFactories.OrganismoResponsableListViewModelVacio();
        var origenesVm = TestFactories.OrigenFinanciamientoListViewModelVacio();

        var vm = new CatalogosTareaViewModel(zonasVm, dimensionesVm, organismosVm, origenesVm);

        Assert.Same(zonasVm, vm.ZonasVm);
        Assert.Same(dimensionesVm, vm.DimensionesVm);
        Assert.Same(organismosVm, vm.OrganismosVm);
        Assert.Same(origenesVm, vm.OrigenesVm);
    }
}

/// <summary>
/// Fábricas mínimas para construir los cuatro ListViewModel con dependencias mockeadas,
/// sin repetir el helper Crear() de cada archivo de test de Task 7 (que es privado a su clase).
/// </summary>
internal static class TestFactories
{
    public static ZonaListViewModel ZonaListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IZonaService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.Zona>());
        return new ZonaListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static DimensionTematicaListViewModel DimensionTematicaListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IDimensionTematicaService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.DimensionTematica>());
        return new DimensionTematicaListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static OrganismoResponsableListViewModel OrganismoResponsableListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IOrganismoResponsableService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.OrganismoResponsable>());
        return new OrganismoResponsableListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }

    public static OrigenFinanciamientoListViewModel OrigenFinanciamientoListViewModelVacio()
    {
        var svc = new Moq.Mock<StockApp.Application.Catalogo.IOrigenFinanciamientoService>();
        svc.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<StockApp.Domain.Entities.OrigenFinanciamiento>());
        return new OrigenFinanciamientoListViewModel(
            svc.Object,
            Moq.Mock.Of<StockApp.Presentation.Navigation.INavigationService>(),
            Moq.Mock.Of<StockApp.Presentation.Services.IConfirmacionService>());
    }
}
