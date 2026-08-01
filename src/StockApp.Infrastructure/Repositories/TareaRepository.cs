using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class TareaRepository : ITareaRepository
{
    private readonly AppDbContext _ctx;

    public TareaRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<Tarea> ConIncludes() =>
        _ctx.Tareas
            .Include(t => t.TomadaPor)
            .Include(t => t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id));

    public async Task<int> AgregarAsync(Tarea tarea)
    {
        _ctx.Tareas.Add(tarea);
        await _ctx.SaveChangesAsync();
        return tarea.Id;
    }

    public Task<Tarea?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
        => await ConIncludes().OrderByDescending(t => t.FechaCreacion).ToListAsync();

    /// <summary>
    /// Notas nuevas (Id == 0, agregadas por el servicio a la colección de una Tarea ya
    /// tracked): se agregan EXPLÍCITAMENTE al DbSet en vez de confiar en el fixup automático
    /// del change tracker sobre una colección modificada a mano — mismo criterio explícito que
    /// GastoRepository.AsignarGastoAMovimientosAsync (loop + asignación de FK + SaveChanges).
    /// </summary>
    public async Task ActualizarAsync(Tarea tarea)
    {
        foreach (var nota in tarea.Notas.Where(n => n.Id == 0))
        {
            nota.TareaId = tarea.Id;
            _ctx.NotasTarea.Add(nota);
        }

        _ctx.Tareas.Update(tarea);
        await _ctx.SaveChangesAsync();
    }
}
