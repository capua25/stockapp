using Avalonia.Controls;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class BloqueoLicenciaView : UserControl
{
    public BloqueoLicenciaView()
    {
        InitializeComponent();

        // No hay un hook de navegación que dispare la carga de datos al mostrar la vista,
        // así que se cablea acá: cuando el ViewModel se asigna como DataContext, se carga
        // el código de máquina (igual que el resto de las Views del proyecto).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is BloqueoLicenciaViewModel vm)
                await vm.CargarEstadoAsync();
        };
    }
}
