using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IIngresoCajaRepository
{
    /// <summary>Incluye la FuenteFinanciamiento navegable.</summary>
    Task<IngresoCaja?> ObtenerPorIdAsync(int id);

    /// <summary>Incluye la fuente. Ordena por Fecha desc, luego Id desc.</summary>
    Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync();

    Task<int> AgregarAsync(IngresoCaja ingreso);
    Task ActualizarAsync(IngresoCaja ingreso);
}
