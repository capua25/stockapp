using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LibroCajaView : UserControl
{
    public LibroCajaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LibroCajaViewModel vm)
                await vm.CargarAsync();
        };
    }
}
