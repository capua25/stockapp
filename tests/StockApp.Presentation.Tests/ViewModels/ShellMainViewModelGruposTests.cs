using System.Collections.Generic;
using System.Linq;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// Grupos colapsables del sidebar (Tanda 5, Task 5.2). Hasta esta tanda no existía el concepto
/// de grupo en ShellMainViewModel: los "grupos" de hoy son 7 TextBlock sueltos con su propio
/// IsVisible. Estos tests custodian el modelo nuevo (GrupoNavegacion / ItemNavegacion) y la
/// persistencia de expansión, ANTES de que el XAML de la Task 5.3 los bindee.
/// </summary>
public class ShellMainViewModelGruposTests
{
    private static (ShellMainViewModel vm, Mock<ICurrentSession> sessionMock, Mock<IServicioPreferenciasSidebar> preferenciasMock)
        Crear(RolUsuario rol, IEnumerable<string>? permisos = null, PreferenciasSidebar? preferenciasGuardadas = null)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.RolActual).Returns(rol);
        sessionMock.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos ?? Enumerable.Empty<string>()));

        var preferenciasMock = new Mock<IServicioPreferenciasSidebar>();
        preferenciasMock.Setup(p => p.Cargar()).Returns(preferenciasGuardadas);

        var vm = new ShellMainViewModel(
            sessionMock.Object,
            Mock.Of<INavigationService>(),
            Mock.Of<IInfoApp>(x => x.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(),
            Mock.Of<IAuthService>(),
            preferenciasMock.Object);

        return (vm, sessionMock, preferenciasMock);
    }

    [Fact]
    public void Grupos_ParaUnAdmin_TieneLosOchoGrupos()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.Equal(
            new[] { "Movimientos", "Tareas", "Documentos", "Finanzas", "Importación", "Tablas maestras", "Reportes", "Administración" },
            vm.Grupos.Select(g => g.Titulo));
    }

    [Fact]
    public void Grupos_ParaUnOperadorSinPermisos_NingunoEsVisible()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        Assert.All(vm.Grupos, g => Assert.False(g.EsVisible));
    }

    [Fact]
    public void Grupo_EsVisible_SiAlgunItemEsVisible()
    {
        // Bug preexistente que esta tanda arregla de paso: el header "Finanzas" iba por
        // PuedeVerFinanzas mientras "Maestros de finanzas" va por PuedeGestionarMaestrosFinanzas
        // — un Operador con SOLO este último permiso veía el botón sin título de sección.
        var (vm, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.GestionarMaestrosFinanzas });

        var finanzas = vm.Grupos.Single(g => g.Titulo == "Finanzas");

        Assert.True(finanzas.EsVisible);
        Assert.Single(finanzas.ItemsVisibles);
        Assert.Equal("MaestrosFinanzas", finanzas.ItemsVisibles[0].Seccion);
    }

    [Fact]
    public void Grupos_ConPreferenciasGuardadas_RestauraLosAbiertos()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin, preferenciasGuardadas: new PreferenciasSidebar(new[] { "Finanzas" }));

        foreach (var grupo in vm.Grupos)
        {
            Assert.Equal(grupo.Titulo == "Finanzas", grupo.EstaExpandido);
        }
    }

    [Fact]
    public void Grupos_SinPreferenciasGuardadas_ArrancanTodosCerrados()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.All(vm.Grupos, g => Assert.False(g.EstaExpandido));
    }

    [Fact]
    public void AlternarGrupo_GuardaLaPreferencia()
    {
        var (vm, _, preferenciasMock) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "Finanzas");

        vm.AlternarGrupoCommand.Execute(finanzas);

        preferenciasMock.Verify(
            p => p.Guardar(It.Is<PreferenciasSidebar>(pref => pref.GruposAbiertos.Contains("Finanzas"))),
            Times.Once);
    }

    [Fact]
    public void AlternarGrupo_DosVeces_LoSacaDeLaPreferencia()
    {
        var (vm, _, preferenciasMock) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "Finanzas");

        vm.AlternarGrupoCommand.Execute(finanzas);
        vm.AlternarGrupoCommand.Execute(finanzas);

        preferenciasMock.Verify(
            p => p.Guardar(It.Is<PreferenciasSidebar>(pref => !pref.GruposAbiertos.Contains("Finanzas"))),
            Times.Once);
    }

    [Fact]
    public void Navegar_AUnaSeccion_AutoabreSuGrupo()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "Finanzas");
        Assert.False(finanzas.EstaExpandido);

        vm.NavGastosCommand.Execute(null);

        Assert.True(finanzas.EstaExpandido);
    }

    [Fact]
    public void AbrirUnGrupo_NoCierraLosOtros()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "Finanzas");
        var reportes = vm.Grupos.Single(g => g.Titulo == "Reportes");

        vm.AlternarGrupoCommand.Execute(finanzas);
        vm.AlternarGrupoCommand.Execute(reportes);

        Assert.True(finanzas.EstaExpandido);
        Assert.True(reportes.EstaExpandido);
    }

    [Fact]
    public void Grupos_ConPreferenciaDeUnGrupoQueYaNoExiste_NoTira()
    {
        var ex = Record.Exception(() =>
            Crear(RolUsuario.Admin, preferenciasGuardadas: new PreferenciasSidebar(new[] { "GrupoViejo" })));

        Assert.Null(ex);
    }
}
