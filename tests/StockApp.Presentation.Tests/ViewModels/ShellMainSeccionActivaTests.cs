using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

/// <summary>
/// La sección activa del sidebar sigue a la navegación real (INavigationService.Cambiado), no solo a los
/// comandos del sidebar: desde Inicio (accesos, filas de tarea, "Ver todas", calendario) y al loguear.
/// </summary>
public class ShellMainSeccionActivaTests
{
    /// <summary>Navegación real con VMs sin construir: el shell solo mira el tipo del contenido.</summary>
    private static NavigationService NavegacionReal() =>
        new(tipo => RuntimeHelpers.GetUninitializedObject(tipo));

    private static ShellMainViewModel Crear(INavigationService navegacion, RolUsuario rol, params string[] permisos)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        return new ShellMainViewModel(sesion.Object, navegacion, Mock.Of<IInfoApp>(i => i.Version == "0.0.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
    }

    [Fact]
    public void AlLoguear_InicioQuedaActivo()
    {
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Admin);
        Assert.Null(vm.SeccionActiva); // precondición: nadie tocó el sidebar

        // Lo que hace ShellViewModel tras el login: Navegar<InicioViewModel>().
        navegacion.Navegar<InicioViewModel>();

        Assert.Equal("Inicio", vm.SeccionActiva);
        Assert.True(vm.ItemInicio.EstaActivo);
    }

    public static TheoryData<Type, string> DestinosDeInicio => new()
    {
        { typeof(ProductoListViewModel), "Productos" },
        { typeof(EntradaRegistroViewModel), "RegistrarEntrada" },
        { typeof(SalidaRegistroViewModel), "RegistrarSalida" },
        { typeof(MovimientoHistorialViewModel), "HistorialMovimientos" },
        { typeof(ValorizacionViewModel), "Valorizacion" },
        { typeof(AuditoriaLogViewModel), "AuditoriaLog" },
        { typeof(CalendarioPagosViewModel), "CalendarioPagos" },
        { typeof(TareaListViewModel), "Tareas" },
        { typeof(TareaFormViewModel), "Tareas" },
    };

    [Theory]
    [MemberData(nameof(DestinosDeInicio))]
    public void NavegarDesdeInicio_MarcaLaSeccionDelDestino(Type destino, string seccion)
    {
        var navegacion = new Mock<INavigationService>();
        var vm = Crear(navegacion.Object, RolUsuario.Admin);
        vm.NavInicioCommand.Execute(null);
        Assert.Equal("Inicio", vm.SeccionActiva);

        // Lo que hacen los comandos de InicioViewModel: Navegar<Destino>() sin pasar por el sidebar.
        navegacion.SetupGet(n => n.Actual).Returns((ViewModelBase)RuntimeHelpers.GetUninitializedObject(destino));
        navegacion.Raise(n => n.Cambiado += null);

        Assert.Equal(seccion, vm.SeccionActiva);
        Assert.False(vm.ItemInicio.EstaActivo);
        var item = Assert.Single(vm.Grupos.SelectMany(g => g.Items), i => i.EstaActivo);
        Assert.Equal(seccion, item.Seccion);
    }

    [Fact]
    public void NavegarAUnaPantallaSinItemPropio_ConservaLaSeccion()
    {
        // Un formulario abierto desde un listado (Productos → ProductoForm) sigue marcando el listado.
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Admin);
        navegacion.Navegar<ProductoListViewModel>();

        navegacion.Navegar<ProductoFormViewModel>();

        Assert.Equal("Productos", vm.SeccionActiva);
    }

    [Fact]
    public void OperadorMixto_NavegarAUnaSeccionSinPermiso_NoRompeYNoMarcaInicio()
    {
        // Gate: el ítem de Valorización está oculto para este operador. La sección se marca igual (el ítem
        // invisible queda activo, sin efecto en pantalla) y ningún ítem visible queda marcado por error.
        var navegacion = NavegacionReal();
        var vm = Crear(navegacion, RolUsuario.Operador, Permisos.GestionarProductos, Permisos.GestionarTareas);
        navegacion.Navegar<InicioViewModel>();

        navegacion.Navegar<ValorizacionViewModel>();

        Assert.Equal("Valorizacion", vm.SeccionActiva);
        Assert.False(vm.ItemInicio.EstaActivo);
        Assert.DoesNotContain(vm.Grupos.SelectMany(g => g.Items), i => i.EsVisible && i.EstaActivo);
    }

    [Fact]
    public void CadaItemDelSidebar_SuPantallaMarcaSuSeccion()
    {
        // La tabla pantalla → sección cubre todos los ítems: llegar a la pantalla de cada ítem por fuera del
        // sidebar (como desde Inicio) lo marca a él. El destino de cada ítem se toma de su propio comando.
        var navegacion = new Mock<INavigationService>();
        var vm = Crear(navegacion.Object, RolUsuario.Admin);
        var items = vm.Grupos.SelectMany(g => g.Items).Append(vm.ItemInicio)
            // Importar planillas está deshabilitado (ImportacionHabilitada): su comando no navega.
            .Where(i => i.Seccion != "Importacion" || ShellMainViewModel.ImportacionHabilitada)
            .ToList();
        Assert.True(items.Count > 20);

        foreach (var item in items)
        {
            Type? destino = null;
            var otra = Crear(new NavegacionQueAnotaElTipo(t => destino = t), RolUsuario.Admin);
            otra.Grupos.SelectMany(g => g.Items).Append(otra.ItemInicio).Single(i => i.Seccion == item.Seccion).Comando.Execute(null);
            Assert.NotNull(destino);

            vm.SeccionActiva = null;
            navegacion.SetupGet(n => n.Actual).Returns((ViewModelBase)RuntimeHelpers.GetUninitializedObject(destino));
            navegacion.Raise(n => n.Cambiado += null);

            Assert.True(item.Seccion == vm.SeccionActiva, $"{destino.Name} marca {vm.SeccionActiva ?? "nada"}, no {item.Seccion}");
        }
    }

    private sealed class NavegacionQueAnotaElTipo(Action<Type> anotar) : INavigationService
    {
        public ViewModelBase? Actual => null;
        public event Action? Cambiado { add { } remove { } }
        public void Navegar<TVm>() where TVm : ViewModelBase => anotar(typeof(TVm));
        public void Navegar<TVm>(Action<TVm> inicializar) where TVm : ViewModelBase => anotar(typeof(TVm));
    }
}
