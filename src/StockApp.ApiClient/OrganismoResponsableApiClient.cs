using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record OrganismoResponsableWire(int Id, string Nombre, bool Activo);
internal sealed record OrganismoResponsableBody(string Nombre);

/// <summary>
/// IOrganismoResponsableService contra /organismos-responsables. La interfaz habla en
/// entidades de dominio (así la consumen los VMs) y el wire habla en
/// OrganismoResponsableDto (Task 5): este client mapea.
/// </summary>
public sealed class OrganismoResponsableApiClient : IOrganismoResponsableService
{
    private readonly HttpClient _http;

    public OrganismoResponsableApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(OrganismoResponsable organismo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("organismos-responsables", new OrganismoResponsableBody(organismo.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el organismo.");
        return creado.Id;
    }

    public async Task ModificarAsync(OrganismoResponsable organismo)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"organismos-responsables/{organismo.Id}", new OrganismoResponsableBody(organismo.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"organismos-responsables/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync() => ListarAsync("organismos-responsables");

    public Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync() => ListarAsync("organismos-responsables/activas");

    private async Task<IReadOnlyList<OrganismoResponsable>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<OrganismoResponsableWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static OrganismoResponsable AEntidad(OrganismoResponsableWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
