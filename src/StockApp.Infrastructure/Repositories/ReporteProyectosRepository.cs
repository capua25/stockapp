using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura del reporte-matriz de proyectos (EF Core / PostgreSQL). Toda la
/// agregación (conteos por estado, fila "(sin asignar)") se hace en SQL con GROUP BY -- mismo
/// criterio que ReporteStockRepository.ObtenerStockPorCategoriaAsync: se agrupa directamente
/// por el nombre YA resuelto (con el fallback "(sin asignar)"), no por el Id, para que la
/// query completa traduzca a un único GROUP BY sin postprocesado.
/// </summary>
public class ReporteProyectosRepository : IReporteProyectosRepository
{
    private const string SinAsignar = "(sin asignar)";

    private readonly AppDbContext _ctx;

    public ReporteProyectosRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FilaReporteProyectos>> ObtenerAsync(FiltroReporteProyectos filtro)
    {
        var query = AplicarFiltro(_ctx.Proyectos, filtro);

        var filas = filtro.Agrupador switch
        {
            AgrupadorProyectos.Zona => await AgruparPorZonaAsync(query),
            AgrupadorProyectos.DimensionTematica => await AgruparPorDimensionAsync(query),
            AgrupadorProyectos.OrganismoResponsable => await AgruparPorOrganismoAsync(query),
            AgrupadorProyectos.OrigenFinanciamiento => await AgruparPorOrigenFinanciamientoAsync(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Agrupador, "Agrupador de proyectos no soportado."),
        };

        // D22: orden alfabético por Clasificador, con "(sin asignar)" siempre al final.
        // Se ordena EN MEMORIA sobre la lista ya agregada (una fila por clasificador, nunca
        // por proyecto): no viola "la agregación se hace en SQL" -- solo el orden final de un
        // puñado de filas, mismo criterio que ObtenerMasMovidosAsync con el Take(topN).
        return filas
            .OrderBy(f => f.Clasificador == SinAsignar ? 1 : 0)
            .ThenBy(f => f.Clasificador, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Filtro por rango de fechas según el criterio (decisión 16). Con Cierre, se exige
    /// FechaFin no nulo -- eso excluye por construcción a Pendiente/EnCurso (FechaFin solo se
    /// sella al pasar a Terminado/Cancelado), así que sus conteos dan cero sin lógica aparte.
    /// </summary>
    private static IQueryable<Proyecto> AplicarFiltro(IQueryable<Proyecto> query, FiltroReporteProyectos filtro)
    {
        // filtro.Hasta llega como INSTANTE UTC ya convertido por el ViewModel (medianoche local
        // de Uruguay = 03:00Z) -- ver el contrato documentado en FiltroReporteProyectos. Truncar con
        // .Date reancla el fin de día a medianoche UTC y pierde las últimas 3hs del día local; el
        // fin de rango se calcula sumando un día completo al instante recibido, sin tocar su hora.
        var hastaFinDia = filtro.Hasta.AddDays(1).AddTicks(-1);

        return filtro.Criterio switch
        {
            CriterioFechaProyectos.Creacion => query.Where(p =>
                p.FechaCreacion >= filtro.Desde && p.FechaCreacion <= hastaFinDia),
            CriterioFechaProyectos.Cierre => query.Where(p =>
                p.FechaFin != null && p.FechaFin >= filtro.Desde && p.FechaFin <= hastaFinDia),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Criterio, "Criterio de fecha no soportado."),
        };
    }

    private static Task<List<FilaReporteProyectos>> AgruparPorZonaAsync(IQueryable<Proyecto> query) =>
        query
            .GroupBy(p => p.Zona != null ? p.Zona.Nombre : SinAsignar)
            .Select(g => new FilaReporteProyectos(
                g.Key,
                g.Count(p => p.Estado == EstadoProyecto.Pendiente),
                g.Count(p => p.Estado == EstadoProyecto.EnCurso),
                g.Count(p => p.Estado == EstadoProyecto.Terminado),
                g.Count(p => p.Estado == EstadoProyecto.Cancelado),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteProyectos>> AgruparPorDimensionAsync(IQueryable<Proyecto> query) =>
        query
            .GroupBy(p => p.DimensionTematica != null ? p.DimensionTematica.Nombre : SinAsignar)
            .Select(g => new FilaReporteProyectos(
                g.Key,
                g.Count(p => p.Estado == EstadoProyecto.Pendiente),
                g.Count(p => p.Estado == EstadoProyecto.EnCurso),
                g.Count(p => p.Estado == EstadoProyecto.Terminado),
                g.Count(p => p.Estado == EstadoProyecto.Cancelado),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteProyectos>> AgruparPorOrganismoAsync(IQueryable<Proyecto> query) =>
        query
            .GroupBy(p => p.OrganismoResponsable != null ? p.OrganismoResponsable.Nombre : SinAsignar)
            .Select(g => new FilaReporteProyectos(
                g.Key,
                g.Count(p => p.Estado == EstadoProyecto.Pendiente),
                g.Count(p => p.Estado == EstadoProyecto.EnCurso),
                g.Count(p => p.Estado == EstadoProyecto.Terminado),
                g.Count(p => p.Estado == EstadoProyecto.Cancelado),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteProyectos>> AgruparPorOrigenFinanciamientoAsync(IQueryable<Proyecto> query) =>
        query
            .GroupBy(p => p.OrigenFinanciamiento != null ? p.OrigenFinanciamiento.Nombre : SinAsignar)
            .Select(g => new FilaReporteProyectos(
                g.Key,
                g.Count(p => p.Estado == EstadoProyecto.Pendiente),
                g.Count(p => p.Estado == EstadoProyecto.EnCurso),
                g.Count(p => p.Estado == EstadoProyecto.Terminado),
                g.Count(p => p.Estado == EstadoProyecto.Cancelado),
                g.Count()))
            .ToListAsync();
}
