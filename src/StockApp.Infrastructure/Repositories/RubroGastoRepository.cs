using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class RubroGastoRepository : IRubroGastoRepository
{
    private readonly AppDbContext _ctx;

    public RubroGastoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<RubroGasto?> ObtenerPorIdAsync(int id)
        => _ctx.RubrosGasto.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()
        => await _ctx.RubrosGasto.OrderBy(r => r.Codigo).ToListAsync();

    public Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.RubrosGasto.AnyAsync(r => r.Codigo == codigo && r.Id != excluyendoId.Value)
            : _ctx.RubrosGasto.AnyAsync(r => r.Codigo == codigo);

    public async Task<int> AgregarAsync(RubroGasto rubro)
    {
        _ctx.RubrosGasto.Add(rubro);
        await _ctx.SaveChangesAsync();
        return rubro.Id;
    }

    public Task ActualizarAsync(RubroGasto rubro)
    {
        _ctx.RubrosGasto.Update(rubro);
        return _ctx.SaveChangesAsync();
    }
}
