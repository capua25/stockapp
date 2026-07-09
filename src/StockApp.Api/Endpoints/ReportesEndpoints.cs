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
            DateTime? fechaDesde, DateTime? fechaHasta, int topN, IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN == 0 ? 20 : topN)));

        group.MapGet("/historial-producto/{productoId:int}", async (
            int productoId, DateTime? fechaDesde, DateTime? fechaHasta, IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerHistorialPorProductoAsync(productoId, fechaDesde, fechaHasta)));

        return app;
    }
}
