# Fase 2b — Superficie completa de la API REST — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Exponer por HTTP todos los casos de uso de la capa de aplicación de StockApp (movimientos, reportes, auditoría, usuarios, catálogo completo) con políticas de autorización derivadas de `AuthorizationService` — sin duplicar la tabla rol→permiso — y manejo de errores centralizado.

**Architecture:** Un archivo de endpoints por recurso en `src/StockApp.Api/Endpoints/`. Cada endpoint inyecta el servicio de aplicación existente y delega — la API es un adaptador HTTP sin lógica de negocio. Las políticas de autorización se derivan en `Program.cs` iterando `Permisos.Todos` contra un nuevo método de consulta `IAuthorizationService.TienePermiso(rol, accion)`. Los errores de negocio se traducen a HTTP en un `IExceptionHandler` central (`DomainExceptionHandler`) — los endpoints no hacen try/catch.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, JWT Bearer (ya implementado en Fase 2a), xUnit + WebApplicationFactory + Testcontainers.PostgreSql para integración, xUnit + Moq para la capa de aplicación.

## Global Constraints

- Target framework `net10.0` en todos los proyectos (sin cambios de proyecto: no se crean `.csproj` nuevos en esta fase).
- Minimal APIs con `MapGroup` + métodos de extensión `MapXxxEndpoints`; **no Controllers**.
- **NO se toca la app desktop**: `StockApp.Presentation`, `App.axaml.cs`, `InMemorySession` quedan exactamente como están.
- **Reusar** `IXxxService`/repositorios/DTOs existentes tal cual — **única excepción**: `IUsuarioService.ListarAsync()` se agrega (D6 del spec) porque no existe y es indispensable para `GET /usuarios`.
- Contratos HTTP (request/response) calcan los DTOs de `StockApp.Application/` — cero traducción de modelos.
- Políticas de autorización HTTP derivadas de `AuthorizationService` (D1 del spec) — nunca declaradas a mano por recurso.
- Defensa en profundidad (D2 del spec): la política HTTP es la primera barrera; los servicios de aplicación conservan su `Verificar(rol, permiso)` interno sin cambios.
- Convención de status HTTP en esta fase (no estaba en el spec, se fija acá para tener un criterio uniforme): endpoints que **crean** un recurso (`POST /movimientos`, `POST /productos`, `POST /categorias`, `POST /proveedores`, `POST /unidades-medida`, `POST /usuarios`) devuelven **201 Created**; el resto (`PUT`, `DELETE`, endpoints de acción como `recalcular-stock`) devuelve **200 OK**.
- Manejo de errores centralizado en un `IExceptionHandler` (`DomainExceptionHandler`) — los endpoints no hacen try/catch propio.
- Tests de integración contra **Postgres real vía Testcontainers** (mismo patrón que Fase 2a: `ApiFactory` + `ApiTestBase` + `TRUNCATE ... RESTART IDENTITY CASCADE` entre tests).
- Tests de aplicación (Bloque A, Task 4) con **Moq**, mismo patrón que `UsuarioServiceTests.cs`/`CategoriaServiceTests.cs` existentes.
- Conventional Commits, **sin** `Co-Authored-By`.
- TDD: test rojo → implementación mínima → test verde → commit.
- Commits frecuentes (uno por task, al cierre de cada task).

---

## Bloque A — Cimientos

## Task 1: `Permisos.Todos`

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs`

**Interfaces:**
- Produces: `Permisos.Todos: IReadOnlyList<string>` — consumido por `Program.cs` (Task 3) para derivar las políticas HTTP y por el test de cierre del enfoque B (Task 3).

- [ ] **Step 1: Escribir el test que falla**

```csharp
using StockApp.Application.Authorization;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class PermisosTests
{
    [Fact]
    public void Todos_ContieneLasSeisConstantesExactas()
    {
        var esperados = new[]
        {
            Permisos.GestionarUsuarios,
            Permisos.VerReportes,
            Permisos.GestionarProductos,
            Permisos.GestionarTablasMaestras,
            Permisos.RegistrarMovimientos,
            Permisos.RecalcularStock,
        };

        Assert.Equal(esperados.Length, Permisos.Todos.Count);
        foreach (var permiso in esperados)
            Assert.Contains(permiso, Permisos.Todos);
    }

    [Fact]
    public void Todos_NoTieneDuplicados()
    {
        Assert.Equal(Permisos.Todos.Count, Permisos.Todos.Distinct().Count());
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter PermisosTests`
Expected: FAIL — error de compilación (`Permisos.Todos` no existe todavía).

- [ ] **Step 3: Implementar `Permisos.Todos`**

Reemplazar el contenido completo de `src/StockApp.Application/Authorization/Permisos.cs`:

```csharp
namespace StockApp.Application.Authorization;

/// <summary>
/// Nombres canónicos de las acciones protegidas del sistema.
/// Todos los servicios de Application usan estas constantes al llamar a IAuthorizationService.
/// </summary>
public static class Permisos
{
    public const string GestionarUsuarios       = "usuarios.gestionar";
    public const string VerReportes             = "reportes.ver";
    public const string GestionarProductos      = "catalogo.productos";
    public const string GestionarTablasMaestras = "catalogo.maestras";
    public const string RegistrarMovimientos    = "movimientos.registrar";
    public const string RecalcularStock         = "stock.recalcular";

    /// <summary>
    /// Lista explícita de todos los permisos del sistema (sin reflection). Consumida por
    /// StockApp.Api/Program.cs (Fase 2b, D1) para derivar las políticas de autorización
    /// HTTP a partir de AuthorizationService, en vez de declararlas a mano por recurso.
    /// </summary>
    public static readonly IReadOnlyList<string> Todos =
    [
        GestionarUsuarios,
        VerReportes,
        GestionarProductos,
        GestionarTablasMaestras,
        RegistrarMovimientos,
        RecalcularStock,
    ];
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter PermisosTests`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Application/Authorization/Permisos.cs tests/StockApp.Application.Tests/Authorization/PermisosTests.cs
git commit -m "feat(application): agrega Permisos.Todos para derivar politicas HTTP"
```

---

## Task 2: `AuthorizationService.TienePermiso` (consulta sin lanzar)

**Decisión de diseño:** `AuthorizationService.AccionesOperador` es un `HashSet<string> private static readonly` — no hay forma de leer la tabla rol→permiso desde afuera sin agregar un método. `Verificar(rol, accion)` lanza en vez de devolver `bool`, así que no sirve para "consultar sin lanzar" (usarlo con try/catch en un loop de arranque de `Program.cs` sería un anti-patrón). Se agrega `TienePermiso(RolUsuario rol, string accion): bool` a `IAuthorizationService`, que reusa la misma tabla interna. `Verificar` no se toca — los servicios de aplicación existentes siguen exactamente igual.

**Files:**
- Modify: `src/StockApp.Application/Authorization/IAuthorizationService.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`

**Interfaces:**
- Produces: `IAuthorizationService.TienePermiso(RolUsuario rol, string accion): bool` — consumido por `Program.cs` (Task 3) para derivar las políticas HTTP.

- [ ] **Step 1: Agregar los tests que fallan a `AuthorizationServiceTests.cs`**

Agregar al final de la clase `AuthorizationServiceTests` (en `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`, antes del `}` de cierre):

```csharp

    // ── TienePermiso (Fase 2b, D1): consulta sin lanzar, misma tabla que Verificar ──

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarProductos)]
    [InlineData(Permisos.GestionarTablasMaestras)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    public void TienePermiso_Admin_DevuelveTrueParaTodo(string accion)
    {
        Assert.True(_svc.TienePermiso(RolUsuario.Admin, accion));
    }

    [Theory]
    [InlineData(Permisos.GestionarProductos)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    public void TienePermiso_Operador_DevuelveTrueParaAccionesOperativas(string accion)
    {
        Assert.True(_svc.TienePermiso(RolUsuario.Operador, accion));
    }

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarTablasMaestras)]
    public void TienePermiso_Operador_DevuelveFalseParaAccionesDeAdmin(string accion)
    {
        Assert.False(_svc.TienePermiso(RolUsuario.Operador, accion));
    }

    [Fact]
    public void TienePermiso_NuncaLanza_ADiferenciaDeVerificar()
    {
        var ex = Record.Exception(() => _svc.TienePermiso(RolUsuario.Operador, Permisos.GestionarUsuarios));
        Assert.Null(ex);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AuthorizationServiceTests`
Expected: FAIL — error de compilación (`TienePermiso` no existe todavía en `AuthorizationService`).

- [ ] **Step 3: Agregar `TienePermiso` a `IAuthorizationService`**

Reemplazar el contenido completo de `src/StockApp.Application/Authorization/IAuthorizationService.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Guard de autorización por rol. Cada servicio de Application llama a
/// <see cref="Verificar"/> al inicio de los métodos que requieren permiso.
/// </summary>
public interface IAuthorizationService
{
    /// <summary>
    /// Verifica que <paramref name="rolActual"/> puede ejecutar <paramref name="accion"/>.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso o no hay sesión.</exception>
    void Verificar(RolUsuario? rolActual, string accion);

    /// <summary>
    /// Igual que <see cref="Verificar"/> pero sin lanzar: devuelve si <paramref name="rol"/>
    /// puede ejecutar <paramref name="accion"/>, consultando la misma tabla rol→permiso.
    /// Usado por StockApp.Api/Program.cs (Fase 2b, D1) para derivar las políticas de
    /// autorización HTTP a partir de esta única fuente de verdad, en vez de declararlas
    /// a mano por recurso.
    /// </summary>
    bool TienePermiso(RolUsuario rol, string accion);
}
```

- [ ] **Step 4: Implementar `TienePermiso` en `AuthorizationService`**

Reemplazar el contenido completo de `src/StockApp.Application/Authorization/AuthorizationService.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Authorization;

/// <summary>
/// Implementación simple de <see cref="IAuthorizationService"/>:
/// tabla de acciones permitidas por rol. Admin tiene acceso a todo; Operador solo
/// a las acciones operativas (catálogo, movimientos, recálculo).
/// </summary>
public class AuthorizationService : IAuthorizationService
{
    // Acciones habilitadas para Operador. VerReportes, GestionarUsuarios y
    // GestionarTablasMaestras están deliberadamente AUSENTES: son exclusivas de
    // Admin (fail-closed por diseño). Operador puede gestionar productos pero
    // NO tablas maestras (Categoria/Proveedor/UnidadMedida).
    private static readonly HashSet<string> AccionesOperador =
    [
        Permisos.GestionarProductos,
        Permisos.RegistrarMovimientos,
        Permisos.RecalcularStock,
    ];

    public void Verificar(RolUsuario? rolActual, string accion)
    {
        if (rolActual is null)
            throw new UnauthorizedAccessException("No hay sesión activa.");

        if (rolActual == RolUsuario.Admin)
            return; // Admin puede todo

        if (!AccionesOperador.Contains(accion))
            throw new UnauthorizedAccessException(
                $"El rol Operador no tiene permiso para ejecutar la acción '{accion}'.");
    }

    public bool TienePermiso(RolUsuario rol, string accion) =>
        rol == RolUsuario.Admin || AccionesOperador.Contains(accion);
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AuthorizationServiceTests`
Expected: PASS (todas — las originales + las 4 nuevas de `TienePermiso`)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Authorization tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs
git commit -m "feat(application): agrega IAuthorizationService.TienePermiso para consultar sin lanzar"
```

---

## Task 3: Políticas HTTP derivadas en `Program.cs` + registros DI de catálogo/auditoría/usuarios

**Files:**
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/PoliticasDerivadasTests.cs`

**Interfaces:**
- Consumes: `Permisos.Todos` (Task 1), `IAuthorizationService.TienePermiso` (Task 2).
- Consumes: `ICategoriaService`+`ICategoriaRepository`/`CategoriaRepository`, `IProveedorService`+`IProveedorRepository`/`ProveedorRepository`, `IUnidadMedidaService`+`IUnidadMedidaRepository`/`UnidadMedidaRepository` (ya registrado desde 2a), `IAuditoriaQueryService`+`IAuditoriaQueryRepository`/`AuditoriaQueryRepository`, `IUsuarioService`/`UsuarioService` (existentes en `StockApp.Application`/`StockApp.Infrastructure`).
- Produces: bloque `AddAuthorization` derivado — consumido transitivamente por TODOS los endpoints de Bloque C (ninguno declara su propia política; todos usan `.RequireAuthorization(Permisos.X)`).

- [ ] **Step 1: Escribir el test que falla — `PoliticasDerivadasTests.cs`**

```csharp
using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// Test de cierre del enfoque B (spec Fase 2b, D1): itera Permisos.Todos y verifica que
/// cada política registrada en la API autoriza EXACTAMENTE los roles que
/// AuthorizationService.TienePermiso dicta — ni más ni menos. Si alguien agrega un
/// permiso nuevo y se olvida de que el loop de Program.cs lo cubre automáticamente
/// (o si AuthorizationService cambia la tabla rol→permiso), este test lo detecta.
/// </summary>
public class PoliticasDerivadasTests : ApiTestBase
{
    public PoliticasDerivadasTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task CadaPoliticaRegistrada_AutorizaExactamenteLosRolesQueAuthorizationServiceDicta()
    {
        var provider = Factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var authService = new AuthorizationService();

        foreach (var permiso in Permisos.Todos)
        {
            var policy = await provider.GetPolicyAsync(permiso);
            Assert.True(policy is not null, $"No hay política registrada para el permiso '{permiso}'.");

            var requirement = policy!.Requirements.OfType<ClaimsAuthorizationRequirement>().Single();
            var rolesEnPolitica = requirement.AllowedValues!.ToHashSet();

            var rolesEsperados = Enum.GetValues<RolUsuario>()
                .Where(rol => authService.TienePermiso(rol, permiso))
                .Select(rol => rol.ToString())
                .ToHashSet();

            Assert.True(
                rolesEsperados.SetEquals(rolesEnPolitica),
                $"Política '{permiso}': esperados [{string.Join(",", rolesEsperados)}], " +
                $"registrados [{string.Join(",", rolesEnPolitica)}].");
        }
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PoliticasDerivadasTests`
Expected: FAIL — hoy solo hay 2 políticas registradas a mano (`GestionarProductos`, `VerReportes`); las otras 4 permisos (`GestionarUsuarios`, `GestionarTablasMaestras`, `RegistrarMovimientos`, `RecalcularStock`) no tienen política y `GetPolicyAsync` devuelve `null`.

- [ ] **Step 3: Reemplazar el bloque `AddAuthorization` de `Program.cs` por el loop derivado**

En `src/StockApp.Api/Program.cs`, reemplazar:

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

por:

```csharp
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
```

(`AuthorizationService` no tiene dependencias — constructor sin parámetros — por eso se puede instanciar directamente acá, antes de que el contenedor DI esté armado, solo para derivar las políticas. La instancia registrada como singleton vía `AddSingleton<IAuthorizationService, AuthorizationService>()` sigue siendo la que usan los servicios de aplicación en runtime; esta es una instancia auxiliar de solo lectura de la tabla.)

- [ ] **Step 4: Agregar los `using` que falten**

Al principio de `Program.cs`, confirmar/agregar (ya está `using StockApp.Application.Authorization;` desde 2a; agregar los siguientes si no están):

```csharp
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
```

- [ ] **Step 5: Registrar en DI los servicios/repositorios de catálogo maestro, auditoría y usuarios**

Agregar, después del bloque de DI de reportes (`builder.Services.AddScoped<IReporteStockService, ReporteStockService>();`):

```csharp
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
```

- [ ] **Step 6: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter PoliticasDerivadasTests`
Expected: PASS (1 test)

- [ ] **Step 7: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — las políticas derivadas producen los mismos roles que las 2 políticas manuales que reemplazan, así que `ProductosEndpointTests`/`ReporteValorizacionEndpointTests`/`ProblemDetailsTests` siguen en verde).

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/PoliticasDerivadasTests.cs
git commit -m "feat(api): deriva politicas de autorizacion desde AuthorizationService y registra DI de catalogo/auditoria/usuarios"
```

---

## Task 4: `IUsuarioService.ListarAsync()` + `UsuarioDto`

**Files:**
- Create: `src/StockApp.Application/Auth/Dtos.cs`
- Modify: `src/StockApp.Application/Auth/IUsuarioService.cs`
- Modify: `src/StockApp.Application/Auth/UsuarioService.cs`
- Test: `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`

**Interfaces:**
- Produces: `record UsuarioDto(int Id, string NombreUsuario, string? NombreCompleto, RolUsuario Rol, bool Activo, DateTime FechaAlta)` — nunca expone `HashContrasena`. Consumido por `UsuariosEndpoints` (Bloque C, Task 9).
- Produces: `IUsuarioService.ListarAsync(): Task<IReadOnlyList<UsuarioDto>>` — verifica `Permisos.GestionarUsuarios` internamente, mismo patrón que sus hermanos (`AltaUsuarioAsync`, etc.).

- [ ] **Step 1: Agregar los tests que fallan a `UsuarioServiceTests.cs`**

Agregar al final de la clase `UsuarioServiceTests` (antes del `}` de cierre):

```csharp

    // ── ListarAsync (Fase 2b, D6) ───────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_Admin_DevuelveDtosSinHashContrasena()
    {
        var (svc, repo, _, _, _, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<Usuario>
        {
            new()
            {
                Id = 1, NombreUsuario = "admin", NombreCompleto = "Admin Uno",
                HashContrasena = "hash-secreto", Rol = RolUsuario.Admin,
                Activo = true, FechaAlta = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            },
            new()
            {
                Id = 2, NombreUsuario = "operador1", NombreCompleto = null,
                HashContrasena = "otro-hash", Rol = RolUsuario.Operador,
                Activo = false, FechaAlta = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            },
        });

        var resultado = await svc.ListarAsync();

        Assert.Equal(2, resultado.Count);
        Assert.Contains(resultado, u => u.Id == 1 && u.NombreUsuario == "admin"
            && u.NombreCompleto == "Admin Uno" && u.Rol == RolUsuario.Admin && u.Activo);
        Assert.Contains(resultado, u => u.Id == 2 && u.NombreUsuario == "operador1"
            && u.NombreCompleto == null && u.Rol == RolUsuario.Operador && !u.Activo);
    }

    [Fact]
    public async Task ListarAsync_Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _, _) = Crear(rolSesion: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarAsync());
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UsuarioServiceTests`
Expected: FAIL — error de compilación (`ListarAsync`/`UsuarioDto` no existen todavía).

- [ ] **Step 3: Crear `src/StockApp.Application/Auth/Dtos.cs`**

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// DTO de lectura de Usuario para GET /usuarios (Fase 2b). Nunca incluye
/// HashContrasena — ese campo no sale de la capa de aplicación.
/// </summary>
public record UsuarioDto(
    int Id,
    string NombreUsuario,
    string? NombreCompleto,
    RolUsuario Rol,
    bool Activo,
    DateTime FechaAlta);
```

- [ ] **Step 4: Agregar `ListarAsync` a `IUsuarioService`**

Reemplazar el contenido completo de `src/StockApp.Application/Auth/IUsuarioService.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>Contrato del ABM de usuarios. Permite mockear UsuarioService en tests de Presentation.</summary>
public interface IUsuarioService
{
    Task AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol);
    Task BajaLogicaAsync(int usuarioId);
    Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol);
    Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null);

    /// <summary>Lista todos los usuarios (activos e inactivos). Requiere GestionarUsuarios (Fase 2b).</summary>
    Task<IReadOnlyList<UsuarioDto>> ListarAsync();
}
```

- [ ] **Step 5: Implementar `ListarAsync` en `UsuarioService`**

Agregar al final de la clase `UsuarioService` (en `src/StockApp.Application/Auth/UsuarioService.cs`, antes del `}` de cierre):

```csharp

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuarios = await _repo.ListarTodosAsync();
        return usuarios.Select(AUsuarioDto).ToList();
    }

    private static UsuarioDto AUsuarioDto(Usuario u) => new UsuarioDto(
        Id:             u.Id,
        NombreUsuario:  u.NombreUsuario,
        NombreCompleto: u.NombreCompleto,
        Rol:            u.Rol,
        Activo:         u.Activo,
        FechaAlta:      u.FechaAlta);
```

Agregar `using System.Linq;` al principio del archivo si no está presente (necesario para `.Select().ToList()`):

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter UsuarioServiceTests`
Expected: PASS (todas — las originales + las 2 nuevas de `ListarAsync`)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Auth tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
git commit -m "feat(application): agrega IUsuarioService.ListarAsync y UsuarioDto"
```

---

## Bloque B — Manejo de errores

## Task 5: `DomainExceptionHandler` (mapeo centralizado excepción→HTTP)

**Files:**
- Create: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: `StockApp.Domain.Exceptions.StockInsuficienteException` (única excepción custom del dominio; el resto de los errores de negocio usa `ArgumentException`/`InvalidOperationException`/`KeyNotFoundException`/`UnauthorizedAccessException` del BCL — confirmado por inspección de `MovimientoStockService`, `ProductoService`, `CategoriaService`, `ProveedorService`, `UnidadMedidaService`, `UsuarioService`).
- Produces: `IExceptionHandler DomainExceptionHandler` — registrado vía `AddExceptionHandler<DomainExceptionHandler>()`, activado por el `app.UseExceptionHandler()` ya existente desde Fase 2a. Ningún endpoint de Bloque C hace try/catch: cualquier excepción no capturada llega acá.

**Tabla de mapeo (spec, sección "Manejo de errores"):**

| Tipo de excepción | HTTP | Motivo |
|---|---|---|
| `StockInsuficienteException` | 409 | Regla de negocio (stock insuficiente sin `forzar`) |
| `InvalidOperationException` | 409 | Regla de negocio (código/nombre duplicado, entidad ya inactiva, último Admin, auto-baja) |
| `KeyNotFoundException` | 404 | Entidad inexistente |
| `ArgumentException` | 400 | Input inválido |
| `UnauthorizedAccessException` | 403 | Acceso denegado por el servicio (2ª barrera, D2) |
| Cualquier otra | 500 | Genérico, sin `exception.Message` en el body (fail-closed) |

- [ ] **Step 1: Escribir los tests que fallan — `DomainExceptionHandlerTests.cs`**

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.ErrorHandling;
using StockApp.Domain.Exceptions;
using System.Text.Json;
using Xunit;

namespace StockApp.Api.Tests.ErrorHandling;

/// <summary>
/// Test unitario del handler, sin WebApplicationFactory: arma un DefaultHttpContext
/// con un IProblemDetailsService real (via ServiceCollection mínimo) e invoca
/// TryHandleAsync directamente. Mismo espíritu que JwtTokenServiceTests (Fase 2a,
/// Task 2): no hace falta un host HTTP completo para probar una unidad aislada.
/// </summary>
public class DomainExceptionHandlerTests
{
    private static async Task<(int Status, string ContentType, JsonDocument Body)> EjecutarAsync(Exception excepcion)
    {
        var services = new ServiceCollection();
        services.AddProblemDetails();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            Response = { Body = new MemoryStream() },
        };

        var handler = new DomainExceptionHandler();
        var manejada = await handler.TryHandleAsync(context, excepcion, CancellationToken.None);

        Assert.True(manejada);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var texto = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, context.Response.ContentType!, JsonDocument.Parse(texto));
    }

    [Fact]
    public async Task StockInsuficienteException_Mapea409()
    {
        var (status, contentType, body) = await EjecutarAsync(
            new StockInsuficienteException(1, 5m, 10m));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.StartsWith("application/problem+json", contentType);
        Assert.Equal(409, body.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task InvalidOperationException_Mapea409()
    {
        var (status, _, _) = await EjecutarAsync(new InvalidOperationException("ya existe"));
        Assert.Equal(StatusCodes.Status409Conflict, status);
    }

    [Fact]
    public async Task KeyNotFoundException_Mapea404()
    {
        var (status, _, _) = await EjecutarAsync(new KeyNotFoundException("no existe"));
        Assert.Equal(StatusCodes.Status404NotFound, status);
    }

    [Fact]
    public async Task ArgumentException_Mapea400()
    {
        var (status, _, _) = await EjecutarAsync(new ArgumentException("dato invalido"));
        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task UnauthorizedAccessException_Mapea403()
    {
        var (status, _, _) = await EjecutarAsync(new UnauthorizedAccessException("sin permiso"));
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task ExcepcionGenerica_Mapea500SinExponerElMensajeInterno()
    {
        var (status, _, body) = await EjecutarAsync(new Exception("detalle interno sensible"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        var tieneDetail = body.RootElement.TryGetProperty("detail", out var detalle);
        if (tieneDetail)
            Assert.DoesNotContain("detalle interno sensible", detalle.GetString());
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: FAIL — error de compilación (`DomainExceptionHandler` no existe todavía).

- [ ] **Step 3: Implementar `DomainExceptionHandler.cs`**

```csharp
using Microsoft.AspNetCore.Diagnostics;
using StockApp.Domain.Exceptions;

namespace StockApp.Api.ErrorHandling;

/// <summary>
/// Mapeo centralizado de excepciones de dominio/aplicación a status HTTP + ProblemDetails
/// (Fase 2b, sección "Manejo de errores" del spec). Los endpoints no hacen try/catch:
/// cualquier excepción no capturada por Minimal API llega acá vía app.UseExceptionHandler().
/// </summary>
public class DomainExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            StockInsuficienteException  => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            InvalidOperationException   => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            KeyNotFoundException        => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ArgumentException           => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            UnauthorizedAccessException => (StatusCodes.Status403Forbidden, "Prohibido."),
            _                           => (StatusCodes.Status500InternalServerError, "Error interno."),
        };

        httpContext.Response.StatusCode = status;
        httpContext.Response.ContentType = "application/problem+json";

        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            Exception = exception,
            ProblemDetails =
            {
                Status = status,
                Title = title,
                // 500: nunca exponer exception.Message (fail-closed, spec "Manejo de errores").
                Detail = status == StatusCodes.Status500InternalServerError ? null : exception.Message,
            },
        });
    }
}
```

Agregar `using Microsoft.Extensions.DependencyInjection;` al principio del archivo (necesario para `GetRequiredService`):

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Domain.Exceptions;

namespace StockApp.Api.ErrorHandling;
```

- [ ] **Step 4: Registrar el handler en `Program.cs`**

Agregar el `using` al principio de `src/StockApp.Api/Program.cs`:

```csharp
using StockApp.Api.ErrorHandling;
```

Agregar, inmediatamente antes de `builder.Services.AddProblemDetails();`:

```csharp
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
```

(El `app.UseExceptionHandler();` que ya existe desde Fase 2a, Task 6, no cambia — al llamarse sin delegate, ASP.NET Core usa la cadena de `IExceptionHandler` registrados, que ahora incluye `DomainExceptionHandler`.)

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: PASS (6 tests)

- [ ] **Step 6: Correr toda la suite de `StockApp.Api.Tests` para verificar que nada se rompió**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/ErrorHandling src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/ErrorHandling
git commit -m "feat(api): mapeo centralizado de excepciones a status HTTP via DomainExceptionHandler"
```

---

## Bloque C — Endpoints

## Task 6: `MovimientosEndpoints` (registrar, historial, recalcular stock)

**Files:**
- Create: `src/StockApp.Api/Endpoints/MovimientosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/MovimientosEndpointTests.cs`

**Interfaces:**
- Consumes: `IMovimientoStockService.RegistrarAsync(RegistrarMovimientoDto, bool forzar): Task<MovimientoRegistradoDto>`, `.ObtenerHistorialAsync(HistorialMovimientoFiltro): Task<IReadOnlyList<MovimientoHistorialDto>>`, `.RecalcularStockAsync(int): Task<RecalculoResultadoDto>` (existentes, `StockApp.Application.Movimientos`).
- Produces: `record RegistrarMovimientoRequest(int ProductoId, TipoMovimiento Tipo, MotivoMovimiento Motivo, decimal Cantidad, decimal? PrecioUnitario, string? Comentario, bool Forzar = false)` — público en `StockApp.Api.Endpoints`, consumido por los tests. `Forzar` viaja separado de `RegistrarMovimientoDto` porque el DTO de aplicación no lo incluye (es el segundo parámetro de `RegistrarAsync`).

- [ ] **Step 1: Extender `DatosDePrueba.cs` con seed de Producto con stock configurable**

Agregar al final de la clase `DatosDePrueba` (en `tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs`, antes del `}` de cierre):

```csharp

    public static async Task<Producto> SeedProductoConStockAsync(
        AppDbContext ctx, string codigo, string nombre, decimal stockActual)
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
            StockActual = stockActual,
            StockMinimo = 0m,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };

        ctx.Productos.Add(producto);
        await ctx.SaveChangesAsync();
        return producto;
    }
```

- [ ] **Step 2: Escribir los tests que fallan — `MovimientosEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class MovimientosEndpointTests : ApiTestBase
{
    public MovimientosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── POST /movimientos ────────────────────────────────────────────────────

    [Fact]
    public async Task PostMovimientos_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(1, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostMovimientos_ConTokenOperador_RegistraEntradaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M1", "Producto Mov 1", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, "Compra test"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var registrado = await response.Content.ReadFromJsonAsync<MovimientoRegistradoDto>();
        Assert.Equal(15m, registrado!.StockNuevo);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(15m, actualizado.StockActual);
    }

    [Fact]
    public async Task PostMovimientos_SalidaMayorAlStock_SinForzar_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M2", "Producto Mov 2", 3m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Salida, MotivoMovimiento.Venta, 10m, 20m, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostMovimientos_SalidaMayorAlStock_ConForzar_Devuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M3", "Producto Mov 3", 3m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Salida, MotivoMovimiento.Venta, 10m, 20m, null, Forzar: true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ── GET /movimientos/historial ───────────────────────────────────────────

    [Fact]
    public async Task GetHistorial_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/movimientos/historial");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ConTokenOperador_FiltraPorProductoId()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M4", "Producto Mov 4", 10m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        await client.PostAsJsonAsync("/movimientos",
            new RegistrarMovimientoRequest(producto.Id, TipoMovimiento.Entrada, MotivoMovimiento.Compra, 5m, 10m, null));

        var response = await client.GetAsync($"/movimientos/historial?productoId={producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historial = await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>();
        Assert.Single(historial!);
        Assert.Equal(producto.Id, historial![0].ProductoId);
    }

    // ── POST /productos/{id}/recalcular-stock ────────────────────────────────

    [Fact]
    public async Task PostRecalcularStock_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync("/productos/1/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRecalcularStock_ConTokenOperador_RecalculaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoConStockAsync(ctx, "SKU-M5", "Producto Mov 5", 999m);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsync($"/productos/{producto.Id}/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<RecalculoResultadoDto>();
        Assert.Equal(0m, resultado!.StockNuevo); // sin movimientos previos: neto = 0

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(0m, actualizado.StockActual);
    }

    [Fact]
    public async Task PostRecalcularStock_ProductoInexistente_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsync("/productos/99999/recalcular-stock", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter MovimientosEndpointTests`
Expected: FAIL — error de compilación (`RegistrarMovimientoRequest` no existe) y/o `404 Not Found` en las rutas.

- [ ] **Step 4: Implementar `MovimientosEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

/// <summary>
/// Request de POST /movimientos: calca RegistrarMovimientoDto (StockApp.Application)
/// y agrega Forzar, que en la capa de aplicación viaja como segundo parámetro de
/// IMovimientoStockService.RegistrarAsync en vez de dentro del DTO.
/// </summary>
public record RegistrarMovimientoRequest(
    int ProductoId,
    TipoMovimiento Tipo,
    MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal? PrecioUnitario,
    string? Comentario,
    bool Forzar = false);

public static class MovimientosEndpoints
{
    public static IEndpointRouteBuilder MapMovimientosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/movimientos");

        group.MapPost("/", async (RegistrarMovimientoRequest request, IMovimientoStockService movimientos) =>
        {
            var dto = new RegistrarMovimientoDto(
                request.ProductoId, request.Tipo, request.Motivo,
                request.Cantidad, request.PrecioUnitario, request.Comentario);

            var registrado = await movimientos.RegistrarAsync(dto, request.Forzar);
            return Results.Created($"/movimientos/{registrado.MovimientoId}", registrado);
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        group.MapGet("/historial", async (
            int? productoId, TipoMovimiento? tipo, DateTime? fechaDesde, DateTime? fechaHasta,
            IMovimientoStockService movimientos) =>
        {
            var filtro = new HistorialMovimientoFiltro(productoId, tipo, fechaDesde, fechaHasta);
            return Results.Ok(await movimientos.ObtenerHistorialAsync(filtro));
        })
        .RequireAuthorization(Permisos.RegistrarMovimientos);

        return app;
    }
}
```

- [ ] **Step 5: Agregar `POST /productos/{id}/recalcular-stock` a `ProductosEndpoints.cs`**

Agregar, dentro de `MapProductosEndpoints` (en `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`), después del `group.MapGet("/", ...)` existente:

```csharp
        group.MapPost("/{id:int}/recalcular-stock", async (int id, IMovimientoStockService movimientos) =>
            Results.Ok(await movimientos.RecalcularStockAsync(id)))
            .RequireAuthorization(Permisos.RecalcularStock);
```

Agregar el `using StockApp.Application.Movimientos;` al principio del archivo si no está (ya está presente desde Fase 2a por el reporte de valorización, que también usa `StockApp.Application.Reportes` — confirmar y dejar ambos).

- [ ] **Step 6: Registrar `MapMovimientosEndpoints()` en `Program.cs`**

Agregar el `using StockApp.Api.Endpoints;` (ya presente). Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapMovimientosEndpoints();
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter MovimientosEndpointTests`
Expected: PASS (9 tests)

- [ ] **Step 8: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Api/Endpoints tests/StockApp.Api.Tests/MovimientosEndpointTests.cs tests/StockApp.Api.Tests/Fixtures/DatosDePrueba.cs
git commit -m "feat(api): endpoints de movimientos (registrar, historial) y recalcular-stock"
```

---

## Task 7: `ReportesEndpoints` (muda valorización desde /productos + 3 endpoints nuevos)

**Files:**
- Create: `src/StockApp.Api/Endpoints/ReportesEndpoints.cs`
- Modify: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs` (elimina `/productos/reporte-valorizacion`)
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `tests/StockApp.Api.Tests/ProblemDetailsTests.cs` (la ruta que probaba 403 se movió)
- Delete: `tests/StockApp.Api.Tests/ReporteValorizacionEndpointTests.cs` (reemplazado por `ReportesEndpointTests.cs`)
- Test: `tests/StockApp.Api.Tests/ReportesEndpointTests.cs`

**Interfaces:**
- Consumes: `IReporteStockService.ObtenerValorizacionAsync(): Task<ValorizacionReporteDto>`, `.ObtenerStockPorCategoriaAsync(): Task<IReadOnlyList<StockCategoriaDto>>`, `.ObtenerMasMovidosAsync(DateTime?, DateTime?, int topN = 20): Task<IReadOnlyList<MasMovidoDto>>`, `.ObtenerHistorialPorProductoAsync(int, DateTime?, DateTime?): Task<IReadOnlyList<MovimientoHistorialDto>>` (existentes, `StockApp.Application.Reportes`).

**D3 (breaking change deliberado):** `GET /productos/reporte-valorizacion` desaparece. No hay clientes hoy — el costo es cero.

- [ ] **Step 1: Actualizar `ProblemDetailsTests.cs` para usar la ruta nueva**

En `tests/StockApp.Api.Tests/ProblemDetailsTests.cs`, reemplazar el método `TokenOperador_EnEndpointSoloAdmin_Devuelve403ComoProblemDetails`:

```csharp
    [Fact]
    public async Task TokenOperador_EnEndpointSoloAdmin_Devuelve403ComoProblemDetails()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.StartsWith("application/problem+json", response.Content.Headers.ContentType!.ToString());

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(403, doc.RootElement.GetProperty("status").GetInt32());
    }
```

- [ ] **Step 2: Borrar `ReporteValorizacionEndpointTests.cs`**

```bash
rm tests/StockApp.Api.Tests/ReporteValorizacionEndpointTests.cs
```

- [ ] **Step 3: Escribir los tests que fallan — `ReportesEndpointTests.cs`**

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

public class ReportesEndpointTests : ApiTestBase
{
    public ReportesEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── GET /reportes/valorizacion ───────────────────────────────────────────

    [Fact]
    public async Task GetValorizacion_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetValorizacion_ConTokenAdmin_Devuelve200ConValorizacion()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R1", "Producto Reporte 1");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/valorizacion");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>();
        Assert.Contains(reporte!.Items, i => i.Codigo == "SKU-R1");
    }

    [Fact]
    public async Task GetProductosReporteValorizacion_RutaVieja_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/productos/reporte-valorizacion");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── GET /reportes/stock-por-categoria ────────────────────────────────────

    [Fact]
    public async Task GetStockPorCategoria_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R2", "Producto Reporte 2");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<StockCategoriaDto>>());
    }

    [Fact]
    public async Task GetStockPorCategoria_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/stock-por-categoria");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /reportes/mas-movidos ────────────────────────────────────────────

    [Fact]
    public async Task GetMasMovidos_ConTokenAdmin_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/mas-movidos?topN=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>());
    }

    // ── GET /reportes/historial-producto/{productoId} ────────────────────────

    [Fact]
    public async Task GetHistorialProducto_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-R3", "Producto Reporte 3");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/reportes/historial-producto/{producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>());
    }

    [Fact]
    public async Task GetHistorialProducto_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/reportes/historial-producto/1");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ReportesEndpointTests`
Expected: FAIL — `404 Not Found` en todas las rutas `/reportes/*` (no existen todavía).

- [ ] **Step 5: Implementar `ReportesEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Reportes;

namespace StockApp.Api.Endpoints;

public static class ReportesEndpoints
{
    public static IEndpointRouteBuilder MapReportesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reportes").RequireAuthorization(Permisos.VerReportes);

        group.MapGet("/valorizacion", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerValorizacionAsync()));

        group.MapGet("/stock-por-categoria", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerStockPorCategoriaAsync()));

        group.MapGet("/mas-movidos", async (
            DateTime? fechaDesde, DateTime? fechaHasta, int topN, IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerMasMovidosAsync(fechaDesde, fechaHasta, topN == 0 ? 20 : topN)));

        group.MapGet("/historial-producto/{productoId:int}", async (
            int productoId, DateTime? fechaDesde, DateTime? fechaHasta, IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerHistorialPorProductoAsync(productoId, fechaDesde, fechaHasta)));

        return app;
    }
}
```

(`topN == 0 ? 20 : topN`: Minimal API no soporta un valor default de C# en un parámetro `int` bindeado desde query string cuando el query string no lo provee — sin `?`, el binder exige el parámetro o falla con 400. Se usa `int topN` con un chequeo manual para replicar el default `20` de `IReporteStockService.ObtenerMasMovidosAsync`, en vez de exigir que el cliente HTTP siempre mande `topN`.)

- [ ] **Step 6: Eliminar `GET /productos/reporte-valorizacion` de `ProductosEndpoints.cs`**

En `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`, eliminar el bloque:

```csharp
        group.MapGet("/reporte-valorizacion", async (IReporteStockService reportes) =>
            Results.Ok(await reportes.ObtenerValorizacionAsync()))
            .RequireAuthorization(Permisos.VerReportes);
```

Si `using StockApp.Application.Reportes;` queda sin otro uso en el archivo después de este borrado, eliminarlo también (verificar: el `POST /{id}/recalcular-stock` de Task 6 usa `IMovimientoStockService`, no `IReporteStockService` — sí queda sin uso, eliminar el using).

- [ ] **Step 7: Registrar `MapReportesEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapReportesEndpoints();
```

- [ ] **Step 8: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "ReportesEndpointTests|ProblemDetailsTests"`
Expected: PASS (todas)

- [ ] **Step 9: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(api): endpoints de reportes bajo /reportes y elimina /productos/reporte-valorizacion (breaking change D3)"
```

---

## Task 8: `AuditoriaEndpoints`

**Files:**
- Create: `src/StockApp.Api/Endpoints/AuditoriaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/AuditoriaEndpointTests.cs`

**Interfaces:**
- Consumes: `IAuditoriaQueryService.ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta): Task<IReadOnlyList<AuditoriaItemDto>>` (existente, `StockApp.Application.Auditoria`). Política `VerReportes` (D5 del spec: la auditoría no tiene permiso propio).

- [ ] **Step 1: Escribir los tests que fallan — `AuditoriaEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auditoria;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class AuditoriaEndpointTests : ApiTestBase
{
    public AuditoriaEndpointTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task GetAuditoria_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditoria_ConTokenOperador_Devuelve403()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAuditoria_ConTokenAdmin_DevuelveLogGeneradoPorAltaUsuario()
    {
        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var tokenAdmin = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenAdmin);

        // Generar una entrada de auditoría real dando de alta un usuario (mismo cliente HTTP).
        await client.PostAsJsonAsync("/usuarios",
            new { NombreUsuario = "auditoria.test", NombreCompleto = (string?)null, ContrasenaPlan = "pwd12345", Rol = RolUsuario.Operador });

        var response = await client.GetAsync("/auditoria");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.Content.ReadFromJsonAsync<List<AuditoriaItemDto>>();
        Assert.Contains(items!, i => i.Accion == AccionAuditada.AltaUsuario);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter AuditoriaEndpointTests`
Expected: FAIL — `404 Not Found` en `/auditoria` (no existe todavía). El tercer test también depende de `POST /usuarios` (Task 9, todavía no implementado) — se corre igual acá porque valida el flujo real, y quedará en verde recién al cerrar Task 9; documentar esto como dependencia cruzada explícita en el Step 8 de Task 9.

- [ ] **Step 3: Implementar `AuditoriaEndpoints.cs`**

```csharp
using StockApp.Application.Auditoria;
using StockApp.Application.Authorization;

namespace StockApp.Api.Endpoints;

public static class AuditoriaEndpoints
{
    public static IEndpointRouteBuilder MapAuditoriaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/auditoria", async (
            int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta, IAuditoriaQueryService auditoria) =>
            Results.Ok(await auditoria.ObtenerLogAsync(usuarioId, fechaDesde, fechaHasta)))
            .RequireAuthorization(Permisos.VerReportes);

        return app;
    }
}
```

- [ ] **Step 4: Registrar `MapAuditoriaEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapAuditoriaEndpoints();
```

- [ ] **Step 5: Correr los tests de las dos primeras aserciones (401/403) y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "GetAuditoria_SinToken_Devuelve401|GetAuditoria_ConTokenOperador_Devuelve403"`
Expected: PASS (2 tests). El tercer test (`GetAuditoria_ConTokenAdmin_DevuelveLogGeneradoPorAltaUsuario`) sigue en rojo hasta Task 9 — es esperado, se deja documentado.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Endpoints/AuditoriaEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/AuditoriaEndpointTests.cs
git commit -m "feat(api): endpoint GET /auditoria con politica VerReportes (D5)"
```

---

## Task 9: `UsuariosEndpoints`

**Files:**
- Create: `src/StockApp.Api/Endpoints/UsuariosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/UsuariosEndpointTests.cs`

**Interfaces:**
- Consumes: `IUsuarioService.ListarAsync(): Task<IReadOnlyList<UsuarioDto>>` (Task 4), `.AltaUsuarioAsync(string, string?, string, RolUsuario): Task`, `.BajaLogicaAsync(int): Task`, `.CambiarRolAsync(int, RolUsuario): Task`, `.CambiarContrasenaAsync(int, string, string?): Task` (existentes, `StockApp.Application.Auth`).
- Produces: `record CrearUsuarioRequest(string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol)`, `record CambiarRolRequest(RolUsuario NuevoRol)`, `record CambiarContrasenaRequest(string NuevaContrasena, string? ContrasenaActual)` — públicos en `StockApp.Api.Endpoints`.

**Nota de diseño:** `AltaUsuarioAsync` devuelve `Task` (void) — no expone el id del usuario creado (D6 del spec: el único cambio permitido en la capa de aplicación fue `ListarAsync`, no tocar `AltaUsuarioAsync`). `POST /usuarios` devuelve `201 Created` con `Location: /usuarios` y body vacío — no hay id para incluir.

- [ ] **Step 1: Escribir los tests que fallan — `UsuariosEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class UsuariosEndpointTests : ApiTestBase
{
    public UsuariosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    // ── GET /usuarios ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetUsuarios_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsuarios_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUsuarios_ConTokenAdmin_Devuelve200SinExponerHash()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.listado", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("HashContrasena", body);

        var usuarios = await response.Content.ReadFromJsonAsync<List<UsuarioDto>>();
        Assert.Contains(usuarios!, u => u.NombreUsuario == "usuario.listado");
    }

    // ── POST /usuarios ────────────────────────────────────────────────────────

    [Fact]
    public async Task PostUsuarios_ConTokenAdmin_CreaUsuarioYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("nuevo.usuario", "Nuevo Usuario", "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var ctx = Factory.CrearContexto();
        Assert.True(await ctx.Usuarios.AnyAsync(u => u.NombreUsuario == "nuevo.usuario"));
    }

    [Fact]
    public async Task PostUsuarios_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/usuarios",
            new CrearUsuarioRequest("otro", null, "pwd12345", RolUsuario.Operador));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── DELETE /usuarios/{id} ────────────────────────────────────────────────

    [Fact]
    public async Task DeleteUsuario_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.baja", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/usuarios/{usuario.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task DeleteUsuario_AutoBaja_Devuelve409()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync("/usuarios/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /usuarios/{id}/rol ───────────────────────────────────────────────

    [Fact]
    public async Task PutRol_ConTokenAdmin_CambiaRolYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.rol", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/usuarios/{usuario.Id}/rol", new CambiarRolRequest(RolUsuario.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Usuarios.SingleAsync(u => u.Id == usuario.Id);
        Assert.Equal(RolUsuario.Admin, actualizado.Rol);
    }

    // ── PUT /usuarios/{id}/contrasena ────────────────────────────────────────

    [Fact]
    public async Task PutContrasena_AdminReseteandoOtroUsuario_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var usuario = await DatosDePrueba.SeedUsuarioAsync(ctx, "usuario.pwd", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync(
            $"/usuarios/{usuario.Id}/contrasena", new CambiarContrasenaRequest("nuevaClave123", null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UsuariosEndpointTests`
Expected: FAIL — error de compilación (`CrearUsuarioRequest`/`CambiarRolRequest`/`CambiarContrasenaRequest` no existen) y/o `404 Not Found`.

- [ ] **Step 3: Implementar `UsuariosEndpoints.cs`**

```csharp
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record CrearUsuarioRequest(string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol);
public record CambiarRolRequest(RolUsuario NuevoRol);
public record CambiarContrasenaRequest(string NuevaContrasena, string? ContrasenaActual);

public static class UsuariosEndpoints
{
    public static IEndpointRouteBuilder MapUsuariosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/usuarios").RequireAuthorization(Permisos.GestionarUsuarios);

        group.MapGet("/", async (IUsuarioService usuarios) =>
            Results.Ok(await usuarios.ListarAsync()));

        group.MapPost("/", async (CrearUsuarioRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.AltaUsuarioAsync(
                request.NombreUsuario, request.NombreCompleto, request.ContrasenaPlan, request.Rol);
            return Results.Created("/usuarios", (object?)null);
        });

        group.MapDelete("/{id:int}", async (int id, IUsuarioService usuarios) =>
        {
            await usuarios.BajaLogicaAsync(id);
            return Results.Ok();
        });

        group.MapPut("/{id:int}/rol", async (int id, CambiarRolRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.CambiarRolAsync(id, request.NuevoRol);
            return Results.Ok();
        });

        group.MapPut("/{id:int}/contrasena", async (int id, CambiarContrasenaRequest request, IUsuarioService usuarios) =>
        {
            await usuarios.CambiarContrasenaAsync(id, request.NuevaContrasena, request.ContrasenaActual);
            return Results.Ok();
        });

        return app;
    }
}
```

- [ ] **Step 4: Registrar `MapUsuariosEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapUsuariosEndpoints();
```

- [ ] **Step 5: Correr los tests de `UsuariosEndpointTests` y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UsuariosEndpointTests`
Expected: PASS (9 tests)

- [ ] **Step 6: Correr el test pendiente de Task 8 (`GetAuditoria_ConTokenAdmin_DevuelveLogGeneradoPorAltaUsuario`) y verificar que ahora pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter AuditoriaEndpointTests`
Expected: PASS (3 tests — el tercero, que dependía de `POST /usuarios`, ahora pasa).

- [ ] **Step 7: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Api/Endpoints/UsuariosEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/UsuariosEndpointTests.cs
git commit -m "feat(api): endpoints de ABM de usuarios (listar, alta, baja, cambio de rol/contrasena)"
```

---

## Task 10: ABM de `Productos` (POST, PUT, DELETE, PUT precio, GET ?texto=)

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ProductosEndpoints.cs`
- Test: `tests/StockApp.Api.Tests/ProductosEndpointTests.cs`

**Interfaces:**
- Consumes: `IProductoService.AltaAsync(Producto): Task<int>`, `.ModificarAsync(Producto): Task`, `.BajaLogicaAsync(int): Task`, `.CambiarPrecioAsync(int, decimal, decimal): Task`, `.BuscarPorTextoAsync(string?): Task<IReadOnlyList<ProductoDto>>` (existentes, `StockApp.Application.Catalogo`).
- Produces: `record CrearProductoRequest(string Codigo, string? CodigoBarras, string Nombre, string? Descripcion, int? CategoriaId, int? ProveedorId, int UnidadMedidaId, decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo)`, `record ModificarProductoRequest(...)` (mismos campos + `Id`), `record CambiarPrecioRequest(decimal PrecioCosto, decimal PrecioVenta)`.

- [ ] **Step 1: Agregar los tests que fallan a `ProductosEndpointTests.cs`**

Agregar al final de la clase `ProductosEndpointTests` (antes del `}` de cierre), y agregar `using Microsoft.EntityFrameworkCore;`, `using StockApp.Api.Endpoints;`, `using Microsoft.Extensions.DependencyInjection;` (verificar cuáles faltan — `Microsoft.Extensions.DependencyInjection` ya está; agregar `Microsoft.EntityFrameworkCore` y `StockApp.Api.Endpoints`):

```csharp

    // ── GET /productos?texto= ────────────────────────────────────────────────

    [Fact]
    public async Task GetProductos_ConTexto_FiltraPorTexto()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T1", "Coca Cola 1.5L");
        await DatosDePrueba.SeedProductoAsync(ctx, "SKU-T2", "Sprite 1.5L");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/productos?texto=Coca");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var productos = await response.Content.ReadFromJsonAsync<List<ProductoDto>>();
        Assert.Single(productos!);
        Assert.Equal("SKU-T1", productos![0].Codigo);
    }

    // ── POST /productos ───────────────────────────────────────────────────────

    [Fact]
    public async Task PostProductos_ConTokenOperador_CreaProductoYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        var unidad = new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/productos", new CrearProductoRequest(
            "SKU-P1", null, "Producto Nuevo", null, null, null, unidad.Id, 5m, 10m, 0m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Productos.AnyAsync(p => p.Codigo == "SKU-P1"));
    }

    [Fact]
    public async Task PostProductos_CodigoDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P2", "Producto Existente");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PostAsJsonAsync("/productos", new CrearProductoRequest(
            "SKU-P2", null, "Otro Nombre", null, null, null, producto.UnidadMedidaId, 5m, 10m, 0m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ── PUT /productos/{id} ───────────────────────────────────────────────────

    [Fact]
    public async Task PutProductos_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P3", "Nombre Original");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}", new ModificarProductoRequest(
            producto.Id, producto.Codigo, null, "Nombre Modificado", null, null, null, producto.UnidadMedidaId, 10m, 20m, 0m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal("Nombre Modificado", actualizado.Nombre);
    }

    // ── DELETE /productos/{id} ───────────────────────────────────────────────

    [Fact]
    public async Task DeleteProductos_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P4", "Producto Baja");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(1, RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.DeleteAsync($"/productos/{producto.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.False(actualizado.Activo);
    }

    // ── PUT /productos/{id}/precio ───────────────────────────────────────────

    [Fact]
    public async Task PutPrecio_ConTokenOperador_CambiaPrecioYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var producto = await DatosDePrueba.SeedProductoAsync(ctx, "SKU-P5", "Producto Precio");

        var jwt = Factory.Services.GetRequiredService<IJwtTokenService>();
        var token = jwt.GenerarToken(2, RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.PutAsJsonAsync($"/productos/{producto.Id}/precio", new CambiarPrecioRequest(15m, 30m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Productos.SingleAsync(p => p.Id == producto.Id);
        Assert.Equal(15m, actualizado.PrecioCosto);
        Assert.Equal(30m, actualizado.PrecioVenta);
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: FAIL — error de compilación (`CrearProductoRequest`/`ModificarProductoRequest`/`CambiarPrecioRequest` no existen) y/o `404 Not Found`/`405 Method Not Allowed`.

- [ ] **Step 3: Reemplazar el contenido completo de `ProductosEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearProductoRequest(
    string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);

public record ModificarProductoRequest(
    int Id, string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);

public record CambiarPrecioRequest(decimal PrecioCosto, decimal PrecioVenta);

public static class ProductosEndpoints
{
    public static IEndpointRouteBuilder MapProductosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/productos");

        group.MapGet("/", async (string? texto, IProductoService productos) =>
            Results.Ok(await productos.BuscarPorTextoAsync(texto)))
            .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/", async (CrearProductoRequest request, IProductoService productos) =>
        {
            var producto = new Producto
            {
                Codigo = request.Codigo,
                CodigoBarras = request.CodigoBarras,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                CategoriaId = request.CategoriaId,
                ProveedorId = request.ProveedorId,
                UnidadMedidaId = request.UnidadMedidaId,
                PrecioCosto = request.PrecioCosto,
                PrecioVenta = request.PrecioVenta,
                StockMinimo = request.StockMinimo,
            };

            var id = await productos.AltaAsync(producto);
            return Results.Created($"/productos/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPut("/{id:int}", async (int id, ModificarProductoRequest request, IProductoService productos) =>
        {
            var producto = new Producto
            {
                Id = id,
                Codigo = request.Codigo,
                CodigoBarras = request.CodigoBarras,
                Nombre = request.Nombre,
                Descripcion = request.Descripcion,
                CategoriaId = request.CategoriaId,
                ProveedorId = request.ProveedorId,
                UnidadMedidaId = request.UnidadMedidaId,
                PrecioCosto = request.PrecioCosto,
                PrecioVenta = request.PrecioVenta,
                StockMinimo = request.StockMinimo,
            };

            await productos.ModificarAsync(producto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapDelete("/{id:int}", async (int id, IProductoService productos) =>
        {
            await productos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPut("/{id:int}/precio", async (int id, CambiarPrecioRequest request, IProductoService productos) =>
        {
            await productos.CambiarPrecioAsync(id, request.PrecioCosto, request.PrecioVenta);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarProductos);

        group.MapPost("/{id:int}/recalcular-stock", async (int id, IMovimientoStockService movimientos) =>
            Results.Ok(await movimientos.RecalcularStockAsync(id)))
            .RequireAuthorization(Permisos.RecalcularStock);

        return app;
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProductosEndpointTests`
Expected: PASS (todas — las 3 originales de Fase 2a + las 7 nuevas)

- [ ] **Step 5: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProductosEndpoints.cs tests/StockApp.Api.Tests/ProductosEndpointTests.cs
git commit -m "feat(api): ABM completo de productos (alta, modificacion, baja, precio, busqueda por texto)"
```

---

## Task 11: `CategoriasEndpoints`

**Files:**
- Create: `src/StockApp.Api/Endpoints/CategoriasEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/CategoriasEndpointTests.cs`

**Interfaces:**
- Consumes: `ICategoriaService.AltaAsync(Categoria): Task<int>`, `.ModificarAsync(Categoria): Task`, `.BajaLogicaAsync(int): Task`, `.ListarTodasAsync(): Task<IReadOnlyList<Categoria>>`, `.ListarActivasAsync(): Task<IReadOnlyList<Categoria>>` (existentes, `StockApp.Application.Catalogo`).
- Produces: `record CrearCategoriaRequest(string Nombre)`, `record ModificarCategoriaRequest(int Id, string Nombre)`.

- [ ] **Step 1: Escribir los tests que fallan — `CategoriasEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class CategoriasEndpointTests : ApiTestBase
{
    public CategoriasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetCategorias_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCategorias_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCategorias_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Categorias.Add(new Categoria { Nombre = "Bebidas", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/categorias");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<Categoria>>();
        Assert.Contains(categorias!, c => c.Nombre == "Bebidas");
    }

    [Fact]
    public async Task PostCategorias_ConTokenAdmin_CreaYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Lácteos"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Categorias.AnyAsync(c => c.Nombre == "Lácteos"));
    }

    [Fact]
    public async Task PostCategorias_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Lácteos"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostCategorias_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Categorias.Add(new Categoria { Nombre = "Carnes", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/categorias", new CrearCategoriaRequest("Carnes"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutCategorias_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var categoria = new Categoria { Nombre = "Original", Activo = true };
        ctx.Categorias.Add(categoria);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/categorias/{categoria.Id}", new ModificarCategoriaRequest(categoria.Id, "Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCategorias_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var categoria = new Categoria { Nombre = "Para Baja", Activo = true };
        ctx.Categorias.Add(categoria);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/categorias/{categoria.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.Categorias.SingleAsync(c => c.Id == categoria.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetCategoriasActivas_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Categorias.Add(new Categoria { Nombre = "Activa", Activo = true });
        ctx.Categorias.Add(new Categoria { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/categorias/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var categorias = await response.Content.ReadFromJsonAsync<List<Categoria>>();
        Assert.Contains(categorias!, c => c.Nombre == "Activa");
        Assert.DoesNotContain(categorias!, c => c.Nombre == "Inactiva");
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter CategoriasEndpointTests`
Expected: FAIL — error de compilación (`CrearCategoriaRequest`/`ModificarCategoriaRequest` no existen) y/o `404 Not Found`.

- [ ] **Step 3: Implementar `CategoriasEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearCategoriaRequest(string Nombre);
public record ModificarCategoriaRequest(int Id, string Nombre);

public static class CategoriasEndpoints
{
    public static IEndpointRouteBuilder MapCategoriasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/categorias");

        group.MapGet("/", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearCategoriaRequest request, ICategoriaService categorias) =>
        {
            var id = await categorias.AltaAsync(new Categoria { Nombre = request.Nombre });
            return Results.Created($"/categorias/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarCategoriaRequest request, ICategoriaService categorias) =>
        {
            await categorias.ModificarAsync(new Categoria { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, ICategoriaService categorias) =>
        {
            await categorias.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/activas", async (ICategoriaService categorias) =>
            Results.Ok(await categorias.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
```

- [ ] **Step 4: Registrar `MapCategoriasEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapCategoriasEndpoints();
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter CategoriasEndpointTests`
Expected: PASS (9 tests)

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Endpoints/CategoriasEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/CategoriasEndpointTests.cs
git commit -m "feat(api): endpoints de ABM de categorias con GET /categorias/activas"
```

---

## Task 12: `ProveedoresEndpoints`

**Files:**
- Create: `src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs`

**Interfaces:**
- Consumes: `IProveedorService.AltaAsync(Proveedor): Task<int>`, `.ModificarAsync(Proveedor): Task`, `.BajaLogicaAsync(int): Task`, `.ListarTodosAsync(): Task<IReadOnlyList<Proveedor>>` (existentes, `StockApp.Application.Catalogo`). **Nota:** a diferencia de Categoría/UnidadMedida, `IProveedorService` no tiene `ListarActivasAsync()` (asimetría real del código, spec la documenta como alternativa C descartada) — no se agrega un endpoint `/proveedores/activas` en esta fase.
- Produces: `record CrearProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas)`, `record ModificarProveedorRequest(int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas)`.

- [ ] **Step 1: Escribir los tests que fallan — `ProveedoresEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ProveedoresEndpointTests : ApiTestBase
{
    public ProveedoresEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetProveedores_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetProveedores_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetProveedores_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Proveedores.Add(new Proveedor { Nombre = "Proveedor Uno", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/proveedores");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var proveedores = await response.Content.ReadFromJsonAsync<List<Proveedor>>();
        Assert.Contains(proveedores!, p => p.Nombre == "Proveedor Uno");
    }

    [Fact]
    public async Task PostProveedores_ConTokenAdmin_CreaYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/proveedores",
            new CrearProveedorRequest("Distribuidora XYZ", "011-1234", "xyz@mail.com", "Calle 123", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.Proveedores.AnyAsync(p => p.Nombre == "Distribuidora XYZ"));
    }

    [Fact]
    public async Task PostProveedores_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.Proveedores.Add(new Proveedor { Nombre = "Ya Existe", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/proveedores",
            new CrearProveedorRequest("Ya Existe", null, null, null, null));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutProveedores_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var proveedor = new Proveedor { Nombre = "Original", Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/proveedores/{proveedor.Id}",
            new ModificarProveedorRequest(proveedor.Id, "Modificado", "011-9999", null, null, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProveedores_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var proveedor = new Proveedor { Nombre = "Para Baja", Activo = true };
        ctx.Proveedores.Add(proveedor);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/proveedores/{proveedor.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.Proveedores.SingleAsync(p => p.Id == proveedor.Id);
        Assert.False(actualizado.Activo);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProveedoresEndpointTests`
Expected: FAIL — error de compilación y/o `404 Not Found`.

- [ ] **Step 3: Implementar `ProveedoresEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearProveedorRequest(string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);
public record ModificarProveedorRequest(int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);

public static class ProveedoresEndpoints
{
    public static IEndpointRouteBuilder MapProveedoresEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/proveedores").RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/", async (IProveedorService proveedores) =>
            Results.Ok(await proveedores.ListarTodosAsync()));

        group.MapPost("/", async (CrearProveedorRequest request, IProveedorService proveedores) =>
        {
            var proveedor = new Proveedor
            {
                Nombre = request.Nombre,
                Telefono = request.Telefono,
                Email = request.Email,
                Direccion = request.Direccion,
                Notas = request.Notas,
            };
            var id = await proveedores.AltaAsync(proveedor);
            return Results.Created($"/proveedores/{id}", new { id });
        });

        group.MapPut("/{id:int}", async (int id, ModificarProveedorRequest request, IProveedorService proveedores) =>
        {
            var proveedor = new Proveedor
            {
                Id = id,
                Nombre = request.Nombre,
                Telefono = request.Telefono,
                Email = request.Email,
                Direccion = request.Direccion,
                Notas = request.Notas,
            };
            await proveedores.ModificarAsync(proveedor);
            return Results.Ok();
        });

        group.MapDelete("/{id:int}", async (int id, IProveedorService proveedores) =>
        {
            await proveedores.BajaLogicaAsync(id);
            return Results.Ok();
        });

        return app;
    }
}
```

- [ ] **Step 4: Registrar `MapProveedoresEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapProveedoresEndpoints();
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter ProveedoresEndpointTests`
Expected: PASS (7 tests)

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Endpoints/ProveedoresEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/ProveedoresEndpointTests.cs
git commit -m "feat(api): endpoints de ABM de proveedores"
```

---

## Task 13: `UnidadesMedidaEndpoints`

**Files:**
- Create: `src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs`

**Interfaces:**
- Consumes: `IUnidadMedidaService.AltaAsync(UnidadMedida): Task<int>`, `.ModificarAsync(UnidadMedida): Task`, `.BajaLogicaAsync(int): Task`, `.ListarTodasAsync(): Task<IReadOnlyList<UnidadMedida>>`, `.ListarActivasAsync(): Task<IReadOnlyList<UnidadMedida>>` (existentes, `StockApp.Application.Catalogo`). No se expone `GarantizarUnidadPorDefectoAsync()` — es un método de seed interno, no una operación HTTP con sentido para un cliente.
- Produces: `record CrearUnidadMedidaRequest(string Nombre, string Abreviatura)`, `record ModificarUnidadMedidaRequest(int Id, string Nombre, string Abreviatura)`.

- [ ] **Step 1: Escribir los tests que fallan — `UnidadesMedidaEndpointTests.cs`**

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class UnidadesMedidaEndpointTests : ApiTestBase
{
    public UnidadesMedidaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetUnidadesMedida_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUnidadesMedida_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetUnidadesMedida_ConTokenAdmin_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/unidades-medida");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedida>>();
        Assert.Contains(unidades!, u => u.Nombre == "Kilo");
    }

    [Fact]
    public async Task PostUnidadesMedida_ConTokenAdmin_CreaYDevuelve201()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/unidades-medida", new CrearUnidadMedidaRequest("Metro", "m"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.UnidadesMedida.AnyAsync(u => u.Nombre == "Metro"));
    }

    [Fact]
    public async Task PostUnidadesMedida_AbreviaturaDuplicada_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Kilo", Abreviatura = "kg", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/unidades-medida", new CrearUnidadMedidaRequest("Kilogramo", "kg"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutUnidadesMedida_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var unidad = new UnidadMedida { Nombre = "Original", Abreviatura = "or", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/unidades-medida/{unidad.Id}",
            new ModificarUnidadMedidaRequest(unidad.Id, "Modificada", "mo"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUnidadesMedida_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        var unidad = new UnidadMedida { Nombre = "Para Baja", Abreviatura = "pb", Activo = true };
        ctx.UnidadesMedida.Add(unidad);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/unidades-medida/{unidad.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.UnidadesMedida.SingleAsync(u => u.Id == unidad.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetUnidadesMedidaActivas_ConTokenOperador_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Activa", Abreviatura = "ac", Activo = true });
        ctx.UnidadesMedida.Add(new UnidadMedida { Nombre = "Inactiva", Abreviatura = "in", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/unidades-medida/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var unidades = await response.Content.ReadFromJsonAsync<List<UnidadMedida>>();
        Assert.Contains(unidades!, u => u.Nombre == "Activa");
        Assert.DoesNotContain(unidades!, u => u.Nombre == "Inactiva");
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: FAIL — error de compilación y/o `404 Not Found`.

- [ ] **Step 3: Implementar `UnidadesMedidaEndpoints.cs`**

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearUnidadMedidaRequest(string Nombre, string Abreviatura);
public record ModificarUnidadMedidaRequest(int Id, string Nombre, string Abreviatura);

public static class UnidadesMedidaEndpoints
{
    public static IEndpointRouteBuilder MapUnidadesMedidaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/unidades-medida");

        group.MapGet("/", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarTodasAsync()))
            .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPost("/", async (CrearUnidadMedidaRequest request, IUnidadMedidaService unidades) =>
        {
            var unidad = new UnidadMedida { Nombre = request.Nombre, Abreviatura = request.Abreviatura };
            var id = await unidades.AltaAsync(unidad);
            return Results.Created($"/unidades-medida/{id}", new { id });
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapPut("/{id:int}", async (int id, ModificarUnidadMedidaRequest request, IUnidadMedidaService unidades) =>
        {
            var unidad = new UnidadMedida { Id = id, Nombre = request.Nombre, Abreviatura = request.Abreviatura };
            await unidades.ModificarAsync(unidad);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapDelete("/{id:int}", async (int id, IUnidadMedidaService unidades) =>
        {
            await unidades.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTablasMaestras);

        group.MapGet("/activas", async (IUnidadMedidaService unidades) =>
            Results.Ok(await unidades.ListarActivasAsync()))
            .RequireAuthorization(Permisos.GestionarProductos);

        return app;
    }
}
```

- [ ] **Step 4: Registrar `MapUnidadesMedidaEndpoints()` en `Program.cs`**

Agregar la línea, junto a las demás `app.MapXxxEndpoints()`:

```csharp
app.MapUnidadesMedidaEndpoints();
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter UnidadesMedidaEndpointTests`
Expected: PASS (9 tests)

- [ ] **Step 6: Correr toda la suite**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas)

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Endpoints/UnidadesMedidaEndpoints.cs src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/UnidadesMedidaEndpointTests.cs
git commit -m "feat(api): endpoints de ABM de unidades de medida con GET /unidades-medida/activas"
```

---

## Bloque D — Cierre

## Task 14: Actualizar `README.md` con la superficie completa

**Files:**
- Modify: `src/StockApp.Api/README.md`

**Interfaces:** ninguna nueva — documentación.

- [ ] **Step 1: Reemplazar el contenido completo de `src/StockApp.Api/README.md`**

```markdown
# StockApp.Api

API de StockApp: JWT + superficie HTTP completa (Fase 2a: login + slice vertical;
Fase 2b: movimientos, reportes, auditoría, usuarios, catálogo completo).

## Requisitos

- .NET 10 SDK
- PostgreSQL accesible (local o contenedor Docker) con la base `stockapp` migrada
  (mismas migraciones que la app desktop, en `StockApp.Infrastructure/Migrations`).
  En desarrollo se usa el contenedor `stockapp-pg` (`postgres:16-alpine`), expuesto
  en `localhost:5432`, con la connection string por defecto de `appsettings.json`.
- Al menos un usuario Admin y un usuario Operador existentes en la tabla `Usuarios`
  (sembrados por `StockApp.Seeder` o por `PrimerArranqueService` de la app desktop —
  el bootstrap de primer arranque vía API queda para Fase 4).

## Configurar el secreto JWT (desarrollo)

El secreto de firma NUNCA se hardcodea ni se committea.

```bash
cd src/StockApp.Api
dotnet user-secrets init
dotnet user-secrets set "Jwt:Secret" "una-clave-de-desarrollo-de-al-menos-32-caracteres"
```

## Correr la API

```bash
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http
```

Kestrel expone la API en `http://localhost:5043` con el profile `http`.

## Superficie de endpoints

Todos requieren `Authorization: Bearer <token>` salvo `POST /auth/login`. Las
políticas están derivadas de `AuthorizationService` (`Permisos.Todos` en
`Program.cs`) — Admin siempre tiene acceso; Operador solo a lo marcado "Admin+Op".

| Recurso | Endpoint | Rol |
|---|---|---|
| Auth | `POST /auth/login` | público |
| Movimientos | `POST /movimientos` | Admin+Op |
| | `GET /movimientos/historial` | Admin+Op |
| | `POST /productos/{id}/recalcular-stock` | Admin+Op |
| Reportes | `GET /reportes/valorizacion` | Admin |
| | `GET /reportes/stock-por-categoria` | Admin |
| | `GET /reportes/mas-movidos` | Admin |
| | `GET /reportes/historial-producto/{productoId}` | Admin |
| Auditoría | `GET /auditoria` | Admin |
| Usuarios | `GET /usuarios` · `POST /usuarios` · `DELETE /usuarios/{id}` · `PUT /usuarios/{id}/rol` · `PUT /usuarios/{id}/contrasena` | Admin |
| Productos | `GET /productos?texto=` · `POST /productos` · `PUT /productos/{id}` · `DELETE /productos/{id}` · `PUT /productos/{id}/precio` | Admin+Op |
| Categorías | `GET /categorias` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| | `GET /categorias/activas` | Admin+Op |
| Proveedores | `GET /proveedores` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| Unidades | `GET /unidades-medida` · `POST` · `PUT /{id}` · `DELETE /{id}` | Admin |
| | `GET /unidades-medida/activas` | Admin+Op |

## Verificación manual (curl)

Con la API corriendo, en otra terminal (puerto `5043`):

```bash
# 1) Login Admin
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"admin123"}'
# Copiar "token" -> <TOKEN_ADMIN>

# 2) Login Operador
curl -X POST http://localhost:5043/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"operador","contrasena":"operador123"}'
# Copiar "token" -> <TOKEN_OPERADOR>

# 3) Alta de categoría (Admin) -> 201
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_ADMIN>" \
  -d '{"nombre":"Bebidas"}'

# 4) La misma acción con Operador -> 403 (sin GestionarTablasMaestras)
curl -i -X POST http://localhost:5043/categorias \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"nombre":"Otra"}'

# 5) Alta de producto (Operador, tiene GestionarProductos) -> 201
curl -i -X POST http://localhost:5043/productos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"codigo":"SKU-CURL-1","codigoBarras":null,"nombre":"Producto Curl","descripcion":null,"categoriaId":null,"proveedorId":null,"unidadMedidaId":1,"precioCosto":10,"precioVenta":20,"stockMinimo":0}'

# 6) Registrar un movimiento de entrada (Operador) -> 201
curl -i -X POST http://localhost:5043/movimientos \
  -H "Content-Type: application/json" -H "Authorization: Bearer <TOKEN_OPERADOR>" \
  -d '{"productoId":1,"tipo":"Entrada","motivo":"Compra","cantidad":5,"precioUnitario":10,"comentario":"Carga inicial"}'

# 7) Reporte de valorización (Admin) -> 200; con Operador -> 403
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_ADMIN>"
curl -i http://localhost:5043/reportes/valorizacion -H "Authorization: Bearer <TOKEN_OPERADOR>"

# 8) Auditoría (Admin, D5: usa VerReportes) -> 200 con las entradas generadas arriba
curl http://localhost:5043/auditoria -H "Authorization: Bearer <TOKEN_ADMIN>"

# 9) Listado de usuarios (Admin) -> 200, sin HashContrasena en el body
curl http://localhost:5043/usuarios -H "Authorization: Bearer <TOKEN_ADMIN>"
```

Confirmar: cada `201`/`200` con Admin/Operador según la tabla de arriba; cada
intento fuera de rol devuelve `403` en formato `application/problem+json`; los
efectos (categoría creada, producto creado, stock incrementado) son visibles en
consultas posteriores (`GET /categorias`, `GET /productos`, historial de
movimientos) — no alcanza con mirar el status code.
```

- [ ] **Step 2: Ejecutar la verificación manual real**

Con Docker (o Postgres local) disponible y la API corriendo (`dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http`), correr la secuencia completa de `curl` del README recién escrito contra la base real. Confirmar cada resultado esperado (201/200/403) y que los efectos quedan reflejados en consultas posteriores.

Expected: todos los pasos devuelven el status esperado; los datos creados aparecen en los listados subsiguientes; `GET /reportes/valorizacion` con `<TOKEN_OPERADOR>` devuelve `403`.

- [ ] **Step 3: Commit**

```bash
git add src/StockApp.Api/README.md
git commit -m "docs(api): actualiza README con la superficie completa de la Fase 2b"
```

---

## Task 15: Suite completa del repositorio

**Files:** ninguno — solo verificación.

- [ ] **Step 1: Correr la suite completa de `StockApp.Application.Tests`**

Run: `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj`
Expected: PASS (todas — incluye `PermisosTests`, `AuthorizationServiceTests` extendido, `UsuarioServiceTests` extendido, más toda la suite preexistente sin cambios).

- [ ] **Step 2: Correr la suite completa de `StockApp.Api.Tests`**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS (todas — Health, JwtTokenService, Login, Productos, PoliticasDerivadas, DomainExceptionHandler, Movimientos, Reportes, Auditoria, Usuarios, Categorias, Proveedores, UnidadesMedida, ProblemDetails, Aislamiento).

- [ ] **Step 3: Correr la solución completa**

Run: `dotnet test StockApp.sln`
Expected: PASS (todas — incluye también `StockApp.Infrastructure.Tests` y cualquier otro proyecto de test preexistente, sin cambios de esta fase).

- [ ] **Step 4: Commit (si `Step 1-3` requirieron algún ajuste; si no, no hay commit — este task es de verificación pura)**

Si todo pasó sin cambios, no hay nada para commitear — Task 15 cierra el plan sin diff propio.

---

## Self-Review

**1. Cobertura del spec:**

| Decisión/sección del spec | Task que la implementa |
|---|---|
| D1 — Políticas derivadas | Task 1 (`Permisos.Todos`), Task 2 (`TienePermiso`), Task 3 (loop en `Program.cs` + test de cierre) |
| D2 — Defensa en profundidad | No requiere código nuevo: los `Verificar(...)` de los servicios de aplicación no se tocan en ningún task; verificado explícitamente en Task 9 (`DeleteUsuario_AutoBaja_Devuelve409`, que depende de la 2ª barrera del servicio, no de la política HTTP) |
| D3 — Valorización se muda a /reportes | Task 7 (elimina la ruta vieja, agrega `GET /reportes/valorizacion`, ajusta `ProblemDetailsTests.cs`, borra `ReporteValorizacionEndpointTests.cs`, agrega test explícito de 404 en la ruta vieja) |
| D4 — ABM de productos en alcance | Task 10 |
| D5 — Auditoría sin permiso propio (usa VerReportes) | Task 8 |
| D6 — Único cambio en Application (`ListarAsync`) | Task 4 |
| Tabla de endpoints completa (11 filas) | Movimientos → Task 6; Reportes → Task 7; Auditoría → Task 8; Usuarios → Task 9; Productos → Task 10; Categorías (+activas) → Task 11; Proveedores → Task 12; Unidades (+activas) → Task 13 |
| Manejo de errores (409/404/400/403/500) | Task 5 (`DomainExceptionHandler`), ejercitado end-to-end en Task 6 (409 stock insuficiente, 404 producto inexistente), Task 9 (409 auto-baja), Task 10 (409 código duplicado), Task 11/12/13 (409 nombre/abreviatura duplicados) |
| Testing — matriz mínima (401/403/200-201/409) | Presente en cada task de Bloque C |
| Testing — test de cierre del enfoque B | Task 3 (`PoliticasDerivadasTests`) |
| Fuera de alcance (migración desktop, paginación, OpenAPI, permiso propio de auditoría, refresh tokens) | Ningún task los toca — omisión deliberada, consistente con el spec |

**2. Scan de placeholders:** no quedan `TODO`, "manejar apropiadamente" ni "similar a Task N". Cada `Create`/`Modify` muestra el archivo completo o el bloque exacto a insertar, con código real y compilable (verificado contra las firmas exactas leídas del código fuente real: `IMovimientoStockService`, `IReporteStockService`, `IAuditoriaQueryService`, `IUsuarioService`, `ICategoriaService`, `IProveedorService`, `IUnidadMedidaService`, `IProductoService`, entidades de dominio, `StockInsuficienteException`).

**3. Consistencia de tipos:** `Permisos.Todos` (Task 1) se consume igual en Task 2 (test) y Task 3 (`Program.cs` + test de cierre). `IAuthorizationService.TienePermiso(RolUsuario, string): bool` (Task 2) se usa con la misma firma en Task 3. `UsuarioDto` (Task 4) se usa igual en `UsuariosEndpoints` (Task 9) y su test. `DomainExceptionHandler` (Task 5) no se referencia por nombre de tipo en ningún endpoint — se activa vía `AddExceptionHandler<T>()` + `UseExceptionHandler()`, correcto para el patrón `IExceptionHandler` de .NET 8+. `RegistrarMovimientoRequest` (Task 6) mantiene el mismo orden/nombres de parámetros en su único punto de uso (el test). Los DTOs de catálogo (`CrearCategoriaRequest`, `CrearProveedorRequest`, `CrearUnidadMedidaRequest`, `CrearProductoRequest`/`ModificarProductoRequest`) usan siempre los nombres de propiedad de las entidades de dominio reales (`Nombre`, `Abreviatura`, `Telefono`, `Codigo`, etc.), sin inventar campos.

**4. Decisiones tomadas al leer el código real que precisan o corrigen el spec:**

- **D1, mecanismo de exposición:** el spec dejaba abierto "cómo" se deriva la tabla. `AuthorizationService.AccionesOperador` es un `HashSet<string> private static readonly` sin ningún accesor público. Se agregó `IAuthorizationService.TienePermiso(RolUsuario, string): bool` — una consulta que no lanza, reusando la misma tabla que `Verificar` — en vez de exponer la colección directamente o hacer `Program.cs` capturar excepciones de `Verificar` en un loop (anti-patrón).
- **Excepciones de dominio reales:** el spec decía "los tipos exactos se verifican al escribir el plan". Se confirmó por inspección (`rg "class.*Exception"`) que existe **una sola** excepción custom (`StockApp.Domain.Exceptions.StockInsuficienteException : Exception`) — el resto de los errores de negocio (duplicados, "ya inactivo", "último Admin", auto-baja) usa `InvalidOperationException`/`ArgumentException`/`KeyNotFoundException`/`UnauthorizedAccessException` del BCL directamente, sin envolver en tipos de dominio. El mapeo de `DomainExceptionHandler` (Task 5) refleja esto: no hay jerarquía de excepciones de dominio que mapear, solo 5 tipos concretos + default.
- **D6, `AltaUsuarioAsync` no cambia:** confirmado que `AltaUsuarioAsync` devuelve `Task` (no `Task<int>`) y el spec prohíbe tocarlo. `POST /usuarios` (Task 9) devuelve `201 Created` con body vacío — no hay id de usuario creado para incluir en la respuesta. Documentado explícitamente en el task para que no se lea como un descuido.
- **Asimetría de `IProveedorService`:** confirmado que no tiene `ListarActivasAsync()` (a diferencia de `ICategoriaService`/`IUnidadMedidaService`) — el spec ya lo anticipaba como ejemplo de por qué se descartó un helper CRUD genérico (alternativa C). Task 12 no agrega `GET /proveedores/activas`; se documenta la omisión en la sección "Interfaces" del task, no como un olvido.
- **Convención de status HTTP:** el spec no fijaba 200 vs 201 vs 204 explícitamente (solo dice "200/201" en la matriz de testing). Se definió una convención uniforme en Global Constraints (POST-crea → 201, resto → 200) para que los 8 tasks de Bloque C no diverjan entre sí.
- **`GET /reportes/mas-movidos?topN`:** Minimal API no soporta bindear un query param `int` opcional con default de C# (`int topN = 20`) sin marcarlo nullable — se implementó como `int topN` con un chequeo manual (`topN == 0 ? 20 : topN`) para preservar el default de `IReporteStockService.ObtenerMasMovidosAsync` sin forzar al cliente a mandar siempre el parámetro.
