using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class CorridaBackupRepository : ICorridaBackupRepository
{
    private readonly AppDbContext _ctx;

    public CorridaBackupRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<int> AgregarAsync(CorridaBackup corrida)
    {
        _ctx.CorridasBackup.Add(corrida);
        await _ctx.SaveChangesAsync();
        return corrida.Id;
    }

    public async Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
        => await _ctx.CorridasBackup.OrderByDescending(c => c.FinalizadaEn).ToListAsync();

    public async Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
        => await _ctx.CorridasBackup
            .Where(c => c.Resultado == ResultadoBackup.Exitosa)
            .OrderByDescending(c => c.FinalizadaEn)
            .ToListAsync();

    public Task<CorridaBackup?> ObtenerPorIdAsync(int id)
        => _ctx.CorridasBackup.FirstOrDefaultAsync(c => c.Id == id);

    public Task<CorridaBackup?> ObtenerUltimaExitosaAsync()
        => _ctx.CorridasBackup
            .Where(c => c.Resultado == ResultadoBackup.Exitosa)
            .OrderByDescending(c => c.FinalizadaEn)
            .FirstOrDefaultAsync();

    public async Task EliminarAsync(int id)
    {
        var corrida = await _ctx.CorridasBackup.FirstOrDefaultAsync(c => c.Id == id);
        if (corrida is null) return;
        _ctx.CorridasBackup.Remove(corrida);
        await _ctx.SaveChangesAsync();
    }
}
