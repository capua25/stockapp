namespace StockApp.Application.Movimientos;

/// <summary>
/// Servicio de movimientos de stock: registro, historial y recálculo.
/// Todos los métodos son fail-closed: verifican autorización al inicio.
/// </summary>
public interface IMovimientoStockService
{
    /// <summary>
    /// Registra un movimiento de stock de forma atómica.
    /// </summary>
    /// <param name="dto">Datos del movimiento a registrar.</param>
    /// <param name="forzar">Si true, permite que el stock quede negativo (RM-09).</param>
    Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false);

    /// <summary>
    /// Obtiene el historial de movimientos con filtros combinables (HM-02..HM-05).
    /// Retorna lista vacía si no hay coincidencias.
    /// </summary>
    Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro);

    /// <summary>
    /// Recalcula el stock actual del producto sumando todos sus movimientos (RS-03).
    /// Persiste el nuevo valor de forma atómica con su auditoría.
    /// </summary>
    Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId);
}
