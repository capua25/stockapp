using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Auditoria;

namespace StockApp.ApiClient;

/// <summary>IAuditoriaQueryService contra GET /auditoria (Admin-only: reportes.ver).</summary>
public sealed class AuditoriaQueryApiClient : IAuditoriaQueryService
{
    private readonly HttpClient _http;

    public AuditoriaQueryApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var query = ApiQuery.Construir(
            ("usuarioId", usuarioId?.ToString(CultureInfo.InvariantCulture)),
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("auditoria" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<AuditoriaItemDto>>() ?? new();
    }
}
