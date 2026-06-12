using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura para reportes de stock (EF Core / SQLite).
/// Devuelve los items YA proyectados: ValorCosto/ValorVenta calculados y la
/// categoría null resuelta a "Sin categoría". El service solo agrega/totaliza.
/// </summary>
public class ReporteStockRepository : IReporteStockRepository
{
    private const string SinCategoria = "Sin categoría";

    private readonly AppDbContext _ctx;

    public ReporteStockRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ValorizacionItemDto>> ObtenerValorizacionAsync()
    {
        // Nota SQLite: el OrderBy debe ir ANTES del Select sobre una columna real
        // (p.Nombre). Si se ordena por el DTO proyectado (OrderBy(x => x.Nombre) tras
        // el Select), EF reescribe el ORDER BY sobre el constructor completo del record
        // y NO traduce a SQL (InvalidOperationException de traducción). Ordenando por la
        // columna antes de proyectar, toda la query corre en SQL.
        return await _ctx.Productos
            .Where(p => p.Activo)
            .Include(p => p.Categoria)
            .OrderBy(p => p.Nombre)
            .Select(p => new ValorizacionItemDto(
                p.Id,
                p.Codigo,
                p.Nombre,
                p.Categoria != null ? p.Categoria.Nombre : SinCategoria,
                p.StockActual,
                p.PrecioCosto,
                p.PrecioVenta,
                p.StockActual * p.PrecioCosto,
                p.StockActual * p.PrecioVenta))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
    {
        return await _ctx.Productos
            .Where(p => p.Activo)
            .GroupBy(p => p.Categoria != null ? p.Categoria.Nombre : SinCategoria)
            .Select(g => new StockCategoriaDto(
                g.Key,
                g.Count(),
                g.Sum(p => p.StockActual),
                g.Sum(p => p.StockActual * p.PrecioCosto),
                g.Sum(p => p.StockActual * p.PrecioVenta)))
            .ToListAsync();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN)
    {
        // FechaHasta se ajusta a fin de día (23:59:59.9999999) para incluir todos
        // los movimientos del día indicado, sin importar la hora con que se pasó.
        var fechaHastaFinDia = fechaHasta?.Date.AddDays(1).AddTicks(-1);

        // ADAPTACIÓN SQLite (REGLA DE ORO): la query de referencia con
        // GroupBy(m => m.ProductoId).Select(g => new MasMovidoDto(g.Key,
        // g.First().Producto.Codigo, ...)).OrderByDescending(x => x.VolumenTotal)
        // NO traduce: EF intenta traducir el OrderByDescending sobre el DTO proyectado
        // junto con g.First().Producto navigation y lanza InvalidOperationException.
        //
        // Estrategia: la AGREGACIÓN (GroupBy + Count + Sum por ProductoId) SÍ traduce a
        // SQL, así que se ejecuta server-side. Luego se traen los agregados a memoria,
        // se resuelven Codigo/Nombre con un lookup de productos, y el OrderByDescending +
        // Take(topN) se aplica client-side sobre el set ya reducido (una fila por producto).
        var agregados = await _ctx.MovimientosStock
            .Where(m => (fechaDesde == null || m.Fecha >= fechaDesde)
                     && (fechaHastaFinDia == null || m.Fecha <= fechaHastaFinDia))
            .GroupBy(m => m.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Cantidad   = g.Count(),
                Volumen    = g.Sum(m => m.Cantidad)
            })
            .ToListAsync();

        if (agregados.Count == 0)
            return Array.Empty<MasMovidoDto>();

        // Lookup de Codigo/Nombre para los productos involucrados (una sola query).
        var productoIds = agregados.Select(a => a.ProductoId).ToList();
        var productos = await _ctx.Productos
            .Where(p => productoIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Codigo, p.Nombre })
            .ToListAsync();
        var porId = productos.ToDictionary(p => p.Id);

        return agregados
            .Select(a => new MasMovidoDto(
                a.ProductoId,
                porId.TryGetValue(a.ProductoId, out var p) ? p.Codigo : string.Empty,
                porId.TryGetValue(a.ProductoId, out var p2) ? p2.Nombre : string.Empty,
                a.Cantidad,
                a.Volumen))
            .OrderByDescending(x => x.VolumenTotal)
            .Take(topN)
            .ToList();
    }
}
