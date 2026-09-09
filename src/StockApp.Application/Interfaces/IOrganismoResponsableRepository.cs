using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IOrganismoResponsableRepository
{
    Task<OrganismoResponsable?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(OrganismoResponsable organismo);
    Task ActualizarAsync(OrganismoResponsable organismo);
}
