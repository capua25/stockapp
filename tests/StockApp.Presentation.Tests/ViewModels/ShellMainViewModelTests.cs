using Moq;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class ShellMainViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock)
        Crear(RolUsuario rol)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);

        var navMock = new Mock<INavigationService>();

        var vm = new ShellMainViewModel(sessionMock.Object, navMock.Object);
        return (vm, sessionMock, navMock);
    }

    // ── D2.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Admin_EsAdmin_True()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void Operador_EsAdmin_False()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        Assert.False(vm.EsAdmin);
    }

    [Fact]
    public void NavProductos_LlamaNavegar_AProductoListViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavCategoria_Admin_LlamaNavegar_ACategoriaListViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        navMock.Verify(n => n.Navegar<CategoriaListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavProveedores_Admin_LlamaNavegar_AProveedorListViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProveedorListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavUnidadesMedida_Admin_LlamaNavegar_AUnidadMedidaListViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<UnidadMedidaListViewModel>(), Times.Once);
    }
}
