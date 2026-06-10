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

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Categorias.AnyAsync(c => c.Nombre == nombre && c.Id != excluyendoId.Value)
            : _ctx.Categorias.AnyAsync(c => c.Nombre == nombre);

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
