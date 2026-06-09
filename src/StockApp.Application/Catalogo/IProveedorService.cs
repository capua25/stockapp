using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IProveedorService
{
    Task<int> AltaAsync(Proveedor proveedor);
    Task ModificarAsync(Proveedor proveedor);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Proveedor>> ListarTodosAsync();
}
