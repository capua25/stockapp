using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class OrganismoResponsableRepository : IOrganismoResponsableRepository
{
    private readonly AppDbContext _ctx;

    public OrganismoResponsableRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<OrganismoResponsable?> ObtenerPorIdAsync(int id)
        => _ctx.OrganismosResponsables.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<OrganismoResponsable>> ListarTodasAsync()
        => await _ctx.OrganismosResponsables.OrderBy(o => o.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.OrganismosResponsables.AnyAsync(o => o.Nombre == nombre && o.Id != excluyendoId.Value)
            : _ctx.OrganismosResponsables.AnyAsync(o => o.Nombre == nombre);

    public async Task<int> AgregarAsync(OrganismoResponsable organismo)
    {
        _ctx.OrganismosResponsables.Add(organismo);
        await _ctx.SaveChangesAsync();
        return organismo.Id;
    }

    public Task ActualizarAsync(OrganismoResponsable organismo)
    {
        _ctx.OrganismosResponsables.Update(organismo);
        return _ctx.SaveChangesAsync();
    }
}
