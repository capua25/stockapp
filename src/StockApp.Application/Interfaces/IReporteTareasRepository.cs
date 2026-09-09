using StockApp.Application.Reportes;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Contrato de persistencia del reporte-matriz de tareas (solo lectura). La agregación
/// completa (conteos por estado, fila "(sin asignar)", orden alfabético con esa fila siempre
/// al final) la hace el repo en SQL con GROUP BY -- el service solo valida y suma el total.
/// </summary>
public interface IReporteTareasRepository
{
    /// <summary>Filas ya agregadas y ordenadas (decisión 22). Lista vacía si no hay tareas
    /// dentro del rango/criterio filtrado.</summary>
    Task<IReadOnlyList<FilaReporteTareas>> ObtenerAsync(FiltroReporteTareas filtro);
}
