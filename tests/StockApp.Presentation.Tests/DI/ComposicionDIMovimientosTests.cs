using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Presentation.Tests.DI;

/// <summary>
/// Red de seguridad para el cableado DI de movimientos de stock (Inc 5 Bloque C).
/// Verifica que IMovimientoStockRepository e IMovimientoStockService se resuelven
/// desde el contenedor sin lanzar excepción.
/// </summary>
public class ComposicionDIMovimientosTests
{
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICurrentSession, InMemorySession>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddTransient<IAuditLogger, AuditService>();

        // ── Movimientos ───────────────────────────────────────────────────────
        services.AddTransient<IMovimientoStockRepository, MovimientoStockRepository>();
        services.AddTransient<IMovimientoStockService, MovimientoStockService>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Contenedor_Resuelve_IMovimientoStockRepository()
    {
        var sp   = CrearContenedor();
        var repo = sp.GetRequiredService<IMovimientoStockRepository>();
        Assert.NotNull(repo);
        Assert.IsType<MovimientoStockRepository>(repo);
    }

    [Fact]
    public void Contenedor_Resuelve_IMovimientoStockService()
    {
        var sp  = CrearContenedor();
        var svc = sp.GetRequiredService<IMovimientoStockService>();
        Assert.NotNull(svc);
        Assert.IsType<MovimientoStockService>(svc);
    }
}
