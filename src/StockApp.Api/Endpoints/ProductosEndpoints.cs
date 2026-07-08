using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;

namespace StockApp.Api.Endpoints;

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(null)))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
