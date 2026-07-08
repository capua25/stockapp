using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Infrastructure.Tests.Fixtures;

/// <summary>
/// Levanta UN contenedor PostgreSQL para toda la colección de tests de Infrastructure
/// y aplica las migraciones una sola vez. Requiere Docker disponible en la máquina.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>Crea un AppDbContext nuevo apuntado al contenedor (uno por unidad de trabajo).</summary>
    public AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new AppDbContext(options);
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture> { }
