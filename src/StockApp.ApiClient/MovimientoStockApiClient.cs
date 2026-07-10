// src/StockApp.ApiClient/MovimientoStockApiClient.cs
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Movimientos;

namespace StockApp.ApiClient;

internal sealed record RegistrarMovimientoBody(
    int ProductoId,
    StockApp.Domain.Enums.TipoMovimiento Tipo,
    StockApp.Domain.Enums.MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal? PrecioUnitario,
    string? Comentario,
    bool Forzar);

/// <summary>
/// IMovimientoStockService contra /movimientos y /productos/{id}/recalcular-stock.
/// El parámetro forzar (RM-09) viaja dentro del body del POST (contrato de 2b). Un 409
/// por stock insuficiente vuelve como StockInsuficienteException tipada (via ApiErrores +
/// extensiones del problem+json, Task 5) para no romper el flujo "¿forzar salida?" del VM.
/// </summary>
public sealed class MovimientoStockApiClient : IMovimientoStockService
{
    private readonly HttpClient _http;

    public MovimientoStockApiClient(HttpClient http) => _http = http;

    public async Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
    {
        var body = new RegistrarMovimientoBody(
            dto.ProductoId, dto.Tipo, dto.Motivo, dto.Cantidad, dto.PrecioUnitario, dto.Comentario, forzar);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("movimientos", body));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<MovimientoRegistradoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el movimiento.");
    }

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
    {
        var query = ApiQuery.Construir(
            ("productoId", filtro.ProductoId?.ToString(CultureInfo.InvariantCulture)),
            ("tipo", filtro.Tipo is null ? null : ((int)filtro.Tipo.Value).ToString(CultureInfo.InvariantCulture)),
            ("fechaDesde", ApiQuery.Fecha(filtro.FechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(filtro.FechaHasta)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("movimientos/historial" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>() ?? new();
    }

    public async Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"productos/{productoId}/recalcular-stock", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<RecalculoResultadoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al recalcular el stock.");
    }
}
