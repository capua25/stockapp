using StockApp.Application.Interfaces;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización. Cada servicio de Application llama a <see cref="Verificar"/> al
/// inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que la sesión completa (rol + permisos configurables ya resueltos por el
    /// middleware, spec 2026-08-10) puede ejecutar <paramref name="accion"/>. SINCRÓNICO:
    /// no hace ningún SELECT, lee sesion.PermisosActuales, ya poblado antes de que cualquier
    /// servicio de Application se ejecute.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Sin sesión, o sin el permiso requerido.</exception>
    void Verificar(ICurrentSession sesion, string accion);
}
