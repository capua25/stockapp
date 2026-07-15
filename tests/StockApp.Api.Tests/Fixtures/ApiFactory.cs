using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StockApp.Application.Licenciamiento;
using StockApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Levanta la API completa (WebApplicationFactory) contra un Postgres real de
/// Testcontainers — mismo patrón que PostgresFixture en
/// tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs (Fase 1), pero
/// arrancando el host HTTP completo en vez de solo un AppDbContext. Sobrescribe
/// 'ConnectionStrings:Default' para apuntar al contenedor.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecretDePrueba = "clave-de-prueba-de-al-menos-32-caracteres-1234567890";
    public const string AdminUsuarioDePrueba = "admin-arranque";
    public const string AdminPasswordDePrueba = "arranque-secreta-123";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:Secret"] = JwtSecretDePrueba,
                ["Bootstrap:AdminUser"] = AdminUsuarioDePrueba,
                ["Bootstrap:Password"] = AdminPasswordDePrueba,
                ["Licencia:ClavePublicaBase64"] = ClavesDePrueba.ClavePublicaBase64,
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IFingerprintMaquina, FingerprintMaquinaFake>());
            services.Replace(ServiceDescriptor.Singleton<IAlmacenLicencia>(
                _ => new AlmacenLicenciaEnMemoria(ClavesDePrueba.EmitirLicencia())));
        });
    }

    /// <summary>
    /// Connection string real del contenedor Testcontainers (puerto aleatorio del host).
    /// Expuesta para que los tests puedan probar que el AppDbContext resuelto por DI
    /// dentro de la API (Program.cs) apunta acá y no al Postgres local de desarrollo.
    /// </summary>
    public string ConnectionStringDelContenedor => _container.GetConnectionString();

    /// <summary>Crea un AppDbContext nuevo apuntado al contenedor (para setup/seed de datos en tests).</summary>
    public AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options);
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory> { }
