using StockApp.Api.Json;
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
            // Medido empíricamente (repro mínimo, mismo TFM net10.0 / SDK 10.0.300 que este
            // proyecto): el binder de query string de Minimal API entrega Kind=Utc, ya
            // convertido al instante correcto, cuando la fecha trae offset o "Z" (ej.
            // "2026-01-01T00:00:00Z" o "...-03:00"), y Kind=Unspecified cuando no trae offset
            // (ej. "2026-01-01"). Un SpecifyKind(Utc) ciego NUNCA fue un bug para el caso con
            // offset -- Kind ya llega en Utc, así que reetiquetarlo es un no-op. Se reusa
            // NormalizarAUtc igual, no para corregir un defecto activo sino para no depender de
            // ese detalle del binder: unifica el criterio con el converter del pipeline JSON, y
            // si el día de mañana cambia el tipo del parámetro, el framework, o dónde viajan
            // estas fechas, este código sigue siendo correcto sin que nadie tenga que volver a
            // pensarlo.
            var desdeUtc = DateTimeUnspecifiedAsUtcConverter.NormalizarAUtc(desde);
            var hastaUtc = DateTimeUnspecifiedAsUtcConverter.NormalizarAUtc(hasta);

            return Results.Ok(await reportes.ObtenerAsync(
                new FiltroReporteTareas(agrupador, criterio, desdeUtc, hastaUtc)));
        });

        return app;
    }
}
