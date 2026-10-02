using System.Collections.Generic;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>Pie del sidebar v2 (spec §4.1): avatar con iniciales, nombre y rol del usuario.</summary>
public class ShellMainViewModelPieTests
{
    private static ShellMainViewModel Crear(UsuarioSesion? usuario)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario?.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new ShellMainViewModel(sesion.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(i => i.Version == "0.4.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
    }

    [Fact]
    public void ConNombreCompleto_MuestraElNombreCompletoYSusIniciales()
    {
        var vm = Crear(new UsuarioSesion(1, "mperez", RolUsuario.Admin, "María Pérez"));

        Assert.Equal("María Pérez", vm.NombreUsuarioVisible);
        Assert.Equal("MP", vm.Iniciales);
        Assert.Equal("Administrador", vm.RolTexto);
    }

    [Fact]
    public void SinNombreCompleto_UsaElNombreDeUsuario()
    {
        var vm = Crear(new UsuarioSesion(2, "jrodriguez", RolUsuario.Operador, null));

        Assert.Equal("jrodriguez", vm.NombreUsuarioVisible);
        Assert.Equal("JR", vm.Iniciales);
        Assert.Equal("Operador", vm.RolTexto);
    }

    [Fact]
    public void NombreCompletoEnBlanco_UsaElNombreDeUsuario() =>
        Assert.Equal("admin", Crear(new UsuarioSesion(3, "admin", RolUsuario.Admin, "   ")).NombreUsuarioVisible);

    [Fact]
    public void SinSesion_TodoVacio()
    {
        var vm = Crear(null);

        Assert.Equal("", vm.NombreUsuarioVisible);
        Assert.Equal("", vm.Iniciales);
        Assert.Equal("", vm.RolTexto);
    }

    [Theory]
    [InlineData("María Pérez", "MP")]
    [InlineData("Juan Carlos Rodríguez", "JR")]
    [InlineData("  ana   gómez  ", "AG")]
    [InlineData("admin", "AD")]
    [InlineData("x", "X")]
    [InlineData("", "")]
    public void CalcularIniciales(string nombre, string esperado) =>
        Assert.Equal(esperado, ShellMainViewModel.CalcularIniciales(nombre));
}
