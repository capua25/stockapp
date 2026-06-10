using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class UnidadMedidaRepository : IUnidadMedidaRepository
{
    private readonly AppDbContext _ctx;

    public UnidadMedidaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<UnidadMedida?> ObtenerPorIdAsync(int id)
        => _ctx.UnidadesMedida.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync()
        => await _ctx.UnidadesMedida.OrderBy(u => u.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.UnidadesMedida.AnyAsync(u => u.Nombre == nombre && u.Id != excluyendoId.Value)
            : _ctx.UnidadesMedida.AnyAsync(u => u.Nombre == nombre);

    public Task<bool> ExisteAbreviaturaAsync(string abreviatura, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.UnidadesMedida.AnyAsync(u => u.Abreviatura == abreviatura && u.Id != excluyendoId.Value)
            : _ctx.UnidadesMedida.AnyAsync(u => u.Abreviatura == abreviatura);

    public async Task<int> AgregarAsync(UnidadMedida unidadMedida)
    {
        _ctx.UnidadesMedida.Add(unidadMedida);
        await _ctx.SaveChangesAsync();
        return unidadMedida.Id;
    }

    public Task ActualizarAsync(UnidadMedida unidadMedida)
    {
        _ctx.UnidadesMedida.Update(unidadMedida);
        return _ctx.SaveChangesAsync();
    }
}
