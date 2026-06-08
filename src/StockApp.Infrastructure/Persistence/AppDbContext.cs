using Microsoft.EntityFrameworkCore;

namespace StockApp.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    // Las entidades (DbSet<>) se agregan en el Incremento 2.
}
