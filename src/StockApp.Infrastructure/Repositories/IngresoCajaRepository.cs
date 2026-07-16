using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class IngresoCajaRepository : IIngresoCajaRepository
{
    private readonly AppDbContext _ctx;

    public IngresoCajaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<IngresoCaja?> ObtenerPorIdAsync(int id)
        => _ctx.IngresosCaja
            .Include(i => i.FuenteFinanciamiento)
            .FirstOrDefaultAsync(i => i.Id == id);

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
        => await _ctx.IngresosCaja
            .Include(i => i.FuenteFinanciamiento)
            .OrderByDescending(i => i.Fecha)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

    public async Task<int> AgregarAsync(IngresoCaja ingreso)
    {
        _ctx.IngresosCaja.Add(ingreso);
        await _ctx.SaveChangesAsync();
        return ingreso.Id;
    }

    public Task ActualizarAsync(IngresoCaja ingreso)
    {
        _ctx.IngresosCaja.Update(ingreso);
        return _ctx.SaveChangesAsync();
    }
}
