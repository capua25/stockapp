using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Tareas;

/// <summary>
/// Tareas operativas del equipo (spec 2026-08-01): lista común sin asignación previa,
/// máquina de estados en el dominio (Tarea.CambiarEstado), notas append-only.
/// </summary>
public interface ITareaService
{
    /// <summary>Alta. La prioridad SIEMPRE se fuerza a Media, sin importar lo que traiga <paramref name="tarea"/>.</summary>
    Task<int> CrearAsync(Tarea tarea);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary>Pendiente → EnCurso. Registra quién la tomó. Implementado en Task 4.</summary>
    Task TomarAsync(int id);

    /// <summary>EnCurso → Pendiente. Limpia el responsable. Implementado en Task 4.</summary>
    Task SoltarAsync(int id);

    /// <summary>EnCurso → Terminada. Registra quién la cerró. Implementado en Task 4.</summary>
    Task TerminarAsync(int id);

    /// <summary>Pendiente/EnCurso → Cancelada. Solo Admin. Implementado en Task 5.</summary>
    Task CancelarAsync(int id);

    /// <summary>Cambia la prioridad. Solo Admin. Genera nota automática. Implementado en Task 5.</summary>
    Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad);

    /// <summary>Nota manual. Las notas son append-only: no hay método para editarlas ni
    /// borrarlas. Implementado en Task 6.</summary>
    Task AgregarNotaAsync(int id, string texto);

    /// <summary>Una sola tarea por id, o null si no existe (spec 2026-09-08). Expone
    /// ITareaRepository.ObtenerPorIdAsync, que ya existía con sus Includes completos —
    /// TareaFormViewModel.ReclasificarAsync (Task 12) lo usa para refrescar la tarea
    /// reclasificada sin traer la lista completa.</summary>
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Reemplaza los cinco clasificadores (spec 2026-09-08, D21: total, no parcial —
    /// null desasigna). Solo Admin (AdministrarTareas). Alcanza también tareas terminales
    /// (D9): reclasificar NO cambia el estado ni reabre la tarea. Misma validación que
    /// CrearAsync (D12). Si nada cambia, no genera nota ni auditoría (mismo guard que
    /// CambiarPrioridadAsync).</summary>
    Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos);

    /// <summary>Tareas vinculadas a un expediente (D11/D13): la dependencia va en un solo
    /// sentido, Documentos pregunta acá.</summary>
    Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId);
}
