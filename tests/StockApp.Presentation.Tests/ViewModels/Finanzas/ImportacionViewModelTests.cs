using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ImportacionViewModelTests
{
    [Fact]
    public void Constructor_ExponeAmbosSubVms()
    {
        var servicio = Mock.Of<IImportacionService>();
        var seleccion = Mock.Of<IServicioSeleccionArchivo>();
        var confirmacion = Mock.Of<IConfirmacionService>();

        var nuevaVm = new NuevaImportacionViewModel(servicio, seleccion, confirmacion);
        var historialVm = new HistorialImportacionesViewModel(servicio, confirmacion);

        var vm = new ImportacionViewModel(nuevaVm, historialVm);

        Assert.Same(nuevaVm, vm.NuevaVm);
        Assert.IsType<NuevaImportacionViewModel>(vm.NuevaVm);
        Assert.Same(historialVm, vm.HistorialVm);
        Assert.IsType<HistorialImportacionesViewModel>(vm.HistorialVm);
    }
}
