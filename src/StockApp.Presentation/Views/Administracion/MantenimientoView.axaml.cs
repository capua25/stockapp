using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.Views.Administracion;

public partial class MantenimientoView : UserControl
{
    public MantenimientoView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is MantenimientoViewModel vm)
                await vm.CargarAsync();
        };
    }
}
