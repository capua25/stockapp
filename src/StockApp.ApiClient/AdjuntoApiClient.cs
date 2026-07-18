using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IAdjuntoService contra /finanzas/.../adjuntos. Primer cliente que sube multipart/form-data
/// (upload) y descarga bytes crudos (download) — el resto de los XxxApiClient son JSON puro.
/// </summary>
public sealed class AdjuntoApiClient : IAdjuntoService
{
    private readonly HttpClient _http;

    public AdjuntoApiClient(HttpClient http) => _http = http;

    public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido)
        => SubirAsync($"finanzas/gastos/{gastoId}/adjuntos", nombreArchivo, contenido);

    public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido)
        => SubirAsync($"finanzas/pagos/{pagoGastoId}/adjuntos", nombreArchivo, contenido);

    private async Task<AdjuntoDto> SubirAsync(string ruta, string nombreArchivo, byte[] contenido)
    {
        using var multipart = new MultipartFormDataContent();
        using var archivo = new ByteArrayContent(contenido);
        multipart.Add(archivo, "archivo", nombreArchivo);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<AdjuntoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al subir el adjunto.");
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/gastos/{gastoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>() ?? new List<AdjuntoDto>();
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/pagos/{pagoGastoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>() ?? new List<AdjuntoDto>();
    }

    public async Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/adjuntos/{adjuntoId}/contenido"));
        await ApiErrores.AsegurarExitoAsync(response);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var nombreArchivo = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "adjunto";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        return new AdjuntoContenidoDto(nombreArchivo, contentType, bytes);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/adjuntos/{adjuntoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
