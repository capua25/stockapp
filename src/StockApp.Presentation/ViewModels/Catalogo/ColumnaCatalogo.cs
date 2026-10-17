namespace StockApp.Presentation.ViewModels.Catalogo;

/// <summary>
/// Columna que un catálogo declara además de NOMBRE (que es fija y siempre la primera). Siempre es medida:
/// la grilla ajusta su ancho al contenido.
/// </summary>
public sealed record ColumnaCatalogo(string Encabezado);
