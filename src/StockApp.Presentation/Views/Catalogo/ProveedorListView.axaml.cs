using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ProveedorListView : UserControl
{
    public ProveedorListView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializa el listado de proveedores.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ProveedorListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
