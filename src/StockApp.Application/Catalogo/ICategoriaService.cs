using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface ICategoriaService
{
    Task<int> AltaAsync(Categoria categoria);
    Task ModificarAsync(Categoria categoria);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Categoria>> ListarTodasAsync();
}
