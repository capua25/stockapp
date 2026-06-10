using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ShellViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ShellViewModel shell, Mock<IPrimerArranqueService> primerArranqueMock)
        Crear(bool requiereCrearAdmin)
    {
        var primerArranqueMock = new Mock<IPrimerArranqueService>();
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ReturnsAsync(requiereCrearAdmin);

        // NavigationService real con un resolver que devuelve un ShellMainViewModel stub
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc);

        return (shell, primerArranqueMock);
    }

    // ── tests: navegación de arranque ────────────────────────────────────────

    [Fact]
    public async Task Inicializar_RequiereCrearAdmin_MuestraPrimerArranque()
    {
        var (shell, _) = Crear(requiereCrearAdmin: true);

        await shell.InicializarAsync();

        Assert.IsType<PrimerArranqueViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Inicializar_NoRequiereCrearAdmin_MuestraLogin()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    // ── tests: navegación manual ─────────────────────────────────────────────

    [Fact]
    public void MostrarLogin_EstableceLoginViewModel()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        shell.MostrarLogin();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarPrimerArranque_EstablecePrimerArranqueViewModel()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        shell.MostrarPrimerArranque();

        Assert.IsType<PrimerArranqueViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarContenidoPrincipal_EstableceShellMainViewModel()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        shell.MostrarContenidoPrincipal();

        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
    }
}
