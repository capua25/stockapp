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
    public Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
        => throw new NotImplementedException();

    /// <inheritdoc/>
    public Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN)
        => throw new NotImplementedException();
}
