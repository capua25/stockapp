using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

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

    /// <summary>
    /// Guard de la anulación en cascada (asiento inverso o baja lógica simple, según tenga
    /// movimientos de stock asociados). Unifica el chequeo que antes estaba DUPLICADO, con el
    /// mismo texto, en GastoService.AnularAsync e IngresoPorFacturaService.AnularLoteAsync —
    /// vivía en dos servicios distintos y se habría desincronizado con el primer cambio que
    /// tocara solo uno de los dos.
    ///
    /// Si hay algún pago MANUAL activo (plata que un operador registró a mano — RegistrarPagoAsync
    /// o ABM), bloquea SIEMPRE, sin importar la confirmación: una confirmación distraída no puede
    /// borrar un pago parcial real. Si el único pago activo es el automático de contado (creado
    /// por AltaAsync/IngresoPorFacturaService.RegistrarAsync — spec §4), permite anularlo en
    /// cascada, pero solo con confirmación explícita: sin ella, informa que hay un pago
    /// automático que se va a eliminar; con ella, devuelve los pagos automáticos a dar de baja
    /// junto con el gasto.
    /// </summary>
    public IReadOnlyList<PagoGasto> PagosAutomaticosADarDeBajaEnAnulacion(bool confirmarAnulacionDePagoAutomatico)
    {
        var pagosActivos = Pagos.Where(p => p.Activo).ToList();
        if (pagosActivos.Count == 0)
            return Array.Empty<PagoGasto>();

        if (pagosActivos.Any(p => !p.EsAutomatico))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        if (!confirmarAnulacionDePagoAutomatico)
            throw new AnulacionRequierePagoAutomaticoConfirmadoException(
                Id, pagosActivos.Sum(p => p.Monto));

        return pagosActivos;
    }
}
