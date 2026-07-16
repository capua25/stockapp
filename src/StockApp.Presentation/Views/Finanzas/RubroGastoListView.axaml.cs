using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class RubroGastoListView : UserControl
{
    public RubroGastoListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is RubroGastoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
