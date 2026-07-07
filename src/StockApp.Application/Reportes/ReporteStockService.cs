using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;

namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio de reportes de stock.
/// Patrón: auth fail-closed → repo (items ya proyectados) → agregación de totales.
/// El historial por producto se delega al servicio de movimientos del Inc 5 (D2).
/// </summary>
public class ReporteStockService : IReporteStockService
{
    private readonly IReporteStockRepository  _repo;
    private readonly IMovimientoStockService  _movimientos;
    private readonly ICurrentSession          _session;
    private readonly IAuthorizationService    _auth;

    public ReporteStockService(
        IReporteStockRepository repo,
        IMovimientoStockService movimientos,
        ICurrentSession         session,
        IAuthorizationService   auth)
    {
        _repo        = repo;
        _movimientos = movimientos;
        _session     = session;
        _auth        = auth;
    }

    public async Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        var items = await _repo.ObtenerValorizacionAsync();

        var totales = new ValorizacionTotalesDto(
            TotalValorCosto: items.Sum(i => i.ValorCosto),
            TotalValorVenta: items.Sum(i => i.ValorVenta));

        return new ValorizacionReporteDto(items, totales);
    }

    public async Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        return await _repo.ObtenerStockPorCategoriaAsync();
    }

    public async Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        // Parámetros se pasan TAL CUAL: el ajuste de FechaHasta, el orden y el
        // Take(topN) son responsabilidad del repo (C3).
        return await _repo.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN);
    }

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
        int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        // Autorización fail-closed: PRIMERO, antes de delegar.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        // DOBLE-GUARD: este método verifica VerReportes; el servicio delegado
        // (IMovimientoStockService.ObtenerHistorialAsync) verifica RegistrarMovimientos.
        // Hoy es seguro porque VerReportes es Admin-only y Admin tiene todos los permisos.
        // Si la matriz de roles evoluciona (ej. dar VerReportes a un rol sin RegistrarMovimientos),
        // este reporte fallará. Tener presente si se cambian permisos.

        // D2: no reimplementamos el historial; delegamos al servicio de movimientos
        // del Inc 5, que ya resuelve el running balance y el ajuste de fechas.
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: productoId,
            FechaDesde: fechaDesde,
            FechaHasta: fechaHasta);

        return await _movimientos.ObtenerHistorialAsync(filtro);
    }
}
