// Alias para evitar la ambigüedad entre Avalonia.Application y el namespace StockApp.Application.
using AvaloniaApp = Avalonia.Application;

using Avalonia.Controls.ApplicationLifetimes;
using System.IO;
using Avalonia.Markup.Xaml;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.ViewModels;
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
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };

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
        services.AddTransient<AuthService>();
        services.AddTransient<PrimerArranqueService>();
        services.AddTransient<UsuarioService>();

        return services.BuildServiceProvider();
    }
}
