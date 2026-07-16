using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearFuenteFinanciamientoRequest(string Nombre);
public record ModificarFuenteFinanciamientoRequest(string Nombre);
public record FuenteFinanciamientoDto(int Id, string Nombre, bool Activo);

public static class FuentesFinanciamientoEndpoints
{
    public static IEndpointRouteBuilder MapFuentesFinanciamientoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/fuentes");

        group.MapGet("/", async (IFuenteFinanciamientoService fuentes) =>
            Results.Ok((await fuentes.ListarTodasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearFuenteFinanciamientoRequest request, IFuenteFinanciamientoService fuentes) =>
        {
            var id = await fuentes.AltaAsync(new FuenteFinanciamiento { Nombre = request.Nombre });
            // Sin Location: no existe GET /finanzas/fuentes/{id} (mismo criterio que /categorias).
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarFuenteFinanciamientoRequest request, IFuenteFinanciamientoService fuentes) =>
        {
            await fuentes.ModificarAsync(new FuenteFinanciamiento { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, IFuenteFinanciamientoService fuentes) =>
        {
            await fuentes.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activas", async (IFuenteFinanciamientoService fuentes) =>
            Results.Ok((await fuentes.ListarActivasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static FuenteFinanciamientoDto ADto(FuenteFinanciamiento f) => new(f.Id, f.Nombre, f.Activo);
}
