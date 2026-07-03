// Alias para evitar la ambigüedad entre Avalonia.Application y el namespace StockApp.Application.
using AvaloniaApp = Avalonia.Application;

using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Infrastructure.Actualizaciones;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Services;
using StockApp.Presentation.Actualizaciones;
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
        // Captura excepciones no manejadas del hilo de UI de Avalonia (ej. lanzadas desde
        // handlers de eventos o bindings). Dispatcher.UIThread ya está inicializado en este
        // punto del ciclo de vida. No marcamos e.Handled = true para no alterar el
        // comportamiento de crash existente: solo agregamos visibilidad vía crash.log.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
            Program.LogFatal("UIThread", e.Exception);

        _serviceProvider = ConfigurarServicios();

        // Inicializa la BD (backup pre-migración + migrate) y arranca el backup periódico.
        // Se corre en el thread pool (Task.Run) para evitar el DEADLOCK que produce bloquear
        // el UI thread de Avalonia sobre cadenas async que postean su continuación al contexto capturado.
        var initializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
        Task.Run(() => initializer.InicializarAsync()).GetAwaiter().GetResult();

        var backupPeriodico = _serviceProvider.GetRequiredService<BackupPeriodicoService>();
        Task.Run(() => backupPeriodico.IniciarAsync()).GetAwaiter().GetResult();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = _serviceProvider.GetRequiredService<ShellViewModel>();

            // Inicializa el shell (decide login / primer arranque) ANTES de asignar el DataContext,
            // y en el thread pool, para no deadlockear el UI thread ni disparar PropertyChanged
            // desde un hilo no-UI con el binding ya activo.
            Task.Run(() => shell.InicializarAsync()).GetAwaiter().GetResult();

            // Defensivo: por defecto ShutdownMode es OnLastWindowClose, lo que puede apagar
            // la app si transitoriamente queda sin ventanas visibles (ej. un diálogo modal
            // que se cierra antes de que la ventana principal termine de mostrarse). Fijamos
            // explícitamente que el ciclo de vida dependa solo del cierre de MainWindow.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            var mainWindow = new MainWindow
            {
                DataContext = shell,
            };

            desktop.MainWindow = mainWindow;

            Program.LogTrace("Arranque", $"MainWindow asignada. ShutdownMode={desktop.ShutdownMode}");

            desktop.ShutdownRequested += (_, e) =>
                Program.LogTrace("ShutdownRequested", $"Cancel={e.Cancel}\n{Environment.StackTrace}");

            mainWindow.Closing += (_, e) =>
                Program.LogTrace("MainWindow.Closing", Environment.StackTrace);

            desktop.Exit += (_, e) =>
            {
                Program.LogTrace("Exit", $"code={e.ApplicationExitCode}\n{Environment.StackTrace}");
                _serviceProvider?.Dispose();
            };
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

        // Repositorios: transient — dependen de AppDbContext (Scoped), evita captive dependency.
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
        services.AddTransient<StockCategoriaViewModel>();
        services.AddTransient<HistorialPorProductoViewModel>();
        services.AddTransient<MasMovidosViewModel>();
        services.AddTransient<AuditoriaLogViewModel>();

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

        // ── Inc 7 Fase A: actualizador in-app ─────────────────────────────────

        // UpdaterOptions: configura fuentes. GitHub es primaria (real); feed propio es fallback opcional.
        services.AddSingleton(new UpdaterOptions
        {
            GitHubRepoUrl  = "https://github.com/capua25/stockapp",
            GitHubPrerelease = false,
            FeedPropiUrl   = null,    // null → solo GitHub; setear URL para habilitar feed propio
            Orden          = OrdenFuentes.GitHubPrimero,
        });

        // Gateway: singleton — envuelve UpdateManager de Velopack (proceso-global)
        services.AddSingleton<IVelopackGateway, VelopackGatewayReal>();

        // IUpdateService: singleton — mantiene _updatePendiente entre BuscarAsync→DescargarAsync→Aplicar
        services.AddSingleton<IUpdateService, VelopackUpdateService>();

        // PoliticaUxActualizacion: singleton — sin dependencias propias, decide AccionUx a partir
        // de UpdateCheckResult. Requerida por CoordinadorActualizacion.
        services.AddSingleton<PoliticaUxActualizacion>();

        // CoordinadorActualizacion: singleton — orquesta chequeo→política en background al arranque.
        // Los ViewModels de actualización (Banner/Modal/Bloqueo) se instancian directamente
        // con la AccionUx resultante; no se registran en DI porque toman AccionUx en su constructor.
        services.AddSingleton<CoordinadorActualizacion>();

        return services.BuildServiceProvider();
    }
}
