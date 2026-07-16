using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class LineaPoaRepository : ILineaPoaRepository
{
    private readonly AppDbContext _ctx;

    public LineaPoaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<LineaPoa?> ObtenerPorIdAsync(int id)
        => _ctx.LineasPoa
            .Include(l => l.Asignaciones)
            .ThenInclude(a => a.FuenteFinanciamiento)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()
        => await _ctx.LineasPoa
            .Include(l => l.Asignaciones)
            .ThenInclude(a => a.FuenteFinanciamiento)
            .OrderByDescending(l => l.Ejercicio)
            .ThenBy(l => l.Nombre)
            .ToListAsync();

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.LineasPoa.AnyAsync(l => l.Nombre == nombre && l.Ejercicio == ejercicio && l.Id != excluyendoId.Value)
            : _ctx.LineasPoa.AnyAsync(l => l.Nombre == nombre && l.Ejercicio == ejercicio);

    public async Task<int> AgregarAsync(LineaPoa linea)
    {
        _ctx.LineasPoa.Add(linea);  // inserta el grafo completo (línea + asignaciones)
        await _ctx.SaveChangesAsync();
        return linea.Id;
    }

    public async Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones)
    {
        // Las asignaciones son hijas del agregado: se reemplazan con delete explícito +
        // insert (DeleteBehavior.Restrict NO impide deletes explícitos de las hijas;
        // solo bloquea cascadas desde el padre). Se crean instancias frescas con Id = 0
        // para que EF las inserte, sin arrastrar tracking previo.
        _ctx.AsignacionesPresupuestales.RemoveRange(linea.Asignaciones);
        linea.Asignaciones = nuevasAsignaciones
            .Select(a => new AsignacionPresupuestal
            {
                LineaPoaId = linea.Id,
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList();

        _ctx.LineasPoa.Update(linea);
        await _ctx.SaveChangesAsync();
    }

    public async Task ActualizarSinAsignacionesAsync(LineaPoa linea)
    {
        // La línea llega tracked (vía ObtenerPorIdAsync) con sus campos propios ya
        // modificados por el caller (ej. Activo = false). No se toca la colección
        // Asignaciones: un simple SaveChanges persiste solo lo que cambió en la fila
        // padre, sin el delete+insert físico de las hijas que hace ActualizarAsync.
        await _ctx.SaveChangesAsync();
    }
}
