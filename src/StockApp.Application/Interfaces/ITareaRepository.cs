using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ITareaRepository
{
    Task<int> AgregarAsync(Tarea tarea);
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary><paramref name="tarea"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Tarea tarea);
}
