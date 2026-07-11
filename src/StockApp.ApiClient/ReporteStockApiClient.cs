using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;

namespace StockApp.ApiClient;

/// <summary>IReporteStockService contra /reportes/* (Admin-only: reportes.ver).</summary>
public sealed class ReporteStockApiClient : IReporteStockService
{
    private readonly HttpClient _http;

    public ReporteStockApiClient(HttpClient http) => _http = http;

    public async Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/valorizacion"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el reporte de valorización.");
    }

    public async Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/stock-por-categoria"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<StockCategoriaDto>>() ?? new();
    }

    public async Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)),
            ("topN", topN.ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/mas-movidos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>() ?? new();
    }

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
        int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)));

        var response = await ApiErrores.EnviarAsync(() =>
            _http.GetAsync($"reportes/historial-producto/{productoId}" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>() ?? new();
    }
}
