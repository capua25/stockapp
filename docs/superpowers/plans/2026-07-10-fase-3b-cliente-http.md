# Fase 3b — Cliente HTTP del desktop — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** El desktop Avalonia deja de acceder a Postgres y consume la API REST de Fase 2b/3a: un proyecto nuevo `src/StockApp.ApiClient` implementa las MISMAS interfaces `IXxxService` de `StockApp.Application` como clientes HTTP, y `StockApp.Presentation` pierde su referencia a `StockApp.Infrastructure` (criterio de éxito duro).

**Architecture:** Los ~22 ViewModels no se tocan — siguen consumiendo `IAuthService`, `IProductoService`, etc.; solo cambia la implementación registrada en el DI de `App.axaml.cs`. `StockApp.ApiClient` (referencia SOLO a Application y Domain) contiene: 10 `XxxApiClient`, `ApiSession : ICurrentSession` (singleton con token y evento de sesión vencida), `AuthTokenHandler : DelegatingHandler` (Bearer en cada request; 401 con token → evento), y `ApiErrores` (traducción centralizada de `problem+json` → excepciones de dominio de 3a; conexión/timeout → `ServidorNoDisponibleException` nueva). API-only, sin modo dual.

**Tech Stack:** .NET 10, `HttpClient` + `System.Net.Http.Json` (del framework, sin paquetes nuevos), Avalonia 12 (Presentation), xUnit + `HttpMessageHandler` falso (ApiClient.Tests), xUnit + Moq (Presentation.Tests), xUnit puro (Api.Tests unit del handler).

## Global Constraints

- Target framework `net10.0`. Proyectos nuevos permitidos SOLO dos: `src/StockApp.ApiClient` y `tests/StockApp.ApiClient.Tests` (la constraint "sin csproj nuevos" era de 3a, no aplica acá).
- `StockApp.ApiClient` referencia SOLO `StockApp.Application` y `StockApp.Domain`. **Cero `PackageReference`** — `System.Net.Http.Json` viene del shared framework.
- `BaseAddress` del `HttpClient` SIEMPRE termina en `/` y los paths relativos NUNCA empiezan con `/` (`"auth/login"`, no `"/auth/login"`) — si no, la resolución de URI descarta el path base.
- JSON con `ReadFromJsonAsync`/`PostAsJsonAsync`/`PutAsJsonAsync` sin options propias: los defaults Web (camelCase, case-insensitive, **enums numéricos**) coinciden con la API. NO registrar converters.
- Traducción de errores HTTP centralizada en `ApiErrores` — ningún `XxxApiClient` hace try/catch propio de HTTP ni switch de status codes.
- Mapa de traducción (fijado por el spec + `DomainExceptionHandler` de la API): 404→`EntidadNoEncontradaException`, 409→`ReglaDeNegocioException` (o `StockInsuficienteException` si el problem+json trae las extensiones de stock — ver Task 5), 400→`ArgumentException`, 403→`UnauthorizedAccessException`, 401→`UnauthorizedAccessException` (además del evento de sesión vencida), conexión/timeout→`ServidorNoDisponibleException`. El mensaje es SIEMPRE el `detail` del problem+json (fallback: `title`, fallback: genérico con el status).
- El cliente NUNCA usa el header `Location` — ningún POST de la API lo emite (los 201 devuelven `{ id }` o el DTO en el body).
- `HttpClient.Timeout` = **10 segundos** (ver resolución OQ-3). Sin Polly, sin reintentos, sin offline, sin refresh tokens, sin TLS (fuera de alcance del spec).
- Los ViewModels NO se tocan, con exactamente TRES retoques exigidos por la tabla "Manejo de errores" del spec: `LoginViewModel` (servidor caído en login), `PrimerArranqueViewModel` (ídem en crear admin) y `ShellViewModel` (arranque con servidor caído + `MostrarLoginConAviso`). Ningún otro VM cambia.
- Criterio de éxito duro: `src/StockApp.Presentation/StockApp.Presentation.csproj` SIN `ProjectReference` a `StockApp.Infrastructure`, compilando y andando.
- TDD estricto: test rojo → implementación mínima → verde. Cada task deja la suite completa en verde antes de terminar.
- Conventional Commits, **sin** `Co-Authored-By`. Un commit por task, al cierre de cada task.

## Resolución de los open questions del spec (verificada contra el código real)

- **OQ-1 `IAuthorizationService` local**: `rg "IAuthorizationService" src/StockApp.Presentation` → SOLO `App.axaml.cs` (el registro DI). Ninguna View/VM lo consulta — la autorización vivía dentro de los servicios de Application, que se van. **Se elimina del DI del desktop** (Task 17). La autorización real es del servidor (403 → `UnauthorizedAccessException`, que los VMs ya manejan).
- **OQ-2 `CsvExporter`**: el spec dice que vive en Infrastructure — **es incorrecto**: vive en `src/StockApp.Application/Exportacion/` y sus `using` son solo BCL (`System.Globalization/Reflection/Text`). **No hay mudanza**; el registro `AddTransient<ICsvExporter, CsvExporter>()` queda idéntico (Task 17). Corrección documentada al spec.
- **OQ-3 Timeout del `HttpClient`**: **10 segundos**. La API vive en la LAN (ida y vuelta de milisegundos); 10 s cubre con margen el reporte más pesado contra Postgres y acota la espera con el server caído/host inalcanzable — el default de 100 s dejaría la UI colgada más de un minuto y medio antes de informar el error.
- **OQ-4 Mecanismo de "sesión vencida"**: evento .NET `event Action? SesionVencida` en `ApiSession`, disparado por `AuthTokenHandler` SOLO cuando un 401 responde a un request **que llevaba token** (el 401 de `POST /auth/login` con credenciales malas no lo dispara). El wiring vive en la composition root (`App.axaml.cs`): `apiSession.SesionVencida += () => uiDispatcher.Post(() => shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo."))` — coherente con cómo navega el Shell hoy (`MostrarLogin()` crea el `LoginViewModel` directo) y marshaleado al UI thread con el `IUiDispatcher` existente.
- **OQ-5 Tests de Presentation con Infrastructure**: `rg` encontró exactamente 4 archivos: el `.csproj` (ProjectReference) y los 3 DI tests (`ComposicionDICatalogoTests`, `ComposicionDIMovimientosTests`, `ComposicionDIReportesTests`) que arman un contenedor con `AppDbContext`/repos reales. **Se reemplazan por `ComposicionDIApiTests`** que espeja la composición nueva (Task 18) ANTES de quitar la ProjectReference (Task 19).

## Minas descubiertas al planificar (correcciones al spec)

- **Mina 1 — el updater Velopack vive en Infrastructure**: `IVelopackGateway`, `VelopackGatewayReal`, `VelopackUpdateService`, `UpdaterOptions` y `FallbackUpdateSource` están en `src/StockApp.Infrastructure/Actualizaciones/` y `App.axaml.cs` los registra. El spec no los menciona. Sin mudarlos, quitar la referencia a Infrastructure rompe el actualizador. **Task 15 los muda a `src/StockApp.Presentation/Actualizaciones/`** (Presentation ya referencia el paquete Velopack y ya tiene esa carpeta con `CoordinadorActualizacion`), junto con sus 2 archivos de tests.
- **Mina 2 — el flujo "¿forzar salida?" depende del tipo `StockInsuficienteException`**: `MovimientoRegistroViewModelBase.RegistrarAsync` hace `catch (StockInsuficienteException ex)` y usa `ex.StockResultante` para preguntar y reintentar con `forzar: true`. Un 409 traducido a `ReglaDeNegocioException` plano rompería ese flujo sin tocar el VM (prohibido por el spec). **Task 5 agrega extensiones estructuradas** (`productoId`, `stockActual`, `cantidadSolicitada`) al problem+json de `StockInsuficienteException` en `DomainExceptionHandler`, y el cliente la reconstruye con su constructor real (mismo mensaje, mismo `StockResultante`). Cambio aditivo a la API, no rompe la superficie 3a.
- **Estrategia de tests (precisión sobre el spec)**: los ApiClients se cubren con **tests unitarios con `HttpMessageHandler` falso** (asserts de método/ruta/query/body y del mapeo de responses y errores) en `tests/StockApp.ApiClient.Tests` — más rápidos y deterministas que la suite Testcontainers para el contrato del cliente — y el contrato real punta a punta se cubre con la **verificación orgánica final** (Task 21: API real + desktop real + Postgres, convención del proyecto).
- **Puerto de desarrollo**: la API corre en `http://localhost:5043` (launch profile `http`). El `appsettings.json` del desktop se entrega con `Api:BaseUrl = http://localhost:5043` para que la verificación orgánica ande sin flags; el **default en código** cuando falta la clave es `http://localhost:5000` (el que fija el spec).

---

## Bloque A — Proyecto ApiClient e infraestructura HTTP (Tasks 1-5)

Primero la base compartida: proyecto + excepción nueva (Task 1), sesión (Task 2), traducción de errores y query strings (Task 3), handler de token (Task 4) y el cambio aditivo en la API para la Mina 2 (Task 5). Los 10 clients del Bloque B consumen exactamente estos tipos.

## Task 1: Proyecto `StockApp.ApiClient` + `ServidorNoDisponibleException`

**Files:**
- Create: `src/StockApp.ApiClient/StockApp.ApiClient.csproj`
- Create: `src/StockApp.ApiClient/ServidorNoDisponibleException.cs`
- Create: `tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
- Test: `tests/StockApp.ApiClient.Tests/ServidorNoDisponibleExceptionTests.cs`
- Modify: `StockApp.sln` (vía `dotnet sln add`)

**Interfaces:**
- Produces: `ServidorNoDisponibleException(Exception? inner = null)` con `const string MensajePorDefecto` — consumida por `ApiErrores` (Task 3), `LoginViewModel`/`PrimerArranqueViewModel`/`ShellViewModel` (Task 16) y el handler global de `App.axaml.cs` (Task 17).
- Produces: proyecto `StockApp.ApiClient` (namespace raíz `StockApp.ApiClient`, `InternalsVisibleTo` para el proyecto de tests) — TODOS los tasks de los Bloques A/B crean archivos acá.

- [ ] **Step 1: Crear `src/StockApp.ApiClient/StockApp.ApiClient.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Los tests ejercitan helpers internal (ApiErrores, ApiQuery, wire records). -->
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>StockApp.ApiClient.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>

  <ItemGroup>
    <!-- SOLO Application y Domain (spec 3b): interfaces IXxxService + DTOs + entidades
         + excepciones de dominio. Sin PackageReferences: System.Net.Http.Json es parte
         del shared framework. -->
    <ProjectReference Include="..\StockApp.Application\StockApp.Application.csproj" />
    <ProjectReference Include="..\StockApp.Domain\StockApp.Domain.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Crear `tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`** (mismo template que `StockApp.Application.Tests`, sin Moq — el doble de test es un `HttpMessageHandler` falso propio)

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
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\StockApp.ApiClient\StockApp.ApiClient.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 3: Agregar ambos proyectos a la solución**

Run:
```bash
dotnet sln StockApp.sln add src/StockApp.ApiClient/StockApp.ApiClient.csproj --solution-folder src
dotnet sln StockApp.sln add tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --solution-folder tests
```
Expected: `Project ... added to the solution.` dos veces.

- [ ] **Step 4: Escribir el test que falla — `ServidorNoDisponibleExceptionTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ServidorNoDisponibleExceptionTests.cs
using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests;

public class ServidorNoDisponibleExceptionTests
{
    [Fact]
    public void Constructor_SinInner_UsaElMensajePorDefecto()
    {
        var ex = new ServidorNoDisponibleException();

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void Constructor_ConInner_PreservaLaCausa()
    {
        var causa = new HttpRequestException("connection refused");

        var ex = new ServidorNoDisponibleException(causa);

        Assert.Same(causa, ex.InnerException);
        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ex.Message);
    }
}
```

- [ ] **Step 5: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
Expected: FAIL — error de compilación (`ServidorNoDisponibleException` no existe).

- [ ] **Step 6: Implementar `ServidorNoDisponibleException.cs`**

```csharp
// src/StockApp.ApiClient/ServidorNoDisponibleException.cs
namespace StockApp.ApiClient;

/// <summary>
/// El servidor de StockApp no respondió: conexión rechazada, host inalcanzable o timeout
/// (spec 3b, "Manejo de errores"). Mensaje accionable pensado para mostrarse tal cual al
/// usuario en los ViewModels (que muestran ex.Message) y en la red global de App.axaml.cs.
/// </summary>
public class ServidorNoDisponibleException : Exception
{
    public const string MensajePorDefecto =
        "No se pudo conectar con el servidor de StockApp. " +
        "Verificá que el servidor esté encendido y accesible en la red, y volvé a intentar.";

    public ServidorNoDisponibleException(Exception? inner = null)
        : base(MensajePorDefecto, inner)
    {
    }
}
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
Expected: PASS (2 tests).

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.ApiClient tests/StockApp.ApiClient.Tests StockApp.sln
git commit -m "feat(apiclient): proyecto StockApp.ApiClient + ServidorNoDisponibleException"
```

## Task 2: `ApiSession : ICurrentSession` — token + evento de sesión vencida

**Files:**
- Create: `src/StockApp.ApiClient/ApiSession.cs`
- Test: `tests/StockApp.ApiClient.Tests/ApiSessionTests.cs`

**Interfaces:**
- Consumes: `ICurrentSession` (`StockApp.Application.Interfaces`: `EstaAutenticado`, `UsuarioActual`, `RolActual`, `IniciarSesion(Usuario)`, `CerrarSesion()`), `UsuarioSesion(int Id, string NombreUsuario, RolUsuario Rol, string? NombreCompleto)` (`StockApp.Application.Auth`).
- Produces: `ApiSession` (clase pública, singleton en el DI): `string? Token { get; }`, `void EstablecerSesion(UsuarioSesion usuario, string token)`, `event Action? SesionVencida`, `internal void DispararSesionVencida()`. Consumida por `AuthTokenHandler` (Task 4), `AuthApiClient` (Task 6) y `App.axaml.cs` (Task 17).

- [ ] **Step 1: Escribir los tests que fallan — `ApiSessionTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ApiSessionTests.cs
using StockApp.ApiClient;
using StockApp.Application.Auth;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class ApiSessionTests
{
    [Fact]
    public void SinSesion_NoEstaAutenticado()
    {
        var session = new ApiSession();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.UsuarioActual);
        Assert.Null(session.RolActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void EstablecerSesion_GuardaSnapshotYToken()
    {
        var session = new ApiSession();

        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, "Ana Admin"), "tok-123");

        Assert.True(session.EstaAutenticado);
        Assert.Equal("admin", session.UsuarioActual!.NombreUsuario);
        Assert.Equal("Ana Admin", session.UsuarioActual.NombreCompleto);
        Assert.Equal(RolUsuario.Admin, session.RolActual);
        Assert.Equal("tok-123", session.Token);
    }

    [Fact]
    public void CerrarSesion_LimpiaSnapshotYToken()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), "tok-123");

        session.CerrarSesion();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.UsuarioActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void IniciarSesion_ProyectaLaEntidadAUnSnapshot_SinToken()
    {
        // Miembro del contrato ICurrentSession: en modo API el login usa EstablecerSesion,
        // pero la implementación se mantiene funcional (misma proyección que InMemorySession).
        var session = new ApiSession();
        var usuario = new Usuario { Id = 2, NombreUsuario = "oper", Rol = RolUsuario.Operador };

        session.IniciarSesion(usuario);

        Assert.True(session.EstaAutenticado);
        Assert.Equal("oper", session.UsuarioActual!.NombreUsuario);
        Assert.Equal(RolUsuario.Operador, session.RolActual);
        Assert.Null(session.Token);
    }

    [Fact]
    public void DispararSesionVencida_InvocaElEvento()
    {
        var session = new ApiSession();
        var disparado = false;
        session.SesionVencida += () => disparado = true;

        session.DispararSesionVencida();

        Assert.True(disparado);
    }

    [Fact]
    public void DispararSesionVencida_SinSuscriptores_NoLanza()
    {
        var session = new ApiSession();

        session.DispararSesionVencida();
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ApiSessionTests`
Expected: FAIL — error de compilación (`ApiSession` no existe).

- [ ] **Step 3: Implementar `ApiSession.cs`**

```csharp
// src/StockApp.ApiClient/ApiSession.cs
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

/// <summary>
/// Sesión del cliente API (spec 3b): reemplaza a InMemorySession en el desktop. Singleton.
/// Se puebla desde el LoginResponse enriquecido (3a, D8) vía EstablecerSesion; además del
/// snapshot de identidad guarda el token JWT que AuthTokenHandler adjunta a cada request.
/// Hilo-segura con lock simple, igual que InMemorySession (la UI de Avalonia puede acceder
/// desde hilos distintos).
/// </summary>
public class ApiSession : ICurrentSession
{
    private readonly object _lock = new();
    private UsuarioSesion? _sesionActual;
    private string? _token;

    /// <summary>
    /// El servidor respondió 401 a un request que llevaba token: la sesión venció.
    /// La composition root lo cablea a la navegación al login con aviso (App.axaml.cs).
    /// </summary>
    public event Action? SesionVencida;

    public bool EstaAutenticado { get { lock (_lock) return _sesionActual != null; } }

    public UsuarioSesion? UsuarioActual { get { lock (_lock) return _sesionActual; } }

    public RolUsuario? RolActual { get { lock (_lock) return _sesionActual?.Rol; } }

    /// <summary>Token JWT vigente, o null si no hay sesión.</summary>
    public string? Token { get { lock (_lock) return _token; } }

    /// <summary>
    /// Miembro de ICurrentSession (proyección entidad → snapshot, igual que InMemorySession).
    /// En modo API nadie lo llama — el login usa <see cref="EstablecerSesion"/> — pero se
    /// implementa funcional para honrar el contrato. No establece token.
    /// </summary>
    public void IniciarSesion(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        var snapshot = new UsuarioSesion(
            usuario.Id,
            usuario.NombreUsuario,
            usuario.Rol,
            usuario.NombreCompleto);
        lock (_lock)
        {
            _sesionActual = snapshot;
            _token        = null;
        }
    }

    /// <summary>Establece la sesión desde el LoginResponse de la API (snapshot + token JWT).</summary>
    public void EstablecerSesion(UsuarioSesion usuario, string token)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        lock (_lock)
        {
            _sesionActual = usuario;
            _token        = token;
        }
    }

    public void CerrarSesion()
    {
        lock (_lock)
        {
            _sesionActual = null;
            _token        = null;
        }
    }

    /// <summary>Lo invoca AuthTokenHandler ante un 401 con token (internal + InternalsVisibleTo).</summary>
    internal void DispararSesionVencida() => SesionVencida?.Invoke();
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ApiSessionTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/ApiSession.cs tests/StockApp.ApiClient.Tests/ApiSessionTests.cs
git commit -m "feat(apiclient): ApiSession como ICurrentSession con token y evento de sesion vencida"
```

## Task 3: `ApiErrores` + `ApiQuery` — traducción centralizada de errores y query strings

**Files:**
- Create: `src/StockApp.ApiClient/ApiErrores.cs`
- Create: `src/StockApp.ApiClient/ApiQuery.cs`
- Test: `tests/StockApp.ApiClient.Tests/ApiErroresTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/ApiQueryTests.cs`

**Interfaces:**
- Consumes: `ServidorNoDisponibleException` (Task 1); `EntidadNoEncontradaException(string)`, `ReglaDeNegocioException(string)`, `StockInsuficienteException(int productoId, decimal stockActual, decimal cantidadSolicitada)` (`StockApp.Domain.Exceptions`, existentes de 3a).
- Produces (todo `internal`, consumido por los 10 clients del Bloque B):
  - `Task<HttpResponseMessage> ApiErrores.EnviarAsync(Func<Task<HttpResponseMessage>> enviar)` — envuelve el envío; `HttpRequestException`/`TaskCanceledException` → `ServidorNoDisponibleException`.
  - `Task ApiErrores.AsegurarExitoAsync(HttpResponseMessage response)` — status no exitoso → excepción de dominio según el mapa de Global Constraints.
  - `internal sealed record IdCreado(int Id)` — shape `{ id }` de los 201 de la API.
  - `string ApiQuery.Construir(params (string Clave, string? Valor)[] parametros)` — arma `"?a=1&b=2"` omitiendo nulos y escapando valores; `""` si no hay ninguno.
  - `string? ApiQuery.Fecha(DateTime? fecha)` — formato round-trip `"O"` o null.

- [ ] **Step 1: Escribir los tests que fallan — `ApiErroresTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ApiErroresTests.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ApiErroresTests
{
    /// <summary>problem+json como lo emite DomainExceptionHandler/JwtBearerEvents de la API.</summary>
    private static HttpResponseMessage Problema(HttpStatusCode status, string? detail, string? title = "Error.")
        => new(status)
        {
            Content = JsonContent.Create(new { title, detail, status = (int)status }),
        };

    [Fact]
    public async Task StatusExitoso_NoLanza()
    {
        await ApiErrores.AsegurarExitoAsync(new HttpResponseMessage(HttpStatusCode.OK));
        await ApiErrores.AsegurarExitoAsync(new HttpResponseMessage(HttpStatusCode.Created));
    }

    [Fact]
    public async Task NotFound_LanzaEntidadNoEncontradaConElDetail()
    {
        var response = Problema(HttpStatusCode.NotFound, "Producto 5 no encontrado.");

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Producto 5 no encontrado.", ex.Message);
    }

    [Fact]
    public async Task Conflict_LanzaReglaDeNegocioConElDetail()
    {
        var response = Problema(HttpStatusCode.Conflict, "Ya existe una categoría con el nombre 'Bebidas'.");

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }

    [Fact]
    public async Task Conflict_ConExtensionesDeStock_ReconstruyeStockInsuficiente()
    {
        // Mina 2: las extensiones estructuradas del problem+json (Task 5, API) permiten
        // reconstruir la excepción tipada que MovimientoRegistroViewModelBase necesita
        // para el flujo "¿forzar salida?" (usa ex.StockResultante).
        var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Regla de negocio violada.",
                detail = "Stock insuficiente para el producto 7.",
                status = 409,
                productoId = 7,
                stockActual = 5.0,
                cantidadSolicitada = 8.0,
            }),
        };

        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal(7, ex.ProductoId);
        Assert.Equal(5m, ex.StockActual);
        Assert.Equal(8m, ex.CantidadSolicitada);
        Assert.Equal(-3m, ex.StockResultante);
    }

    [Fact]
    public async Task BadRequest_LanzaArgumentExceptionConElDetail()
    {
        var response = Problema(HttpStatusCode.BadRequest, "La cantidad debe ser mayor que cero.");

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("La cantidad debe ser mayor que cero.", ex.Message);
    }

    [Fact]
    public async Task Forbidden_LanzaUnauthorizedAccessConElDetail()
    {
        var response = Problema(HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción.");

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("El rol autenticado no tiene permiso para esta acción.", ex.Message);
    }

    [Fact]
    public async Task Unauthorized_LanzaUnauthorizedAccess()
    {
        // El evento de sesión vencida lo dispara AuthTokenHandler (Task 4); acá solo se
        // garantiza que la llamada en curso corta con una excepción que los VMs ya manejan.
        var response = Problema(HttpStatusCode.Unauthorized, "El token es inválido, venció o no fue provisto.");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ApiErrores.AsegurarExitoAsync(response));
    }

    [Fact]
    public async Task Error500_SinDetail_LanzaInvalidOperationConTitleYStatus()
    {
        // La API nunca expone detail en 500 (fail-closed).
        var response = Problema(HttpStatusCode.InternalServerError, detail: null, title: "Error interno.");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Contains("500", ex.Message);
        Assert.Contains("Error interno.", ex.Message);
    }

    [Fact]
    public async Task BodyNoJson_UsaElMensajeGenericoConElStatus()
    {
        var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("<html>proxy error</html>"),
        };

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => ApiErrores.AsegurarExitoAsync(response));

        Assert.Equal("El servidor respondió 404.", ex.Message);
    }

    [Fact]
    public async Task EnviarAsync_HttpRequestException_LanzaServidorNoDisponible()
    {
        var causa = new HttpRequestException("connection refused");

        var ex = await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => ApiErrores.EnviarAsync(() => throw causa));

        Assert.Same(causa, ex.InnerException);
    }

    [Fact]
    public async Task EnviarAsync_TaskCanceled_LanzaServidorNoDisponible()
    {
        // HttpClient.Timeout vencido llega como TaskCanceledException. Los clients no pasan
        // CancellationToken propio, así que toda cancelación acá es timeout.
        await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => ApiErrores.EnviarAsync(() => throw new TaskCanceledException("timeout")));
    }

    [Fact]
    public async Task EnviarAsync_Exito_DevuelveLaResponse()
    {
        var esperada = new HttpResponseMessage(HttpStatusCode.OK);

        var response = await ApiErrores.EnviarAsync(() => Task.FromResult(esperada));

        Assert.Same(esperada, response);
    }
}
```

- [ ] **Step 2: Escribir los tests que fallan — `ApiQueryTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ApiQueryTests.cs
using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests;

public class ApiQueryTests
{
    [Fact]
    public void SinValores_DevuelveVacio()
    {
        Assert.Equal(string.Empty, ApiQuery.Construir(("sku", null), ("nombre", null)));
    }

    [Fact]
    public void OmiteNulosYConcatenaLosPresentes()
    {
        var query = ApiQuery.Construir(("sku", null), ("nombre", "coca"), ("codigoBarras", "779"));

        Assert.Equal("?nombre=coca&codigoBarras=779", query);
    }

    [Fact]
    public void EscapaLosValores()
    {
        var query = ApiQuery.Construir(("texto", "agua c/gas & más"));

        Assert.Equal("?texto=agua%20c%2Fgas%20%26%20m%C3%A1s", query);
    }

    [Fact]
    public void Fecha_UsaFormatoRoundTrip()
    {
        var fecha = new DateTime(2026, 7, 10, 14, 30, 0);

        Assert.Equal("2026-07-10T14:30:00.0000000", ApiQuery.Fecha(fecha));
        Assert.Null(ApiQuery.Fecha(null));
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "ApiErroresTests|ApiQueryTests"`
Expected: FAIL — error de compilación (`ApiErrores`/`ApiQuery` no existen).

- [ ] **Step 4: Implementar `ApiErrores.cs`**

```csharp
// src/StockApp.ApiClient/ApiErrores.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

/// <summary>Shape del body 201 `{ id }` que emiten los POST de la API (sin Location).</summary>
internal sealed record IdCreado(int Id);

/// <summary>
/// Proyección del problem+json (RFC 7807) de la API. Además de title/detail/status,
/// DomainExceptionHandler agrega extensiones estructuradas para StockInsuficienteException
/// (productoId/stockActual/cantidadSolicitada — Task 5 de este plan).
/// ReadFromJsonAsync usa defaults Web: camelCase + case-insensitive.
/// </summary>
internal sealed record ProblemaJson(
    string? Title,
    string? Detail,
    int? Status,
    int? ProductoId,
    decimal? StockActual,
    decimal? CantidadSolicitada);

/// <summary>
/// Traducción centralizada HTTP → excepciones de dominio (spec 3b): UN solo lugar, los
/// 10 XxxApiClient no repiten switches de status ni try/catch de transporte.
/// </summary>
internal static class ApiErrores
{
    /// <summary>
    /// Ejecuta el envío HTTP convirtiendo los fallos de transporte en
    /// <see cref="ServidorNoDisponibleException"/> (conexión rechazada, DNS, timeout).
    /// </summary>
    internal static async Task<HttpResponseMessage> EnviarAsync(Func<Task<HttpResponseMessage>> enviar)
    {
        try
        {
            return await enviar();
        }
        catch (HttpRequestException ex)
        {
            throw new ServidorNoDisponibleException(ex);
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient.Timeout vencido: llega como TaskCanceledException (inner TimeoutException).
            // Los clients no pasan CancellationToken propio → toda cancelación es timeout.
            throw new ServidorNoDisponibleException(ex);
        }
    }

    /// <summary>
    /// Si el status no es exitoso, lanza la excepción de dominio correspondiente con el
    /// detail del problem+json como mensaje (los ViewModels muestran ex.Message tal cual).
    /// </summary>
    internal static async Task AsegurarExitoAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var problema = await LeerProblemaAsync(response);
        var mensaje = problema?.Detail
            ?? problema?.Title
            ?? $"El servidor respondió {(int)response.StatusCode}.";

        throw response.StatusCode switch
        {
            HttpStatusCode.NotFound     => new EntidadNoEncontradaException(mensaje),
            HttpStatusCode.Conflict     => CrearConflicto(problema, mensaje),
            HttpStatusCode.BadRequest   => new ArgumentException(mensaje),
            HttpStatusCode.Forbidden    => new UnauthorizedAccessException(mensaje),
            HttpStatusCode.Unauthorized => new UnauthorizedAccessException(mensaje),
            _ => new InvalidOperationException(
                $"Error inesperado del servidor ({(int)response.StatusCode}): {mensaje}"),
        };
    }

    /// <summary>
    /// 409 con extensiones de stock → StockInsuficienteException reconstruida con el MISMO
    /// constructor que usó el servidor (mensaje y StockResultante idénticos) — preserva el
    /// flujo "¿forzar salida?" de MovimientoRegistroViewModelBase. Cualquier otro 409 →
    /// ReglaDeNegocioException con el detail del servidor.
    /// </summary>
    private static Exception CrearConflicto(ProblemaJson? problema, string mensaje)
    {
        if (problema is { ProductoId: int productoId, StockActual: decimal stockActual, CantidadSolicitada: decimal cantidadSolicitada })
            return new StockInsuficienteException(productoId, stockActual, cantidadSolicitada);

        return new ReglaDeNegocioException(mensaje);
    }

    private static async Task<ProblemaJson?> LeerProblemaAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemaJson>();
        }
        catch (Exception)
        {
            // Body vacío o no-JSON (proxy, HTML de error): se cae al mensaje genérico.
            return null;
        }
    }
}
```

- [ ] **Step 5: Implementar `ApiQuery.cs`**

```csharp
// src/StockApp.ApiClient/ApiQuery.cs
namespace StockApp.ApiClient;

/// <summary>
/// Armado de query strings para los GET con filtros (/productos, /movimientos/historial,
/// /reportes/*, /auditoria): omite parámetros nulos y escapa los valores.
/// </summary>
internal static class ApiQuery
{
    internal static string Construir(params (string Clave, string? Valor)[] parametros)
    {
        var partes = parametros
            .Where(p => p.Valor is not null)
            .Select(p => $"{p.Clave}={Uri.EscapeDataString(p.Valor!)}")
            .ToList();

        return partes.Count == 0 ? string.Empty : "?" + string.Join("&", partes);
    }

    /// <summary>Formato round-trip "O": lo parsea el binding DateTime de Minimal APIs sin pérdida.</summary>
    internal static string? Fecha(DateTime? fecha) => fecha?.ToString("O");
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "ApiErroresTests|ApiQueryTests"`
Expected: PASS (16 tests).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.ApiClient/ApiErrores.cs src/StockApp.ApiClient/ApiQuery.cs tests/StockApp.ApiClient.Tests/ApiErroresTests.cs tests/StockApp.ApiClient.Tests/ApiQueryTests.cs
git commit -m "feat(apiclient): traduccion centralizada de errores HTTP a excepciones de dominio"
```

## Task 4: `AuthTokenHandler` — Bearer en cada request, 401 → sesión vencida

**Files:**
- Create: `src/StockApp.ApiClient/AuthTokenHandler.cs`
- Create: `tests/StockApp.ApiClient.Tests/TestInfra/FakeHttpHandler.cs`
- Create: `tests/StockApp.ApiClient.Tests/TestInfra/TestHttp.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs`

**Interfaces:**
- Consumes: `ApiSession` (Task 2: `Token`, `CerrarSesion()`, `DispararSesionVencida()`).
- Produces: `AuthTokenHandler(ApiSession session) : DelegatingHandler` — el `HttpClient` singleton del desktop se construye con este handler (Task 17).
- Produces (infra de tests, consumida por TODOS los tests del Bloque B):
  - `FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)` con `HttpRequestMessage? UltimaRequest` y `string? UltimoBody`.
  - `HttpClient TestHttp.CrearCliente(FakeHttpHandler fake, ApiSession? session = null)` — `BaseAddress = "http://localhost:5000/"` con `AuthTokenHandler` encadenado.
  - `HttpResponseMessage TestHttp.Json(object body, HttpStatusCode status = OK)` y `HttpResponseMessage TestHttp.Problema(HttpStatusCode status, string? detail, string? title = "Error.")`.

- [ ] **Step 1: Crear la infraestructura de tests — `FakeHttpHandler.cs` y `TestHttp.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/TestInfra/FakeHttpHandler.cs
namespace StockApp.ApiClient.Tests.TestInfra;

/// <summary>
/// HttpMessageHandler falso: captura la última request (método, URI, body serializado)
/// y responde lo que indique el responder. Doble de test de TODOS los XxxApiClientTests.
/// </summary>
public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? UltimaRequest { get; private set; }
    public string? UltimoBody { get; private set; }

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        UltimaRequest = request;
        UltimoBody = request.Content is null
            ? null
            : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}
```

```csharp
// tests/StockApp.ApiClient.Tests/TestInfra/TestHttp.cs
using System.Net;
using System.Text;
using System.Text.Json;
using StockApp.ApiClient;

namespace StockApp.ApiClient.Tests.TestInfra;

public static class TestHttp
{
    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// HttpClient con la MISMA cadena que arma App.axaml.cs (AuthTokenHandler → transporte),
    /// pero con el transporte falso. BaseAddress con "/" final, igual que en producción.
    /// </summary>
    public static HttpClient CrearCliente(FakeHttpHandler fake, ApiSession? session = null)
    {
        var handler = new AuthTokenHandler(session ?? new ApiSession()) { InnerHandler = fake };
        return new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000/") };
    }

    public static HttpResponseMessage Json(object body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonWeb), Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Problema(HttpStatusCode status, string? detail, string? title = "Error.")
        => Json(new { title, detail, status = (int)status }, status);
}
```

- [ ] **Step 2: Escribir los tests que fallan — `AuthTokenHandlerTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/AuthTokenHandlerTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthTokenHandlerTests
{
    private static ApiSession SesionConToken(string token = "tok-123")
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), token);
        return session;
    }

    [Fact]
    public async Task ConToken_AdjuntaAuthorizationBearer()
    {
        var session = SesionConToken();
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake, session);

        await http.GetAsync("categorias");

        Assert.Equal("Bearer", fake.UltimaRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("tok-123", fake.UltimaRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SinToken_NoAdjuntaHeader()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake);

        await http.GetAsync("auth/primer-arranque");

        Assert.Null(fake.UltimaRequest!.Headers.Authorization);
    }

    [Fact]
    public async Task Un401ConToken_CierraSesionYDisparaElEvento()
    {
        var session = SesionConToken();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var http = TestHttp.CrearCliente(fake, session);

        var response = await http.GetAsync("productos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(disparado);
        Assert.False(session.EstaAutenticado);
        Assert.Null(session.Token);
    }

    [Fact]
    public async Task Un401SinToken_NoDisparaElEvento()
    {
        // POST /auth/login con credenciales inválidas devuelve 401 sin que hubiera token:
        // eso NO es sesión vencida (lo maneja AuthApiClient como LoginResult.Fallo).
        var session = new ApiSession();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var http = TestHttp.CrearCliente(fake, session);

        await http.PostAsync("auth/login", null);

        Assert.False(disparado);
    }

    [Fact]
    public async Task Un200ConToken_NoDisparaElEvento()
    {
        var session = SesionConToken();
        var disparado = false;
        session.SesionVencida += () => disparado = true;
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var http = TestHttp.CrearCliente(fake, session);

        await http.GetAsync("categorias");

        Assert.False(disparado);
        Assert.True(session.EstaAutenticado);
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AuthTokenHandlerTests`
Expected: FAIL — error de compilación (`AuthTokenHandler` no existe).

- [ ] **Step 4: Implementar `AuthTokenHandler.cs`**

```csharp
// src/StockApp.ApiClient/AuthTokenHandler.cs
using System.Net;
using System.Net.Http.Headers;

namespace StockApp.ApiClient;

/// <summary>
/// Adjunta `Authorization: Bearer` a cada request con el token de ApiSession, y detecta
/// la sesión vencida en UN solo lugar (spec 3b): un 401 a un request QUE LLEVABA token
/// cierra la sesión y dispara ApiSession.SesionVencida (el Shell navega al login con
/// aviso). El 401 del login (sin token, credenciales malas) NO dispara el evento.
/// </summary>
public class AuthTokenHandler : DelegatingHandler
{
    private readonly ApiSession _session;

    public AuthTokenHandler(ApiSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = _session.Token;
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized && token is not null)
        {
            _session.CerrarSesion();
            _session.DispararSesionVencida();
        }

        return response;
    }
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AuthTokenHandlerTests`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.ApiClient/AuthTokenHandler.cs tests/StockApp.ApiClient.Tests
git commit -m "feat(apiclient): AuthTokenHandler adjunta Bearer y detecta sesion vencida"
```

## Task 5: API — `DomainExceptionHandler` expone los datos de `StockInsuficienteException` como extensiones

**Files:**
- Modify: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Modify: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: `StockInsuficienteException` (`ProductoId`, `StockActual`, `CantidadSolicitada` — propiedades públicas existentes).
- Produces: el problem+json de un 409 por stock insuficiente incluye las claves top-level `productoId` (int), `stockActual` (decimal) y `cantidadSolicitada` (decimal) — las consume `ApiErrores.CrearConflicto` (Task 3). Cambio ADITIVO: title/detail/status no cambian; ningún test existente de 3a se ve afectado.

- [ ] **Step 1: Escribir los tests que fallan en `DomainExceptionHandlerTests.cs`** (agregar al final de la clase, antes del `}` de cierre — el helper `EjecutarAsync(Exception)` ya existe en ese archivo)

```csharp

    [Fact]
    public async Task StockInsuficienteException_IncluyeLosDatosEstructuradosComoExtensiones()
    {
        // Fase 3b (Mina 2): el cliente HTTP del desktop reconstruye StockInsuficienteException
        // desde estas extensiones para preservar el flujo "¿forzar salida?" del ViewModel
        // (que usa ex.StockResultante). Sin ellas, el 409 solo permite un ReglaDeNegocio plano.
        var (status, _, body) = await EjecutarAsync(new StockInsuficienteException(7, 5m, 8m));

        Assert.Equal(StatusCodes.Status409Conflict, status);
        Assert.Equal(7, body.RootElement.GetProperty("productoId").GetInt32());
        Assert.Equal(5m, body.RootElement.GetProperty("stockActual").GetDecimal());
        Assert.Equal(8m, body.RootElement.GetProperty("cantidadSolicitada").GetDecimal());
    }

    [Fact]
    public async Task ReglaDeNegocioException_NoIncluyeLasExtensionesDeStock()
    {
        var (_, _, body) = await EjecutarAsync(new ReglaDeNegocioException("Ya existe."));

        Assert.False(body.RootElement.TryGetProperty("productoId", out _));
        Assert.False(body.RootElement.TryGetProperty("stockActual", out _));
    }
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: FAIL — `StockInsuficienteException_IncluyeLosDatosEstructuradosComoExtensiones` (la property `productoId` no existe en el body). Los demás siguen en verde. Nota: estos tests son unitarios (sin `WebApplicationFactory`), no requieren Docker.

- [ ] **Step 3: Modificar `TryHandleAsync` en `DomainExceptionHandler.cs`** — reemplazar el bloque final (desde `var problemDetailsService = ...` hasta el `});` de cierre del `TryWriteAsync`) por:

```csharp
        var problemDetailsService = httpContext.RequestServices.GetRequiredService<IProblemDetailsService>();

        var contexto = new ProblemDetailsContext
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
        };

        // Fase 3b: datos estructurados para que el cliente HTTP del desktop reconstruya
        // StockInsuficienteException tipada (el flujo "¿forzar salida?" del ViewModel usa
        // StockResultante). Cambio aditivo: title/detail/status no cambian.
        if (exception is StockInsuficienteException stock)
        {
            contexto.ProblemDetails.Extensions["productoId"]         = stock.ProductoId;
            contexto.ProblemDetails.Extensions["stockActual"]        = stock.StockActual;
            contexto.ProblemDetails.Extensions["cantidadSolicitada"] = stock.CantidadSolicitada;
        }

        return await problemDetailsService.TryWriteAsync(contexto);
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter DomainExceptionHandlerTests`
Expected: PASS (todos, incluidos los 2 nuevos).

- [ ] **Step 5: Correr la suite completa de la API** (requiere Docker para los tests de integración, patrón Testcontainers existente)

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASS, 0 failures — el cambio es aditivo, ningún assert existente inspecciona extensiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs
git commit -m "feat(api): ProblemDetails de StockInsuficienteException con datos estructurados"
```

---

## Bloque B — Los 10 ApiClients (Tasks 6-14)

Un client por interfaz de Application, todos con el mismo patrón: `ApiErrores.EnviarAsync` → `ApiErrores.AsegurarExitoAsync` → `ReadFromJsonAsync`. Los tests usan `FakeHttpHandler`/`TestHttp` (Task 4) y assertan método, ruta (`AbsolutePath`/`PathAndQuery`), body serializado y el mapeo de la response — más al menos un caso de traducción de error por client. Rutas SIEMPRE relativas sin `/` inicial (Global Constraints).

## Task 6: `AuthApiClient : IAuthService` + `PrimerArranqueApiClient : IPrimerArranqueService`

**Files:**
- Create: `src/StockApp.ApiClient/AuthApiClient.cs`
- Create: `src/StockApp.ApiClient/PrimerArranqueApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuthApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs`

**Interfaces:**
- Consumes: `IAuthService` (`Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)`, `Task LogoutAsync()`); `LoginResult.Ok()/.Fallo(LoginError)`; `IPrimerArranqueService` (`Task<bool> RequiereCrearAdminAsync()`, `Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)`); `ApiSession.EstablecerSesion/CerrarSesion` (Task 2); `ApiErrores` (Task 3).
- Consumes (contrato HTTP de la API): `POST /auth/login` → 200 `{token, usuario:{id,nombreUsuario,nombreCompleto,rol}}` / 401 problem+json (credenciales inválidas o usuario inactivo, sin distinguir — anti-enumeración); `GET /auth/primer-arranque` → 200 `{requiereCrearAdmin}`; `POST /auth/primer-admin` → 201 (una vez) / 409 / 400.
- Produces: `AuthApiClient(HttpClient http, ApiSession session)` y `PrimerArranqueApiClient(HttpClient http)` — registrados en el DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `AuthApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/AuthApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuthApiClientTests
{
    private static readonly object LoginOkBody = new
    {
        token = "tok-1",
        usuario = new { id = 1, nombreUsuario = "admin", nombreCompleto = "Ana Admin", rol = 0 },
    };

    [Fact]
    public async Task Login_Exitoso_POSTAuthLogin_EstableceLaSesionYDevuelveOk()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ => TestHttp.Json(LoginOkBody));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var resultado = await client.LoginAsync("admin", "admin123");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/login", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"admin\"", fake.UltimoBody);
        Assert.Contains("\"contrasena\":\"admin123\"", fake.UltimoBody);
        Assert.True(resultado.Exitoso);
        Assert.True(session.EstaAutenticado);
        Assert.Equal("tok-1", session.Token);
        Assert.Equal(1, session.UsuarioActual!.Id);
        Assert.Equal("Ana Admin", session.UsuarioActual.NombreCompleto);
        Assert.Equal(RolUsuario.Admin, session.RolActual);
    }

    [Fact]
    public async Task Login_401_DevuelveFalloSinEstablecerSesion()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ =>
            TestHttp.Problema(HttpStatusCode.Unauthorized, null, "Usuario o contraseña inválidos."));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        var resultado = await client.LoginAsync("admin", "mala");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.ContrasenaInvalida, resultado.Error);
        Assert.False(session.EstaAutenticado);
    }

    [Fact]
    public async Task Login_LimpiaLaSesionAnteriorAntesDeIntentar()
    {
        // Un login nuevo invalida la sesión previa y evita adjuntar un token viejo al request.
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(9, "viejo", RolUsuario.Operador, null), "tok-viejo");
        var fake = new FakeHttpHandler(_ => TestHttp.Json(LoginOkBody));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await client.LoginAsync("admin", "admin123");

        Assert.Null(fake.UltimaRequest!.Headers.Authorization);
        Assert.Equal("tok-1", session.Token);
    }

    [Fact]
    public async Task Login_ServidorCaido_LanzaServidorNoDisponible()
    {
        var session = new ApiSession();
        var fake = new FakeHttpHandler(_ => throw new HttpRequestException("connection refused"));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await Assert.ThrowsAsync<ServidorNoDisponibleException>(
            () => client.LoginAsync("admin", "admin123"));
        Assert.False(session.EstaAutenticado);
    }

    [Fact]
    public async Task Logout_CierraLaSesionSinLlamarAlServidor()
    {
        var session = new ApiSession();
        session.EstablecerSesion(new UsuarioSesion(1, "admin", RolUsuario.Admin, null), "tok-1");
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new AuthApiClient(TestHttp.CrearCliente(fake, session), session);

        await client.LogoutAsync();

        Assert.False(session.EstaAutenticado);
        Assert.Null(session.Token);
        Assert.Null(fake.UltimaRequest); // JWT sin estado: no existe endpoint de logout.
    }
}
```

- [ ] **Step 2: Escribir los tests que fallan — `PrimerArranqueApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class PrimerArranqueApiClientTests
{
    [Fact]
    public async Task RequiereCrearAdmin_GETAuthPrimerArranque_DevuelveElFlag()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { requiereCrearAdmin = true }));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        var requiere = await client.RequiereCrearAdminAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/primer-arranque", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.True(requiere);
    }

    [Fact]
    public async Task RequiereCrearAdmin_False_DevuelveFalse()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { requiereCrearAdmin = false }));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        Assert.False(await client.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task CrearAdminInicial_POSTAuthPrimerAdmin_ConElBodyCorrecto()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        await client.CrearAdminInicialAsync("admin", "admin123");

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/auth/primer-admin", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"admin\"", fake.UltimoBody);
        Assert.Contains("\"contrasena\":\"admin123\"", fake.UltimoBody);
    }

    [Fact]
    public async Task CrearAdminInicial_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict,
            "No se puede crear el Admin inicial: ya existen usuarios en la base de datos."));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.CrearAdminInicialAsync("admin", "admin123"));

        Assert.Equal(
            "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.",
            ex.Message);
    }

    [Fact]
    public async Task CrearAdminInicial_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "La contraseña debe tener al menos 6 caracteres."));
        var client = new PrimerArranqueApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ArgumentException>(
            () => client.CrearAdminInicialAsync("admin", "corta"));
    }
}
```

- [ ] **Step 3: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "AuthApiClientTests|PrimerArranqueApiClientTests"`
Expected: FAIL — error de compilación (los clients no existen).

- [ ] **Step 4: Implementar `AuthApiClient.cs`**

```csharp
// src/StockApp.ApiClient/AuthApiClient.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.Application.Auth;

namespace StockApp.ApiClient;

internal sealed record LoginBody(string NombreUsuario, string Contrasena);
internal sealed record UsuarioLoginWire(int Id, string NombreUsuario, string? NombreCompleto, StockApp.Domain.Enums.RolUsuario Rol);
internal sealed record LoginRespuestaWire(string Token, UsuarioLoginWire Usuario);

/// <summary>
/// IAuthService contra POST /auth/login (3a, D8: LoginResponse enriquecido). Puebla
/// ApiSession con el snapshot del usuario + token; el 401 del login se traduce a
/// LoginResult.Fallo (la UI muestra un único mensaje genérico — anti-enumeración).
/// </summary>
public sealed class AuthApiClient : IAuthService
{
    private readonly HttpClient _http;
    private readonly ApiSession _session;

    public AuthApiClient(HttpClient http, ApiSession session)
    {
        _http    = http;
        _session = session;
    }

    public async Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
    {
        // Un login nuevo invalida cualquier sesión previa (y evita adjuntar un token viejo).
        _session.CerrarSesion();

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/login", new LoginBody(nombreUsuario, contrasena)));

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            // El servidor no distingue usuario inexistente / contraseña mala / inactivo
            // (anti-enumeración) y la UI tampoco: cualquier LoginError produce el mismo mensaje.
            return LoginResult.Fallo(LoginError.ContrasenaInvalida);
        }

        await ApiErrores.AsegurarExitoAsync(response);

        var body = await response.Content.ReadFromJsonAsync<LoginRespuestaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el login.");

        _session.EstablecerSesion(
            new UsuarioSesion(body.Usuario.Id, body.Usuario.NombreUsuario, body.Usuario.Rol, body.Usuario.NombreCompleto),
            body.Token);

        return LoginResult.Ok();
    }

    public Task LogoutAsync()
    {
        // JWT sin estado: no hay endpoint de logout; alcanza con descartar el token local.
        _session.CerrarSesion();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 5: Implementar `PrimerArranqueApiClient.cs`**

```csharp
// src/StockApp.ApiClient/PrimerArranqueApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Auth;

namespace StockApp.ApiClient;

internal sealed record PrimerArranqueEstadoWire(bool RequiereCrearAdmin);
internal sealed record CrearAdminInicialBody(string NombreUsuario, string Contrasena);

/// <summary>
/// IPrimerArranqueService contra los endpoints bootstrap anónimos de 3a (D7):
/// GET /auth/primer-arranque y POST /auth/primer-admin. PrimerArranqueViewModel no cambia.
/// </summary>
public sealed class PrimerArranqueApiClient : IPrimerArranqueService
{
    private readonly HttpClient _http;

    public PrimerArranqueApiClient(HttpClient http) => _http = http;

    public async Task<bool> RequiereCrearAdminAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("auth/primer-arranque"));
        await ApiErrores.AsegurarExitoAsync(response);

        var estado = await response.Content.ReadFromJsonAsync<PrimerArranqueEstadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el bootstrap.");
        return estado.RequiereCrearAdmin;
    }

    public async Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("auth/primer-admin", new CrearAdminInicialBody(nombreUsuario, contrasenaPlana)));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter "AuthApiClientTests|PrimerArranqueApiClientTests"`
Expected: PASS (10 tests).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.ApiClient/AuthApiClient.cs src/StockApp.ApiClient/PrimerArranqueApiClient.cs tests/StockApp.ApiClient.Tests/AuthApiClientTests.cs tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs
git commit -m "feat(apiclient): AuthApiClient y PrimerArranqueApiClient"
```

## Task 7: `CategoriaApiClient : ICategoriaService`

**Files:**
- Create: `src/StockApp.ApiClient/CategoriaApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/CategoriaApiClientTests.cs`

**Interfaces:**
- Consumes: `ICategoriaService` (`Task<int> AltaAsync(Categoria)`, `Task ModificarAsync(Categoria)`, `Task BajaLogicaAsync(int)`, `Task<IReadOnlyList<Categoria>> ListarTodasAsync()`, `Task<IReadOnlyList<Categoria>> ListarActivasAsync()`); entidad `Categoria { Id, Nombre, Activo }`; `ApiErrores`/`IdCreado` (Task 3).
- Consumes (HTTP): `GET /categorias` y `GET /categorias/activas` → `[{id,nombre,activo}]` (`CategoriaDto`, 3a D3); `POST /categorias` body `{nombre}` → 201 `{id}`; `PUT /categorias/{id}` body `{nombre}` (SIN `Id` — 3a D1); `DELETE /categorias/{id}` → 200.
- Produces: `CategoriaApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `CategoriaApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/CategoriaApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class CategoriaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETCategorias_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Bebidas", activo = true },
            new { id = 2, nombre = "Limpieza", activo = false },
        }));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var categorias = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, categorias.Count);
        Assert.Equal(1, categorias[0].Id);
        Assert.Equal("Bebidas", categorias[0].Nombre);
        Assert.False(categorias[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETCategoriasActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var categorias = await client.ListarActivasAsync();

        Assert.Equal("/categorias/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(categorias);
    }

    [Fact]
    public async Task Alta_POSTCategorias_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Categoria { Nombre = "Bebidas" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Bebidas\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        // 3a, D1: el id viaja SOLO en la ruta; el body no lo repite.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Categoria { Id = 3, Nombre = "Bebidas y Licores" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Bebidas y Licores\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETECategoriasId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/categorias/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una categoría con el nombre 'Bebidas'."));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new Categoria { Nombre = "Bebidas" }));

        Assert.Equal("Ya existe una categoría con el nombre 'Bebidas'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Categoría 99 no encontrada."));
        var client = new CategoriaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter CategoriaApiClientTests`
Expected: FAIL — error de compilación (`CategoriaApiClient` no existe).

- [ ] **Step 3: Implementar `CategoriaApiClient.cs`**

```csharp
// src/StockApp.ApiClient/CategoriaApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record CategoriaWire(int Id, string Nombre, bool Activo);
internal sealed record CategoriaBody(string Nombre);

/// <summary>
/// ICategoriaService contra /categorias. La interfaz habla en entidades de dominio
/// (así la consumen los VMs) y el wire habla en CategoriaDto (3a, D3): este client mapea.
/// </summary>
public sealed class CategoriaApiClient : ICategoriaService
{
    private readonly HttpClient _http;

    public CategoriaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Categoria categoria)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("categorias", new CategoriaBody(categoria.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la categoría.");
        return creado.Id;
    }

    public async Task ModificarAsync(Categoria categoria)
    {
        // 3a, D1: el id de ruta es la única fuente; el body no lleva Id.
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"categorias/{categoria.Id}", new CategoriaBody(categoria.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"categorias/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<Categoria>> ListarTodasAsync() => ListarAsync("categorias");

    public Task<IReadOnlyList<Categoria>> ListarActivasAsync() => ListarAsync("categorias/activas");

    private async Task<IReadOnlyList<Categoria>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<CategoriaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static Categoria AEntidad(CategoriaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter CategoriaApiClientTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/CategoriaApiClient.cs tests/StockApp.ApiClient.Tests/CategoriaApiClientTests.cs
git commit -m "feat(apiclient): CategoriaApiClient"
```

## Task 8: `ProveedorApiClient : IProveedorService`

**Files:**
- Create: `src/StockApp.ApiClient/ProveedorApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/ProveedorApiClientTests.cs`

**Interfaces:**
- Consumes: `IProveedorService` (`Task<int> AltaAsync(Proveedor)`, `Task ModificarAsync(Proveedor)`, `Task BajaLogicaAsync(int)`, `Task<IReadOnlyList<Proveedor>> ListarTodosAsync()` — sin `/activas`, asimetría real del dominio); entidad `Proveedor { Id, Nombre, Telefono?, Email?, Direccion?, Notas?, Activo }`; `ApiErrores`/`IdCreado` (Task 3).
- Consumes (HTTP): `GET /proveedores` → `[ProveedorDto]`; `POST /proveedores` body `{nombre,telefono,email,direccion,notas}` → 201 `{id}`; `PUT /proveedores/{id}` (sin `Id` en body); `DELETE /proveedores/{id}`.
- Produces: `ProveedorApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `ProveedorApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ProveedorApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ProveedorApiClientTests
{
    [Fact]
    public async Task ListarTodos_GETProveedores_MapeaTodosLosCampos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombre = "Distribuidora Sur", telefono = "099123456",
                email = "ventas@sur.com.uy", direccion = "Ruta 21 km 2", notas = (string?)null,
                activo = true,
            },
        }));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var proveedores = await client.ListarTodosAsync();

        Assert.Equal("/proveedores", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var p = Assert.Single(proveedores);
        Assert.Equal(1, p.Id);
        Assert.Equal("Distribuidora Sur", p.Nombre);
        Assert.Equal("099123456", p.Telefono);
        Assert.Equal("ventas@sur.com.uy", p.Email);
        Assert.Equal("Ruta 21 km 2", p.Direccion);
        Assert.Null(p.Notas);
        Assert.True(p.Activo);
    }

    [Fact]
    public async Task Alta_POSTProveedores_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 5 }, HttpStatusCode.Created));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Proveedor { Nombre = "Distribuidora Sur", Telefono = "099123456" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Distribuidora Sur\"", fake.UltimoBody);
        Assert.Contains("\"telefono\":\"099123456\"", fake.UltimoBody);
        Assert.Equal(5, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Proveedor { Id = 2, Nombre = "Sur SRL", Email = "sur@srl.uy" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores/2", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"email\":\"sur@srl.uy\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEProveedoresId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(2);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/proveedores/2", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "El proveedor ya está inactivo."));
        var client = new ProveedorApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(2));

        Assert.Equal("El proveedor ya está inactivo.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ProveedorApiClientTests`
Expected: FAIL — error de compilación (`ProveedorApiClient` no existe).

- [ ] **Step 3: Implementar `ProveedorApiClient.cs`**

```csharp
// src/StockApp.ApiClient/ProveedorApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ProveedorWire(
    int Id, string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas, bool Activo);
internal sealed record ProveedorBody(
    string Nombre, string? Telefono, string? Email, string? Direccion, string? Notas);

/// <summary>IProveedorService contra /proveedores (sin variante /activas: asimetría real del dominio).</summary>
public sealed class ProveedorApiClient : IProveedorService
{
    private readonly HttpClient _http;

    public ProveedorApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Proveedor proveedor)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("proveedores", ABody(proveedor)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el proveedor.");
        return creado.Id;
    }

    public async Task ModificarAsync(Proveedor proveedor)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"proveedores/{proveedor.Id}", ABody(proveedor)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"proveedores/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<Proveedor>> ListarTodosAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("proveedores"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<ProveedorWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static ProveedorBody ABody(Proveedor p)
        => new(p.Nombre, p.Telefono, p.Email, p.Direccion, p.Notas);

    private static Proveedor AEntidad(ProveedorWire dto) => new()
    {
        Id        = dto.Id,
        Nombre    = dto.Nombre,
        Telefono  = dto.Telefono,
        Email     = dto.Email,
        Direccion = dto.Direccion,
        Notas     = dto.Notas,
        Activo    = dto.Activo,
    };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ProveedorApiClientTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/ProveedorApiClient.cs tests/StockApp.ApiClient.Tests/ProveedorApiClientTests.cs
git commit -m "feat(apiclient): ProveedorApiClient"
```

## Task 9: `UnidadMedidaApiClient : IUnidadMedidaService`

**Files:**
- Create: `src/StockApp.ApiClient/UnidadMedidaApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/UnidadMedidaApiClientTests.cs`

**Interfaces:**
- Consumes: `IUnidadMedidaService` (`Task<int> AltaAsync(UnidadMedida)`, `Task ModificarAsync(UnidadMedida)`, `Task BajaLogicaAsync(int)`, `Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync()`, `Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync()`, `Task<UnidadMedida> GarantizarUnidadPorDefectoAsync()`); entidad `UnidadMedida { Id, Nombre, Abreviatura, Activo }`; `ApiErrores`/`IdCreado` (Task 3).
- Consumes (HTTP): `GET /unidades-medida` y `/activas` → `[{id,nombre,abreviatura,activo}]`; `POST /unidades-medida` body `{nombre,abreviatura}` → 201 `{id}`; `PUT /unidades-medida/{id}` (sin `Id` en body); `DELETE /unidades-medida/{id}`; `POST /unidades-medida/garantizar-por-defecto` → 200 `UnidadMedidaDto` (idempotente, 3a D6).
- Produces: `UnidadMedidaApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `UnidadMedidaApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/UnidadMedidaApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class UnidadMedidaApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETUnidadesMedida_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Unidad", abreviatura = "u", activo = true },
            new { id = 2, nombre = "Kilo", abreviatura = "kg", activo = false },
        }));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var unidades = await client.ListarTodasAsync();

        Assert.Equal("/unidades-medida", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(2, unidades.Count);
        Assert.Equal("u", unidades[0].Abreviatura);
        Assert.False(unidades[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETUnidadesMedidaActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.ListarActivasAsync();

        Assert.Equal("/unidades-medida/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_POSTUnidadesMedida_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 3 }, HttpStatusCode.Created));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new UnidadMedida { Nombre = "Litro", Abreviatura = "l" });

        Assert.Equal("/unidades-medida", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Litro\"", fake.UltimoBody);
        Assert.Contains("\"abreviatura\":\"l\"", fake.UltimoBody);
        Assert.Equal(3, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new UnidadMedida { Id = 3, Nombre = "Litros", Abreviatura = "lt" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEUnidadesMedidaId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(3);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task GarantizarPorDefecto_POSTSinBody_DevuelveLaEntidad()
    {
        // 3a, D6: idempotente — misma unidad "Unidad" en cada llamada, sin duplicar.
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 1, nombre = "Unidad", abreviatura = "u", activo = true }));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var unidad = await client.GarantizarUnidadPorDefectoAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/unidades-medida/garantizar-por-defecto", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(1, unidad.Id);
        Assert.Equal("Unidad", unidad.Nombre);
    }

    [Fact]
    public async Task Baja_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "La unidad de medida ya está inactiva."));
        var client = new UnidadMedidaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(3));

        Assert.Equal("La unidad de medida ya está inactiva.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter UnidadMedidaApiClientTests`
Expected: FAIL — error de compilación (`UnidadMedidaApiClient` no existe).

- [ ] **Step 3: Implementar `UnidadMedidaApiClient.cs`**

```csharp
// src/StockApp.ApiClient/UnidadMedidaApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record UnidadMedidaWire(int Id, string Nombre, string Abreviatura, bool Activo);
internal sealed record UnidadMedidaBody(string Nombre, string Abreviatura);

/// <summary>IUnidadMedidaService contra /unidades-medida, incluido garantizar-por-defecto (3a, D6).</summary>
public sealed class UnidadMedidaApiClient : IUnidadMedidaService
{
    private readonly HttpClient _http;

    public UnidadMedidaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(UnidadMedida unidadMedida)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("unidades-medida", ABody(unidadMedida)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la unidad de medida.");
        return creado.Id;
    }

    public async Task ModificarAsync(UnidadMedida unidadMedida)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"unidades-medida/{unidadMedida.Id}", ABody(unidadMedida)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"unidades-medida/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<UnidadMedida>> ListarTodasAsync() => ListarAsync("unidades-medida");

    public Task<IReadOnlyList<UnidadMedida>> ListarActivasAsync() => ListarAsync("unidades-medida/activas");

    public async Task<UnidadMedida> GarantizarUnidadPorDefectoAsync()
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("unidades-medida/garantizar-por-defecto", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        var dto = await response.Content.ReadFromJsonAsync<UnidadMedidaWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al garantizar la unidad por defecto.");
        return AEntidad(dto);
    }

    private async Task<IReadOnlyList<UnidadMedida>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<UnidadMedidaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static UnidadMedidaBody ABody(UnidadMedida u) => new(u.Nombre, u.Abreviatura);

    private static UnidadMedida AEntidad(UnidadMedidaWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Abreviatura = dto.Abreviatura, Activo = dto.Activo };
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter UnidadMedidaApiClientTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/UnidadMedidaApiClient.cs tests/StockApp.ApiClient.Tests/UnidadMedidaApiClientTests.cs
git commit -m "feat(apiclient): UnidadMedidaApiClient"
```

## Task 10: `ProductoApiClient : IProductoService`

**Files:**
- Create: `src/StockApp.ApiClient/ProductoApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/ProductoApiClientTests.cs`

**Interfaces:**
- Consumes: `IProductoService` (`Task<int> AltaAsync(Producto)`, `Task ModificarAsync(Producto)`, `Task BajaLogicaAsync(int)`, `Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)`, `Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)`, `Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)`); `ProductoDto` (Application, 16 campos — va DIRECTO por el wire, sin mapeo); entidad `Producto`; `ApiErrores`/`ApiQuery`/`IdCreado` (Task 3).
- Consumes (HTTP): `GET /productos?texto=|sku=&codigoBarras=&nombre=` (todos ausentes = listar todo, 3a D5) → `[ProductoDto]`; `POST /productos` body `{codigo,codigoBarras,nombre,descripcion,categoriaId,proveedorId,unidadMedidaId,precioCosto,precioVenta,stockMinimo}` → 201 `{id}`; `PUT /productos/{id}` (mismo body, sin `Id`); `DELETE /productos/{id}`; `PUT /productos/{id}/precio` body `{precioCosto,precioVenta}`.
- Produces: `ProductoApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `ProductoApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ProductoApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class ProductoApiClientTests
{
    private static readonly object ProductoJson = new
    {
        id = 1, codigo = "SKU-001", codigoBarras = "7791234567890", nombre = "Agua 2L",
        descripcion = (string?)null, categoriaId = 2, categoriaNombre = "Bebidas",
        proveedorId = (int?)null, unidadMedidaId = 1, unidadMedidaNombre = "Unidad",
        precioCosto = 25.5, precioVenta = 40.0, stockActual = 12.0, stockMinimo = 3.0,
        activo = true, fechaAlta = "2026-07-01T10:00:00Z",
    };

    [Fact]
    public async Task Buscar_SinFiltros_GETProductosSinQuery_DevuelveLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[] { ProductoJson }));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var productos = await client.BuscarAsync(null, null, null);

        Assert.Equal("/productos", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var p = Assert.Single(productos);
        Assert.Equal("SKU-001", p.Codigo);
        Assert.Equal("Bebidas", p.CategoriaNombre);
        Assert.Equal(25.5m, p.PrecioCosto);
        Assert.Equal(12m, p.StockActual);
    }

    [Fact]
    public async Task Buscar_ConFiltros_ArmaLaQuerySoloConLosPresentes()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarAsync("SKU-001", null, "agua");

        Assert.Equal("/productos?sku=SKU-001&nombre=agua", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task BuscarPorTexto_GETProductosConTexto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarPorTextoAsync("agua con gas");

        Assert.Equal("/productos?texto=agua%20con%20gas", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task BuscarPorTexto_Null_GETProductosSinQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BuscarPorTextoAsync(null);

        Assert.Equal("/productos", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task Alta_POSTProductos_SoloLosCamposDelRequest_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 11 }, HttpStatusCode.Created));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new Producto
        {
            Codigo = "SKU-002", Nombre = "Yerba 1kg", UnidadMedidaId = 1,
            CategoriaId = 2, PrecioCosto = 100m, PrecioVenta = 150m, StockMinimo = 5m,
            StockActual = 99m, // el stock NO viaja en el alta: lo gobiernan los movimientos
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/productos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":\"SKU-002\"", fake.UltimoBody);
        Assert.Contains("\"stockMinimo\":5", fake.UltimoBody);
        Assert.DoesNotContain("stockActual", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
        Assert.Equal(11, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new Producto
        {
            Id = 11, Codigo = "SKU-002", Nombre = "Yerba 1kg suave", UnidadMedidaId = 1,
        });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEProductosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(11);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CambiarPrecio_PUTProductosIdPrecio()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarPrecioAsync(11, 110m, 165m);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/11/precio", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"precioCosto\":110", fake.UltimoBody);
        Assert.Contains("\"precioVenta\":165", fake.UltimoBody);
    }

    [Fact]
    public async Task Modificar_404_LanzaEntidadNoEncontradaConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Producto 99 no encontrado."));
        var client = new ProductoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.ModificarAsync(new Producto { Id = 99, Codigo = "X", Nombre = "X", UnidadMedidaId = 1 }));

        Assert.Equal("Producto 99 no encontrado.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ProductoApiClientTests`
Expected: FAIL — error de compilación (`ProductoApiClient` no existe).

- [ ] **Step 3: Implementar `ProductoApiClient.cs`**

```csharp
// src/StockApp.ApiClient/ProductoApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record ProductoBody(
    string Codigo, string? CodigoBarras, string Nombre, string? Descripcion,
    int? CategoriaId, int? ProveedorId, int UnidadMedidaId,
    decimal PrecioCosto, decimal PrecioVenta, decimal StockMinimo);
internal sealed record CambiarPrecioBody(decimal PrecioCosto, decimal PrecioVenta);

/// <summary>
/// IProductoService contra /productos. Las búsquedas devuelven ProductoDto (el mismo DTO
/// de Application viaja por el wire — sin mapeo); las escrituras arman el request desde
/// la entidad, sin Id ni StockActual en el body (3a, D1/D5).
/// </summary>
public sealed class ProductoApiClient : IProductoService
{
    private readonly HttpClient _http;

    public ProductoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(Producto producto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("productos", ABody(producto)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el producto.");
        return creado.Id;
    }

    public async Task ModificarAsync(Producto producto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"productos/{producto.Id}", ABody(producto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"productos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarPrecioAsync(int id, decimal precioCosto, decimal precioVenta)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"productos/{id}/precio", new CambiarPrecioBody(precioCosto, precioVenta)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<ProductoDto>> BuscarAsync(string? sku, string? codigoBarras, string? nombre)
        => BuscarConQueryAsync(ApiQuery.Construir(("sku", sku), ("codigoBarras", codigoBarras), ("nombre", nombre)));

    public Task<IReadOnlyList<ProductoDto>> BuscarPorTextoAsync(string? texto)
        => BuscarConQueryAsync(ApiQuery.Construir(("texto", texto)));

    private async Task<IReadOnlyList<ProductoDto>> BuscarConQueryAsync(string query)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("productos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ProductoDto>>() ?? new();
    }

    private static ProductoBody ABody(Producto p) => new(
        p.Codigo, p.CodigoBarras, p.Nombre, p.Descripcion,
        p.CategoriaId, p.ProveedorId, p.UnidadMedidaId,
        p.PrecioCosto, p.PrecioVenta, p.StockMinimo);
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ProductoApiClientTests`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/ProductoApiClient.cs tests/StockApp.ApiClient.Tests/ProductoApiClientTests.cs
git commit -m "feat(apiclient): ProductoApiClient"
```

## Task 11: `UsuarioApiClient : IUsuarioService`

**Files:**
- Create: `src/StockApp.ApiClient/UsuarioApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs`

**Interfaces:**
- Consumes: `IUsuarioService` (`Task<int> AltaUsuarioAsync(string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol)` — `Task<int>` desde 3a D2; `Task BajaLogicaAsync(int usuarioId)`; `Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol)`; `Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null)`; `Task<IReadOnlyList<UsuarioDto>> ListarAsync()`); `UsuarioDto` (Application — directo por el wire); `ApiErrores`/`IdCreado` (Task 3).
- Consumes (HTTP): `GET /usuarios` → `[UsuarioDto]`; `POST /usuarios` body `{nombreUsuario,nombreCompleto,contrasenaPlan,rol}` → 201 `{id}` (3a D2); `DELETE /usuarios/{id}`; `PUT /usuarios/{id}/rol` body `{nuevoRol}`; `PUT /usuarios/{id}/contrasena` body `{nuevaContrasena,contrasenaActual}`. Todo Admin-only (403 si no).
- Produces: `UsuarioApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `UsuarioApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class UsuarioApiClientTests
{
    [Fact]
    public async Task Listar_GETUsuarios_DevuelveLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombreUsuario = "admin", nombreCompleto = (string?)null,
                rol = 0, activo = true, fechaAlta = "2026-07-01T10:00:00Z",
            },
        }));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var usuarios = await client.ListarAsync();

        Assert.Equal("/usuarios", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var u = Assert.Single(usuarios);
        Assert.Equal("admin", u.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, u.Rol);
        Assert.True(u.Activo);
    }

    [Fact]
    public async Task Alta_POSTUsuarios_DevuelveElIdDel201()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 4 }, HttpStatusCode.Created));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaUsuarioAsync("oper1", "Operario Uno", "secreto123", RolUsuario.Operador);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/usuarios", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombreUsuario\":\"oper1\"", fake.UltimoBody);
        Assert.Contains("\"contrasenaPlan\":\"secreto123\"", fake.UltimoBody);
        Assert.Contains("\"rol\":1", fake.UltimoBody); // enum numérico
        Assert.Equal(4, id);
    }

    [Fact]
    public async Task Baja_DELETEUsuariosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/usuarios/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task CambiarRol_PUTUsuariosIdRol()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarRolAsync(4, RolUsuario.Admin);

        Assert.Equal("/usuarios/4/rol", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nuevoRol\":0", fake.UltimoBody);
    }

    [Fact]
    public async Task CambiarContrasena_PUTUsuariosIdContrasena()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarContrasenaAsync(4, "nueva123", "vieja123");

        Assert.Equal("/usuarios/4/contrasena", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"nuevaContrasena\":\"nueva123\"", fake.UltimoBody);
        Assert.Contains("\"contrasenaActual\":\"vieja123\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Alta_403_LanzaUnauthorizedAccess()
    {
        // Operador intentando gestionar usuarios: la política HTTP responde 403.
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.AltaUsuarioAsync("x", null, "secreto123", RolUsuario.Operador));
    }

    [Fact]
    public async Task Baja_409_UltimoAdmin_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede dar de baja al último Admin activo."));
        var client = new UsuarioApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.BajaLogicaAsync(1));

        Assert.Equal("No se puede dar de baja al último Admin activo.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter UsuarioApiClientTests`
Expected: FAIL — error de compilación (`UsuarioApiClient` no existe).

- [ ] **Step 3: Implementar `UsuarioApiClient.cs`**

```csharp
// src/StockApp.ApiClient/UsuarioApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record CrearUsuarioBody(
    string NombreUsuario, string? NombreCompleto, string ContrasenaPlan, RolUsuario Rol);
internal sealed record CambiarRolBody(RolUsuario NuevoRol);
internal sealed record CambiarContrasenaBody(string NuevaContrasena, string? ContrasenaActual);

/// <summary>IUsuarioService contra /usuarios (Admin-only; el alta devuelve el id — 3a, D2).</summary>
public sealed class UsuarioApiClient : IUsuarioService
{
    private readonly HttpClient _http;

    public UsuarioApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto, string contrasenaPlan, RolUsuario rol)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync(
            "usuarios", new CrearUsuarioBody(nombreUsuario, nombreCompleto, contrasenaPlan, rol)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el usuario.");
        return creado.Id;
    }

    public async Task BajaLogicaAsync(int usuarioId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"usuarios/{usuarioId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"usuarios/{usuarioId}/rol", new CambiarRolBody(nuevoRol)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task CambiarContrasenaAsync(
        int usuarioId, string nuevaContrasenaPlan, string? contrasenaActualPlan = null)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PutAsJsonAsync(
            $"usuarios/{usuarioId}/contrasena",
            new CambiarContrasenaBody(nuevaContrasenaPlan, contrasenaActualPlan)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<UsuarioDto>> ListarAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("usuarios"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<UsuarioDto>>() ?? new();
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter UsuarioApiClientTests`
Expected: PASS (7 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/UsuarioApiClient.cs tests/StockApp.ApiClient.Tests/UsuarioApiClientTests.cs
git commit -m "feat(apiclient): UsuarioApiClient"
```

## Task 12: `MovimientoStockApiClient : IMovimientoStockService` — con el flujo "forzar" intacto

**Files:**
- Create: `src/StockApp.ApiClient/MovimientoStockApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/MovimientoStockApiClientTests.cs`

**Interfaces:**
- Consumes: `IMovimientoStockService` (`Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)`, `Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)`, `Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)`); DTOs de `StockApp.Application.Movimientos` (van directo por el wire); `ApiErrores`/`ApiQuery` (Task 3); extensiones de stock del 409 (Task 5, vía `ApiErrores.CrearConflicto`).
- Consumes (HTTP): `POST /movimientos` body `{productoId,tipo,motivo,cantidad,precioUnitario,comentario,forzar}` → 201 `MovimientoRegistradoDto` (sin Location); `GET /movimientos/historial?productoId=&tipo=&fechaDesde=&fechaHasta=` → `[MovimientoHistorialDto]`; `POST /productos/{id}/recalcular-stock` → 200 `RecalculoResultadoDto`.
- Produces: `MovimientoStockApiClient(HttpClient http)` — DI en Task 17. GARANTÍA CLAVE: un 409 por stock insuficiente llega al VM como `StockInsuficienteException` tipada (con `StockResultante`), preservando el flujo "¿confirmar la salida igual?" sin tocar `MovimientoRegistroViewModelBase`.

- [ ] **Step 1: Escribir los tests que fallan — `MovimientoStockApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/MovimientoStockApiClientTests.cs
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class MovimientoStockApiClientTests
{
    private static RegistrarMovimientoDto Salida(decimal cantidad = 8m) => new(
        ProductoId: 7, Tipo: TipoMovimiento.Salida, Motivo: MotivoMovimiento.Venta,
        Cantidad: cantidad, PrecioUnitario: 40m, Comentario: null);

    [Fact]
    public async Task Registrar_POSTMovimientos_ConForzarEnElBody_DevuelveElDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            movimientoId = 15, productoId = 7, tipo = 1, motivo = 1, cantidad = 8.0,
            precioUnitario = 40.0, stockAnterior = 12.0, stockNuevo = 4.0,
            fecha = "2026-07-10T14:00:00Z",
        }, HttpStatusCode.Created));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var registrado = await client.RegistrarAsync(Salida(), forzar: false);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/movimientos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"productoId\":7", fake.UltimoBody);
        Assert.Contains("\"tipo\":1", fake.UltimoBody);   // enum numérico
        Assert.Contains("\"forzar\":false", fake.UltimoBody);
        Assert.Equal(15, registrado.MovimientoId);
        Assert.Equal(4m, registrado.StockNuevo);
    }

    [Fact]
    public async Task Registrar_ConForzarTrue_LoEnviaEnElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            movimientoId = 16, productoId = 7, tipo = 1, motivo = 1, cantidad = 8.0,
            precioUnitario = 40.0, stockAnterior = 4.0, stockNuevo = -4.0,
            fecha = "2026-07-10T14:05:00Z",
        }, HttpStatusCode.Created));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        await client.RegistrarAsync(Salida(), forzar: true);

        Assert.Contains("\"forzar\":true", fake.UltimoBody);
    }

    [Fact]
    public async Task Registrar_409ConExtensiones_LanzaStockInsuficienteTipada()
    {
        // Mina 2: el VM hace catch (StockInsuficienteException ex) y usa ex.StockResultante
        // para preguntar "¿forzar?". Las extensiones del problem+json (Task 5) lo permiten.
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Regla de negocio violada.",
                detail = "Stock insuficiente para el producto 7.",
                status = 409,
                productoId = 7,
                stockActual = 5.0,
                cantidadSolicitada = 8.0,
            }),
        });
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<StockInsuficienteException>(
            () => client.RegistrarAsync(Salida(), forzar: false));

        Assert.Equal(-3m, ex.StockResultante);
    }

    [Fact]
    public async Task Registrar_409SinExtensiones_LanzaReglaDeNegocioPlano()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "El producto está inactivo."));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.RegistrarAsync(Salida(), forzar: false));

        Assert.Equal("El producto está inactivo.", ex.Message);
    }

    [Fact]
    public async Task Historial_GETConTodosLosFiltros()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));
        var filtro = new HistorialMovimientoFiltro(
            ProductoId: 7,
            Tipo: TipoMovimiento.Salida,
            FechaDesde: new DateTime(2026, 7, 1),
            FechaHasta: new DateTime(2026, 7, 10));

        await client.ObtenerHistorialAsync(filtro);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/movimientos/historial?", pathAndQuery);
        Assert.Contains("productoId=7", pathAndQuery);
        Assert.Contains("tipo=1", pathAndQuery); // enum numérico en la query
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("fechaHasta=2026-07-10T00%3A00%3A00.0000000", pathAndQuery);
    }

    [Fact]
    public async Task Historial_SinFiltros_GETSinQuery_MapeaLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                movimientoId = 1, productoId = 7, productoNombre = "Agua 2L", tipo = 0, motivo = 0,
                cantidad = 10.0, precioUnitario = 25.5, stockAnterior = 0.0, stockNuevo = 10.0,
                comentario = (string?)null, fecha = "2026-07-01T10:00:00Z",
                usuarioId = 1, usuarioNombre = "admin",
            },
        }));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var historial = await client.ObtenerHistorialAsync(new HistorialMovimientoFiltro());

        Assert.Equal("/movimientos/historial", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(historial);
        Assert.Equal("Agua 2L", item.ProductoNombre);
        Assert.Equal(TipoMovimiento.Entrada, item.Tipo);
        Assert.Equal(10m, item.StockNuevo);
        Assert.Equal("admin", item.UsuarioNombre);
    }

    [Fact]
    public async Task RecalcularStock_POSTProductosIdRecalcularStock_DevuelveElResultado()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            productoId = 7, stockAnterior = 4.0, stockNuevo = 6.0, totalMovimientos = 12,
        }));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RecalcularStockAsync(7);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/productos/7/recalcular-stock", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(6m, resultado.StockNuevo);
        Assert.Equal(12, resultado.TotalMovimientos);
    }

    [Fact]
    public async Task Registrar_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Producto 99 no encontrado."));
        var client = new MovimientoStockApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => client.RegistrarAsync(Salida() with { ProductoId = 99 }, forzar: false));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter MovimientoStockApiClientTests`
Expected: FAIL — error de compilación (`MovimientoStockApiClient` no existe).

- [ ] **Step 3: Implementar `MovimientoStockApiClient.cs`**

```csharp
// src/StockApp.ApiClient/MovimientoStockApiClient.cs
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Movimientos;

namespace StockApp.ApiClient;

internal sealed record RegistrarMovimientoBody(
    int ProductoId,
    StockApp.Domain.Enums.TipoMovimiento Tipo,
    StockApp.Domain.Enums.MotivoMovimiento Motivo,
    decimal Cantidad,
    decimal? PrecioUnitario,
    string? Comentario,
    bool Forzar);

/// <summary>
/// IMovimientoStockService contra /movimientos y /productos/{id}/recalcular-stock.
/// El parámetro forzar (RM-09) viaja dentro del body del POST (contrato de 2b). Un 409
/// por stock insuficiente vuelve como StockInsuficienteException tipada (via ApiErrores +
/// extensiones del problem+json, Task 5) para no romper el flujo "¿forzar salida?" del VM.
/// </summary>
public sealed class MovimientoStockApiClient : IMovimientoStockService
{
    private readonly HttpClient _http;

    public MovimientoStockApiClient(HttpClient http) => _http = http;

    public async Task<MovimientoRegistradoDto> RegistrarAsync(RegistrarMovimientoDto dto, bool forzar = false)
    {
        var body = new RegistrarMovimientoBody(
            dto.ProductoId, dto.Tipo, dto.Motivo, dto.Cantidad, dto.PrecioUnitario, dto.Comentario, forzar);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("movimientos", body));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<MovimientoRegistradoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el movimiento.");
    }

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialAsync(HistorialMovimientoFiltro filtro)
    {
        var query = ApiQuery.Construir(
            ("productoId", filtro.ProductoId?.ToString(CultureInfo.InvariantCulture)),
            ("tipo", filtro.Tipo is null ? null : ((int)filtro.Tipo.Value).ToString(CultureInfo.InvariantCulture)),
            ("fechaDesde", ApiQuery.Fecha(filtro.FechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(filtro.FechaHasta)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("movimientos/historial" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>() ?? new();
    }

    public async Task<RecalculoResultadoDto> RecalcularStockAsync(int productoId)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"productos/{productoId}/recalcular-stock", content: null));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<RecalculoResultadoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al recalcular el stock.");
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter MovimientoStockApiClientTests`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/MovimientoStockApiClient.cs tests/StockApp.ApiClient.Tests/MovimientoStockApiClientTests.cs
git commit -m "feat(apiclient): MovimientoStockApiClient con flujo forzar intacto"
```

## Task 13: `ReporteStockApiClient : IReporteStockService`

**Files:**
- Create: `src/StockApp.ApiClient/ReporteStockApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/ReporteStockApiClientTests.cs`

**Interfaces:**
- Consumes: `IReporteStockService` (`Task<ValorizacionReporteDto> ObtenerValorizacionAsync()`, `Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()`, `Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)`, `Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(int productoId, DateTime? fechaDesde, DateTime? fechaHasta)`); DTOs de `StockApp.Application.Reportes` y `MovimientoHistorialDto` (directo por el wire); `ApiErrores`/`ApiQuery` (Task 3).
- Consumes (HTTP): `GET /reportes/valorizacion`; `GET /reportes/stock-por-categoria`; `GET /reportes/mas-movidos?fechaDesde=&fechaHasta=&topN=`; `GET /reportes/historial-producto/{productoId}?fechaDesde=&fechaHasta=`. Todo Admin-only (`reportes.ver` → 403 para Operador).
- Produces: `ReporteStockApiClient(HttpClient http)` — DI en Task 17.

- [ ] **Step 1: Escribir los tests que fallan — `ReporteStockApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/ReporteStockApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;

namespace StockApp.ApiClient.Tests;

public class ReporteStockApiClientTests
{
    [Fact]
    public async Task Valorizacion_GETReportesValorizacion_MapeaItemsYTotales()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            items = new[]
            {
                new
                {
                    productoId = 1, codigo = "SKU-001", nombre = "Agua 2L", categoria = "Bebidas",
                    stockActual = 12.0, precioCosto = 25.5, precioVenta = 40.0,
                    valorCosto = 306.0, valorVenta = 480.0,
                },
            },
            totales = new { totalValorCosto = 306.0, totalValorVenta = 480.0 },
        }));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        var reporte = await client.ObtenerValorizacionAsync();

        Assert.Equal("/reportes/valorizacion", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(reporte.Items);
        Assert.Equal("SKU-001", item.Codigo);
        Assert.Equal(306m, item.ValorCosto);
        Assert.Equal(480m, reporte.Totales.TotalValorVenta);
    }

    [Fact]
    public async Task StockPorCategoria_GETReportesStockPorCategoria()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                categoria = "Bebidas", cantidadProductos = 3, stockTotal = 50.0,
                valorCosto = 1000.0, valorVenta = 1500.0,
            },
        }));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        var resumen = await client.ObtenerStockPorCategoriaAsync();

        Assert.Equal("/reportes/stock-por-categoria", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var fila = Assert.Single(resumen);
        Assert.Equal("Bebidas", fila.Categoria);
        Assert.Equal(3, fila.CantidadProductos);
    }

    [Fact]
    public async Task MasMovidos_GETConFechasYTopN()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerMasMovidosAsync(
            new DateTime(2026, 7, 1), new DateTime(2026, 7, 10), topN: 5);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/mas-movidos?", pathAndQuery);
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("topN=5", pathAndQuery);
    }

    [Fact]
    public async Task MasMovidos_SinFechas_SoloEnviaTopNPorDefecto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerMasMovidosAsync(null, null);

        Assert.Equal("/reportes/mas-movidos?topN=20", fake.UltimaRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task HistorialPorProducto_GETConElIdEnLaRuta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerHistorialPorProductoAsync(7, new DateTime(2026, 7, 1), null);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/historial-producto/7?", pathAndQuery);
        Assert.Contains("fechaDesde=", pathAndQuery);
        Assert.DoesNotContain("fechaHasta=", pathAndQuery);
    }

    [Fact]
    public async Task Valorizacion_403Operador_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ReporteStockApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerValorizacionAsync());
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ReporteStockApiClientTests`
Expected: FAIL — error de compilación (`ReporteStockApiClient` no existe).

- [ ] **Step 3: Implementar `ReporteStockApiClient.cs`**

```csharp
// src/StockApp.ApiClient/ReporteStockApiClient.cs
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;

namespace StockApp.ApiClient;

/// <summary>IReporteStockService contra /reportes/* (Admin-only: reportes.ver).</summary>
public sealed class ReporteStockApiClient : IReporteStockService
{
    private readonly HttpClient _http;

    public ReporteStockApiClient(HttpClient http) => _http = http;

    public async Task<ValorizacionReporteDto> ObtenerValorizacionAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/valorizacion"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ValorizacionReporteDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el reporte de valorización.");
    }

    public async Task<IReadOnlyList<StockCategoriaDto>> ObtenerStockPorCategoriaAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/stock-por-categoria"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<StockCategoriaDto>>() ?? new();
    }

    public async Task<IReadOnlyList<MasMovidoDto>> ObtenerMasMovidosAsync(
        DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)),
            ("topN", topN.ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/mas-movidos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MasMovidoDto>>() ?? new();
    }

    public async Task<IReadOnlyList<MovimientoHistorialDto>> ObtenerHistorialPorProductoAsync(
        int productoId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)));

        var response = await ApiErrores.EnviarAsync(() =>
            _http.GetAsync($"reportes/historial-producto/{productoId}" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<MovimientoHistorialDto>>() ?? new();
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter ReporteStockApiClientTests`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.ApiClient/ReporteStockApiClient.cs tests/StockApp.ApiClient.Tests/ReporteStockApiClientTests.cs
git commit -m "feat(apiclient): ReporteStockApiClient"
```

## Task 14: `AuditoriaQueryApiClient : IAuditoriaQueryService`

**Files:**
- Create: `src/StockApp.ApiClient/AuditoriaQueryApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/AuditoriaQueryApiClientTests.cs`

**Interfaces:**
- Consumes: `IAuditoriaQueryService` (`Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)`); `AuditoriaItemDto` (Application — directo por el wire; `Accion` es el enum `AccionAuditada`, numérico en JSON); `ApiErrores`/`ApiQuery` (Task 3).
- Consumes (HTTP): `GET /auditoria?usuarioId=&fechaDesde=&fechaHasta=` → `[AuditoriaItemDto]` (Admin-only).
- Produces: `AuditoriaQueryApiClient(HttpClient http)` — DI en Task 17. Con este task quedan implementadas las 10 interfaces.

- [ ] **Step 1: Escribir los tests que fallan — `AuditoriaQueryApiClientTests.cs`**

```csharp
// tests/StockApp.ApiClient.Tests/AuditoriaQueryApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient.Tests;

public class AuditoriaQueryApiClientTests
{
    [Fact]
    public async Task ObtenerLog_SinFiltros_GETAuditoria_MapeaLosDtos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                fecha = "2026-07-10T12:00:00Z", nombreUsuario = "admin", accion = 1,
                entidad = "Producto", entidadId = 7, detalle = "Alta de producto 'Agua 2L'",
            },
        }));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        var log = await client.ObtenerLogAsync(null, null, null);

        Assert.Equal("/auditoria", fake.UltimaRequest!.RequestUri!.PathAndQuery);
        var item = Assert.Single(log);
        Assert.Equal("admin", item.NombreUsuario);
        Assert.Equal(AccionAuditada.AltaProducto, item.Accion);
        Assert.Equal(7, item.EntidadId);
    }

    [Fact]
    public async Task ObtenerLog_ConFiltros_ArmaLaQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerLogAsync(3, new DateTime(2026, 7, 1), new DateTime(2026, 7, 10));

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/auditoria?", pathAndQuery);
        Assert.Contains("usuarioId=3", pathAndQuery);
        Assert.Contains("fechaDesde=2026-07-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("fechaHasta=2026-07-10T00%3A00%3A00.0000000", pathAndQuery);
    }

    [Fact]
    public async Task ObtenerLog_403_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new AuditoriaQueryApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerLogAsync(null, null, null));
    }
}
```

- [ ] **Step 2: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AuditoriaQueryApiClientTests`
Expected: FAIL — error de compilación (`AuditoriaQueryApiClient` no existe).

- [ ] **Step 3: Implementar `AuditoriaQueryApiClient.cs`**

```csharp
// src/StockApp.ApiClient/AuditoriaQueryApiClient.cs
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Auditoria;

namespace StockApp.ApiClient;

/// <summary>IAuditoriaQueryService contra GET /auditoria (Admin-only: reportes.ver).</summary>
public sealed class AuditoriaQueryApiClient : IAuditoriaQueryService
{
    private readonly HttpClient _http;

    public AuditoriaQueryApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<AuditoriaItemDto>> ObtenerLogAsync(
        int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)
    {
        var query = ApiQuery.Construir(
            ("usuarioId", usuarioId?.ToString(CultureInfo.InvariantCulture)),
            ("fechaDesde", ApiQuery.Fecha(fechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(fechaHasta)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("auditoria" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<AuditoriaItemDto>>() ?? new();
    }
}
```

- [ ] **Step 4: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AuditoriaQueryApiClientTests`
Expected: PASS (3 tests).

- [ ] **Step 5: Correr TODA la suite del ApiClient (cierre del Bloque B)**

Run: `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj`
Expected: PASS, 0 failures — las 10 interfaces implementadas y cubiertas.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.ApiClient/AuditoriaQueryApiClient.cs tests/StockApp.ApiClient.Tests/AuditoriaQueryApiClientTests.cs
git commit -m "feat(apiclient): AuditoriaQueryApiClient"
```

---

## Bloque C — Presentation cambia de backend (Tasks 15-19)

Orden crítico: primero se muda el updater (Task 15, desbloquea el criterio duro), después los retoques de VMs con su referencia a ApiClient (Task 16), después la composición nueva (Task 17), después los DI tests espejo (Task 18) y RECIÉN entonces se corta la referencia a Infrastructure (Task 19).

## Task 15: Mudanza del updater Velopack — `Infrastructure/Actualizaciones` → `Presentation/Actualizaciones`

**Files:**
- Move: `src/StockApp.Infrastructure/Actualizaciones/IVelopackGateway.cs` → `src/StockApp.Presentation/Actualizaciones/IVelopackGateway.cs`
- Move: `src/StockApp.Infrastructure/Actualizaciones/VelopackGatewayReal.cs` → `src/StockApp.Presentation/Actualizaciones/VelopackGatewayReal.cs`
- Move: `src/StockApp.Infrastructure/Actualizaciones/VelopackUpdateService.cs` → `src/StockApp.Presentation/Actualizaciones/VelopackUpdateService.cs`
- Move: `src/StockApp.Infrastructure/Actualizaciones/UpdaterOptions.cs` → `src/StockApp.Presentation/Actualizaciones/UpdaterOptions.cs`
- Move: `src/StockApp.Infrastructure/Actualizaciones/FallbackUpdateSource.cs` → `src/StockApp.Presentation/Actualizaciones/FallbackUpdateSource.cs`
- Move: `tests/StockApp.Infrastructure.Tests/Actualizaciones/VelopackUpdateServiceTests.cs` → `tests/StockApp.Presentation.Tests/Actualizaciones/VelopackUpdateServiceTests.cs`
- Move: `tests/StockApp.Infrastructure.Tests/Actualizaciones/FallbackUpdateSourceTests.cs` → `tests/StockApp.Presentation.Tests/Actualizaciones/FallbackUpdateSourceTests.cs`
- Modify: `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj` (quitar `PackageReference` Velopack)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (quitar `using StockApp.Infrastructure.Actualizaciones;`)

**Interfaces:**
- Produces: `IVelopackGateway`, `VelopackGatewayReal`, `VelopackUpdateService`, `UpdaterOptions` (con `GitHubRepoUrlDefault` y `OrdenFuentes`) y `FallbackUpdateSource` pasan al namespace `StockApp.Presentation.Actualizaciones` — el MISMO namespace que ya importa `App.axaml.cs` para `CoordinadorActualizacion`, por eso alcanza con borrar el using viejo. Task 17 los re-registra igual que hoy.
- Justificación (Mina 1): son código de ciclo de vida de la app desktop (Velopack), Presentation ya referencia el paquete `Velopack 1.2.0` y es su único consumidor. Sin esta mudanza, el criterio duro (Task 19) rompería el actualizador. Los archivos NO cambian salvo la línea `namespace` (y los `using` en los tests movidos).

- [ ] **Step 1: Mover los 5 archivos de producción con `git mv`**

Run:
```bash
git mv src/StockApp.Infrastructure/Actualizaciones/IVelopackGateway.cs src/StockApp.Presentation/Actualizaciones/
git mv src/StockApp.Infrastructure/Actualizaciones/VelopackGatewayReal.cs src/StockApp.Presentation/Actualizaciones/
git mv src/StockApp.Infrastructure/Actualizaciones/VelopackUpdateService.cs src/StockApp.Presentation/Actualizaciones/
git mv src/StockApp.Infrastructure/Actualizaciones/UpdaterOptions.cs src/StockApp.Presentation/Actualizaciones/
git mv src/StockApp.Infrastructure/Actualizaciones/FallbackUpdateSource.cs src/StockApp.Presentation/Actualizaciones/
```
Expected: sin output; `git status` muestra 5 renames.

- [ ] **Step 2: Actualizar el namespace en los 5 archivos movidos**

En CADA uno de los 5 archivos de `src/StockApp.Presentation/Actualizaciones/` recién movidos, reemplazar la línea:

```csharp
namespace StockApp.Infrastructure.Actualizaciones;
```

por:

```csharp
namespace StockApp.Presentation.Actualizaciones;
```

(El resto del contenido NO cambia: sus `using` son solo `Velopack.*` y `StockApp.Application.Actualizaciones`.)

- [ ] **Step 3: Quitar el using muerto en `App.axaml.cs`**

Eliminar la línea:

```csharp
using StockApp.Infrastructure.Actualizaciones;
```

(Los tipos ahora resuelven por el `using StockApp.Presentation.Actualizaciones;` que ya existe en el archivo.)

- [ ] **Step 4: Quitar el paquete Velopack de Infrastructure**

En `src/StockApp.Infrastructure/StockApp.Infrastructure.csproj`, eliminar la línea:

```xml
    <PackageReference Include="Velopack" Version="1.2.0" />
```

(Era su único consumidor dentro de Infrastructure; Presentation ya tiene su propia referencia `Velopack 1.2.0`.)

- [ ] **Step 5: Mover los 2 archivos de tests y actualizar namespace + usings**

Run:
```bash
mkdir -p tests/StockApp.Presentation.Tests/Actualizaciones
git mv tests/StockApp.Infrastructure.Tests/Actualizaciones/VelopackUpdateServiceTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/
git mv tests/StockApp.Infrastructure.Tests/Actualizaciones/FallbackUpdateSourceTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/
```

En AMBOS archivos movidos, reemplazar:

```csharp
using StockApp.Infrastructure.Actualizaciones;
```
por:
```csharp
using StockApp.Presentation.Actualizaciones;
```

y:

```csharp
namespace StockApp.Infrastructure.Tests.Actualizaciones;
```
por:
```csharp
namespace StockApp.Presentation.Tests.Actualizaciones;
```

(Presentation.Tests ya tiene Moq y recibe Velopack transitivo vía la ProjectReference a Presentation — no se agregan paquetes.)

- [ ] **Step 6: Compilar y correr las DOS suites afectadas**

Run:
```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
```
Expected: build OK; ambas suites PASS (los tests del updater ahora corren dentro de Presentation.Tests).

- [ ] **Step 7: Commit**

```bash
git add -A src/StockApp.Infrastructure src/StockApp.Presentation tests/StockApp.Infrastructure.Tests tests/StockApp.Presentation.Tests
git commit -m "refactor(desktop): updater Velopack se muda de Infrastructure a Presentation"
```

## Task 16: Shell/Login/PrimerArranque — servidor caído y aviso de sesión vencida

**Files:**
- Modify: `src/StockApp.Presentation/StockApp.Presentation.csproj` (agregar ProjectReference a ApiClient — la de Infrastructure NO se toca todavía)
- Modify: `src/StockApp.Presentation/ViewModels/ShellViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/LoginViewModel.cs`
- Modify: `src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs`

**Interfaces:**
- Consumes: `ServidorNoDisponibleException` (Task 1, con `MensajePorDefecto`).
- Produces: `ShellViewModel.MostrarLoginConAviso(string aviso)` — crea el `LoginViewModel` y le fija `MensajeError = aviso`; lo consume el wiring de sesión vencida en `App.axaml.cs` (Task 17). `ShellViewModel.InicializarAsync` tolera el servidor caído en el arranque (cae al login). `LoginViewModel.EntrarAsync` y `PrimerArranqueViewModel.CrearAdminAsync` muestran `ex.Message` de `ServidorNoDisponibleException` en su `MensajeError` (tabla "Manejo de errores" del spec: el login "muestra el error de conexión, permite reintentar").
- Los tests usan los helpers EXISTENTES de cada archivo (`Crear(...)`, `CrearShellFake()`, `Crear(excepcionCreacion:)`) — solo se agregan métodos de test y usings.

- [ ] **Step 1: Agregar la ProjectReference a ApiClient en `StockApp.Presentation.csproj`**

En el `ItemGroup` final de ProjectReferences, dejar:

```xml
  <ItemGroup>
    <ProjectReference Include="..\StockApp.ApiClient\StockApp.ApiClient.csproj" />
    <ProjectReference Include="..\StockApp.Application\StockApp.Application.csproj" />
    <ProjectReference Include="..\StockApp.Infrastructure\StockApp.Infrastructure.csproj" />
  </ItemGroup>
```

(Infrastructure sale recién en Task 19, cuando ya nada la use.)

- [ ] **Step 2: Escribir los tests que fallan en `ShellViewModelTests.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings del archivo, y estos dos tests al final de la clase (antes del `}` de cierre):

```csharp

    [Fact]
    public async Task Inicializar_ServidorCaido_MuestraLoginIgual()
    {
        // Spec 3b, manejo de errores: si la API no responde en el arranque, la app no
        // muere — muestra el login; el intento de login informará el error de conexión.
        var (shell, primerArranqueMock) = Crear(requiereCrearAdmin: false);
        primerArranqueMock
            .Setup(p => p.RequiereCrearAdminAsync())
            .ThrowsAsync(new ServidorNoDisponibleException());

        await shell.InicializarAsync();

        Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
    }

    [Fact]
    public void MostrarLoginConAviso_EstableceLoginConElMensaje()
    {
        var (shell, _) = Crear(requiereCrearAdmin: false);

        shell.MostrarLoginConAviso("Sesión vencida, ingresá de nuevo.");

        var login = Assert.IsType<LoginViewModel>(shell.CurrentViewModel);
        Assert.Equal("Sesión vencida, ingresá de nuevo.", login.MensajeError);
    }
```

- [ ] **Step 3: Escribir el test que falla en `LoginViewModelTests.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings, y este test al final de la clase:

```csharp

    [Fact]
    public async Task Entrar_ServidorCaido_MuestraElMensajeDeConexion()
    {
        // Spec 3b: "Login con servidor caído → el login muestra el error de conexión,
        // permite reintentar" (el comando queda habilitado de nuevo al terminar).
        var authMock = new Mock<IAuthService>();
        authMock
            .Setup(a => a.LoginAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new ServidorNoDisponibleException());
        var vm = new LoginViewModel(authMock.Object, CrearShellFake(), Mock.Of<IInfoApp>(x => x.Version == "0.0.0"));
        vm.NombreUsuario = "admin";
        vm.Contrasena    = "secreto123";

        await vm.EntrarCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, vm.MensajeError);
        Assert.False(vm.OperacionEnCurso);
        Assert.True(vm.EntrarCommand.CanExecute(null)); // puede reintentar
    }
```

- [ ] **Step 4: Escribir el test que falla en `PrimerArranqueViewModelTests.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings, y este test al final de la clase (usa el helper existente `Crear(excepcionCreacion:)`):

```csharp

    [Fact]
    public async Task CrearAdmin_ServidorCaido_MuestraElMensajeDeConexion()
    {
        var ctx = Crear(excepcionCreacion: new ServidorNoDisponibleException());
        ctx.Vm.NombreUsuario       = "admin";
        ctx.Vm.Contrasena          = "secreto123";
        ctx.Vm.ConfirmarContrasena = "secreto123";

        await ctx.Vm.CrearAdminCommand.ExecuteAsync(null);

        Assert.Equal(ServidorNoDisponibleException.MensajePorDefecto, ctx.Vm.MensajeError);
        Assert.False(ctx.Vm.MostrarRecomendacion2doAdmin);
    }
```

- [ ] **Step 5: Correr los tests y verificar que fallan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "Inicializar_ServidorCaido_MuestraLoginIgual|MostrarLoginConAviso_EstableceLoginConElMensaje|Entrar_ServidorCaido_MuestraElMensajeDeConexion|CrearAdmin_ServidorCaido_MuestraElMensajeDeConexion"`
Expected: FAIL — `MostrarLoginConAviso` no compila; los de servidor caído fallan (la excepción hoy escapa del comando o del `InicializarAsync`).

- [ ] **Step 6: Modificar `ShellViewModel.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings. Reemplazar el inicio de `InicializarAsync`:

```csharp
    public async Task InicializarAsync()
    {
        if (await _primerArranqueService.RequiereCrearAdminAsync())
            MostrarPrimerArranque();
        else
            MostrarLogin();
```

por:

```csharp
    public async Task InicializarAsync()
    {
        bool requiereCrearAdmin;
        try
        {
            requiereCrearAdmin = await _primerArranqueService.RequiereCrearAdminAsync();
        }
        catch (ServidorNoDisponibleException)
        {
            // Servidor caído en el arranque (spec 3b): se muestra el login igual; el
            // intento de login informa el error de conexión y permite reintentar.
            requiereCrearAdmin = false;
        }

        if (requiereCrearAdmin)
            MostrarPrimerArranque();
        else
            MostrarLogin();
```

Y agregar el método nuevo inmediatamente después de `MostrarLogin()`:

```csharp
    /// <summary>
    /// Navega al login mostrando un aviso (ej. "Sesión vencida, ingresá de nuevo.").
    /// Lo cablea App.axaml.cs al evento ApiSession.SesionVencida (spec 3b, OQ-4).
    /// </summary>
    public void MostrarLoginConAviso(string aviso)
    {
        var login = new LoginViewModel(_authService, this, _infoApp);
        login.MensajeError = aviso;
        CurrentViewModel = login;
    }
```

- [ ] **Step 7: Modificar `LoginViewModel.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings. En `EntrarAsync`, agregar el catch entre el `try { ... }` y el `finally`:

```csharp
        catch (ServidorNoDisponibleException ex)
        {
            // Spec 3b: el login muestra el error de conexión y permite reintentar.
            MensajeError = ex.Message;
        }
```

- [ ] **Step 8: Modificar `PrimerArranqueViewModel.cs`**

Agregar `using StockApp.ApiClient;` al bloque de usings. En `CrearAdminAsync`, agregar un catch más (antes del `catch (ReglaDeNegocioException ex)`):

```csharp
        catch (ServidorNoDisponibleException ex)
        {
            MensajeError = ex.Message;
        }
```

(`CrearSegundoAdminAsync` no cambia: su `catch (Exception ex)` ya muestra `ex.Message`, que para esta excepción es el mensaje accionable.)

- [ ] **Step 9: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASS completo (los 4 tests nuevos + toda la suite existente intacta).

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Presentation tests/StockApp.Presentation.Tests
git commit -m "feat(desktop): manejo de servidor caido y aviso de sesion vencida en Shell/Login/PrimerArranque"
```

## Task 17: `App.axaml.cs` — composición API-only + `appsettings.json` con `Api:BaseUrl`

**Files:**
- Modify: `src/StockApp.Presentation/App.axaml.cs` (reemplazo COMPLETO del archivo — abajo)
- Modify: `src/StockApp.Presentation/appsettings.json`

**Interfaces:**
- Consumes: `ApiSession` (Task 2), `AuthTokenHandler` (Task 4), los 10 `XxxApiClient` (Tasks 6-14), `ShellViewModel.MostrarLoginConAviso` (Task 16), tipos del updater en `StockApp.Presentation.Actualizaciones` (Task 15).
- Produces: composición DI API-only — SALEN `AppDbContext`, `DatabaseInitializer`, `IUserDataPathProvider`, `IPasswordHasher`, `IAuditLogger`, los 8 repositorios, los 10 servicios locales de Application, `IAuthorizationService` (OQ-1) e `InMemorySession`; ENTRAN `ApiSession` (singleton, también como `ICurrentSession`), `HttpClient` singleton (BaseAddress de `Api:BaseUrl` con default `http://localhost:5000`, timeout 10 s, `AuthTokenHandler` encadenado) y los 10 ApiClients transient. La consume el DI test espejo (Task 18). También: arranque sin `DatabaseInitializer` (la API migra la BD — 3a D9), wiring de `SesionVencida` → `MostrarLoginConAviso`, y la red global de excepciones muestra el mensaje accionable de `ServidorNoDisponibleException`.

- [ ] **Step 1: Reemplazar `appsettings.json` completo**

```json
{
  "Api": {
    "BaseUrl": "http://localhost:5043"
  },
  "Updater": {
    "GitHubRepoUrl": "https://github.com/capua25/stockapp",
    "GitHubPrerelease": false
  }
}
```

(Sale `ConnectionStrings` — el desktop ya no conoce Postgres. `5043` es el puerto del launch profile `http` de la API, así la verificación orgánica anda sin flags; si la clave falta, el código cae al default `http://localhost:5000` del spec. En un puesto de la LAN se edita a `http://<ip-del-servidor>:<puerto>`.)

- [ ] **Step 2: Reemplazar `App.axaml.cs` COMPLETO por:**

```csharp
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
```

- [ ] **Step 3: Compilar la solución y correr la suite de Presentation**

Run:
```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
```
Expected: build OK (los DI tests viejos siguen compilando: Presentation.Tests todavía referencia Infrastructure directo — se reemplazan en Task 18); suite PASS.

- [ ] **Step 4: Verificación de humo — la app arranca contra la API real**

Con el contenedor `stockapp-pg` corriendo:

Run (terminal 1): `dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http`
Run (terminal 2): `dotnet run --project src/StockApp.Presentation/StockApp.Presentation.csproj`

Expected: la app abre en login (o primer arranque si la BD está virgen) SIN tocar Postgres directo. Cerrar ambas al confirmar. Si la API está apagada, la app debe abrir igual en login y el intento de login muestra el mensaje de conexión.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/App.axaml.cs src/StockApp.Presentation/appsettings.json
git commit -m "feat(desktop): composicion API-only en App.axaml.cs y appsettings con Api:BaseUrl"
```

## Task 18: DI tests de Presentation — espejo de la composición API-only

**Files:**
- Create: `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs`
- Delete: `tests/StockApp.Presentation.Tests/DI/ComposicionDICatalogoTests.cs`
- Delete: `tests/StockApp.Presentation.Tests/DI/ComposicionDIMovimientosTests.cs`
- Delete: `tests/StockApp.Presentation.Tests/DI/ComposicionDIReportesTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`

**Interfaces:**
- Consumes: todo lo producido en Tasks 2, 4, 6-14 y el patrón de registro de Task 17 (el contenedor del test ES un espejo de `ConfigurarServicios`, misma convención que los 3 tests que reemplaza).
- Produces: red de seguridad del cableado DI nuevo — cubre la resolución de las 10 interfaces (con su implementación ApiClient exacta), la identidad singleton `ICurrentSession`≡`ApiSession`, el `HttpClient` con `BaseAddress` terminada en `/`, y los mismos VMs que cubrían los 3 tests viejos. OQ-5 resuelto: tras este task, ningún test de Presentation menciona `StockApp.Infrastructure`.

- [ ] **Step 1: Crear `ComposicionDIApiTests.cs`**

```csharp
// tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs
using Microsoft.Extensions.DependencyInjection;
using StockApp.ApiClient;
using StockApp.Application.Auditoria;
using StockApp.Application.Auth;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Interfaces;
using StockApp.Application.Movimientos;
using StockApp.Application.Reportes;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.DI;

/// <summary>
/// Red de seguridad del cableado DI API-only (Fase 3b). Espejo de ConfigurarServicios en
/// App.axaml.cs — reemplaza a los 3 ComposicionDI* de las Fases 4-6, que armaban la cadena
/// vieja con AppDbContext/repos de Infrastructure. No hace ninguna llamada HTTP real:
/// solo verifica que el contenedor resuelve toda la cadena sin lanzar.
/// </summary>
public class ComposicionDIApiTests
{
    private static IServiceProvider CrearContenedor()
    {
        var services = new ServiceCollection();

        // ── Sesión API + HttpClient (espejo de App.axaml.cs, Fase 3b) ─────────
        services.AddSingleton<ApiSession>();
        services.AddSingleton<ICurrentSession>(sp => sp.GetRequiredService<ApiSession>());
        services.AddSingleton(sp =>
        {
            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost:5000/"),
                Timeout = TimeSpan.FromSeconds(10),
            };
        });

        // ── ApiClients: las mismas 10 interfaces de Application ───────────────
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

        // ── Servicios de Presentation (igual que App.axaml.cs) ────────────────
        services.AddSingleton<INavigationService>(sp =>
            new NavigationService(t => sp.GetRequiredService(t)));
        services.AddSingleton<IInfoApp, InfoApp>();
        services.AddSingleton<IConfirmacionService, ConfirmacionService>();
        services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();
        services.AddTransient<ICsvExporter, CsvExporter>();

        // ── ViewModels (los mismos que cubrían los 3 tests reemplazados) ──────
        services.AddTransient<ShellMainViewModel>();
        services.AddTransient<ProductoListViewModel>();
        services.AddTransient<ProductoFormViewModel>();
        services.AddTransient<CategoriaListViewModel>();
        services.AddTransient<CategoriaFormViewModel>();
        services.AddTransient<ProveedorListViewModel>();
        services.AddTransient<ProveedorFormViewModel>();
        services.AddTransient<UnidadMedidaListViewModel>();
        services.AddTransient<UnidadMedidaFormViewModel>();
        services.AddTransient<EntradaRegistroViewModel>();
        services.AddTransient<SalidaRegistroViewModel>();
        services.AddTransient<MovimientoHistorialViewModel>();
        services.AddTransient<ValorizacionViewModel>();

        return services.BuildServiceProvider();
    }

    // ─── ApiClients: interfaz → implementación exacta ─────────────────────────

    [Theory]
    [InlineData(typeof(IAuthService), typeof(AuthApiClient))]
    [InlineData(typeof(IPrimerArranqueService), typeof(PrimerArranqueApiClient))]
    [InlineData(typeof(IUsuarioService), typeof(UsuarioApiClient))]
    [InlineData(typeof(IProductoService), typeof(ProductoApiClient))]
    [InlineData(typeof(ICategoriaService), typeof(CategoriaApiClient))]
    [InlineData(typeof(IProveedorService), typeof(ProveedorApiClient))]
    [InlineData(typeof(IUnidadMedidaService), typeof(UnidadMedidaApiClient))]
    [InlineData(typeof(IMovimientoStockService), typeof(MovimientoStockApiClient))]
    [InlineData(typeof(IReporteStockService), typeof(ReporteStockApiClient))]
    [InlineData(typeof(IAuditoriaQueryService), typeof(AuditoriaQueryApiClient))]
    public void Contenedor_Resuelve_CadaInterfazConSuApiClient(Type interfaz, Type implementacion)
    {
        var sp = CrearContenedor();

        var servicio = sp.GetRequiredService(interfaz);

        Assert.IsType(implementacion, servicio);
    }

    // ─── Sesión y HttpClient ──────────────────────────────────────────────────

    [Fact]
    public void ICurrentSession_Y_ApiSession_SonLaMismaInstanciaSingleton()
    {
        var sp = CrearContenedor();

        var comoInterfaz = sp.GetRequiredService<ICurrentSession>();
        var comoConcreta = sp.GetRequiredService<ApiSession>();

        Assert.Same(comoConcreta, comoInterfaz);
    }

    [Fact]
    public void HttpClient_EsSingletonConBaseAddressTerminadaEnBarra()
    {
        var sp = CrearContenedor();

        var http1 = sp.GetRequiredService<HttpClient>();
        var http2 = sp.GetRequiredService<HttpClient>();

        Assert.Same(http1, http2);
        Assert.EndsWith("/", http1.BaseAddress!.ToString());
    }

    // ─── ViewModels: toda la cadena resuelve sin Infrastructure ───────────────

    [Theory]
    [InlineData(typeof(ShellMainViewModel))]
    [InlineData(typeof(ProductoListViewModel))]
    [InlineData(typeof(ProductoFormViewModel))]
    [InlineData(typeof(CategoriaListViewModel))]
    [InlineData(typeof(CategoriaFormViewModel))]
    [InlineData(typeof(ProveedorListViewModel))]
    [InlineData(typeof(ProveedorFormViewModel))]
    [InlineData(typeof(UnidadMedidaListViewModel))]
    [InlineData(typeof(UnidadMedidaFormViewModel))]
    [InlineData(typeof(EntradaRegistroViewModel))]
    [InlineData(typeof(SalidaRegistroViewModel))]
    [InlineData(typeof(MovimientoHistorialViewModel))]
    [InlineData(typeof(ValorizacionViewModel))]
    public void Contenedor_Resuelve_CadaViewModel(Type tipoVm)
    {
        var sp = CrearContenedor();

        var vm = sp.GetRequiredService(tipoVm);

        Assert.NotNull(vm);
    }

    [Fact]
    public void Contenedor_Resuelve_INavigationService_Y_IServicioGuardadoArchivo()
    {
        var sp = CrearContenedor();

        Assert.NotNull(sp.GetRequiredService<INavigationService>());
        Assert.NotNull(sp.GetRequiredService<IServicioGuardadoArchivo>());
    }
}
```

- [ ] **Step 2: Correr el test nuevo y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter ComposicionDIApiTests`
Expected: PASS (26 tests: 10 + 2 + 13 + 1).

- [ ] **Step 3: Borrar los 3 DI tests viejos**

Run:
```bash
git rm tests/StockApp.Presentation.Tests/DI/ComposicionDICatalogoTests.cs
git rm tests/StockApp.Presentation.Tests/DI/ComposicionDIMovimientosTests.cs
git rm tests/StockApp.Presentation.Tests/DI/ComposicionDIReportesTests.cs
```
Expected: 3 `rm` confirmados.

- [ ] **Step 4: Limpiar `StockApp.Presentation.Tests.csproj`** — quitar la referencia a Infrastructure y el paquete EF que solo existía por ella. Eliminar estas líneas:

```xml
    <ProjectReference Include="..\..\src\StockApp.Infrastructure\StockApp.Infrastructure.csproj" />
```

```xml
    <!-- Fija la version de EF Core Design para evitar el conflicto de version flotante
         (CS1705) entre Infrastructure (Npgsql arrastra 10.0.9) y la resolucion transitiva
         por defecto (10.0.4) cuando el proyecto no tiene referencia directa a EF Core. -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
```

(Se mantienen `Moq`, `Microsoft.Extensions.DependencyInjection` y las referencias a Application y Presentation. Nota: en este punto Presentation TODAVÍA referencia Infrastructure — el corte definitivo con verificación es Task 19; acá solo puede quedar EF transitivo hasta ese task.)

- [ ] **Step 5: Correr la suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASS, 0 failures. Verificar además: `rg -l "StockApp.Infrastructure" tests/StockApp.Presentation.Tests` → sin resultados.

- [ ] **Step 6: Commit**

```bash
git add -A tests/StockApp.Presentation.Tests
git commit -m "test(desktop): DI tests espejan la composicion API-only sin Infrastructure"
```

## Task 19: Criterio duro — `StockApp.Presentation` sin referencia a Infrastructure

**Files:**
- Modify: `src/StockApp.Presentation/StockApp.Presentation.csproj`
- Modify: `src/StockApp.Presentation/Converters/FechaUtcALocalConverter.cs` (solo un comentario XML-doc)

**Interfaces:**
- Consumes: todo el Bloque C anterior (nada en Presentation usa ya tipos de Infrastructure).
- Produces: el criterio de éxito duro de la fase — `StockApp.Presentation.csproj` sin `ProjectReference` a Infrastructure ni paquetes de EF/Npgsql, compilando con la suite verde.

- [ ] **Step 1: Editar `StockApp.Presentation.csproj`** — eliminar estas líneas:

```xml
    <ProjectReference Include="..\StockApp.Infrastructure\StockApp.Infrastructure.csproj" />
```

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.*">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="10.*" />
```

(El `ItemGroup` de ProjectReferences queda con ApiClient y Application solamente.)

- [ ] **Step 2: Limpiar el cref muerto en `FechaUtcALocalConverter.cs`** — sin la referencia, el `<see cref>` a un tipo de Infrastructure genera warning CS1574. Reemplazar:

```csharp
/// <see cref="StockApp.Application.Movimientos.MovimientoStockService"/> y
/// <see cref="StockApp.Infrastructure.Repositories.MovimientoStockRepository"/>) a la hora
```

por:

```csharp
/// <see cref="StockApp.Application.Movimientos.MovimientoStockService"/> y el
/// repositorio de movimientos del lado del servidor) a la hora
```

- [ ] **Step 3: Grep de verificación — cero rastros de Infrastructure en Presentation**

Run: `rg -l "StockApp.Infrastructure" src/StockApp.Presentation tests/StockApp.Presentation.Tests`
Expected: sin resultados (0 archivos).

Run: `rg -l "ConnectionStrings|UseNpgsql" src/StockApp.Presentation`
Expected: sin resultados.

- [ ] **Step 4: Compilar TODA la solución y correr la suite completa del repo**

Run:
```bash
dotnet build StockApp.sln
dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj
dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
```
Expected: build OK; PASS en las 5 suites (Api.Tests corre completa en Task 21, requiere Docker).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation
git commit -m "feat(desktop): Presentation sin referencia a Infrastructure (criterio duro 3b)"
```

---

## Bloque D — Documentación y verificación final (Tasks 20-21)

## Task 20: README de la API — arranque del servidor y configuración de los clientes desktop

**Files:**
- Modify: `src/StockApp.Api/README.md`

**Interfaces:**
- Consumes: la configuración `Api:BaseUrl` (Task 17) y el comportamiento de sesión/errores del cliente (Tasks 3-4).
- Produces: la documentación operativa que el spec deja como único entregable del deploy ("se documenta el arranque en el README").

- [ ] **Step 1: Agregar al final de `src/StockApp.Api/README.md` la sección:**

````markdown

## Clientes desktop (Fase 3b)

El desktop Avalonia ya no accede a Postgres: consume esta API vía `StockApp.ApiClient`
(clientes HTTP que implementan las mismas interfaces de `StockApp.Application`).
Multi-puesto: N desktops → 1 API → 1 Postgres.

### Apuntar un puesto al servidor

Editar `appsettings.json` junto al ejecutable del desktop:

```json
{ "Api": { "BaseUrl": "http://<ip-del-servidor>:5043" } }
```

Si la clave falta, el default es `http://localhost:5000`. Sin `ConnectionStrings`: el
desktop no conoce la base de datos.

### Arranque del servidor en la LAN

```bash
# desarrollo (localhost:5043)
dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http

# publicado, escuchando en todas las interfaces de la LAN
dotnet StockApp.Api.dll --urls http://0.0.0.0:5043
```

Requisitos: Postgres accesible (`ConnectionStrings:Default`) y `Jwt:Secret` configurado
(user-secrets o variable de entorno). La API migra su base al arrancar (Fase 3a, D9).

### Comportamiento del cliente

- **Sesión**: token JWT de jornada (`Jwt:ExpiracionHoras`, default 12 h). Ante un 401 con
  token (sesión vencida), el desktop vuelve al login con el aviso "Sesión vencida,
  ingresá de nuevo.".
- **Errores**: el cliente traduce el `problem+json` a las excepciones de dominio que los
  ViewModels ya muestran (404/409/400/403). El 409 de stock insuficiente viaja con
  extensiones estructuradas (`productoId`, `stockActual`, `cantidadSolicitada`) para
  preservar el flujo "¿forzar salida?" del desktop.
- **Servidor caído / timeout (10 s)**: mensaje accionable al usuario; el login permite
  reintentar.
- **Supuesto de red**: LAN interna sin TLS (HTTP plano) — fuera del alcance de 3b, igual
  que el instalador/deploy remoto.
````

- [ ] **Step 2: Commit**

```bash
git add src/StockApp.Api/README.md
git commit -m "docs(api): arranque del servidor y configuracion del desktop cliente"
```

## Task 21: Suite completa del repo + verificación orgánica end-to-end

**Files:** ninguno modificado — task de verificación final (convención del proyecto: probar con la app real).

**Interfaces:** ninguna.

- [ ] **Step 1: Correr la suite completa de cada proyecto de test**

Run:
```bash
dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj
dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj
dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj
dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj
dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj
dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj
```
Expected: PASS en los 6 proyectos, 0 failures (Api.Tests requiere Docker — Testcontainers).

- [ ] **Step 2: Greps del criterio duro**

Run: `rg -l "StockApp.Infrastructure" src/StockApp.Presentation src/StockApp.ApiClient tests/StockApp.Presentation.Tests tests/StockApp.ApiClient.Tests`
Expected: sin resultados.

Run: `rg -n "Location" src/StockApp.ApiClient`
Expected: sin resultados — el cliente jamás usa el header Location.

- [ ] **Step 3: Preparar el escenario "server virgen"**

Con el contenedor `stockapp-pg` corriendo (convención: queda siempre andando):

```bash
docker exec stockapp-pg psql -U stockapp -d stockapp -c 'TRUNCATE "Usuarios" RESTART IDENTITY CASCADE;'
```

(Vacía usuarios —y por cascada auditoría/movimientos que los referencian— para ejercitar el primer arranque real. Si se prefiere una base 100% virgen, alternativamente `DROP DATABASE`/`CREATE DATABASE` y dejar que la API migre al arrancar.)

- [ ] **Step 4: Levantar API y desktop reales**

Run (terminal 1): `dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http`
Run (terminal 2): `dotnet run --project src/StockApp.Presentation/StockApp.Presentation.csproj`

(El `appsettings.json` del desktop ya apunta a `http://localhost:5043`.)

- [ ] **Step 5: Guion manual end-to-end (spec 3b, sección Testing)**

1. **Primer arranque**: la app abre en la pantalla de primer arranque (la BD no tiene usuarios). Crear el admin (`admin` / `admin123`). Crear el 2do admin opcional (`admin2` / `admin456`) — verifica login por API + `AltaUsuarioAsync` con Bearer.
2. **Login**: entrar con `admin`. El shell muestra el nombre del usuario (la sesión se pobló desde el `LoginResponse`, sin decodificar JWT).
3. **Catálogo**: alta de categoría "Bebidas"; alta de unidad "Unidad" (o verificar que el form de producto la garantiza solo — garantizar-por-defecto); alta de producto "Agua 2L" (SKU-001, categoría Bebidas). Editar el nombre del producto y guardarlo (PUT sin Id en el body).
4. **Duplicado (409 → mensaje del servidor)**: intentar crear otra categoría "Bebidas" → el form muestra el mensaje de duplicado del servidor, la app NO crashea.
5. **Movimientos**: registrar una entrada de 10 unidades. Registrar una salida de 8 → OK. Registrar OTRA salida de 8 → aparece la confirmación "El stock quedará en -6. ¿Confirmar la salida igual?" (Mina 2 verificada end-to-end); confirmar y ver el stock negativo en el historial.
6. **Reportes**: abrir valorización (totales correctos) y exportar CSV (CsvExporter local intacto). Abrir stock por categoría y más movidos.
7. **Auditoría**: abrir el log — deben estar las altas, el cambio de producto y los movimientos, con el nombre de usuario.
8. **Servidor caído**: apagar la API (Ctrl+C en terminal 1) y ejecutar una acción (ej. refrescar productos) → mensaje accionable "No se pudo conectar con el servidor..." y la app sigue viva. Volver al login (o reiniciar la app): el login con la API caída muestra el error de conexión y permite reintentar.
9. **Sesión vencida (401 → login con aviso)**: levantar la API de nuevo, loguearse, y reiniciarla con otro secreto para invalidar el token: `Jwt__Secret="otro-secreto-de-al-menos-32-caracteres!!" dotnet run --project src/StockApp.Api/StockApp.Api.csproj --launch-profile http`. Ejecutar cualquier acción en el desktop → la app navega sola al login con "Sesión vencida, ingresá de nuevo.".
10. **Criterio duro final**: `rg "StockApp.Infrastructure" src/StockApp.Presentation/StockApp.Presentation.csproj` → sin resultados, con la app recién usada andando.

Expected: los 10 pasos se comportan como se describe. Si alguno falla, volver al task correspondiente, corregir y repetir.

- [ ] **Step 6: Bajar API y desktop**

Run: `Ctrl+C` en ambas terminales. El contenedor `stockapp-pg` queda corriendo (convención del proyecto).

No hay commit en este task — es puramente de verificación.

---

## Self-Review

**1. Cobertura del spec (2026-07-09-fase-3b-cliente-http-design.md):**

| Requisito del spec | Tasks |
|---|---|
| Proyecto nuevo `StockApp.ApiClient` (solo Application+Domain) | 1 |
| ~10 ApiClients, mismas interfaces, VMs intactos | 6, 7, 8, 9, 10, 11, 12, 13, 14 |
| `ApiSession : ICurrentSession` poblada desde LoginResponse (D8) | 2, 6 |
| `AuthTokenHandler` (Bearer + 401 → evento → login con aviso) | 4, 16, 17 |
| Traducción de errores centralizada (404/409/400/403/conexión) | 3 (+5 para el 409 tipado) |
| `ServidorNoDisponibleException` nueva con mensaje claro | 1 |
| Login con servidor caído muestra error y permite reintentar | 16 |
| `App.axaml.cs`: salen Infra/DbContext/initializer/servicios locales; entran HttpClient+clients+sesión | 17 |
| `IAuthorizationService` local: eliminar si nadie lo consulta | OQ-1, 17 |
| Quitar ProjectReference a Infrastructure (criterio duro) | 19 (+15 y 18 que lo desbloquean) |
| `appsettings.json`: `Api:BaseUrl`, sin `ConnectionStrings` | 17 |
| `CsvExporter` local | OQ-2, 17 (sin mudanza — corrección al spec) |
| Flujo de primer arranque contra bootstrap de 3a | 6, 21 (paso 1) |
| Tests: contrato de cada método de cada client + auth + errores | 3, 4, 6-14 (unit con handler falso; ver nota de estrategia) |
| Tests de Presentation existentes intactos | 16 (solo se AGREGAN tests), 18 (los 3 DI tests se reemplazan porque codificaban la composición vieja — OQ-5) |
| Verificación orgánica final (API real + desktop real) | 21 |
| README con el arranque del servidor | 20 |
| Fuera de alcance respetado (sin offline/refresh/TLS/Polly) | Global Constraints |

Las dos minas descubiertas están desactivadas explícitamente: Mina 1 (updater en Infrastructure) en Task 15; Mina 2 (`StockInsuficienteException` tipada para el flujo forzar) en Tasks 5, 3 y 12, verificada end-to-end en Task 21 paso 5.

**2. Placeholders:** ninguno — no hay "TBD", "similar a Task N" sin código, ni pasos sin bloque ejecutable. Cada client repite su código completo; los tests de VMs usan los helpers existentes citados textualmente desde los archivos reales del repo (leídos antes de escribir el plan).

**3. Consistencia de tipos entre tasks:**
- `ServidorNoDisponibleException(Exception? inner = null)` + `MensajePorDefecto` (Task 1) — usada con esa firma en Tasks 3, 6, 16, 17.
- `ApiSession.EstablecerSesion(UsuarioSesion, string)` / `Token` / `DispararSesionVencida()` (Task 2) — consumidas idénticas en Tasks 4, 6, 18; `UsuarioSesion(int, string, RolUsuario, string?)` respeta el orden posicional real del record de Application (Id, NombreUsuario, Rol, NombreCompleto).
- `ApiErrores.EnviarAsync(Func<Task<HttpResponseMessage>>)` y `AsegurarExitoAsync(HttpResponseMessage)` (Task 3) — usadas con esas firmas exactas en los 10 clients; `IdCreado(int Id)` en Tasks 7-11.
- `ApiQuery.Construir(params (string, string?)[])` y `ApiQuery.Fecha(DateTime?)` (Task 3) — Tasks 10, 12, 13, 14.
- Extensiones del 409: claves `productoId`/`stockActual`/`cantidadSolicitada` idénticas en Task 5 (API las escribe), Task 3 (`ProblemaJson` las lee) y los tests de Tasks 3 y 12.
- `MostrarLoginConAviso(string)` (Task 16) — invocada con esa firma en Task 17; el texto del aviso "Sesión vencida, ingresá de nuevo." es idéntico en Task 16 (test), Task 17 (wiring), Task 20 (README) y Task 21 (guion).
- Rutas relativas sin `/` inicial en los 10 clients, y `BaseAddress` con `/` final en Task 17, Task 18 y `TestHttp` (Task 4) — sin excepciones.

**Correcciones aplicadas durante el self-review:** se fijó el orden del Bloque C (mudanza del updater ANTES de los retoques de VMs y del corte de la referencia) tras verificar que `App.axaml.cs` consume los tipos de `Infrastructure.Actualizaciones`; y se explicitó en Task 18 que el paquete EF Design del proyecto de tests recién puede eliminarse ahí porque Presentation todavía arrastra EF transitivo hasta Task 19. El resto del plan se escribió leyendo el código real (interfaces, endpoints, VMs, tests y csproj) vía sub-agentes de exploración antes de redactar cada task, así que firmas y call-sites ya estaban verificados.

