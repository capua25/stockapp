using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.VisualTests.Pantallas.Shell;

namespace StockApp.Presentation.VisualTests.Pantallas.Inicio;

/// <summary>
/// Datos fijos de docs/ui-v2/mockups/inicio/*.html. Reloj fijo (martes 6 de octubre de 2026, 13:00, zona UTC):
/// subtítulo, días de cada tarea y hora del backup no cambian con el día ni con la PC (convención 8).
/// </summary>
public static class DatosInicio
{
    public static readonly DateTimeOffset Ahora = new(2026, 10, 6, 13, 0, 0, TimeSpan.Zero);

    public static readonly UsuarioSesion UsuarioAdmin = new(1, "mperez", RolUsuario.Admin, "María Pérez");
    public static readonly UsuarioSesion UsuarioOperadorMixto = new(2, "jrodriguez", RolUsuario.Operador, "Juan Rodríguez");
    public static readonly UsuarioSesion UsuarioSinFinanzasNiTareas = new(3, "lsilva", RolUsuario.Operador, "Laura Silva");

    public static readonly string[] PermisosMixtos =
        [Permisos.GestionarProductos, Permisos.RegistrarMovimientos, Permisos.VerFinanzas, Permisos.GestionarTareas];

    public static readonly string[] PermisosSinFinanzasNiTareas = [Permisos.GestionarProductos, Permisos.VerReportes];

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Ahora;
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    private static DateTime Dia(int desplazamiento) =>
        new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc).AddDays(desplazamiento);

    private static Tarea Tarea(int id, string titulo, int dias) => new()
    {
        Id = id, Titulo = titulo, Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = Dia(-30), FechaLimite = Dia(dias),
    };

    private static CalendarioPagosDto Calendario(int vencidas, int aVencer)
    {
        FacturaCalendarioDto Factura(int id, int dias) =>
            new(id, "Proveedor de prueba", $"A-{id:0000}", 1000m, DateOnly.FromDateTime(Dia(dias)), "Pendiente");
        return new CalendarioPagosDto(
            Enumerable.Range(1, vencidas).Select(i => Factura(i, -5)).ToList(),
            Enumerable.Range(100, aVencer).Select(i => Factura(i, 3)).ToList(),
            [], []);
    }

    /// <summary>calendario, tareas o salud en null: ese servicio falla ("API caída").</summary>
    public static InicioViewModel Inicio(UsuarioSesion usuario, string[] permisos,
        CalendarioPagosDto? calendario, IReadOnlyList<Tarea>? tareas, SaludBackupDto? salud)
    {
        var caida = new HttpRequestException("API caída");
        var sesion = new Mock<ICurrentSession>();
        sesion.Setup(s => s.UsuarioActual).Returns(usuario);
        sesion.Setup(s => s.RolActual).Returns(usuario.Rol);
        sesion.Setup(s => s.PermisosActuales).Returns(new HashSet<string>(permisos));

        var finanzas = new Mock<IFinanzasVistasService>();
        if (calendario is null)
        {
            finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ThrowsAsync(caida);
        }
        else
        {
            finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(It.IsAny<DateTime?>())).ReturnsAsync(calendario);
        }

        var servicioTareas = new Mock<ITareaService>();
        if (tareas is null)
        {
            servicioTareas.Setup(t => t.ListarAsync()).ThrowsAsync(caida);
        }
        else
        {
            servicioTareas.Setup(t => t.ListarAsync()).ReturnsAsync(tareas);
        }

        var backups = new Mock<IBackupsService>();
        if (salud is null)
        {
            backups.Setup(b => b.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ThrowsAsync(caida);
        }
        else
        {
            backups.Setup(b => b.ObtenerSaludAsync(It.IsAny<CancellationToken>())).ReturnsAsync(salud);
        }

        var auth = new Mock<IAuthService>();
        auth.Setup(a => a.ObtenerPermisosPropiosAsync()).ReturnsAsync((IReadOnlySet<string>)new HashSet<string>(permisos));

        return new InicioViewModel(sesion.Object, Mock.Of<INavigationService>(), finanzas.Object, backups.Object,
            servicioTareas.Object, auth.Object, new RelojFijo());
    }

    private static readonly SaludBackupDto BackupVencido = new(new DateTime(2026, 10, 4, 14, 32, 0, DateTimeKind.Utc), true, 26);
    private static readonly SaludBackupDto BackupAlDia = new(new DateTime(2026, 10, 6, 3, 0, 0, DateTimeKind.Utc), false, 26);

    public static ShellMainViewModel Admin() =>
        DatosShell.Crear(UsuarioAdmin, [], "Inicio", Inicio(UsuarioAdmin, [], Calendario(0, 3),
            [
                Tarea(1, "Reparar bache en Av. 18 de Julio", -2),
                Tarea(2, "Retirar poda zona Rambla", -1),
                Tarea(3, "Inspección de alumbrado público", 2),
                Tarea(4, "Mantenimiento plaza Independencia", 3),
            ],
            BackupVencido));

    public static ShellMainViewModel OperadorMixto() =>
        DatosShell.Crear(UsuarioOperadorMixto, PermisosMixtos, "Inicio", Inicio(UsuarioOperadorMixto, PermisosMixtos, Calendario(1, 2),
            [Tarea(5, "Controlar stock del depósito", -3), Tarea(6, "Recibir pedido de luminarias", 0)],
            BackupAlDia));

    public static ShellMainViewModel OperadorSinFinanzasNiTareas() =>
        DatosShell.Crear(UsuarioSinFinanzasNiTareas, PermisosSinFinanzasNiTareas, "Inicio",
            Inicio(UsuarioSinFinanzasNiTareas, PermisosSinFinanzasNiTareas, Calendario(0, 0), [], BackupAlDia));

    public static ShellMainViewModel AdminSinDatos() =>
        DatosShell.Crear(UsuarioAdmin, [], "Inicio", Inicio(UsuarioAdmin, [], null, null, null));
}
