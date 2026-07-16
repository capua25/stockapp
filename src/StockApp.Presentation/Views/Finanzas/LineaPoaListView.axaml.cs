using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LineaPoaListView : UserControl
{
    public LineaPoaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LineaPoaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
