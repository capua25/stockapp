using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record DimensionTematicaWire(int Id, string Nombre, bool Activo);
internal sealed record DimensionTematicaBody(string Nombre);

/// <summary>
/// IDimensionTematicaService contra /dimensiones-tematicas. La interfaz habla en entidades de
/// dominio (así la consumen los VMs) y el wire habla en DimensionTematicaDto (Task 5): este
/// client mapea.
/// </summary>
public sealed class DimensionTematicaApiClient : IDimensionTematicaService
{
    private readonly HttpClient _http;

    public DimensionTematicaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(DimensionTematica dimension)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("dimensiones-tematicas", new DimensionTematicaBody(dimension.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la dimensión.");
        return creado.Id;
    }

    public async Task ModificarAsync(DimensionTematica dimension)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"dimensiones-tematicas/{dimension.Id}", new DimensionTematicaBody(dimension.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"dimensiones-tematicas/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync() => ListarAsync("dimensiones-tematicas");

    public Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync() => ListarAsync("dimensiones-tematicas/activas");

    private async Task<IReadOnlyList<DimensionTematica>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<DimensionTematicaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static DimensionTematica AEntidad(DimensionTematicaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
