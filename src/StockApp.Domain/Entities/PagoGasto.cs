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

    /// <summary>
    /// Navegación inversa (F4, vistas calculadas): permite Include(p => p.Gasto) desde
    /// consultas que arrancan en PagosGasto (ej. libro caja, que necesita Proveedor/Rubro/
    /// Fuente del gasto dueño de cada pago). Mismo FK GastoId que ya existía — sin
    /// migración nueva, solo se reconfigura la relación en AppDbContext.OnModelCreating.
    /// </summary>
    public Gasto? Gasto { get; set; }

    public DateTime Fecha { get; set; }        // UTC — el saldo de caja impacta ACÁ
    public decimal Monto { get; set; }         // precisión 18,4
    public string? Nota { get; set; }
    public bool Activo { get; set; } = true;   // false = pago anulado

    /// <summary>
    /// Guid del lote de /confirmar que creó este pago (F5c Task 8, re-review CRITICAL/IMPORTANT
    /// 2). Null para todo pago cargado a mano (ABM manual, incluidos los que un operador registra
    /// después sobre un gasto importado) — mismo patrón que Gasto.IdImportacion/
    /// IngresoCaja.IdImportacion/LineaPoa.IdImportacion. Permite a /revertir/{id} distinguir el
    /// pago automático de contado que el propio importador creó (que SÍ se da de baja en la
    /// reversa) de un pago manual real (que NUNCA se toca: la reversa se bloquea si lo encuentra).
    /// </summary>
    public Guid? IdImportacion { get; set; }
}
