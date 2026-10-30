using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using StockApp.Presentation.ViewModels.Proyectos;

namespace StockApp.Presentation.Views.Proyectos;

public partial class ProyectoFormView : UserControl
{
    public ProyectoFormView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la ficha y los catálogos se piden
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ProyectoFormViewModel vm)
                await vm.InicializarAsync();
        };

        // Salir de la caja sin completarla cuenta como tocarla: su error aparece ahí y no recién al guardar.
        CajaTitulo.LostFocus += (_, _) => (DataContext as ProyectoFormViewModel)?.MarcarTocado(nameof(ProyectoFormViewModel.Titulo));
    }

    /// <summary>Doble click en una tarea del proyecto = abrir su detalle.</summary>
    private void OnTareaDobleClick(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(includeSelf: true) is not { DataContext: ViewModels.Tareas.TareaFila fila })
            return;
        if (DataContext is ProyectoFormViewModel vm && vm.VerTareaCommand.CanExecute(fila))
            vm.VerTareaCommand.Execute(fila);
    }
}
