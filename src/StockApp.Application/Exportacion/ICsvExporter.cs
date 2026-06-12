namespace StockApp.Application.Exportacion;

/// <summary>
/// Exportador CSV genérico conforme a RFC 4180. Lo usan todos los reportes
/// del sistema para producir un CSV consistente (BOM UTF-8 + CRLF).
/// </summary>
public interface ICsvExporter
{
    /// <summary>
    /// Serializa una colección de items a CSV.
    /// </summary>
    /// <typeparam name="T">Tipo de los items a exportar.</typeparam>
    /// <param name="items">Colección de items. Cada uno produce una fila de datos.</param>
    /// <param name="columnOrder">
    /// Nombres de las propiedades a exportar, EN EL ORDEN deseado. Resuelve a
    /// propósito el no-determinismo de <c>Type.GetProperties()</c>: el header y
    /// cada fila respetan exactamente este orden.
    /// </param>
    /// <returns>
    /// El CSV completo como string. Arranca con BOM UTF-8 y cada fila (header y
    /// datos) termina en CRLF (<c>\r\n</c>).
    /// </returns>
    string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder);
}
