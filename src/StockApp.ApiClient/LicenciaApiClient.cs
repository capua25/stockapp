using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Licenciamiento;

namespace StockApp.ApiClient;

internal sealed record EstadoLicenciaWire(bool Activada, string CodigoMaquina);
internal sealed record ActivarLicenciaBody(string Licencia);
internal sealed record ProblemaWire(string? Detail, string? Title);

/// <summary>
/// ILicenciaService contra /licencia/*. La activación NO usa el mapeo de errores de ApiErrores:
/// un 400 acá es flujo esperado (licencia inválida), no una excepción — se traduce a
/// ResultadoActivacionDto con el motivo del problem+json.
/// </summary>
public sealed class LicenciaApiClient : ILicenciaService
{
    private readonly HttpClient _http;

    public LicenciaApiClient(HttpClient http) => _http = http;

    public async Task<EstadoLicenciaDto> ObtenerEstadoAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("licencia/estado"));
        await ApiErrores.AsegurarExitoAsync(response);

        var wire = await response.Content.ReadFromJsonAsync<EstadoLicenciaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía de /licencia/estado.");
        return new EstadoLicenciaDto(wire.Activada, wire.CodigoMaquina);
    }

    public async Task<ResultadoActivacionDto> ActivarAsync(string licencia)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("licencia/activar", new ActivarLicenciaBody(licencia)));

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problema = await LeerProblemaAsync(response);
            return new ResultadoActivacionDto(false, problema?.Detail ?? problema?.Title
                ?? "No se pudo activar la licencia.");
        }

        await ApiErrores.AsegurarExitoAsync(response);
        return new ResultadoActivacionDto(true, null);
    }

    private static async Task<ProblemaWire?> LeerProblemaAsync(HttpResponseMessage response)
    {
        try { return await response.Content.ReadFromJsonAsync<ProblemaWire>(); }
        catch (Exception) { return null; }
    }
}
