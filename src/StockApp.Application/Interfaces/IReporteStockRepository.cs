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
}
