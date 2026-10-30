using System.Collections.Generic;
using System.ComponentModel;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ReportesViewModelTests
{
    private static (ReportesViewModel vm, Mock<ICurrentSession> sesion, Mock<INavigationService> nav) Crear(
        RolUsuario rol, IReadOnlySet<string>? permisos = null)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(rol);
        sesion.Setup(s => s.PermisosActuales).Returns(permisos ?? new HashSet<string>());
        var nav = new Mock<INavigationService>();
        return (new ReportesViewModel(sesion.Object, nav.Object), sesion, nav);
    }

    [Theory]
    [InlineData(new[] { Permisos.VerReportes }, true, false)]
    [InlineData(new[] { Permisos.VerReportes, Permisos.RegistrarMovimientos }, true, false)]
    [InlineData(new[] { Permisos.VerReportes, Permisos.RegistrarMovimientos, Permisos.GestionarProductos }, true, true)]
    [InlineData(new[] { Permisos.RegistrarMovimientos, Permisos.GestionarProductos }, false, false)]
    public void Operador_GatesDeLasTarjetas(string[] permisos, bool verReportes, bool historial)
    {
        var (vm, _, _) = Crear(RolUsuario.Operador, new HashSet<string>(permisos));

        Assert.Equal(verReportes, vm.PuedeVerReportes);
        Assert.Equal(historial, vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Admin_VeTodasLasTarjetas()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.True(vm.PuedeVerReportes);
        Assert.True(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void Gates_SeReevaluanConLosPermisosActuales_SinRecrear()
    {
        var permisos = new HashSet<string>();
        var (vm, sesion, _) = Crear(RolUsuario.Operador);
        sesion.Setup(s => s.PermisosActuales).Returns(() => permisos);

        Assert.False(vm.PuedeVerReportes);
        Assert.False(vm.PuedeVerHistorialPorProducto);

        permisos.Add(Permisos.VerReportes);
        permisos.Add(Permisos.RegistrarMovimientos);
        permisos.Add(Permisos.GestionarProductos);

        Assert.True(vm.PuedeVerReportes);
        Assert.True(vm.PuedeVerHistorialPorProducto);
    }

    [Fact]
    public void CadaTarjeta_NavegaASuReporte()
    {
        var (vm, _, nav) = Crear(RolUsuario.Admin);

        vm.AbrirValorizacionCommand.Execute(null);
        vm.AbrirStockCategoriaCommand.Execute(null);
        vm.AbrirHistorialPorProductoCommand.Execute(null);
        vm.AbrirMasMovidosCommand.Execute(null);
        vm.AbrirAuditoriaCommand.Execute(null);
        vm.AbrirEstadisticaProyectosCommand.Execute(null);

        nav.Verify(n => n.Navegar<ValorizacionViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<StockCategoriaViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<HistorialPorProductoViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<MasMovidosViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<AuditoriaLogViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<ReporteProyectosViewModel>(), Times.Once);
    }

    [Fact]
    public void NotificarPermisosActualizados_NotificaLosGates()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);
        var notificadas = new List<string?>();
        vm.PropertyChanged += (_, e) => notificadas.Add(e.PropertyName);

        vm.NotificarPermisosActualizados();

        Assert.Contains(string.Empty, notificadas);
    }
}
