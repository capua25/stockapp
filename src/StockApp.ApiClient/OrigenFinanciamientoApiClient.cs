using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record OrigenFinanciamientoWire(int Id, string Nombre, bool Activo);
internal sealed record OrigenFinanciamientoBody(string Nombre);

/// <summary>
/// IOrigenFinanciamientoService contra /origenes-financiamiento. La interfaz habla en
/// entidades de dominio (así la consumen los VMs) y el wire habla en
/// OrigenFinanciamientoDto (Task 5): este client mapea.
/// </summary>
public sealed class OrigenFinanciamientoApiClient : IOrigenFinanciamientoService
{
    private readonly HttpClient _http;

    public OrigenFinanciamientoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(OrigenFinanciamiento origen)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("origenes-financiamiento", new OrigenFinanciamientoBody(origen.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el origen de financiamiento.");
        return creado.Id;
    }

    public async Task ModificarAsync(OrigenFinanciamiento origen)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"origenes-financiamiento/{origen.Id}", new OrigenFinanciamientoBody(origen.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"origenes-financiamiento/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync() => ListarAsync("origenes-financiamiento");

    public Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync() => ListarAsync("origenes-financiamiento/activas");

    private async Task<IReadOnlyList<OrigenFinanciamiento>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<OrigenFinanciamientoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static OrigenFinanciamiento AEntidad(OrigenFinanciamientoWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
