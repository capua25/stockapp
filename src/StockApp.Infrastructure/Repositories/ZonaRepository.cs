using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ZonaRepository : IZonaRepository
{
    private readonly AppDbContext _ctx;

    public ZonaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Zona?> ObtenerPorIdAsync(int id)
        => _ctx.Zonas.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Zona>> ListarTodasAsync()
        => await _ctx.Zonas.OrderBy(z => z.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.Zonas.AnyAsync(z => z.Nombre == nombre && z.Id != excluyendoId.Value)
            : _ctx.Zonas.AnyAsync(z => z.Nombre == nombre);

    public async Task<int> AgregarAsync(Zona zona)
    {
        _ctx.Zonas.Add(zona);
        await _ctx.SaveChangesAsync();
        return zona.Id;
    }

    public Task ActualizarAsync(Zona zona)
    {
        _ctx.Zonas.Update(zona);
        return _ctx.SaveChangesAsync();
    }
}
