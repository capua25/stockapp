using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.Views.Administracion;

public partial class UsuariosAdminView : UserControl
{
    public UsuariosAdminView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is UsuariosAdminViewModel vm)
                await vm.CargarAsync();
        };
    }
}
