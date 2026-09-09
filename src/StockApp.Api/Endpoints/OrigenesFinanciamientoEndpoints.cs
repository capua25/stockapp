using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearOrigenFinanciamientoRequest(string Nombre);
public record ModificarOrigenFinanciamientoRequest(string Nombre);
public record OrigenFinanciamientoDto(int Id, string Nombre, bool Activo);

public static class OrigenesFinanciamientoEndpoints
{
    public static IEndpointRouteBuilder MapOrigenesFinanciamientoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/origenes-financiamiento");

        group.MapGet("/", async (IOrigenFinanciamientoService origenes) =>
            Results.Ok((await origenes.ListarTodasAsync()).Select(AOrigenFinanciamientoDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearOrigenFinanciamientoRequest request, IOrigenFinanciamientoService origenes) =>
        {
            var id = await origenes.AltaAsync(new OrigenFinanciamiento { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarOrigenFinanciamientoRequest request, IOrigenFinanciamientoService origenes) =>
        {
            await origenes.ModificarAsync(new OrigenFinanciamiento { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IOrigenFinanciamientoService origenes) =>
        {
            await origenes.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IOrigenFinanciamientoService origenes) =>
            Results.Ok((await origenes.ListarActivasAsync()).Select(AOrigenFinanciamientoDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static OrigenFinanciamientoDto AOrigenFinanciamientoDto(OrigenFinanciamiento o) => new(o.Id, o.Nombre, o.Activo);
}
