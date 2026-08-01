using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

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
    private readonly IUsuarioRepository    _usuarios;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;

    public TareaService(
        ITareaRepository repo, IUsuarioRepository usuarios,
        ICurrentSession session, IAuthorizationService auth)
    {
        _repo     = repo;
        _usuarios = usuarios;
        _session  = session;
        _auth     = auth;
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

    public async Task TomarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaInicio        = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);
    }

    public async Task SoltarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorAnteriorId = tarea.TomadaPorUsuarioId;

        tarea.CambiarEstado(EstadoTarea.Pendiente);
        tarea.TomadaPorUsuarioId = null;
        tarea.FechaInicio        = null;

        // Decisión 11 del spec: toda acción sobre una tarea ajena genera nota automática.
        if (tomadorAnteriorId is int tomadorId && tomadorId != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, tomadorId, "soltó"));

        await _repo.ActualizarAsync(tarea);
    }

    public async Task TerminarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorId = tarea.TomadaPorUsuarioId;

        tarea.CambiarEstado(EstadoTarea.Terminada);
        tarea.CerradaPorUsuarioId = actorId;
        tarea.FechaFin            = DateTime.UtcNow;

        if (tomadorId is int idTomador && idTomador != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, idTomador, "terminó"));

        await _repo.ActualizarAsync(tarea);
    }

    /// <summary>Formato exacto de la decisión 11 del spec: "García terminó una tarea tomada por Juan".</summary>
    private async Task<NotaTarea> NotaAjenaAsync(int actorId, int tomadorId, string verbo)
    {
        var actorNombre   = _session.UsuarioActual!.NombreUsuario;
        var tomador       = await _usuarios.ObtenerPorIdAsync(tomadorId);
        var tomadorNombre = tomador?.NombreUsuario ?? $"usuario {tomadorId}";

        return new NotaTarea
        {
            UsuarioId    = actorId,
            Fecha        = DateTime.UtcNow,
            Texto        = $"{actorNombre} {verbo} una tarea tomada por {tomadorNombre}.",
            EsAutomatica = true,
        };
    }

    public async Task CancelarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        tarea.CambiarEstado(EstadoTarea.Cancelada);
        tarea.CerradaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaFin            = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);
    }

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        if (tarea.Prioridad == prioridad)
            return;   // sin cambios: no hay nada que registrar

        var anterior = tarea.Prioridad;
        tarea.Prioridad = prioridad;
        // Decisión 9 del spec: cada cambio de prioridad genera nota automática.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"Prioridad: {anterior} → {prioridad}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);
    }

    public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException();                 // Task 6
}
