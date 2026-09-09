using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IDimensionTematicaService
{
    Task<int> AltaAsync(DimensionTematica dimension);
    Task ModificarAsync(DimensionTematica dimension);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<DimensionTematica>> ListarTodasAsync();

    /// <summary>
    /// Dimensiones activas disponibles para selección. Gateada con GestionarTareas —
    /// ver nota en IZonaService.
    /// </summary>
    Task<IReadOnlyList<DimensionTematica>> ListarActivasAsync();
}
