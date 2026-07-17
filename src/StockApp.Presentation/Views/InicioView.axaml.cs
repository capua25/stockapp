using Avalonia.Controls;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class InicioView : UserControl
{
    public InicioView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is InicioViewModel vm)
                await vm.CargarAsync();
        };
    }
}
