using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class ZonaListView : UserControl
{
    public ZonaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ZonaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
