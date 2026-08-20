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
using StockApp.Configuracion;
using StockApp.Application.Actualizaciones;
using StockApp.Application.Alertas;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Application.Logs;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Application.Tareas;
using StockApp.Presentation.Actualizaciones;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Documentos;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using StockApp.Presentation.ViewModels.Tareas;
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

        // Bridge composition root -> helper estático (fix 2026-08-20): RefrescoPermisos es un
        // helper estático (lo consumen varios ViewModels vía DispararBestEffortAsync, no un
        // servicio DI en sí), así que su IRegistroFallos se configura acá, una única vez, en
        // vez de vía constructor. Antes de este fix llamaba directo a Program.LogFatal y
        // ensuciaba el crash.log real en cada corrida de `dotnet test` — ver TestBootstrap en
        // StockApp.Presentation.Tests / .UiTests para el equivalente en los proyectos de test.
        RefrescoPermisos.ConfigurarRegistroFallos(_serviceProvider.GetRequiredService<IRegistroFallos>());

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

            // Licencia desactivada (Inc 7 Fase B): cualquier request que reciba un 423
            // (ej. borraron licencia.lic con la app abierta) dispara este evento; se marshalea
            // al UI thread y se muestra la pantalla de bloqueo. Idempotente por diseño: el
            // Shell simplemente reasigna CurrentViewModel, no importa si el evento se dispara
            // varias veces (varios requests concurrentes con 423).
            apiSession.LicenciaDesactivada += () => uiDispatcher.Post(
                () => shell.MostrarBloqueoLicencia());

            // Acceso revocado (spec 2026-08-10): un 403 con sesión válida significa que el
            // servidor rechazó una operación por falta de permiso. A diferencia de
            // SesionVencida, NO se cierra sesión ni se navega. Se refresca el cache de permisos
            // para que el menú deje de mostrar ítems ya revocados, y se avisa. Best-effort: si
            // el refresco falla, el aviso igual se muestra.
            //
            // Bug 2026-08-15 (corrección sobre el fix de Round 1): un 403 NO siempre significa
            // que el Admin revocó algo en caliente -- puede ser que el usuario NUNCA tuvo ese
            // permiso (ej. Operador con permisos mínimos que toca una sección para la que nunca
            // tuvo acceso). Afirmar "tus permisos cambiaron" en ese caso es falso y manda al
            // usuario a buscar un cambio que no ocurrió. AvisoAccesoRevocado.Resolver compara el
            // snapshot de permisos ANTES del refresco contra el de DESPUÉS -- solo si difieren
            // se usa el mensaje de "cambiaron"; si no, un mensaje genérico pero siempre
            // verdadero (el 403 ya probó que no tiene acceso). Por eso el refresco ahora corre
            // ANTES de mostrar el aviso (antes se mostraba primero): el contenido del mensaje
            // depende de su resultado.
            apiSession.AccesoRevocado += () => uiDispatcher.Post(async () =>
            {
                var authService = _serviceProvider!.GetRequiredService<IAuthService>();

                var permisosAntes = apiSession.PermisosActuales;
                await RefrescoPermisos.DispararBestEffortAsync(
                    () => authService.ObtenerPermisosPropiosAsync(), "AccesoRevocado");
                var mensaje = AvisoAccesoRevocado.Resolver(permisosAntes, apiSession.PermisosActuales);

                var confirmacion = _serviceProvider!.GetRequiredService<IConfirmacionService>();
                await confirmacion.InformarAsync(mensaje);
            });

            // Inicializa el shell (decide login / primer arranque) ANTES de asignar el
            // DataContext, y en el thread pool, para no deadlockear el UI thread ni disparar
            // PropertyChanged desde un hilo no-UI con el binding ya activo. Si la API está
            // caída, InicializarAsync cae al login (no lanza — ver ShellViewModel).
            Task.Run(() => shell.InicializarAsync()).GetAwaiter().GetResult();

            // Defensivo: por defecto ShutdownMode es OnLastWindowClose, lo que puede apagar
            // la app si transitoriamente queda sin ventanas visibles. Fijamos explícitamente
            // que el ciclo de vida dependa solo del cierre de MainWindow.
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;

            var mainWindow = new MainWindow(_serviceProvider.GetRequiredService<IServicioEstadoVentana>())
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

        var configuration = ConstruirConfiguracion();

        // ── Fase 3b: sesión API + HttpClient (reemplazan a AppDbContext/repos/servicios) ──

        // ApiSession: singleton — la sesión (snapshot + token JWT) es única en toda la app.
        // Se registra también como ICurrentSession apuntando a la MISMA instancia.
        services.AddSingleton<ApiSession>();
        services.AddSingleton<ICurrentSession>(sp => sp.GetRequiredService<ApiSession>());

        // HttpClient: singleton (correcto para desktop: reusa conexiones, un solo pool).
        // AuthTokenHandler adjunta el Bearer y detecta la sesión vencida en un solo lugar.
        services.AddSingleton(sp =>
        {
            var baseUrl = ResolverApiBaseUrl(configuration);

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

        // HttpClient "Descargas": mismo BaseAddress/AuthTokenHandler que el principal, pero con
        // timeout de 30 MINUTOS (no 10s como el principal, y NO infinito — ver decisión de
        // diseño 2 del Task). En una LAN (despliegue real de este sistema, mismo criterio que el
        // timeout de 10s del HttpClient principal) hasta un dump de varios GB baja en minutos;
        // 30 minutos es margen de sobra para VPN/disco lento del servidor, pero sigue siendo un
        // límite finito: si el servidor cuelga a mitad de una descarga, la UI se libera sola aun
        // si el usuario nunca toca "Cancelar" (Task 9).
        services.AddKeyedSingleton<HttpClient>("Descargas", (sp, _) =>
        {
            var baseUrl = ResolverApiBaseUrl(configuration);

            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(30),
            };
        });

        // ── Fase 3b: ApiClients — implementan las MISMAS interfaces de Application que
        //    consumen los ViewModels; los ~22 VMs no se tocan ─────────────────────────────
        services.AddTransient<IAuthService, AuthApiClient>();
        services.AddTransient<IUsuarioService, UsuarioApiClient>();
        services.AddTransient<IProductoService, ProductoApiClient>();
        services.AddTransient<ICategoriaService, CategoriaApiClient>();
        services.AddTransient<IProveedorService, ProveedorApiClient>();
        services.AddTransient<IUnidadMedidaService, UnidadMedidaApiClient>();
        services.AddTransient<IMovimientoStockService, MovimientoStockApiClient>();
        services.AddTransient<IReporteStockService, ReporteStockApiClient>();
        services.AddTransient<IAuditoriaQueryService, AuditoriaQueryApiClient>();

        // ── Módulo Finanzas — Fase 1: maestros ────────────────────────────────
        services.AddTransient<IFuenteFinanciamientoService, FuenteFinanciamientoApiClient>();
        services.AddTransient<IRubroGastoService, RubroGastoApiClient>();
        services.AddTransient<ILineaPoaService, LineaPoaApiClient>();

        // ── Módulo Finanzas — Fase 2: gastos e ingresos de caja ───────────────
        services.AddTransient<IGastoService, GastoApiClient>();
        services.AddTransient<IIngresoCajaService, IngresoCajaApiClient>();
        services.AddTransient<IFinanzasVistasService, FinanzasVistasApiClient>();
        services.AddTransient<IAdjuntoService, AdjuntoApiClient>();
        services.AddTransient<IIngresoPorFacturaService, IngresoPorFacturaApiClient>();

        // ── Módulo Tareas (independiente de Finanzas, spec 2026-08-01) ────────
        services.AddTransient<ITareaService, TareaApiClient>();

        // ── Módulo Documentos administrativos (spec 2026-08-11) ────────────────
        services.AddTransient<IDocumentoAdministrativoService, DocumentoApiClient>();
        services.AddTransient<IAdjuntoDocumentoService, AdjuntoDocumentoApiClient>();

        // ── Backups programados (Entrega 1) ────────────────────────────────────
        services.AddTransient<IBackupsService>(sp =>
            new BackupsApiClient(sp.GetRequiredKeyedService<HttpClient>("Descargas")));

        // ── Diagnóstico/logs (Entrega 2): mismo HttpClient keyed "Descargas" (timeout de 30
        //    minutos) que IBackupsService — el ZIP de logs es una descarga, no una llamada de
        //    API común, y el HttpClient por defecto tiene timeout de 10s (colgaría con archivos
        //    grandes) ──────────────────────────────────────────────────────────
        services.AddTransient<ILogsService>(sp =>
            new LogsApiClient(sp.GetRequiredKeyedService<HttpClient>("Descargas")));

        // ── Alerta de backup (webhook) ── Bodies chicos, sin descarga de archivos: usa el
        //    HttpClient PRINCIPAL (timeout 10s), no el keyed "Descargas" de arriba.
        services.AddTransient<IConfiguracionAlertasService>(sp =>
            new ConfiguracionAlertasApiClient(sp.GetRequiredService<HttpClient>()));

        // ── Módulo Finanzas — F5d: importador de planillas (historial + análisis/confirmación/reversa) ──
        services.AddTransient<IImportacionService, ImportacionApiClient>();

        // ── Inc 7 Fase B: licenciamiento (pantalla de bloqueo + reset de Admin) ──
        services.AddTransient<ILicenciaService>(sp => new LicenciaApiClient(sp.GetRequiredService<HttpClient>()));
        services.AddTransient<IResetAdminService>(sp => new ResetAdminApiClient(sp.GetRequiredService<HttpClient>()));

        // NOTA (spec 3b): NO se registran IPasswordHasher ni IAuditLogger ni repositorios —
        // el hashing y la auditoría son responsabilidad exclusiva del servidor.
        //
        // IAuthorizationService SÍ se registra (excepción a la nota original de 3b): es
        // lógica pura (tabla de acciones por rol, sin infraestructura) y se usa acá SOLO
        // para gating de UI (ej. habilitar/deshabilitar botones según permiso). El servidor
        // sigue siendo la única fuente de verdad de autorización — cada ApiClient reintenta
        // la operación contra la API, que valida con su propia instancia del mismo servicio.
        services.AddTransient<IAuthorizationService, AuthorizationService>();

        // ── Inc 5: confirmación de stock insuficiente ─────────────────────────
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();

        // ── Marshaling al UI thread para asignaciones desde background (ej: overlay
        // de actualización en ShellViewModel) ─────────────────────────────────────
        services.AddSingleton<IUiDispatcher, AvaloniaUiDispatcher>();

        // ── Info de la app (versión mostrada en login y shell) ────────────────
        services.AddSingleton<IInfoApp, InfoApp>();

        // ── Registro de fallos best-effort (fix 2026-08-20): singleton porque
        // RegistroFallosArchivo no tiene estado propio más allá de la ruta fija de crash.log,
        // igual criterio que IServicioEstadoVentana más abajo. Ver el bridge hacia
        // RefrescoPermisos en OnFrameworkInitializationCompleted.
        services.AddSingleton<IRegistroFallos, RegistroFallosArchivo>();

        // ── Inc 6: exportación CSV (vive en Application, sin dependencias de Infra — OQ-2)
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── Inc 6: guardado de archivos (file picker) ─────────────────────────
        // Singleton — sin estado, accede a la ventana principal vía IStorageProvider.
        services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();
        services.AddSingleton<IServicioSeleccionArchivo, ServicioSeleccionArchivo>();
        services.AddSingleton<IServicioAperturaArchivo, ServicioAperturaArchivo>();

        // ── Persistencia de estado de ventana (tamaño/posición/maximizada) ────
        // Singleton — preferencia LOCAL por PC (JSON en ApplicationData), no por usuario
        // logueado ni en BD/API. Sin estado interno propio más allá de la ruta del archivo.
        services.AddSingleton<IServicioEstadoVentana, ServicioEstadoVentana>();

        // ── Persistencia de preferencias del sidebar (grupos abiertos) ────────
        // Singleton — preferencia LOCAL por PC, archivo propio sidebar.json (ciclo de vida
        // distinto al de ventana.json: se guarda en cada click, no solo al cerrar la app).
        services.AddSingleton<IServicioPreferenciasSidebar, ServicioPreferenciasSidebar>();

        // ── Inc 5: VMs de movimientos ─────────────────────────────────────────
        services.AddTransient<EntradaRegistroViewModel>();
        services.AddTransient<SalidaRegistroViewModel>();
        services.AddTransient<MovimientoHistorialViewModel>();
        services.AddTransient<IngresoPorFacturaViewModel>();

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
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.MantenimientoViewModel>();
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.UsuariosAdminViewModel>();
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.PanelPermisosViewModel>();

        // Factory de MantenimientoViewModel para ShellViewModel (FIX 1, re-review final E1):
        // el modo acceso limitado (licencia vencida) resuelve una instancia fresca desde acá
        // en vez de recibir IServiceProvider completo (evita el anti-patrón Service Locator
        // en ShellViewModel — mismo criterio que el Func<Type, object> de NavigationService).
        services.AddTransient<Func<StockApp.Presentation.ViewModels.Administracion.MantenimientoViewModel>>(
            sp => sp.GetRequiredService<StockApp.Presentation.ViewModels.Administracion.MantenimientoViewModel>);
        services.AddTransient<ProductoListViewModel>();
        services.AddTransient<ProductoFormViewModel>();
        services.AddTransient<CategoriaListViewModel>();
        services.AddTransient<CategoriaFormViewModel>();
        services.AddTransient<ProveedorListViewModel>();
        services.AddTransient<ProveedorFormViewModel>();
        services.AddTransient<UnidadMedidaListViewModel>();
        services.AddTransient<UnidadMedidaFormViewModel>();

        // ── Módulo Finanzas — Fase 1: VMs de maestros ─────────────────────────
        services.AddTransient<MaestrosFinanzasViewModel>();
        services.AddTransient<FuenteFinanciamientoListViewModel>();
        services.AddTransient<FuenteFinanciamientoFormViewModel>();
        services.AddTransient<RubroGastoListViewModel>();
        services.AddTransient<RubroGastoFormViewModel>();
        services.AddTransient<LineaPoaListViewModel>();
        services.AddTransient<LineaPoaFormViewModel>();

        // ── Módulo Finanzas — Fase 2: VMs de gastos e ingresos ────────────────
        services.AddTransient<GastosViewModel>();
        services.AddTransient<GastoFormViewModel>();
        services.AddTransient<PagosGastoViewModel>();
        services.AddTransient<IngresosViewModel>();
        services.AddTransient<IngresoFormViewModel>();
        services.AddTransient<LibroCajaViewModel>();
        services.AddTransient<ControlPoaViewModel>();
        services.AddTransient<AdjuntosPanelViewModel>();
        services.AddTransient<CalendarioPagosViewModel>();

        // ── Módulo Finanzas — F5d: importador de planillas ────────────────────
        services.AddTransient<HistorialImportacionesViewModel>();
        services.AddTransient<NuevaImportacionViewModel>();
        services.AddTransient<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>();

        // ── Módulo Tareas (spec 2026-08-01) ───────────────────────────────────
        services.AddTransient<TareaListViewModel>();
        services.AddTransient<TareaFormViewModel>();

        // ── Módulo Documentos administrativos (spec 2026-08-11) ────────────────
        services.AddTransient<AdjuntosDocumentoPanelViewModel>();
        services.AddTransient<DocumentoListViewModel>();
        services.AddTransient<DocumentoFormViewModel>();

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

    /// <summary>
    /// Precedencia de resolución de configuración (2026-08-20, configurador de conexión):
    /// 1. %AppData%\GestionMunicipal\conexion.json — lo que escribe tools/StockApp.Configurador.
    /// 2. appsettings.json del directorio de instalación — valor de fábrica.
    /// 3. ConexionDefaults.UrlPorDefecto — único fallback hardcodeado (ver ResolverApiBaseUrl).
    ///
    /// Los providers de Microsoft.Extensions.Configuration.Json se aplican en orden: el que se
    /// agrega DESPUÉS gana. Por eso conexion.json se agrega después de appsettings.json. Ambos
    /// son optional: true — si faltan, ResolverApiBaseUrl cae al único default.
    ///
    /// Los parámetros de override existen solo para poder testear la precedencia sin depender
    /// de AppContext.BaseDirectory ni de %AppData% reales (ver
    /// StockApp.Presentation.Tests.Config.ResolucionApiBaseUrlTests); en producción se llaman
    /// sin argumentos.
    /// </summary>
    internal static IConfiguration ConstruirConfiguracion(
        string? rutaAppsettingsOverride = null,
        string? rutaConexionOverride = null)
    {
        var builder = new ConfigurationBuilder();

        if (rutaAppsettingsOverride is null)
        {
            builder.SetBasePath(AppContext.BaseDirectory)
                   .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        }
        else
        {
            builder.AddJsonFile(rutaAppsettingsOverride, optional: true, reloadOnChange: false);
        }

        var rutaConexion = rutaConexionOverride ?? RutaConexion.ObtenerRutaArchivo();
        builder.AddJsonFile(rutaConexion, optional: true, reloadOnChange: false);

        return builder.Build();
    }

    /// <summary>
    /// ÚNICO lugar que resuelve Api:BaseUrl. Antes de este fix el mismo fallback
    /// "http://localhost:5000" estaba hardcodeado DOS veces (HttpClient principal y
    /// "Descargas"), desincronizado del default real de appsettings.json (5043) — si faltaba
    /// el appsettings.json la app caía a un puerto donde no escuchaba nadie.
    /// </summary>
    internal static string ResolverApiBaseUrl(IConfiguration configuration)
    {
        var baseUrl = configuration[ConexionDefaults.ClaveApiBaseUrl];
        return string.IsNullOrWhiteSpace(baseUrl) ? ConexionDefaults.UrlPorDefecto : baseUrl;
    }
}
