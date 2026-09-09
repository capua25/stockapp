using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrigenFinanciamientoListView : UserControl
{
    public OrigenFinanciamientoListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is OrigenFinanciamientoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
