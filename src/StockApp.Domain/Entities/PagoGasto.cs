namespace StockApp.Domain.Entities;

/// <summary>
/// Pago (total o parcial) de un gasto. Hija del agregado Gasto, con baja lógica PROPIA
/// (a diferencia de AsignacionPresupuestal): anular un pago conserva la historia y
/// recalcula el estado de la factura. Contado ⇒ se crea un pago automático por el
/// total en la fecha del gasto.
/// </summary>
public class PagoGasto
{
    public int Id { get; set; }
    public int GastoId { get; set; }
    public DateTime Fecha { get; set; }        // UTC — el saldo de caja impacta ACÁ
    public decimal Monto { get; set; }         // precisión 18,4
    public string? Nota { get; set; }
    public bool Activo { get; set; } = true;   // false = pago anulado
}
