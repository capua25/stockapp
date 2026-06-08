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

    /// <summary>Proyecta <paramref name="usuario"/> a un snapshot <see cref="UsuarioSesion"/> y lo establece como sesión activa.</summary>
    void IniciarSesion(Usuario usuario);

    /// <summary>Limpia la sesión. La app sigue corriendo; es necesario loguearse de nuevo.</summary>
    void CerrarSesion();
}
