using System.Net.Http.Json;
using StockApp.Application.Alertas;

namespace StockApp.ApiClient;

internal sealed record GuardarConfiguracionAlertasBody(string? UrlWebhook, bool Habilitado);

internal sealed record ProbarConfiguracionAlertasBody(string? UrlWebhook);

/// <summary>IConfiguracionAlertasService contra /configuracion/alertas.</summary>
public sealed class ConfiguracionAlertasApiClient : IConfiguracionAlertasService
{
    private readonly HttpClient _http;

    public ConfiguracionAlertasApiClient(HttpClient http) => _http = http;

    public async Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("configuracion/alertas", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<ConfiguracionAlertasDto>(cancellationToken: ct)
               ?? new ConfiguracionAlertasDto(null, false, null);
    }

    public async Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync(
            "configuracion/alertas", new GuardarConfiguracionAlertasBody(urlWebhook, habilitado), ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<ResultadoPruebaAlertaDto> ProbarAsync(
        string? urlWebhook = null, CancellationToken ct = default)
    {
        // Siempre se manda body (aunque UrlWebhook sea null): el endpoint lo acepta vacío igual,
        // pero mandarlo siempre evita dos formas distintas de la misma llamada.
        var response = await ApiErrores.EnviarAsync(
            () => _http.PostAsJsonAsync(
                "configuracion/alertas/probar", new ProbarConfiguracionAlertasBody(urlWebhook), ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<ResultadoPruebaAlertaDto>(cancellationToken: ct)
               ?? new ResultadoPruebaAlertaDto(false, null, "Respuesta vacía del servidor.");
    }
}
