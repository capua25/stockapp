using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Movimientos;

public partial class MovimientoHistorialView : UserControl
{
    public MovimientoHistorialView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializan los productos del filtro y se carga el historial
        // (mismo patrón que ProductoFormView.axaml.cs).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is MovimientoHistorialViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
