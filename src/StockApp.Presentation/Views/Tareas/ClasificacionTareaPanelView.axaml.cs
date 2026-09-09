using Avalonia.Controls;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Sin DataContextChanged propio a propósito: quien la embebe (TareaFormView, Task 13;
/// ReclasificarTareaDialog, Task 10) es responsable de llamar
/// ClasificacionTareaPanelViewModel.InicializarAsync() en su propio punto de carga — mismo
/// criterio que AdjuntosDocumentoPanelView, que tampoco se auto-inicializa.
/// </summary>
public partial class ClasificacionTareaPanelView : UserControl
{
    public ClasificacionTareaPanelView()
    {
        InitializeComponent();
    }
}
