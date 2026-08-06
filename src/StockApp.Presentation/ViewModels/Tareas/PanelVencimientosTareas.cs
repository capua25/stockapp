using System;
using System.Collections.Generic;
using System.Linq;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Agrupación pura del panel "Tareas que requieren atención" de Inicio (spec 2026-08-06): qué
/// tareas entran (vencidas / próximas a vencer en <see cref="VentanaDiasProximasAVencer"/> días),
/// en qué orden, y qué ve cada rol. Vive en Presentation (no en Application) porque devuelve
/// TareaFila, un tipo de presentación -- el mismo criterio que TareaFila/TareaListViewModel, sin
/// duplicar el cálculo de DiasParaVencer.
/// </summary>
public static class PanelVencimientosTareas
{
    /// <summary>Ventana de "próxima a vencer" pedida por el encargo: hoy, o en 1/2/3 días.</summary>
    public const int VentanaDiasProximasAVencer = 3;

    /// <summary>
    /// Overload de uso real: usa el reloj y la zona horaria reales de la máquina (mismo criterio
    /// que el overload de 2 argumentos de TareaFila).
    /// </summary>
    public static (IReadOnlyList<TareaFila> Vencidas, IReadOnlyList<TareaFila> Proximas) Agrupar(
        IEnumerable<Tarea> tareas, RolUsuario rol, int usuarioActualId) =>
        Agrupar(tareas, rol, usuarioActualId, DateTime.UtcNow, TimeZoneInfo.Local);

    /// <summary>
    /// Overload testeable: recibe el instante UTC y la zona horaria explícitos, igual que
    /// TareaFila.CalcularDiasParaVencer, para que el borde de la ventana de días no dependa de
    /// la hora real ni de la máquina que corre la suite.
    /// </summary>
    public static (IReadOnlyList<TareaFila> Vencidas, IReadOnlyList<TareaFila> Proximas) Agrupar(
        IEnumerable<Tarea> tareas, RolUsuario rol, int usuarioActualId, DateTime ahoraUtc, TimeZoneInfo zonaLocal)
    {
        var filas = tareas
            .Where(t => t.FechaLimite is not null)
            .Where(t => t.Estado is EstadoTarea.Pendiente or EstadoTarea.EnCurso)
            .Where(t => EsVisibleParaRol(t, rol, usuarioActualId))
            .Select(t => new TareaFila(t, rol, ahoraUtc, zonaLocal))
            .ToList();

        var vencidas = filas
            .Where(f => f.DiasParaVencer < 0m)
            .OrderBy(f => f.DiasParaVencer)
            .ToList();

        var proximas = filas
            .Where(f => f.DiasParaVencer >= 0m && f.DiasParaVencer <= VentanaDiasProximasAVencer)
            .OrderBy(f => f.DiasParaVencer)
            .ToList();

        return (vencidas, proximas);
    }

    /// <summary>
    /// Decisión del spec: Admin ve todas las tareas. Operador ve las suyas (que tomó) más las
    /// que nadie tomó -- nunca las tomadas por otro operador.
    /// </summary>
    private static bool EsVisibleParaRol(Tarea tarea, RolUsuario rol, int usuarioActualId) =>
        rol == RolUsuario.Admin
        || tarea.TomadaPorUsuarioId is null
        || tarea.TomadaPorUsuarioId == usuarioActualId;
}
