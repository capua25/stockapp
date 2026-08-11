using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ShellMainFinanzasTests
{
    [Fact]
    public void NavMaestrosFinanzas_NavegaYMarcaSeccionActiva()
    {
        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            new Mock<ICurrentSession>().Object,
            navMock.Object,
            Mock.Of<IInfoApp>(i => i.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(),
            Mock.Of<IAuthService>());

        vm.NavMaestrosFinanzasCommand.Execute(null);

        Assert.Equal("MaestrosFinanzas", vm.SeccionActiva);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }
}
