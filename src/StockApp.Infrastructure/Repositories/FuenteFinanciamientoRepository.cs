using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class FuenteFinanciamientoRepository : IFuenteFinanciamientoRepository
{
    private readonly AppDbContext _ctx;

    public FuenteFinanciamientoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id)
        => _ctx.FuentesFinanciamiento.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()
        => await _ctx.FuentesFinanciamiento.OrderBy(f => f.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.FuentesFinanciamiento.AnyAsync(f => f.Nombre == nombre && f.Id != excluyendoId.Value)
            : _ctx.FuentesFinanciamiento.AnyAsync(f => f.Nombre == nombre);

    public async Task<int> AgregarAsync(FuenteFinanciamiento fuente)
    {
        _ctx.FuentesFinanciamiento.Add(fuente);
        await _ctx.SaveChangesAsync();
        return fuente.Id;
    }

    public Task ActualizarAsync(FuenteFinanciamiento fuente)
    {
        _ctx.FuentesFinanciamiento.Update(fuente);
        return _ctx.SaveChangesAsync();
    }
}
