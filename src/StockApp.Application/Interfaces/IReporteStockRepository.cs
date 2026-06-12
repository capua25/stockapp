using StockApp.Application.Reportes;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Contrato de persistencia para reportes de stock (solo lectura).
/// El repo devuelve los items YA proyectados: ValorCosto/ValorVenta calculados
/// y Categoria resuelta a "Sin categoría" cuando es null. El service solo agrega/totaliza.
/// </summary>
public interface IReporteStockRepository
{
    /// <summary>
    /// Items de valorización por producto, con ValorCosto/ValorVenta ya calculados
    /// y Categoria resuelta. Retorna lista vacía si no hay productos.
    /// </summary>
    Task<IReadOnlyList<ValorizacionItemDto>> ObtenerValorizacionAsync();

    /// <summary>
    /// Resumen de stock agrupado por categoría, con CantidadProductos/StockTotal/
    /// ValorCosto/ValorVenta ya agregados y la categoría null resuelta a "Sin categoría".
    /// Retorna lista vacía si no hay productos.
    /// </summary>
    Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync();

    /// <summary>
    /// Productos más movidos en el período, ordenados por volumen total descendente
    /// y limitados a <paramref name="topN"/>. El repo ajusta FechaHasta a fin de día
    /// y aplica el orden y el Take(topN). Retorna lista vacía si no hay movimientos.
    /// </summary>
    Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN);
}
