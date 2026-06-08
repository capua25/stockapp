using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class LogAuditoria
{
    public int Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public DateTime Fecha { get; set; }
    public AccionAuditada Accion { get; set; }
    public string Entidad { get; set; } = string.Empty;   // ej: "Producto", "Usuario"
    public int EntidadId { get; set; }
    public string Detalle { get; set; } = string.Empty;   // ej: "PrecioVenta 100,00 → 120,00"
}
