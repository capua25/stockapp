using Moq;
using StockApp.ApiClient;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
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

public class LoginViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Fake de IUiDispatcher: ejecuta inline (sin depender de Dispatcher.UIThread real,
    /// no inicializado en tests unitarios sin Application de Avalonia).
    /// </summary>
    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public void Post(Action accion) => accion();
    }

    private static ShellViewModel CrearShellFake()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(
                    sessionMock.Object, Mock.Of<INavigationService>(),
                    Mock.Of<IInfoApp>(x => x.Version == "0.0.0"), Mock.Of<IConfirmacionService>());
            if (t == typeof(InicioViewModel))
                return new InicioViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        return new ShellViewModel(
            Mock.Of<IAuthService>(),
            Mock.Of<ILicenciaService>(),
            Mock.Of<IResetAdminService>(),
            navSvc,
            coordinador,
            new FakeUiDispatcher(),
            Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
    }

    private static (LoginViewModel vm, Mock<IAuthService> authMock, ShellViewModel shell)
        Crear(LoginResult resultado)
    {
        var authMock = new Mock<IAuthService>();
        authMock
            .Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultado);

        var shell = CrearShellFake();
        var vm    = new LoginViewModel(authMock.Object, shell, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
        return (vm, authMock, shell);
    }

    // ── tests: CanExecute (botón Entrar) ─────────────────────────────────────

    [Fact]
    public void EntrarCommand_SinDatos_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear(LoginResult.Ok());

        Assert.False(vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public void EntrarCommand_ConUsuarioSinContrasena_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear(LoginResult.Ok());
        vm.NombreUsuario = "admin";

        Assert.False(vm.EntrarCommand.CanExecute(null));
    }

    [Fact]
    public void EntrarCommand_ConAmbosValores_EstaHabilitado()
    {
        var (vm, _, _) = Crear(LoginResult.Ok());
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        Assert.True(vm.EntrarCommand.CanExecute(null));
    }

    // ── tests: login exitoso ─────────────────────────────────────────────────

    [Fact]
    public async Task Login_Exitoso_NavegaAContenidoPrincipal()
    {
        var (vm, _, shell) = Crear(LoginResult.Ok());
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.IsType<ShellMainViewModel>(shell.CurrentViewModel);
        Assert.Null(vm.MensajeError);
    }

    // ── tests: mensajes de error (anti user-enumeration) ────────────────────

    [Theory]
    [InlineData(LoginError.UsuarioNoEncontrado)]
    [InlineData(LoginError.ContrasenaInvalida)]
    [InlineData(LoginError.UsuarioInactivo)]
    public async Task Login_CualquierFallo_MuestraMensajeGenerico(LoginError error)
    {
        var (vm, _, shell) = Crear(LoginResult.Fallo(error));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal("Usuario o contraseña incorrectos.", vm.MensajeError);
        Assert.IsNotType<ShellMainViewModel>(shell.CurrentViewModel);
    }

    [Theory]
    [InlineData(LoginError.UsuarioNoEncontrado)]
    [InlineData(LoginError.ContrasenaInvalida)]
    [InlineData(LoginError.UsuarioInactivo)]
    public async Task Login_TodosLosErrores_ProducenElMismoMensaje(LoginError error)
    {
        var (vm, _, _) = Crear(LoginResult.Fallo(error));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        await vm.EntrarCommand.ExecuteAsync(null);

        // Verificar que el mensaje NO distingue el tipo de error
        Assert.Equal("Usuario o contraseña incorrectos.", vm.MensajeError);
    }

    // ── tests: versión de la app ─────────────────────────────────────────────

    [Fact]
    public void VersionTexto_ExponeVersionDeIInfoApp_ConPrefijoV()
    {
        var authMock = new Mock<IAuthService>();
        var shell    = CrearShellFake();
        var infoApp  = Mock.Of<IInfoApp>(x => x.Version == "9.9.9");

        var vm = new LoginViewModel(authMock.Object, shell, infoApp);

        Assert.Equal("v9.9.9", vm.VersionTexto);
    }

    // ── tests: OperacionEnCurso desactiva el botón ───────────────────────────

    [Fact]
    public async Task Login_MientrasEstaEnCurso_BotonDeshabilitado()
    {
        var tcs      = new TaskCompletionSource<LoginResult>();
        var authMock = new Mock<IAuthService>();
        authMock
            .Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(tcs.Task);

        var shell = CrearShellFake();
        var vm    = new LoginViewModel(authMock.Object, shell, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        var tarea = vm.EntrarCommand.ExecuteAsync(null);

        // Mientras la tarea está pendiente, CanExecute debe ser false
        Assert.False(vm.EntrarCommand.CanExecute(null));

        tcs.SetResult(LoginResult.Ok());
        await tarea;
    }

    [Fact]
    public async Task Entrar_ServidorCaido_MuestraElMensajeDeConexion()
    {
        // Spec 3b: "Login con servidor caído → el login muestra el error de conexión,
        // permite reintentar" (el comando queda habilitado de nuevo al terminar).
        var authMock = new Mock<IAuthService>();
        authMock
            .Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new ServidorNoDisponibleException());
        var vm = new LoginViewModel(authMock.Object, CrearShellFake(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto123";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);
        Assert.False(vm.OperacionEnCurso);
        Assert.True(vm.EntrarCommand.CanExecute(null)); // puede reintentar
    }

    // ── tests: reset de Admin (Inc 7 Fase B) ─────────────────────────────────

    [Fact]
    public void ResetearAdmin_MuestraElResetEnElShell()
    {
        var shell = CrearShellFake();
        var vm    = new LoginViewModel(Mock.Of<IAuthService>(), shell, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));

        vm.ResetearAdminCommand.Execute(null);

        Assert.IsType<ResetAdminViewModel>(shell.CurrentViewModel);
    }
}
