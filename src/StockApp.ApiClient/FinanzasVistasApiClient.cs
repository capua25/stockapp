using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IFinanzasVistasService contra /finanzas/libro-caja, /finanzas/control-poa y
/// /finanzas/calendario-pagos. A diferencia de GastoApiClient, no hace falta remapear:
/// los DTOs de Application ya son la forma de wire (records planos, sin entidades de EF).
/// </summary>
public sealed class FinanzasVistasApiClient : IFinanzasVistasService
{
    private readonly HttpClient _http;

    public FinanzasVistasApiClient(HttpClient http) => _http = http;

    public async Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
    {
        var query = ApiQuery.Construir(
            ("anio", anio.ToString(CultureInfo.InvariantCulture)),
            ("mes", mes.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/libro-caja" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<LibroCajaMesDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el libro caja del mes.");
    }

    public async Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
    {
        var query = ApiQuery.Construir(("anio", anio.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/libro-caja" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el libro caja anual.");
    }

    public async Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        var query = ApiQuery.Construir(("ejercicio", ejercicio.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/control-poa" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ControlPoaLineaDto>>() ?? new();
    }

    public async Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
    {
        // fechaReferencia NUNCA viaja: el servidor es la única autoridad de "hoy" (ver
        // decisión registrada al inicio del plan). El parámetro solo sirve para tests
        // determinísticos de FinanzasVistasService en el servidor.
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/calendario-pagos"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<CalendarioPagosDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el calendario de pagos.");
    }
}
