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
        // La credencial NUNCA se hardcodea acá: el rol "stockapp" es a nivel de cluster
        // Postgres y su password puede rotar (ver deploy/.env). Se lee de la misma
        // convención de configuración que usa StockApp.Api (ConnectionStrings__Default,
        // doble guión bajo = separador de sección en ASP.NET Core). Sin la env var, cae al
        // literal histórico contra la base de diseño (comportamiento sin cambios para quien
        // no la seteó).
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
            ?? "Host=localhost;Port=5432;Database=stockapp_design;Username=stockapp;Password=stockapp";

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new AppDbContext(options);
    }
}
