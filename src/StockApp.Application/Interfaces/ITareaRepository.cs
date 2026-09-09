using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ITareaRepository
{
    Task<int> AgregarAsync(Tarea tarea);
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary>Tareas vinculadas a un expediente (spec 2026-09-08, D11/D13): la dependencia
    /// va en un solo sentido (Tarea conoce a DocumentoAdministrativo), así que la ficha del
    /// documento pregunta acá en vez de que Documentos cargue una colección de tareas.</summary>
    Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId);

    /// <summary><paramref name="tarea"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Tarea tarea);
}
