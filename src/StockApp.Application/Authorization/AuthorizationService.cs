using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación de <see cref="IAuthorizationService"/>. Admin tiene acceso a todo. Para
/// Operador, cuatro permisos son estructuralmente Admin-only (nunca se resuelven contra
/// PermisoUsuario) y los 11 restantes se resuelven contra ICurrentSession.PermisosActuales,
/// ya poblado por el middleware de la Task 8 antes de que cualquier servicio de Application
/// se ejecute (spec 2026-08-10).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    /// <summary>
    /// Los 4 permisos que NUNCA se resuelven contra PermisoUsuario: Admin los tiene siempre,
    /// Operador nunca, sin consultar la tabla ni la cache. Punto de falla más peligroso del
    /// diseño (spec, "Riesgos") — el corte tiene que pasar ANTES de mirar PermisosActuales,
    /// nunca después. Compartida por AuthorizationService.Verificar y PermisoAuthorizationHandler
    /// (Task 7) — una sola fuente de verdad.
    /// </summary>
    public static readonly IReadOnlySet<string> PermisosEstructuralesAdmin = new HashSet<string>
    {
        Permisos.GestionarUsuarios,
        Permisos.ImportarPlanillas,
        Permisos.GestionarDiagnostico,
        Permisos.AdministrarTareas,
    };

    /// <summary>
    /// Los 11 permisos configurables: Permisos.Todos menos los 4 estructurales. Derivado, no
    /// una lista aparte a mano — evita que ambas listas diverjan si algún día se agrega un
    /// permiso nuevo a Permisos.Todos sin decidir explícitamente su categoría acá.
    /// </summary>
    public static readonly IReadOnlyList<string> PermisosConfigurables =
        Permisos.Todos.Where(p => !PermisosEstructuralesAdmin.Contains(p)).ToList();

    /// <summary>
    /// Plantilla de arranque para Operadores nuevos (spec decisión 3): reemplaza a la vieja
    /// AccionesOperador privada. Orden fijo y explícito — no depende del orden de iteración de
    /// un HashSet (mismo criterio que la corrección al backfill de LotesImportacion, af4321b).
    /// Consumida por el backfill de la migración (Task 1, vía PermisoUsuarioBackfillSql, que
    /// tiene esta MISMA lista transcripta a SQL) y por UsuarioService.AltaUsuarioAsync (Task 11).
    /// </summary>
    public static readonly IReadOnlyList<string> PermisosInicialesOperador =
    [
        Permisos.GestionarProductos,
        Permisos.RegistrarMovimientos,
        Permisos.RecalcularStock,
        Permisos.VerFinanzas,
        Permisos.GestionarMaestrosFinanzas,
        Permisos.RegistrarGastos,
        Permisos.RegistrarPagos,
        Permisos.RegistrarIngresos,
        Permisos.GestionarTareas,
    ];

    public void Verificar(ICurrentSession sesion, string accion)
    {
        if (!sesion.EstaAutenticado)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (sesion.RolActual == RolUsuario.Admin)
            return;

        // Corte ANTES de mirar PermisosActuales (Global Constraints): un Operador nunca pasa
        // acá, sin importar qué haya en la sesión.
        if (PermisosEstructuralesAdmin.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");

        if (!sesion.PermisosActuales.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }
}
