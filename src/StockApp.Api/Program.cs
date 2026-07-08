using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;

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

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();

// JwtOptions: misma razón que arriba — el secreto se lee de forma diferida en el
// factory (resuelto post-Build), no en una `var` top-level. JwtOptions es un record
// posicional sin constructor sin parámetros, así que no es compatible con el patrón
// AddOptions<T>().Bind(...).ValidateOnStart() estándar (ese patrón requiere poder
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

var app = builder.Build();

// Fail-fast de configuración al arrancar el host (post-Build, ya con la configuración
// final —incluidos los overrides de test de ApiFactory—, no con la snapshot pre-Build).
app.Services.GetRequiredService<JwtOptions>();
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<AppDbContext>();
}

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();

app.Run();

public partial class Program;
