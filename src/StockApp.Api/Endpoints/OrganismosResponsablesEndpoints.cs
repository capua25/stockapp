using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearOrganismoResponsableRequest(string Nombre);
public record ModificarOrganismoResponsableRequest(string Nombre);
public record OrganismoResponsableDto(int Id, string Nombre, bool Activo);

public static class OrganismosResponsablesEndpoints
{
    public static IEndpointRouteBuilder MapOrganismosResponsablesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/organismos-responsables");

        group.MapGet("/", async (IOrganismoResponsableService organismos) =>
            Results.Ok((await organismos.ListarTodasAsync()).Select(AOrganismoResponsableDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearOrganismoResponsableRequest request, IOrganismoResponsableService organismos) =>
        {
            var id = await organismos.AltaAsync(new OrganismoResponsable { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarOrganismoResponsableRequest request, IOrganismoResponsableService organismos) =>
        {
            await organismos.ModificarAsync(new OrganismoResponsable { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IOrganismoResponsableService organismos) =>
        {
            await organismos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarTareas — ver nota en ZonasEndpoints.
        group.MapGet("/activas", async (IOrganismoResponsableService organismos) =>
            Results.Ok((await organismos.ListarActivasAsync()).Select(AOrganismoResponsableDto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static OrganismoResponsableDto AOrganismoResponsableDto(OrganismoResponsable o) => new(o.Id, o.Nombre, o.Activo);
}
