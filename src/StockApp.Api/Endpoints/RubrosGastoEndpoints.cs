using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearRubroGastoRequest(int Codigo, string Nombre);
public record ModificarRubroGastoRequest(int Codigo, string Nombre);
public record RubroGastoDto(int Id, int Codigo, string Nombre, bool Activo);

public static class RubrosGastoEndpoints
{
    public static IEndpointRouteBuilder MapRubrosGastoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/rubros");

        group.MapGet("/", async (IRubroGastoService rubros) =>
            Results.Ok((await rubros.ListarTodosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearRubroGastoRequest request, IRubroGastoService rubros) =>
        {
            var id = await rubros.AltaAsync(new RubroGasto { Codigo = request.Codigo, Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarRubroGastoRequest request, IRubroGastoService rubros) =>
        {
            await rubros.ModificarAsync(new RubroGasto { Id = id, Codigo = request.Codigo, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, IRubroGastoService rubros) =>
        {
            await rubros.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activos", async (IRubroGastoService rubros) =>
            Results.Ok((await rubros.ListarActivosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static RubroGastoDto ADto(RubroGasto r) => new(r.Id, r.Codigo, r.Nombre, r.Activo);
}
