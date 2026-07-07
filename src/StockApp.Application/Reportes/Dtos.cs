namespace StockApp.Application.Reportes;

/// <summary>Item de reporte de valorización por producto.</summary>
public record ValorizacionItemDto(
    int ProductoId,
    string Codigo,
    string Nombre,
    string Categoria,
    decimal StockActual,
    decimal PrecioCosto,
    decimal PrecioVenta,
    decimal ValorCosto,
    decimal ValorVenta);

/// <summary>Totales de reporte de valorización.</summary>
public record ValorizacionTotalesDto(
    decimal TotalValorCosto,
    decimal TotalValorVenta);

/// <summary>Resumen de stock por categoría.</summary>
public record StockCategoriaDto(
    string Categoria,
    int CantidadProductos,
    decimal StockTotal,
    decimal ValorCosto,
    decimal ValorVenta);

/// <summary>Producto más movido en un período.</summary>
public record MasMovidoDto(
    int ProductoId,
    string Codigo,
    string Nombre,
    int CantidadMovimientos,
    decimal VolumenTotal);

/// <summary>
/// Envoltorio serializable de la valorización: reemplaza la tupla nombrada
/// (Items, Totales) que no serializa de forma estándar por HTTP (Fase 0 de la
/// migración client-server — fricción #2 del spec).
/// </summary>
public record ValorizacionReporteDto(
    IReadOnlyList<ValorizacionItemDto> Items,
    ValorizacionTotalesDto Totales);
