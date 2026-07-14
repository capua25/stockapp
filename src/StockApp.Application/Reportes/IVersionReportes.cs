namespace StockApp.Application.Reportes;

/// <summary>
/// Versión monotónica del conjunto de datos de reportes de stock. Cada mutación que
/// afecta un reporte (movimiento, ABM de producto, cambio de precio, ABM de categoría)
/// llama a <see cref="Invalidar"/> después de commitear; el caché de reportes incluye
/// <see cref="Actual"/> en sus claves, de modo que al incrementarse la versión las
/// entradas viejas quedan huérfanas.
/// </summary>
public interface IVersionReportes
{
    /// <summary>Versión vigente. Comienza en 0.</summary>
    long Actual { get; }

    /// <summary>Incrementa la versión (invalida todo el caché de reportes).</summary>
    void Invalidar();
}
