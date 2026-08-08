using System.Net.Http.Json;
using System.Threading;
using StockApp.Application.Backups;

namespace StockApp.ApiClient;

/// <summary>IBackupsService contra /backups. Usa el HttpClient keyed "Descargas" (Task 7,
/// App.axaml.cs) — timeout extendido, necesario para el body de DescargarAsync (dump de
/// varios MB/GB), y por simplicidad se usa el mismo cliente también para Listar/Salud (bodies
/// chicos, el timeout extendido no los perjudica).</summary>
public sealed class BackupsApiClient : IBackupsService
{
    private readonly HttpClient _http;

    public BackupsApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default)
    {
        // ct viaja DOBLE: al propio GetAsync (corta la llamada HTTP) y a EnviarAsync (para que
        // distinga "yo lo cancelé" de "se venció el timeout" — ver Step 1 de este Task).
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("backups", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<CorridaBackupDto>>(cancellationToken: ct) ?? new();
    }

    public async Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(
            () => _http.GetAsync($"backups/{id}/contenido", HttpCompletionOption.ResponseHeadersRead, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? $"backup_{id}.dump";

        // Si el servidor ya mandó headers y se cancela DESPUÉS (mientras se lee el body), no
        // pasa por ApiErrores — ReadAsStreamAsync(ct) y el CopyToAsync de GuardarBytesAsync
        // (Task 7) lanzan OperationCanceledException directo, sin envoltorio.
        var contenido = await response.Content.ReadAsStreamAsync(ct);
        return new BackupDescargaDto(nombreArchivo, contenido);
    }

    public async Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("backups/salud", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<SaludBackupDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener la salud del backup.");
    }

    public async Task IniciarAsync(CancellationToken ct = default)
    {
        // Sin body: POST /backups no espera nada del cliente, el usuario viaja en el JWT.
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync("backups", null, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
