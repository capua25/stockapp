using System.Threading.Tasks;
using StockApp.Application.Tareas;

namespace StockApp.Presentation.Services;

/// <summary>
/// Diálogo modal de reclasificación (spec 2026-09-08, D9/D21). A diferencia de
/// IConfirmacionService (texto libre o sí/no), este pide un formulario completo — mismos
/// cuatro combos y buscador de expediente que el alta, precargados con los valores actuales
/// de la tarea (Presentation.ViewModels.Tareas.ClasificacionTareaPanelViewModel).
/// </summary>
public interface IClasificacionTareaDialogService
{
    /// <summary>Muestra el modal precargado con <paramref name="actual"/>. Devuelve los
    /// datos elegidos si el Admin confirma, o null si canceló.</summary>
    Task<DatosClasificacionTarea?> PedirClasificacionAsync(DatosClasificacionTarea actual);
}
