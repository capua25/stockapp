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

    // Ver LockInicializacionContenedores: se retiene desde que arranca el contenedor
    // hasta que se dispone, para que este proceso (Infrastructure.Tests) y
    // StockApp.Api.Tests -- que corre como proceso `dotnet test` separado cuando se
    // invoca `dotnet test StockApp.sln` -- nunca tengan contenedores/Ryuk vivos a la vez.
    private IDisposable? _lockContenedores;

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        _lockContenedores = await LockInicializacionContenedores.AdquirirAsync();
        try
        {
            await _container.StartAsync();
            await using var ctx = CrearContexto();
            await ctx.Database.MigrateAsync();
        }
        catch
        {
            _lockContenedores.Dispose();
            _lockContenedores = null;
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await _container.DisposeAsync();
        }
        finally
        {
            _lockContenedores?.Dispose();
        }
    }

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
