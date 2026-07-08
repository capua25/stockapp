using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Services;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

/// <summary>
/// DatabaseInitializer contra PostgreSQL real: aplica migraciones sin error y deja
/// el esquema al día. El backup file-based se removió (concepto SQLite; el reemplazo
/// por pg_dump es Fase posterior). Requiere Docker (Testcontainers).
/// </summary>
[Collection("Postgres")]
public class DatabaseInitializerTests
{
    private readonly PostgresFixture _fixture;

    public DatabaseInitializerTests(PostgresFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task InicializarAsync_AplicaMigraciones_SinError()
    {
        using var ctx = _fixture.CrearContexto();
        var initializer = new DatabaseInitializer(ctx);

        var ex = await Record.ExceptionAsync(() => initializer.InicializarAsync());

        Assert.Null(ex);
    }

    [Fact]
    public async Task InicializarAsync_DejaMigracionesAlDia()
    {
        using var ctx = _fixture.CrearContexto();
        var initializer = new DatabaseInitializer(ctx);

        await initializer.InicializarAsync();

        var pendientes = await ctx.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendientes);
    }
}
