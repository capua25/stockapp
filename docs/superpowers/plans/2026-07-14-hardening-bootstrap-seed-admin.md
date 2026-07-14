# Seed de Admin en Arranque + Eliminación del Bootstrap HTTP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cerrar la deuda de seguridad D7 haciendo que el primer admin nazca por seed en el arranque de la API (fail-fast) y eliminando por completo el bootstrap HTTP anónimo (endpoints, ApiClient y flujo del desktop).

**Architecture:** Una clase `BootstrapAdminSeeder` corre dentro del scope de migración de `Program.cs`, justo después de `MigrateAsync()`: si la BD no tiene usuarios, lee `Bootstrap:AdminUser`/`Bootstrap:Password` de la configuración y reutiliza `IPrimerArranqueService.CrearAdminInicialAsync` (que ya trae validación de contraseña y semáforo anti-TOCTOU). Se eliminan los endpoints `GET /auth/primer-arranque` y `POST /auth/primer-admin`, el `PrimerArranqueApiClient` del desktop, y el flujo de "primer arranque" del `ShellViewModel` (el desktop arranca directo al login).

**Tech Stack:** .NET (ASP.NET Core Minimal API), EF Core + Npgsql (PostgreSQL), xUnit + Testcontainers (integración con Postgres real), Avalonia (desktop MVVM), CommunityToolkit.Mvvm.

## Global Constraints

- Commits: conventional commits, en español, SIN `Co-Authored-By` ni atribución de IA.
- Rama: trabajar sobre una rama de feature y luego mergear a `main` con `--ff-only` + push (convención del proyecto). NO abrir PR.
- La interfaz `IPrimerArranqueService` y la clase `PrimerArranqueService` (server) NO se eliminan: las reutiliza el seed.
- `ContrasenaValidator` NO se toca: lo comparte `UsuarioService` (alta y cambio de contraseña).
- `Bootstrap:AdminUser` y `Bootstrap:Password` no van en `appsettings.json` versionado (user-secrets en dev, env vars `Bootstrap__AdminUser`/`Bootstrap__Password` en el server). Mismo criterio que `Jwt:Secret`.
- Suite completa verde al cerrar cada task: `dotnet test` a nivel solución (`StockApp.sln`).

---

### Task 1: `BootstrapAdminSeeder` + tests unitarios

**Files:**
- Create: `src/StockApp.Api/Auth/BootstrapAdminSeeder.cs`
- Test: `tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederTests.cs`

**Interfaces:**
- Consumes: `IPrimerArranqueService` (de `StockApp.Application.Auth`) — `Task<bool> RequiereCrearAdminAsync()` y `Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)`.
- Produces: `BootstrapAdminSeeder(IPrimerArranqueService primerArranque, string? adminUser, string? adminPassword)` con método `Task SembrarAsync()`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederTests.cs`:

```csharp
using System.Threading.Tasks;
using StockApp.Api.Auth;
using StockApp.Application.Auth;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class BootstrapAdminSeederTests
{
    // Fake manual de IPrimerArranqueService: evita depender de un paquete de mocking
    // en este proyecto de tests. Registra las llamadas para poder asertar.
    private sealed class PrimerArranqueFake : IPrimerArranqueService
    {
        private readonly bool _requiere;
        public PrimerArranqueFake(bool requiere) => _requiere = requiere;

        public int VecesCreado { get; private set; }
        public string? UltimoUsuario { get; private set; }
        public string? UltimaContrasena { get; private set; }

        public Task<bool> RequiereCrearAdminAsync() => Task.FromResult(_requiere);

        public Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
        {
            VecesCreado++;
            UltimoUsuario = nombreUsuario;
            UltimaContrasena = contrasenaPlana;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task SembrarAsync_CuandoYaHayUsuarios_NoCreaAdmin()
    {
        var fake = new PrimerArranqueFake(requiere: false);
        var seeder = new BootstrapAdminSeeder(fake, "admin", "secreta123");

        await seeder.SembrarAsync();

        Assert.Equal(0, fake.VecesCreado);
    }

    [Fact]
    public async Task SembrarAsync_DbVirgenConCredenciales_CreaAdmin()
    {
        var fake = new PrimerArranqueFake(requiere: true);
        var seeder = new BootstrapAdminSeeder(fake, "admin", "secreta123");

        await seeder.SembrarAsync();

        Assert.Equal(1, fake.VecesCreado);
        Assert.Equal("admin", fake.UltimoUsuario);
        Assert.Equal("secreta123", fake.UltimaContrasena);
    }

    [Theory]
    [InlineData(null, "secreta123")]
    [InlineData("", "secreta123")]
    [InlineData("   ", "secreta123")]
    [InlineData("admin", null)]
    [InlineData("admin", "")]
    [InlineData("admin", "   ")]
    public async Task SembrarAsync_DbVirgenSinCredenciales_LanzaInvalidOperation(string? user, string? pass)
    {
        var fake = new PrimerArranqueFake(requiere: true);
        var seeder = new BootstrapAdminSeeder(fake, user, pass);

        await Assert.ThrowsAsync<InvalidOperationException>(() => seeder.SembrarAsync());
        Assert.Equal(0, fake.VecesCreado);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~BootstrapAdminSeederTests"`
Expected: FALLA de compilación — `BootstrapAdminSeeder` no existe todavía.

- [ ] **Step 3: Escribir la implementación mínima**

Crear `src/StockApp.Api/Auth/BootstrapAdminSeeder.cs`:

```csharp
using StockApp.Application.Auth;

namespace StockApp.Api.Auth;

/// <summary>
/// Siembra el usuario Admin inicial en el arranque de la API cuando la base de datos no
/// tiene ningún usuario. Reemplaza al bootstrap HTTP anónimo (endpoints /auth/primer-arranque
/// y /auth/primer-admin, eliminados) que abría una ventana de "admin génesis" explotable en LAN.
/// Idempotente: si ya existe algún usuario, no hace nada y no lee la configuración.
/// Fail-fast: con la BD vacía y sin credenciales configuradas, lanza y la API no arranca
/// (mismo criterio que el fail-fast de Jwt:Secret).
/// </summary>
public sealed class BootstrapAdminSeeder
{
    private readonly IPrimerArranqueService _primerArranque;
    private readonly string? _adminUser;
    private readonly string? _adminPassword;

    public BootstrapAdminSeeder(
        IPrimerArranqueService primerArranque,
        string? adminUser,
        string? adminPassword)
    {
        _primerArranque = primerArranque;
        _adminUser = adminUser;
        _adminPassword = adminPassword;
    }

    public async Task SembrarAsync()
    {
        if (!await _primerArranque.RequiereCrearAdminAsync())
            return;

        if (string.IsNullOrWhiteSpace(_adminUser) || string.IsNullOrWhiteSpace(_adminPassword))
            throw new InvalidOperationException(
                "La base de datos no tiene usuarios y falta configurar el administrador inicial. " +
                "Definí 'Bootstrap:AdminUser' y 'Bootstrap:Password'. En desarrollo: " +
                "dotnet user-secrets set \"Bootstrap:AdminUser\" \"<usuario>\" y " +
                "dotnet user-secrets set \"Bootstrap:Password\" \"<contraseña-de-al-menos-6-caracteres>\".");

        // CrearAdminInicialAsync valida longitud de contraseña y nombre en blanco (ArgumentException),
        // y crea el Admin con el semáforo anti-TOCTOU. Si la contraseña es inválida, la excepción
        // burbujea y la API no arranca (fail-fast).
        await _primerArranque.CrearAdminInicialAsync(_adminUser, _adminPassword);
    }
}
```

- [ ] **Step 4: Correr el test para verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~BootstrapAdminSeederTests"`
Expected: PASA (8 casos: 1 + 1 + 6 del Theory).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Auth/BootstrapAdminSeeder.cs tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederTests.cs
git commit -m "feat(api): BootstrapAdminSeeder para sembrar el admin inicial en el arranque (D7)"
```

---

### Task 2: Cablear el seed en el arranque + config de test + test de integración

**Files:**
- Modify: `src/StockApp.Api/Program.cs:207-211` (scope de migración)
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs:20-48` (constantes + `AddInMemoryCollection`)
- Test: `tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederIntegracionTests.cs`

**Interfaces:**
- Consumes: `BootstrapAdminSeeder` (Task 1); `ApiFactory.CreateClient()`, `ApiFactory.Services`, `ApiTestBase` (`protected readonly ApiFactory Factory`).
- Produces: constantes `ApiFactory.AdminUsuarioDePrueba` y `ApiFactory.AdminPasswordDePrueba`.

- [ ] **Step 1: Escribir el test de integración que falla**

Crear `tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederIntegracionTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Auth;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests.Auth;

public class BootstrapAdminSeederIntegracionTests : ApiTestBase
{
    public BootstrapAdminSeederIntegracionTests(ApiFactory factory) : base(factory) { }

    [Fact]
    public async Task SembrarAsync_DbVirgen_CreaAdminQuePuedeLoguearse()
    {
        // La base arranca vacía (TRUNCATE en ApiTestBase). Resolvemos el servicio real
        // (PrimerArranqueService + repo contra el Postgres del contenedor) y sembramos.
        using var scope = Factory.Services.CreateScope();
        var primerArranque = scope.ServiceProvider.GetRequiredService<IPrimerArranqueService>();
        var seeder = new BootstrapAdminSeeder(primerArranque, "admin-seed-test", "clave-seed-123");

        await seeder.SembrarAsync();

        var client = Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/auth/login", new LoginRequest("admin-seed-test", "clave-seed-123"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.Equal("admin-seed-test", body!.Usuario.NombreUsuario);
        Assert.Equal(RolUsuario.Admin, body.Usuario.Rol);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter "FullyQualifiedName~BootstrapAdminSeederIntegracionTests"`
Expected: FALLA — al construir el host, `Program.cs` todavía no llama al seed y (una vez cableado) `ApiFactory` no provee `Bootstrap:*`. Antes de cablear, el test compila pero el seed no corre en el arranque; el objetivo del cableado es que el arranque del host no rompa con fail-fast. Verificá que falla o no compila según el estado actual.

- [ ] **Step 3: Cablear el seed en `Program.cs`**

En `src/StockApp.Api/Program.cs`, reemplazar el bloque del scope de migración (líneas 207-211):

```csharp
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}
```

por:

```csharp
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
}
```

Nota: `using StockApp.Api.Auth;` (namespace de `BootstrapAdminSeeder`) ya está en `Program.cs` porque se usa `JwtOptionsFactory` del mismo namespace. `IPrimerArranqueService` (de `StockApp.Application.Auth`) ya está importado (registro en L93).

- [ ] **Step 4: Proveer la config de bootstrap en `ApiFactory`**

En `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`, agregar dos constantes junto a `JwtSecretDePrueba` (después de la línea 20):

```csharp
    public const string JwtSecretDePrueba = "clave-de-prueba-de-al-menos-32-caracteres-1234567890";
    public const string AdminUsuarioDePrueba = "admin-arranque";
    public const string AdminPasswordDePrueba = "arranque-secreta-123";
```

Y agregar las claves al `AddInMemoryCollection` (diccionario en líneas 43-47), quedando:

```csharp
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _container.GetConnectionString(),
                ["Jwt:Secret"] = JwtSecretDePrueba,
                ["Bootstrap:AdminUser"] = AdminUsuarioDePrueba,
                ["Bootstrap:Password"] = AdminPasswordDePrueba,
            });
```

- [ ] **Step 5: Correr los tests para verificar que pasan**

Run: `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj`
Expected: PASA toda la suite de Api.Tests. El host arranca con el seed activo (la config `Bootstrap:*` de `ApiFactory` evita el fail-fast) y el test de integración crea y loguea el admin. Ojo: `PrimerArranqueEndpointTests` todavía existe y sigue verde en este punto (se elimina en la Task 5).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api/Program.cs tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs tests/StockApp.Api.Tests/Auth/BootstrapAdminSeederIntegracionTests.cs
git commit -m "feat(api): cablear el seed de admin en el arranque y proveer Bootstrap:* en ApiFactory (D7)"
```

---

### Task 3: Desacoplar `ShellViewModel` del bootstrap (arranque directo al login)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs:49`
- Modify: `tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs`

**Interfaces:**
- Produces: `ShellViewModel` sin parámetro `IPrimerArranqueService` en el constructor; `InicializarAsync()` va directo a `MostrarLogin()`; se elimina `MostrarPrimerArranque()`.

- [ ] **Step 1: Ajustar los tests primero (rojo)**

En `tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs`:
- En el helper de construcción del `ShellViewModel` (alrededor de las líneas 31-36), eliminar la creación del `Mock<IPrimerArranqueService>` y su `.Setup(p => p.RequiereCrearAdminAsync())`, y quitar ese mock del constructor del `ShellViewModel`.
- Eliminar el test `Inicializar_RequiereCrearAdmin_MuestraPrimerArranque` (alrededor de L70-76).
- Eliminar el test `MostrarPrimerArranque_EstablecePrimerArranqueViewModel` (alrededor de L102-108).
- En los tests que quedan y hacían `.Setup(p => p.RequiereCrearAdminAsync())` (alrededor de L128 y L147), quitar ese Setup; ajustar los asserts para el nuevo comportamiento (tras `InicializarAsync()`, `CurrentViewModel` es `LoginViewModel`).

En `tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs`:
- Línea 49: eliminar el argumento `Mock.Of<IPrimerArranqueService>(),` del constructor del `ShellViewModel`.

En `tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs`:
- En cada test, eliminar `var primerArranqueMock = new Mock<IPrimerArranqueService>();` y su `.Setup(p => p.RequiereCrearAdminAsync())`, y quitar ese mock del constructor del `ShellViewModel` (líneas 58,60 / 104,106 / 149,151 / 195,197 / 243,245 / 300,302 / 355,357 / 409,411).

Eliminar cualquier `using` de `IPrimerArranqueService` que quede sin uso en esos archivos.

- [ ] **Step 2: Correr para verificar rojo (compilación)**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter "FullyQualifiedName~ShellViewModel"`
Expected: FALLA de compilación — el constructor de `ShellViewModel` todavía exige `IPrimerArranqueService`.

- [ ] **Step 3: Modificar `ShellViewModel`**

En `src/StockApp.Presentation/ViewModels/ShellViewModel.cs`:

(a) Eliminar el campo (línea 17):
```csharp
    private readonly IPrimerArranqueService  _primerArranqueService;
```

(b) En el constructor (líneas 36-52), eliminar el parámetro `IPrimerArranqueService primerArranqueService,` y la asignación `_primerArranqueService = primerArranqueService;`. El constructor queda:

```csharp
    public ShellViewModel(
        IAuthService            authService,
        IUsuarioService         usuarioService,
        INavigationService      navigation,
        CoordinadorActualizacion coordinadorActualizacion,
        IUiDispatcher           uiDispatcher,
        IInfoApp                infoApp)
    {
        _authService              = authService;
        _usuarioService           = usuarioService;
        _navigation               = navigation;
        _coordinadorActualizacion = coordinadorActualizacion;
        _uiDispatcher             = uiDispatcher;
        _infoApp                  = infoApp;
    }
```

(c) Reemplazar `InicializarAsync()` completo (líneas 58-87) por:

```csharp
    public Task InicializarAsync()
    {
        // El primer admin ahora nace por seed en el arranque de la API (D7); el desktop
        // ya no consulta "primer arranque" y va directo al login.
        MostrarLogin();

        // Fire-and-forget controlado: el coordinador no debe tumbar el arranque si falla.
        // _tareaActualizacion se expone como internal para que los tests puedan awaitarla
        // y evitar condiciones de carrera con Task.Delay.
        _tareaActualizacion = EvaluarYAsignarOverlayAsync();
        _ = _tareaActualizacion;
        return Task.CompletedTask;
    }
```

(d) Eliminar el método `MostrarPrimerArranque()` completo (líneas 194-201).

(e) Eliminar el `using` de `StockApp.Application.Auth` SOLO si queda sin uso (verificar que ningún otro símbolo de ese namespace se use en el archivo; `IAuthService`/`IUsuarioService` suelen estar en otros namespaces — confirmar antes de quitar).

- [ ] **Step 4: Correr para verificar verde**

Run: `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj`
Expected: PASA. `InicializarAsync()` deja `CurrentViewModel` como `LoginViewModel` y dispara la evaluación de actualizaciones.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/ShellViewModel.cs tests/StockApp.Presentation.Tests/ViewModels/ShellViewModelTests.cs tests/StockApp.Presentation.Tests/ViewModels/LoginViewModelTests.cs tests/StockApp.Presentation.Tests/Actualizaciones/ShellViewModelActualizacionTests.cs
git commit -m "refactor(desktop): el arranque del desktop va directo al login, sin consultar primer arranque (D7)"
```

---

### Task 4: Eliminar la View/ViewModel de primer arranque, el ApiClient y sus registros

**Files:**
- Delete: `src/StockApp.Presentation/Views/PrimerArranqueView.axaml`
- Delete: `src/StockApp.Presentation/Views/PrimerArranqueView.axaml.cs`
- Delete: `src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs`
- Delete: `tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs`
- Delete: `src/StockApp.ApiClient/PrimerArranqueApiClient.cs`
- Delete: `tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs:156` (registro DI)
- Modify: `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs:57,97`

**Interfaces:**
- Consumes: estado post-Task 3 (el `ShellViewModel` ya no referencia `PrimerArranqueViewModel` ni `IPrimerArranqueService`).

- [ ] **Step 1: Ajustar `ComposicionDIApiTests` (rojo)**

En `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs`:
- Eliminar la línea 57: `services.AddTransient<IPrimerArranqueService, PrimerArranqueApiClient>();`
- Eliminar la línea 97: `[InlineData(typeof(IPrimerArranqueService), typeof(PrimerArranqueApiClient))]`
- Eliminar los `using` de `StockApp.ApiClient`/`StockApp.Application.Auth` que queden sin uso por esas eliminaciones (verificar el resto del archivo antes de quitarlos).

- [ ] **Step 2: Eliminar el registro DI del ApiClient**

En `src/StockApp.Presentation/App.axaml.cs`, eliminar la línea 156:
```csharp
        services.AddTransient<IPrimerArranqueService, PrimerArranqueApiClient>();
```

- [ ] **Step 3: Borrar los archivos muertos**

```bash
git rm src/StockApp.Presentation/Views/PrimerArranqueView.axaml \
       src/StockApp.Presentation/Views/PrimerArranqueView.axaml.cs \
       src/StockApp.Presentation/ViewModels/PrimerArranqueViewModel.cs \
       tests/StockApp.Presentation.Tests/ViewModels/PrimerArranqueViewModelTests.cs \
       src/StockApp.ApiClient/PrimerArranqueApiClient.cs \
       tests/StockApp.ApiClient.Tests/PrimerArranqueApiClientTests.cs
```

- [ ] **Step 4: Compilar y correr toda la suite**

Run: `dotnet test`
Expected: PASA toda la solución. Ya no quedan referencias a `PrimerArranqueViewModel`, `PrimerArranqueView` ni `PrimerArranqueApiClient`. Si el compilador reporta un símbolo sin resolver, es un `using` o referencia residual — eliminarla.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(desktop): eliminar View/ViewModel/ApiClient de primer arranque y su registro DI (D7)"
```

---

### Task 5: Eliminar los endpoints de bootstrap del servidor

**Files:**
- Modify: `src/StockApp.Api/Endpoints/AuthEndpoints.cs` (eliminar endpoints y records huérfanos)
- Delete: `tests/StockApp.Api.Tests/PrimerArranqueEndpointTests.cs`

**Interfaces:**
- Consumes: nada nuevo. `PrimerArranqueService`/`IPrimerArranqueService` siguen registrados y vivos (los usa el seed).

- [ ] **Step 1: Borrar el test de los endpoints (rojo)**

```bash
git rm tests/StockApp.Api.Tests/PrimerArranqueEndpointTests.cs
```

- [ ] **Step 2: Eliminar los endpoints y records huérfanos**

En `src/StockApp.Api/Endpoints/AuthEndpoints.cs`:
- Eliminar el endpoint `GET /primer-arranque` (líneas 53-57).
- Eliminar el endpoint `POST /primer-admin` (líneas 59-64).
- Eliminar los records ahora sin uso: `PrimerArranqueEstadoResponse` (línea 11) y `CrearAdminInicialRequest` (línea 12).

El archivo queda con el `MapGroup("/auth")`, el `MapPost("/login", ...)` y `return app;`. Conservar los records `LoginRequest`, `UsuarioLoginResponse` y `LoginResponse`. Si el `using StockApp.Domain.Enums;` sigue siendo necesario (lo usa `UsuarioLoginResponse` con `RolUsuario`), conservarlo.

- [ ] **Step 3: Compilar y correr toda la suite**

Run: `dotnet test`
Expected: PASA toda la solución. No debe quedar ninguna referencia a `/auth/primer-arranque` ni `/auth/primer-admin`.

- [ ] **Step 4: Verificación dura (grep)**

Run:
```bash
rg -n "primer-arranque|primer-admin|PrimerArranqueApiClient|PrimerArranqueViewModel|PrimerArranqueEstadoResponse|CrearAdminInicialRequest" src tests
```
Expected: 0 resultados (los únicos símbolos "PrimerArranque" que sobreviven son `IPrimerArranqueService` y `PrimerArranqueService` del lado server, más sus tests en `StockApp.Application.Tests`).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(api): eliminar endpoints de bootstrap HTTP /auth/primer-arranque y /auth/primer-admin (D7)"
```

---

### Task 6: Verificación final + documentación de configuración

**Files:**
- Modify: `src/StockApp.Api/README.md` (documentar `Bootstrap:AdminUser`/`Bootstrap:Password`)

- [ ] **Step 1: Suite completa verde**

Run: `dotnet test`
Expected: PASA la solución completa (todas las suites). Anotar el total de tests.

- [ ] **Step 2: Documentar la config de bootstrap en el README de la API**

En `src/StockApp.Api/README.md`, agregar (cerca de donde se documenta `Jwt:Secret` y el arranque en LAN) una nota:

```markdown
## Bootstrap del administrador inicial

En el primer arranque contra una base de datos vacía, la API crea el usuario Admin inicial
leyendo dos claves de configuración (NO van en appsettings.json):

- `Bootstrap:AdminUser` — nombre del administrador inicial.
- `Bootstrap:Password` — su contraseña (mínimo 6 caracteres).

En desarrollo (user-secrets):

    dotnet user-secrets set "Bootstrap:AdminUser" "admin"
    dotnet user-secrets set "Bootstrap:Password" "<contraseña-segura>"

En el servidor (variables de entorno):

    Bootstrap__AdminUser=admin
    Bootstrap__Password=<contraseña-segura>

Si la base está vacía y faltan estas claves, la API NO arranca (fail-fast) con un mensaje
claro. Si ya existe algún usuario, la configuración se ignora. La rotación de contraseña y
el alta de más administradores se hacen desde el desktop, logueado como admin.

Ya NO existen endpoints HTTP de bootstrap (`/auth/primer-arranque` y `/auth/primer-admin`
fueron eliminados): el bootstrap es un paso local del servidor, sin superficie de red.
```

- [ ] **Step 3: Verificación orgánica (manual, con el stack real)**

Levantar el stack y verificar a mano (contenedor `stockapp-pg`, API real, desktop real):
1. Con una BD que ya tiene usuarios: la API arranca normal y el desktop abre en el login.
2. (Opcional, con BD vacía en un contenedor limpio y sin `Bootstrap:*`): la API no arranca y loguea el mensaje de fail-fast.
3. Con `Bootstrap:*` configurado y BD vacía: la API arranca, crea el admin, y el login del desktop funciona con esas credenciales.

- [ ] **Step 4: Commit de la documentación**

```bash
git add src/StockApp.Api/README.md
git commit -m "docs(api): documentar el bootstrap del admin inicial por configuración (D7)"
```

- [ ] **Step 5: Merge a main**

```bash
git checkout main
git merge --ff-only <rama-de-feature>
git push origin main
```

---

## Notas de ejecución

- Orden de tasks pensado para que NO haya estado intermedio con endpoints muertos en uso: primero nace el admin por seed (1-2), luego el desktop deja de consultar el bootstrap (3-4), y recién al final se borran los endpoints del server (5). En ningún punto intermedio el desktop llama a un endpoint inexistente.
- Todos los consumidores de `IPrimerArranqueService` en el desktop se eliminan; la interfaz sobrevive solo del lado server (seed).
- Si algún test de `StockApp.Application.Tests` (por ej. `PrimerArranqueServiceTests`) referencia el servicio, NO se toca: `PrimerArranqueService` sigue vivo.
