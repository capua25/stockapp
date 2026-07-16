using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresoFormView : UserControl
{
    public IngresoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
