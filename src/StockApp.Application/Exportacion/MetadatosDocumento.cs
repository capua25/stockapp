namespace StockApp.Application.Exportacion;

/// <summary>
/// Metadatos del membrete de un export PDF (spec 2026-09-18): título del reporte, descripción
/// textual de los filtros aplicados (no decorativa — un PDF que dice "Gastos" sin aclarar el
/// rango de fechas no es auditable) y usuario que lo generó. La fecha de emisión, la
/// paginación y el logo los resuelve la plantilla (<c>PlantillaTabular</c> en
/// StockApp.Documentos), no el llamador.
/// </summary>
public sealed record MetadatosDocumento(
    string Titulo,
    string DescripcionFiltros,
    string UsuarioEmisor);
