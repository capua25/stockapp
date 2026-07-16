using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ILineaPoaRepository
{
    /// <summary>Incluye las Asignaciones con su FuenteFinanciamiento navegable.</summary>
    Task<LineaPoa?> ObtenerPorIdAsync(int id);

    /// <summary>Incluye las Asignaciones. Ordena por Ejercicio desc, luego Nombre.</summary>
    Task<IReadOnlyList<LineaPoa>> ListarTodasAsync();

    Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null);

    /// <summary>Inserta la línea CON sus asignaciones (grafo completo).</summary>
    Task<int> AgregarAsync(LineaPoa linea);

    /// <summary>
    /// Actualiza los campos de la línea y REEMPLAZA sus asignaciones por
    /// <paramref name="nuevasAsignaciones"/> (delete + insert físico — son hijas del
    /// agregado, sin baja lógica propia). <paramref name="linea"/> debe ser la instancia
    /// tracked obtenida vía <see cref="ObtenerPorIdAsync"/>.
    /// </summary>
    Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones);
}
