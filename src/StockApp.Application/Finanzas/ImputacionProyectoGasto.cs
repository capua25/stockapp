using StockApp.Application.Interfaces;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Regla de imputación de un gasto a un proyecto, compartida por GastoService y
/// IngresoPorFacturaService (las dos vías que crean o modifican gastos).
/// </summary>
internal static class ImputacionProyectoGasto
{
    /// <summary>
    /// Valida cuando el proyecto nuevo es no nulo y distinto del actual: debe existir y estar
    /// pendiente o en curso. En un alta no hay valor actual (null). Un gasto ya imputado a un
    /// proyecto que después se cerró sigue siendo válido si el valor no cambia.
    /// </summary>
    public static async Task ValidarAsync(
        IProyectoRepository proyectos, int? proyectoIdNuevo, int? proyectoIdActual = null)
    {
        if (proyectoIdNuevo is not int pid || pid == proyectoIdActual)
            return;

        var proyecto = await proyectos.ObtenerPorIdAsync(pid)
            ?? throw new EntidadNoEncontradaException($"Proyecto {pid} no encontrado.");
        if (proyecto.EsCerrado)
            throw new ReglaDeNegocioException("Solo se pueden imputar proyectos pendientes o en curso.");
    }
}
