using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearUnidadMedidaRequest(string Nombre, string Abreviatura);
public record ModificarUnidadMedidaRequest(string Nombre, string Abreviatura);

/// <summary>DTO de lectura de UnidadMedida (Fase 3a, D3). Reemplaza la entidad de dominio cruda.</summary>
public record UnidadMedidaDto(int Id, string Nombre, string Abreviatura, bool Activo);

public static class UnidadesMedidaEndpoints
{
    public static IEndpointRouteBuilder MapUnidadesMedidaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/unidades-medida");

        group.MapGet("/", async (IUnidadMedidaService unidades) =>
            Results.Ok((await unidades.ListarTodasAsync()).Select(AUnidadMedidaDto)))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearUnidadMedidaRequest request, IUnidadMedidaService unidades) =>
        {
            var unidad = new UnidadMedida { Nombre = request.Nombre, Abreviatura = request.Abreviatura };
            var id = await unidades.AltaAsync(unidad);
            // Sin Location: no existe GET /unidades-medida/{id} (el GET del recurso es la lista
            // completa o /activas) — emitir una ruta que no resuelve es peor que omitirla.
            return Results.Created((string?)null, new { id });
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
            Results.Ok((await unidades.ListarActivasAsync()).Select(AUnidadMedidaDto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/garantizar-por-defecto", async (IUnidadMedidaService unidades) =>
            Results.Ok(AUnidadMedidaDto(await unidades.GarantizarUnidadPorDefectoAsync())))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }

    private static UnidadMedidaDto AUnidadMedidaDto(UnidadMedida u) =>
        new(u.Id, u.Nombre, u.Abreviatura, u.Activo);
}
