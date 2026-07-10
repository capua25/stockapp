using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record CategoriaWire(int Id, string Nombre, bool Activo);
internal sealed record CategoriaBody(string Nombre);

/// <summary>
/// ICategoriaService contra /categorias. La interfaz habla en entidades de dominio
/// (así la consumen los VMs) y el wire habla en CategoriaDto (3a, D3): este client mapea.
/// </summary>
public sealed class CategoriaApiClient : ICategoriaService
{
    private readonly HttpClient _http;

    public CategoriaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Categoria categoria)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("categorias", new CategoriaBody(categoria.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la categoría.");
        return creado.Id;
    }

    public async Task ModificarAsync(Categoria categoria)
    {
        // 3a, D1: el id de ruta es la única fuente; el body no lleva Id.
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"categorias/{categoria.Id}", new CategoriaBody(categoria.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"categorias/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<Categoria>> ListarTodasAsync() => ListarAsync("categorias");

    public Task<IReadOnlyList<Categoria>> ListarActivasAsync() => ListarAsync("categorias/activas");

    private async Task<IReadOnlyList<Categoria>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<CategoriaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static Categoria AEntidad(CategoriaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
