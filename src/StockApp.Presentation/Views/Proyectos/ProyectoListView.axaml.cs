using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Proyectos;

namespace StockApp.Presentation.Views.Proyectos;

public partial class ProyectoListView : UserControl
{
    public ProyectoListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ProyectoListViewModel vm)
                await vm.CargarAsync();
        };
    }

    /// <summary>Doble click en una fila = Ver. Solo sobre una fila: un doble click en el encabezado ordena, no navega.</summary>
    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is null)
            return;
        if (DataContext is ProyectoListViewModel { FilaSeleccionada: { } fila } vm && vm.VerDetalleCommand.CanExecute(fila))
            vm.VerDetalleCommand.Execute(fila);
    }
}
