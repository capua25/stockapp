using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IProductoService
{
    Task<int> AltaAsync(Producto producto);
    Task ModificarAsync(Producto producto);
    Task BajaLogicaAsync(int id);
    Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta);
    Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
    Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto);
}
