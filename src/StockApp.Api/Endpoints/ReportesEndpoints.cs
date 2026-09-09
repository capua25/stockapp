using StockApp.Application.Authorization;
using StockApp.Application.Reportes;

namespace StockApp.Api.Endpoints;

public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reportes").RequireAuthorization(Permisos.VerReportes);

        group.MapGet("/valorizacion", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerValorizacionAsync()));

        group.MapGet("/stock-por-categoria", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerStockPorCategoriaAsync()));

        group.MapGet("/mas-movidos", async (
            IReporteStockService reportes, DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20) =>
            Results.Ok(await reportes.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN)));

        group.MapGet("/historial-producto/{productoId:int}", async (
            int productoId, DateTime? fechaDesde, DateTime? fechaHasta, IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerHistorialPorProductoAsync(productoId, fechaDesde, fechaHasta)));

        group.MapGet("/tareas", async (
            IReporteTareasService reportes,
            AgrupadorTareas agrupador, CriterioFechaTareas criterio, DateTime desde, DateTime hasta) =>
        {
            // El binding de query string (a diferencia del body JSON, que pasa por
            // DateTimeUnspecifiedAsUtcConverter) deja Kind=Unspecified cuando la fecha no
            // trae offset (ej. "2026-01-01"). Npgsql rechaza escribir/comparar Unspecified
            // contra una columna timestamptz -- se reinterpreta como UTC acá, mismo criterio
            // que el converter del pipeline (SpecifyKind, no ToUniversalTime).
            var desdeUtc = DateTime.SpecifyKind(desde, DateTimeKind.Utc);
            var hastaUtc = DateTime.SpecifyKind(hasta, DateTimeKind.Utc);

            return Results.Ok(await reportes.ObtenerAsync(
                new FiltroReporteTareas(agrupador, criterio, desdeUtc, hastaUtc)));
        });

        return app;
    }
}
