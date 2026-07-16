namespace StockApp.Domain.Enums;

/// <summary>
/// Estado CALCULADO de un gasto/factura — nunca se persiste (spec Finanzas §4):
/// se deriva de sum(pagos activos) vs MontoTotal + FechaVencimiento + Activo,
/// así jamás queda inconsistente.
/// </summary>
public enum EstadoGasto
{
    Pendiente = 0,
    Parcial   = 1,
    Pagada    = 2,
    Vencida   = 3,
    Anulada   = 4,
}
