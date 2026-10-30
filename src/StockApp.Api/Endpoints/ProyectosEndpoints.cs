using StockApp.Application.Authorization;
using StockApp.Application.Proyectos;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record NotaProyectoDto(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

/// <summary>Proyecto con los nombres de sus clasificadores y sus notas (no se expone la entidad).</summary>
public record ProyectoDto(
    int Id, string Titulo, string? Descripcion,
    EstadoProyecto Estado, PrioridadTarea Prioridad, DateTime? FechaLimite, decimal? Presupuesto,
    int CreadoPorUsuarioId, DateTime FechaCreacion, DateTime? FechaFin,
    int? ZonaId, string? ZonaNombre,
    int? DimensionTematicaId, string? DimensionTematicaNombre,
    int? OrganismoResponsableId, string? OrganismoResponsableNombre,
    int? OrigenFinanciamientoId, string? OrigenFinanciamientoNombre,
    List<NotaProyectoDto> Notas);

/// <summary>Espejo HTTP de DatosProyecto (Application) para el alta.</summary>
public record CrearProyectoRequest(
    string Titulo, string? Descripcion, PrioridadTarea Prioridad,
    DateTime? FechaLimite, decimal? Presupuesto,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId, int? OrigenFinanciamientoId);

/// <summary>Espejo HTTP de DatosProyecto (Application) para la modificación (reemplazo total).</summary>
public record ModificarProyectoRequest(
    string Titulo, string? Descripcion, PrioridadTarea Prioridad,
    DateTime? FechaLimite, decimal? Presupuesto,
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId, int? OrigenFinanciamientoId);

public record CambiarEstadoProyectoRequest(EstadoProyecto Estado);
public record AgregarNotaProyectoRequest(string Texto);
public record ProyectoCreadoResponse(int Id);

/// <summary>Ficha de proyecto. Gastos es null (no vacío) cuando la sesión no tiene finanzas.ver.</summary>
public record FichaProyectoResponse(
    ProyectoDto Proyecto, decimal TotalGastado, decimal? Disponible,
    List<GastoDeProyectoDto>? Gastos);

public record OpcionProyectoResponse(int Id, string Titulo, EstadoProyecto Estado);

public static class ProyectosEndpoints
{
    public static IEndpointRouteBuilder MapProyectosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/proyectos");

        group.MapPost("/", async (CrearProyectoRequest request, IProyectoService service) =>
        {
            var id = await service.CrearAsync(new DatosProyecto(
                request.Titulo, request.Descripcion, request.Prioridad,
                request.FechaLimite, request.Presupuesto,
                request.ZonaId, request.DimensionTematicaId,
                request.OrganismoResponsableId, request.OrigenFinanciamientoId));
            return Results.Created((string?)null, new ProyectoCreadoResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarProyectos);

        group.MapPut("/{id:int}", async (int id, ModificarProyectoRequest request, IProyectoService service) =>
        {
            await service.ModificarAsync(id, new DatosProyecto(
                request.Titulo, request.Descripcion, request.Prioridad,
                request.FechaLimite, request.Presupuesto,
                request.ZonaId, request.DimensionTematicaId,
                request.OrganismoResponsableId, request.OrigenFinanciamientoId));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProyectos);

        group.MapPost("/{id:int}/estado", async (int id, CambiarEstadoProyectoRequest request, IProyectoService service) =>
        {
            await service.CambiarEstadoAsync(id, request.Estado);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProyectos);

        group.MapPost("/{id:int}/notas", async (int id, AgregarNotaProyectoRequest request, IProyectoService service) =>
        {
            await service.AgregarNotaAsync(id, request.Texto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProyectos);

        group.MapGet("/", async (IProyectoService service) =>
        {
            var proyectos = await service.ListarAsync();
            return Results.Ok(proyectos.Select(ADto));
        })
        .RequireAuthorization(Permisos.VerProyectos);

        group.MapGet("/{id:int}", async (int id, IProyectoService service) =>
        {
            var ficha = await service.ObtenerFichaAsync(id);
            return Results.Ok(new FichaProyectoResponse(
                ADto(ficha.Proyecto), ficha.TotalGastado, ficha.Disponible, ficha.Gastos?.ToList()));
        })
        .RequireAuthorization(Permisos.VerProyectos);

        // Solo exige sesión: alimenta los selectores de proyecto de usuarios sin proyectos.ver.
        // Sin policy concreta, por eso no figura en la fixture de PermisosEndpointGuardTests.
        group.MapGet("/opciones", async (IProyectoService service) =>
        {
            var opciones = await service.ListarOpcionesAsync();
            return Results.Ok(opciones.Select(o => new OpcionProyectoResponse(o.Id, o.Titulo, o.Estado)));
        })
        .RequireAuthorization();

        return app;
    }

    private static ProyectoDto ADto(Proyecto p) => new(
        p.Id, p.Titulo, p.Descripcion,
        p.Estado, p.Prioridad, p.FechaLimite, p.Presupuesto,
        p.CreadoPorUsuarioId, p.FechaCreacion, p.FechaFin,
        p.ZonaId, p.Zona?.Nombre,
        p.DimensionTematicaId, p.DimensionTematica?.Nombre,
        p.OrganismoResponsableId, p.OrganismoResponsable?.Nombre,
        p.OrigenFinanciamientoId, p.OrigenFinanciamiento?.Nombre,
        p.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaProyectoDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
}
