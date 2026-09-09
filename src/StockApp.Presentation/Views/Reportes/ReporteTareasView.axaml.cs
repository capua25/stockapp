using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class ReporteTareasView : UserControl
{
    public ReporteTareasView()
    {
        InitializeComponent();

        // No hay un hook de navegación que dispare la carga de datos al mostrar la vista
        // (bug recurrente del proyecto): se cablea acá, igual que StockCategoriaView.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ReporteTareasViewModel vm)
                await vm.CargarAsync();
        };
    }
}
