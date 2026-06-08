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

    /// <summary>El usuario actual, o null si no hay sesión.</summary>
    Usuario? UsuarioActual { get; }

    /// <summary>Atajo: rol del usuario actual, o null si no hay sesión.</summary>
    RolUsuario? RolActual { get; }

    /// <summary>Establece <paramref name="usuario"/> como usuario activo de la sesión.</summary>
    void IniciarSesion(Usuario usuario);

    /// <summary>Limpia la sesión. La app sigue corriendo; es necesario loguearse de nuevo.</summary>
    void CerrarSesion();
}
