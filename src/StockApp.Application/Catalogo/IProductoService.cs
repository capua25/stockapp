using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IProductoService
{
    Task<int> AltaAsync(Producto producto);
    Task ModificarAsync(Producto producto);
    Task BajaLogicaAsync(int id);
    Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta);
    Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
    Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto);
}
