using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ProveedorWire(
    int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas, bool Activo);
internal sealed record ProveedorBody(
    string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);

/// <summary>IProveedorService contra /proveedores (sin variante /activas: asimetría real del dominio).</summary>
public sealed class ProveedorApiClient : IProveedorService
{
    private readonly HttpClient _http;

    public ProveedorApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Proveedor proveedor)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("proveedores", ABody(proveedor)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el proveedor.");
        return creado.Id;
    }

    public async Task ModificarAsync(Proveedor proveedor)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"proveedores/{proveedor.Id}", ABody(proveedor)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"proveedores/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<Proveedor>> ListarTodosAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("proveedores"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<ProveedorWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static ProveedorBody ABody(Proveedor p)
        => new(p.Nombre, p.Telefono, p.Email, p.Direccion, p.Notas);

    private static Proveedor AEntidad(ProveedorWire dto) => new()
    {
        Id        = dto.Id,
        Nombre    = dto.Nombre,
        Telefono  = dto.Telefono,
        Email     = dto.Email,
        Direccion = dto.Direccion,
        Notas     = dto.Notas,
        Activo    = dto.Activo,
    };
}
