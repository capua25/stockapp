using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ZonaWire(int Id, string Nombre, bool Activo);
internal sealed record ZonaBody(string Nombre);

/// <summary>
/// IZonaService contra /zonas. La interfaz habla en entidades de dominio
/// (así la consumen los VMs) y el wire habla en ZonaDto (Task 5): este client mapea.
/// </summary>
public sealed class ZonaApiClient : IZonaService
{
    private readonly HttpClient _http;

    public ZonaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Zona zona)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("zonas", new ZonaBody(zona.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la zona.");
        return creado.Id;
    }

    public async Task ModificarAsync(Zona zona)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"zonas/{zona.Id}", new ZonaBody(zona.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"zonas/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<Zona>> ListarTodasAsync() => ListarAsync("zonas");

    public Task<IReadOnlyList<Zona>> ListarActivasAsync() => ListarAsync("zonas/activas");

    private async Task<IReadOnlyList<Zona>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<ZonaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static Zona AEntidad(ZonaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
