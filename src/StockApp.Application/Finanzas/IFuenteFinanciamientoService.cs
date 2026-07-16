using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface IFuenteFinanciamientoService
{
    Task<int> AltaAsync(FuenteFinanciamiento fuente);
    Task ModificarAsync(FuenteFinanciamiento fuente);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync();

    /// <summary>
    /// Fuentes activas para selección (combos de gastos/asignaciones). A diferencia de
    /// <see cref="ListarTodasAsync"/>, exige solo VerFinanzas, no GestionarMaestrosFinanzas.
    /// </summary>
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync();
}
