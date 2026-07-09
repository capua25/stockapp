using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearProductoRequest(
    string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);

public record ModificarProductoRequest(
    int Id, string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);

public record CambiarPrecioRequest(decimal PrecioCosto, decimal PrecioVenta);

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (string? texto, IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(texto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/", async (CrearProductoRequest request, IProductoService productos) =>
        {
            var producto = new Producto
            {
                Codigo = request.Codigo,
                CodigoBarras = request.CodigoBarras,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                CategoriaId = request.CategoriaId,
                ProveedorId = request.ProveedorId,
                UnidadMedidaId = request.UnidadMedidaId,
                PrecioCosto = request.PrecioCosto,
                PrecioVenta = request.PrecioVenta,
                StockMinimo = request.StockMinimo,
            };

            var id = await productos.AltaAsync(producto);
            return Results.Created($"/productos/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPut("/{id:int}", async (int id, ModificarProductoRequest request, IProductoService productos) =>
        {
            var producto = new Producto
            {
                Id = id,
                Codigo = request.Codigo,
                CodigoBarras = request.CodigoBarras,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                CategoriaId = request.CategoriaId,
                ProveedorId = request.ProveedorId,
                UnidadMedidaId = request.UnidadMedidaId,
                PrecioCosto = request.PrecioCosto,
                PrecioVenta = request.PrecioVenta,
                StockMinimo = request.StockMinimo,
            };

            await productos.ModificarAsync(producto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapDelete("/{id:int}", async (int id, IProductoService productos) =>
        {
            await productos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPut("/{id:int}/precio", async (int id, CambiarPrecioRequest request, IProductoService productos) =>
        {
            await productos.CambiarPrecioAsync(id, request.PrecioCosto, request.PrecioVenta);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/{id:int}/recalcular-stock", async (int id, IMovimientoStockService movimientos) =>
            Results.Ok(await movimientos.RecalcularStockAsync(id)))
            .RequireAuthorization(Permisos.RecalcularStock);

        return app;
    }
}
