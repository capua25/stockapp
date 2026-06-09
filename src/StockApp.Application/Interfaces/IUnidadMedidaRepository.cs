using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IUnidadMedidaRepository
{
    Task<UnidadMedida?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<bool> ExisteAbreviaturaAsync(string abreviatura, int? excluyendoId = null);
    Task<int> AgregarAsync(UnidadMedida unidadMedida);
    Task ActualizarAsync(UnidadMedida unidadMedida);
}
