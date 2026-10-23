using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IFuenteFinanciamientoRepository
{
    Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    /// <summary>True si algún ingreso o pago (activo o dado de baja) usa la fuente.</summary>
    Task<bool> EstaEnUsoAsync(int fuenteFinanciamientoId);
    Task<int> AgregarAsync(FuenteFinanciamiento fuente);
    Task ActualizarAsync(FuenteFinanciamiento fuente);
}
