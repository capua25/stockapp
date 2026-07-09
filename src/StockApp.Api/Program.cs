using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.ErrorHandling;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
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
builder.Services.AddScoped<IMovimientoStockRepository, MovimientoStockRepository>();
builder.Services.AddScoped<IMovimientoStockService, MovimientoStockService>();
builder.Services.AddScoped<IReporteStockRepository, ReporteStockRepository>();
builder.Services.AddScoped<IReporteStockService, ReporteStockService>();

// Catálogo — tablas maestras (Fase 2b)
builder.Services.AddScoped<ICategoriaRepository, CategoriaRepository>();
builder.Services.AddScoped<ICategoriaService, CategoriaService>();
builder.Services.AddScoped<IProveedorRepository, ProveedorRepository>();
builder.Services.AddScoped<IProveedorService, ProveedorService>();
builder.Services.AddScoped<IUnidadMedidaService, UnidadMedidaService>();
// IUnidadMedidaRepository ya está registrado desde Fase 2a (usado por ProductosEndpoints).

// Auditoría (Fase 2b)
builder.Services.AddScoped<IAuditoriaQueryRepository, AuditoriaQueryRepository>();
builder.Services.AddScoped<IAuditoriaQueryService, AuditoriaQueryService>();

// Usuarios — ABM completo vía API (Fase 2b). IUsuarioRepository y IPasswordHasher
// ya están registrados desde Fase 2a (usados por AuthEndpoints).
builder.Services.AddScoped<IUsuarioService, UsuarioService>();

// JwtOptions: misma razón que AppDbContext arriba — el secreto se lee de forma diferida
// en el factory (resuelto post-Build), no en una `var` top-level. JwtOptions es un
// record posicional sin constructor sin parámetros, así que no es compatible con el
// patrón AddOptions<T>().Bind(...).ValidateOnStart() estándar (ese patrón requiere poder
// instanciar T con Activator.CreateInstance<T>() y mutar propiedades por reflexión).
// Se preserva el fail-fast con mensaje amigable forzando la resolución del singleton
// apenas arranca el host (justo después de builder.Build(), abajo).
builder.Services.AddSingleton(sp =>
{
    var secret = sp.GetRequiredService<IConfiguration>()["Jwt:Secret"]
        ?? throw new InvalidOperationException(
            "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
            "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");
    return new JwtOptions(secret, TimeSpan.FromHours(10));
});
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

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

var app = builder.Build();

// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>();
}

// Andamiaje base para excepciones no manejadas: 500 -> ProblemDetails via
// AddProblemDetails() de arriba (mismo servicio que los eventos de JwtBearer usan
// para el shape de 401/403).
app.UseExceptionHandler();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
app.MapProductosEndpoints();
app.MapMovimientosEndpoints();
app.MapReportesEndpoints();
app.MapAuditoriaEndpoints();

app.Run();

public partial class Program;
