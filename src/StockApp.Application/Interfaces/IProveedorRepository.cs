using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IProveedorRepository
{
    Task<Proveedor?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Proveedor>> ListarTodosAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(Proveedor proveedor);
    Task ActualizarAsync(Proveedor proveedor);
}
