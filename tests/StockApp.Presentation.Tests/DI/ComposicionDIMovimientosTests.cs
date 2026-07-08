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

        // AppDbContext apuntado a Postgres — solo para que los repos resuelvan (DI wiring,
        // no se ejecuta ninguna query real en este test, no requiere Docker).
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql("Host=localhost;Port=5432;Database=stockapp_di;Username=stockapp;Password=stockapp"),
            ServiceLifetime.Transient);

        services.AddSingleton<ICurrentSession, InMemorySession>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddTransient<IAuditLogger, AuditService>();

        // ── Catálogo (requerido por Entrada/SalidaRegistroViewModel → IProductoService) ──
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
        services.AddTransient<EntradaRegistroViewModel>();
        services.AddTransient<SalidaRegistroViewModel>();
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
    public void Contenedor_Resuelve_EntradaRegistroViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<EntradaRegistroViewModel>();
        Assert.NotNull(vm);
        Assert.IsType<EntradaRegistroViewModel>(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_SalidaRegistroViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<SalidaRegistroViewModel>();
        Assert.NotNull(vm);
        Assert.IsType<SalidaRegistroViewModel>(vm);
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
