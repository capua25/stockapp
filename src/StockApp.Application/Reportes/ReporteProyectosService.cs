using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio del reporte-matriz de proyectos. Patrón: auth fail-closed → validación de rango de
/// fechas → repo (agregación completa en SQL) → total general. SIN CACHE (decisión 14) --
/// no copiar el patrón de <c>ReporteStockServiceCacheado</c> acá.
/// </summary>
public class ReporteProyectosService : IReporteProyectosService
{
    private readonly IReporteProyectosRepository _repo;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ReporteProyectosService(
        IReporteProyectosRepository repo, ICurrentSession session, IAuthorizationService auth)
    {
        _repo = repo;
        _session = session;
        _auth = auth;
    }

    public async Task<ReporteProyectosDto> ObtenerAsync(FiltroReporteProyectos filtro)
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session, Permisos.VerReportes);

        // D18: el rango de fechas es obligatorio y se valida ACÁ, no solo en la UI -- mismo
        // criterio que DocumentoAdministrativoService.ListarHistorialAsync con el año.
        if (filtro.Desde == default || filtro.Hasta == default)
            throw new ArgumentException(
                "El rango de fechas es obligatorio para el reporte de proyectos.", nameof(filtro));
        if (filtro.Desde > filtro.Hasta)
            throw new ArgumentException(
                "La fecha 'Desde' no puede ser posterior a 'Hasta'.", nameof(filtro));

        var filas = await _repo.ObtenerAsync(filtro);

        // D23: el ViewModel no calcula nada -- este total se calcula ACÁ, sumando una lista
        // ya agregada por el repo (mismo criterio que ReporteStockService.ObtenerValorizacionAsync
        // con TotalValorCosto). No es un cálculo por-entidad: Filas ya viene agregada por SQL.
        var totalGeneral = new FilaReporteProyectos(
            "Total general",
            filas.Sum(f => f.Pendientes),
            filas.Sum(f => f.EnCurso),
            filas.Sum(f => f.Terminados),
            filas.Sum(f => f.Cancelados),
            filas.Sum(f => f.Total));

        return new ReporteProyectosDto(filas, totalGeneral);
    }
}
