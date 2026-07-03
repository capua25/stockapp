using Moq;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// Tarea E1 (Inc 6): navegación al grupo "Reportes" desde el ShellMainViewModel,
/// visible solo para Admin. Mismo patrón de visibilidad por rol (EsAdmin) y de
/// navegación (INavigationService.Navegar) que las entradas existentes del Inc 5.
/// </summary>
public class ShellMainViewModelReportesTests
{
    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock)
        Crear(RolUsuario rol)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);

        var navMock = new Mock<INavigationService>();

        var vm = new ShellMainViewModel(sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
        return (vm, sessionMock, navMock);
    }

    // ── E1 — Visibilidad del grupo Reportes (solo Admin) ──────────────────────

    [Fact]
    public void Admin_VeEntradasGrupoReportes()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.True(vm.EsAdmin);
    }

    [Fact]
    public void Operador_NoVeEntradasGrupoReportes()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        Assert.False(vm.EsAdmin);
    }

    // ── E1 — Navegación a los 5 reportes ──────────────────────────────────────

    [Fact]
    public void NavReportes_LlamaNavegar_ConViewModelCorrecto()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavValorizacionCommand.Execute(null);
        vm.NavStockCategoriaCommand.Execute(null);
        vm.NavHistorialPorProductoCommand.Execute(null);
        vm.NavMasMovidosCommand.Execute(null);
        vm.NavAuditoriaLogCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ValorizacionViewModel>(),        Times.Once);
        navMock.Verify(n => n.Navegar<StockCategoriaViewModel>(),      Times.Once);
        navMock.Verify(n => n.Navegar<HistorialPorProductoViewModel>(), Times.Once);
        navMock.Verify(n => n.Navegar<MasMovidosViewModel>(),          Times.Once);
        navMock.Verify(n => n.Navegar<AuditoriaLogViewModel>(),        Times.Once);
    }
}
