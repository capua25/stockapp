using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Movimientos;

namespace StockApp.Presentation.Views.Movimientos;

public partial class IngresoPorFacturaView : UserControl
{
    public IngresoPorFacturaView()
    {
        InitializeComponent();

        // Mismo patrón que EntradaRegistroView: no hay hook de INavigationService que
        // dispare la carga; se cablea acá.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoPorFacturaViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
