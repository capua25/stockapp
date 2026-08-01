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
    List<NotaTareaDto> Notas);

public record CrearTareaRequest(string Titulo, string? Descripcion, DateTime? FechaLimite);
public record CambiarPrioridadRequest(PrioridadTarea Prioridad);
public record AgregarNotaRequest(string Texto);
public record TareaCreadaResponse(int Id);

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
            };
            var id = await service.CrearAsync(tarea);
            return Results.Created((string?)null, new TareaCreadaResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapGet("/", async (ITareaService service) =>
            Results.Ok((await service.ListarAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarTareas);

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
        t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaTareaDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
}
