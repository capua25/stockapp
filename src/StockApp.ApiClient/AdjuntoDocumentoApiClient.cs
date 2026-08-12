using System.Net.Http.Json;
using StockApp.Application.Documentos;

namespace StockApp.ApiClient;

/// <summary>
/// IAdjuntoDocumentoService contra /documentos/.../adjuntos. Mismo patrón multipart (upload) y
/// descarga de bytes crudos que AdjuntoApiClient (Finanzas) — ver ese archivo para el porqué del
/// FileNameStar preferido sobre FileName al leer Content-Disposition.
/// </summary>
public sealed class AdjuntoDocumentoApiClient : IAdjuntoDocumentoService
{
    private readonly HttpClient _http;

    public AdjuntoDocumentoApiClient(HttpClient http) => _http = http;

    public async Task<AdjuntoDocumentoDto> AgregarAsync(int documentoId, string nombreArchivo, byte[] contenido)
    {
        using var multipart = new MultipartFormDataContent();
        using var archivo = new ByteArrayContent(contenido);
        multipart.Add(archivo, "archivo", nombreArchivo);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync($"documentos/{documentoId}/adjuntos", multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<AdjuntoDocumentoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al subir el adjunto.");
    }

    public async Task<IReadOnlyList<AdjuntoDocumentoDto>> ListarPorDocumentoAsync(int documentoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/{documentoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDocumentoDto>>() ?? new List<AdjuntoDocumentoDto>();
    }

    public async Task<AdjuntoDocumentoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"documentos/adjuntos/{adjuntoId}/contenido"));
        await ApiErrores.AsegurarExitoAsync(response);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? "adjunto";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        return new AdjuntoDocumentoContenidoDto(nombreArchivo, contentType, bytes);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"documentos/adjuntos/{adjuntoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
