// Alias para evitar la ambigüedad entre Avalonia.Application y el namespace StockApp.Application.
using AvaloniaApp = Avalonia.Application;

using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.IO;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.Views;

namespace StockApp.Presentation;

public partial class App : AvaloniaApp
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        _serviceProvider = ConfigurarServicios();

        // Inicializa la BD (backup pre-migración + migrate) y arranca el backup periódico
        var initializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
        initializer.InicializarAsync().GetAwaiter().GetResult();

        var backupPeriodico = _serviceProvider.GetRequiredService<BackupPeriodicoService>();
        backupPeriodico.IniciarAsync().GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = _serviceProvider.GetRequiredService<ShellViewModel>();

            var mainWindow = new MainWindow
            {
                DataContext = shell,
            };

            // Inicializa el shell de forma asíncrona antes de mostrar la ventana.
            shell.InicializarAsync().GetAwaiter().GetResult();

            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigurarServicios()
    {
        var services = new ServiceCollection();

        // ── Inc 2: infraestructura de datos y backup ──────────────────────────

        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();

        // AppDbContext: transient para evitar captive dependency en app desktop
        // (no hay scope de request; los servicios que lo consumen son transient también).
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
            Directory.CreateDirectory(pathProvider.GetDataDirectory());
            options.UseSqlite($"DataSource={pathProvider.GetDatabasePath()}");
        }, ServiceLifetime.Transient);

        // BackupService: singleton — solo rutas de archivos, sin estado de EF
        services.AddSingleton<BackupService>(sp =>
        {
            var pathProvider = sp.GetRequiredService<IUserDataPathProvider>();
            return new BackupService(
                pathProvider.GetDatabasePath(),
                pathProvider.GetBackupsDirectory());
        });

        services.AddTransient<DatabaseInitializer>();

        // BackupPeriodicoService: singleton — mantiene el timer vivo mientras corre la app
        services.AddSingleton<BackupPeriodicoService>();

        // ── Inc 3: autenticación, autorización y sesión ───────────────────────

        // Singleton: la sesión debe ser única en toda la app
        services.AddSingleton<ICurrentSession, InMemorySession>();

        services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

        // Transient: dependen de AppDbContext (transient) — evita captive dependency
        services.AddTransient<IUsuarioRepository, UsuarioRepository>();
        services.AddTransient<IAuditLogger, AuditService>();

        // AuthorizationService: sin estado, singleton es suficiente
        services.AddSingleton<IAuthorizationService, AuthorizationService>();

        // Servicios de Application: transient — sin estado propio
        services.AddTransient<IAuthService, AuthService>();
        services.AddTransient<IPrimerArranqueService, PrimerArranqueService>();
        services.AddTransient<IUsuarioService, UsuarioService>();

        // ── Inc 4: catálogo — repositorios y servicios ───────────────────────

        services.AddTransient<IProductoRepository, ProductoRepository>();
        services.AddTransient<ICategoriaRepository, CategoriaRepository>();
        services.AddTransient<IProveedorRepository, ProveedorRepository>();
        services.AddTransient<IUnidadMedidaRepository, UnidadMedidaRepository>();

        services.AddTransient<IProductoService, ProductoService>();
        services.AddTransient<ICategoriaService, CategoriaService>();
        services.AddTransient<IProveedorService, ProveedorService>();
        services.AddTransient<IUnidadMedidaService, UnidadMedidaService>();

        // ── Inc 5: movimientos — repositorio y servicio ───────────────────────

        services.AddTransient<IMovimientoStockRepository, MovimientoStockRepository>();
        services.AddTransient<IMovimientoStockService, MovimientoStockService>();

        // ── Inc 5: confirmación de stock insuficiente ─────────────────────────
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();

        // ── Inc 6: reportes y auditoría — repositorios y servicios ────────────

        // Repositorios: transient — dependen de AppDbContext (transient), evita captive dependency.
        services.AddTransient<IReporteStockRepository, ReporteStockRepository>();
        services.AddTransient<IAuditoriaQueryRepository, AuditoriaQueryRepository>();

        // Servicios de Application: transient — sin estado propio.
        services.AddTransient<IReporteStockService, ReporteStockService>();
        services.AddTransient<IAuditoriaQueryService, AuditoriaQueryService>();
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── Inc 6: guardado de archivos (file picker) ─────────────────────────
        // Singleton — sin estado, accede a la ventana principal vía IStorageProvider.
        services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();

        // ── Inc 5: VMs de movimientos ─────────────────────────────────────────
        services.AddTransient<MovimientoRegistroViewModel>();
        services.AddTransient<MovimientoHistorialViewModel>();

        // ── Inc 6: VMs de reportes ────────────────────────────────────────────
        services.AddTransient<ValorizacionViewModel>();

        // ── Inc 4: navegación ─────────────────────────────────────────────────

        // NavigationService: singleton — mantiene el VM activo para toda la sesión
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(t => sp.GetRequiredService(t)));

        // VMs de catálogo: transient — se resuelven por el NavigationService
        services.AddTransient<ShellMainViewModel>();
        services.AddTransient<ProductoListViewModel>();
        services.AddTransient<ProductoFormViewModel>();
        services.AddTransient<CategoriaListViewModel>();
        services.AddTransient<CategoriaFormViewModel>();
        services.AddTransient<ProveedorListViewModel>();
        services.AddTransient<ProveedorFormViewModel>();
        services.AddTransient<UnidadMedidaListViewModel>();
        services.AddTransient<UnidadMedidaFormViewModel>();

        // ── Presentation: ViewModels del shell ───────────────────────────────

        // ShellViewModel: singleton — vive toda la vida de la app
        services.AddSingleton<ShellViewModel>();

        return services.BuildServiceProvider();
    }
}
