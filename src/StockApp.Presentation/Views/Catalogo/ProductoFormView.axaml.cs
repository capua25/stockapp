using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ProductoFormView : UserControl
{
    public ProductoFormView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializan las colecciones de unidades de medida y categorías
        // (incluida la garantía idempotente de la unidad "Unidad" por defecto).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ProductoFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
