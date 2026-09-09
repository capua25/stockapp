using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record NotaTareaDto(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

public record TareaDto(
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
    List<NotaTareaDto> Notas);

// Los 5 ids de clasificación con default null (bugfix de compatibilidad): los sitios de
// test/producción existentes que construyen CrearTareaRequest con 3 argumentos posicionales
// (Titulo, Descripcion, FechaLimite) siguen compilando sin tocarlos.
public record CrearTareaRequest(
    string Titulo, string? Descripcion, DateTime? FechaLimite,
    int? ZonaId = null, int? DimensionTematicaId = null, int? OrganismoResponsableId = null,
    int? OrigenFinanciamientoId = null, int? DocumentoAdministrativoId = null);

public record CambiarPrioridadRequest(PrioridadTarea Prioridad);
public record AgregarNotaRequest(string Texto);
public record TareaCreadaResponse(int Id);

/// <summary>Espejo HTTP de DatosClasificacionTarea (Application) — reemplazo total, D21:
/// null en un campo desasigna ese clasificador.</summary>
public record ClasificarTareaRequest(
    int? ZonaId, int? DimensionTematicaId, int? OrganismoResponsableId,
    int? OrigenFinanciamientoId, int? DocumentoAdministrativoId);

public static class TareasEndpoints
{
    public static IEndpointRouteBuilder MapTareasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tareas");

        group.MapPost("/", async (CrearTareaRequest request, ITareaService service) =>
        {
            var tarea = new Tarea
            {
                Titulo      = request.Titulo,
                Descripcion = request.Descripcion,
                FechaLimite = request.FechaLimite,
                ZonaId                  = request.ZonaId,
                DimensionTematicaId     = request.DimensionTematicaId,
                OrganismoResponsableId  = request.OrganismoResponsableId,
                OrigenFinanciamientoId  = request.OrigenFinanciamientoId,
                DocumentoAdministrativoId = request.DocumentoAdministrativoId,
            };
            var id = await service.CrearAsync(tarea);
            return Results.Created((string?)null, new TareaCreadaResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        // GET por id (corrección de contrato, spec 2026-09-08): no existía — mismo molde
        // exacto que DocumentosEndpoints.MapGet("/{id:int}", ...) (documento is null ?
        // Results.NotFound() : Results.Ok(ADto(documento))).
        group.MapGet("/{id:int}", async (int id, ITareaService service) =>
        {
            var tarea = await service.ObtenerPorIdAsync(id);
            return tarea is null ? Results.NotFound() : Results.Ok(ADto(tarea));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        // documentoId es opcional (Minimal API lo deja null si no viene en la query string):
        // sin él, el comportamiento es idéntico al GET /tareas de siempre; con él, delega en
        // ListarPorDocumentoAsync (D11/D13 del spec: el endpoint vive en Tareas, no en
        // Documentos). Mismo permiso (GestionarTareas) en los dos casos.
        group.MapGet("/", async (int? documentoId, ITareaService service) =>
        {
            var tareas = documentoId is int did
                ? await service.ListarPorDocumentoAsync(did)
                : await service.ListarAsync();
            return Results.Ok(tareas.Select(ADto));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPut("/{id:int}/clasificacion", async (int id, ClasificarTareaRequest request, ITareaService service) =>
        {
            await service.ReclasificarAsync(id, new DatosClasificacionTarea(
                request.ZonaId, request.DimensionTematicaId, request.OrganismoResponsableId,
                request.OrigenFinanciamientoId, request.DocumentoAdministrativoId));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);

        group.MapPost("/{id:int}/tomar", async (int id, ITareaService service) =>
        {
            await service.TomarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/soltar", async (int id, ITareaService service) =>
        {
            await service.SoltarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/terminar", async (int id, ITareaService service) =>
        {
            await service.TerminarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/cancelar", async (int id, ITareaService service) =>
        {
            await service.CancelarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);

        group.MapPost("/{id:int}/prioridad", async (int id, CambiarPrioridadRequest request, ITareaService service) =>
        {
            await service.CambiarPrioridadAsync(id, request.Prioridad);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);

        group.MapPost("/{id:int}/notas", async (int id, AgregarNotaRequest request, ITareaService service) =>
        {
            await service.AgregarNotaAsync(id, request.Texto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static TareaDto ADto(Tarea t) => new(
        t.Id, t.Titulo, t.Descripcion,
        t.Estado, t.Prioridad, t.FechaLimite,
        t.CreadaPorUsuarioId, t.FechaCreacion,
        t.TomadaPorUsuarioId, t.TomadaPor?.NombreUsuario, t.FechaInicio,
        t.CerradaPorUsuarioId, t.FechaFin,
        t.ZonaId, t.Zona?.Nombre,
        t.DimensionTematicaId, t.DimensionTematica?.Nombre,
        t.OrganismoResponsableId, t.OrganismoResponsable?.Nombre,
        t.OrigenFinanciamientoId, t.OrigenFinanciamiento?.Nombre,
        t.DocumentoAdministrativoId, t.DocumentoAdministrativo?.Numero,
        t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaTareaDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
}
