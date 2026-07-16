using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record AsignacionPresupuestalRequest(int FuenteFinanciamientoId, decimal Monto);
public record CrearLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones);
public record ModificarLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones);
public record AsignacionPresupuestalDto(int Id, int FuenteFinanciamientoId, string? FuenteFinanciamientoNombre, decimal Monto);
public record LineaPoaDto(int Id, string Nombre, string Programa, int Ejercicio, bool Activo, List<AsignacionPresupuestalDto> Asignaciones);

public static class LineasPoaEndpoints
{
    public static IEndpointRouteBuilder MapLineasPoaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/lineas-poa");

        group.MapGet("/", async (ILineaPoaService lineas) =>
            Results.Ok((await lineas.ListarTodasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearLineaPoaRequest request, ILineaPoaService lineas) =>
        {
            var id = await lineas.AltaAsync(AEntidad(0, request.Nombre, request.Programa, request.Ejercicio, request.Asignaciones));
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarLineaPoaRequest request, ILineaPoaService lineas) =>
        {
            await lineas.ModificarAsync(AEntidad(id, request.Nombre, request.Programa, request.Ejercicio, request.Asignaciones));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, ILineaPoaService lineas) =>
        {
            await lineas.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activas", async (ILineaPoaService lineas) =>
            Results.Ok((await lineas.ListarActivasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static LineaPoa AEntidad(int id, string nombre, string programa, int ejercicio,
        List<AsignacionPresupuestalRequest> asignaciones) => new()
    {
        Id = id,
        Nombre = nombre,
        Programa = programa,
        Ejercicio = ejercicio,
        Asignaciones = (asignaciones ?? [])
            .Select(a => new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList(),
    };

    private static LineaPoaDto ADto(LineaPoa l) => new(
        l.Id, l.Nombre, l.Programa, l.Ejercicio, l.Activo,
        l.Asignaciones
            .Select(a => new AsignacionPresupuestalDto(
                a.Id, a.FuenteFinanciamientoId, a.FuenteFinanciamiento?.Nombre, a.Monto))
            .ToList());
}
