using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearCategoriaRequest(string Nombre);
public record ModificarCategoriaRequest(string Nombre);

/// <summary>
/// DTO de lectura de Categoria (Fase 3a, D3). Reemplaza la entidad de dominio cruda en las
/// responses de GET: una nav property futura en Categoria ya no puede cambiar el contrato
/// HTTP silenciosamente.
/// </summary>
public record CategoriaDto(int Id, string Nombre, bool Activo);

public static class CategoriasEndpoints
{
    public static IEndpointRouteBuilder MapCategoriasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categorias");

        group.MapGet("/", async (ICategoriaService categorias) =>
            Results.Ok((await categorias.ListarTodasAsync()).Select(ACategoriaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearCategoriaRequest request, ICategoriaService categorias) =>
        {
            var id = await categorias.AltaAsync(new Categoria { Nombre = request.Nombre });
            // Sin Location: no existe GET /categorias/{id} (el GET del recurso es la lista
            // completa o /activas) — emitir una ruta que no resuelve es peor que omitirla.
            return Results.Created((string?)null, new { id });
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
            Results.Ok((await categorias.ListarActivasAsync()).Select(ACategoriaDto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }

    private static CategoriaDto ACategoriaDto(Categoria c) => new(c.Id, c.Nombre, c.Activo);
}
