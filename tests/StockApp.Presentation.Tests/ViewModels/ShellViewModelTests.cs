using Moq;
using StockApp.ApiClient;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ShellViewModelTests
{
    private static readonly IInfoApp InfoAppStub = Mock.Of<IInfoApp>(x => x.Version == "0.0.0");

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fake de IUiDispatcher: ejecuta inline (sin depender de Dispatcher.UIThread real,
    /// no inicializado en tests unitarios sin Application de Avalonia).
    /// </summary>
    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public void Post(Action accion) => accion();
    }

    private static ShellViewModel Crear()
    {
        // NavigationService real con un resolver que devuelve un ShellMainViewModel stub
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>(), InfoAppStub);
            if (t == typeof(InicioViewModel))
                return new InicioViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        return new ShellViewModel(
            Mock.Of<IAuthService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            InfoAppStub);
    }

    // ── tests: navegación de arranque ────────────────────────────────────────

    [Fact]
    public async Task Inicializar_MuestraLogin()
    {
        var shell = Crear();

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    // ── tests: navegación manual ─────────────────────────────────────────────

    [Fact]
    public void MostrarLogin_EstableceLoginViewModel()
    {
        var shell = Crear();

        shell.MostrarLogin();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarContenidoPrincipal_EstableceShellMainViewModel()
    {
        var shell = Crear();

        shell.MostrarContenidoPrincipal();

        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarLoginConAviso_EstableceLoginConElMensaje()
    {
        var shell = Crear();

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.Equal("Sesión vencida, ingresá de nuevo.", login.MensajeError);
    }
}
