using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;

namespace StockApp.Api.Endpoints;

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(null)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/{id:int}/recalcular-stock", async (int id, IMovimientoStockService movimientos) =>
            Results.Ok(await movimientos.RecalcularStockAsync(id)))
            .RequireAuthorization(Permisos.RecalcularStock);

        return app;
    }
}
