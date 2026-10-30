namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio del reporte-matriz de proyectos (spec 2026-09-08). Patrón: auth fail-closed →
/// validación de rango de fechas → repo (agregación completa en SQL) → total general.
/// SIN CACHE (decisión 14): a diferencia de <c>IReporteStockService</c>, este servicio
/// NO se envuelve en un decorator cacheado ni participa de <c>IVersionReportes</c> --
/// los proyectos cambian de estado todo el día y un reporte cacheado una hora informa mal.
/// </summary>
public interface IReporteProyectosService
{
    /// <param name="filtro">Ver contrato de <see cref="FiltroReporteProyectos.Desde"/>/<see cref="FiltroReporteProyectos.Hasta"/>:
    /// deben llegar ya convertidas a UTC (no reetiquetadas) -- el instante local se convierte con
    /// <c>SpecifyKind(Local).ToUniversalTime()</c>, no con <c>SpecifyKind(Utc)</c>.</param>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso para ver reportes.</exception>
    /// <exception cref="ArgumentException">Si el rango de fechas está ausente o invertido (Desde &gt; Hasta).</exception>
    Task<ReporteProyectosDto> ObtenerAsync(FiltroReporteProyectos filtro);
}
