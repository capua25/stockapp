using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class PagosGastoView : UserControl
{
    public PagosGastoView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is PagosGastoViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
