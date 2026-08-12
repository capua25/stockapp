using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using StockApp.Api.Auth;
using StockApp.Api.Backups;
using StockApp.Api.Endpoints;
using StockApp.Api.ErrorHandling;
using StockApp.Api.Json;
using StockApp.Api.Licenciamiento;
using StockApp.Api.Logging;
using StockApp.Application.Alertas;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Application.Catalogo;
using StockApp.Application.Documentos;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Application.Logs;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Application.Tareas;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Backups;
using StockApp.Infrastructure.Finanzas;
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Infrastructure.Notificaciones;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Reportes;
using StockApp.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ── Logging a archivo (Entrega 2) ──────────────────────────────────────
// Un problema de logging no puede dejar al municipio sin sistema: si el directorio
// no se puede crear, la API arranca igual y solo pierde el sink de archivo.
const string PlantillaLog = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

var directorioLogs = DirectorioLogsResolver.Resolver(builder.Configuration, new UserDataPathProvider());

// La consola también se sanea: si el proceso corre como servicio (systemd, journald,
// Docker), stdout queda capturado y persistido igual que el archivo — sin esto, esa es
// una segunda vía de filtración de credenciales que las tasks 2-4 justamente vienen a
// cerrar. Se pierde el coloreado por nivel de Serilog al usar un formatter propio; es
// aceptable.
var configuracionLog = new LoggerConfiguration()
    .MinimumLevel.Warning()
    .WriteTo.Console(new FormateadorSaneado(new MessageTemplateTextFormatter(
        PlantillaLog, CultureInfo.InvariantCulture)));

try
{
    Directory.CreateDirectory(directorioLogs);

    // Directory.CreateDirectory no lanza si el directorio YA existe, sin importar si el
    // proceso puede leerlo o escribirlo -- alcanza con que exista para que no haga nada. Sin
    // esta prueba explícita, un directorio que existe pero perdió permisos (chmod manual,
    // política nueva del municipio, etc.) deja a la API arrancando en silencio, sin sink de
    // archivo y sin ningún aviso por consola. El archivo de prueba se escribe y se borra acá
    // mismo, antes de comprometernos con Serilog.WriteTo.File más abajo.
    var archivoDePrueba = Path.Combine(directorioLogs, $".stockapp-prueba-{Guid.NewGuid():N}");
    File.WriteAllText(archivoDePrueba, string.Empty);
    File.Delete(archivoDePrueba);

    configuracionLog = configuracionLog.WriteTo.File(
        formatter: new FormateadorSaneado(new MessageTemplateTextFormatter(
            PlantillaLog, CultureInfo.InvariantCulture)),
        path: Path.Combine(directorioLogs, "stockapp-.log"),
        rollingInterval: RollingInterval.Day,
        // Sin esto, el default de Serilog es 1 GB con rollOnFileSizeLimit=false: al
        // llegar ahi el archivo del dia deja de recibir eventos EN SILENCIO por el resto
        // del dia -- justo cuando una tormenta de errores es cuando mas se necesitan los
        // logs. 50 MB + rollOnFileSizeLimit=true hace que rote a un archivo nuevo (con
        // sufijo de secuencia dentro del mismo dia) en vez de callarse.
        fileSizeLimitBytes: 50 * 1024 * 1024,
        rollOnFileSizeLimit: true,
        retainedFileTimeLimit: TimeSpan.FromDays(30),
        restrictedToMinimumLevel: LogEventLevel.Warning,
        shared: true);
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"[StockApp] No se pudo inicializar el log de archivo en '{directorioLogs}': {ex.Message}. "
        + "La API arranca igual, pero no va a haber logs descargables.");
}

Log.Logger = configuracionLog.CreateLogger();
builder.Logging.ClearProviders();
builder.Host.UseSerilog();

// Segunda barrera de defensa en profundidad para BackupProgramadoService (Entrega 1, Task 5):
// el default de HostOptions.BackgroundServiceExceptionBehavior es StopHost, es decir que
// cualquier excepción no atrapada en UN BackgroundService tumba TODO el host, endpoints HTTP
// incluidos. BackupProgramadoService ya atrapa todo lo que puede (arranque + cada corrida), pero
// si algo se escapa igual, Ignore hace que solo muera el servicio de backup y la API siga
// sirviendo. Un municipio sin backups automáticos es un problema; un municipio sin sistema
// (porque un pg_dump falló mal) es una emergencia.
builder.Services.Configure<HostOptions>(options =>
{
    options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// AppDbContext: Scoped por request (patrón natural de ASP.NET Core). La app desktop
// sigue con AppDbContext Transient en su propia composición root — no se unifican.
//
// IMPORTANTE: la connection string se lee de forma DIFERIDA (dentro del callback
// (sp, options) => ..., resuelto post-Build) en vez de leerse eager en una `var`
// top-level. WebApplicationFactory (tests de integración) inyecta su override de
// configuración (Testcontainers) recién cuando el host termina de construirse — una
// lectura eager de builder.Configuration ANTES de Build() nunca ve ese override y cae
// silenciosamente al fallback de appsettings.json. Ver nota al pie del plan de Fase 2a.
builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
            "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");
    options.UseNpgsql(connectionString);
});

// ICurrentSession: scoped, armada desde los claims del JWT del request. Reemplaza a
// InMemorySession SOLO acá — la app desktop sigue con InMemorySession sin cambios.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentSession, HttpCurrentSession>();

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddSingleton<StockApp.Application.Authorization.IAuthorizationService, AuthorizationService>();

// DomainExceptionHandler: mapeo centralizado de excepciones de dominio/aplicación a
// status HTTP (Fase 2b, sección "Manejo de errores" del spec). Los endpoints de
// Bloque C no hacen try/catch — cualquier excepción no capturada llega acá.
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// ProblemDetails: shape uniforme para 400/401/403/500. Los 401/403 se escriben
// explícitamente en los eventos de JwtBearerOptions (abajo) en vez de depender de la
// conversión automática de status codes de AddProblemDetails() — así el shape no
// depende de comportamiento implícito del framework. UseExceptionHandler() (más abajo,
// post-Build) cubre el caso de excepción no manejada (500) con el mismo servicio.
builder.Services.AddProblemDetails();

// JSON: normaliza DateTime Unspecified (fecha pelada sin zona horaria, ej. "2026-01-15")
// a UTC medianoche al deserializar el body de un request — ver DateTimeUnspecifiedAsUtcConverter
// para el detalle del bug (Npgsql rechaza escribir Unspecified en columnas timestamptz).
// Minimal APIs con binding de parámetro complejo (Results.Ok(request)) usan las opciones de
// ConfigureHttpJsonOptions, no las de AddControllers/AddJsonOptions (esta API no usa MVC).
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new DateTimeUnspecifiedAsUtcConverter());
});

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IAuditLogger, AuditService>();

// Catálogo (slice: GET /productos)
builder.Services.AddScoped<IProductoRepository, ProductoRepository>();
builder.Services.AddScoped<IUnidadMedidaRepository, UnidadMedidaRepository>();
builder.Services.AddScoped<IProductoService, ProductoService>();

// Reportes (slice: GET /reportes/*)
// IVersionReportes: singleton (contador monotónico en memoria, compartido por todo el proceso).
// IMemoryCache + ReporteStockServiceCacheado (Task 4): decorator que cachea las 4 lecturas de
// reporte y se invalida cuando ProductoService/CategoriaService/MovimientoStockService llaman
// IVersionReportes.Invalidar() tras un commit exitoso. La auditoría (GET /auditoria) NO pasa
// por este decorator — no la cachea.
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IVersionReportes, VersionReportes>();
builder.Services.AddScoped<IMovimientoStockRepository, MovimientoStockRepository>();
builder.Services.AddScoped<IMovimientoStockService, MovimientoStockService>();
builder.Services.AddScoped<IReporteStockRepository, ReporteStockRepository>();
builder.Services.AddScoped<ReporteStockService>();
builder.Services.AddScoped<IReporteStockService>(sp =>
    new ReporteStockServiceCacheado(
        sp.GetRequiredService<ReporteStockService>(),
        sp.GetRequiredService<IMemoryCache>(),
        sp.GetRequiredService<IVersionReportes>()));

// Catálogo — tablas maestras (Fase 2b)
builder.Services.AddScoped<ICategoriaRepository, CategoriaRepository>();
builder.Services.AddScoped<ICategoriaService, CategoriaService>();
builder.Services.AddScoped<IProveedorRepository, ProveedorRepository>();
builder.Services.AddScoped<IProveedorService, ProveedorService>();
builder.Services.AddScoped<IUnidadMedidaService, UnidadMedidaService>();
// IUnidadMedidaRepository ya está registrado desde Fase 2a (usado por ProductosEndpoints).

// Finanzas — Fase 1: maestros (fuentes, rubros, líneas POA + asignaciones)
builder.Services.AddScoped<IFuenteFinanciamientoRepository, FuenteFinanciamientoRepository>();
builder.Services.AddScoped<IFuenteFinanciamientoService, FuenteFinanciamientoService>();
builder.Services.AddScoped<IRubroGastoRepository, RubroGastoRepository>();
builder.Services.AddScoped<IRubroGastoService, RubroGastoService>();
builder.Services.AddScoped<ILineaPoaRepository, LineaPoaRepository>();
builder.Services.AddScoped<ILineaPoaService, LineaPoaService>();

// Finanzas — Fase 2: gastos, pagos e ingresos de caja
builder.Services.AddScoped<IGastoRepository, GastoRepository>();
builder.Services.AddScoped<IGastoService, GastoService>();
builder.Services.AddScoped<IIngresoCajaRepository, IngresoCajaRepository>();
builder.Services.AddScoped<IIngresoCajaService, IngresoCajaService>();
builder.Services.AddScoped<IIngresoPorFacturaService, IngresoPorFacturaService>();

// Finanzas — Fase 3: adjuntos de gastos/pagos
builder.Services.AddScoped<IAdjuntoRepository, AdjuntoRepository>();
builder.Services.AddScoped<IAdjuntoService, AdjuntoService>();

// Finanzas — Fase 4: vistas calculadas (libro caja, control POA, calendario de pagos)
builder.Services.AddScoped<IFinanzasVistasService, FinanzasVistasService>();

// Finanzas — F5b: análisis (read-only) del importador de planillas .ods. IPlanillaParser
// (interfaz de Application, F5a) se registra acá por primera vez, detrás de la impl de
// Infrastructure PlanillaOdsParser.
builder.Services.AddScoped<IPlanillaParser, PlanillaOdsParser>();
builder.Services.AddScoped<IAnalisisImportacionService, AnalisisImportacionService>();

// Tareas — módulo independiente (spec 2026-08-01)
builder.Services.AddScoped<ITareaRepository, TareaRepository>();
builder.Services.AddScoped<ITareaService, TareaService>();

// Documentos administrativos — módulo independiente (spec 2026-08-11)
builder.Services.AddScoped<IDocumentoAdministrativoRepository, DocumentoAdministrativoRepository>();
builder.Services.AddScoped<IDocumentoAdministrativoService, DocumentoAdministrativoService>();
builder.Services.AddScoped<IAdjuntoDocumentoRepository, AdjuntoDocumentoRepository>();
builder.Services.AddScoped<IAdjuntoDocumentoService, AdjuntoDocumentoService>();

// Finanzas — F5c: confirmación transaccional del importador (escritura + idempotencia +
// guard de re-importación + reversa). IImportacionRepository es la única pieza de todo el
// flujo de importación que toca EF/Npgsql directamente.
builder.Services.AddScoped<IImportacionRepository, ImportacionRepository>();
builder.Services.AddScoped<IConfirmacionImportacionService, ConfirmacionImportacionService>();

// Auditoría (Fase 2b)
builder.Services.AddScoped<IAuditoriaQueryRepository, AuditoriaQueryRepository>();
builder.Services.AddScoped<IAuditoriaQueryService, AuditoriaQueryService>();

// Usuarios — ABM completo vía API (Fase 2b). IUsuarioRepository y IPasswordHasher
// ya están registrados desde Fase 2a (usados por AuthEndpoints).
builder.Services.AddScoped<IUsuarioService, UsuarioService>();

// Permisos por operador (spec 2026-08-10): IProveedorPermisos es Singleton (cache de proceso);
// IPermisoUsuarioRepository es Scoped (usa AppDbContext) — el proveedor resuelve el lifetime
// mismatch con su propio IServiceScopeFactory, no inyectando el repo directo.
builder.Services.AddScoped<IPermisoUsuarioRepository, PermisoUsuarioRepository>();
builder.Services.AddSingleton<IProveedorPermisos, ProveedorPermisosEnMemoria>();

// Bootstrap de primer arranque (Fase 3a, D7) — reusa IUsuarioRepository/IPasswordHasher.
builder.Services.AddScoped<IPrimerArranqueService, PrimerArranqueService>();

// Licenciamiento (Inc 7 Fase B). La clave pública se lee de config (Licencia:ClavePublicaBase64)
// con la constante embebida como fallback. EstadoLicencia/fingerprint/almacén/validador son
// SINGLETON (estables por proceso); ServicioLicencia es SCOPED. IUserDataPathProvider lo usa
// AlmacenLicenciaArchivo para persistir licencia.lic en el directorio de datos del server.
builder.Services.AddSingleton<IUserDataPathProvider, UserDataPathProvider>();
builder.Services.AddSingleton<IFingerprintMaquina>(_ => FingerprintMaquinaFactory.Crear());
builder.Services.AddSingleton<IAlmacenLicencia, AlmacenLicenciaArchivo>();
builder.Services.AddSingleton<EstadoLicencia>();
builder.Services.AddSingleton(sp =>
{
    var clavePublica = sp.GetRequiredService<IConfiguration>()["Licencia:ClavePublicaBase64"]
        ?? OpcionesLicencia.ClavePublicaBase64Default;
    return new ValidadorFirma(clavePublica);
});
builder.Services.AddScoped<ServicioLicencia>();
builder.Services.AddSingleton<IAlmacenDesafiosReset, AlmacenDesafiosResetEnMemoria>();
builder.Services.AddScoped<ServicioResetAdmin>();

// Backups programados (Entrega 1): primer BackgroundService del repo. IUserDataPathProvider ya
// está registrado Singleton más arriba (Licenciamiento). ICorridaBackupRepository/ServicioBackup
// Scoped porque usan AppDbContext; BackupProgramadoService crea su propio scope por corrida
// (ver Backups/BackupProgramadoService.cs) así que no importa que él mismo sea Singleton.
builder.Services.AddScoped<ICorridaBackupRepository, CorridaBackupRepository>();
builder.Services.AddScoped<IEjecutorPgDump, EjecutorPgDumpProceso>();
builder.Services.AddScoped<ServicioBackup>();
builder.Services.AddScoped<ServicioConsultaBackups>();

// ── Canal de alerta de backups ─────────────────────────────────────────────
// Primer AddHttpClient del repo. Timeout corto a propósito: notificar es best-effort y no
// puede quedar colgado bloqueando el hilo de una corrida de backup.
builder.Services.AddHttpClient<INotificadorAlertas, NotificadorWebhook>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IConfiguracionAlertasRepository, ConfiguracionAlertasRepository>();
builder.Services.AddScoped<ServicioConfiguracionAlertas>();

// fix/integridad-referencial (POST /backups, disparo manual): IGuardiaCorridaBackup es
// Singleton a propósito -- BackupProgramadoService (job automático) y DisparadorBackupManual
// (POST /backups) corren en scopes distintos (uno por tick del timer, otro por request HTTP) y
// necesitan compartir el MISMO gate para que nunca corran dos pg_dump al mismo tiempo.
builder.Services.AddSingleton<IGuardiaCorridaBackup, GuardiaCorridaBackup>();
builder.Services.AddSingleton<DisparadorBackupManual>();
builder.Services.AddHostedService<BackupProgramadoService>();

// Diagnóstico/logs (Entrega 2, Task 7): ServicioConsultaLogs es stateless (recibe la ruta
// por parámetro, mismo patrón que ServicioConsultaBackups), pero se registra Scoped por
// consistencia con el resto de los servicios de request.
builder.Services.AddScoped<ServicioConsultaLogs>();

// JwtOptions: misma razón que AppDbContext arriba — el secreto (y ahora la expiración,
// Fase 3a D10) se leen de forma diferida en el factory (resuelto post-Build), no en una
// `var` top-level. JwtOptions es un record posicional sin constructor sin parámetros, así
// que no es compatible con el patrón AddOptions<T>().Bind(...).ValidateOnStart() estándar
// (ese patrón requiere poder instanciar T con Activator.CreateInstance<T>() y mutar
// propiedades por reflexión). Se preserva el fail-fast con mensaje amigable forzando la
// resolución del singleton apenas arranca el host (justo después de builder.Build(), abajo).
// La construcción en sí vive en JwtOptionsFactory.Crear (testeable sin host completo).
builder.Services.AddSingleton(sp => JwtOptionsFactory.Crear(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

// IRevocadorTokens: SINGLETON en memoria (Fase B hardening). Guarda por usuario el
// mínimo iat aceptado; se pierde al reiniciar la API (LAN, expiración de JWT corta —
// ver comentario de la limitación aceptada en RevocadorTokensEnMemoria).
builder.Services.AddSingleton<IRevocadorTokens, RevocadorTokensEnMemoria>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// Config diferida de JwtBearerOptions: AddOptions<T>(name).Configure<TDep>(...) resuelve
// JwtOptions (el mismo singleton factory de arriba) recién cuando el pipeline de
// autenticación crea las opciones por primera vez (post-Build, ya con la config final
// —incluidos los overrides de test de ApiFactory— aplicada), no en una `var` top-level.
// AddJwtBearer(Action<JwtBearerOptions>) no tiene overload con IServiceProvider, por eso
// se usa este mecanismo en vez de leer jwtSecret directamente arriba. Ver nota al pie
// del plan de Fase 2a ("patrón de config eager roto por WebApplicationFactory").
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<JwtOptions>((options, jwtOptions) =>
    {
        // No remapear los nombres de claim cortos (usuarioId/rol) a URIs largas de
        // ClaimTypes — HttpCurrentSession los lee tal cual los escribió JwtTokenService.
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
        };

        // Eventos explícitos para 401/403 en vez de dejar que ASP.NET Core devuelva un
        // body vacío sin Content-Type: el shape de ProblemDetails tiene que ser
        // determinístico, no un efecto colateral de AddProblemDetails() + status code.
        options.Events = new JwtBearerEvents
        {
            // Fase B hardening: además de firma/expiración (ya validadas por el pipeline
            // JwtBearer antes de llegar acá), se consulta IRevocadorTokens con el
            // usuarioId + iat del token. Si el token fue revocado (reset de contraseña
            // posterior a su emisión), context.Fail dispara OnChallenge → 401, con el
            // mismo shape de ProblemDetails que cualquier otro token inválido.
            OnTokenValidated = context =>
            {
                var revocador = context.HttpContext.RequestServices
                    .GetRequiredService<IRevocadorTokens>();
                var usuarioIdClaim = context.Principal?.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
                var iatClaim = context.Principal?.FindFirst(JwtRegisteredClaimNames.Iat)?.Value;

                if (usuarioIdClaim is null || iatClaim is null
                    || !int.TryParse(usuarioIdClaim, out var usuarioId)
                    || !long.TryParse(iatClaim, out var iatEpoch))
                {
                    context.Fail("El token no tiene los claims requeridos.");
                    return Task.CompletedTask;
                }

                var emitidoEn = DateTimeOffset.FromUnixTimeMilliseconds(iatEpoch).UtcDateTime;
                if (!revocador.EsValido(usuarioId, emitidoEn))
                    context.Fail("El token fue revocado.");

                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                var problemDetailsService = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "No autorizado.",
                        Detail = "El token es inválido, venció o no fue provisto.",
                    },
                });
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/problem+json";

                var problemDetailsService = context.HttpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();
                await problemDetailsService.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = context.HttpContext,
                    ProblemDetails =
                    {
                        Status = StatusCodes.Status403Forbidden,
                        Title = "Prohibido.",
                        Detail = "El rol autenticado no tiene permiso para esta acción.",
                    },
                });
            },
        };
    });

// Políticas derivadas de un AuthorizationHandler (spec 2026-08-10): reemplaza el RequireClaim
// fijo por rol. Cada policy sigue llamándose igual que el permiso (Permisos.X) — los 32
// endpoints existentes no cambian ni una línea de .RequireAuthorization(Permisos.X). El
// handler resuelve contra los permisos reales del usuario (PermisoAuthorizationHandler,
// Api/Auth/), consultando IProveedorPermisos solo quando el permiso no es uno de los 4
// estructurales (AuthorizationService.PermisosEstructuralesAdmin).
builder.Services.AddScoped<IAuthorizationHandler, PermisoAuthorizationHandler>();
builder.Services.AddAuthorization(options =>
{
    foreach (var permiso in Permisos.Todos)
    {
        options.AddPolicy(permiso, policy =>
            policy.Requirements.Add(new PermisoRequirement(permiso)));
    }
});

// Rate limiting de los endpoints anónimos de licenciamiento (hardening post-Fase B):
// POST /licencia/activar, POST /auth/reset-admin/desafio y POST /auth/reset-admin son
// pre-login y de superficie de ataque (fuerza bruta de licencia / reset de Admin). Los
// GET de estado quedan afuera a propósito (el desktop los consulta en cada arranque).
// Los límites se leen de IConfiguration DENTRO del factory de la política (resuelto por
// request, no en una `var` top-level) por la misma razón documentada arriba para
// AppDbContext/JwtOptions: ApiFactory inyecta su override recién post-Build.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("licenciamiento", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = config.GetValue<int?>("RateLimiting:Licenciamiento:PermitLimit") ?? 10;
        var windowSeconds = config.GetValue<int?>("RateLimiting:Licenciamiento:WindowSeconds") ?? 60;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    // POST /auth/login (hardening deploy-vps-linux): política PROPIA, no reutiliza
    // "licenciamiento". A diferencia de licencia/activar y reset-admin (uso raro, solo
    // setup/recuperación), login es tráfico normal y frecuente del desktop -- compartir
    // el mismo balde de 10 req/60s haría que el uso legítimo de login agote la cuota de
    // los endpoints de recuperación (o viceversa). Mismo shape de configuración
    // (RateLimiting:Login:PermitLimit/WindowSeconds).
    //
    // Default 30/60, NO 10/60 como antes (review deploy-vps-linux, IMPORTANTE 6): la
    // partición es por RemoteIpAddress, pero el acceso productivo es SIEMPRE por túnel
    // SSH -- todo el tráfico entra como 127.0.0.1, así que el límite no es "N intentos por
    // usuario", es "N intentos por MINUTO para todo el municipio". Con el default viejo
    // (10), varios empleados abriendo el desktop a la misma hora (lunes 8am) más algún
    // typo de contraseña alcanzaba para que el intento 11 devolviera 429 a todos, incluido
    // el admin. 30/60 sigue siendo un freno real contra fuerza bruta automatizada (cada
    // intento paga el costo de un login completo, hashing incluido) mientras deja margen
    // para uso humano compartido y simultáneo. Ajustable sin recompilar vía appsettings o
    // una variable extra en deploy/.env -- ver el passthrough en deploy/install.sh y
    // deploy/.env.example.
    options.AddPolicy("login", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var permitLimit = config.GetValue<int?>("RateLimiting:Login:PermitLimit") ?? 30;
        var windowSeconds = config.GetValue<int?>("RateLimiting:Login:WindowSeconds") ?? 60;

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "desconocido",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
    });

    // Mismo shape de ProblemDetails que el resto de la API (401/403/500) en vez de un
    // body vacío por defecto.
    options.OnRejected = async (context, _) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var problemDetailsService = context.HttpContext.RequestServices
            .GetRequiredService<IProblemDetailsService>();
        await problemDetailsService.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails =
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = "Demasiadas solicitudes.",
                Detail = "Se superó el límite de solicitudes permitido. Esperá antes de volver a intentar.",
            },
        });
    };
});

// Limite de multipart para /finanzas/.../adjuntos (spec F3): 10MB + margen para headers,
// devuelve 400 en vez de la excepcion cruda de Kestrel si se supera. El tope de negocio
// real (10MB exactos, con mensaje claro) lo valida AdjuntoValidador en el service.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});

var app = builder.Build();

// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();

// Migración automática al arranque (Fase 3a, D9): reemplaza al DatabaseInitializer del
// desktop, que se elimina en Fase 3b. MigrateAsync es idempotente — no-op si no hay
// migraciones pendientes, así que no colisiona con ApiFactory (que ya migra su contenedor
// de Testcontainers en InitializeAsync, antes de que el host arranque).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();

    // Seed del Admin inicial (D7): reemplaza el bootstrap HTTP anónimo. Idempotente
    // (no-op si ya hay usuarios) y fail-fast (con la BD vacía y sin Bootstrap:AdminUser/
    // Bootstrap:Password configurados, lanza y la API no arranca).
    var seeder = new BootstrapAdminSeeder(
        scope.ServiceProvider.GetRequiredService<IPrimerArranqueService>(),
        app.Configuration["Bootstrap:AdminUser"],
        app.Configuration["Bootstrap:Password"]);
    await seeder.SembrarAsync();

    // Cargar el estado de licencia al arranque (Inc 7 Fase B): resuelve el código de máquina
    // y valida licencia.lic. Nunca lanza — si no hay licencia válida, la API arranca bloqueada.
    var servicioLicencia = scope.ServiceProvider.GetRequiredService<ServicioLicencia>();
    await servicioLicencia.CargarAlArranqueAsync();
}

// Andamiaje base para excepciones no manejadas: 500 -> ProblemDetails via
// AddProblemDetails() de arriba (mismo servicio que los eventos de JwtBearer usan
// para el shape de 401/403).
app.UseExceptionHandler();

// UseRateLimiter ANTES del bloqueo por licencia: aunque BloqueoLicenciaMiddleware siempre
// deja pasar /licencia/*, /auth/reset-admin/* y /auth/login (están en su propia allowlist),
// el rate limiter va primero en el pipeline para cortar un flood contra esos endpoints lo
// antes posible, sin depender de esa allowlist como única defensa. Solo pesa sobre los 4
// endpoints con .RequireRateLimiting("licenciamiento"|"login") — el resto pasa de largo
// sin costo.
app.UseRateLimiter();

// Bloqueo por licencia (Inc 7 Fase B): 423 Locked a todo salvo /licencia/*, /auth/reset-admin/*,
// /auth/login y /backups (ver BloqueoLicenciaMiddleware para el detalle y el porqué de cada
// excepción) cuando no hay licencia activa. Va antes de autenticación por RUTA, no por identidad
// -- un token obtenido vía /auth/login con licencia vencida solo abre el camino hacia /backups,
// el resto del sistema sigue bloqueado incondicionalmente.
app.UseMiddleware<BloqueoLicenciaMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

// Middleware de permisos (spec 2026-08-10): resuelve una sola vez por request, DESPUÉS de que
// el usuario esté autenticado y autorizado a nivel de policy (PermisoAuthorizationHandler, que
// corre DENTRO de UseAuthorization() y por lo tanto ANTES que este middleware), ANTES de que
// cualquier endpoint (y por lo tanto cualquier servicio de Application) se ejecute. Es el único
// punto de I/O asíncrono de todo este diseño del lado Application — permite que
// AuthorizationService.Verificar siga siendo sincrónico. Para requests sin sesión (login,
// licencia) no hace nada. Cuando el handler ya resolvió el permiso de la policy del endpoint,
// esto pega al mismo cache de IProveedorPermisos — cache-hit, no un segundo SELECT.
app.Use(async (context, next) =>
{
    if (context.User.Identity?.IsAuthenticated == true)
    {
        var usuarioIdClaim = context.User.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
        if (usuarioIdClaim is not null && int.TryParse(usuarioIdClaim, out var usuarioId))
        {
            var session = context.RequestServices.GetRequiredService<ICurrentSession>();
            var proveedor = context.RequestServices.GetRequiredService<IProveedorPermisos>();
            session.EstablecerPermisos(await proveedor.ObtenerAsync(usuarioId));
        }
    }
    await next(context);
});

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
app.MapProductosEndpoints();
app.MapMovimientosEndpoints();
app.MapIngresoPorFacturaEndpoints();
app.MapReportesEndpoints();
app.MapAuditoriaEndpoints();
app.MapUsuariosEndpoints();
app.MapCategoriasEndpoints();
app.MapProveedoresEndpoints();
app.MapUnidadesMedidaEndpoints();
app.MapFuentesFinanciamientoEndpoints();
app.MapRubrosGastoEndpoints();
app.MapLineasPoaEndpoints();
app.MapGastosEndpoints();
app.MapAdjuntosEndpoints();
app.MapIngresosCajaEndpoints();
app.MapFinanzasVistasEndpoints();
app.MapImportacionEndpoints();
app.MapLicenciaEndpoints();
app.MapResetAdminEndpoints();
app.MapBackupsEndpoints();
app.MapConfiguracionAlertasEndpoints();
app.MapLogsEndpoints();
app.MapTareasEndpoints();
app.MapDocumentosEndpoints();

app.Run();

public partial class Program;
