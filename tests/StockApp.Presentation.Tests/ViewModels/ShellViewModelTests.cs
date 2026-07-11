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
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>(), InfoAppStub);
            if (t == typeof(InicioViewModel))
                return new InicioViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        var shell = new ShellViewModel(
            primerArranqueMock.Object,
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            InfoAppStub);

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

    [Fact]
    public async Task Inicializar_ServidorCaido_MuestraLoginIgual()
    {
        // Spec 3b, manejo de errores: si la API no responde en el arranque, la app no
        // muere — muestra el login; el intento de login informará el error de conexión.
        var (shell, primerArranqueMock) = Crear(requiereCrearAdmin: false);
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ThrowsAsync(new ServidorNoDisponibleException());

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarLoginConAviso_EstableceLoginConElMensaje()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.Equal("Sesión vencida, ingresá de nuevo.", login.MensajeError);
    }
}
