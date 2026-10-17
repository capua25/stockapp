using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StockApp.Application.Documentos;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.UiTests.V2;

/// <summary>
/// ITareaService con los tipos reales. ListarAsync responde, en este orden: Falla (lo que lanza ApiErrores: 403 →
/// UnauthorizedAccessException, sin servidor → ServidorNoDisponibleException), Pendiente (consulta colgada) o la lista
/// ordenada por creación descendente. Las transiciones aplican las reglas reales del dominio y graban qué se pidió.
/// </summary>
internal sealed class TareasFake : ITareaService
{
    public List<Tarea> Lista { get; set; } = new();
    public Exception? Falla { get; set; }
    public TaskCompletionSource<IReadOnlyList<Tarea>>? Pendiente { get; set; }
    public Exception? FallaAlCrear { get; set; }
    public int LlamadasListar { get; private set; }
    public List<int> Tomadas { get; } = new();
    public List<int> Canceladas { get; } = new();
    public List<Tarea> Creadas { get; } = new();
    public List<(int Id, string Texto)> Notas { get; } = new();
    public List<(int Id, PrioridadTarea Prioridad)> Prioridades { get; } = new();
    public List<(int Id, DatosClasificacionTarea Datos)> Reclasificaciones { get; } = new();

    /// <summary>Hace que el servidor "falle" solo la próxima vez (después se recupera).</summary>
    public void ListarPendiente() => Pendiente = new TaskCompletionSource<IReadOnlyList<Tarea>>();

    public Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        LlamadasListar++;
        return Falla is not null ? Task.FromException<IReadOnlyList<Tarea>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<Tarea>>(Lista.OrderByDescending(t => t.FechaCreacion).ToList());
    }

    private Tarea Buscar(int id) => Lista.First(t => t.Id == id);

    public Task TomarAsync(int id)
    {
        var t = Buscar(id);
        t.CambiarEstado(EstadoTarea.EnCurso);
        t.TomadaPor = new Usuario { Id = 2, NombreUsuario = "jrodriguez" };
        Tomadas.Add(id);
        return Task.CompletedTask;
    }

    public Task SoltarAsync(int id)
    {
        var t = Buscar(id);
        t.CambiarEstado(EstadoTarea.Pendiente);
        t.TomadaPor = null;
        return Task.CompletedTask;
    }

    public Task TerminarAsync(int id)
    {
        Buscar(id).CambiarEstado(EstadoTarea.Terminada);
        return Task.CompletedTask;
    }

    public Task CancelarAsync(int id)
    {
        Buscar(id).CambiarEstado(EstadoTarea.Cancelada);
        Canceladas.Add(id);
        return Task.CompletedTask;
    }

    public Task<int> CrearAsync(Tarea tarea)
    {
        if (FallaAlCrear is not null) return Task.FromException<int>(FallaAlCrear);
        tarea.Id = Lista.Count + 1;
        Lista.Add(tarea);
        Creadas.Add(tarea);
        return Task.FromResult(tarea.Id);
    }

    public Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        Buscar(id).CambiarPrioridad(prioridad);
        Prioridades.Add((id, prioridad));
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto)
    {
        Buscar(id).Notas.Add(new NotaTarea { TareaId = id, Texto = texto, Fecha = DateTime.UtcNow });
        Notas.Add((id, texto));
        return Task.CompletedTask;
    }

    public Task<Tarea?> ObtenerPorIdAsync(int id) => Task.FromResult(Lista.FirstOrDefault(t => t.Id == id));

    public Task ReclasificarAsync(int tareaId, DatosClasificacionTarea datos)
    {
        Reclasificaciones.Add((tareaId, datos));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<Tarea>>(Lista.Where(t => t.DocumentoAdministrativoId == documentoId).ToList());
}

/// <summary>
/// IDocumentoAdministrativoService con los tipos reales. Cada consulta graba su FiltroDocumentos y responde, en este
/// orden: Falla, Pendiente (solo Activos) o la lista de su vista.
/// </summary>
internal sealed class DocumentosFake : IDocumentoAdministrativoService
{
    public List<DocumentoAdministrativo> Activos { get; set; } = new();
    public List<DocumentoAdministrativo> Historial { get; set; } = new();
    public Exception? Falla { get; set; }
    public TaskCompletionSource<IReadOnlyList<DocumentoAdministrativo>>? Pendiente { get; set; }
    public Exception? FallaAlRegistrar { get; set; }
    public List<FiltroDocumentos> FiltrosActivos { get; } = new();
    public List<FiltroDocumentos> FiltrosHistorial { get; } = new();
    public List<int> Finalizados { get; } = new();
    public List<int> Iniciados { get; } = new();
    public List<DocumentoAdministrativo> Registrados { get; } = new();
    public List<(int Id, string Motivo)> Anulados { get; } = new();
    public List<(int Id, string Texto)> Notas { get; } = new();
    public List<(int Id, DatosEdicionDocumento Datos)> Ediciones { get; } = new();

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarActivosAsync(FiltroDocumentos filtro)
    {
        FiltrosActivos.Add(filtro);
        return Falla is not null ? Task.FromException<IReadOnlyList<DocumentoAdministrativo>>(Falla)
            : Pendiente is not null ? Pendiente.Task
            : Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(Activos.ToList());
    }

    public Task<IReadOnlyList<DocumentoAdministrativo>> ListarHistorialAsync(FiltroDocumentos filtro)
    {
        FiltrosHistorial.Add(filtro);
        return Falla is not null ? Task.FromException<IReadOnlyList<DocumentoAdministrativo>>(Falla)
            : Task.FromResult<IReadOnlyList<DocumentoAdministrativo>>(Historial.ToList());
    }

    private DocumentoAdministrativo Buscar(int id) => Activos.Concat(Historial).First(d => d.Id == id);

    public Task<DocumentoAdministrativo?> ObtenerPorIdAsync(int id) =>
        Task.FromResult<DocumentoAdministrativo?>(Activos.Concat(Historial).FirstOrDefault(d => d.Id == id));

    public Task<int> RegistrarAsync(DocumentoAdministrativo documento)
    {
        if (FallaAlRegistrar is not null) return Task.FromException<int>(FallaAlRegistrar);
        Registrados.Add(documento);
        return Task.FromResult(99);
    }

    public Task EditarAsync(int id, DatosEdicionDocumento datos)
    {
        Ediciones.Add((id, datos));
        return Task.CompletedTask;
    }

    public Task IniciarProcesoAsync(int id)
    {
        Buscar(id).CambiarEstado(EstadoDocumento.EnProceso);
        Iniciados.Add(id);
        return Task.CompletedTask;
    }

    public Task VolverAPendienteAsync(int id)
    {
        Buscar(id).CambiarEstado(EstadoDocumento.Pendiente);
        return Task.CompletedTask;
    }

    public Task FinalizarAsync(int id)
    {
        var d = Buscar(id);
        d.CambiarEstado(EstadoDocumento.Finalizado);
        Activos.Remove(d);
        Historial.Add(d);
        Finalizados.Add(id);
        return Task.CompletedTask;
    }

    public Task AgregarNotaAsync(int id, string texto)
    {
        Notas.Add((id, texto));
        return Task.CompletedTask;
    }

    public Task AnularAsync(int id, string motivo)
    {
        Anulados.Add((id, motivo));
        return Task.CompletedTask;
    }

    public Task ReabrirAsync(int id, string motivo) => Task.CompletedTask;
}

/// <summary>IAdjuntoDocumentoService con los tipos reales; Quitar saca el adjunto de la lista.</summary>
internal sealed class AdjuntosDocumentoFake : IAdjuntoDocumentoService
{
    public List<AdjuntoDocumentoDto> Lista { get; set; } = new();
    public List<int> Quitados { get; } = new();

    public Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId) =>
        Task.FromResult<IReadOnlyList<AdjuntoDocumentoDto>>(Lista.ToList());

    public Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId) => throw new NotSupportedException();

    public Task QuitarAsync(int adjuntoId)
    {
        Quitados.Add(adjuntoId);
        Lista.RemoveAll(a => a.Id == adjuntoId);
        return Task.CompletedTask;
    }
}
