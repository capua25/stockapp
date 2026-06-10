using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Movimientos;
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

        // ── Catálogo (requerido por MovimientoRegistroViewModel → IProductoService) ──
        services.AddTransient<IProductoRepository, ProductoRepository>();
        services.AddTransient<IUnidadMedidaRepository, UnidadMedidaRepository>();
        services.AddTransient<IProductoService, ProductoService>();

        // ── Movimientos ───────────────────────────────────────────────────────
        services.AddTransient<IMovimientoStockRepository, MovimientoStockRepository>();
        services.AddTransient<IMovimientoStockService, MovimientoStockService>();

        // ── Navegación y confirmación ─────────────────────────────────────────
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(t => sp.GetRequiredService(t)));
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();

        // ── ViewModels de movimientos ─────────────────────────────────────────
        services.AddTransient<MovimientoRegistroViewModel>();
        services.AddTransient<MovimientoHistorialViewModel>();

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

    // ─── ViewModels (D5) ──────────────────────────────────────────────────────

    [Fact]
    public void Contenedor_Resuelve_MovimientoRegistroViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<MovimientoRegistroViewModel>();
        Assert.NotNull(vm);
        Assert.IsType<MovimientoRegistroViewModel>(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_MovimientoHistorialViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<MovimientoHistorialViewModel>();
        Assert.NotNull(vm);
        Assert.IsType<MovimientoHistorialViewModel>(vm);
    }
}
