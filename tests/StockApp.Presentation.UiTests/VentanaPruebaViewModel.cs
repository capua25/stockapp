using System.Collections.ObjectModel;
using Avalonia.Collections;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// DataContext minimo para la ventana de prueba. Expone tanto la coleccion cruda
/// (Items) como una DataGridCollectionView sobre ella (ItemsView), igual al patron
/// real de MovimientoHistorialViewModel/ProductoListViewModel, para poder probar
/// ambos escenarios (con y sin collection view) en el banco de pruebas.
/// </summary>
public sealed class VentanaPruebaViewModel
{
    public ObservableCollection<ItemPrueba> Items { get; }

    public DataGridCollectionView ItemsView { get; }

    public VentanaPruebaViewModel(ObservableCollection<ItemPrueba> items)
    {
        Items = items;
        ItemsView = new DataGridCollectionView(items);
    }
}
