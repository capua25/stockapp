using Moq;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class UsuariosAdminViewModelTests
{
    private static (UsuariosAdminViewModel vm, Mock<IUsuarioService> svc, Mock<IConfirmacionService> confirm) Crear()
    {
        var svc = new Mock<IUsuarioService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var vm = new UsuariosAdminViewModel(svc.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    private static UsuarioDto Dto(int id, RolUsuario rol) =>
        new(id, $"usuario{id}", null, rol, true, DateTime.UtcNow);

    [Fact]
    public async Task CargarAsync_PueblaItemsDesdeElServicio()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto> { Dto(1, RolUsuario.Admin), Dto(2, RolUsuario.Operador) });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void UsuarioSeleccionado_Admin_EsAdminSeleccionadoEsTrue()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(1, RolUsuario.Admin);

        Assert.True(vm.EsAdminSeleccionado);
    }

    [Fact]
    public void UsuarioSeleccionado_Operador_EsAdminSeleccionadoEsFalse()
    {
        var (vm, _, _) = Crear();

        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        Assert.False(vm.EsAdminSeleccionado);
    }

    [Fact]
    public async Task AltaAsync_LlamaAlServicioConLosCamposCargados_YRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador))
            .ReturnsAsync(9);
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.NuevoNombreUsuario = "nuevo";
        vm.NuevoNombreCompleto = "Nombre Completo";
        vm.NuevaContrasenaPlan = "pwd12345";
        vm.NuevoRol = RolUsuario.Operador;

        await vm.AltaCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaUsuarioAsync("nuevo", "Nombre Completo", "pwd12345", RolUsuario.Operador), Times.Once);
        svc.Verify(s => s.ListarAsync(), Times.Once);
        Assert.Equal(string.Empty, vm.NuevoNombreUsuario);
    }

    [Fact]
    public async Task AltaAsync_NombreDuplicado_MuestraMensajeErrorSinRecargar()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre."));
        vm.NuevoNombreUsuario = "repetido";
        vm.NuevaContrasenaPlan = "pwd12345";

        await vm.AltaCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe un usuario con ese nombre.", vm.MensajeError);
        svc.Verify(s => s.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task BajaAsync_ConConfirmacion_LlamaAlServicioYRecarga()
    {
        var (vm, svc, confirm) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svc.Verify(s => s.BajaLogicaAsync(2), Times.Once);
    }

    [Fact]
    public async Task BajaAsync_SinSeleccion_NoLlamaAlServicio()
    {
        var (vm, svc, _) = Crear();

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarRolAsync_LlamaAlServicioConElUsuarioSeleccionadoYRecarga()
    {
        var (vm, svc, _) = Crear();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(new List<UsuarioDto>());
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);

        await vm.CambiarRolCommand.ExecuteAsync(RolUsuario.Admin);

        svc.Verify(s => s.CambiarRolAsync(2, RolUsuario.Admin), Times.Once);
    }

    [Fact]
    public async Task CambiarContrasenaAsync_LlamaAlServicioConLaContrasenaCargadaYLimpiaElCampo()
    {
        var (vm, svc, _) = Crear();
        vm.UsuarioSeleccionado = Dto(2, RolUsuario.Operador);
        vm.NuevaContrasenaParaSeleccionado = "otraClave123";

        await vm.CambiarContrasenaCommand.ExecuteAsync(null);

        svc.Verify(s => s.CambiarContrasenaAsync(2, "otraClave123", null), Times.Once);
        Assert.Equal(string.Empty, vm.NuevaContrasenaParaSeleccionado);
    }
}
