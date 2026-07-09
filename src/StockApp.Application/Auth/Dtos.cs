using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// DTO de lectura de Usuario para GET /usuarios (Fase 2b). Nunca incluye
/// HashContrasena — ese campo no sale de la capa de aplicación.
/// </summary>
public record UsuarioDto(
    int Id,
    string NombreUsuario,
    string? NombreCompleto,
    RolUsuario Rol,
    bool Activo,
    DateTime FechaAlta);
