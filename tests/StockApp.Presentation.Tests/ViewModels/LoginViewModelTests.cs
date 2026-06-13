using Moq;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class LoginViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ShellViewModel CrearShellFake()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

        var navSvc = new NavigationService(t =>
        {
            if (t == typeof(ShellMainViewModel))
                return new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>());
            throw new InvalidOperationException($"Tipo no registrado en test: {t.Name}");
        });

        var updateStub = new Mock<IUpdateService>();
        updateStub.Setup(s => s.BuscarAsync(default)).ReturnsAsync(UpdateCheckResult.SinUpdate);
        var coordinador = new CoordinadorActualizacion(updateStub.Object, new PoliticaUxActualizacion());

        return new ShellViewModel(
            Mock.Of<IPrimerArranqueService>(),
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>(),
            navSvc,
            coordinador);
    }

    private static (LoginViewModel vm, Mock<IAuthService> authMock, ShellViewModel shell)
        Crear(LoginResult resultado)
    {
        var authMock = new Mock<IAuthService>();
        authMock
            .Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(resultado);

        var shell = CrearShellFake();
        var vm    = new LoginViewModel(authMock.Object, shell);
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
        var vm    = new LoginViewModel(authMock.Object, shell);
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto";

        var tarea = vm.EntrarCommand.ExecuteAsync(null);

        // Mientras la tarea está pendiente, CanExecute debe ser false
        Assert.False(vm.EntrarCommand.CanExecute(null));

        tcs.SetResult(LoginResult.Ok());
        await tarea;
    }
}
