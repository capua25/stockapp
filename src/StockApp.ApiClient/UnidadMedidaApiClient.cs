using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record UnidadMedidaWire(int Id, string Nombre, string Abreviatura, bool Activo);
internal sealed record UnidadMedidaBody(string Nombre, string Abreviatura);

/// <summary>IUnidadMedidaService contra /unidades-medida, incluido garantizar-por-defecto (3a, D6).</summary>
public sealed class UnidadMedidaApiClient : IUnidadMedidaService
{
    private readonly HttpClient _http;

    public UnidadMedidaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(UnidadMedida unidadMedida)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("unidades-medida", ABody(unidadMedida)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la unidad de medida.");
        return creado.Id;
    }

    public async Task ModificarAsync(UnidadMedida unidadMedida)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"unidades-medida/{unidadMedida.Id}", ABody(unidadMedida)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"unidades-medida/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => ListarAsync("unidades-medida");

    public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => ListarAsync("unidades-medida/activas");

    public async Task<UnidadMedida> GarantizarUnidadPorDefectoAsync()
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("unidades-medida/garantizar-por-defecto", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        var dto = await response.Content.ReadFromJsonAsync<UnidadMedidaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al garantizar la unidad por defecto.");
        return AEntidad(dto);
    }

    private async Task<IReadOnlyList<UnidadMedida>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<UnidadMedidaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static UnidadMedidaBody ABody(UnidadMedida u) => new(u.Nombre, u.Abreviatura);

    private static UnidadMedida AEntidad(UnidadMedidaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Abreviatura = dto.Abreviatura, Activo = dto.Activo };
}
