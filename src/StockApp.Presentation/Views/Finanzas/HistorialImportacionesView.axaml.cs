using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class HistorialImportacionesView : UserControl
{
    public HistorialImportacionesView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is HistorialImportacionesViewModel vm)
                await vm.CargarAsync();
        };
    }
}
