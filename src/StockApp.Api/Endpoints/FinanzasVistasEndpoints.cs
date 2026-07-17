using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class FinanzasVistasEndpoints
{
    public static IEndpointRouteBuilder MapFinanzasVistasEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/finanzas/libro-caja", async (int anio, int? mes, IFinanzasVistasService vistas) =>
        {
            if (mes is < 1 or > 12)
                return Results.BadRequest("El mes debe estar entre 1 y 12.");

            return mes is null
                ? Results.Ok(await vistas.ObtenerLibroCajaAnualAsync(anio))
                : Results.Ok(await vistas.ObtenerLibroCajaMesAsync(anio, mes.Value));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/control-poa", async (int ejercicio, IFinanzasVistasService vistas) =>
            Results.Ok(await vistas.ObtenerControlPoaAsync(ejercicio)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/calendario-pagos", async (IFinanzasVistasService vistas) =>
            Results.Ok(await vistas.ObtenerCalendarioPagosAsync()))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }
}
