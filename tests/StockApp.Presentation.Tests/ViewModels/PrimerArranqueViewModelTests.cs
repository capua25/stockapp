using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class PrimerArranqueViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static ShellViewModel CrearShellFake()
    {
        return new ShellViewModel(
            Mock.Of<IPrimerArranqueService>(),
            Mock.Of<IAuthService>(),
            Mock.Of<IUsuarioService>());
    }

    private record Contexto(
        PrimerArranqueViewModel Vm,
        Mock<IPrimerArranqueService> PrimerArranqueMock,
        Mock<IAuthService> AuthMock,
        Mock<IUsuarioService> UsuarioMock,
        ShellViewModel Shell);

    private static Contexto Crear(
        Exception? excepcionCreacion = null)
    {
        var primerArranqueMock = new Mock<IPrimerArranqueService>();

        if (excepcionCreacion is not null)
            primerArranqueMock
                .Setup(p => p.CrearAdminInicialAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(excepcionCreacion);
        else
            primerArranqueMock
                .Setup(p => p.CrearAdminInicialAsync(It.IsAny<string>(), It.IsAny<string>()))
                .Returns(Task.CompletedTask);

        var authMock    = new Mock<IAuthService>();
        var usuarioMock = new Mock<IUsuarioService>();

        var shell = CrearShellFake();

        var vm = new PrimerArranqueViewModel(
            primerArranqueMock.Object,
            authMock.Object,
            usuarioMock.Object,
            shell);

        return new Contexto(vm, primerArranqueMock, authMock, usuarioMock, shell);
    }

    // ── tests: validación de formulario ──────────────────────────────────────

    [Fact]
    public void ValidarFormulario_CamposVacios_RetornaError()
    {
        var ctx = Crear();

        var error = ctx.Vm.ValidarFormulario();

        Assert.NotNull(error);
        Assert.Contains("vacío", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidarFormulario_ContrasenaCorta_RetornaError()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "123";
        ctx.Vm.ConfirmarContrasena = "123";

        var error = ctx.Vm.ValidarFormulario();

        Assert.NotNull(error);
        Assert.Contains("6", error); // debe mencionar el mínimo de caracteres
    }

    [Fact]
    public void ValidarFormulario_ContrasenasDistintas_RetornaError()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto456";

        var error = ctx.Vm.ValidarFormulario();

        Assert.NotNull(error);
        Assert.Contains("coinciden", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidarFormulario_DatosOk_RetornaNull()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        var error = ctx.Vm.ValidarFormulario();

        Assert.Null(error);
    }

    // ── tests: CanExecute del comando CrearAdmin ──────────────────────────────

    [Fact]
    public void CrearAdminCommand_FormularioInvalido_EstaDeshabilitado()
    {
        var ctx = Crear();
        // Formulario vacío
        Assert.False(ctx.Vm.CrearAdminCommand.CanExecute(null));
    }

    [Fact]
    public void CrearAdminCommand_ContrasenaCorta_EstaDeshabilitado()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "123";
        ctx.Vm.ConfirmarContrasena = "123";

        Assert.False(ctx.Vm.CrearAdminCommand.CanExecute(null));
    }

    [Fact]
    public void CrearAdminCommand_ContrasenasDistintas_EstaDeshabilitado()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "otraCosa";

        Assert.False(ctx.Vm.CrearAdminCommand.CanExecute(null));
    }

    [Fact]
    public void CrearAdminCommand_FormularioValido_EstaHabilitado()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        Assert.True(ctx.Vm.CrearAdminCommand.CanExecute(null));
    }

    // ── tests: flujo de creación exitosa → recomendación 2do Admin ───────────

    [Fact]
    public async Task CrearAdmin_Exitoso_MuestraRecomendacion2doAdmin()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        Assert.True(ctx.Vm.MostrarRecomendacion2doAdmin);
        Assert.Null(ctx.Vm.MensajeError);
    }

    [Fact]
    public async Task CrearAdmin_Exitoso_LlamaPrimerArranqueService()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        ctx.PrimerArranqueMock.Verify(
            p => p.CrearAdminInicialAsync("admin", "secreto123"),
            Times.Once);
    }

    // ── tests: errores en la creación ────────────────────────────────────────

    [Fact]
    public async Task CrearAdmin_InvalidOperationException_MuestraMensajeError()
    {
        var ctx = Crear(excepcionCreacion: new InvalidOperationException("Ya existe un usuario."));
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        Assert.NotNull(ctx.Vm.MensajeError);
        Assert.False(ctx.Vm.MostrarRecomendacion2doAdmin);
    }

    // ── tests: continuar sin 2do Admin ───────────────────────────────────────

    [Fact]
    public async Task ContinuarSin2doAdmin_NavegaALogin()
    {
        var ctx = Crear();
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";
        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        ctx.Vm.ContinuarSin2doAdminCommand.Execute(null);

        Assert.IsType<LoginViewModel>(ctx.Shell.CurrentViewModel);
    }
}
