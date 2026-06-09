using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IProductoRepository
{
    Task<Producto?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre);
    Task<bool> ExisteCodigoAsync(string codigo, int? excluyendoId = null);
    Task<bool> ExisteCodigoBarrasAsync(string codigoBarras, int? excluyendoId = null);
    Task<int> AgregarAsync(Producto producto);
    Task ActualizarAsync(Producto producto);
}
