using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Gasto de la caja municipal (cabecera única — enfoque A del spec): cada factura o
/// compromiso se registra UNA sola vez con sus dimensiones (fuente, rubro, línea POA
/// opcional). Agregado: sus <see cref="PagoGasto"/> se gestionan a través del gasto.
/// El número de factura es opcional (compromisos sin factura: solicitudes de
/// suministro, expedientes).
/// </summary>
public class Gasto
{
    public int Id { get; set; }
    public int ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }
    public string? NumeroFactura { get; set; }
    public string? NumeroOrden { get; set; }              // orden de compra
    public string Detalle { get; set; } = string.Empty;   // obligatorio
    public string? Destino { get; set; }
    public DateTime Fecha { get; set; }                   // UTC
    public decimal MontoTotal { get; set; }               // precisión 18,4
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public int RubroGastoId { get; set; }
    public RubroGasto? RubroGasto { get; set; }
    public int? LineaPoaId { get; set; }
    public LineaPoa? LineaPoa { get; set; }
    public CondicionPago CondicionPago { get; set; }
    public DateTime? FechaVencimiento { get; set; }       // obligatoria si crédito
    public bool Activo { get; set; } = true;              // false = anulado

    /// <summary>
    /// Guid del lote de /confirmar que creó este gasto (F5c). Null para TODO lo cargado por
    /// las vías normales (ABM manual) — que es, hoy y a futuro, la inmensa mayoría de los
    /// datos. Permite a /revertir/{id} encontrar y dar de baja un lote completo.
    /// </summary>
    public Guid? IdImportacion { get; set; }

    public List<PagoGasto> Pagos { get; set; } = new();

    /// <summary>Suma de los pagos ACTIVOS (los anulados no cuentan).</summary>
    public decimal TotalPagado => Pagos.Where(p => p.Activo).Sum(p => p.Monto);

    /// <summary>Lo que falta pagar de la factura.</summary>
    public decimal SaldoPendiente => MontoTotal - TotalPagado;

    /// <summary>
    /// Estado calculado (spec §4): Anulada si el gasto está inactivo; Pagada si los
    /// pagos activos cubren el total; Vencida si es crédito con vencimiento anterior a
    /// la fecha de referencia y no está pagada; Parcial si hay pagos que no cubren el
    /// total; Pendiente en el resto. Recibe la fecha de referencia (hoy) por parámetro
    /// para ser determinístico y testeable.
    /// </summary>
    public EstadoGasto CalcularEstado(DateTime fechaReferencia)
    {
        if (!Activo)
            return EstadoGasto.Anulada;
        if (TotalPagado >= MontoTotal)
            return EstadoGasto.Pagada;
        if (CondicionPago == CondicionPago.Credito
            && FechaVencimiento is not null
            && FechaVencimiento.Value.Date < fechaReferencia.Date)
            return EstadoGasto.Vencida;
        return TotalPagado > 0 ? EstadoGasto.Parcial : EstadoGasto.Pendiente;
    }
}
