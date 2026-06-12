namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio de reportes de stock. Patrón: auth → repo → agregación.
/// Requiere el permiso <c>reportes.ver</c> (negado a Operador).
/// </summary>
public interface IReporteStockService
{
    /// <summary>
    /// Valorización de stock: items por producto + totales agregados.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso para ver reportes.</exception>
    Task<(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales)> ObtenerValorizacionAsync();

    /// <summary>
    /// Resumen de stock agrupado por categoría. El agrupamiento lo realiza el repo.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso para ver reportes.</exception>
    Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync();
}
