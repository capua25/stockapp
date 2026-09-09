using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearDimensionTematicaRequest(string Nombre);
public record ModificarDimensionTematicaRequest(string Nombre);
public record DimensionTematicaDto(int Id, string Nombre, bool Activo);

public static class DimensionesTematicasEndpoints
{
    public static IEndpointRouteBuilder MapDimensionesTematicasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/dimensiones-tematicas");

        group.MapGet("/", async (IDimensionTematicaService dimensiones) =>
            Results.Ok((await dimensiones.ListarTodasAsync()).Select(ADimensionTematicaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearDimensionTematicaRequest request, IDimensionTematicaService dimensiones) =>
        {
            var id = await dimensiones.AltaAsync(new DimensionTematica { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarDimensionTematicaRequest request, IDimensionTematicaService dimensiones) =>
        {
            await dimensiones.ModificarAsync(new DimensionTematica { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IDimensionTematicaService dimensiones) =>
        {
            await dimensiones.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IDimensionTematicaService dimensiones) =>
            Results.Ok((await dimensiones.ListarActivasAsync()).Select(ADimensionTematicaDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static DimensionTematicaDto ADimensionTematicaDto(DimensionTematica d) => new(d.Id, d.Nombre, d.Activo);
}
