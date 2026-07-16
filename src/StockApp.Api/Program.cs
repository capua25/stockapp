using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.ErrorHandling;
using StockApp.Api.Licenciamiento;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Licenciamiento;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Licenciamiento;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Platform;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Reportes;
using StockApp.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton<IAuthorizationService, AuthorizationService>();

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

// Auditoría (Fase 2b)
builder.Services.AddScoped<IAuditoriaQueryRepository, AuditoriaQueryRepository>();
builder.Services.AddScoped<IAuditoriaQueryService, AuditoriaQueryService>();

// Usuarios — ABM completo vía API (Fase 2b). IUsuarioRepository y IPasswordHasher
// ya están registrados desde Fase 2a (usados por AuthEndpoints).
builder.Services.AddScoped<IUsuarioService, UsuarioService>();

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

// Políticas derivadas de AuthorizationService (Fase 2b, D1): NO se declaran a mano.
// Para cada permiso de Permisos.Todos, se arma la política con los roles que
// AuthorizationService.TienePermiso autoriza — una sola fuente de verdad para la
// tabla rol→permiso, compartida entre la API (primera barrera) y los servicios de
// aplicación (segunda barrera, defensa en profundidad — D2).
var authServiceParaPoliticas = new AuthorizationService();
builder.Services.AddAuthorization(options =>
{
    foreach (var permiso in Permisos.Todos)
    {
        var rolesPermitidos = Enum.GetValues<RolUsuario>()
            .Where(rol => authServiceParaPoliticas.TienePermiso(rol, permiso))
            .Select(rol => rol.ToString())
            .ToArray();

        options.AddPolicy(permiso, policy =>
            policy.RequireClaim(StockAppClaimTypes.Rol, rolesPermitidos));
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
// deja pasar /licencia/* y /auth/reset-admin/* (están en su propia allowlist), el rate
// limiter va primero en el pipeline para cortar un flood contra esos endpoints lo antes
// posible, sin depender de esa allowlist como única defensa. Solo pesa sobre los 3
// endpoints con .RequireRateLimiting("licenciamiento") — el resto pasa de largo sin costo.
app.UseRateLimiter();

// Bloqueo por licencia (Inc 7 Fase B): 423 Locked a todo salvo /licencia/* y /auth/reset-admin/*
// cuando no hay licencia activa. Va antes de autenticación (bloquea incluso el login).
app.UseMiddleware<BloqueoLicenciaMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
app.MapProductosEndpoints();
app.MapMovimientosEndpoints();
app.MapReportesEndpoints();
app.MapAuditoriaEndpoints();
app.MapUsuariosEndpoints();
app.MapCategoriasEndpoints();
app.MapProveedoresEndpoints();
app.MapUnidadesMedidaEndpoints();
app.MapFuentesFinanciamientoEndpoints();
app.MapRubrosGastoEndpoints();
app.MapLineasPoaEndpoints();
app.MapLicenciaEndpoints();
app.MapResetAdminEndpoints();

app.Run();

public partial class Program;
