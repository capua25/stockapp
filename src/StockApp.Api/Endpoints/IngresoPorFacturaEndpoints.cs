using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record ProductoNuevoRequest(
    string Codigo, string Nombre, int? CategoriaId, int UnidadMedidaId, decimal PrecioVenta);

public record RenglonFacturaRequest(
    int? ProductoId,
    ProductoNuevoRequest? ProductoNuevo,
    decimal Cantidad,
    decimal PrecioUnitario,
    bool ActualizarPrecioCosto);

public record IngresoPorFacturaRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    DateTime Fecha, string Detalle, string? Destino, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<RenglonFacturaRequest> Lineas);

public static class IngresoPorFacturaEndpoints
{
    public static IEndpointRouteBuilder MapIngresoPorFacturaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/movimientos/ingreso-factura");

        group.MapPost("/", async (IngresoPorFacturaRequest request, IIngresoPorFacturaService service) =>
        {
            var dto = new IngresoPorFacturaDto(
                request.ProveedorId, request.NumeroFactura, request.NumeroOrden,
                request.Fecha, request.Detalle, request.Destino, request.MontoTotal,
                request.FuenteFinanciamientoId, request.RubroGastoId, request.LineaPoaId,
                request.CondicionPago, request.FechaVencimiento,
                request.Lineas.Select(l => new RenglonFacturaDto(
                    l.ProductoId,
                    l.ProductoNuevo is null ? null : new ProductoNuevoDto(
                        l.ProductoNuevo.Codigo, l.ProductoNuevo.Nombre, l.ProductoNuevo.CategoriaId,
                        l.ProductoNuevo.UnidadMedidaId, l.ProductoNuevo.PrecioVenta),
                    l.Cantidad, l.PrecioUnitario, l.ActualizarPrecioCosto)).ToList());

            var resultado = await service.RegistrarAsync(dto);
            // Sin Location: mismo criterio que /movimientos y /finanzas/gastos (no hay
            // convención de Location en los POST del proyecto).
            return Results.Created((string?)null, resultado);
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        // confirmar (default false): mismo criterio que DELETE /finanzas/gastos/{id} — la
        // anulación en cascada del pago automático de contado exige confirmación explícita.
        group.MapPost("/{gastoId:int}/anular", async (int gastoId, IIngresoPorFacturaService service, bool confirmar = false) =>
        {
            await service.AnularLoteAsync(gastoId, confirmarAnulacionDePagoAutomatico: confirmar);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        return app;
    }
}
