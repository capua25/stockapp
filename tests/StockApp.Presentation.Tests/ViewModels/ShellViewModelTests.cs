using Moq;
using StockApp.ApiClient;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
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

        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(true, "MAQ")); // activada → va al login

        return new ShellViewModel(
            Mock.Of<IAuthService>(),
            licenciaMock.Object,
            Mock.Of<IResetAdminService>(),
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

    // ── tests: licencia (Inc 7 Fase B) ───────────────────────────────────────

    [Fact]
    public async Task Inicializar_LicenciaNoActivada_MuestraBloqueo()
    {
        // helper inline con licencia NO activada
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ReturnsAsync(new EstadoLicenciaDto(false, "MAQ-1"));
        var navSvc = new NavigationService(_ => throw new InvalidOperationException());
        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(), licenciaMock.Object, Mock.Of<IResetAdminService>(),
            navSvc, coordinador, new FakeUiDispatcher(), InfoAppStub);

        await shell.InicializarAsync();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarBloqueoLicencia_EstableceBloqueoLicenciaViewModel()
    {
        var shell = Crear();

        shell.MostrarBloqueoLicencia();

        Assert.IsType<BloqueoLicenciaViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public async Task Inicializar_ApiCaidaAlConsultarLicencia_MuestraLogin()
    {
        // API inalcanzable al arrancar: no debe tumbar el arranque, cae a login
        // (el login es quien muestra el error de conexión al usuario).
        var licenciaMock = new Mock<ILicenciaService>();
        licenciaMock.Setup(s => s.ObtenerEstadoAsync())
                    .ThrowsAsync(new ServidorNoDisponibleException());
        var navSvc = new NavigationService(_ => throw new InvalidOperationException());
        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());
        var shell = new ShellViewModel(
            Mock.Of<IAuthService>(), licenciaMock.Object, Mock.Of<IResetAdminService>(),
            navSvc, coordinador, new FakeUiDispatcher(), InfoAppStub);

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarReset_EstableceResetAdminViewModel()
    {
        var shell = Crear();

        shell.MostrarReset();

        Assert.IsType<ResetAdminViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarBloqueoLicencia_LlamadoDosVeces_NoRecreaElViewModel()
    {
        // Idempotencia: varios 423 concurrentes (ver ApiSession.LicenciaDesactivada) no
        // deben re-navegar ni perder lo que el usuario ya pegó en el VM de bloqueo activo.
        var shell = Crear();

        shell.MostrarBloqueoLicencia();
        var primerVm = shell.CurrentViewModel;
        shell.MostrarBloqueoLicencia();

        Assert.Same(primerVm, shell.CurrentViewModel);
    }
}
