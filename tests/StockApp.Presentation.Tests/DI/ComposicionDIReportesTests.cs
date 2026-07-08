using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.DI;

/// <summary>
/// Red de seguridad para el cableado DI de reportes (Inc 6).
/// Verifica que los servicios y ViewModels de reportes se resuelven desde el contenedor.
/// </summary>
public class ComposicionDIReportesTests
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
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddTransient<IAuditLogger, AuditService>();

        // ── Movimientos (requerido por ReporteStockService) ───────────────────
        services.AddTransient<IMovimientoStockRepository, MovimientoStockRepository>();
        services.AddTransient<IMovimientoStockService, MovimientoStockService>();

        // ── Reportes ──────────────────────────────────────────────────────────
        services.AddTransient<IReporteStockRepository, ReporteStockRepository>();
        services.AddTransient<IReporteStockService, ReporteStockService>();
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── Guardado de archivos ──────────────────────────────────────────────
        services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();

        // ── ViewModels de reportes ────────────────────────────────────────────
        services.AddTransient<ValorizacionViewModel>();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Contenedor_Resuelve_IServicioGuardadoArchivo()
    {
        var sp = CrearContenedor();
        var servicio = sp.GetRequiredService<IServicioGuardadoArchivo>();
        Assert.NotNull(servicio);
        Assert.IsType<ServicioGuardadoArchivo>(servicio);
    }

    [Fact]
    public void Contenedor_Resuelve_ValorizacionViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ValorizacionViewModel>();
        Assert.NotNull(vm);
        Assert.IsType<ValorizacionViewModel>(vm);
    }
}
