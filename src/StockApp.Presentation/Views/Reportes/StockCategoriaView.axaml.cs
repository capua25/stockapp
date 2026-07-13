using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class StockCategoriaView : UserControl
{
    public StockCategoriaView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializa el stock por categoría.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is StockCategoriaViewModel vm)
                await vm.CargarAsync();
        };
    }
}
