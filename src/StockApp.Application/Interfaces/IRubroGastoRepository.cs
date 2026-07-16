using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IRubroGastoRepository
{
    Task<RubroGasto?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<RubroGasto>> ListarTodosAsync();
    Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null);
    Task<int> AgregarAsync(RubroGasto rubro);
    Task ActualizarAsync(RubroGasto rubro);
}
