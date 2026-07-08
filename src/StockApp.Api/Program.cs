using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Interfaces;
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

builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IAuditLogger, AuditService>();

// Catálogo (slice: GET /productos)
builder.Services.AddScoped<IProductoRepository, ProductoRepository>();
builder.Services.AddScoped<IUnidadMedidaRepository, UnidadMedidaRepository>();
builder.Services.AddScoped<IProductoService, ProductoService>();

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
    });

// Políticas nombradas igual que las constantes de Permisos: el nombre de la política
// HTTP es literalmente el mismo string que ya usa AuthorizationService.Verificar
// puertas adentro (spec §3).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Permisos.GestionarProductos, policy =>
        policy.RequireClaim(StockAppClaimTypes.Rol,
            RolUsuario.Admin.ToString(), RolUsuario.Operador.ToString()));
});

var app = builder.Build();

// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
app.MapProductosEndpoints();

app.Run();

public partial class Program;
