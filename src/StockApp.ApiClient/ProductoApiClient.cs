// src/StockApp.ApiClient/ProductoApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ProductoBody(
    string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);
internal sealed record CambiarPrecioBody(decimal PrecioCosto, decimal PrecioVenta);

/// <summary>
/// IProductoService contra /productos. Las búsquedas devuelven ProductoDto (el mismo DTO
/// de Application viaja por el wire — sin mapeo); las escrituras arman el request desde
/// la entidad, sin Id ni StockActual en el body (3a, D1/D5).
/// </summary>
public sealed class ProductoApiClient : IProductoService
{
    private readonly HttpClient _http;

    public ProductoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Producto producto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("productos", ABody(producto)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el producto.");
        return creado.Id;
    }

    public async Task ModificarAsync(Producto producto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"productos/{producto.Id}", ABody(producto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"productos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"productos/{id}/precio", new CambiarPrecioBody(precioCosto, precioVenta)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
        => BuscarConQueryAsync(ApiQuery.Construir(("sku", sku), ("codigoBarras", codigoBarras), ("nombre", nombre)));

    public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
        => BuscarConQueryAsync(ApiQuery.Construir(("texto", texto)));

    private async Task<IReadOnlyList<ProductoDto>> BuscarConQueryAsync(string query)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("productos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ProductoDto>>() ?? new();
    }

    private static ProductoBody ABody(Producto p) => new(
        p.Codigo, p.CodigoBarras, p.Nombre, p.Descripcion,
        p.CategoriaId, p.ProveedorId, p.UnidadMedidaId,
        p.PrecioCosto, p.PrecioVenta, p.StockMinimo);
}
