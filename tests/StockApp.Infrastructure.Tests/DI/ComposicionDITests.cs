using Microsoft.Extensions.DependencyInjection;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.DI;

/// <summary>
/// Verifica que el contenedor DI resuelve los servicios de Infrastructure sin throw.
/// Prueba la composición directamente (no depende de Avalonia). El backup file-based
/// (BackupService/BackupPeriodicoService) se removió del arranque en la Task 7 de Fase 1:
/// con Postgres no hay archivo .db que copiar (pg_dump server-side es Fase 4).
/// </summary>
public class ComposicionDITests
{
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        // Mismo cableado que App.axaml.cs
        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();

        services.AddTransient<DatabaseInitializer>(sp =>
        {
            // En tests no instanciamos AppDbContext real; validamos sólo que el
            // contenedor puede construir los servicios que no dependen de EF.
            // DatabaseInitializer necesita AppDbContext — lo omitimos aquí y
            // probamos su resolución en el test de integración de DatabaseInitializer.
            throw new InvalidOperationException(
                "DatabaseInitializer no se puede resolver sin AppDbContext en este test.");
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Contenedor_Resuelve_IUserDataPathProvider()
    {
        var sp = CrearContenedor();
        var provider = sp.GetRequiredService<IUserDataPathProvider>();
        Assert.NotNull(provider);
        Assert.IsType<UserDataPathProvider>(provider);
    }
}
