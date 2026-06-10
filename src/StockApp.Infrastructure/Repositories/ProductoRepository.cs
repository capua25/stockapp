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
    /// Contains() se traduce a LIKE '%x%' en SQLite, que es case-insensitive
    /// para ASCII por defecto — comportamiento documentado y testeado.
    /// </summary>
    public async Task<IReadOnlyList<Producto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
    {
        var q = _ctx.Productos.AsQueryable();

        if (!string.IsNullOrWhiteSpace(sku))
            q = q.Where(p => p.Codigo.Contains(sku));

        if (!string.IsNullOrWhiteSpace(codigoBarras))
            q = q.Where(p => p.CodigoBarras != null && p.CodigoBarras.Contains(codigoBarras));

        if (!string.IsNullOrWhiteSpace(nombre))
            q = q.Where(p => p.Nombre.Contains(nombre));

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
