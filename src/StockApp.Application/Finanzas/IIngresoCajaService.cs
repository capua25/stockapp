using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de ingresos de caja (partidas mensuales, multas, préstamos, saldo inicial).
/// Mutaciones exigen RegistrarIngresos; el listado, VerFinanzas.
/// </summary>
public interface IIngresoCajaService
{
    Task<int> AltaAsync(IngresoCaja ingreso);
    Task ModificarAsync(IngresoCaja ingreso);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync();
}
