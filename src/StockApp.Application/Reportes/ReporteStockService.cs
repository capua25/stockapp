using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio de reportes de stock.
/// Patrón: auth fail-closed → repo (items ya proyectados) → agregación de totales.
/// </summary>
public class ReporteStockService : IReporteStockService
{
    private readonly IReporteStockRepository _repo;
    private readonly ICurrentSession         _session;
    private readonly IAuthorizationService   _auth;

    public ReporteStockService(
        IReporteStockRepository repo,
        ICurrentSession         session,
        IAuthorizationService   auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }

    public async Task<(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales)>
        ObtenerValorizacionAsync()
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session.RolActual, Permisos.VerReportes);

        var items = await _repo.ObtenerValorizacionAsync();

        var totales = new ValorizacionTotalesDto(
            TotalValorCosto: items.Sum(i => i.ValorCosto),
            TotalValorVenta: items.Sum(i => i.ValorVenta));

        return (items, totales);
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
}
