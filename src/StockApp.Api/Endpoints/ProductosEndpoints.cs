using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Reportes;

namespace StockApp.Api.Endpoints;

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(null)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapGet("/reporte-valorizacion", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerValorizacionAsync()))
            .RequireAuthorization(Permisos.VerReportes);

        return app;
    }
}
