using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// Snapshot inmutable de identidad post-login. Nunca contiene el hash de contraseña
/// ni referencias a entidades EF (evita referencias stale al change tracker).
/// </summary>
public record UsuarioSesion(
    int Id,
    string NombreUsuario,
    RolUsuario Rol,
    string? NombreCompleto);
