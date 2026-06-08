using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _context;
    private readonly BackupService _backupService;

    public DatabaseInitializer(AppDbContext context, BackupService backupService)
    {
        _context = context;
        _backupService = backupService;
    }

    /// <summary>
    /// Orquesta el arranque de la BD:
    ///   1. Si el .db ya existe → backup pre-migración.
    ///   2. Aplica migraciones pendientes (MigrateAsync).
    ///   3. Aplica retención de backups.
    /// </summary>
    public async Task InicializarAsync()
    {
        var dbPath = _context.Database.GetDbConnection().DataSource;
        var dbExiste = !string.IsNullOrEmpty(dbPath) && File.Exists(dbPath);

        if (dbExiste)
        {
            await _backupService.CrearBackupAsync("pre-migration");
        }

        await _context.Database.MigrateAsync();

        await _backupService.AplicarRetencionAsync();
    }
}
