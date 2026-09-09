using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class DimensionTematicaListView : UserControl
{
    public DimensionTematicaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is DimensionTematicaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
