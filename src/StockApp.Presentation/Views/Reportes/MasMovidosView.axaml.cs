using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class MasMovidosView : UserControl
{
    public MasMovidosView()
    {
        InitializeComponent();

        // No hay un hook de navegación (INavigationService) que dispare la carga de datos
        // al mostrar la vista, así que se cablea acá: cuando el ViewModel se asigna como
        // DataContext, se inicializa el reporte de más movidos.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is MasMovidosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
