using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record RubroGastoWire(int Id, int Codigo, string Nombre, bool Activo);
internal sealed record RubroGastoBody(int Codigo, string Nombre);

/// <summary>IRubroGastoService contra /finanzas/rubros.</summary>
public sealed class RubroGastoApiClient : IRubroGastoService
{
    private readonly HttpClient _http;

    public RubroGastoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(RubroGasto rubro)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/rubros", new RubroGastoBody(rubro.Codigo, rubro.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el rubro de gasto.");
        return creado.Id;
    }

    public async Task ModificarAsync(RubroGasto rubro)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/rubros/{rubro.Id}", new RubroGastoBody(rubro.Codigo, rubro.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/rubros/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => ListarAsync("finanzas/rubros");

    public Task<IReadOnlyList<RubroGasto>> ListarActivosAsync() => ListarAsync("finanzas/rubros/activos");

    private async Task<IReadOnlyList<RubroGasto>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<RubroGastoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static RubroGasto AEntidad(RubroGastoWire dto)
        => new() { Id = dto.Id, Codigo = dto.Codigo, Nombre = dto.Nombre, Activo = dto.Activo };
}
