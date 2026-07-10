using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

/// <summary>
/// Request de POST /movimientos: calca RegistrarMovimientoDto (StockApp.Application)
/// y agrega Forzar, que en la capa de aplicación viaja como segundo parámetro de
/// IMovimientoStockService.RegistrarAsync en vez de dentro del DTO.
/// </summary>
public record RegistrarMovimientoRequest(
    int ProductoId,
    TipoMovimiento Tipo,
    MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal? PrecioUnitario,
    string? Comentario,
    bool Forzar = false);

public static class MovimientosEndpoints
{
    public static IEndpointRouteBuilder MapMovimientosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/movimientos");

        group.MapPost("/", async (RegistrarMovimientoRequest request, IMovimientoStockService movimientos) =>
        {
            var dto = new RegistrarMovimientoDto(
                request.ProductoId, request.Tipo, request.Motivo,
                request.Cantidad, request.PrecioUnitario, request.Comentario);

            var registrado = await movimientos.RegistrarAsync(dto, request.Forzar);
            // Sin Location: no existe GET /movimientos/{id} (el único GET del recurso es
            // /movimientos/historial) — emitir una ruta que no resuelve es peor que omitirla
            // (review final de Fase 2b).
            return Results.Created((string?)null, registrado);
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        group.MapGet("/historial", async (
            int? productoId, TipoMovimiento? tipo, DateTime? fechaDesde, DateTime? fechaHasta,
            IMovimientoStockService movimientos) =>
        {
            var filtro = new HistorialMovimientoFiltro(productoId, tipo, fechaDesde, fechaHasta);
            return Results.Ok(await movimientos.ObtenerHistorialAsync(filtro));
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        return app;
    }
}
