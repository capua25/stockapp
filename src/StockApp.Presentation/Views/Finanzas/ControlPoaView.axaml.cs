using Avalonia.Controls;
using Avalonia.Input;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class ControlPoaView : UserControl
{
    public ControlPoaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ControlPoaViewModel vm)
                await vm.CargarAsync();
        };
    }

    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if (DataContext is ControlPoaViewModel { FilaSeleccionada: not null } vm
            && vm.AbrirGastosDeLaLineaCommand.CanExecute(null))
            vm.AbrirGastosDeLaLineaCommand.Execute(null);
    }
}
