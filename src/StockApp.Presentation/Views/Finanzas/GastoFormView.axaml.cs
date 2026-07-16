using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastoFormView : UserControl
{
    public GastoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastoFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
