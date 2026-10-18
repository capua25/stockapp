using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.Views.Administracion;

public partial class CatalogosView : UserControl
{
    public CatalogosView()
    {
        InitializeComponent();

        // La carga la dispara el contenedor y no el detalle: abre el catálogo pedido (o el primero visible) y carga solo ese.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is CatalogosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
