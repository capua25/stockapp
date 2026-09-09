using System.Net.Http.Json;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

internal sealed record NotaTareaWire(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

internal sealed record TareaWire(
    int Id, string Titulo, string? Descripcion,
    EstadoTarea Estado, PrioridadTarea Prioridad, DateTime? FechaLimite,
    int CreadaPorUsuarioId, DateTime FechaCreacion,
    int? TomadaPorUsuarioId, string? TomadaPorNombre, DateTime? FechaInicio,
    int? CerradaPorUsuarioId, DateTime? FechaFin,
    int? ZonaId, string? ZonaNombre,
    int? DimensionTematicaId, string? DimensionTematicaNombre,
    int? OrganismoResponsableId, string? OrganismoResponsableNombre,
    int? OrigenFinanciamientoId, string? OrigenFinanciamientoNombre,
    int? DocumentoAdministrativoId, string? DocumentoAdministrativoNumero,
    List<NotaTareaWire> Notas);

internal sealed record CrearTareaBody(
    string Titulo, string? Descripcion, DateTime? FechaLimite,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);

internal sealed record CambiarPrioridadBody(PrioridadTarea Prioridad);
internal sealed record AgregarNotaBody(string Texto);
internal sealed record ClasificarTareaBody(
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);

/// <summary>ITareaService contra /tareas.</summary>
public sealed class TareaApiClient : ITareaService
{
    private readonly HttpClient _http;

    public TareaApiClient(HttpClient http) => _http = http;

    public async Task<Tarea?> ObtenerPorIdAsync(int id)
    {
        try
        {
            var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"tareas/{id}"));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<TareaWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = tarea inexistente: contrato de la interfaz (null)
        }
    }

    public async Task<int> CrearAsync(Tarea tarea)
    {
        var body = new CrearTareaBody(
            tarea.Titulo, tarea.Descripcion, tarea.FechaLimite,
            tarea.ZonaId, tarea.DimensionTematicaId, tarea.OrganismoResponsableId,
            tarea.OrigenFinanciamientoId, tarea.DocumentoAdministrativoId);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("tareas", body));
        await ApiErrores.AsegurarExitoAsync(response);

        var creada = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la tarea.");
        return creada.Id;
    }

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("tareas"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<TareaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<IReadOnlyList<Tarea>> ListarPorDocumentoAsync(int documentoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"tareas?documentoId={documentoId}"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<TareaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public Task TomarAsync(int id) => PostSinBodyAsync($"tareas/{id}/tomar");
    public Task SoltarAsync(int id) => PostSinBodyAsync($"tareas/{id}/soltar");
    public Task TerminarAsync(int id) => PostSinBodyAsync($"tareas/{id}/terminar");
    public Task CancelarAsync(int id) => PostSinBodyAsync($"tareas/{id}/cancelar");

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"tareas/{id}/prioridad", new CambiarPrioridadBody(prioridad)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"tareas/{id}/notas", new AgregarNotaBody(texto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task ReclasificarAsync(int id, DatosClasificacionTarea datos)
    {
        var body = new ClasificarTareaBody(
            datos.ZonaId, datos.DimensionTematicaId, datos.OrganismoResponsableId,
            datos.OrigenFinanciamientoId, datos.DocumentoAdministrativoId);
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync($"tareas/{id}/clasificacion", body));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private async Task PostSinBodyAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static Tarea AEntidad(TareaWire dto) => new()
    {
        Id = dto.Id,
        Titulo = dto.Titulo,
        Descripcion = dto.Descripcion,
        Estado = dto.Estado,
        Prioridad = dto.Prioridad,
        FechaLimite = dto.FechaLimite,
        CreadaPorUsuarioId = dto.CreadaPorUsuarioId,
        FechaCreacion = dto.FechaCreacion,
        TomadaPorUsuarioId = dto.TomadaPorUsuarioId,
        TomadaPor = dto.TomadaPorNombre is null
            ? null : new Usuario { Id = dto.TomadaPorUsuarioId!.Value, NombreUsuario = dto.TomadaPorNombre },
        FechaInicio = dto.FechaInicio,
        CerradaPorUsuarioId = dto.CerradaPorUsuarioId,
        FechaFin = dto.FechaFin,
        ZonaId = dto.ZonaId,
        Zona = dto.ZonaNombre is null ? null : new Zona { Id = dto.ZonaId!.Value, Nombre = dto.ZonaNombre },
        DimensionTematicaId = dto.DimensionTematicaId,
        DimensionTematica = dto.DimensionTematicaNombre is null
            ? null : new DimensionTematica { Id = dto.DimensionTematicaId!.Value, Nombre = dto.DimensionTematicaNombre },
        OrganismoResponsableId = dto.OrganismoResponsableId,
        OrganismoResponsable = dto.OrganismoResponsableNombre is null
            ? null : new OrganismoResponsable { Id = dto.OrganismoResponsableId!.Value, Nombre = dto.OrganismoResponsableNombre },
        OrigenFinanciamientoId = dto.OrigenFinanciamientoId,
        OrigenFinanciamiento = dto.OrigenFinanciamientoNombre is null
            ? null : new OrigenFinanciamiento { Id = dto.OrigenFinanciamientoId!.Value, Nombre = dto.OrigenFinanciamientoNombre },
        DocumentoAdministrativoId = dto.DocumentoAdministrativoId,
        DocumentoAdministrativo = dto.DocumentoAdministrativoNumero is null
            ? null : new DocumentoAdministrativo { Id = dto.DocumentoAdministrativoId!.Value, Numero = dto.DocumentoAdministrativoNumero },
        Notas = dto.Notas.Select(n => new NotaTarea
        {
            Id = n.Id, TareaId = dto.Id, UsuarioId = n.UsuarioId, Fecha = n.Fecha,
            Texto = n.Texto, EsAutomatica = n.EsAutomatica,
        }).ToList(),
    };
}
