using System.Threading.Tasks;
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

    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock, Mock<IConfirmacionService> confirmMock)
        Crear(RolUsuario rol)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);

        var navMock = new Mock<INavigationService>();

        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"), confirmMock.Object);
        return (vm, sessionMock, navMock, confirmMock);
    }

    // ── Inicio ───────────────────────────────────────────────────────────────

    [Fact]
    public void NavInicio_LlamaNavegar_AInicioViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavInicioCommand.Execute(null);

        navMock.Verify(n => n.Navegar<InicioViewModel>(), Times.Once);
    }

    [Fact]
    public void NavInicio_EstableceSeccionActiva_Inicio()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavInicioCommand.Execute(null);

        Assert.Equal("Inicio", vm.SeccionActiva);
    }

    // ── D2.1 tests ────────────────────────────────────────────────────────────

    [Fact]
    public void Admin_EsAdmin_True()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void Operador_EsAdmin_False()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        Assert.False(vm.EsAdmin);
    }

    // ── tests: versión de la app ─────────────────────────────────────────────

    [Fact]
    public void VersionTexto_ExponeVersionDeIInfoApp_ConPrefijoV()
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var infoApp = Mock.Of<IInfoApp>(x => x.Version == "9.9.9");

        var vm = new ShellMainViewModel(
            sessionMock.Object, Mock.Of<INavigationService>(), infoApp, Mock.Of<IConfirmacionService>());

        Assert.Equal("v9.9.9", vm.VersionTexto);
    }

    [Fact]
    public void NavProductos_LlamaNavegar_AProductoListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProductoListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavCategoria_Admin_LlamaNavegar_ACategoriaListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        navMock.Verify(n => n.Navegar<CategoriaListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavProveedores_Admin_LlamaNavegar_AProveedorListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ProveedorListViewModel>(), Times.Once);
    }

    [Fact]
    public void NavUnidadesMedida_Admin_LlamaNavegar_AUnidadMedidaListViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<UnidadMedidaListViewModel>(), Times.Once);
    }

    // ── D6 — Navegación a movimientos ────────────────────────────────────────

    [Fact]
    public void NavRegistrarEntrada_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarSalida_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavHistorialMovimientos_LlamaNavegar_AMovimientoHistorialViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        navMock.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarEntrada_Admin_LlamaNavegar_AEntradaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavRegistrarEntradaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<EntradaRegistroViewModel>(), Times.Once);
    }

    [Fact]
    public void NavRegistrarSalida_Admin_LlamaNavegar_ASalidaRegistroViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavRegistrarSalidaCommand.Execute(null);

        navMock.Verify(n => n.Navegar<SalidaRegistroViewModel>(), Times.Once);
    }

    // ── Tarea 4 (UI Kit): estado activo del sidebar ──────────────────────────

    [Fact]
    public void NavProductos_EstableceSeccionActiva_Productos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavProductosCommand.Execute(null);

        Assert.Equal("Productos", vm.SeccionActiva);
    }

    [Fact]
    public void NavCategorias_EstableceSeccionActiva_Categorias()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavCategoriasCommand.Execute(null);

        Assert.Equal("Categorias", vm.SeccionActiva);
    }

    [Fact]
    public void NavProveedores_EstableceSeccionActiva_Proveedores()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavProveedoresCommand.Execute(null);

        Assert.Equal("Proveedores", vm.SeccionActiva);
    }

    [Fact]
    public void NavUnidadesMedida_EstableceSeccionActiva_UnidadesMedida()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavUnidadesMedidaCommand.Execute(null);

        Assert.Equal("UnidadesMedida", vm.SeccionActiva);
    }

    [Fact]
    public void NavRegistrarEntrada_EstableceSeccionActiva_RegistrarEntrada()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarEntradaCommand.Execute(null);

        Assert.Equal("RegistrarEntrada", vm.SeccionActiva);
    }

    [Fact]
    public void NavRegistrarSalida_EstableceSeccionActiva_RegistrarSalida()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavRegistrarSalidaCommand.Execute(null);

        Assert.Equal("RegistrarSalida", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialMovimientos_EstableceSeccionActiva_HistorialMovimientos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Operador);

        vm.NavHistorialMovimientosCommand.Execute(null);

        Assert.Equal("HistorialMovimientos", vm.SeccionActiva);
    }

    [Fact]
    public void NavValorizacion_EstableceSeccionActiva_Valorizacion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavValorizacionCommand.Execute(null);

        Assert.Equal("Valorizacion", vm.SeccionActiva);
    }

    [Fact]
    public void NavStockCategoria_EstableceSeccionActiva_StockCategoria()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavStockCategoriaCommand.Execute(null);

        Assert.Equal("StockCategoria", vm.SeccionActiva);
    }

    [Fact]
    public void NavHistorialPorProducto_EstableceSeccionActiva_HistorialPorProducto()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavHistorialPorProductoCommand.Execute(null);

        Assert.Equal("HistorialPorProducto", vm.SeccionActiva);
    }

    [Fact]
    public void NavMasMovidos_EstableceSeccionActiva_MasMovidos()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavMasMovidosCommand.Execute(null);

        Assert.Equal("MasMovidos", vm.SeccionActiva);
    }

    [Fact]
    public void NavAuditoriaLog_EstableceSeccionActiva_AuditoriaLog()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavAuditoriaLogCommand.Execute(null);

        Assert.Equal("AuditoriaLog", vm.SeccionActiva);
    }

    // ── Cerrar sesión ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CerrarSesionCommand_Confirmado_LimpiaLaSesionYDisparaElEvento()
    {
        var (vm, sessionMock, _, confirmMock) = Crear(RolUsuario.Admin);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var disparado = false;
        vm.CerrarSesionSolicitado += () => disparado = true;

        await vm.CerrarSesionCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync("¿Cerrar la sesión?"), Times.Once);
        sessionMock.Verify(s => s.CerrarSesion(), Times.Once);
        Assert.True(disparado);
    }

    [Fact]
    public async Task CerrarSesionCommand_Cancelado_NoLimpiaNiDisparaElEvento()
    {
        var (vm, sessionMock, _, confirmMock) = Crear(RolUsuario.Admin);
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);

        var disparado = false;
        vm.CerrarSesionSolicitado += () => disparado = true;

        await vm.CerrarSesionCommand.ExecuteAsync(null);

        sessionMock.Verify(s => s.CerrarSesion(), Times.Never);
        Assert.False(disparado);
    }

    // ── Desconectar (fix leak de suscripción a INavigationService.Cambiado) ────

    [Fact]
    public void Desconectar_DesuscribeDeNavegacionCambiada_YaNoActualizaCurrentContent()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        var otroVm = Mock.Of<ViewModelBase>();
        navMock.Setup(n => n.Actual).Returns(otroVm);
        navMock.Raise(n => n.Cambiado += null);
        Assert.Same(otroVm, vm.CurrentContent);

        vm.Desconectar();

        var otroVm2 = Mock.Of<ViewModelBase>();
        navMock.Setup(n => n.Actual).Returns(otroVm2);
        navMock.Raise(n => n.Cambiado += null);

        // Tras Desconectar(), el handler ya no está enganchado: CurrentContent no debe
        // actualizarse con la nueva notificación del singleton (INavigationService), que
        // es justamente lo que evita que esta instancia "muerta" quede reaccionando
        // indefinidamente a navegaciones de la sesión siguiente.
        Assert.Same(otroVm, vm.CurrentContent);
    }

    [Fact]
    public void Desconectar_LlamadoDosVeces_NoLanzaExcepcion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.Desconectar();
        var ex = Record.Exception(() => vm.Desconectar());

        Assert.Null(ex);
    }

    // ── F5d Task 10 — Navegación a Importar planillas ────────────────────────

    [Fact]
    public void NavImportacion_LlamaNavegar_AImportacionViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>(), Times.Once);
    }

    [Fact]
    public void NavImportacion_EstableceSeccionActiva_Importacion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        Assert.Equal("Importacion", vm.SeccionActiva);
    }
}
