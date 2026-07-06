using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class SalidaRegistroViewModelTests : MovimientoRegistroViewModelTestsBase
{
    protected override MovimientoRegistroViewModelBase CrearVm(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        => new SalidaRegistroViewModel(service, productoService, navigation, confirmacion);

    private static SalidaRegistroViewModel CrearVmConcreto()
        => new SalidaRegistroViewModel(
            Mock.Of<IMovimientoStockService>(),
            Mock.Of<IProductoService>(),
            Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>());

    [Fact]
    public void Tipo_EsSalida()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(TipoMovimiento.Salida, vm.Tipo);
    }

    [Fact]
    public void MotivosDisponibles_VentaMermaAjuste()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(new[] { MotivoMovimiento.Venta, MotivoMovimiento.Merma, MotivoMovimiento.Ajuste }, vm.MotivosDisponibles);
    }

    [Fact]
    public void Motivo_PorDefecto_EsVenta()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(MotivoMovimiento.Venta, vm.Motivo);
    }
}
