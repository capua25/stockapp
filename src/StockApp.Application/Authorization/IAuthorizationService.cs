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

    /// <summary>
    /// Igual que <see cref="Verificar"/> pero sin lanzar: devuelve si <paramref name="rol"/>
    /// puede ejecutar <paramref name="accion"/>, consultando la misma tabla rol→permiso.
    /// Usado por StockApp.Api/Program.cs (Fase 2b, D1) para derivar las políticas de
    /// autorización HTTP a partir de esta única fuente de verdad, en vez de declararlas
    /// a mano por recurso.
    /// </summary>
    bool TienePermiso(RolUsuario rol, string accion);
}
