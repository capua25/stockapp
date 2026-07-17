using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class AdjuntoRepository : IAdjuntoRepository
{
    private readonly AppDbContext _ctx;

    public AdjuntoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Adjunto?> ObtenerPorIdAsync(int id)
        => _ctx.Adjuntos.FirstOrDefaultAsync(a => a.Id == id);

    public Task<IReadOnlyList<Adjunto>> ListarPorGastoAsync(int gastoId)
        => ListarAsync(a => a.GastoId == gastoId);

    public Task<IReadOnlyList<Adjunto>> ListarPorPagoAsync(int pagoGastoId)
        => ListarAsync(a => a.PagoGastoId == pagoGastoId);

    private async Task<IReadOnlyList<Adjunto>> ListarAsync(
        System.Linq.Expressions.Expression<Func<Adjunto, bool>> filtro)
        => await _ctx.Adjuntos
            .Where(a => a.Activo)
            .Where(filtro)
            .OrderByDescending(a => a.FechaAltaUtc)
            .ToListAsync();

    public async Task<int> AgregarAsync(Adjunto adjunto, byte[] contenido)
    {
        _ctx.Adjuntos.Add(adjunto);
        await _ctx.SaveChangesAsync();

        _ctx.AdjuntosContenido.Add(new AdjuntoContenido { Id = adjunto.Id, Contenido = contenido });
        await _ctx.SaveChangesAsync();

        return adjunto.Id;
    }

    public async Task<byte[]?> ObtenerContenidoAsync(int adjuntoId)
    {
        var fila = await _ctx.AdjuntosContenido
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == adjuntoId);
        return fila?.Contenido;
    }

    public Task ActualizarAsync(Adjunto adjunto)
    {
        _ctx.Adjuntos.Update(adjunto);
        return _ctx.SaveChangesAsync();
    }
}
