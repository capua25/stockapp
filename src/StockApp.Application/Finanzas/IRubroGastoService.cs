using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface IRubroGastoService
{
    Task<int> AltaAsync(RubroGasto rubro);
    Task ModificarAsync(RubroGasto rubro);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<RubroGasto>> ListarTodosAsync();

    /// <summary>
    /// Rubros activos para selección (combo de gastos). Exige solo VerFinanzas.
    /// </summary>
    Task<IReadOnlyList<RubroGasto>> ListarActivosAsync();
}
