using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

public class MovimientoStock
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }
    public int UsuarioId { get; set; }
    public Usuario? Usuario { get; set; }
    public TipoMovimiento Tipo { get; set; }
    public decimal Cantidad { get; set; }           // siempre positiva; Tipo define el signo
    public decimal PrecioUnitario { get; set; }     // precio del momento (costo o venta)
    public DateTime Fecha { get; set; }
    public MotivoMovimiento Motivo { get; set; }
    public string? Comentario { get; set; }
}
