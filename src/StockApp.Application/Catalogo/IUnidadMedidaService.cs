using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IUnidadMedidaService
{
    Task<int> AltaAsync(UnidadMedida unidadMedida);
    Task ModificarAsync(UnidadMedida unidadMedida);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync();
}
