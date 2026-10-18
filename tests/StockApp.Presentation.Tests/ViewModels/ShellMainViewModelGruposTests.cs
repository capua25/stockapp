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
    public void Grupos_SonLasCincoSeccionesDeLaSpec()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.Equal(
            new[] { "INVENTARIO", "FINANZAS", "GESTIÓN", "ANÁLISIS", "ADMINISTRACIÓN" },
            vm.Grupos.Select(g => g.Titulo));
    }

    // Spec §4.1 (orden y textos) con P1 (sidebar transitorio): ANÁLISIS y ADMINISTRACIÓN transitorias
    // hasta el hub de Reportes (Fase 6) y Catálogos (Fase 7).
    [Theory]
    [InlineData("INVENTARIO", "Productos|Registrar entrada|Registrar salida|Ingreso por factura|Historial de movimientos")]
    [InlineData("FINANZAS", "Gastos y facturas|Ingresos de caja|Libro caja|Control POA|Calendario de pagos")]
    [InlineData("GESTIÓN", "Tareas|Documentos")]
    [InlineData("ANÁLISIS", "Reportes")]
    [InlineData("ADMINISTRACIÓN", "Categorías|Proveedores|Unidades de medida|Catálogos de tareas|Maestros de finanzas|Usuarios|Mantenimiento|Importar planillas")]
    public void Seccion_TieneSusItemsEnElOrdenDeLaSpec(string seccion, string titulos)
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        Assert.Equal(titulos.Split('|'), vm.Grupos.Single(g => g.Titulo == seccion).Items.Select(i => i.Titulo));
    }

    [Fact]
    public void Importacion_ExisteEnAdministracionPeroQuedaOcultaAunParaAdmin()
    {
        // ImportacionHabilitada=false (2026-09-04): el ítem existe pero no se muestra (spec §4.1).
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var administracion = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");

        Assert.Contains(administracion.Items, i => i.Seccion == "Importacion");
        Assert.DoesNotContain(administracion.ItemsVisibles, i => i.Seccion == "Importacion");
    }

    [Fact]
    public void OperadorMixto_VeInventarioFinanzasYGestion_SinAnalisisNiAdministracion()
    {
        // Gate probado con permisos mixtos, no con Admin (Admin cortocircuita el OR).
        var (vm, _, _) = Crear(RolUsuario.Operador, new[]
        {
            Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas,
        });

        Assert.Equal(new[] { "INVENTARIO", "FINANZAS", "GESTIÓN" }, vm.Grupos.Where(g => g.EsVisible).Select(g => g.Titulo));
        Assert.Equal(
            new[] { "Productos", "RegistrarEntrada", "RegistrarSalida", "HistorialMovimientos" },
            vm.Grupos.Single(g => g.Titulo == "INVENTARIO").ItemsVisibles.Select(i => i.Seccion));
        Assert.Equal(new[] { "Tareas" }, vm.Grupos.Single(g => g.Titulo == "GESTIÓN").ItemsVisibles.Select(i => i.Seccion));
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
        // Un Operador con SOLO GestionarMaestrosFinanzas: ADMINISTRACIÓN se ve con ese único ítem
        // y FINANZAS (VerFinanzas) no se ve.
        var (vm, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.GestionarMaestrosFinanzas });

        var administracion = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");
        Assert.True(administracion.EsVisible);
        Assert.Equal("MaestrosFinanzas", Assert.Single(administracion.ItemsVisibles).Seccion);
        Assert.False(vm.Grupos.Single(g => g.Titulo == "FINANZAS").EsVisible);
    }

    [Fact]
    public void Grupos_ConPreferenciasGuardadas_RestauraLosAbiertos()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin, preferenciasGuardadas: new PreferenciasSidebar(new[] { "FINANZAS" }));

        foreach (var grupo in vm.Grupos)
        {
            Assert.Equal(grupo.Titulo == "FINANZAS", grupo.EstaExpandido);
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
        var finanzas = vm.Grupos.Single(g => g.Titulo == "FINANZAS");

        vm.AlternarGrupoCommand.Execute(finanzas);

        preferenciasMock.Verify(
            p => p.Guardar(It.Is<PreferenciasSidebar>(pref => pref.GruposAbiertos.Contains("FINANZAS"))),
            Times.Once);
    }

    [Fact]
    public void AlternarGrupo_DosVeces_LoSacaDeLaPreferencia()
    {
        var (vm, _, preferenciasMock) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "FINANZAS");

        vm.AlternarGrupoCommand.Execute(finanzas);
        vm.AlternarGrupoCommand.Execute(finanzas);

        preferenciasMock.Verify(
            p => p.Guardar(It.Is<PreferenciasSidebar>(pref => !pref.GruposAbiertos.Contains("FINANZAS"))),
            Times.Once);
    }

    [Fact]
    public void Navegar_AUnaSeccion_AutoabreSuGrupo()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "FINANZAS");
        Assert.False(finanzas.EstaExpandido);

        vm.NavGastosCommand.Execute(null);

        Assert.True(finanzas.EstaExpandido);
    }

    [Fact]
    public void AbrirUnGrupo_NoCierraLosOtros()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);
        var finanzas = vm.Grupos.Single(g => g.Titulo == "FINANZAS");
        var reportes = vm.Grupos.Single(g => g.Titulo == "ANÁLISIS");

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

    [Fact]
    public void GrupoTablasMaestras_TieneElItemCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Admin);

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");

        Assert.Contains(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }

    [Fact]
    public void GrupoTablasMaestras_OperadorSinPermiso_NoVeCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador);

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");

        Assert.DoesNotContain(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }

    [Fact]
    public void GrupoTablasMaestras_OperadorConGestionarTablasMaestras_VeCatalogosTarea()
    {
        var (vm, _, _) = Crear(RolUsuario.Operador, new[] { Permisos.GestionarTablasMaestras });

        var tablasMaestras = vm.Grupos.Single(g => g.Titulo == "ADMINISTRACIÓN");

        Assert.Contains(tablasMaestras.ItemsVisibles, i => i.Seccion == "CatalogosTarea");
    }
}
