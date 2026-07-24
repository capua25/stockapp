using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IImportacionService contra /finanzas/importar/*. Sin registros Wire: los DTOs de
/// StockApp.Application.Finanzas ya son la forma de wire (mismo criterio que
/// FinanzasVistasApiClient) — no hace falta remapear.
/// </summary>
public sealed class ImportacionApiClient : IImportacionService
{
    private readonly HttpClient _http;

    public ImportacionApiClient(HttpClient http) => _http = http;

    public async Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio)
    {
        using var multipart = new MultipartFormDataContent();

        using var archivoGastos = new ByteArrayContent(gastosOds);
        archivoGastos.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoGastos, "gastos", nombreArchivoGastos);

        using var archivoPoa = new ByteArrayContent(poaOds);
        archivoPoa.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoPoa, "poa", nombreArchivoPoa);

        multipart.Add(
            new StringContent(ejercicio.ToString(CultureInfo.InvariantCulture)), "ejercicio");

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("finanzas/importar/analizar", multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoAnalisisDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al analizar la importación.");
    }

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/importar/confirmar", dto));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al confirmar la importación.");
    }

    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"finanzas/importar/revertir/{idImportacion}", null));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoReversionDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al revertir la importación.");
    }

    public async Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/importar/historial"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>() ?? new();
    }
}
