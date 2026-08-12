using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class AdjuntoDocumentoRepository : IAdjuntoDocumentoRepository
{
    private readonly AppDbContext _ctx;

    public AdjuntoDocumentoRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<int> AgregarAsync(AdjuntoDocumento adjunto, byte[] contenido)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        _ctx.AdjuntosDocumento.Add(adjunto);
        await _ctx.SaveChangesAsync();

        _ctx.AdjuntosDocumentoContenido.Add(
            new AdjuntoDocumentoContenido { Id = adjunto.Id, Contenido = contenido });
        await _ctx.SaveChangesAsync();

        await tx.CommitAsync();

        return adjunto.Id;
    }

    public async Task<IReadOnlyList<AdjuntoDocumento>> ListarPorDocumentoAsync(int documentoId)
        => await _ctx.AdjuntosDocumento
            .Where(a => a.DocumentoAdministrativoId == documentoId)
            .OrderByDescending(a => a.FechaAltaUtc)
            .ThenByDescending(a => a.Id)
            .ToListAsync();

    public Task<AdjuntoDocumento?> ObtenerPorIdAsync(int id)
        => _ctx.AdjuntosDocumento.FirstOrDefaultAsync(a => a.Id == id);

    public async Task<byte[]?> ObtenerContenidoAsync(int adjuntoId)
    {
        var fila = await _ctx.AdjuntosDocumentoContenido
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == adjuntoId);
        return fila?.Contenido;
    }

    public Task ActualizarAsync(AdjuntoDocumento adjunto)
    {
        _ctx.AdjuntosDocumento.Update(adjunto);
        return _ctx.SaveChangesAsync();
    }
}
