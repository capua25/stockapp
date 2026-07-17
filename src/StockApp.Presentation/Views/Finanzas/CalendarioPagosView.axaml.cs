using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class CalendarioPagosView : UserControl
{
    public CalendarioPagosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is CalendarioPagosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
