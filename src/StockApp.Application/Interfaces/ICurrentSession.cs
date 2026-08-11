using StockApp.Application.Auth;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Estado de la sesión actual en memoria. Se registra como singleton en el contenedor DI.
/// No persiste entre reinicios de la app (eso es intencional: cada arranque requiere login).
/// </summary>
public interface ICurrentSession
{
    /// <summary>true si hay un usuario logueado.</summary>
    bool EstaAutenticado { get; }

    /// <summary>Snapshot de identidad del usuario actual, o null si no hay sesión.</summary>
    UsuarioSesion? UsuarioActual { get; }

    /// <summary>Atajo: rol del usuario actual, o null si no hay sesión.</summary>
    RolUsuario? RolActual { get; }

    /// <summary>
    /// Permisos configurables efectivos del usuario actual (spec 2026-08-10). Vacío si no hay
    /// sesión o si el usuario no tiene ninguno concedido — nunca null. Para Admin, quien la
    /// puebla (el middleware del lado API, o ApiSession del lado desktop) puede optar por
    /// dejarlo vacío igual: AuthorizationService.Verificar nunca consulta este set para Admin,
    /// así que su contenido es irrelevante en ese caso.
    /// </summary>
    IReadOnlySet<string> PermisosActuales { get; }

    /// <summary>Puebla PermisosActuales. Llamado una vez por request (HttpCurrentSession, vía el
    /// middleware nuevo) o tras login/refresh (ApiSession).</summary>
    void EstablecerPermisos(IReadOnlySet<string> permisos);

    /// <summary>Proyecta <paramref name="usuario"/> a un snapshot <see cref="UsuarioSesion"/> y lo establece como sesión activa.</summary>
    void IniciarSesion(Usuario usuario);

    /// <summary>Limpia la sesión. La app sigue corriendo; es necesario loguearse de nuevo.</summary>
    void CerrarSesion();
}
