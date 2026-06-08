using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StockApp.Infrastructure.Persistence;

/// <summary>
/// Factory de design-time para que las herramientas EF (migrations add, database update)
/// puedan instanciar AppDbContext sin necesitar el startup project completo.
/// Solo se usa en tiempo de diseño; no interviene en el arranque de la app.
/// </summary>
internal class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=stockapp-design.db")
            .Options;

        return new AppDbContext(options);
    }
}
