using Microsoft.Extensions.DependencyInjection;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.DI;

/// <summary>
/// Verifica que el contenedor DI resuelve los servicios de Infrastructure sin throw.
/// Prueba la composición directamente (no depende de Avalonia).
/// </summary>
public class ComposicionDITests
{
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        // Mismo cableado que App.axaml.cs
        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();

        services.AddSingleton<BackupService>(sp =>
        {
            var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
            return new BackupService(
                pathProvider.GetDatabasePath(),
                pathProvider.GetBackupsDirectory());
        });

        services.AddSingleton<BackupPeriodicoService>();
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

    [Fact]
    public void Contenedor_Resuelve_BackupService()
    {
        var sp = CrearContenedor();
        var service = sp.GetRequiredService<BackupService>();
        Assert.NotNull(service);
    }

    [Fact]
    public void Contenedor_Resuelve_BackupPeriodicoService()
    {
        var sp = CrearContenedor();
        var service = sp.GetRequiredService<BackupPeriodicoService>();
        Assert.NotNull(service);
        Assert.IsType<BackupPeriodicoService>(service);
    }
}
