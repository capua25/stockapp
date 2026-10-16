using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastosView : UserControl
{
    public GastosView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara cuando la navegación asigna el
        // DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastosViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// Doble click en una fila = Editar, con el mismo gate que el botón (oculto sin finanzas.gastos). Solo sobre una fila:
    /// un doble click en el encabezado ordena, no edita.
    /// </summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is GastosViewModel { PuedeRegistrarGastos: true } vm && vm.EditarCommand.CanExecute(null))
            vm.EditarCommand.Execute(null);
    }
}
