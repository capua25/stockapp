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

    public Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio) => throw new NotImplementedException();

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
        => throw new NotImplementedException();

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
