using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient;

internal sealed record DesafioResetWire(string Desafio, string CodigoMaquina);
internal sealed record ResetAdminBody(string Token, string NuevaContrasena);

/// <summary>
/// IResetAdminService contra /auth/reset-admin/*. Igual que la activación de licencia, un 400
/// es flujo esperado (token/desafío inválido) → ResultadoResetDto con motivo, sin excepción.
/// </summary>
public sealed class ResetAdminApiClient : IResetAdminService
{
    private readonly HttpClient _http;

    public ResetAdminApiClient(HttpClient http) => _http = http;

    public async Task<DesafioResetDto> SolicitarDesafioAsync()
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("auth/reset-admin/desafio", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        var wire = await response.Content.ReadFromJsonAsync<DesafioResetWire>()
            ?? throw new InvalidOperationException("Respuesta vacía de /auth/reset-admin/desafio.");
        return new DesafioResetDto(wire.Desafio, wire.CodigoMaquina);
    }

    public async Task<ResultadoResetDto> ResetearAsync(string token, string nuevaContrasena)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/reset-admin", new ResetAdminBody(token, nuevaContrasena)));

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            ProblemaWire? problema;
            try { problema = await response.Content.ReadFromJsonAsync<ProblemaWire>(); }
            catch (Exception) { problema = null; }
            return new ResultadoResetDto(false, problema?.Detail ?? problema?.Title
                ?? "No se pudo resetear el Admin.");
        }

        await ApiErrores.AsegurarExitoAsync(response);
        return new ResultadoResetDto(true, null);
    }
}
