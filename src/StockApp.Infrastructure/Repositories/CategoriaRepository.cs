using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class CategoriaRepository : ICategoriaRepository
{
    private readonly AppDbContext _ctx;

    public CategoriaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Categoria?> ObtenerPorIdAsync(int id)
        => _ctx.Categorias.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Categoria>> ListarTodasAsync()
        => await _ctx.Categorias.OrderBy(c => c.Nombre).ToListAsync();

    // Comparación case-insensitive vía LOWER() (no ILIKE: ILIKE trata % y _ como comodines,
    // lo cual da falsos positivos en nombres de catálogo que los contengan, y además no puede
    // usar el índice funcional sobre LOWER("Nombre") — ver migración AgregaIndiceFuncionalNombreCatalogos).
    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
    {
        var normalizado = nombre.Trim().ToLower();
        return excluyendoId.HasValue
            ? _ctx.Categorias.AnyAsync(c => c.Nombre.ToLower() == normalizado && c.Id != excluyendoId.Value)
            : _ctx.Categorias.AnyAsync(c => c.Nombre.ToLower() == normalizado);
    }

    public async Task<int> AgregarAsync(Categoria categoria)
    {
        _ctx.Categorias.Add(categoria);
        await _ctx.SaveChangesAsync();
        return categoria.Id;
    }

    public Task ActualizarAsync(Categoria categoria)
    {
        _ctx.Categorias.Update(categoria);
        return _ctx.SaveChangesAsync();
    }
}
