using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ProyectoRepository : IProyectoRepository
{
    private readonly AppDbContext _ctx;

    public ProyectoRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<Proyecto> ConIncludes() =>
        _ctx.Proyectos
            .Include(p => p.Zona)
            .Include(p => p.DimensionTematica)
            .Include(p => p.OrganismoResponsable)
            .Include(p => p.OrigenFinanciamiento)
            .Include(p => p.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id));

    public async Task<int> AgregarAsync(Proyecto proyecto)
    {
        _ctx.Proyectos.Add(proyecto);
        await _ctx.SaveChangesAsync();
        return proyecto.Id;
    }

    public Task<Proyecto?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(p => p.Id == id);

    public async Task<IReadOnlyList<Proyecto>> ListarAsync()
        => await ConIncludes().OrderByDescending(p => p.FechaCreacion).ToListAsync();

    public async Task<IReadOnlyList<Gasto>> ListarGastosActivosAsync(int proyectoId)
        => await _ctx.Gastos
            .Include(g => g.Proveedor)
            .Where(g => g.ProyectoId == proyectoId && g.Activo)
            .OrderByDescending(g => g.Fecha)
            .ToListAsync();

    /// <summary>
    /// Notas nuevas (Id == 0) se agregan explícitamente al DbSet, mismo criterio que
    /// TareaRepository.ActualizarAsync: no se confía en el fixup del change tracker.
    /// </summary>
    public async Task ActualizarAsync(Proyecto proyecto)
    {
        foreach (var nota in proyecto.Notas.Where(n => n.Id == 0))
        {
            nota.ProyectoId = proyecto.Id;
            _ctx.NotasProyecto.Add(nota);
        }

        _ctx.Proyectos.Update(proyecto);
        await _ctx.SaveChangesAsync();
    }
}
