using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización. Cada servicio de Application llama a <see cref="Verificar"/> al
/// inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que <paramref name="rolActual"/> puede ejecutar <paramref name="accion"/>.
    /// OBSOLETO (spec 2026-08-10): reemplazado por el overload que recibe ICurrentSession
    /// completo. Se mantiene temporalmente mientras los ~96 call sites migran (Tasks 6a-6d);
    /// se elimina en la Task 7 junto con TienePermiso.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso o no hay sesión.</exception>
    void Verificar(RolUsuario? rolActual, string accion);

    /// <summary>
    /// Verifica que la sesión completa (rol + permisos configurables ya resueltos por el
    /// middleware, spec 2026-08-10) puede ejecutar <paramref name="accion"/>. SINCRÓNICO:
    /// no hace ningún SELECT, lee sesion.PermisosActuales, ya poblado antes de que cualquier
    /// servicio de Application se ejecute.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Sin sesión, o sin el permiso requerido.</exception>
    void Verificar(ICurrentSession sesion, string accion);

    /// <summary>
    /// Igual que <see cref="Verificar(RolUsuario?, string)"/> pero sin lanzar. OBSOLETO:
    /// único consumidor es Program.cs (deriva rolesPermitidos al arrancar); desaparece en la
    /// Task 7 junto con ese código.
    /// </summary>
    bool TienePermiso(RolUsuario rol, string accion);
}
