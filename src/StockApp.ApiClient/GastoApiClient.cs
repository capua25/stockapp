using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

internal sealed record PagoGastoWire(int Id, DateTime Fecha, decimal Monto, string? Nota, bool Activo);

internal sealed record GastoWire(
    int Id,
    int ProveedorId, string? ProveedorNombre,
    string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino,
    DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, string? FuenteNombre,
    int RubroGastoId, string? RubroNombre,
    int? LineaPoaId, string? LineaPoaNombre,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    bool Activo, decimal TotalPagado, string Estado,
    List<PagoGastoWire> Pagos);

internal sealed record GastoBody(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<int>? MovimientoIds);

internal sealed record GastoGuardadoWire(int Id, string? AdvertenciaSobregiro);
internal sealed record RegistrarPagoBody(DateTime Fecha, decimal Monto, string? Nota);
internal sealed record AsociarMovimientosBody(List<int> MovimientoIds);

/// <summary>
/// IGastoService contra /finanzas/gastos. Los nombres de proveedor/fuente/rubro/línea
/// vuelven materializados en las navs (para las grillas, sin otra llamada); el estado
/// lo recalcula la entidad en el cliente a partir de los mismos datos.
/// </summary>
public sealed class GastoApiClient : IGastoService
{
    private readonly HttpClient _http;

    public GastoApiClient(HttpClient http) => _http = http;

    public async Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/gastos", ABody(gasto, movimientoIds)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el gasto.");
        return new ResultadoGastoDto(creado.Id, creado.AdvertenciaSobregiro);
    }

    public async Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/gastos/{gasto.Id}", ABody(gasto, movimientoIds: null)));
        await ApiErrores.AsegurarExitoAsync(response);

        var guardado = await response.Content.ReadFromJsonAsync<GastoGuardadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al modificar el gasto.");
        return new ResultadoGastoDto(guardado.Id, guardado.AdvertenciaSobregiro);
    }

    public async Task AnularAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/gastos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<Gasto> ObtenerPorIdAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/gastos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dto = await response.Content.ReadFromJsonAsync<GastoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el gasto.");
        return AEntidad(dto);
    }

    public async Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura, string? numeroOrden)
    {
        var query = ApiQuery.Construir(
            ("proveedorId", proveedorId.ToString(CultureInfo.InvariantCulture)),
            ("numeroFactura", numeroFactura),
            ("numeroOrden", numeroOrden));
        try
        {
            var response = await ApiErrores.EnviarAsync(() =>
                _http.GetAsync("finanzas/gastos/por-factura" + query));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<GastoWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = no existe la factura: contrato de la interfaz (null)
        }
    }

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(filtro.FechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(filtro.FechaHasta)),
            ("proveedorId", filtro.ProveedorId?.ToString(CultureInfo.InvariantCulture)),
            ("fuenteFinanciamientoId", filtro.FuenteFinanciamientoId?.ToString(CultureInfo.InvariantCulture)),
            ("rubroGastoId", filtro.RubroGastoId?.ToString(CultureInfo.InvariantCulture)),
            ("lineaPoaId", filtro.LineaPoaId?.ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/gastos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<GastoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"finanzas/gastos/{pago.GastoId}/pagos",
                new RegistrarPagoBody(pago.Fecha, pago.Monto, pago.Nota)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el pago.");
        return creado.Id;
    }

    public async Task AnularPagoAsync(int gastoId, int pagoId)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.DeleteAsync($"finanzas/gastos/{gastoId}/pagos/{pagoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"finanzas/gastos/{gastoId}/movimientos",
                new AsociarMovimientosBody(movimientoIds.ToList())));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static GastoBody ABody(Gasto gasto, IReadOnlyList<int>? movimientoIds) => new(
        gasto.ProveedorId, gasto.NumeroFactura, gasto.NumeroOrden,
        gasto.Detalle, gasto.Destino, gasto.Fecha, gasto.MontoTotal,
        gasto.FuenteFinanciamientoId, gasto.RubroGastoId, gasto.LineaPoaId,
        gasto.CondicionPago, gasto.FechaVencimiento,
        movimientoIds?.ToList());

    private static Gasto AEntidad(GastoWire dto) => new()
    {
        Id = dto.Id,
        ProveedorId = dto.ProveedorId,
        Proveedor = dto.ProveedorNombre is null
            ? null : new Proveedor { Id = dto.ProveedorId, Nombre = dto.ProveedorNombre },
        NumeroFactura = dto.NumeroFactura,
        NumeroOrden = dto.NumeroOrden,
        Detalle = dto.Detalle,
        Destino = dto.Destino,
        Fecha = dto.Fecha,
        MontoTotal = dto.MontoTotal,
        FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
        FuenteFinanciamiento = dto.FuenteNombre is null
            ? null : new FuenteFinanciamiento { Id = dto.FuenteFinanciamientoId, Nombre = dto.FuenteNombre },
        RubroGastoId = dto.RubroGastoId,
        RubroGasto = dto.RubroNombre is null
            ? null : new RubroGasto { Id = dto.RubroGastoId, Nombre = dto.RubroNombre },
        LineaPoaId = dto.LineaPoaId,
        LineaPoa = dto.LineaPoaId is null || dto.LineaPoaNombre is null
            ? null : new LineaPoa { Id = dto.LineaPoaId.Value, Nombre = dto.LineaPoaNombre },
        CondicionPago = dto.CondicionPago,
        FechaVencimiento = dto.FechaVencimiento,
        Activo = dto.Activo,
        Pagos = dto.Pagos.Select(p => new PagoGasto
        {
            Id = p.Id, GastoId = dto.Id, Fecha = p.Fecha, Monto = p.Monto, Nota = p.Nota, Activo = p.Activo,
        }).ToList(),
    };
}
