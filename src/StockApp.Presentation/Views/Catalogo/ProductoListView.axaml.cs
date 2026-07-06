using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ProductoListView : UserControl
{
    public ProductoListView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializa el listado de productos.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ProductoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
