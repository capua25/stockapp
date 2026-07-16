using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IFuenteFinanciamientoRepository
{
    Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(FuenteFinanciamiento fuente);
    Task ActualizarAsync(FuenteFinanciamiento fuente);
}
