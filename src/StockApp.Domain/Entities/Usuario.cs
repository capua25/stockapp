using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public string NombreUsuario { get; set; } = string.Empty;   // único, obligatorio
    public string? NombreCompleto { get; set; }
    public string HashContrasena { get; set; } = string.Empty;  // BCrypt; nunca texto plano
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime FechaAlta { get; set; }
    public DateTime? UltimoAcceso { get; set; }
}
