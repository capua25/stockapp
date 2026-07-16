using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresosView : UserControl
{
    public IngresosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
