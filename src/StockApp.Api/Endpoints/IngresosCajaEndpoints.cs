using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record IngresoCajaDto(
    int Id, DateTime Fecha, string Concepto,
    int FuenteFinanciamientoId, string? FuenteNombre,
    decimal Monto, bool Activo, Caja Caja);

public record CrearIngresoCajaRequest(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto, Caja Caja);

public record ModificarIngresoCajaRequest(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto, Caja Caja);

public static class IngresosCajaEndpoints
{
    public static IEndpointRouteBuilder MapIngresosCajaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/ingresos");

        group.MapGet("/", async (IIngresoCajaService ingresos) =>
            Results.Ok((await ingresos.ListarTodosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        group.MapPost("/", async (CrearIngresoCajaRequest request, IIngresoCajaService ingresos) =>
        {
            var id = await ingresos.AltaAsync(new IngresoCaja
            {
                Fecha = request.Fecha,
                Concepto = request.Concepto,
                FuenteFinanciamientoId = request.FuenteFinanciamientoId,
                Monto = request.Monto,
                Caja = request.Caja,
            });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        group.MapPut("/{id:int}", async (int id, ModificarIngresoCajaRequest request, IIngresoCajaService ingresos) =>
        {
            await ingresos.ModificarAsync(new IngresoCaja
            {
                Id = id,
                Fecha = request.Fecha,
                Concepto = request.Concepto,
                FuenteFinanciamientoId = request.FuenteFinanciamientoId,
                Monto = request.Monto,
                Caja = request.Caja,
            });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        group.MapDelete("/{id:int}", async (int id, IIngresoCajaService ingresos) =>
        {
            await ingresos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        return app;
    }

    private static IngresoCajaDto ADto(IngresoCaja i) => new(
        i.Id, i.Fecha, i.Concepto,
        i.FuenteFinanciamientoId, i.FuenteFinanciamiento?.Nombre,
        i.Monto, i.Activo, i.Caja);
}
