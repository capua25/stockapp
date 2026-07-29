using System.Net.Http.Json;
using System.Threading;
using StockApp.Application.Logs;

namespace StockApp.ApiClient;

/// <summary>ILogsService contra /logs (Task 7, grupo de diagnóstico).</summary>
public sealed class LogsApiClient : ILogsService
{
    private readonly HttpClient _http;

    public LogsApiClient(HttpClient http) => _http = http;

    public async Task<ResumenLogsDto> ObtenerResumenAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("logs", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResumenLogsDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException(
                "Respuesta vacía del servidor al obtener el resumen de logs.");
    }

    public async Task<LogsDescargaDto> DescargarZipAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(
            () => _http.GetAsync("logs/contenido", HttpCompletionOption.ResponseHeadersRead, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? "logs.zip";

        var contenido = await response.Content.ReadAsStreamAsync(ct);
        return new LogsDescargaDto(nombreArchivo, contenido);
    }
}
