namespace StockApp.Presentation.UiTests;

/// <summary>
/// Entidad mínima usada por <see cref="DataGridSortClickTests"/> para reproducir el bug
/// de sort por click de encabezado (AvaloniaUI/Avalonia.Controls.DataGrid#232) con un
/// compiled binding igual al que usan las columnas reales de la app
/// (<c>DataGridTextColumn.Binding</c> con <c>DataType</c>).
/// </summary>
public sealed class ItemPrueba(string nombre, int valor)
{
    public string Nombre { get; } = nombre;

    public int Valor { get; } = valor;
}
