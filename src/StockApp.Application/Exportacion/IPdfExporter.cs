namespace StockApp.Application.Exportacion;

/// <summary>
/// Exportador PDF genérico para tablas (spec 2026-09-18). Análogo a <see cref="ICsvExporter"/>
/// pero con membrete institucional, encabezado de tabla repetido por página, apaisado
/// automático en tablas anchas y wrap de texto sin truncar. La implementación real
/// (<c>PdfExporterMigraDoc</c>) vive en StockApp.Documentos -- este contrato vive en
/// Application, sin paquetes externos, porque tanto el desktop como (potencialmente) otros
/// clientes futuros lo consumen sin necesitar la dependencia de MigraDoc.
/// </summary>
public interface IPdfExporter
{
    /// <summary>
    /// Genera el PDF completo de <paramref name="items"/> con las columnas indicadas.
    /// </summary>
    /// <typeparam name="T">Tipo de los items a exportar.</typeparam>
    /// <param name="items">Colección de items YA FILTRADA -- la misma que ve la grilla.</param>
    /// <param name="columnas">
    /// Nombres de las propiedades a exportar, en el orden de la GRILLA (no del CSV). Más de 6
    /// columnas dispara apaisado automático.
    /// </param>
    /// <param name="metadatos">Título, descripción de filtros y usuario emisor del membrete.</param>
    /// <returns>El PDF completo como array de bytes.</returns>
    byte[] Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnas, MetadatosDocumento metadatos);
}
