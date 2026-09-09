using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IDimensionTematicaRepository
{
    Task<DimensionTematica?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(DimensionTematica dimension);
    Task ActualizarAsync(DimensionTematica dimension);
}
