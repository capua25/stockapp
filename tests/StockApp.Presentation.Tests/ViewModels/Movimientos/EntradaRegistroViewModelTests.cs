using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

public class EntradaRegistroViewModelTests : MovimientoRegistroViewModelTestsBase
{
    protected override MovimientoRegistroViewModelBase CrearVm(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        => new EntradaRegistroViewModel(service, productoService, navigation, confirmacion);

    private static EntradaRegistroViewModel CrearVmConcreto()
        => new EntradaRegistroViewModel(
            Mock.Of<IMovimientoStockService>(),
            Mock.Of<IProductoService>(),
            Mock.Of<INavigationService>(),
            Mock.Of<IConfirmacionService>());

    [Fact]
    public void Tipo_EsEntrada()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(TipoMovimiento.Entrada, vm.Tipo);
    }

    [Fact]
    public void MotivosDisponibles_SoloCompraYAjuste()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(new[] { MotivoMovimiento.Compra, MotivoMovimiento.Ajuste }, vm.MotivosDisponibles);
    }

    [Fact]
    public void Motivo_PorDefecto_EsCompra()
    {
        var vm = CrearVmConcreto();

        Assert.Equal(MotivoMovimiento.Compra, vm.Motivo);
    }
}
