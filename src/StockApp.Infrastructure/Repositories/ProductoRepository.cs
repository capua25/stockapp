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
    /// Nombre usa EF.Functions.ILike() en lugar de Contains() porque necesitamos
    /// case-insensitive: ILIKE es el operador nativo de Postgres/Npgsql para esto,
    /// a diferencia de LIKE que en Postgres es case-sensitive.
    /// Limitación conocida: acentos (á/Á) siguen siendo case-sensitive — mejora futura.
    ///
    /// Wildcards % y _ en el término de búsqueda no se escapan actualmente:
    /// un % literal en nombre actuaría como wildcard ILIKE. Para MVP esto es aceptable
    /// (% y _ son raros en nombres de productos).
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
    {
        var q = _ctx.Productos
            .Include(p => p.UnidadMedida)
            .Include(p => p.Categoria)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(sku))
            q = q.Where(p => p.Codigo.Contains(sku));

        if (!string.IsNullOrWhiteSpace(codigoBarras))
            q = q.Where(p => p.CodigoBarras != null && p.CodigoBarras.Contains(codigoBarras));

        if (!string.IsNullOrWhiteSpace(nombre))
            q = q.Where(p => EF.Functions.ILike(p.Nombre, $"%{nombre}%"));

        return await q.OrderBy(p => p.Nombre).ToListAsync();
    }

    /// <summary>
    /// Búsqueda por término único: matchea si el término aparece en Codigo (SKU), CodigoBarras
    /// o Nombre — lógica OR entre campos (design fix del buscador, que prometía buscar por
    /// "nombre, SKU o código de barras" pero solo filtraba por Nombre).
    /// Usa EF.Functions.ILike() en los tres campos para mantener la misma case-insensitividad
    /// que ya tenía el filtro de Nombre en <see cref="BuscarAsync"/> (ILIKE es el operador
    /// nativo case-insensitive de Postgres/Npgsql).
    /// Término vacío/null → sin filtro, devuelve todos ordenados por Nombre.
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarPorTextoAsync(string? texto)
    {
        var q = _ctx.Productos
            .Include(p => p.UnidadMedida)
            .Include(p => p.Categoria)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var patron = $"%{texto}%";
            q = q.Where(p =>
                EF.Functions.ILike(p.Codigo, patron)
                || (p.CodigoBarras != null && EF.Functions.ILike(p.CodigoBarras, patron))
                || EF.Functions.ILike(p.Nombre, patron));
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
