using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Documentos;

namespace StockApp.Presentation.Views.Documentos;

public partial class DocumentoListView : UserControl
{
    public DocumentoListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is DocumentoListViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// D9: carga perezosa del historial -- recién al seleccionar la solapa "Historial"
    /// (índice 1), nunca al cargar la lista de Activos.
    /// </summary>
    private async void OnSolapaSeleccionada(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is TabControl { SelectedIndex: 1 } && DataContext is DocumentoListViewModel vm)
            await vm.AbrirHistorialCommand.ExecuteAsync(null);
    }
}
