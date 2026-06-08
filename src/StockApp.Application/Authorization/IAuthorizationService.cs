using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización por rol. Cada servicio de Application llama a
/// <see cref="Verificar"/> al inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que <paramref name="rolActual"/> puede ejecutar <paramref name="accion"/>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso o no hay sesión.</exception>
    void Verificar(RolUsuario? rolActual, string accion);
}
