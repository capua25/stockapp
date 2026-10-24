using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record PagoGastoDto(
    int Id, DateTime Fecha, decimal Monto, string? Nota, bool Activo,
    Caja Caja, int FuenteFinanciamientoId, string? FuenteNombre, bool EsAutomatico);

public record GastoDto(
    int Id,
    int ProveedorId, string? ProveedorNombre,
    string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino,
    DateTime Fecha, decimal MontoTotal,
    int RubroGastoId, string? RubroNombre,
    int? LineaPoaId, string? LineaPoaNombre,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    bool Activo,
    decimal TotalPagado,
    string Estado,                       // calculado por el servidor (DateTime.UtcNow)
    bool TieneMovimientosDeStock,
    List<PagoGastoDto> Pagos);

// Caja y FuenteFinanciamientoId son la caja y el literal del pago de un gasto de CONTADO: se mandan juntos
// (contado) o ninguno (crédito). Nullable a propósito: un body sin ellos NO cae en Local/0, se rechaza.
public record CrearGastoRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    Caja? Caja, int? FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<int>? MovimientoIds);

public record ModificarGastoRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    Caja? Caja, int? FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento);

public record GastoGuardadoResponse(int Id, string? AdvertenciaSobregiro);
public record RegistrarPagoRequest(DateTime Fecha, decimal Monto, string? Nota, int? FuenteFinanciamientoId);
public record PagoCreadoResponse(int Id);
public record AsociarMovimientosRequest(List<int> MovimientoIds);

public static class GastosEndpoints
{
    public static IEndpointRouteBuilder MapGastosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/gastos");

        group.MapGet("/", async (
            DateTime? fechaDesde, DateTime? fechaHasta, int? proveedorId,
            int? fuenteFinanciamientoId, int? rubroGastoId, int? lineaPoaId,
            IGastoService gastos) =>
        {
            var filtro = new GastoFiltro(
                fechaDesde, fechaHasta, proveedorId, fuenteFinanciamientoId, rubroGastoId, lineaPoaId);
            return Results.Ok((await gastos.ListarAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        group.MapGet("/{id:int}", async (int id, IGastoService gastos) =>
            Results.Ok(ADto(await gastos.ObtenerPorIdAsync(id))))
            .RequireAuthorization(Permisos.VerFinanzas);

        // Conciliación del vínculo stock: ¿ya existe la factura+orden de este proveedor?
        // numeroOrden es opcional (F5c: dos gastos activos pueden compartir factura con
        // distinto orden — sin especificarlo, se busca la variante SIN orden, no "cualquiera").
        group.MapGet("/por-factura", async (
            int proveedorId, string numeroFactura, string? numeroOrden, IGastoService gastos) =>
        {
            var gasto = await gastos.ObtenerPorProveedorYFacturaAsync(proveedorId, numeroFactura, numeroOrden);
            return gasto is null ? Results.NotFound() : Results.Ok(ADto(gasto));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        group.MapPost("/", async (CrearGastoRequest request, IGastoService gastos) =>
        {
            var resultado = await gastos.AltaAsync(
                AEntidad(request), PagoContadoDe(request.CondicionPago, request.Caja, request.FuenteFinanciamientoId), request.MovimientoIds);
            // Sin Location: no hay convención de Location en los POST del proyecto.
            return Results.Created((string?)null,
                new GastoGuardadoResponse(resultado.Id, resultado.AdvertenciaSobregiro));
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        group.MapPut("/{id:int}", async (int id, ModificarGastoRequest request, IGastoService gastos) =>
        {
            var gasto = AEntidad(new CrearGastoRequest(
                request.ProveedorId, request.NumeroFactura, request.NumeroOrden,
                request.Detalle, request.Destino, request.Fecha, request.MontoTotal,
                request.Caja, request.FuenteFinanciamientoId, request.RubroGastoId, request.LineaPoaId,
                request.CondicionPago, request.FechaVencimiento, null));
            gasto.Id = id;
            var resultado = await gastos.ModificarAsync(
                gasto, PagoContadoDe(request.CondicionPago, request.Caja, request.FuenteFinanciamientoId));
            return Results.Ok(new GastoGuardadoResponse(resultado.Id, resultado.AdvertenciaSobregiro));
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        // confirmar (default false): la anulación en cascada del pago automático de contado
        // exige confirmación EXPLÍCITA del cliente — sin ella, AnularAsync lanza
        // AnulacionRequierePagoAutomaticoConfirmadoException (409 con gastoId/montoPagoAutomatico
        // como extensión del problem+json) en vez de destruir el pago en silencio.
        group.MapDelete("/{id:int}", async (int id, IGastoService gastos, bool confirmar = false) =>
        {
            await gastos.AnularAsync(id, confirmarAnulacionDePagoAutomatico: confirmar);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        group.MapPost("/{id:int}/pagos", async (int id, RegistrarPagoRequest request, IGastoService gastos) =>
        {
            var pagoId = await gastos.RegistrarPagoAsync(new PagoGasto
            {
                GastoId = id, Fecha = request.Fecha, Monto = request.Monto, Nota = request.Nota,
                FuenteFinanciamientoId = CajaYLiteral.FuenteObligatoria(
                    request.FuenteFinanciamientoId, CajaYLiteral.MensajeFuenteDelPagoObligatoria),
            });
            return Results.Created((string?)null, new PagoCreadoResponse(pagoId));
        })
        .RequireAuthorization(Permisos.RegistrarPagos);

        group.MapDelete("/{id:int}/pagos/{pagoId:int}", async (int id, int pagoId, IGastoService gastos) =>
        {
            await gastos.AnularPagoAsync(id, pagoId);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarPagos);

        group.MapPost("/{id:int}/movimientos", async (int id, AsociarMovimientosRequest request, IGastoService gastos) =>
        {
            await gastos.AsociarMovimientosAsync(id, request.MovimientoIds);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        return app;
    }

    /// <summary>
    /// Caja y literal del pago de contado: ambos o ninguno (un crédito no los lleva). Una mitad sola, o un
    /// contado sin ninguno, es un body incompleto (400): nunca se completa con Local ni con 0.
    /// </summary>
    internal static PagoContadoDto? PagoContadoDe(CondicionPago condicion, Caja? caja, int? fuenteFinanciamientoId)
    {
        if (caja is null && fuenteFinanciamientoId is null)
            return condicion == CondicionPago.Contado
                ? throw new ArgumentException(CajaYLiteral.MensajeContadoSinCajaOFuente)
                : null;
        if (caja is null || fuenteFinanciamientoId is null)
            throw new ArgumentException(CajaYLiteral.MensajeCajaYFuenteVanJuntas);
        return new PagoContadoDto(caja.Value, fuenteFinanciamientoId.Value);
    }

    private static Gasto AEntidad(CrearGastoRequest r) => new()
    {
        ProveedorId = r.ProveedorId,
        NumeroFactura = string.IsNullOrWhiteSpace(r.NumeroFactura) ? null : r.NumeroFactura.Trim(),
        NumeroOrden = string.IsNullOrWhiteSpace(r.NumeroOrden) ? null : r.NumeroOrden.Trim(),
        Detalle = r.Detalle,
        Destino = r.Destino,
        Fecha = r.Fecha,
        MontoTotal = r.MontoTotal,
        RubroGastoId = r.RubroGastoId,
        LineaPoaId = r.LineaPoaId,
        CondicionPago = r.CondicionPago,
        FechaVencimiento = r.FechaVencimiento,
    };

    private static GastoDto ADto(Gasto g) => new(
        g.Id,
        g.ProveedorId, g.Proveedor?.Nombre,
        g.NumeroFactura, g.NumeroOrden,
        g.Detalle, g.Destino,
        g.Fecha, g.MontoTotal,
        g.RubroGastoId, g.RubroGasto?.Nombre,
        g.LineaPoaId, g.LineaPoa?.Nombre,
        g.CondicionPago, g.FechaVencimiento,
        g.Activo,
        g.TotalPagado,
        g.CalcularEstado(DateTime.UtcNow).ToString(),
        g.TieneMovimientosDeStock,
        g.Pagos.OrderBy(p => p.Fecha).ThenBy(p => p.Id)
            .Select(p => new PagoGastoDto(
                p.Id, p.Fecha, p.Monto, p.Nota, p.Activo,
                p.Caja, p.FuenteFinanciamientoId, p.FuenteFinanciamiento?.Nombre, p.EsAutomatico))
            .ToList());
}
