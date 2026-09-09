using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IOrigenFinanciamientoRepository
{
    Task<OrigenFinanciamiento?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(OrigenFinanciamiento origen);
    Task ActualizarAsync(OrigenFinanciamiento origen);
}
