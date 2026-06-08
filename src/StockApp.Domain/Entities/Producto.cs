namespace StockApp.Domain.Entities;

public class Producto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;   // SKU interno, único, obligatorio
    public string? CodigoBarras { get; set; }            // EAN opcional; único cuando no es nulo
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public int? CategoriaId { get; set; }
    public Categoria? Categoria { get; set; }
    public int? ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }
    public int UnidadMedidaId { get; set; }
    public UnidadMedida? UnidadMedida { get; set; }
    public decimal PrecioCosto { get; set; }
    public decimal PrecioVenta { get; set; }
    public decimal StockActual { get; set; }             // saldo denormalizado; ver §6 del spec
    public decimal StockMinimo { get; set; }             // previsto para alertas futuras; default 0
    public bool Activo { get; set; } = true;
    public DateTime FechaAlta { get; set; }
}
