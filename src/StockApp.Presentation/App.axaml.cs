// Alias para evitar la ambigüedad entre Avalonia.Application y el namespace StockApp.Application.
using AvaloniaApp = Avalonia.Application;

using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StockApp.ApiClient;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
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
        // Red de ÚLTIMO recurso (ver historia en el repo: un crash real por una excepción
        // de dominio esperable demostró que dejar morir el proceso es un bug sistémico).
        // El manejo fino va en los comandos; si algo escapa igual, acá se loguea a
        // crash.log y se informa al usuario en vez de crashear. Fase 3b: si lo que escapó
        // es ServidorNoDisponibleException (API caída en un flujo sin catch propio), se
        // muestra su mensaje accionable en lugar del genérico.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            Program.LogFatal("UIThread", e.Exception);
            e.Handled = true;

            var confirmacion = _serviceProvider?.GetService<IConfirmacionService>();
            if (confirmacion is not null)
            {
                var mensaje = e.Exception is ServidorNoDisponibleException
                    ? e.Exception.Message
                    : "Ocurrió un error inesperado. Podés seguir usando la aplicación; " +
                      "si el problema persiste, contactá a soporte.";
                _ = confirmacion.InformarAsync(mensaje);
            }
        };

        // Fase 3b: ya NO se inicializa ninguna base de datos acá — la API migra su BD al
        // arrancar (Fase 3a, D9). El desktop solo necesita alcanzar la API por HTTP.

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var shell = _serviceProvider.GetRequiredService<ShellViewModel>();

            // Sesión vencida (spec 3b, OQ-4): un 401 con token dispara el evento en
            // ApiSession (via AuthTokenHandler); acá se marshalea al UI thread y se navega
            // al login con aviso. UN solo lugar para toda la app.
            var apiSession   = _serviceProvider.GetRequiredService<ApiSession>();
            var uiDispatcher = _serviceProvider.GetRequiredService<IUiDispatcher>();
            apiSession.SesionVencida += () => uiDispatcher.Post(
                () => shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo."));

            // Inicializa el shell (decide login / primer arranque) ANTES de asignar el
            // DataContext, y en el thread pool, para no deadlockear el UI thread ni disparar
            // PropertyChanged desde un hilo no-UI con el binding ya activo. Si la API está
            // caída, InicializarAsync cae al login (no lanza — ver ShellViewModel).
            Task.Run(() => shell.InicializarAsync()).GetAwaiter().GetResult();

            // Defensivo: por defecto ShutdownMode es OnLastWindowClose, lo que puede apagar
            // la app si transitoriamente queda sin ventanas visibles. Fijamos explícitamente
            // que el ciclo de vida dependa solo del cierre de MainWindow.
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

        // Configuración externa: appsettings.json es opcional (optional: true) — si falta,
        // Api:BaseUrl cae al default http://localhost:5000 y el updater a sus defaults.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        // ── Fase 3b: sesión API + HttpClient (reemplazan a AppDbContext/repos/servicios) ──

        // ApiSession: singleton — la sesión (snapshot + token JWT) es única en toda la app.
        // Se registra también como ICurrentSession apuntando a la MISMA instancia.
        services.AddSingleton<ApiSession>();
        services.AddSingleton<ICurrentSession>(sp => sp.GetRequiredService<ApiSession>());

        // HttpClient: singleton (correcto para desktop: reusa conexiones, un solo pool).
        // AuthTokenHandler adjunta el Bearer y detecta la sesión vencida en un solo lugar.
        services.AddSingleton(sp =>
        {
            var baseUrl = configuration["Api:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5000"; // default del spec 3b
            }

            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };

            return new HttpClient(handler)
            {
                // BaseAddress DEBE terminar en "/" para que los paths relativos ("auth/login")
                // se resuelvan contra la base y no la pisen.
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                // 10 s (spec 3b, OQ-3): LAN local — cubre el reporte más pesado y acota la
                // espera con el server caído (el default de 100 s colgaría la UI).
                Timeout = TimeSpan.FromSeconds(10),
            };
        });

        // ── Fase 3b: ApiClients — implementan las MISMAS interfaces de Application que
        //    consumen los ViewModels; los ~22 VMs no se tocan ─────────────────────────────
        services.AddTransient<IAuthService, AuthApiClient>();
        services.AddTransient<IPrimerArranqueService, PrimerArranqueApiClient>();
        services.AddTransient<IUsuarioService, UsuarioApiClient>();
        services.AddTransient<IProductoService, ProductoApiClient>();
        services.AddTransient<ICategoriaService, CategoriaApiClient>();
        services.AddTransient<IProveedorService, ProveedorApiClient>();
        services.AddTransient<IUnidadMedidaService, UnidadMedidaApiClient>();
        services.AddTransient<IMovimientoStockService, MovimientoStockApiClient>();
        services.AddTransient<IReporteStockService, ReporteStockApiClient>();
        services.AddTransient<IAuditoriaQueryService, AuditoriaQueryApiClient>();

        // NOTA (spec 3b): NO se registran IAuthorizationService ni IPasswordHasher ni
        // IAuditLogger ni repositorios — la autorización, el hashing y la auditoría son
        // responsabilidad del servidor. Ninguna UI los consumía directo (verificado, OQ-1).

        // ── Inc 5: confirmación de stock insuficiente ─────────────────────────
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();

        // ── Marshaling al UI thread para asignaciones desde background (ej: overlay
        // de actualización en ShellViewModel) ─────────────────────────────────────
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // ── Info de la app (versión mostrada en login y shell) ────────────────
        services.AddSingleton<IInfoApp, InfoApp>();

        // ── Inc 6: exportación CSV (vive en Application, sin dependencias de Infra — OQ-2)
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

        // ── Inc 7 Fase A: actualizador in-app (mudado a Presentation en Fase 3b) ──

        // UpdaterOptions: configura fuentes. GitHub es primaria (real); feed propio es
        // fallback opcional. La URL y el flag de prerelease vienen de appsettings.json
        // (sección "Updater"); si la key falta o el archivo no existe, se cae al fallback
        // defensivo de UpdaterOptions.
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
        services.AddSingleton<CoordinadorActualizacion>();

        return services.BuildServiceProvider();
    }
}
