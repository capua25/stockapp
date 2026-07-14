using Microsoft.Extensions.Caching.Memory;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;

namespace StockApp.Infrastructure.Reportes;

/// <summary>
/// Decorator de <see cref="IReporteStockService"/> que cachea los 4 reportes de stock
/// en <see cref="IMemoryCache"/> con claves versionadas por <see cref="IVersionReportes"/>.
/// Al incrementarse la versión, las claves viejas quedan huérfanas y expiran por TTL o
/// por presión de tamaño. TTL de respaldo: 1 hora (la invalidación por versión es la
/// defensa primaria e inmediata).
/// </summary>
public sealed class ReporteStockServiceCacheado : IReporteStockService
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(1);

    private readonly IReporteStockService _inner;
    private readonly IMemoryCache _cache;
    private readonly IVersionReportes _version;

    public ReporteStockServiceCacheado(
        IReporteStockService inner, IMemoryCache cache, IVersionReportes version)
    {
        _inner = inner;
        _cache = cache;
        _version = version;
    }

    public Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
        => GetOrCreate(
            $"valorizacion@v{_version.Actual}",
            () => _inner.ObtenerValorizacionAsync());

    public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
        => GetOrCreate(
            $"stock-categoria@v{_version.Actual}",
            () => _inner.ObtenerStockPorCategoriaAsync());

    public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
        => GetOrCreate(
            $"mas-movidos:{fechaDesde:o}:{fechaHasta:o}:{topN}@v{_version.Actual}",
            () => _inner.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN));

    public Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
        int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
        => GetOrCreate(
            $"historial:{productoId}:{fechaDesde:o}:{fechaHasta:o}@v{_version.Actual}",
            () => _inner.ObtenerHistorialPorProductoAsync(productoId, fechaDesde, fechaHasta));

    private Task<T> GetOrCreate<T>(string clave, Func<Task<T>> calcular)
        => _cache.GetOrCreateAsync(clave, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Ttl;
            return calcular();
        })!;
}
