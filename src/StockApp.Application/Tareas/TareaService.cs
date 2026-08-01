using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Tareas;

/// <summary>
/// Servicio de tareas. Patrón: auth → validación → mutación de la entidad (la máquina de
/// estados vive en Tarea.CambiarEstado) → persistencia. Tomar/Soltar/Terminar se
/// implementan en Task 4 (agrega IUsuarioRepository al constructor, para resolver nombres
/// en las notas automáticas); Cancelar/CambiarPrioridad en Task 5; AgregarNota + auditoría
/// en Task 6 (agrega IAuditLogger).
/// </summary>
public class TareaService : ITareaService
{
    private readonly ITareaRepository      _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;

    public TareaService(ITareaRepository repo, ICurrentSession session, IAuthorizationService auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }

    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(tarea.Titulo))
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(tarea.Titulo));

        // Decisión 8 del spec: la prioridad nace SIEMPRE en Media, incluso si el llamador
        // (Admin incluido) trae otra cosa en la entidad.
        tarea.Estado              = EstadoTarea.Pendiente;
        tarea.Prioridad           = PrioridadTarea.Media;
        tarea.CreadaPorUsuarioId  = _session.UsuarioActual!.Id;
        tarea.FechaCreacion       = DateTime.UtcNow;
        tarea.TomadaPorUsuarioId  = null;
        tarea.FechaInicio         = null;
        tarea.CerradaPorUsuarioId = null;
        tarea.FechaFin            = null;

        return await _repo.AgregarAsync(tarea);
    }

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);
        return await _repo.ListarAsync();
    }

    public Task TomarAsync(int id) => throw new NotImplementedException();                                     // Task 4
    public Task SoltarAsync(int id) => throw new NotImplementedException();                                    // Task 4
    public Task TerminarAsync(int id) => throw new NotImplementedException();                                  // Task 4
    public Task CancelarAsync(int id) => throw new NotImplementedException();                                  // Task 5
    public Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad) => throw new NotImplementedException(); // Task 5
    public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException();                 // Task 6
}
