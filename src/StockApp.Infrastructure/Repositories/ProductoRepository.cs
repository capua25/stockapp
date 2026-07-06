using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ProductoRepository : IProductoRepository
{
    private readonly AppDbContext _ctx;

    public ProductoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Producto?> ObtenerPorIdAsync(int id)
        => _ctx.Productos.FindAsync(id).AsTask();

    /// <summary>
    /// Búsqueda con filtros LINQ condicionales (design D-SEARCH).
    /// Cada parámetro no nulo/vacío agrega un Where encadenado.
    /// Sin filtros → devuelve todos, ordenados por Nombre.
    ///
    /// Nombre usa EF.Functions.Like() en lugar de Contains() porque el operador
    /// LIKE de SQLite es case-insensitive para ASCII por defecto, lo que permite
    /// buscar "aceite" y encontrar "Aceite de Oliva".
    /// Limitación conocida: acentos (á/Á) siguen siendo case-sensitive — mejora futura.
    ///
    /// Wildcards % y _ en el término de búsqueda no se escapan actualmente:
    /// un % literal en nombre actuaría como wildcard LIKE. Para MVP esto es aceptable
    /// (% y _ son raros en nombres de productos).
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
    {
        var q = _ctx.Productos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(sku))
            q = q.Where(p => p.Codigo.Contains(sku));

        if (!string.IsNullOrWhiteSpace(codigoBarras))
            q = q.Where(p => p.CodigoBarras != null && p.CodigoBarras.Contains(codigoBarras));

        if (!string.IsNullOrWhiteSpace(nombre))
            q = q.Where(p => EF.Functions.Like(p.Nombre, $"%{nombre}%"));

        return await q.OrderBy(p => p.Nombre).ToListAsync();
    }

    /// <summary>
    /// Búsqueda por término único: matchea si el término aparece en Codigo (SKU), CodigoBarras
    /// o Nombre — lógica OR entre campos (design fix del buscador, que prometía buscar por
    /// "nombre, SKU o código de barras" pero solo filtraba por Nombre).
    /// Usa EF.Functions.Like() en los tres campos para mantener la misma case-insensitividad
    /// ASCII que ya tenía el filtro de Nombre en <see cref="BuscarAsync"/>.
    /// Término vacío/null → sin filtro, devuelve todos ordenados por Nombre.
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto)
    {
        var q = _ctx.Productos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var patron = $"%{texto}%";
            q = q.Where(p =>
                EF.Functions.Like(p.Codigo, patron)
                || (p.CodigoBarras != null && EF.Functions.Like(p.CodigoBarras, patron))
                || EF.Functions.Like(p.Nombre, patron));
        }

        return await q.OrderBy(p => p.Nombre).ToListAsync();
    }

    public Task<bool> ExisteCodigoAsync(string codigo, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Productos.AnyAsync(p => p.Codigo == codigo && p.Id != excluyendoId.Value)
            : _ctx.Productos.AnyAsync(p => p.Codigo == codigo);

    public Task<bool> ExisteCodigoBarrasAsync(string codigoBarras, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Productos.AnyAsync(p => p.CodigoBarras == codigoBarras && p.Id != excluyendoId.Value)
            : _ctx.Productos.AnyAsync(p => p.CodigoBarras == codigoBarras);

    public async Task<int> AgregarAsync(Producto producto)
    {
        _ctx.Productos.Add(producto);
        await _ctx.SaveChangesAsync();
        return producto.Id;
    }

    public Task ActualizarAsync(Producto producto)
    {
        _ctx.Productos.Update(producto);
        return _ctx.SaveChangesAsync();
    }
}
