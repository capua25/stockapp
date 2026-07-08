# Fase 2a: Servidor API + JWT + Slice Vertical — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Levantar `StockApp.Api` (ASP.NET Core Minimal APIs, net10.0) con autenticación JWT, andamiaje de autorización por políticas y un slice vertical de dos endpoints de catálogo/reportes, probado de punta a punta contra Postgres real — sin tocar la app desktop.

**Architecture:** `StockApp.Api` referencia `StockApp.Application` + `StockApp.Infrastructure` y reutiliza tal cual los `IXxxService`/repositorios/DTOs existentes; la API solo orquesta routing, JWT y mapeo de errores HTTP. Composición root propia en `Program.cs` con `AppDbContext` **Scoped** (vs. Transient en la app desktop, que no se toca). `ICurrentSession` tiene una segunda implementación (`HttpCurrentSession`, scoped, armada desde los claims del JWT) que convive con `InMemorySession` sin reemplazarla — cada host registra la suya.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, JWT Bearer, Npgsql/EF Core 10, xUnit + WebApplicationFactory + Testcontainers.PostgreSql, Moq.

## Global Constraints

- Target framework `net10.0` en todos los proyectos nuevos (mismo que el resto de la solución).
- Minimal APIs con `MapGroup` + métodos de extensión `MapXxxEndpoints`; **no Controllers**.
- **NO se toca la app desktop**: `StockApp.Presentation`, `App.axaml.cs`, `InMemorySession` quedan exactamente como están.
- `AppDbContext` **Scoped por request** SOLO en la composición root de `StockApp.Api` (`Program.cs`). La app desktop sigue con `AppDbContext` Transient, sin cambios.
- **Reusar** `IXxxService`, repositorios y DTOs existentes tal cual — la API no reimplementa lógica de negocio.
- Tests de integración contra **Postgres real vía Testcontainers** (mismo patrón que Fase 1: contenedor `postgres:16-alpine`, `TRUNCATE ... RESTART IDENTITY CASCADE` entre tests para aislar estado).
- Secreto de firma JWT vía configuración / `dotnet user-secrets`, **nunca hardcodeado ni committeado**.
- Conventional Commits, **sin** `Co-Authored-By`.
- TDD: test rojo → implementación mínima → test verde → commit.
- Commits frecuentes (uno por task, al cierre de cada task).

---

## Task 1: Scaffold `StockApp.Api` + proyecto de tests + endpoint de salud

**Files:**
- Create: `src/StockApp.Api/StockApp.Api.csproj`
- Create: `src/StockApp.Api/Program.cs`
- Create: `src/StockApp.Api/appsettings.json`
- Create: `tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
- Create: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`
- Create: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`
- Test: `tests/StockApp.Api.Tests/HealthEndpointTests.cs`
- Modify: `StockApp.sln` (agrega ambos proyectos nuevos)

**Interfaces:**
- Produces: clase `public partial class Program` (entry point de Minimal API, consumido por `WebApplicationFactory<Program>` en todos los tests posteriores).
- Produces: `ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime` con `CrearContexto(): AppDbContext` — consumida por todas las tasks siguientes para seed de datos.
- Produces: `ApiTestBase` (clase base abstracta) con `protected readonly ApiFactory Factory` — todas las clases de test de integración posteriores heredan de ella.

- [ ] **Step 1: Scaffold del proyecto `StockApp.Api`**

```bash
dotnet new web -n StockApp.Api -o src/StockApp.Api
```

Esto crea `src/StockApp.Api/StockApp.Api.csproj`, `Program.cs` y `appsettings.json` con el template por defecto (incluye un endpoint `/weatherforecast` de ejemplo). En los pasos siguientes se reemplaza el contenido completo de estos tres archivos.

- [ ] **Step 2: Reemplazar `StockApp.Api.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\StockApp.Application\StockApp.Application.csproj" />
    <ProjectReference Include="..\StockApp.Infrastructure\StockApp.Infrastructure.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Reemplazar `Program.cs` (mínimo, sin rutas todavía)**

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// AppDbContext: Scoped por request (patrón natural de ASP.NET Core). La app desktop
// sigue con AppDbContext Transient en su propia composición root — no se unifican.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
        "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

var app = builder.Build();

app.Run();

public partial class Program;
```

- [ ] **Step 4: Reemplazar `appsettings.json`**

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=stockapp;Username=stockapp;Password=stockapp"
  }
}
```

- [ ] **Step 5: Borrar los archivos sobrantes del template**

```bash
rm -f src/StockApp.Api/StockApp.Api.http
```

(`appsettings.Development.json`, si el template lo generó, se puede dejar vacío `{}` o borrar; no se usa en 2a.)

- [ ] **Step 6: Agregar `StockApp.Api` a la solución**

```bash
dotnet sln StockApp.sln add src/StockApp.Api/StockApp.Api.csproj --solution-folder src
```

- [ ] **Step 7: Scaffold del proyecto de tests `StockApp.Api.Tests`**

```bash
dotnet new xunit -n StockApp.Api.Tests -o tests/StockApp.Api.Tests
rm -f tests/StockApp.Api.Tests/UnitTest1.cs
```

- [ ] **Step 8: Reemplazar `StockApp.Api.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.*" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="Testcontainers.PostgreSql" Version="4.13.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StockApp.Api\StockApp.Api.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 9: Agregar `StockApp.Api.Tests` a la solución**

```bash
dotnet sln StockApp.sln add tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --solution-folder tests
```

- [ ] **Step 10: Escribir `ApiFactory.cs` (fixture: WebApplicationFactory + Postgres real de Testcontainers)**

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StockApp.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Levanta la API completa (WebApplicationFactory) contra un Postgres real de
/// Testcontainers — mismo patrón que PostgresFixture en
/// tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs (Fase 1), pero
/// arrancando el host HTTP completo en vez de solo un AppDbContext. Sobrescribe
/// 'ConnectionStrings:Default' para apuntar al contenedor.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
            });
        });
    }

    /// <summary>Crea un AppDbContext nuevo apuntado al contenedor (para setup/seed de datos en tests).</summary>
    public AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options);
    }
}

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory> { }
```

- [ ] **Step 11: Escribir `ApiTestBase.cs` (aislamiento de datos entre tests)**

```csharp
using Xunit;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Base para tests de integración de StockApp.Api. Antes de cada test hace TRUNCATE
/// de todas las tablas con RESTART IDENTITY para aislar el estado — mismo patrón que
/// PostgresRepositoryTestBase en StockApp.Infrastructure.Tests (Fase 1).
/// </summary>
[Collection("Api")]
public abstract class ApiTestBase
{
    protected readonly ApiFactory Factory;

    protected ApiTestBase(ApiFactory factory)
    {
        Factory = factory;
        LimpiarTablas();
    }

    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
}
```

- [ ] **Step 12: Escribir el test que falla — `HealthEndpointTests.cs`**

```csharp
using System.Net;
using StockApp.Api.Tests.Fixtures;
using Xunit;

namespace StockApp.Api.Tests;

public class HealthEndpointTests : ApiTestBase
{
    public HealthEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetRaiz_DevuelveOk()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 13: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter GetRaiz_DevuelveOk`
Expected: FAIL — `404 Not Found` (no hay ninguna ruta mapeada en `Program.cs` todavía). Requiere Docker corriendo (Testcontainers levanta el contenedor Postgres igual, aunque este test no toque la DB).

- [ ] **Step 14: Mapear el endpoint de salud en `Program.cs`**

Reemplazar el bloque `var app = builder.Build(); app.Run();` por:

```csharp
var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.Run();
```

- [ ] **Step 15: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter GetRaiz_DevuelveOk`
Expected: PASS

- [ ] **Step 16: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests StockApp.sln
git commit -m "feat(api): scaffold de StockApp.Api con endpoint de salud"
```

---

## Task 2: Servicio de emisión de JWT

**Files:**
- Create: `src/StockApp.Api/Auth/StockAppClaimTypes.cs`
- Create: `src/StockApp.Api/Auth/JwtOptions.cs`
- Create: `src/StockApp.Api/Auth/IJwtTokenService.cs`
- Create: `src/StockApp.Api/Auth/JwtTokenService.cs`
- Modify: `src/StockApp.Api/StockApp.Api.csproj` (agrega `Microsoft.AspNetCore.Authentication.JwtBearer`)
- Test: `tests/StockApp.Api.Tests/Auth/JwtTokenServiceTests.cs`

**Interfaces:**
- Consumes: `RolUsuario` (`StockApp.Domain.Enums`, ya existe: `Admin`/`Operador`).
- Produces: `IJwtTokenService.GenerarToken(int usuarioId, RolUsuario rol): string` — consumido por `AuthEndpoints` (Task 3) y directamente por los tests de Task 4/5 para fabricar tokens sin pasar por `/auth/login`.
- Produces: `JwtOptions(string Secret, TimeSpan Expiracion)` — registrado como singleton en `Program.cs` (Task 3).
- Produces: `StockAppClaimTypes.UsuarioId = "usuarioId"`, `StockAppClaimTypes.Rol = "rol"` — usados por `JwtTokenService` (escritura) y `HttpCurrentSession` (Task 4, lectura) para no tener nombres de claim divergentes entre quien firma y quien lee.

- [ ] **Step 1: Agregar el paquete JwtBearer al csproj**

En `src/StockApp.Api/StockApp.Api.csproj`, dentro de un nuevo `<ItemGroup>`:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.*" />
  </ItemGroup>
```

- [ ] **Step 2: Escribir el test que falla — `JwtTokenServiceTests.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using StockApp.Api.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class JwtTokenServiceTests
{
    private static readonly JwtOptions Options =
        new("clave-de-prueba-de-al-menos-32-caracteres-1234567890", TimeSpan.FromHours(10));

    [Fact]
    public void GenerarToken_IncluyeClaimsDeUsuarioIdYRol()
    {
        var service = new JwtTokenService(Options);

        var token = service.GenerarToken(42, RolUsuario.Admin);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("42", jwt.Claims.Single(c => c.Type == StockAppClaimTypes.UsuarioId).Value);
        Assert.Equal("Admin", jwt.Claims.Single(c => c.Type == StockAppClaimTypes.Rol).Value);
    }

    [Fact]
    public void GenerarToken_VenceEnDiezHoras()
    {
        var service = new JwtTokenService(Options);
        var antes = DateTime.UtcNow;

        var token = service.GenerarToken(1, RolUsuario.Operador);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var vencimientoEsperado = antes.Add(Options.Expiracion);
        Assert.True(Math.Abs((jwt.ValidTo - vencimientoEsperado).TotalMinutes) < 1);
    }
}
```

Falta `using System.Linq;` — agregarlo (no está en implicit usings del SDK de test):

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using StockApp.Api.Auth;
using StockApp.Domain.Enums;
using Xunit;
```

- [ ] **Step 3: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter JwtTokenServiceTests`
Expected: FAIL — error de compilación (`JwtOptions`, `JwtTokenService`, `StockAppClaimTypes` no existen todavía).

- [ ] **Step 4: Implementar `StockAppClaimTypes.cs`**

```csharp
namespace StockApp.Api.Auth;

/// <summary>
/// Nombres de claim del JWT de 2a. Centralizados acá para que quien firma el token
/// (JwtTokenService) y quien lo lee (HttpCurrentSession, las políticas de
/// autorización en Program.cs) usen exactamente el mismo string — evita drift
/// entre escritor y lector de claims.
/// </summary>
public static class StockAppClaimTypes
{
    public const string UsuarioId = "usuarioId";
    public const string Rol = "rol";
}
```

- [ ] **Step 5: Implementar `JwtOptions.cs`**

```csharp
namespace StockApp.Api.Auth;

/// <summary>
/// Secreto de firma y tiempo de vida del JWT. El secreto viene de configuración
/// (user-secrets en desarrollo; variable de entorno o secret store en producción —
/// fuera de alcance de 2a, ver spec §2). Expiracion es fija en 10 horas en 2a,
/// no configurable (spec §2: revisitar en 2b/2c si hace falta ajustarla).
/// </summary>
public record JwtOptions(string Secret, TimeSpan Expiracion);
```

- [ ] **Step 6: Implementar `IJwtTokenService.cs`**

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

public interface IJwtTokenService
{
    /// <summary>Firma un JWT con claims usuarioId y rol, vencimiento según JwtOptions.Expiracion.</summary>
    string GenerarToken(int usuarioId, RolUsuario rol);
}
```

- [ ] **Step 7: Implementar `JwtTokenService.cs`**

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

public class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(JwtOptions options) => _options = options;

    public string GenerarToken(int usuarioId, RolUsuario rol)
    {
        var claims = new[]
        {
            new Claim(StockAppClaimTypes.UsuarioId, usuarioId.ToString()),
            new Claim(StockAppClaimTypes.Rol, rol.ToString()),
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.Add(_options.Expiracion),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
```

- [ ] **Step 8: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter JwtTokenServiceTests`
Expected: PASS (2 tests)

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Api/StockApp.Api.csproj src/StockApp.Api/Auth tests/StockApp.Api.Tests/Auth
git commit -m "feat(api): servicio de emisión de JWT con claims usuarioId/rol"
```

---

## Task 3: `POST /auth/login`

**Files:**
- Create: `src/StockApp.Api/Endpoints/AuthEndpoints.cs`
- Create: `tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` (agrega `Jwt:Secret` a la config de test)
- Test: `tests/StockApp.Api.Tests/LoginEndpointTests.cs`

**Interfaces:**
- Consumes: `IUsuarioRepository.BuscarPorNombreAsync(string): Task<Usuario?>` (existente, `StockApp.Application/Interfaces/IUsuarioRepository.cs`).
- Consumes: `IPasswordHasher.Verify(string plaintext, string hash): bool` (existente, `BcryptPasswordHasher`).
- Consumes: `IJwtTokenService.GenerarToken(int, RolUsuario): string` (Task 2).
- Produces: `record LoginRequest(string? NombreUsuario, string? Contrasena)`, `record LoginResponse(string Token)` — públicos en `StockApp.Api.Endpoints`, consumidos por los tests.
- Produces: `DatosDePrueba.SeedUsuarioAsync(AppDbContext, string nombreUsuario, string contrasena, RolUsuario rol): Task<Usuario>` — reusado por Task 4/5.

**Decisión de diseño (no reusar `IAuthService.LoginAsync`):** `AuthService.LoginAsync` (existente) llama a `_session.IniciarSesion(usuario)`, mutando el `ICurrentSession` — ese es el modelo de sesión mutable de la app desktop (un proceso de larga vida, un solo usuario logueado). En la API cada request es independiente y la "sesión" se deriva del JWT, no de una mutación server-side. Por eso el endpoint de login llama directamente a `IUsuarioRepository` + `IPasswordHasher` (tal como indica el spec §2: "Verifica la contraseña server-side reusando IPasswordHasher"), sin pasar por `IAuthService`/`ICurrentSession.IniciarSesion`.

- [ ] **Step 1: Agregar `Jwt:Secret` a la config de test en `ApiFactory.cs`**

En `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`, agregar una constante pública y usarla en `ConfigureWebHost`:

```csharp
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string JwtSecretDePrueba = "clave-de-prueba-de-al-menos-32-caracteres-1234567890";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using var ctx = CrearContexto();
        await ctx.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _container.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:Secret"] = JwtSecretDePrueba,
            });
        });
    }

    public AppDbContext CrearContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_container.GetConnectionString())
            .Options;
        return new AppDbContext(options);
    }
}
```

(El resto del archivo, incluida la clase `ApiCollection`, queda igual que en Task 1.)

- [ ] **Step 2: Escribir `DatosDePrueba.cs`**

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>Helpers de seed para los tests de integración de StockApp.Api.</summary>
public static class DatosDePrueba
{
    private static readonly BcryptPasswordHasher Hasher = new();

    public static async Task<Usuario> SeedUsuarioAsync(
        AppDbContext ctx, string nombreUsuario, string contrasena, RolUsuario rol)
    {
        var usuario = new Usuario
        {
            NombreUsuario = nombreUsuario,
            HashContrasena = Hasher.Hash(contrasena),
            Rol = rol,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Usuarios.Add(usuario);
        await ctx.SaveChangesAsync();
        return usuario;
    }
}
```

- [ ] **Step 3: Escribir el test que falla — `LoginEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Json;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class LoginEndpointTests : ApiTestBase
{
    public LoginEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task Login_ConCredencialesValidas_Devuelve200ConToken()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "Secreta123!"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(body!.Token));
    }

    [Fact]
    public async Task Login_ConCredencialesInvalidas_Devuelve401()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin.test", "ContraseñaIncorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ConBodyVacio_Devuelve400()
    {
        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter LoginEndpointTests`
Expected: FAIL — error de compilación (`LoginRequest`/`LoginResponse` no existen todavía).

- [ ] **Step 5: Implementar `AuthEndpoints.cs`**

```csharp
using StockApp.Api.Auth;
using StockApp.Application.Interfaces;

namespace StockApp.Api.Endpoints;

public record LoginRequest(string? NombreUsuario, string? Contrasena);
public record LoginResponse(string Token);

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth");

        group.MapPost("/login", async (
            LoginRequest request,
            IUsuarioRepository usuarios,
            IPasswordHasher hasher,
            IJwtTokenService jwtTokenService) =>
        {
            if (string.IsNullOrWhiteSpace(request.NombreUsuario)
                || string.IsNullOrWhiteSpace(request.Contrasena))
            {
                return Results.Problem(
                    title: "Usuario y contraseña son obligatorios.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var usuario = await usuarios.BuscarPorNombreAsync(request.NombreUsuario);

            // No se distingue "usuario inexistente" de "contraseña incorrecta" ni de
            // "usuario inactivo" en la respuesta (spec §2: no filtrar si el usuario existe).
            if (usuario is null
                || !usuario.Activo
                || !hasher.Verify(request.Contrasena, usuario.HashContrasena))
            {
                return Results.Problem(
                    title: "Usuario o contraseña inválidos.",
                    statusCode: StatusCodes.Status401Unauthorized);
            }

            var token = jwtTokenService.GenerarToken(usuario.Id, usuario.Rol);
            return Results.Ok(new LoginResponse(token));
        });

        return app;
    }
}
```

- [ ] **Step 6: Registrar los servicios de login y el endpoint en `Program.cs`**

Reemplazar el contenido completo de `src/StockApp.Api/Program.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Auth;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;

var builder = WebApplication.CreateBuilder(args);

// AppDbContext: Scoped por request (patrón natural de ASP.NET Core). La app desktop
// sigue con AppDbContext Transient en su propia composición root — no se unifican.
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
        "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
        "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");

builder.Services.AddSingleton(new JwtOptions(jwtSecret, TimeSpan.FromHours(10)));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter LoginEndpointTests`
Expected: PASS (3 tests)

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(api): endpoint POST /auth/login con verificacion BCrypt y emision de JWT"
```

---

## Task 4: Middleware JWT Bearer + `HttpCurrentSession` + política `GestionarProductos` + `GET /productos`

**Files:**
- Create: `src/StockApp.Api/Auth/HttpCurrentSession.cs`
- Create: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs` (agrega seed de Producto)
- Test: `tests/StockApp.Api.Tests/ProductosEndpointTests.cs`

**Interfaces:**
- Consumes: `IProductoService.BuscarPorTextoAsync(string? texto): Task<IReadOnlyList<ProductoDto>>` (existente, `null` devuelve el listado completo).
- Consumes: `ICurrentSession` (existente, `StockApp.Application/Interfaces/ICurrentSession.cs`) — nueva implementación `HttpCurrentSession`.
- Consumes: `Permisos.GestionarProductos = "catalogo.productos"` (existente).
- Produces: `HttpCurrentSession : ICurrentSession` — registrado Scoped, consumido transitivamente por `ProductoService`/`AuthorizationService` (defensa en profundidad: la política ASP.NET Core ya filtra antes de llegar al service, pero `ProductoService.BuscarPorTextoAsync` vuelve a verificar `Permisos.GestionarProductos` puertas adentro).

- [ ] **Step 1: Extender `DatosDePrueba.cs` con seed de Producto**

Agregar al final de la clase `DatosDePrueba` (en `tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs`):

```csharp
    public static async Task<Producto> SeedProductoAsync(AppDbContext ctx, string codigo, string nombre)
    {
        var unidad = new UnidadMedida { Nombre = "Unidad", Abreviatura = "u", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = nombre,
            UnidadMedidaId = unidad.Id,
            PrecioCosto = 10m,
            PrecioVenta = 20m,
            StockActual = 5m,
            StockMinimo = 0m,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }
```

(`Producto` y `UnidadMedida` viven en `StockApp.Domain.Entities`, ya importado por el `using` existente en el archivo.)

- [ ] **Step 2: Escribir los tests que fallan — `ProductosEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Catalogo;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProductosEndpointTests : ApiTestBase
{
    public ProductosEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetProductos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProductos_ConTokenAdmin_Devuelve200ConProductosSeedeados()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-A1", "Producto Admin Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Contains(productos!, p => p.Codigo == "SKU-A1");
    }

    [Fact]
    public async Task GetProductos_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-O1", "Producto Operador Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: FAIL — `404 Not Found` en las tres (ninguna ruta `/productos` mapeada todavía).

- [ ] **Step 4: Implementar `HttpCurrentSession.cs`**

```csharp
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Auth;

/// <summary>
/// ICurrentSession construida por request a partir de los claims del JWT ya validado.
/// Reemplaza a InMemorySession SOLO en el grafo de DI de StockApp.Api; InMemorySession
/// sigue en uso, sin cambios, en la composición root de la app desktop (App.axaml.cs).
/// No admite mutación: el JWT de 2a solo lleva usuarioId y rol (spec §2), así que
/// UsuarioActual.NombreUsuario/NombreCompleto quedan vacíos — ningún endpoint del
/// slice de 2a los consume (solo RolActual, vía AuthorizationService.Verificar).
/// </summary>
public class HttpCurrentSession : ICurrentSession
{
    private readonly IHttpContextAccessor _accessor;

    public HttpCurrentSession(IHttpContextAccessor accessor) => _accessor = accessor;

    public bool EstaAutenticado => _accessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;

    public UsuarioSesion? UsuarioActual
    {
        get
        {
            var user = _accessor.HttpContext?.User;
            if (user is null || user.Identity?.IsAuthenticated != true)
                return null;

            var idClaim = user.FindFirst(StockAppClaimTypes.UsuarioId)?.Value;
            var rolClaim = user.FindFirst(StockAppClaimTypes.Rol)?.Value;

            if (idClaim is null || rolClaim is null)
                return null;

            return new UsuarioSesion(
                int.Parse(idClaim),
                NombreUsuario: string.Empty,
                Enum.Parse<RolUsuario>(rolClaim),
                NombreCompleto: null);
        }
    }

    public RolUsuario? RolActual
    {
        get
        {
            var rolClaim = _accessor.HttpContext?.User.FindFirst(StockAppClaimTypes.Rol)?.Value;
            return rolClaim is null ? null : Enum.Parse<RolUsuario>(rolClaim);
        }
    }

    public void IniciarSesion(Usuario usuario) =>
        throw new NotSupportedException(
            "HttpCurrentSession se arma desde los claims del JWT por request; no admite " +
            "IniciarSesion. El login emite un token nuevo en vez de mutar una sesión existente.");

    public void CerrarSesion() =>
        throw new NotSupportedException(
            "HttpCurrentSession se arma desde los claims del JWT por request; no admite " +
            "CerrarSesion. El cliente descarta el token para 'cerrar sesión'.");
}
```

- [ ] **Step 5: Implementar `ProductosEndpoints.cs` (por ahora, solo `GET /`)**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;

namespace StockApp.Api.Endpoints;

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(null)))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
```

- [ ] **Step 6: Reemplazar `Program.cs` (JWT Bearer, HttpCurrentSession, política GestionarProductos, DI de catálogo)**

```csharp
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Falta la cadena de conexión 'ConnectionStrings:Default' en appsettings.json. " +
        "Se requiere un PostgreSQL accesible (contenedor Docker local u on-premise).");

builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));

// ICurrentSession: scoped, armada desde los claims del JWT del request.
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

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException(
        "Falta 'Jwt:Secret' en la configuración. En desarrollo: " +
        "dotnet user-secrets set \"Jwt:Secret\" \"<clave-de-al-menos-32-caracteres>\".");

builder.Services.AddSingleton(new JwtOptions(jwtSecret, TimeSpan.FromHours(10)));
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
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

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Ok(new { status = "ok", service = "StockApp.Api" }));

app.MapAuthEndpoints();
app.MapProductosEndpoints();

app.Run();

public partial class Program;
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: PASS (3 tests)

- [ ] **Step 8: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — Health, JwtTokenService, Login, Productos)

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(api): middleware JWT Bearer, HttpCurrentSession y GET /productos con politica GestionarProductos"
```

---

## Task 5: Política `VerReportes` (solo Admin) + `GET /productos/reporte-valorizacion`

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/ReporteValorizacionEndpointTests.cs`

**Interfaces:**
- Consumes: `IReporteStockService.ObtenerValorizacionAsync(): Task<ValorizacionReporteDto>` (existente, `StockApp.Application/Reportes/IReporteStockService.cs`).
- Consumes: `Permisos.VerReportes = "reportes.ver"` (existente, ausente de `AccionesOperador` en `AuthorizationService` → exclusiva de Admin).
- Consumes: `IMovimientoStockService`/`IMovimientoStockRepository` (existente, dependencia transitiva de `ReporteStockService`).

- [ ] **Step 1: Escribir los tests que fallan — `ReporteValorizacionEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ReporteValorizacionEndpointTests : ApiTestBase
{
    public ReporteValorizacionEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetReporteValorizacion_ConTokenOperador_Devuelve403()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteValorizacion_ConTokenAdmin_Devuelve200ConValorizacion()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-V1", "Producto Valorizacion Test");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>();
        Assert.Contains(reporte!.Items, i => i.Codigo == "SKU-V1");
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ReporteValorizacionEndpointTests`
Expected: FAIL — `404 Not Found` (la ruta `/productos/reporte-valorizacion` no existe todavía).

- [ ] **Step 3: Extender `ProductosEndpoints.cs` con el segundo endpoint**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Reportes;

namespace StockApp.Api.Endpoints;

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(null)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapGet("/reporte-valorizacion", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerValorizacionAsync()))
            .RequireAuthorization(Permisos.VerReportes);

        return app;
    }
}
```

- [ ] **Step 4: Agregar la política `VerReportes` y la DI de reportes/movimientos en `Program.cs`**

Agregar estos `using` al principio:

```csharp
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
```

Agregar, después del bloque de DI de catálogo (`services.AddScoped<IProductoService, ProductoService>();`):

```csharp
// Reportes (slice: GET /productos/reporte-valorizacion)
builder.Services.AddScoped<IMovimientoStockRepository, MovimientoStockRepository>();
builder.Services.AddScoped<IMovimientoStockService, MovimientoStockService>();
builder.Services.AddScoped<IReporteStockRepository, ReporteStockRepository>();
builder.Services.AddScoped<IReporteStockService, ReporteStockService>();
```

Agregar el `using StockApp.Infrastructure.Repositories;` ya está presente; falta ninguno nuevo de Infrastructure porque `MovimientoStockRepository`/`ReporteStockRepository` viven en el mismo namespace ya importado.

Modificar el bloque `AddAuthorization` para agregar la segunda política:

```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(Permisos.GestionarProductos, policy =>
        policy.RequireClaim(StockAppClaimTypes.Rol,
            RolUsuario.Admin.ToString(), RolUsuario.Operador.ToString()));

    options.AddPolicy(Permisos.VerReportes, policy =>
        policy.RequireClaim(StockAppClaimTypes.Rol, RolUsuario.Admin.ToString()));
});
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ReporteValorizacionEndpointTests`
Expected: PASS (2 tests)

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(api): politica VerReportes (solo Admin) y GET /productos/reporte-valorizacion"
```

---

## Task 6: Andamiaje `ProblemDetails` (401 / 403 / 400)

**Files:**
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/ProblemDetailsTests.cs`

**Interfaces:**
- Consumes: `IProblemDetailsService` (`Microsoft.AspNetCore.Http`, servicio provisto por `AddProblemDetails()`).

Se implementan explícitamente `JwtBearerEvents.OnChallenge`/`OnForbidden` (en vez de depender de la conversión automática de status codes a ProblemDetails) para que el shape de 401/403 sea determinístico y no dependa de comportamiento implícito del framework.

- [ ] **Step 1: Escribir los tests que fallan — `ProblemDetailsTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProblemDetailsTests : ApiTestBase
{
    public ProblemDetailsTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task SinToken_Devuelve401ComoProblemDetails()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(401, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task TokenOperador_EnEndpointSoloAdmin_Devuelve403ComoProblemDetails()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task LoginBodyVacio_Devuelve400ComoProblemDetails()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/auth/login", new LoginRequest(null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProblemDetailsTests`
Expected: FAIL en los dos primeros (401/403 hoy devuelven body vacío, sin `Content-Type`). El tercero (400 de login) probablemente ya pasa porque `Results.Problem` ya devuelve `application/problem+json` por defecto desde Task 3 — igual se corre toda la clase junto para confirmarlo.

- [ ] **Step 3: Agregar `AddProblemDetails()` y los eventos de JWT Bearer en `Program.cs`**

Agregar, inmediatamente después del bloque `AddSingleton<IAuthorizationService, AuthorizationService>();` (no importa el orden exacto respecto a las demás líneas de `builder.Services`, pero debe ir antes de `builder.Build()`):

```csharp
builder.Services.AddProblemDetails();
```

Modificar el bloque `AddJwtBearer` para agregar `options.Events`:

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        };

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
```

Agregar el andamiaje base para excepciones no manejadas (500 → ProblemDetails), justo después de `var app = builder.Build();` y antes de `app.UseAuthentication();`:

```csharp
app.UseExceptionHandler();
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProblemDetailsTests`
Expected: PASS (3 tests)

- [ ] **Step 5: Correr toda la suite completa de `StockApp.Api.Tests`**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(api): andamiaje ProblemDetails para 401/403/400 con eventos JWT Bearer explicitos"
```

---

## Task 7: Verificación orgánica manual + documentación de arranque

**Files:**
- Create: `src/StockApp.Api/README.md`

**Interfaces:** ninguna nueva — este task no agrega código, cierra el sub-incremento con la verificación manual que exige el spec §6 ("no se da 2a por terminada solo con tests en verde").

- [ ] **Step 1: Escribir `src/StockApp.Api/README.md`**

```markdown
# StockApp.Api

API de StockApp (Fase 2a): JWT + slice vertical de catálogo/reportes.

## Requisitos

- .NET 10 SDK
- PostgreSQL accesible (local o contenedor Docker) con la base `stockapp` migrada
  (mismas migraciones que la app desktop, en `StockApp.Infrastructure/Migrations`).
- Al menos un usuario existente en la tabla `Usuarios` (sembrado por `StockApp.Seeder`
  o por `PrimerArranqueService` de la app desktop — el bootstrap de primer arranque
  vía API queda para Fase 4, spec §7).

## Configurar el secreto JWT (desarrollo)

El secreto de firma NUNCA se hardcodea ni se committea. En desarrollo se define vía
user-secrets:

\`\`\`bash
cd src/StockApp.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "una-clave-de-desarrollo-de-al-menos-32-caracteres"
\`\`\`

## Correr la API

\`\`\`bash
dotnet run --project src/StockApp.Api/StockApp.Api.csproj
\`\`\`

Kestrel expone la API en las URLs impresas en consola (HTTPS de desarrollo por
defecto; TLS con certificado real del municipio se resuelve en Fase 2c).

## Verificación manual (curl)

Con la API corriendo, en otra terminal:

\`\`\`bash
# 1) Login con un usuario existente en la base
curl -k -X POST https://localhost:PORT/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"<la-contraseña-real>"}'

# Copiar el valor de "token" de la respuesta y reemplazar <TOKEN> abajo.

# 2) GET /productos con el token obtenido
curl -k https://localhost:PORT/productos \
  -H "Authorization: Bearer <TOKEN>"

# 3) GET /productos/reporte-valorizacion (requiere token de un usuario Admin)
curl -k https://localhost:PORT/productos/reporte-valorizacion \
  -H "Authorization: Bearer <TOKEN>"
\`\`\`

Confirmar que los productos reales de la base salen por HTTP, y que un token de
usuario Operador recibe `403` en el endpoint de valorización.
```

- [ ] **Step 2: Ejecutar la verificación manual real**

Con Docker (o un Postgres local) disponible, seguir exactamente los pasos del README recién creado:

1. `dotnet user-secrets set "Jwt:Secret" "..."` en `src/StockApp.Api`.
2. `dotnet run --project src/StockApp.Api/StockApp.Api.csproj` (dejarlo corriendo).
3. Los tres `curl` del README, contra un usuario real ya sembrado en la base.

Expected: login devuelve `200` + token; `GET /productos` devuelve `200` con productos reales; `GET /productos/reporte-valorizacion` devuelve `403` con un token Operador y `200` con un token Admin.

Esta verificación es manual y no se automatiza — es la que cierra 2a según spec §6 ("no se da 2a por terminada solo con tests en verde, sino viendo el flujo real correr").

- [ ] **Step 3: Correr la suite completa de tests una última vez**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Api/README.md
git commit -m "docs(api): instrucciones de arranque y verificacion manual de StockApp.Api"
```

---

## Self-Review

**1. Cobertura del spec:**

| Sección del spec | Task que la implementa |
|---|---|
| §1 Arquitectura (proyecto nuevo, referencias, DbContext Scoped, endpoints por recurso con MapGroup) | Task 1, 4, 5 |
| §2 Autenticación JWT (login, claims, vencimiento 10h, secreto vía config, sin refresh, middleware, ICurrentSession scoped) | Task 2, 3, 4 |
| §3 Autorización (políticas nombradas como Permisos, GestionarProductos + VerReportes, fail-closed) | Task 4, 5 |
| §4 Slice vertical (GET /productos, GET /productos/reporte-valorizacion) | Task 4, 5 |
| §5 Manejo de errores (401/403/400, 409 diferido) | Task 3 (400), Task 6 (401/403 shape) |
| §6 Testing (WebApplicationFactory + Postgres real, 6 casos + verificación manual) | Task 1, 3, 4, 5, 7 |
| §7 Alcance y límites (Contracts no se extrae, bootstrap diferido, TLS/versionado diferidos, no tocar desktop) | Respetado en todas las tasks (sin tasks propias — son omisiones deliberadas, no requieren código) |
| §8 Dependencias (Fase 0/1 ya completas) | Verificado en la investigación previa a este plan (ProductoDto, ValorizacionReporteDto, PostgresFixture ya existen) |

Gap encontrado y corregido: el spec §6 solo lista 6 casos de test automatizados, pero §5 exige explícitamente el manejo de 400 en `/auth/login`. Se agregó `Login_ConBodyVacio_Devuelve400` en Task 3 y su confirmación de shape ProblemDetails en Task 6 para cerrar ese gap.

**2. Scan de placeholders:** no quedan `TODO`, "manejar apropiadamente" ni "similar a Task N" — cada Modify muestra el archivo completo o el bloque exacto a insertar, con código real.

**3. Consistencia de tipos:** `LoginRequest`/`LoginResponse` (Task 3) se usan igual en Task 6. `IJwtTokenService.GenerarToken(int, RolUsuario): string` (Task 2) se usa igual en Task 3, 4 y 5. `StockAppClaimTypes.UsuarioId`/`Rol` (Task 2) se usan igual en `JwtTokenService`, `HttpCurrentSession` y las políticas de `Program.cs` (Task 4/5). `DatosDePrueba.SeedUsuarioAsync`/`SeedProductoAsync` (Task 3/4) se reusan sin cambios de firma en tasks posteriores. `ApiFactory.CrearContexto()` y `ApiTestBase` (Task 1) se reusan sin cambios en todas las clases de test.
