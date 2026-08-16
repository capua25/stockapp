using StockApp.Domain.Entities;

namespace StockApp.Application.Catalogo;

public interface IProveedorService
{
    Task<int> AltaAsync(Proveedor proveedor);
    Task ModificarAsync(Proveedor proveedor);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<Proveedor>> ListarTodosAsync();

    /// <summary>
    /// Lectura filtrada a Activo=true, con Permisos.VerFinanzas en vez de GestionarTablasMaestras
    /// (bugfix 2026-08-15): un gasto TIENE un proveedor, así que Finanzas necesita leerlos aunque
    /// no pueda gestionar el maestro. Cierra la asimetría real documentada en el design de Fase 2b
    /// (§Alternativas descartadas) — en ese momento ningún caller la necesitaba.
    /// </summary>
    Task<IReadOnlyList<Proveedor>> ListarActivasAsync();
}
