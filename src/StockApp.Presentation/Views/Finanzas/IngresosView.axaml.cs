using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresosView : UserControl
{
    public IngresosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresosViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>
    /// Doble click en una fila = Editar. Solo sobre una fila (un doble click en el encabezado ordena, no edita), solo con
    /// permiso de registrar ingresos (el botón Editar está oculto sin él) y solo si Editar se puede ejecutar (fila activa).
    /// </summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is IngresosViewModel { PuedeRegistrarIngresos: true } vm && vm.EditarCommand.CanExecute(null))
            vm.EditarCommand.Execute(null);
    }
}
