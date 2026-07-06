using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Movimientos;

public partial class EntradaRegistroView : UserControl
{
    public EntradaRegistroView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializan los productos activos disponibles (ver ProductoFormView).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is MovimientoRegistroViewModelBase vm)
                await vm.InicializarAsync();
        };
    }
}
