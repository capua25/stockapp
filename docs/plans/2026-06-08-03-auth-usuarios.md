# Incremento 3: Auth + Usuarios + Autorización — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir la lógica completa de autenticación, sesión, ABM de usuarios, autorización
por rol y auditoría de eventos sensibles sobre las entidades `Usuario` y `LogAuditoria` que
ya existen desde el Inc 2. El foco es la capa Application e Infrastructure; la UI de Avalonia
(pantalla de login y ABM) se menciona pero se trata como integración liviana — la frontera de
seguridad real vive en Application.

**Architecture:**
- `StockApp.Application/Auth/` — casos de uso: login, logout, primer arranque (seed Admin),
  autorización (guard de rol), ABM de usuarios.
- `StockApp.Application/Interfaces/` — contratos que Application expone e Infrastructure implementa:
  `IPasswordHasher`, `ICurrentSession`, `IUsuarioRepository`, `IAuditLogger`.
- `StockApp.Infrastructure/Auth/` — implementaciones: `BcryptPasswordHasher`, `InMemorySession`.
- `StockApp.Infrastructure/Repositories/` — `UsuarioRepository` (EF Core).
- `StockApp.Infrastructure/Services/AuditService.cs` — escribe `LogAuditoria` a la BD.
- Tests en los proyectos espejo correspondientes.

**Tech Stack:** .NET 10, C#, EF Core 10 + SQLite, xUnit, BCrypt.Net-Next.

---

## File Structure

```
src/
  StockApp.Application/
    Interfaces/
      IPasswordHasher.cs
      ICurrentSession.cs
      IUsuarioRepository.cs
      IAuditLogger.cs
    Auth/
      AuthService.cs            # login / logout; valida contra BD, actualiza UltimoAcceso
      PrimerArranqueService.cs  # detecta BD vacía, orquesta creación del Admin inicial
      UsuarioService.cs         # ABM de usuarios (solo Admin)
    Authorization/
      IAuthorizationService.cs  # contrato del guard de rol
      AuthorizationService.cs   # implementación: verifica RolUsuario vs acción requerida
      Permisos.cs               # constantes legibles de las acciones del sistema
  StockApp.Infrastructure/
    Auth/
      BcryptPasswordHasher.cs
      InMemorySession.cs
    Repositories/
      UsuarioRepository.cs
    Services/
      AuditService.cs
tests/
  StockApp.Application.Tests/
    Auth/
      AuthServiceTests.cs
      PrimerArranqueServiceTests.cs
      UsuarioServiceTests.cs
    Authorization/
      AuthorizationServiceTests.cs
  StockApp.Infrastructure.Tests/
    Auth/
      BcryptPasswordHasherTests.cs
      InMemorySessionTests.cs
    Repositories/
      UsuarioRepositoryTests.cs
    Services/
      AuditServiceTests.cs
```

Responsabilidad de cada unidad:
- `IPasswordHasher` / `BcryptPasswordHasher`: abstracción de hashing; la Application no
  sabe que el algoritmo es BCrypt (principio de inversión de dependencias).
- `ICurrentSession` / `InMemorySession`: estado de sesión como singleton; desacoplado de
  cualquier almacenamiento persistente.
- `AuthService`: caso de uso de login/logout. Único punto que toca credenciales.
- `PrimerArranqueService`: detecta si hay usuarios; si no, solicita la creación del Admin.
- `UsuarioService`: ABM protegido; verifica internamente que el llamador es Admin.
- `AuthorizationService`: guard de rol que todos los servicios de Application pueden llamar
  antes de ejecutar acciones restringidas.
- `AuditService`: escribe `LogAuditoria`; lo llaman los servicios que producen eventos sensibles.

---

## Task 1: Hashing de contraseñas (BCrypt)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IPasswordHasher.cs`
- Create: `src/StockApp.Infrastructure/Auth/BcryptPasswordHasher.cs`

### Paquete NuGet

```bash
dotnet add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj \
  package BCrypt.Net-Next
```

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Infrastructure.Tests/Auth/BcryptPasswordHasherTests.cs`:

```csharp
using StockApp.Infrastructure.Auth;
using Xunit;

namespace StockApp.Infrastructure.Tests.Auth;

public class BcryptPasswordHasherTests
{
    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProduceHashNoVacio()
    {
        var hash = _hasher.Hash("miContrasena123");

        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Verify_ConContrasenaCorrecta_RetornaTrue()
    {
        var hash = _hasher.Hash("contrasenaCorrecta");

        Assert.True(_hasher.Verify("contrasenaCorrecta", hash));
    }

    [Fact]
    public void Verify_ConContrasenaIncorrecta_RetornaFalse()
    {
        var hash = _hasher.Hash("contrasenaCorrecta");

        Assert.False(_hasher.Verify("contrasenaIncorrecta", hash));
    }

    [Fact]
    public void Hash_MismaContrasena_ProduceDosHashesDistintos()
    {
        // BCrypt embebe sal aleatoria en cada hash → misma entrada, hashes distintos
        var hash1 = _hasher.Hash("mismaContrasena");
        var hash2 = _hasher.Hash("mismaContrasena");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void Hash_NoGuardaContrasenaEnTextoPlano()
    {
        var contrasena = "secreto123";
        var hash = _hasher.Hash(contrasena);

        Assert.DoesNotContain(contrasena, hash);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación (`BcryptPasswordHasher` no existe todavía).

### Implementación

- [ ] **Step 3: Crear la interfaz**

Create `src/StockApp.Application/Interfaces/IPasswordHasher.cs`:

```csharp
namespace StockApp.Application.Interfaces;

/// <summary>
/// Abstracción de hashing de contraseñas. Application no conoce el algoritmo concreto.
/// </summary>
public interface IPasswordHasher
{
    /// <summary>Calcula el hash con sal de <paramref name="plaintext"/>.</summary>
    string Hash(string plaintext);

    /// <summary>Verifica que <paramref name="plaintext"/> corresponde a <paramref name="hash"/>.</summary>
    bool Verify(string plaintext, string hash);
}
```

- [ ] **Step 4: Crear la implementación BCrypt**

Create `src/StockApp.Infrastructure/Auth/BcryptPasswordHasher.cs`:

```csharp
using StockApp.Application.Interfaces;
using BCrypt.Net;

namespace StockApp.Infrastructure.Auth;

/// <summary>
/// Implementación de <see cref="IPasswordHasher"/> usando BCrypt.Net-Next.
/// Work factor 12 (≈250 ms en hardware moderno; ajustar si cambia el hardware).
/// </summary>
public class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;

    public string Hash(string plaintext)
        => BCrypt.Net.BCrypt.HashPassword(plaintext, WorkFactor);

    public bool Verify(string plaintext, string hash)
        => BCrypt.Net.BCrypt.Verify(plaintext, hash);
}
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — los 5 nuevos tests verdes.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Interfaces/IPasswordHasher.cs \
        src/StockApp.Infrastructure/Auth/BcryptPasswordHasher.cs \
        tests/StockApp.Infrastructure.Tests/Auth/BcryptPasswordHasherTests.cs
git commit -m "feat(auth): interfaz IPasswordHasher e implementación BCrypt"
```

---

## Task 2: Sesión en memoria

**Files:**
- Create: `src/StockApp.Application/Interfaces/ICurrentSession.cs`
- Create: `src/StockApp.Infrastructure/Auth/InMemorySession.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Infrastructure.Tests/Auth/InMemorySessionTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Auth;
using Xunit;

namespace StockApp.Infrastructure.Tests.Auth;

public class InMemorySessionTests
{
    private readonly InMemorySession _session = new();

    private static Usuario UsuarioAdmin() => new()
    {
        Id = 1,
        NombreUsuario = "admin",
        HashContrasena = "hash",
        Rol = RolUsuario.Admin,
        Activo = true,
        FechaAlta = DateTime.UtcNow
    };

    [Fact]
    public void SesionNueva_NoEstaAutenticada()
    {
        Assert.False(_session.EstaAutenticado);
        Assert.Null(_session.UsuarioActual);
    }

    [Fact]
    public void Login_EstableceSesion_Y_EstaAutenticadoEsTrue()
    {
        _session.IniciarSesion(UsuarioAdmin());

        Assert.True(_session.EstaAutenticado);
        Assert.NotNull(_session.UsuarioActual);
        Assert.Equal("admin", _session.UsuarioActual!.NombreUsuario);
    }

    [Fact]
    public void Logout_LimpiaSesion()
    {
        _session.IniciarSesion(UsuarioAdmin());
        _session.CerrarSesion();

        Assert.False(_session.EstaAutenticado);
        Assert.Null(_session.UsuarioActual);
    }

    [Fact]
    public void CambioDeUsuario_SinCerrarApp_EsposibleHaciendoNuevoLogin()
    {
        var operador = new Usuario
        {
            Id = 2,
            NombreUsuario = "operador1",
            HashContrasena = "hash",
            Rol = RolUsuario.Operador,
            Activo = true,
            FechaAlta = DateTime.UtcNow
        };

        _session.IniciarSesion(UsuarioAdmin());
        _session.CerrarSesion();
        _session.IniciarSesion(operador);

        Assert.Equal("operador1", _session.UsuarioActual!.NombreUsuario);
        Assert.Equal(RolUsuario.Operador, _session.UsuarioActual.Rol);
    }

    [Fact]
    public void RolActual_SinSesion_EsNull()
    {
        Assert.Null(_session.RolActual);
    }

    [Fact]
    public void RolActual_ConSesion_RetornaRolDelUsuario()
    {
        _session.IniciarSesion(UsuarioAdmin());

        Assert.Equal(RolUsuario.Admin, _session.RolActual);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación.

### Implementación

- [ ] **Step 3: Crear la interfaz de sesión**

Create `src/StockApp.Application/Interfaces/ICurrentSession.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Estado de la sesión actual en memoria. Se registra como singleton en el contenedor DI.
/// No persiste entre reinicios de la app (eso es intencional: cada arranque requiere login).
/// </summary>
public interface ICurrentSession
{
    /// <summary>true si hay un usuario logueado.</summary>
    bool EstaAutenticado { get; }

    /// <summary>El usuario actual, o null si no hay sesión.</summary>
    Usuario? UsuarioActual { get; }

    /// <summary>Atajo: rol del usuario actual, o null si no hay sesión.</summary>
    RolUsuario? RolActual { get; }

    /// <summary>Establece <paramref name="usuario"/> como usuario activo de la sesión.</summary>
    void IniciarSesion(Usuario usuario);

    /// <summary>Limpia la sesión. La app sigue corriendo; es necesario loguearse de nuevo.</summary>
    void CerrarSesion();
}
```

- [ ] **Step 4: Crear la implementación en memoria**

Create `src/StockApp.Infrastructure/Auth/InMemorySession.cs`:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Infrastructure.Auth;

/// <summary>
/// Singleton en memoria. Hilo-seguro con lock simple (single-PC, sin concurrencia real,
/// pero la UI de Avalonia puede acceder desde hilos distintos).
/// </summary>
public class InMemorySession : ICurrentSession
{
    private readonly object _lock = new();
    private Usuario? _usuarioActual;

    public bool EstaAutenticado { get { lock (_lock) return _usuarioActual != null; } }

    public Usuario? UsuarioActual { get { lock (_lock) return _usuarioActual; } }

    public RolUsuario? RolActual { get { lock (_lock) return _usuarioActual?.Rol; } }

    public void IniciarSesion(Usuario usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        lock (_lock) _usuarioActual = usuario;
    }

    public void CerrarSesion()
    {
        lock (_lock) _usuarioActual = null;
    }
}
```

- [ ] **Step 5: Correr los tests**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — los 6 nuevos tests verdes.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Interfaces/ICurrentSession.cs \
        src/StockApp.Infrastructure/Auth/InMemorySession.cs \
        tests/StockApp.Infrastructure.Tests/Auth/InMemorySessionTests.cs
git commit -m "feat(auth): interfaz ICurrentSession e implementación InMemorySession"
```

---

## Task 3: Servicio de autenticación (Login / Logout)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IUsuarioRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/UsuarioRepository.cs`
- Create: `src/StockApp.Application/Auth/AuthService.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Application.Tests/Auth/AuthServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Auth;

public class AuthServiceTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static Usuario UsuarioActivo(RolUsuario rol = RolUsuario.Operador) => new()
    {
        Id = 1,
        NombreUsuario = "usuario1",
        HashContrasena = "$2a$12$hash_valido",   // simulado
        Rol = rol,
        Activo = true,
        FechaAlta = DateTime.UtcNow
    };

    private static (AuthService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuditLogger> auditMock)
        Crear(Usuario? usuarioEnBd = null, bool hashValido = true)
    {
        var repo = new Mock<IUsuarioRepository>();
        repo.Setup(r => r.BuscarPorNombreAsync("usuario1"))
            .ReturnsAsync(usuarioEnBd);

        var hasher = new Mock<IPasswordHasher>();
        hasher.Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
              .Returns(hashValido);

        var session = new Mock<ICurrentSession>();
        var audit   = new Mock<IAuditLogger>();

        var svc = new AuthService(repo.Object, hasher.Object, session.Object, audit.Object);
        return (svc, repo, hasher, session, audit);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Correcto_EstableceSesionYActualizaUltimoAcceso()
    {
        var usuario = UsuarioActivo();
        var (svc, repo, _, session, _) = Crear(usuario, hashValido: true);

        var resultado = await svc.LoginAsync("usuario1", "contrasena");

        Assert.True(resultado.Exitoso);
        session.Verify(s => s.IniciarSesion(usuario), Times.Once);
        repo.Verify(r => r.ActualizarUltimoAccesoAsync(usuario.Id, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task Login_ContrasenaIncorrecta_FallaYNoEstableceSesion()
    {
        var usuario = UsuarioActivo();
        var (svc, _, _, session, _) = Crear(usuario, hashValido: false);

        var resultado = await svc.LoginAsync("usuario1", "mala");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.ContrasenaInvalida, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Login_UsuarioInexistente_FallaConErrorAdecuado()
    {
        var (svc, _, _, session, _) = Crear(usuarioEnBd: null);

        var resultado = await svc.LoginAsync("noexiste", "cualquiera");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.UsuarioNoEncontrado, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Login_UsuarioInactivo_FallaConErrorEspecifico()
    {
        var usuario = UsuarioActivo();
        usuario.Activo = false;
        var (svc, _, _, session, _) = Crear(usuario, hashValido: true);

        var resultado = await svc.LoginAsync("usuario1", "contrasena");

        Assert.False(resultado.Exitoso);
        Assert.Equal(LoginError.UsuarioInactivo, resultado.Error);
        session.Verify(s => s.IniciarSesion(It.IsAny<Usuario>()), Times.Never);
    }

    [Fact]
    public async Task Logout_LlamaCerrarSesion()
    {
        var (svc, _, _, session, _) = Crear();

        await svc.LogoutAsync();

        session.Verify(s => s.CerrarSesion(), Times.Once);
    }
}
```

> **Nota:** este proyecto necesita el paquete `Moq` en `StockApp.Application.Tests`.
> ```bash
> dotnet add tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj package Moq
> ```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: error de compilación.

### Implementación

- [ ] **Step 3: Crear la interfaz del repositorio de usuarios**

Create `src/StockApp.Application/Interfaces/IUsuarioRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IUsuarioRepository
{
    Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario);
    Task<Usuario?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Usuario>> ListarTodosAsync();
    Task<bool> ExisteAlgunUsuarioAsync();
    Task<int> AgregarAsync(Usuario usuario);
    Task ActualizarAsync(Usuario usuario);
    Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso);
}
```

- [ ] **Step 4: Crear el repositorio (Infrastructure)**

Create `src/StockApp.Infrastructure/Repositories/UsuarioRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly AppDbContext _ctx;

    public UsuarioRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Usuario?> BuscarPorNombreAsync(string nombreUsuario)
        => _ctx.Usuarios.FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario);

    public Task<Usuario?> ObtenerPorIdAsync(int id)
        => _ctx.Usuarios.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<Usuario>> ListarTodosAsync()
        => await _ctx.Usuarios.OrderBy(u => u.NombreUsuario).ToListAsync();

    public Task<bool> ExisteAlgunUsuarioAsync()
        => _ctx.Usuarios.AnyAsync();

    public async Task<int> AgregarAsync(Usuario usuario)
    {
        _ctx.Usuarios.Add(usuario);
        await _ctx.SaveChangesAsync();
        return usuario.Id;
    }

    public Task ActualizarAsync(Usuario usuario)
    {
        _ctx.Usuarios.Update(usuario);
        return _ctx.SaveChangesAsync();
    }

    public async Task ActualizarUltimoAccesoAsync(int usuarioId, DateTime fechaAcceso)
    {
        var usuario = await ObtenerPorIdAsync(usuarioId);
        if (usuario is null) return;
        usuario.UltimoAcceso = fechaAcceso;
        await _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Crear el AuthService con el tipo resultado**

Create `src/StockApp.Application/Auth/AuthService.cs`:

```csharp
using StockApp.Application.Interfaces;

namespace StockApp.Application.Auth;

public enum LoginError { UsuarioNoEncontrado, ContrasenaInvalida, UsuarioInactivo }

public record LoginResult(bool Exitoso, LoginError? Error = null)
{
    public static LoginResult Ok()             => new(true);
    public static LoginResult Fallo(LoginError e) => new(false, e);
}

public class AuthService
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher    _hasher;
    private readonly ICurrentSession    _session;
    private readonly IAuditLogger       _audit;

    public AuthService(
        IUsuarioRepository repo,
        IPasswordHasher    hasher,
        ICurrentSession    session,
        IAuditLogger       audit)
    {
        _repo    = repo;
        _hasher  = hasher;
        _session = session;
        _audit   = audit;
    }

    public async Task<LoginResult> LoginAsync(string nombreUsuario, string contrasena)
    {
        var usuario = await _repo.BuscarPorNombreAsync(nombreUsuario);

        if (usuario is null)
            return LoginResult.Fallo(LoginError.UsuarioNoEncontrado);

        if (!usuario.Activo)
            return LoginResult.Fallo(LoginError.UsuarioInactivo);

        if (!_hasher.Verify(contrasena, usuario.HashContrasena))
            return LoginResult.Fallo(LoginError.ContrasenaInvalida);

        _session.IniciarSesion(usuario);
        await _repo.ActualizarUltimoAccesoAsync(usuario.Id, DateTime.UtcNow);

        return LoginResult.Ok();
    }

    public Task LogoutAsync()
    {
        _session.CerrarSesion();
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Correr los tests**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: `Passed!` — los 5 tests de `AuthServiceTests` verdes.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Interfaces/IUsuarioRepository.cs \
        src/StockApp.Application/Auth/AuthService.cs \
        src/StockApp.Infrastructure/Repositories/UsuarioRepository.cs \
        tests/StockApp.Application.Tests/Auth/AuthServiceTests.cs
git commit -m "feat(auth): AuthService, IUsuarioRepository y UsuarioRepository"
```

---

## Task 4: Asistente de primer arranque (seed del Admin inicial)

**Files:**
- Create: `src/StockApp.Application/Auth/PrimerArranqueService.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Application.Tests/Auth/PrimerArranqueServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Auth;

public class PrimerArranqueServiceTests
{
    private static (PrimerArranqueService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock)
        Crear(bool hayUsuarios)
    {
        var repo   = new Mock<IUsuarioRepository>();
        var hasher = new Mock<IPasswordHasher>();

        repo.Setup(r => r.ExisteAlgunUsuarioAsync()).ReturnsAsync(hayUsuarios);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        var svc = new PrimerArranqueService(repo.Object, hasher.Object);
        return (svc, repo, hasher);
    }

    [Fact]
    public async Task RequiereCrearAdmin_BdVacia_RetornaTrue()
    {
        var (svc, _, _) = Crear(hayUsuarios: false);

        Assert.True(await svc.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task RequiereCrearAdmin_HayUsuarios_RetornaFalse()
    {
        var (svc, _, _) = Crear(hayUsuarios: true);

        Assert.False(await svc.RequiereCrearAdminAsync());
    }

    [Fact]
    public async Task CrearAdminInicial_GuardaConHashYRolAdmin()
    {
        var (svc, repo, hasher) = Crear(hayUsuarios: false);

        await svc.CrearAdminInicialAsync("adminRoot", "contrasenaSegura");

        hasher.Verify(h => h.Hash("contrasenaSegura"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "adminRoot" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Admin &&
            u.Activo == true
        )), Times.Once);
    }

    [Fact]
    public async Task CrearAdminInicial_SiYaHayUsuarios_LanzaExcepcion()
    {
        var (svc, _, _) = Crear(hayUsuarios: true);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.CrearAdminInicialAsync("admin", "pwd"));
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: error de compilación.

### Implementación

- [ ] **Step 3: Crear el PrimerArranqueService**

Create `src/StockApp.Application/Auth/PrimerArranqueService.cs`:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// Detecta si la BD no tiene ningún usuario y, en ese caso, orquesta la creación
/// del primer Admin. No define una contraseña por defecto: la elige el usuario en ese momento.
/// </summary>
public class PrimerArranqueService
{
    private readonly IUsuarioRepository _repo;
    private readonly IPasswordHasher    _hasher;

    public PrimerArranqueService(IUsuarioRepository repo, IPasswordHasher hasher)
    {
        _repo   = repo;
        _hasher = hasher;
    }

    /// <summary>true si no hay ningún usuario en la BD.</summary>
    public async Task<bool> RequiereCrearAdminAsync()
        => !await _repo.ExisteAlgunUsuarioAsync();

    /// <summary>
    /// Crea el primer usuario Admin con la contraseña provista (hasheada).
    /// Lanza <see cref="InvalidOperationException"/> si ya existe al menos un usuario.
    /// </summary>
    public async Task CrearAdminInicialAsync(string nombreUsuario, string contrasenaPlana)
    {
        if (!await RequiereCrearAdminAsync())
            throw new InvalidOperationException(
                "No se puede crear el Admin inicial: ya existen usuarios en la base de datos.");

        var admin = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            HashContrasena = _hasher.Hash(contrasenaPlana),
            Rol            = RolUsuario.Admin,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        await _repo.AgregarAsync(admin);
    }
}
```

- [ ] **Step 4: Correr los tests**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: `Passed!` — los 4 tests verdes.

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Application/Auth/PrimerArranqueService.cs \
        tests/StockApp.Application.Tests/Auth/PrimerArranqueServiceTests.cs
git commit -m "feat(auth): asistente de primer arranque (seed del Admin inicial)"
```

---

## Task 5: Autorización en la capa Application

**Diseño del mecanismo:**

El guard de autorización se implementa como un servicio `IAuthorizationService` con un método
`Verificar(RolUsuario? rolActual, string accion)` que lanza `UnauthorizedAccessException` si el
rol no tiene permiso. Los servicios de Application lo llaman al inicio de cada método que requiere
un rol mínimo. Las constantes de acciones viven en la clase `Permisos` (no strings sueltos en el
código).

Este enfoque es explícito, testeable, y no requiere atributos ni reflexión. Es apropiado para
la escala de este sistema (dos roles, un puñado de acciones). Si en el futuro crece la
complejidad se puede migrar a políticas más declarativas sin cambiar la interfaz.

**Files:**
- Create: `src/StockApp.Application/Authorization/Permisos.cs`
- Create: `src/StockApp.Application/Authorization/IAuthorizationService.cs`
- Create: `src/StockApp.Application/Authorization/AuthorizationService.cs`

### Tests primero

- [ ] **Step 1: Escribir tests que fallan**

Create `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Authorization;

public class AuthorizationServiceTests
{
    private readonly AuthorizationService _svc = new();

    // ── Admin puede todo ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    [InlineData(Permisos.GestionarCatalogo)]
    [InlineData(Permisos.RegistrarMovimientos)]
    public void Admin_PuedeEjecutarCualquierAccion(string accion)
    {
        // No debe lanzar
        _svc.Verificar(RolUsuario.Admin, accion);
    }

    // ── Operador: acciones permitidas ────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarCatalogo)]
    [InlineData(Permisos.RegistrarMovimientos)]
    [InlineData(Permisos.RecalcularStock)]
    public void Operador_PuedeEjecutarAccionesOperativas(string accion)
    {
        // No debe lanzar
        _svc.Verificar(RolUsuario.Operador, accion);
    }

    // ── Operador: acciones denegadas ─────────────────────────────────────────

    [Theory]
    [InlineData(Permisos.GestionarUsuarios)]
    [InlineData(Permisos.VerReportes)]
    public void Operador_NoPuedeEjecutarAccionesDeAdmin(string accion)
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(RolUsuario.Operador, accion));
    }

    // ── Sin sesión ────────────────────────────────────────────────────────────

    [Fact]
    public void SinSesion_CualquierAccionLanzaExcepcion()
    {
        Assert.Throws<UnauthorizedAccessException>(
            () => _svc.Verificar(null, Permisos.GestionarCatalogo));
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: error de compilación.

### Implementación

- [ ] **Step 3: Crear las constantes de permisos**

Create `src/StockApp.Application/Authorization/Permisos.cs`:

```csharp
namespace StockApp.Application.Authorization;

/// <summary>
/// Nombres canónicos de las acciones protegidas del sistema.
/// Todos los servicios de Application usan estas constantes al llamar a IAuthorizationService.
/// </summary>
public static class Permisos
{
    public const string GestionarUsuarios    = "usuarios.gestionar";
    public const string VerReportes          = "reportes.ver";
    public const string GestionarCatalogo    = "catalogo.gestionar";
    public const string RegistrarMovimientos = "movimientos.registrar";
    public const string RecalcularStock      = "stock.recalcular";
}
```

- [ ] **Step 4: Crear la interfaz de autorización**

Create `src/StockApp.Application/Authorization/IAuthorizationService.cs`:

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
}
```

- [ ] **Step 5: Crear la implementación del guard**

Create `src/StockApp.Application/Authorization/AuthorizationService.cs`:

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
    private static readonly HashSet<string> AccionesOperador =
    [
        Permisos.GestionarCatalogo,
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
}
```

- [ ] **Step 6: Correr los tests**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: `Passed!` — los 9 tests de autorización verdes.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Authorization/ \
        tests/StockApp.Application.Tests/Authorization/
git commit -m "feat(application): guard de autorización por rol (Permisos + AuthorizationService)"
```

---

## Task 6: ABM de usuarios (solo Admin)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IAuditLogger.cs`
- Create: `src/StockApp.Infrastructure/Services/AuditService.cs`
- Create: `src/StockApp.Application/Auth/UsuarioService.cs`

### Tests primero

- [ ] **Step 1: Escribir tests del UsuarioService que fallan**

Create `tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Auth;

public class UsuarioServiceTests
{
    private static (UsuarioService service,
                    Mock<IUsuarioRepository> repoMock,
                    Mock<IPasswordHasher> hasherMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rolSesion = RolUsuario.Admin)
    {
        var repo    = new Mock<IUsuarioRepository>();
        var hasher  = new Mock<IPasswordHasher>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rolSesion);
        hasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("$2a$12$hashed");

        // Admin: Verificar no lanza
        if (rolSesion == RolUsuario.Admin)
            auth.Setup(a => a.Verificar(RolUsuario.Admin, It.IsAny<string>()));
        else
            auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarUsuarios))
                .Throws<UnauthorizedAccessException>();

        var svc = new UsuarioService(repo.Object, hasher.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, hasher, session, auth, audit);
    }

    [Fact]
    public async Task AltaUsuario_Admin_CreaConHashYEventoAuditoria()
    {
        var (svc, repo, hasher, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });

        await svc.AltaUsuarioAsync("operador2", "Nombre Completo", "pwd123", RolUsuario.Operador);

        hasher.Verify(h => h.Hash("pwd123"), Times.Once);
        repo.Verify(r => r.AgregarAsync(It.Is<Usuario>(u =>
            u.NombreUsuario == "operador2" &&
            u.HashContrasena == "$2a$12$hashed" &&
            u.Rol == RolUsuario.Operador &&
            u.Activo == true
        )), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaUsuario,
            "Usuario", It.IsAny<int>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogica_Admin_PoneActivoFalseYNoEliminaRegistro()
    {
        var usuario = new Usuario
        {
            Id = 5, NombreUsuario = "operador1", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(usuario);

        await svc.BajaLogicaAsync(5);

        // Se actualizó, NO se eliminó
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaUsuario,
            "Usuario", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AltaUsuario_Operador_LanzaUnauthorized()
    {
        var (svc, _, _, _, _, _) = Crear(rolSesion: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.AltaUsuarioAsync("x", null, "pwd", RolUsuario.Operador));
    }

    [Fact]
    public async Task CambioRol_Admin_ActualizaYRegistraAuditoria()
    {
        var usuario = new Usuario
        {
            Id = 3, NombreUsuario = "alguien", HashContrasena = "h",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, _, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(usuario);

        await svc.CambiarRolAsync(3, RolUsuario.Admin);

        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u => u.Rol == RolUsuario.Admin)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioRol, "Usuario", 3, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambioContrasena_Admin_HashYRegistraAuditoria()
    {
        var usuario = new Usuario
        {
            Id = 4, NombreUsuario = "alguien", HashContrasena = "hash_viejo",
            Rol = RolUsuario.Operador, Activo = true, FechaAlta = DateTime.UtcNow
        };
        var (svc, repo, hasher, session, _, audit) = Crear();
        session.Setup(s => s.UsuarioActual).Returns(new Usuario
            { Id = 1, NombreUsuario = "admin", Rol = RolUsuario.Admin, HashContrasena = "h", FechaAlta = DateTime.UtcNow });
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(usuario);

        await svc.CambiarContrasenaAsync(4, "nuevaContrasena");

        hasher.Verify(h => h.Hash("nuevaContrasena"), Times.Once);
        repo.Verify(r => r.ActualizarAsync(It.Is<Usuario>(u =>
            u.HashContrasena == "$2a$12$hashed")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.CambioContrasena, "Usuario", 4, It.IsAny<string>()), Times.Once);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: error de compilación.

### Implementación

- [ ] **Step 3: Crear la interfaz IAuditLogger**

Create `src/StockApp.Application/Interfaces/IAuditLogger.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Abstracción para registrar eventos de auditoría en <c>LogAuditoria</c>.
/// La implementación concreta escribe a la BD vía EF Core.
/// </summary>
public interface IAuditLogger
{
    /// <summary>Registra un evento de auditoría de forma asincrónica.</summary>
    Task RegistrarAsync(
        int usuarioId,
        AccionAuditada accion,
        string entidad,
        int entidadId,
        string detalle);
}
```

- [ ] **Step 4: Crear AuditService (Infrastructure)**

Create `src/StockApp.Infrastructure/Services/AuditService.cs`:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Services;

public class AuditService : IAuditLogger
{
    private readonly AppDbContext _ctx;

    public AuditService(AppDbContext ctx) => _ctx = ctx;

    public async Task RegistrarAsync(
        int usuarioId, AccionAuditada accion, string entidad, int entidadId, string detalle)
    {
        _ctx.LogsAuditoria.Add(new LogAuditoria
        {
            UsuarioId  = usuarioId,
            Fecha      = DateTime.UtcNow,
            Accion     = accion,
            Entidad    = entidad,
            EntidadId  = entidadId,
            Detalle    = detalle
        });
        await _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Crear UsuarioService**

Create `src/StockApp.Application/Auth/UsuarioService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Auth;

/// <summary>
/// ABM de usuarios. Solo para Admin: todas las operaciones verifican autorización
/// antes de ejecutar. Nunca borra físicamente; usa baja lógica.
/// </summary>
public class UsuarioService
{
    private readonly IUsuarioRepository   _repo;
    private readonly IPasswordHasher      _hasher;
    private readonly ICurrentSession      _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger         _audit;

    public UsuarioService(
        IUsuarioRepository    repo,
        IPasswordHasher       hasher,
        ICurrentSession       session,
        IAuthorizationService auth,
        IAuditLogger          audit)
    {
        _repo    = repo;
        _hasher  = hasher;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task AltaUsuarioAsync(
        string nombreUsuario, string? nombreCompleto,
        string contrasenaPlan, RolUsuario rol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var nuevo = new Usuario
        {
            NombreUsuario  = nombreUsuario,
            NombreCompleto = nombreCompleto,
            HashContrasena = _hasher.Hash(contrasenaPlan),
            Rol            = rol,
            Activo         = true,
            FechaAlta      = DateTime.UtcNow
        };

        var id = await _repo.AgregarAsync(nuevo);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaUsuario,
            "Usuario", id,
            $"Alta de '{nombreUsuario}' con rol {rol}");
    }

    public async Task BajaLogicaAsync(int usuarioId)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        usuario.Activo = false;
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaUsuario,
            "Usuario", usuarioId,
            $"Baja lógica de '{usuario.NombreUsuario}'");
    }

    public async Task CambiarRolAsync(int usuarioId, RolUsuario nuevoRol)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        var rolAnterior = usuario.Rol;
        usuario.Rol = nuevoRol;
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.CambioRol,
            "Usuario", usuarioId,
            $"Rol: {rolAnterior} → {nuevoRol}");
    }

    public async Task CambiarContrasenaAsync(int usuarioId, string nuevaContrasenaPlan)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarUsuarios);

        var usuario = await _repo.ObtenerPorIdAsync(usuarioId)
            ?? throw new KeyNotFoundException($"Usuario {usuarioId} no encontrado.");

        usuario.HashContrasena = _hasher.Hash(nuevaContrasenaPlan);
        await _repo.ActualizarAsync(usuario);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.CambioContrasena,
            "Usuario", usuarioId,
            "Cambio de contraseña por Admin");
    }
}
```

- [ ] **Step 6: Correr los tests**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: `Passed!` — los 5 tests de `UsuarioServiceTests` verdes.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Interfaces/IAuditLogger.cs \
        src/StockApp.Application/Auth/UsuarioService.cs \
        src/StockApp.Infrastructure/Services/AuditService.cs \
        tests/StockApp.Application.Tests/Auth/UsuarioServiceTests.cs
git commit -m "feat(auth): UsuarioService ABM, IAuditLogger y AuditService"
```

---

## Task 7: Tests de integración — Repositorio de usuarios y Auditoría

**Files:**
- Create: `tests/StockApp.Infrastructure.Tests/Repositories/UsuarioRepositoryTests.cs`
- Create: `tests/StockApp.Infrastructure.Tests/Services/AuditServiceTests.cs`

Estos tests verifican que la implementación de Infrastructure interactúa correctamente
con SQLite en memoria (EF Core + `EnsureCreated`).

- [ ] **Step 1: Escribir tests de integración que fallan**

Create `tests/StockApp.Infrastructure.Tests/Repositories/UsuarioRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class UsuarioRepositoryTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly UsuarioRepository _repo;

    public UsuarioRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();
        _repo = new UsuarioRepository(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    private static Usuario NuevoUsuario(string nombre) => new()
    {
        NombreUsuario  = nombre,
        HashContrasena = "$2a$12$hash",
        Rol            = RolUsuario.Operador,
        Activo         = true,
        FechaAlta      = DateTime.UtcNow
    };

    [Fact]
    public async Task AgregarAsync_Y_BuscarPorNombre_Roundtrip()
    {
        var usuario = NuevoUsuario("jperez");
        await _repo.AgregarAsync(usuario);

        var encontrado = await _repo.BuscarPorNombreAsync("jperez");

        Assert.NotNull(encontrado);
        Assert.Equal("jperez", encontrado!.NombreUsuario);
    }

    [Fact]
    public async Task ExisteAlgunUsuarioAsync_BdVacia_RetornaFalse()
    {
        Assert.False(await _repo.ExisteAlgunUsuarioAsync());
    }

    [Fact]
    public async Task ExisteAlgunUsuarioAsync_ConUnUsuario_RetornaTrue()
    {
        await _repo.AgregarAsync(NuevoUsuario("admin"));

        Assert.True(await _repo.ExisteAlgunUsuarioAsync());
    }

    [Fact]
    public async Task ActualizarUltimoAccesoAsync_ActualizaFecha()
    {
        var usuario = NuevoUsuario("test");
        await _repo.AgregarAsync(usuario);

        var ahora = DateTime.UtcNow;
        await _repo.ActualizarUltimoAccesoAsync(usuario.Id, ahora);

        var actualizado = await _repo.ObtenerPorIdAsync(usuario.Id);
        Assert.NotNull(actualizado!.UltimoAcceso);
    }
}
```

Create `tests/StockApp.Infrastructure.Tests/Services/AuditServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Services;
using Xunit;

namespace StockApp.Infrastructure.Tests.Services;

public class AuditServiceTests : IDisposable
{
    private readonly AppDbContext _ctx;
    private readonly AuditService _svc;

    public AuditServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _ctx = new AppDbContext(options);
        _ctx.Database.OpenConnection();
        _ctx.Database.EnsureCreated();

        // Usuario de referencia para la FK
        _ctx.Usuarios.Add(new Usuario
        {
            Id = 1, NombreUsuario = "admin", HashContrasena = "h",
            Rol = RolUsuario.Admin, Activo = true, FechaAlta = DateTime.UtcNow
        });
        _ctx.SaveChanges();

        _svc = new AuditService(_ctx);
    }

    public void Dispose() => _ctx.Dispose();

    [Fact]
    public async Task RegistrarAsync_AltaUsuario_InsertaLogEnBd()
    {
        await _svc.RegistrarAsync(1, AccionAuditada.AltaUsuario, "Usuario", 99, "Alta de 'x'");

        var log = await _ctx.LogsAuditoria.FirstOrDefaultAsync();
        Assert.NotNull(log);
        Assert.Equal(AccionAuditada.AltaUsuario, log!.Accion);
        Assert.Equal("Usuario", log.Entidad);
        Assert.Equal(99, log.EntidadId);
    }

    [Fact]
    public async Task RegistrarAsync_BajaUsuario_InsertaLogConDetalle()
    {
        await _svc.RegistrarAsync(1, AccionAuditada.BajaUsuario, "Usuario", 5, "Baja lógica de 'jperez'");

        var log = await _ctx.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.BajaUsuario);
        Assert.NotNull(log);
        Assert.Contains("jperez", log!.Detalle);
    }

    [Fact]
    public async Task RegistrarAsync_CambioRol_GuardaFechaActual()
    {
        var antes = DateTime.UtcNow.AddSeconds(-1);
        await _svc.RegistrarAsync(1, AccionAuditada.CambioRol, "Usuario", 2, "Rol: Operador → Admin");

        var log = await _ctx.LogsAuditoria
            .FirstOrDefaultAsync(l => l.Accion == AccionAuditada.CambioRol);
        Assert.NotNull(log);
        Assert.True(log!.Fecha >= antes);
    }
}
```

- [ ] **Step 2: Verificar que los tests fallan (compilación falla)**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: error de compilación.

- [ ] **Step 3: Correr los tests una vez creados los archivos de implementación (Task 6)**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: `Passed!` — los 7 nuevos tests de integración verdes.

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Infrastructure.Tests/Repositories/UsuarioRepositoryTests.cs \
        tests/StockApp.Infrastructure.Tests/Services/AuditServiceTests.cs
git commit -m "test(infrastructure): tests de integración para UsuarioRepository y AuditService"
```

---

## Task 8: Registro de DI en Presentation

**Files:**
- Modify: `src/StockApp.Presentation/App.axaml.cs`

- [ ] **Step 1: Agregar paquetes necesarios al proyecto Infrastructure**

```bash
# BCrypt ya se instaló en Task 1
# Verificar referencia de Application desde Infrastructure (si no existe):
dotnet add src/StockApp.Infrastructure/StockApp.Infrastructure.csproj \
  reference src/StockApp.Application/StockApp.Application.csproj
```

- [ ] **Step 2: Registrar los servicios del Inc 3 en el contenedor DI**

Edit `src/StockApp.Presentation/App.axaml.cs` para agregar:

```csharp
// ── Auth ──────────────────────────────────────────────────────────────────
services.AddSingleton<ICurrentSession, InMemorySession>();
services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
services.AddTransient<IUsuarioRepository, UsuarioRepository>();
services.AddTransient<IAuditLogger, AuditService>();
services.AddTransient<IAuthorizationService, AuthorizationService>();
services.AddTransient<AuthService>();
services.AddTransient<PrimerArranqueService>();
services.AddTransient<UsuarioService>();
```

- [ ] **Step 3: Cablear el flujo de arranque**

En `OnFrameworkInitializationCompleted()`, después de `DatabaseInitializer.InicializarAsync()`:

```csharp
// Primer arranque: si no hay usuarios, mostrar asistente de creación de Admin
var primerArranque = services.GetRequiredService<PrimerArranqueService>();
if (await primerArranque.RequiereCrearAdminAsync())
{
    // Navegar a la vista de primer arranque (AsistentePrimerArranqueWindow)
    // La vista llama a primerArranque.CrearAdminInicialAsync() con los datos ingresados
}
else
{
    // Navegar a la vista de login (LoginWindow)
}
```

> Nota: Las ventanas de Avalonia (`LoginWindow`, `AsistentePrimerArranqueWindow`) son
> integración liviana. La lógica de validación y creación ya está testeable en
> `AuthService` y `PrimerArranqueService`. Las VMs correspondientes se crean en este
> incremento como capa fina; los tests de MVVM son opcionales (la cobertura real ya está
> en Application).

- [ ] **Step 4: Verificar que la solución compila**

Run: `dotnet build`
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Correr toda la suite**

Run: `dotnet test`
Expected: `Passed!` — todos los tests verdes, sin regresiones.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/App.axaml.cs
git commit -m "feat(presentation): registro de DI para auth, sesión y autorización (Inc 3)"
```

---

## Self-Review (cobertura del incremento)

- ✅ `IPasswordHasher` / `BcryptPasswordHasher`: hashing con sal; 5 tests (Task 1).
- ✅ `ICurrentSession` / `InMemorySession`: sesión en memoria thread-safe; 6 tests (Task 2).
- ✅ `AuthService`: login (correcto / contraseña inválida / usuario inexistente / usuario inactivo) + logout; 5 tests (Task 3).
- ✅ `PrimerArranqueService`: BD vacía requiere Admin; con usuarios no dispara; crea con hash correcto; 4 tests (Task 4).
- ✅ `AuthorizationService`: Admin puede todo; Operador solo operativo; sin sesión deniega todo; 9 tests (Task 5).
- ✅ `UsuarioService`: alta con hash + auditoría; baja lógica (no física) + auditoría; cambio de rol + auditoría; cambio de contraseña + auditoría; Operador rechazado; 5 tests (Task 6).
- ✅ `UsuarioRepository` + `AuditService`: tests de integración contra SQLite en memoria; 7 tests (Task 7).
- ✅ DI cableado en `App.axaml.cs` con flujo de primer arranque vs. login (Task 8).

---

## Definition of Done

El Incremento 3 se considera completo cuando:

1. `dotnet test` pasa con **0 failures** en los cuatro proyectos de test.
2. **Login funcional**: un usuario activo con contraseña correcta puede loguearse; uno inactivo o con mala contraseña recibe el error correspondiente.
3. **Autorización efectiva en Application**: un Operador que intente ejecutar `GestionarUsuarios` o `VerReportes` recibe `UnauthorizedAccessException` antes de que el caso de uso se ejecute.
4. **Auditoría registrando**: toda alta, baja, cambio de rol o cambio de contraseña genera un `LogAuditoria` con `UsuarioId`, `Fecha`, `Accion`, `Entidad`, `EntidadId` y `Detalle` correctos.
5. **Primer arranque**: con BD vacía, la app detecta la ausencia de usuarios y presenta el asistente de creación del Admin inicial; con al menos un usuario, va directo al login.
6. **Baja lógica garantizada**: `BajaLogicaAsync` pone `Activo = false` y llama a `ActualizarAsync`; no existe ningún `Delete` en el flujo de usuarios.
7. `HashContrasena` nunca contiene la contraseña en texto plano en ningún test ni en la BD (verificado por el test `Hash_NoGuardaContrasenaEnTextoPlano`).

**Lo que este incremento NO hace (próximos incrementos):** catálogo de productos, movimientos de stock, reportes. Los hooks de auditoría para `CambioPrecio`, `AltaProducto` y `BajaProducto` se activarán en el Incremento 4, cuando exista el `ProductoService`.
