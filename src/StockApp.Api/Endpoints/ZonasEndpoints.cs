using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearZonaRequest(string Nombre);
public record ModificarZonaRequest(string Nombre);
public record ZonaDto(int Id, string Nombre, bool Activo);

public static class ZonasEndpoints
{
    public static IEndpointRouteBuilder MapZonasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/zonas");

        group.MapGet("/", async (IZonaService zonas) =>
            Results.Ok((await zonas.ListarTodasAsync()).Select(AZonaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearZonaRequest request, IZonaService zonas) =>
        {
            var id = await zonas.AltaAsync(new Zona { Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarZonaRequest request, IZonaService zonas) =>
        {
            await zonas.ModificarAsync(new Zona { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IZonaService zonas) =>
        {
            await zonas.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        // Gateada con GestionarProyectos (permiso del consumidor: el formulario de proyecto, spec
        // 2026-10-30), NO GestionarTablasMaestras (permiso del administrador) ni el de Tareas —
        // mismo patrón que GET /categorias/activas con GestionarProductos.
        group.MapGet("/activas", async (IZonaService zonas) =>
            Results.Ok((await zonas.ListarActivasAsync()).Select(AZonaDto)))
            .RequireAuthorization(Permisos.GestionarProyectos);

        return app;
    }

    private static ZonaDto AZonaDto(Zona z) => new(z.Id, z.Nombre, z.Activo);
}
