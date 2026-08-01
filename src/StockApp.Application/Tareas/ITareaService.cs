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
}
