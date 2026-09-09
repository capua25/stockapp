using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Catalogo;

namespace StockApp.Presentation.Views.Catalogo;

public partial class OrganismoResponsableListView : UserControl
{
    public OrganismoResponsableListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is OrganismoResponsableListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
