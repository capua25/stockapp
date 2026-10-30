using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IOrigenFinanciamientoService
{
    Task<int> AltaAsync(OrigenFinanciamiento origen);
    Task ModificarAsync(OrigenFinanciamiento origen);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarTodasAsync();

    /// <summary>
    /// Orígenes activos disponibles para selección. Gateada con GestionarProyectos —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<OrigenFinanciamiento>> ListarActivasAsync();
}
