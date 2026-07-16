using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class FuenteFinanciamientoListView : UserControl
{
    public FuenteFinanciamientoListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan: cuando MaestrosFinanzasView asigna el
        // DataContext (binding a FuentesVm), se dispara la carga del listado.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is FuenteFinanciamientoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
