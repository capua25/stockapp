using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface ILineaPoaService
{
    /// <summary>
    /// Alta de la línea CON sus asignaciones presupuestales (agregado completo).
    /// Reglas: al menos una asignación, montos &gt; 0, sin fuentes repetidas.
    /// </summary>
    Task<int> AltaAsync(LineaPoa linea);

    /// <summary>Modifica campos y REEMPLAZA las asignaciones por las de <paramref name="linea"/>.</summary>
    Task ModificarAsync(LineaPoa linea);

    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<LineaPoa>> ListarTodasAsync();

    /// <summary>Líneas activas para selección (combo de gastos). Exige solo VerFinanzas.</summary>
    Task<IReadOnlyList<LineaPoa>> ListarActivasAsync();
}
