using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearUnidadMedidaRequest(string Nombre, string Abreviatura);
public record ModificarUnidadMedidaRequest(int Id, string Nombre, string Abreviatura);

public static class UnidadesMedidaEndpoints
{
    public static IEndpointRouteBuilder MapUnidadesMedidaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/unidades-medida");

        group.MapGet("/", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearUnidadMedidaRequest request, IUnidadMedidaService unidades) =>
        {
            var unidad = new UnidadMedida { Nombre = request.Nombre, Abreviatura = request.Abreviatura };
            var id = await unidades.AltaAsync(unidad);
            return Results.Created($"/unidades-medida/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarUnidadMedidaRequest request, IUnidadMedidaService unidades) =>
        {
            var unidad = new UnidadMedida { Id = id, Nombre = request.Nombre, Abreviatura = request.Abreviatura };
            await unidades.ModificarAsync(unidad);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IUnidadMedidaService unidades) =>
        {
            await unidades.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/activas", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
