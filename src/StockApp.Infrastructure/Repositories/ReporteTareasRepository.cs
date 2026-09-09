using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura del reporte-matriz de tareas (EF Core / PostgreSQL). Toda la
/// agregación (conteos por estado, fila "(sin asignar)") se hace en SQL con GROUP BY -- mismo
/// criterio que ReporteStockRepository.ObtenerStockPorCategoriaAsync: se agrupa directamente
/// por el nombre YA resuelto (con el fallback "(sin asignar)"), no por el Id, para que la
/// query completa traduzca a un único GROUP BY sin postprocesado.
/// </summary>
public class ReporteTareasRepository : IReporteTareasRepository
{
    private const string SinAsignar = "(sin asignar)";

    private readonly AppDbContext _ctx;

    public ReporteTareasRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FilaReporteTareas>> ObtenerAsync(FiltroReporteTareas filtro)
    {
        var query = AplicarFiltro(_ctx.Tareas, filtro);

        var filas = filtro.Agrupador switch
        {
            AgrupadorTareas.Zona => await AgruparPorZonaAsync(query),
            AgrupadorTareas.DimensionTematica => await AgruparPorDimensionAsync(query),
            AgrupadorTareas.OrganismoResponsable => await AgruparPorOrganismoAsync(query),
            AgrupadorTareas.OrigenFinanciamiento => await AgruparPorOrigenFinanciamientoAsync(query),
            AgrupadorTareas.Expediente => await AgruparPorExpedienteAsync(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Agrupador, "Agrupador de tareas no soportado."),
        };

        // D22: orden alfabético por Clasificador, con "(sin asignar)" siempre al final.
        // Se ordena EN MEMORIA sobre la lista ya agregada (una fila por clasificador, nunca
        // por tarea): no viola "la agregación se hace en SQL" -- solo el orden final de un
        // puñado de filas, mismo criterio que ObtenerMasMovidosAsync con el Take(topN).
        return filas
            .OrderBy(f => f.Clasificador == SinAsignar ? 1 : 0)
            .ThenBy(f => f.Clasificador, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Filtro por rango de fechas según el criterio (decisión 16). Con Cierre, se exige
    /// FechaFin no nulo -- eso excluye por construcción a Pendiente/EnCurso (FechaFin solo lo
    /// setean TerminarAsync/CancelarAsync), así que sus conteos dan cero sin lógica aparte.
    /// </summary>
    private static IQueryable<Tarea> AplicarFiltro(IQueryable<Tarea> query, FiltroReporteTareas filtro)
    {
        // filtro.Hasta llega como INSTANTE UTC ya convertido por el ViewModel (medianoche local
        // de Uruguay = 03:00Z) -- ver el contrato documentado en FiltroReporteTareas. Truncar con
        // .Date reancla el fin de día a medianoche UTC y pierde las últimas 3hs del día local; el
        // fin de rango se calcula sumando un día completo al instante recibido, sin tocar su hora.
        var hastaFinDia = filtro.Hasta.AddDays(1).AddTicks(-1);

        return filtro.Criterio switch
        {
            CriterioFechaTareas.Creacion => query.Where(t =>
                t.FechaCreacion >= filtro.Desde && t.FechaCreacion <= hastaFinDia),
            CriterioFechaTareas.Cierre => query.Where(t =>
                t.FechaFin != null && t.FechaFin >= filtro.Desde && t.FechaFin <= hastaFinDia),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Criterio, "Criterio de fecha no soportado."),
        };
    }

    private static Task<List<FilaReporteTareas>> AgruparPorZonaAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.Zona != null ? t.Zona.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorDimensionAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.DimensionTematica != null ? t.DimensionTematica.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorOrganismoAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.OrganismoResponsable != null ? t.OrganismoResponsable.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorOrigenFinanciamientoAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.OrigenFinanciamiento != null ? t.OrigenFinanciamiento.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    /// <summary>
    /// Expediente es un caso aparte: la agregación (conteos por estado) SÍ traduce a SQL
    /// agrupando por DocumentoAdministrativoId, pero el armado de la etiqueta "Numero/Anio"
    /// se resuelve DESPUÉS, en memoria, sobre la lista ya agregada (mismo criterio de
    /// "ADAPTACIÓN" que ReporteStockRepository.ObtenerMasMovidosAsync con Codigo/Nombre):
    /// evita depender de que el proveedor traduzca int.ToString() dentro del GROUP BY.
    /// </summary>
    private static async Task<List<FilaReporteTareas>> AgruparPorExpedienteAsync(IQueryable<Tarea> query)
    {
        var agregados = await query
            .GroupBy(t => new
            {
                t.DocumentoAdministrativoId,
                Numero = t.DocumentoAdministrativo != null ? t.DocumentoAdministrativo.Numero : null,
                Anio = t.DocumentoAdministrativo != null ? t.DocumentoAdministrativo.Anio : (int?)null,
            })
            .Select(g => new
            {
                g.Key.DocumentoAdministrativoId,
                g.Key.Numero,
                g.Key.Anio,
                Pendientes = g.Count(t => t.Estado == EstadoTarea.Pendiente),
                EnCurso = g.Count(t => t.Estado == EstadoTarea.EnCurso),
                Terminadas = g.Count(t => t.Estado == EstadoTarea.Terminada),
                Canceladas = g.Count(t => t.Estado == EstadoTarea.Cancelada),
                Total = g.Count(),
            })
            .ToListAsync();

        return agregados
            .Select(a => new FilaReporteTareas(
                a.DocumentoAdministrativoId is null ? SinAsignar : $"{a.Numero}/{a.Anio}",
                a.Pendientes, a.EnCurso, a.Terminadas, a.Canceladas, a.Total))
            .ToList();
    }
}
