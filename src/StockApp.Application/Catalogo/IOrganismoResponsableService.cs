using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IOrganismoResponsableService
{
    Task<int> AltaAsync(OrganismoResponsable organismo);
    Task ModificarAsync(OrganismoResponsable organismo);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync();

    /// <summary>
    /// Organismos activos disponibles para selección. Gateada con GestionarTareas —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<OrganismoResponsable>> ListarActivasAsync();
}
