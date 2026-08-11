using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// Tarea E1 (Inc 6): navegación al grupo "Reportes" desde el ShellMainViewModel.
///
/// La visibilidad del grupo (review Ronda 1, Task 14, spec 2026-08-10): dejó de depender de
/// EsAdmin/rol y pasó a depender de PuedeVerReportes/permiso configurable VerReportes. Ese
/// contrato ya se prueba de forma completa en ShellMainViewModelTests.cs — Theories
/// Admin_TodasLasPropiedadesPuede_SonTrue / Operador_ConElPermisoEnPermisosActuales_LaPropiedadEsTrue /
/// Operador_SinNingunPermisoEnPermisosActuales_LaPropiedadEsFalse, con `PuedeVerReportes` como uno
/// de los 7 casos de cada Theory — por eso este archivo ya NO prueba visibilidad (los dos Facts
/// que asertaban EsAdmin quedaron redundantes con esas Theories y, peor, dejaron de validar lo
/// que realmente controla el IsVisible real del grupo). Este archivo solo prueba la navegación
/// (INavigationService.Navegar) a los 5 reportes, algo que las Theories de arriba no cubren.
/// </summary>
public class ShellMainViewModelReportesTests
{
    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock)
        Crear(RolUsuario rol)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);

        var navMock = new Mock<INavigationService>();

        var vm = new ShellMainViewModel(
            sessionMock.Object, navMock.Object, Mock.Of<IInfoApp>(x => x.Version == "0.0.0"), Mock.Of<IConfirmacionService>(),
            Mock.Of<IAuthService>());
        return (vm, sessionMock, navMock);
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
