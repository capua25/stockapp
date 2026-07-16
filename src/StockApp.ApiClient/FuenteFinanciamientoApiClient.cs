using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record FuenteFinanciamientoWire(int Id, string Nombre, bool Activo);
internal sealed record FuenteFinanciamientoBody(string Nombre);

/// <summary>
/// IFuenteFinanciamientoService contra /finanzas/fuentes. La interfaz habla en entidades
/// de dominio (así la consumen los VMs) y el wire habla en FuenteFinanciamientoDto: mapea.
/// </summary>
public sealed class FuenteFinanciamientoApiClient : IFuenteFinanciamientoService
{
    private readonly HttpClient _http;

    public FuenteFinanciamientoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(FuenteFinanciamiento fuente)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/fuentes", new FuenteFinanciamientoBody(fuente.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la fuente de financiamiento.");
        return creado.Id;
    }

    public async Task ModificarAsync(FuenteFinanciamiento fuente)
    {
        // El id de ruta es la única fuente; el body no lleva Id (3a, D1).
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/fuentes/{fuente.Id}", new FuenteFinanciamientoBody(fuente.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/fuentes/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => ListarAsync("finanzas/fuentes");

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() => ListarAsync("finanzas/fuentes/activas");

    private async Task<IReadOnlyList<FuenteFinanciamiento>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static FuenteFinanciamiento AEntidad(FuenteFinanciamientoWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
