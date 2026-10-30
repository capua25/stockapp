using System.Net.Http.Json;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record NotaProyectoWire(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

internal sealed record ProyectoWire(
    int Id, string Titulo, string? Descripcion,
    EstadoProyecto Estado, PrioridadTarea Prioridad, DateTime? FechaLimite, decimal? Presupuesto,
    int CreadoPorUsuarioId, DateTime FechaCreacion, DateTime? FechaFin,
    int? ZonaId, string? ZonaNombre,
    int? DimensionTematicaId, string? DimensionTematicaNombre,
    int? OrganismoResponsableId, string? OrganismoResponsableNombre,
    int? OrigenFinanciamientoId, string? OrigenFinanciamientoNombre,
    List<NotaProyectoWire> Notas);

internal sealed record GastoDeProyectoWire(int Id, DateTime Fecha, string ProveedorNombre, string Detalle, decimal MontoTotal);

internal sealed record FichaProyectoWire(
    ProyectoWire Proyecto, decimal TotalGastado, decimal? Disponible,
    List<GastoDeProyectoWire>? Gastos);

internal sealed record OpcionProyectoWire(int Id, string Titulo, EstadoProyecto Estado);

internal sealed record DatosProyectoBody(
    string Titulo, string? Descripcion, PrioridadTarea Prioridad,
    DateTime? FechaLimite, decimal? Presupuesto,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId, int? OrigenFinanciamientoId);

internal sealed record CambiarEstadoProyectoBody(EstadoProyecto Estado);
internal sealed record AgregarNotaProyectoBody(string Texto);

/// <summary>IProyectoService contra /proyectos.</summary>
public sealed class ProyectoApiClient : IProyectoService
{
    private readonly HttpClient _http;

    public ProyectoApiClient(HttpClient http) => _http = http;

    public async Task<int> CrearAsync(DatosProyecto datos)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("proyectos", ABody(datos)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el proyecto.");
        return creado.Id;
    }

    public async Task ModificarAsync(int id, DatosProyecto datos)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync($"proyectos/{id}", ABody(datos)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarEstadoAsync(int id, EstadoProyecto destino)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"proyectos/{id}/estado", new CambiarEstadoProyectoBody(destino)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"proyectos/{id}/notas", new AgregarNotaProyectoBody(texto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<Proyecto>> ListarAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("proyectos"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<ProyectoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<FichaProyectoDto> ObtenerFichaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"proyectos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dto = await response.Content.ReadFromJsonAsync<FichaProyectoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener la ficha del proyecto.");
        return new FichaProyectoDto(
            AEntidad(dto.Proyecto), dto.TotalGastado, dto.Disponible,
            dto.Gastos?.Select(g => new GastoDeProyectoDto(g.Id, g.Fecha, g.ProveedorNombre, g.Detalle, g.MontoTotal)).ToList());
    }

    public async Task<IReadOnlyList<OpcionProyectoDto>> ListarOpcionesAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("proyectos/opciones"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<OpcionProyectoWire>>() ?? new();
        return dtos.Select(o => new OpcionProyectoDto(o.Id, o.Titulo, o.Estado)).ToList();
    }

    private static DatosProyectoBody ABody(DatosProyecto d) => new(
        d.Titulo, d.Descripcion, d.Prioridad, d.FechaLimite, d.Presupuesto,
        d.ZonaId, d.DimensionTematicaId, d.OrganismoResponsableId, d.OrigenFinanciamientoId);

    private static Proyecto AEntidad(ProyectoWire dto) => new()
    {
        Id = dto.Id,
        Titulo = dto.Titulo,
        Descripcion = dto.Descripcion,
        Estado = dto.Estado,
        Prioridad = dto.Prioridad,
        FechaLimite = dto.FechaLimite,
        Presupuesto = dto.Presupuesto,
        CreadoPorUsuarioId = dto.CreadoPorUsuarioId,
        FechaCreacion = dto.FechaCreacion,
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
        Notas = dto.Notas.Select(n => new NotaProyecto
        {
            Id = n.Id, ProyectoId = dto.Id, UsuarioId = n.UsuarioId, Fecha = n.Fecha,
            Texto = n.Texto, EsAutomatica = n.EsAutomatica,
        }).ToList(),
    };
}
