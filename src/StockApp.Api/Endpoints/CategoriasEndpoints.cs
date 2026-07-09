using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearCategoriaRequest(string Nombre);
public record ModificarCategoriaRequest(int Id, string Nombre);

public static class CategoriasEndpoints
{
    public static IEndpointRouteBuilder MapCategoriasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categorias");

        group.MapGet("/", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearCategoriaRequest request, ICategoriaService categorias) =>
        {
            var id = await categorias.AltaAsync(new Categoria { Nombre = request.Nombre });
            return Results.Created($"/categorias/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarCategoriaRequest request, ICategoriaService categorias) =>
        {
            await categorias.ModificarAsync(new Categoria { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, ICategoriaService categorias) =>
        {
            await categorias.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/activas", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
