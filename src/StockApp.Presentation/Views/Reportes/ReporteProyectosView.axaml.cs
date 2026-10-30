using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class ReporteProyectosView : UserControl
{
    public ReporteProyectosView()
    {
        InitializeComponent();

        // No hay un hook de navegación que dispare la carga de datos al mostrar la vista
        // (bug recurrente del proyecto): se cablea acá, igual que StockCategoriaView.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ReporteProyectosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
