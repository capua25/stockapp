using Moq;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
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

        var vm = new ShellMainViewModel(sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
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

    // ── tests: versión de la app ─────────────────────────────────────────────

    [Fact]
    public void VersionTexto_ExponeVersionDeIInfoApp_ConPrefijoV()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var infoApp = Mock.Of<IInfoApp>(x => x.Version == "9.9.9");

        var vm = new ShellMainViewModel(sessionMock.Object, Mock.Of<INavigationService>(), infoApp);

        Assert.Equal("v9.9.9", vm.VersionTexto);
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

    // ── D6 — Navegación a movimientos ────────────────────────────────────────

    [Fact]
    public void NavMovimientos_LlamaNavegar_AMovimientoRegistroViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Operador);

        vm.NavMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavHistorialMovimientos_LlamaNavegar_AMovimientoHistorialViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public void NavMovimientos_Admin_LlamaNavegar_AMovimientoRegistroViewModel()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoRegistroViewModel>(), Times.Once);
    }

    // ── Tarea 4 (UI Kit): estado activo del sidebar ──────────────────────────

    [Fact]
    public void NavProductos_EstableceSeccionActiva_Productos()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        Assert.Equal("Productos", vm.SeccionActiva);
    }

    [Fact]
    public void NavCategorias_EstableceSeccionActiva_Categorias()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        Assert.Equal("Categorias", vm.SeccionActiva);
    }

    [Fact]
    public void NavProveedores_EstableceSeccionActiva_Proveedores()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        Assert.Equal("Proveedores", vm.SeccionActiva);
    }

    [Fact]
    public void NavUnidadesMedida_EstableceSeccionActiva_UnidadesMedida()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        Assert.Equal("UnidadesMedida", vm.SeccionActiva);
    }

    [Fact]
    public void NavMovimientos_EstableceSeccionActiva_Movimientos()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        vm.NavMovimientosCommand.Execute(null);

        Assert.Equal("Movimientos", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialMovimientos_EstableceSeccionActiva_HistorialMovimientos()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        Assert.Equal("HistorialMovimientos", vm.SeccionActiva);
    }

    [Fact]
    public void NavValorizacion_EstableceSeccionActiva_Valorizacion()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavValorizacionCommand.Execute(null);

        Assert.Equal("Valorizacion", vm.SeccionActiva);
    }

    [Fact]
    public void NavStockCategoria_EstableceSeccionActiva_StockCategoria()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavStockCategoriaCommand.Execute(null);

        Assert.Equal("StockCategoria", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialPorProducto_EstableceSeccionActiva_HistorialPorProducto()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavHistorialPorProductoCommand.Execute(null);

        Assert.Equal("HistorialPorProducto", vm.SeccionActiva);
    }

    [Fact]
    public void NavMasMovidos_EstableceSeccionActiva_MasMovidos()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavMasMovidosCommand.Execute(null);

        Assert.Equal("MasMovidos", vm.SeccionActiva);
    }

    [Fact]
    public void NavAuditoriaLog_EstableceSeccionActiva_AuditoriaLog()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        vm.NavAuditoriaLogCommand.Execute(null);

        Assert.Equal("AuditoriaLog", vm.SeccionActiva);
    }
}
