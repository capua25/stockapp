using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Xunit;

namespace StockApp.Infrastructure.Tests;

public class AppDbContextSmokeTests
{
    [Fact]
    public void PuedeCrearYAbrirBaseSqliteEnMemoria()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        using var context = new AppDbContext(options);
        context.Database.OpenConnection();

        var creada = context.Database.EnsureCreated();

        Assert.True(creada);
    }
}
