using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class DatabaseInitializer
{
    private readonly AppDbContext _context;

    public DatabaseInitializer(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Aplica las migraciones pendientes al arrancar. El backup file-based (SQLite) se removió;
    /// el respaldo server-side vía pg_dump queda fuera de alcance de Fase 1 (design §7).
    /// </summary>
    public async Task InicializarAsync()
    {
        await _context.Database.MigrateAsync();
    }
}
