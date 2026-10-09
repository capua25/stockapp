using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using StockApp.Application.Catalogo;

namespace StockApp.Presentation.Views.Dialogs;

/// <summary>
/// Diálogo modal de "Recalcular stock": un buscador de producto (búsqueda server-side). "Recalcular" queda
/// deshabilitado hasta que se elige un producto y devuelve ese <see cref="ProductoDto"/>; "Cancelar" devuelve null.
/// Usá <see cref="Window.ShowDialog{TResult}"/> con TResult=ProductoDto? para obtener el resultado.
/// </summary>
public partial class RecalcularStockDialog : Window
{
    public RecalcularStockDialog()
    {
        InitializeComponent();
    }

    /// <summary>Crea el diálogo con el delegado que alimenta el buscador (<c>AutoCompleteBox.AsyncPopulator</c>).</summary>
    public RecalcularStockDialog(Func<string?, CancellationToken, Task<IEnumerable<object>>> buscar) : this()
    {
        BuscadorProducto.AsyncPopulator = buscar;
        BuscadorProducto.SelectionChanged += (_, _) =>
            RecalcularButton.IsEnabled = BuscadorProducto.SelectedItem is ProductoDto;
    }

    private void OnRecalcularClick(object? sender, RoutedEventArgs e)
        => Close(BuscadorProducto.SelectedItem as ProductoDto);

    private void OnCancelarClick(object? sender, RoutedEventArgs e)
        => Close(null);
}
