using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ReclasificarTareaDialogViewModelTests
{
    [Fact]
    public void Constructor_ExponeElPanelRecibido()
    {
        var panel = new ClasificacionTareaPanelViewModel(
            Mock.Of<IZonaService>(), Mock.Of<IDimensionTematicaService>(),
            Mock.Of<IOrganismoResponsableService>(), Mock.Of<IOrigenFinanciamientoService>(),
            Mock.Of<IDocumentoAdministrativoService>());

        var vm = new ReclasificarTareaDialogViewModel(panel);

        Assert.Same(panel, vm.Panel);
    }
}
