using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IZonaService
{
    Task<int> AltaAsync(Zona zona);
    Task ModificarAsync(Zona zona);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Zona>> ListarTodasAsync();

    /// <summary>
    /// Zonas activas disponibles para selección. Gateada con GestionarTareas — el permiso de
    /// QUIEN CONSUME el catálogo (el futuro formulario de alta de tarea), no GestionarTablasMaestras
    /// (quien lo administra). Mismo patrón que ICategoriaService.ListarActivasAsync con
    /// GestionarProductos (ver Global Constraints del plan 2026-09-08-tareas-catalogos.md).
    /// </summary>
    Task<IReadOnlyList<Zona>> ListarActivasAsync();
}
