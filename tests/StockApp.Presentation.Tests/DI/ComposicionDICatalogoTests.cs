using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using Xunit;

namespace StockApp.Presentation.Tests.DI;

/// <summary>
/// Red de seguridad para el cableado DI del catálogo (Inc 4).
/// Verifica que el contenedor puede resolver repos, servicios y VMs sin lanzar excepción.
/// Mismo patrón que ComposicionDITests en Infrastructure.Tests, pero con toda la cadena
/// de catálogo + navegación.
/// </summary>
public class ComposicionDICatalogoTests
{
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        // AppDbContext en memoria (SQLite in-memory) — solo para que los repos resuelvan
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite("DataSource=:memory:"),
            ServiceLifetime.Transient);

        // ── Infraestructura de sesión y auditoría ─────────────────────────────
        services.AddSingleton<ICurrentSession, InMemorySession>();
        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddTransient<IAuditLogger, AuditService>();

        // ── Repositorios de catálogo ──────────────────────────────────────────
        services.AddTransient<IProductoRepository, ProductoRepository>();
        services.AddTransient<ICategoriaRepository, CategoriaRepository>();
        services.AddTransient<IProveedorRepository, ProveedorRepository>();
        services.AddTransient<IUnidadMedidaRepository, UnidadMedidaRepository>();

        // ── Servicios de catálogo ─────────────────────────────────────────────
        services.AddTransient<IProductoService, ProductoService>();
        services.AddTransient<ICategoriaService, CategoriaService>();
        services.AddTransient<IProveedorService, ProveedorService>();
        services.AddTransient<IUnidadMedidaService, UnidadMedidaService>();

        // ── Navegación ────────────────────────────────────────────────────────
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(t => sp.GetRequiredService(t)));

        // ── Info de la app (versión mostrada en login y shell) ────────────────
        services.AddSingleton<IInfoApp, InfoApp>();

        // ── ViewModels de catálogo (transient, igual que App.axaml.cs) ────────
        services.AddTransient<ShellMainViewModel>();
        services.AddTransient<ProductoListViewModel>();
        services.AddTransient<ProductoFormViewModel>();
        services.AddTransient<CategoriaListViewModel>();
        services.AddTransient<CategoriaFormViewModel>();
        services.AddTransient<ProveedorListViewModel>();
        services.AddTransient<ProveedorFormViewModel>();
        services.AddTransient<UnidadMedidaListViewModel>();
        services.AddTransient<UnidadMedidaFormViewModel>();

        return services.BuildServiceProvider();
    }

    // ─── Repositorios ──────────────────────────────────────────────────────────

    [Fact]
    public void Contenedor_Resuelve_IProductoRepository()
    {
        var sp = CrearContenedor();
        var repo = sp.GetRequiredService<IProductoRepository>();
        Assert.NotNull(repo);
        Assert.IsType<ProductoRepository>(repo);
    }

    [Fact]
    public void Contenedor_Resuelve_ICategoriaRepository()
    {
        var sp = CrearContenedor();
        var repo = sp.GetRequiredService<ICategoriaRepository>();
        Assert.NotNull(repo);
        Assert.IsType<CategoriaRepository>(repo);
    }

    [Fact]
    public void Contenedor_Resuelve_IProveedorRepository()
    {
        var sp = CrearContenedor();
        var repo = sp.GetRequiredService<IProveedorRepository>();
        Assert.NotNull(repo);
        Assert.IsType<ProveedorRepository>(repo);
    }

    [Fact]
    public void Contenedor_Resuelve_IUnidadMedidaRepository()
    {
        var sp = CrearContenedor();
        var repo = sp.GetRequiredService<IUnidadMedidaRepository>();
        Assert.NotNull(repo);
        Assert.IsType<UnidadMedidaRepository>(repo);
    }

    // ─── Servicios ────────────────────────────────────────────────────────────

    [Fact]
    public void Contenedor_Resuelve_IProductoService()
    {
        var sp = CrearContenedor();
        var svc = sp.GetRequiredService<IProductoService>();
        Assert.NotNull(svc);
        Assert.IsType<ProductoService>(svc);
    }

    [Fact]
    public void Contenedor_Resuelve_ICategoriaService()
    {
        var sp = CrearContenedor();
        var svc = sp.GetRequiredService<ICategoriaService>();
        Assert.NotNull(svc);
        Assert.IsType<CategoriaService>(svc);
    }

    [Fact]
    public void Contenedor_Resuelve_IProveedorService()
    {
        var sp = CrearContenedor();
        var svc = sp.GetRequiredService<IProveedorService>();
        Assert.NotNull(svc);
        Assert.IsType<ProveedorService>(svc);
    }

    [Fact]
    public void Contenedor_Resuelve_IUnidadMedidaService()
    {
        var sp = CrearContenedor();
        var svc = sp.GetRequiredService<IUnidadMedidaService>();
        Assert.NotNull(svc);
        Assert.IsType<UnidadMedidaService>(svc);
    }

    // ─── NavigationService ────────────────────────────────────────────────────

    [Fact]
    public void Contenedor_Resuelve_INavigationService()
    {
        var sp = CrearContenedor();
        var nav = sp.GetRequiredService<INavigationService>();
        Assert.NotNull(nav);
    }

    // ─── ViewModels ───────────────────────────────────────────────────────────

    [Fact]
    public void Contenedor_Resuelve_ShellMainViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ShellMainViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_ProductoListViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ProductoListViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_ProductoFormViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ProductoFormViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_CategoriaListViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<CategoriaListViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_CategoriaFormViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<CategoriaFormViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_ProveedorListViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ProveedorListViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_ProveedorFormViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<ProveedorFormViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_UnidadMedidaListViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<UnidadMedidaListViewModel>();
        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_UnidadMedidaFormViewModel()
    {
        var sp = CrearContenedor();
        var vm = sp.GetRequiredService<UnidadMedidaFormViewModel>();
        Assert.NotNull(vm);
    }
}
