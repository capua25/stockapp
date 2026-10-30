using Moq;
using StockApp.Application.Documentos;
using StockApp.Application.Proyectos;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class ReclasificarTareaDialogViewModelTests
{
    [Fact]
    public void Constructor_ExponeElPanelRecibido()
    {
        var panel = new ClasificacionTareaPanelViewModel(
            Mock.Of<IProyectoService>(), Mock.Of<IDocumentoAdministrativoService>(), Mock.Of<IConfirmacionService>());

        var vm = new ReclasificarTareaDialogViewModel(panel);

        Assert.Same(panel, vm.Panel);
    }
}
