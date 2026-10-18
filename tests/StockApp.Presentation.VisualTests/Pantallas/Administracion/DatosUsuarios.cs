using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Administracion;
using StockApp.Presentation.VisualTests.Pantallas.Inventario;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Administracion;

/// <summary>Datos fijos de docs/ui-v2/mockups/administracion/usuarios*.html (textos fijados en DatosAdministracionTests).
/// Las instancias de la lista son siempre las mismas: la vista recarga la lista y la selección sigue apuntando a una fila.</summary>
public static class DatosUsuarios
{
    public static readonly UsuarioDto Admin = new(1, "mperez", "María Pérez", RolUsuario.Admin, true, new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc));
    public static readonly UsuarioDto Operador = new(2, "jrodriguez", "Juan Rodríguez", RolUsuario.Operador, true, new DateTime(2026, 3, 2, 0, 0, 0, DateTimeKind.Utc));
    public static readonly UsuarioDto Lucia = new(3, "lgomez", "Lucía Gómez", RolUsuario.Operador, true, new DateTime(2026, 5, 20, 0, 0, 0, DateTimeKind.Utc));
    public static readonly UsuarioDto Inactivo = new(4, "psosa", "Pedro Sosa", RolUsuario.Operador, false, new DateTime(2026, 2, 8, 0, 0, 0, DateTimeKind.Utc));

    public static readonly IReadOnlyList<UsuarioDto> Usuarios = [Admin, Operador, Lucia, Inactivo];

    /// <summary>Permisos tildados del operador seleccionado: mezcla de grupos, y uno con advertencia de dependencia blanda
    /// (registra movimientos pero no gestiona productos).</summary>
    public static readonly IReadOnlyList<string> PermisosDelOperador =
        [Permisos.RegistrarMovimientos, Permisos.GestionarTablasMaestras, Permisos.VerFinanzas, Permisos.GestionarTareas];

    private static UsuariosAdminViewModel Armar(Action<Mock<IUsuarioService>>? configurar = null)
    {
        var servicio = new Mock<IUsuarioService>();
        servicio.Setup(s => s.ListarAsync()).ReturnsAsync(Usuarios);
        servicio.Setup(s => s.ObtenerPermisosAsync(It.IsAny<int>())).ReturnsAsync(PermisosDelOperador);
        configurar?.Invoke(servicio);
        var confirmacion = Mock.Of<IConfirmacionService>();
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>());
        return new UsuariosAdminViewModel(servicio.Object, confirmacion, new PanelPermisosViewModel(servicio.Object, confirmacion), sesion.Object);
    }

    private static ShellMainViewModel EnShell(UsuariosAdminViewModel vm) =>
        DatosShell.Crear(DatosProductos.UsuarioAdmin, [], "Usuarios", vm);

    private static ShellMainViewModel ConSeleccion(UsuarioDto usuario)
    {
        var vm = Armar();
        vm.Items.Add(usuario);
        vm.UsuarioSeleccionado = usuario;
        vm.PanelPermisos.CargarAsync().GetAwaiter().GetResult();
        return EnShell(vm);
    }

    public static ShellMainViewModel SinSeleccion() => EnShell(Armar());
    public static ShellMainViewModel OperadorSeleccionado() => ConSeleccion(Operador);
    public static ShellMainViewModel AdminSeleccionado() => ConSeleccion(Admin);
    public static ShellMainViewModel InactivoSeleccionado() => ConSeleccion(Inactivo);

    /// <summary>Alta con un usuario que ya existe: el mensaje de la regla de negocio sale en la Alerta de la card de alta.</summary>
    public static ShellMainViewModel AltaConError()
    {
        var vm = Armar(s => s
            .Setup(x => x.AltaUsuarioAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<RolUsuario>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe un usuario con ese nombre.")));
        vm.NuevoNombreUsuario = "jrodriguez";
        vm.NuevoNombreCompleto = "Juan Rodríguez";
        vm.NuevaContrasenaPlan = "Clave1234";
        vm.AltaCommand.ExecuteAsync(null).GetAwaiter().GetResult();
        return EnShell(vm);
    }
}
