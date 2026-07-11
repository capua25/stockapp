// Alias para evitar la ambigüedad entre Avalonia.Application y el namespace StockApp.Application.
using AvaloniaApp = Avalonia.Application;

using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
        _serviceProvider = ConfigurarServicios();

        // Captura excepciones no manejadas del hilo de UI de Avalonia (ej. lanzadas desde
        // handlers de eventos o bindings). Dispatcher.UIThread ya está inicializado en este
        // punto del ciclo de vida.
        //
        // Antes NO se marcaba e.Handled = true a propósito ("para no alterar el comportamiento
        // de crash existente"): la app moría igual ante cualquier excepción no atrapada, solo
        // que con más visibilidad vía crash.log. Se revierte esa decisión: un crash real (dar
        // de baja una unidad de medida ya inactiva — InvalidOperationException de una regla de
        // negocio VÁLIDA, propagada sin manejar desde un [RelayCommand]) demostró que dejar
        // morir el proceso ante una excepción de dominio esperable es un bug sistémico, no un
        // comportamiento aceptable. Esta red es el ÚLTIMO recurso: el manejo fino (confirmación
        // + try/catch de las excepciones de dominio esperables) va en los comandos que pueden
        // fallar — ver BajaAsync en UnidadMedidaListViewModel/CategoriaListViewModel/
        // ProveedorListViewModel. Si algo se escapa igual de esa capa fina (bug no previsto),
        // acá se loguea a crash.log y se informa al usuario en vez de crashear, reusando el
        // mismo mecanismo de aviso (IConfirmacionService.InformarAsync) que usan los comandos.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.LogFatal("UIThread", e.Exception);
            e.Handled = true;

            var confirmacion = _serviceProvider?.GetService<IConfirmacionService>();
            if (confirmacion is not null)
            {
                _ = confirmacion.InformarAsync(
                    "Ocurrió un error inesperado. Podés seguir usando la aplicación; " +
                    "si el problema persiste, contactá a soporte.");
            }
        };

        // Inicializa la BD (migrate) al arrancar. El backup file-based (SQLite) se removió:
        // con Postgres el respaldo real es pg_dump server-side, diferido a la Fase 4 (design §7).
        // Se corre en el thread pool (Task.Run) para evitar el DEADLOCK que produce bloquear
        // el UI thread de Avalonia sobre cadenas async que postean su continuación al contexto capturado.
        var initializer = _serviceProvider.GetRequiredService<DatabaseInitializer>();
        Task.Run(() => initializer.InicializarAsync()).GetAwaiter().GetResult();

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
            desktop.Exit += (_, _) => _serviceProvider?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider ConfigurarServicios()
    {
        var services = new ServiceCollection();

        // Configuración externa: appsettings.json es opcional (optional: true) para que su
        // ausencia en el output no tire excepción — en ese caso se cae al fallback defensivo
        // de UpdaterOptions.GitHubRepoUrlDefault más abajo.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        // ── Inc 2: infraestructura de datos y backup ──────────────────────────

        services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();

        // AppDbContext: transient para evitar captive dependency en app desktop
        // (no hay scope de request; los servicios que lo consumen son transient también).
        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
                    "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");
            options.UseNpgsql(connectionString);
        }, ServiceLifetime.Transient);

        services.AddTransient<DatabaseInitializer>();

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

        // ── Marshaling al UI thread para asignaciones desde background (ej: overlay
        // de actualización en ShellViewModel) ─────────────────────────────────────
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // ── Info de la app (versión mostrada en login y shell) ────────────────
        services.AddSingleton<IInfoApp, InfoApp>();

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
        services.AddTransient<EntradaRegistroViewModel>();
        services.AddTransient<SalidaRegistroViewModel>();
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
        services.AddTransient<InicioViewModel>();
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
        // La URL y el flag de prerelease vienen de appsettings.json (sección "Updater"); si la key
        // falta o el archivo no existe, se cae al fallback defensivo de UpdaterOptions.
        var repoUrl = configuration["Updater:GitHubRepoUrl"];
        if (string.IsNullOrWhiteSpace(repoUrl))
        {
            repoUrl = UpdaterOptions.GitHubRepoUrlDefault;
        }

        if (!bool.TryParse(configuration["Updater:GitHubPrerelease"], out var prerelease))
        {
            prerelease = false;
        }

        services.AddSingleton(new UpdaterOptions
        {
            GitHubRepoUrl  = repoUrl,
            GitHubPrerelease = prerelease,
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
