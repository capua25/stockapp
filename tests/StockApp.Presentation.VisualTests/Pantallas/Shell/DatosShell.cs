using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.VisualTests.Pantallas.Shell;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/shell/sidebar-*.html. CurrentContent queda null: el área de
/// contenido es fondo app en la app y en el mockup.
/// </summary>
public static class DatosShell
{
    public static ShellMainViewModel Admin() =>
        Crear(new UsuarioSesion(1, "mperez", RolUsuario.Admin, "María Pérez"), [], "Productos");

    public static ShellMainViewModel OperadorMixto() =>
        Crear(new UsuarioSesion(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez"),
            [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas],
            "Gastos");

    /// <summary>
    /// Shell con sesión fija. <paramref name="contenido"/>: el VM de la pantalla de contenido (lo resuelve
    /// ViewLocator, como en la app); null deja el área de contenido en fondo app (comparación del sidebar).
    /// </summary>
    public static ShellMainViewModel Crear(UsuarioSesion usuario, string[] permisos, string seccionActiva, ViewModelBase? contenido = null)
    {
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));
        var vm = new ShellMainViewModel(sesion.Object, Mock.Of<INavigationService>(), Mock.Of<IInfoApp>(i => i.Version == "0.4.0"),
            Mock.Of<IConfirmacionService>(), Mock.Of<IAuthService>(), Mock.Of<IServicioPreferenciasSidebar>());
        vm.SeccionActiva = seccionActiva;
        vm.CurrentContent = contenido;
        return vm;
    }
}
