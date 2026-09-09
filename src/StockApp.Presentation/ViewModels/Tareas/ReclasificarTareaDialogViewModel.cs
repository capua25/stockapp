namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// ViewModel del modal de reclasificación: envoltorio delgado sobre ClasificacionTareaPanelViewModel
/// (Task 9) — el diálogo en sí no tiene estado propio, Aceptar/Cancelar viven en el
/// code-behind de ReclasificarTareaDialog (mismo criterio que PedirTextoDialog).
/// </summary>
public partial class ReclasificarTareaDialogViewModel : ViewModelBase
{
    public ClasificacionTareaPanelViewModel Panel { get; }

    public ReclasificarTareaDialogViewModel(ClasificacionTareaPanelViewModel panel)
    {
        Panel = panel;
    }
}
