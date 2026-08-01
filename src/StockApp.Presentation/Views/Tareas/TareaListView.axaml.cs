using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

public partial class TareaListView : UserControl
{
    public TareaListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is TareaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
