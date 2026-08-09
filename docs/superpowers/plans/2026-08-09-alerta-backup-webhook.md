# Canal de alerta ante fallo de backup — Plan de Implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Que un fallo de backup — y también el silencio prolongado del sistema — dispare una notificación externa configurable desde la aplicación, sin acceso al servidor.

**Architecture:** Una entidad `ConfiguracionAlertas` de fila única en Postgres guarda una URL de webhook. Un `NotificadorWebhook` (Infrastructure) postea a `{url}` cuando el backup sale bien (heartbeat) y a `{url}/fail` cuando falla. Apuntando esa URL a healthchecks.io se obtiene, con el mismo mecanismo, alerta de fallo y dead man's switch. Tres endpoints Admin-only permiten leer, guardar y **probar** la configuración desde la pantalla de Mantenimiento.

**Tech Stack:** .NET / C#, EF Core + Npgsql (Postgres), ASP.NET Core Minimal APIs, Avalonia + CommunityToolkit.Mvvm, xUnit, Moq (solo en Presentation.Tests), Testcontainers.

**Spec:** `docs/superpowers/specs/2026-08-09-alerta-backup-webhook-design.md`

## Global Constraints

- **Idioma**: identificadores, comentarios y mensajes de usuario en español, como todo el repo.
- **Commits**: conventional commits. **Nunca** agregar `Co-Authored-By` ni atribución a IA.
- **Tests**: xUnit con `Assert.*`. Fakes manuales escritos a mano en `Application.Tests`, `Api.Tests`, `ApiClient.Tests` e `Infrastructure.Tests`. **Moq solo en `StockApp.Presentation.Tests`**.
- **Permiso**: se reusa `Permisos.GestionarDiagnostico` (`"diagnostico.gestionar"`). **No** se agrega un permiso nuevo — evita tocar `Permisos.Todos` y `AuthorizationService.TienePermiso`.
- **El notificador nunca puede hacer fracasar un backup.** Doble red: `NotificadorWebhook` se traga toda excepción internamente, y además cada punto de enganche envuelve la llamada en try/catch.
- **Condición de fallo**: siempre `corrida.Resultado == ResultadoBackup.Fallida`. **Nunca** `MotivoFallo != null` — esa columna es de doble propósito y marca también corridas exitosas reconciliadas.
- **Migraciones**: `dotnet ef migrations add <Nombre> --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`. Para aplicar contra la base real hay que pasar `--connection` explícito (sin él, `AppDbContextFactory` apunta a `stockapp_design`, no a `stockapp`).
- **No hacer build manual** salvo lo que corran los tests.

## Corrección al spec: la fila sembrada NO se puede dar por garantizada

El spec dice que la migración siembra la fila `Id = 1` "de modo que el código nunca tenga que contemplar su ausencia". **Eso es incorrecto en el entorno de tests y así está corregido en este plan.**

Motivo: `ConfiguracionAlertas.ActualizadoPorUsuarioId` es FK a `Usuarios`. Tanto `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` como `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` hacen `TRUNCATE TABLE ... "Usuarios" ... RESTART IDENTITY CASCADE`. En PostgreSQL, `TRUNCATE ... CASCADE` **arrastra automáticamente toda tabla que referencie a la truncada**, así que la fila sembrada desaparece antes de cada test.

Decisión: **el repositorio es defensivo.**
- `ObtenerAsync()` devuelve una `ConfiguracionAlertas` por defecto (no persistida) si la fila no existe.
- `GuardarAsync()` hace upsert: inserta con `Id = 1` si no existe, actualiza si existe.

El seed en la migración se mantiene igual (sirve en producción y hace explícito el contrato de fila única), pero deja de ser un supuesto del que dependa el código. Consecuencia práctica: **no hay que agregar `"ConfiguracionAlertas"` a ninguna lista de TRUNCATE** — el CASCADE ya la limpia.

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Domain/Entities/ConfiguracionAlertas.cs` | Entidad de fila única |
| `src/StockApp.Application/Interfaces/IConfiguracionAlertasRepository.cs` | Contrato de persistencia |
| `src/StockApp.Application/Interfaces/INotificadorAlertas.cs` | Contrato de notificación |
| `src/StockApp.Application/Alertas/AlertasDtos.cs` | DTOs compartidos Api↔desktop |
| `src/StockApp.Application/Alertas/NotificadorAlertasNulo.cs` | Null Object (tests y desactivación) |
| `src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs` | Lectura/validación/guardado server-side |
| `src/StockApp.Application/Alertas/IConfiguracionAlertasService.cs` | Contrato del cliente desktop |
| `src/StockApp.Infrastructure/Repositories/ConfiguracionAlertasRepository.cs` | EF Core, upsert |
| `src/StockApp.Infrastructure/Notificaciones/NotificadorWebhook.cs` | HttpClient hacia el webhook |
| `src/StockApp.Api/Endpoints/ConfiguracionAlertasEndpoints.cs` | 3 endpoints + request records |
| `src/StockApp.ApiClient/ConfiguracionAlertasApiClient.cs` | Cliente HTTP del desktop |
| `src/StockApp.Presentation/.../MantenimientoViewModel.cs` | Sección Alertas (modificar) |
| `src/StockApp.Presentation/.../MantenimientoView.axaml` | Sección Alertas (modificar) |

---

### Task 1: Entidad, repositorio y migración

**Files:**
- Create: `src/StockApp.Domain/Entities/ConfiguracionAlertas.cs`
- Create: `src/StockApp.Application/Interfaces/IConfiguracionAlertasRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/ConfiguracionAlertasRepository.cs`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (DbSet + OnModelCreating)
- Create: migración EF `AgregaConfiguracionAlertas`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ConfiguracionAlertasRepositoryTests.cs`

**Interfaces:**
- Produces: `ConfiguracionAlertas` (props `Id`, `UrlWebhook`, `Habilitado`, `ActualizadoEn`, `ActualizadoPorUsuarioId`, `Usuario`); `IConfiguracionAlertasRepository.ObtenerAsync()` → `Task<ConfiguracionAlertas>`, `GuardarAsync(ConfiguracionAlertas)` → `Task`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/ConfiguracionAlertasRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class ConfiguracionAlertasRepositoryTests : PostgresRepositoryTestBase
{
    public ConfiguracionAlertasRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private ConfiguracionAlertasRepository Crear() => new(Context);

    [Fact]
    public async Task ObtenerAsync_SinFila_DevuelveConfiguracionPorDefectoDeshabilitada()
    {
        // El TRUNCATE ... CASCADE de la base de tests arrastra ConfiguracionAlertas (FK a
        // Usuarios), así que la fila sembrada por la migración NO está. El repositorio tiene
        // que tolerarlo: sin este comportamiento, todo el subsistema explota en tests.
        var repo = Crear();

        var cfg = await repo.ObtenerAsync();

        Assert.NotNull(cfg);
        Assert.Null(cfg.UrlWebhook);
        Assert.False(cfg.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_SinFilaPrevia_InsertaConId1YSePuedeReleer()
    {
        var repo = Crear();
        var cfg = await repo.ObtenerAsync();
        cfg.UrlWebhook = "https://hc-ping.com/abc";
        cfg.Habilitado = true;
        cfg.ActualizadoEn = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);

        await repo.GuardarAsync(cfg);

        var releida = await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).ObtenerAsync();
        Assert.Equal(1, releida.Id);
        Assert.Equal("https://hc-ping.com/abc", releida.UrlWebhook);
        Assert.True(releida.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_ConFilaPrevia_ActualizaEnLugarDeInsertarOtra()
    {
        var repo = Crear();
        var primera = await repo.ObtenerAsync();
        primera.UrlWebhook = "https://hc-ping.com/vieja";
        primera.Habilitado = true;
        await repo.GuardarAsync(primera);

        var segunda = await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).ObtenerAsync();
        segunda.UrlWebhook = "https://hc-ping.com/nueva";
        segunda.Habilitado = false;
        await new ConfiguracionAlertasRepository(Fixture.CrearContexto()).GuardarAsync(segunda);

        using var ctx = Fixture.CrearContexto();
        var todas = ctx.ConfiguracionesAlertas.ToList();
        Assert.Single(todas);
        Assert.Equal("https://hc-ping.com/nueva", todas[0].UrlWebhook);
        Assert.False(todas[0].Habilitado);
    }
}
```

- [ ] **Step 2: Correr el test para verificar que falla**

```bash
dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ConfiguracionAlertasRepositoryTests
```
Esperado: FALLA de compilación — `ConfiguracionAlertas`, `ConfiguracionAlertasRepository` y `ConfiguracionesAlertas` no existen.

- [ ] **Step 3: Crear la entidad**

`src/StockApp.Domain/Entities/ConfiguracionAlertas.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Configuración del canal de alerta de backups. Tabla de FILA ÚNICA (Id = 1): no hay
/// múltiples configuraciones, hay una sola instalación. Es la primera configuración del
/// sistema persistida en base — el resto vive en appsettings.json, inaccesible después de
/// la instalación (no hay acceso al servidor), y por eso esta tiene que estar en la base.
/// </summary>
public class ConfiguracionAlertas
{
    /// <summary>Siempre 1. La fila única se siembra en la migración.</summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// URL del webhook a pinguear (convención healthchecks.io: éxito a la URL, fallo a
    /// {url}/fail). Null = sin configurar. Se valida https en ServicioConfiguracionAlertas.
    /// </summary>
    public string? UrlWebhook { get; set; }

    /// <summary>Interruptor explícito: con URL cargada pero Habilitado = false, no se notifica.</summary>
    public bool Habilitado { get; set; }

    /// <summary>UTC. Cuándo se guardó por última vez.</summary>
    public DateTime ActualizadoEn { get; set; }

    /// <summary>
    /// Quién guardó la configuración por última vez. Null si nunca se tocó (fila sembrada).
    /// FK Restrict a Usuarios, mismo criterio que CorridaBackup.UsuarioId y NotaTarea.Usuario.
    /// </summary>
    public int? ActualizadoPorUsuarioId { get; set; }

    public Usuario? Usuario { get; set; }
}
```

- [ ] **Step 4: Crear el contrato del repositorio**

`src/StockApp.Application/Interfaces/IConfiguracionAlertasRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IConfiguracionAlertasRepository
{
    /// <summary>
    /// La fila única de configuración. Si no existe (la base de tests la borra por CASCADE al
    /// truncar Usuarios), devuelve una instancia por defecto NO persistida: Habilitado = false
    /// y UrlWebhook = null. Nunca devuelve null.
    /// </summary>
    Task<ConfiguracionAlertas> ObtenerAsync();

    /// <summary>Upsert de la fila única: inserta con Id = 1 si no existe, actualiza si existe.</summary>
    Task GuardarAsync(ConfiguracionAlertas configuracion);
}
```

- [ ] **Step 5: Agregar el DbSet y el mapeo en AppDbContext**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, junto al resto de los `DbSet<>`:

```csharp
    public DbSet<ConfiguracionAlertas> ConfiguracionesAlertas => Set<ConfiguracionAlertas>();
```

Y en `OnModelCreating`, justo después del bloque de `CorridaBackup`:

```csharp
        // ── Configuración del canal de alertas (fila única, Id = 1) ─────────────
        // Mismo patrón de FK que CorridaBackup.Usuario: nullable + Restrict. Null es un valor
        // legítimo (la fila sembrada por la migración nunca la tocó nadie), no un hueco de FK.
        modelBuilder.Entity<ConfiguracionAlertas>(e =>
        {
            e.Property(c => c.Id).ValueGeneratedNever(); // fila única: el Id lo fija el código, no la secuencia
            e.HasOne(c => c.Usuario).WithMany()
                .HasForeignKey(c => c.ActualizadoPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 6: Implementar el repositorio**

`src/StockApp.Infrastructure/Repositories/ConfiguracionAlertasRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class ConfiguracionAlertasRepository : IConfiguracionAlertasRepository
{
    private const int IdFilaUnica = 1;

    private readonly AppDbContext _ctx;

    public ConfiguracionAlertasRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<ConfiguracionAlertas> ObtenerAsync()
    {
        var fila = await _ctx.ConfiguracionesAlertas
            .FirstOrDefaultAsync(c => c.Id == IdFilaUnica);

        // Defensivo a propósito: en los tests, TRUNCATE "Usuarios" ... CASCADE arrastra esta
        // tabla y borra la fila sembrada por la migración. Devolver una instancia por defecto
        // en vez de null hace que todo el subsistema funcione igual, sembrado o no.
        return fila ?? new ConfiguracionAlertas { Id = IdFilaUnica, Habilitado = false };
    }

    public async Task GuardarAsync(ConfiguracionAlertas configuracion)
    {
        configuracion.Id = IdFilaUnica;

        var existente = await _ctx.ConfiguracionesAlertas
            .FirstOrDefaultAsync(c => c.Id == IdFilaUnica);

        if (existente is null)
        {
            _ctx.ConfiguracionesAlertas.Add(configuracion);
        }
        else
        {
            existente.UrlWebhook = configuracion.UrlWebhook;
            existente.Habilitado = configuracion.Habilitado;
            existente.ActualizadoEn = configuracion.ActualizadoEn;
            existente.ActualizadoPorUsuarioId = configuracion.ActualizadoPorUsuarioId;
        }

        await _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 7: Generar la migración**

```bash
dotnet ef migrations add AgregaConfiguracionAlertas --project src/StockApp.Infrastructure --startup-project src/StockApp.Api
```

Después, editar el `Up()` de la migración generada y agregar al final el seed de la fila única:

```csharp
            migrationBuilder.Sql(
                """
                INSERT INTO "ConfiguracionesAlertas" ("Id", "UrlWebhook", "Habilitado", "ActualizadoEn", "ActualizadoPorUsuarioId")
                VALUES (1, NULL, FALSE, NOW() AT TIME ZONE 'utc', NULL)
                ON CONFLICT ("Id") DO NOTHING;
                """);
```

Verificar que el nombre de tabla en el SQL coincida exactamente con el que generó EF (`ConfiguracionesAlertas`, plural, tomado del nombre del `DbSet`). Si EF generó otro nombre, usar ese.

- [ ] **Step 8: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ConfiguracionAlertasRepositoryTests
```
Esperado: 3 tests PASS.

- [ ] **Step 9: Correr la suite completa de Infrastructure para descartar regresiones**

```bash
dotnet test tests/StockApp.Infrastructure.Tests
```
Esperado: todo verde. Línea base conocida: 317/317 (más los 3 nuevos = 320). Nota: si estás en un worktree, los ~16 rojos de fixtures `.ods` son falsos negativos por fixtures no copiadas — no son tuyos.

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Domain/Entities/ConfiguracionAlertas.cs \
        src/StockApp.Application/Interfaces/IConfiguracionAlertasRepository.cs \
        src/StockApp.Infrastructure/Repositories/ConfiguracionAlertasRepository.cs \
        src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        src/StockApp.Infrastructure/Migrations/ \
        tests/StockApp.Infrastructure.Tests/Repositories/ConfiguracionAlertasRepositoryTests.cs
git commit -m "feat(alertas): entidad ConfiguracionAlertas de fila unica con repositorio upsert"
```

---

### Task 2: El notificador webhook

**Files:**
- Create: `src/StockApp.Application/Interfaces/INotificadorAlertas.cs`
- Create: `src/StockApp.Application/Alertas/NotificadorAlertasNulo.cs`
- Create: `src/StockApp.Infrastructure/Notificaciones/NotificadorWebhook.cs`
- Create: `tests/StockApp.Infrastructure.Tests/TestInfra/FakeHttpHandler.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Notificaciones/NotificadorWebhookTests.cs`

**Interfaces:**
- Consumes: `IConfiguracionAlertasRepository` (Task 1), `ConfiguracionAlertas` (Task 1).
- Produces: `INotificadorAlertas.NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)` → `Task`; `NotificadorAlertasNulo` (implementación no-op).

- [ ] **Step 1: Crear el handler HTTP falso**

`tests/StockApp.Infrastructure.Tests/TestInfra/FakeHttpHandler.cs` (copia del que ya existe en `StockApp.ApiClient.Tests` — no hay referencia entre proyectos de test, se replica el patrón):

```csharp
namespace StockApp.Infrastructure.Tests.TestInfra;

public sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

    public HttpRequestMessage? UltimaRequest { get; private set; }
    public string? UltimoBody { get; private set; }
    public int Llamadas { get; private set; }

    public FakeHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
        => _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Llamadas++;
        UltimaRequest = request;
        UltimoBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return _responder(request);
    }
}
```

- [ ] **Step 2: Escribir los tests que fallan**

`tests/StockApp.Infrastructure.Tests/Notificaciones/NotificadorWebhookTests.cs`:

```csharp
using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Notificaciones;
using StockApp.Infrastructure.Tests.TestInfra;
using Xunit;

namespace StockApp.Infrastructure.Tests.Notificaciones;

public class NotificadorWebhookTests
{
    private sealed class ConfiguracionAlertasRepositoryFake : IConfiguracionAlertasRepository
    {
        public ConfiguracionAlertas Configuracion { get; set; } = new();

        public Task<ConfiguracionAlertas> ObtenerAsync() => Task.FromResult(Configuracion);

        public Task GuardarAsync(ConfiguracionAlertas configuracion)
        {
            Configuracion = configuracion;
            return Task.CompletedTask;
        }
    }

    private static CorridaBackup Exitosa() => new()
    {
        IniciadaEn = DateTime.UtcNow.AddMinutes(-1),
        FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Exitosa,
        NombreArchivo = "backup_20260809_030000000.dump",
        TamanioBytes = 2048,
    };

    private static CorridaBackup Fallida(string motivo = "pg_dump: fallo simulado") => new()
    {
        IniciadaEn = DateTime.UtcNow.AddMinutes(-1),
        FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Fallida,
        MotivoFallo = motivo,
    };

    private static (NotificadorWebhook Sut, FakeHttpHandler Handler, ConfiguracionAlertasRepositoryFake Repo) Crear(
        string? url = "https://hc-ping.com/abc",
        bool habilitado = true,
        Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var handler = new FakeHttpHandler(responder ?? (_ => new HttpResponseMessage(HttpStatusCode.OK)));
        var repo = new ConfiguracionAlertasRepositoryFake
        {
            Configuracion = new ConfiguracionAlertas { UrlWebhook = url, Habilitado = habilitado },
        };
        var sut = new NotificadorWebhook(
            new HttpClient(handler), repo, NullLogger<NotificadorWebhook>.Instance);
        return (sut, handler, repo);
    }

    [Fact]
    public async Task CorridaExitosa_PosteaALaUrlSinSufijo()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Exitosa());

        Assert.Equal(1, handler.Llamadas);
        Assert.Equal(HttpMethod.Post, handler.UltimaRequest!.Method);
        Assert.Equal("https://hc-ping.com/abc", handler.UltimaRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task CorridaFallida_PosteaAlSufijoFailConElMotivoEnElBody()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Fallida("pg_dump: server closed the connection"));

        Assert.Equal("https://hc-ping.com/abc/fail", handler.UltimaRequest!.RequestUri!.ToString());
        Assert.Contains("pg_dump: server closed the connection", handler.UltimoBody);
    }

    [Fact]
    public async Task UrlConBarraFinal_NoDuplicaLaBarraEnElSufijoFail()
    {
        var (sut, handler, _) = Crear(url: "https://hc-ping.com/abc/");

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal("https://hc-ping.com/abc/fail", handler.UltimaRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Deshabilitado_NoHaceNingunaLlamada()
    {
        var (sut, handler, _) = Crear(habilitado: false);

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal(0, handler.Llamadas);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SinUrlConfigurada_NoHaceNingunaLlamada(string? url)
    {
        var (sut, handler, _) = Crear(url: url);

        await sut.NotificarCorridaBackupAsync(Fallida());

        Assert.Equal(0, handler.Llamadas);
    }

    [Fact]
    public async Task ElWebhookDevuelveError_NoPropagaExcepcion()
    {
        var (sut, _, _) = Crear(responder: _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var ex = await Record.ExceptionAsync(() => sut.NotificarCorridaBackupAsync(Fallida()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task ElWebhookEstaCaido_NoPropagaExcepcion()
    {
        var (sut, _, _) = Crear(responder: _ => throw new HttpRequestException("sin red"));

        var ex = await Record.ExceptionAsync(() => sut.NotificarCorridaBackupAsync(Fallida()));

        Assert.Null(ex);
    }

    [Fact]
    public async Task MotivoFalloMuyLargo_SeTruncaA2000Caracteres()
    {
        var (sut, handler, _) = Crear();

        await sut.NotificarCorridaBackupAsync(Fallida(new string('x', 5000)));

        Assert.Equal(2000, handler.UltimoBody!.Length);
    }
}
```

- [ ] **Step 3: Correr los tests para verificar que fallan**

```bash
dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~NotificadorWebhookTests
```
Esperado: FALLA de compilación — `NotificadorWebhook` no existe.

- [ ] **Step 4: Crear el contrato**

`src/StockApp.Application/Interfaces/INotificadorAlertas.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Canal de aviso hacia afuera del sistema ante el resultado de una corrida de backup.
/// CONTRATO INVIOLABLE: las implementaciones NUNCA propagan excepciones. El notificador es un
/// observador, no un participante — que se caiga la red no puede hacer fracasar un backup que
/// salió bien. Los puntos de enganche igual envuelven la llamada en try/catch (defensa en
/// profundidad), pero eso no exime a la implementación de cumplir el contrato.
/// </summary>
public interface INotificadorAlertas
{
    Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default);
}
```

- [ ] **Step 5: Crear el Null Object**

`src/StockApp.Application/Alertas/NotificadorAlertasNulo.cs`:

```csharp
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Alertas;

/// <summary>
/// Implementación no-op de <see cref="INotificadorAlertas"/>. Se usa en los tests que no
/// ejercitan la notificación, para no ensuciar cada construcción de SUT con un fake propio.
/// </summary>
public sealed class NotificadorAlertasNulo : INotificadorAlertas
{
    public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
        => Task.CompletedTask;
}
```

- [ ] **Step 6: Implementar el notificador**

`src/StockApp.Infrastructure/Notificaciones/NotificadorWebhook.cs`:

```csharp
using System.Text;
using Microsoft.Extensions.Logging;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Infrastructure.Notificaciones;

/// <summary>
/// Notifica el resultado de una corrida de backup posteando a una URL configurable, siguiendo
/// la convención de healthchecks.io: éxito a {url} (heartbeat), fallo a {url}/fail.
///
/// El heartbeat es lo que cubre el modo de falla realmente peligroso: si la API muere, no hay
/// backup, no hay error y no queda fila en la base — solo silencio, indistinguible de que todo
/// haya salido bien. Un servicio externo que espera el ping periódico es el único componente
/// que sigue vivo cuando el servidor no lo está.
/// </summary>
public sealed class NotificadorWebhook : INotificadorAlertas
{
    /// <summary>Healthchecks.io acepta cuerpos grandes, pero un stderr de pg_dump desbocado no
    /// aporta nada después de los primeros párrafos y sí puede hacer fallar el POST.</summary>
    private const int MaxCaracteresBody = 2000;

    private readonly HttpClient _http;
    private readonly IConfiguracionAlertasRepository _configuracion;
    private readonly ILogger<NotificadorWebhook> _logger;

    public NotificadorWebhook(
        HttpClient http,
        IConfiguracionAlertasRepository configuracion,
        ILogger<NotificadorWebhook> logger)
    {
        _http = http;
        _configuracion = configuracion;
        _logger = logger;
    }

    public async Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
    {
        try
        {
            // Se lee en cada llamada, no se cachea: el backup corre cada 12 horas (el costo es
            // irrelevante) y así un cambio de URL toma efecto sin reiniciar el servidor — que es
            // justamente lo que no se puede hacer después de la instalación.
            var cfg = await _configuracion.ObtenerAsync();

            if (!cfg.Habilitado || string.IsNullOrWhiteSpace(cfg.UrlWebhook))
                return;

            // Resultado == Fallida, NUNCA MotivoFallo != null: esa columna es de doble propósito
            // y también marca corridas EXITOSAS reconciliadas desde disco huérfano. Filtrar por
            // ella dispararía alertas falsas después de cada restauración.
            var fallo = corrida.Resultado == ResultadoBackup.Fallida;

            var url = ConstruirUrl(cfg.UrlWebhook!, fallo);
            var body = ConstruirBody(corrida, fallo);

            using var contenido = new StringContent(body, Encoding.UTF8, "text/plain");
            var respuesta = await _http.PostAsync(url, contenido, ct);

            if (!respuesta.IsSuccessStatusCode)
                _logger.LogWarning(
                    "El webhook de alertas respondió {Status} al notificar la corrida {CorridaId}.",
                    (int)respuesta.StatusCode, corrida.Id);
        }
        catch (Exception ex)
        {
            // Se traga TODO a propósito, incluida la cancelación: notificar es best-effort y no
            // puede hacer fracasar una corrida de backup que salió bien. El log es el rastro.
            _logger.LogWarning(ex, "No se pudo notificar el resultado del backup al webhook configurado.");
        }
    }

    private static string ConstruirUrl(string urlBase, bool fallo)
    {
        var limpia = urlBase.Trim().TrimEnd('/');
        return fallo ? limpia + "/fail" : limpia;
    }

    private static string ConstruirBody(CorridaBackup corrida, bool fallo)
    {
        var texto = fallo
            ? $"Backup FALLIDO el {corrida.FinalizadaEn:yyyy-MM-dd HH:mm} UTC. Motivo: {corrida.MotivoFallo}"
            : $"Backup OK el {corrida.FinalizadaEn:yyyy-MM-dd HH:mm} UTC. Archivo: {corrida.NombreArchivo} ({corrida.TamanioBytes} bytes)";

        return texto.Length > MaxCaracteresBody ? texto[..MaxCaracteresBody] : texto;
    }
}
```

- [ ] **Step 7: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~NotificadorWebhookTests
```
Esperado: 10 tests PASS (8 `[Fact]` + 3 casos del `[Theory]`, menos el que ya se contó — verificar que ninguno quede rojo).

Si `MotivoFalloMuyLargo_SeTruncaA2000Caracteres` falla porque el body quedó más corto de 2000, revisar que el prefijo `"Backup FALLIDO el ..."` más los 5000 `x` efectivamente superen el corte — debería.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Interfaces/INotificadorAlertas.cs \
        src/StockApp.Application/Alertas/NotificadorAlertasNulo.cs \
        src/StockApp.Infrastructure/Notificaciones/NotificadorWebhook.cs \
        tests/StockApp.Infrastructure.Tests/TestInfra/FakeHttpHandler.cs \
        tests/StockApp.Infrastructure.Tests/Notificaciones/NotificadorWebhookTests.cs
git commit -m "feat(alertas): notificador webhook con heartbeat y sufijo /fail"
```

---

### Task 3: Enganche en ServicioBackup

**Files:**
- Modify: `src/StockApp.Application/Backups/ServicioBackup.cs` (constructor + `EjecutarCorridaAsync`)
- Modify: `tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs` (todas las construcciones del SUT)

**Interfaces:**
- Consumes: `INotificadorAlertas` (Task 2), `NotificadorAlertasNulo` (Task 2).
- Produces: constructor `ServicioBackup(IEjecutorPgDump, ICorridaBackupRepository, INotificadorAlertas, ILogger<ServicioBackup>)`.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar al final de `tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs`, dentro de la clase, el fake y los tres tests nuevos:

```csharp
    private sealed class NotificadorAlertasFake : INotificadorAlertas
    {
        private readonly bool _explota;
        public List<CorridaBackup> Notificadas { get; } = new();

        public NotificadorAlertasFake(bool explota = false) => _explota = explota;

        public Task NotificarCorridaBackupAsync(CorridaBackup corrida, CancellationToken ct = default)
        {
            Notificadas.Add(corrida);
            if (_explota)
                throw new InvalidOperationException("el notificador explotó");
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Exitosa_NotificaLaCorrida()
    {
        var directorio = CrearDirectorioTemporal();
        var notificador = new NotificadorAlertasFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(),
            notificador, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Exitosa, notificada.Resultado);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Fallida_NotificaLaCorrida()
    {
        var directorio = CrearDirectorioTemporal();
        var notificador = new NotificadorAlertasFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: false, mensajeError: "pg_dump: fallo simulado"),
            new CorridaBackupRepositoryFake(), notificador, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
        Assert.Equal("pg_dump: fallo simulado", notificada.MotivoFallo);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_ElNotificadorExplota_NoRompeElBackupNiPierdeLaCorrida()
    {
        // El test más importante del conjunto: notificar es best-effort. Un canal de alerta roto
        // que además tumba el backup convierte una molestia en un desastre.
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), repo,
            new NotificadorAlertasFake(explota: true), NullLogger<ServicioBackup>.Instance);

        var ex = await Record.ExceptionAsync(() =>
            svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None));

        Assert.Null(ex);
        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
    }
```

Agregar el `using StockApp.Application.Interfaces;` al tope del archivo si no está.

- [ ] **Step 2: Correr los tests para verificar que fallan**

```bash
dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ServicioBackupTests
```
Esperado: FALLA de compilación — el constructor de `ServicioBackup` todavía toma 3 parámetros.

- [ ] **Step 3: Agregar la dependencia al constructor de ServicioBackup**

En `src/StockApp.Application/Backups/ServicioBackup.cs`, reemplazar el bloque de campos y el constructor:

```csharp
    private readonly IEjecutorPgDump _ejecutor;
    private readonly ICorridaBackupRepository _corridas;
    private readonly INotificadorAlertas _notificador;
    private readonly ILogger<ServicioBackup> _logger;

    public ServicioBackup(
        IEjecutorPgDump ejecutor,
        ICorridaBackupRepository corridas,
        INotificadorAlertas notificador,
        ILogger<ServicioBackup> logger)
    {
        _ejecutor = ejecutor;
        _corridas = corridas;
        _notificador = notificador;
        _logger = logger;
    }
```

- [ ] **Step 4: Notificar después de persistir la corrida**

En `EjecutarCorridaAsync`, reemplazar:

```csharp
        await _corridas.AgregarAsync(corrida);

        if (corrida.Resultado == ResultadoBackup.Exitosa)
            await AplicarRetencionAsync(directorioBackups, ahoraUtc);
```

por:

```csharp
        await _corridas.AgregarAsync(corrida);

        await NotificarSinRomperAsync(corrida, cancellationToken);

        if (corrida.Resultado == ResultadoBackup.Exitosa)
            await AplicarRetencionAsync(directorioBackups, ahoraUtc);
```

Y agregar el método privado al final de la clase:

```csharp
    /// <summary>Defensa en profundidad: INotificadorAlertas ya se compromete a no propagar
    /// excepciones, pero notificar es best-effort y no puede tumbar una corrida que salió bien.
    /// Una implementación mal escrita del contrato no debería costarnos el backup.</summary>
    private async Task NotificarSinRomperAsync(CorridaBackup corrida, CancellationToken ct)
    {
        try
        {
            await _notificador.NotificarCorridaBackupAsync(corrida, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la notificación del resultado del backup.");
        }
    }
```

Agregar `using StockApp.Application.Interfaces;` si no está (ya está: `ICorridaBackupRepository` vive ahí).

- [ ] **Step 5: Actualizar las construcciones existentes del SUT en los tests**

En `tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs` hay ~30 construcciones con la forma `new ServicioBackup(<ejecutor>, <repo>, NullLogger<ServicioBackup>.Instance)`. Insertar `new NotificadorAlertasNulo(),` como tercer argumento en todas.

Reemplazo mecánico:

```bash
sd 'NullLogger<ServicioBackup>\.Instance\)' 'new NotificadorAlertasNulo(), NullLogger<ServicioBackup>.Instance)' tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs
```

Cuidado: eso también tocaría las tres construcciones nuevas del Step 1, que ya pasan un notificador propio. Revisar el diff con `git diff` y revertir a mano esos tres casos (los que pasan `notificador` o `new NotificadorAlertasFake(...)`) para que no queden con dos notificadores.

Agregar `using StockApp.Application.Alertas;` al tope del archivo de tests.

- [ ] **Step 6: Actualizar los demás llamadores que construyan ServicioBackup a mano**

```bash
rg -n 'new ServicioBackup\(' --type cs
```

Para cada resultado fuera de `ServicioBackupTests.cs`, agregar el notificador. Los llamadores de producción resuelven por DI, así que no deberían aparecer — si aparece alguno en `tests/StockApp.Api.Tests` o `tests/StockApp.Infrastructure.Tests`, usar `new NotificadorAlertasNulo()`.

- [ ] **Step 7: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ServicioBackupTests
```
Esperado: todos verdes, incluidos los 3 nuevos.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Application/Backups/ServicioBackup.cs \
        tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs
git commit -m "feat(alertas): ServicioBackup notifica el resultado de cada corrida"
```

---

### Task 4: Enganche en los dos caminos de la Api y registro DI

**Files:**
- Modify: `src/StockApp.Api/Backups/DisparadorBackupManual.cs`
- Modify: `src/StockApp.Api/Backups/BackupProgramadoService.cs`
- Modify: `src/StockApp.Api/Program.cs` (registros DI + `AddHttpClient`)
- Test: `tests/StockApp.Api.Tests/Backups/DisparadorBackupManualTests.cs` (agregar)

**Interfaces:**
- Consumes: `INotificadorAlertas` (Task 2), `IConfiguracionAlertasRepository` (Task 1), `NotificadorWebhook` (Task 2).

**Contexto crítico:** `DisparadorBackupManual` y `BackupProgramadoService` son **Singleton**. `NotificadorWebhook` depende de `IConfiguracionAlertasRepository`, que es **Scoped** (usa `AppDbContext`). Por eso el notificador se registra **Scoped** y en estos dos lugares se resuelve **desde el scope** (`scope.ServiceProvider.GetRequiredService<INotificadorAlertas>()`), nunca por constructor.

- [ ] **Step 1: Escribir el test que falla**

En `tests/StockApp.Api.Tests/Backups/DisparadorBackupManualTests.cs`, agregar un test que verifique que el fallo inesperado también notifica. Adaptar la construcción del SUT al helper que ya use ese archivo (leerlo antes); el núcleo de la aserción:

```csharp
    [Fact]
    public async Task Disparar_FalloInesperado_PersisteLaFallaYLaNotifica()
    {
        // El camino de "fallo inesperado" no pasa por ServicioBackup: si no se engancha acá,
        // este modo de falla queda mudo.
        var notificador = new NotificadorAlertasFake();
        var (disparador, repo) = CrearDisparadorQueFallaInesperadamente(notificador);

        disparador.Disparar(usuarioId: 1);
        await disparador.UltimaCorridaEnBackgroundParaTests!;

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
        var notificada = Assert.Single(notificador.Notificadas);
        Assert.Equal(ResultadoBackup.Fallida, notificada.Resultado);
    }
```

Definir `NotificadorAlertasFake` en ese archivo con la misma forma que en Task 3 (los proyectos de test no comparten código). El helper `CrearDisparadorQueFallaInesperadamente` debe registrar el notificador en el `IServiceScopeFactory` de prueba que ya usa ese archivo — leer cómo está armado y seguir ese patrón.

- [ ] **Step 2: Correr el test para verificar que falla**

```bash
dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~DisparadorBackupManualTests
```
Esperado: FALLA — `notificador.Notificadas` queda vacío.

- [ ] **Step 3: Notificar en PersistirFallaAsync**

En `src/StockApp.Api/Backups/DisparadorBackupManual.cs`, dentro de `PersistirFallaAsync`, después de agregar la corrida al repositorio, resolver el notificador **del mismo scope** y llamarlo:

```csharp
        var notificador = scope.ServiceProvider.GetRequiredService<INotificadorAlertas>();
        try
        {
            await notificador.NotificarCorridaBackupAsync(corrida);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la notificación del backup fallido.");
        }
```

Usar el nombre real de la variable de scope que exista en ese método. Agregar `using StockApp.Application.Interfaces;`.

- [ ] **Step 4: Notificar en la última resistencia del scheduler**

En `src/StockApp.Api/Backups/BackupProgramadoService.cs`, en el `catch (Exception ex) when (ex is not OperationCanceledException)` de `EjecutarCorridaSeguraAsync` (que hoy **solo loguea** y no persiste nada), agregar la notificación de un fallo sintético:

```csharp
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Fallo inesperado en la corrida programada de backup.");

            // Este camino no persiste fila (a diferencia de DisparadorBackupManual): sin esta
            // notificación, una corrida programada que revienta antes de llegar a ServicioBackup
            // no deja ningún rastro hacia afuera. Se arma una corrida sintética solo para avisar.
            try
            {
                await using var scopeAviso = _scopeFactory.CreateAsyncScope();
                var notificador = scopeAviso.ServiceProvider.GetRequiredService<INotificadorAlertas>();
                await notificador.NotificarCorridaBackupAsync(new CorridaBackup
                {
                    IniciadaEn = DateTime.UtcNow,
                    FinalizadaEn = DateTime.UtcNow,
                    Resultado = ResultadoBackup.Fallida,
                    MotivoFallo = $"Fallo inesperado en la corrida programada: {ex.Message}",
                });
            }
            catch (Exception exAviso)
            {
                _logger.LogWarning(exAviso, "Además falló la notificación del fallo inesperado.");
            }
        }
```

Agregar los `using` necesarios: `StockApp.Application.Interfaces`, `StockApp.Domain.Entities`, `StockApp.Domain.Enums`.

- [ ] **Step 5: Registrar todo en el contenedor**

En `src/StockApp.Api/Program.cs`, justo después del bloque de backups (después de la línea que registra `ServicioConsultaBackups`), agregar:

```csharp
// ── Canal de alerta de backups ─────────────────────────────────────────────
// Primer AddHttpClient del repo. Timeout corto a propósito: notificar es best-effort y no
// puede quedar colgado bloqueando el hilo de una corrida de backup.
builder.Services.AddHttpClient<INotificadorAlertas, NotificadorWebhook>(c =>
{
    c.Timeout = TimeSpan.FromSeconds(10);
});
builder.Services.AddScoped<IConfiguracionAlertasRepository, ConfiguracionAlertasRepository>();
```

Nota: `AddHttpClient<TInterface, TImplementation>` registra la implementación como **Transient** con su `HttpClient` inyectado. Eso es compatible con la dependencia Scoped `IConfiguracionAlertasRepository` cuando se resuelve dentro de un scope, que es como lo usan los tres puntos de enganche.

Agregar los `using` que falten: `StockApp.Application.Interfaces`, `StockApp.Infrastructure.Notificaciones`, `StockApp.Infrastructure.Repositories`.

- [ ] **Step 6: Correr los tests de Api**

```bash
dotnet test tests/StockApp.Api.Tests
```
Esperado: todo verde, incluido el test nuevo.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Backups/DisparadorBackupManual.cs \
        src/StockApp.Api/Backups/BackupProgramadoService.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/Backups/DisparadorBackupManualTests.cs
git commit -m "feat(alertas): engancha la notificacion en los tres caminos de fallo de backup"
```

---

### Task 5: Servicio de configuración y endpoints

**Files:**
- Create: `src/StockApp.Application/Alertas/AlertasDtos.cs`
- Create: `src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs`
- Create: `src/StockApp.Api/Endpoints/ConfiguracionAlertasEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (registro + `MapConfiguracionAlertasEndpoints`)
- Test: `tests/StockApp.Application.Tests/Alertas/ServicioConfiguracionAlertasTests.cs`
- Test: `tests/StockApp.Api.Tests/ConfiguracionAlertasEndpointTests.cs`

**Interfaces:**
- Consumes: `IConfiguracionAlertasRepository` (Task 1), `INotificadorAlertas` (Task 2).
- Produces: `ConfiguracionAlertasDto(string? UrlWebhook, bool Habilitado, DateTime? ActualizadoEn)`; `ResultadoPruebaAlertaDto(bool Exitoso, int? StatusCode, string? Mensaje)`; `ServicioConfiguracionAlertas.ObtenerAsync()`, `.GuardarAsync(string? url, bool habilitado)`, `.ProbarAsync()`.

- [ ] **Step 1: Escribir los tests de validación que fallan**

`tests/StockApp.Application.Tests/Alertas/ServicioConfiguracionAlertasTests.cs`:

```csharp
using StockApp.Application.Alertas;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Alertas;

public class ServicioConfiguracionAlertasTests
{
    private sealed class RepoFake : IConfiguracionAlertasRepository
    {
        public ConfiguracionAlertas Configuracion { get; set; } = new();
        public ConfiguracionAlertas? Guardada { get; private set; }

        public Task<ConfiguracionAlertas> ObtenerAsync() => Task.FromResult(Configuracion);

        public Task GuardarAsync(ConfiguracionAlertas configuracion)
        {
            Guardada = configuracion;
            Configuracion = configuracion;
            return Task.CompletedTask;
        }
    }

    private sealed class SesionFake : ICurrentSession
    {
        public Usuario? UsuarioActual { get; set; } = new() { Id = 3, Nombre = "admin" };
        public RolUsuario? RolActual { get; set; } = RolUsuario.Admin;
    }

    private static (ServicioConfiguracionAlertas Sut, RepoFake Repo) Crear()
    {
        var repo = new RepoFake();
        var sut = new ServicioConfiguracionAlertas(
            repo, new AuthorizationService(), new SesionFake(), new NotificadorAlertasNulo());
        return (sut, repo);
    }

    [Fact]
    public async Task GuardarAsync_UrlHttp_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync("http://hc-ping.com/abc", habilitado: true));

        Assert.Contains("https", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GuardarAsync_UrlRelativa_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync("hc-ping.com/abc", habilitado: true));
    }

    [Fact]
    public async Task GuardarAsync_HabilitadoSinUrl_RechazaConArgumentException()
    {
        var (sut, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.GuardarAsync(null, habilitado: true));
    }

    [Fact]
    public async Task GuardarAsync_DeshabilitadoSinUrl_EsValido()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync(null, habilitado: false);

        Assert.NotNull(repo.Guardada);
        Assert.False(repo.Guardada!.Habilitado);
        Assert.Null(repo.Guardada.UrlWebhook);
    }

    [Fact]
    public async Task GuardarAsync_UrlValida_PersisteYSellaAutorYFecha()
    {
        var (sut, repo) = Crear();

        await sut.GuardarAsync("https://hc-ping.com/abc", habilitado: true);

        Assert.Equal("https://hc-ping.com/abc", repo.Guardada!.UrlWebhook);
        Assert.True(repo.Guardada.Habilitado);
        Assert.Equal(3, repo.Guardada.ActualizadoPorUsuarioId);
        Assert.NotEqual(default, repo.Guardada.ActualizadoEn);
    }

    [Fact]
    public async Task ObtenerAsync_DevuelveElEstadoActual()
    {
        var (sut, repo) = Crear();
        repo.Configuracion = new ConfiguracionAlertas
        {
            UrlWebhook = "https://hc-ping.com/xyz",
            Habilitado = true,
            ActualizadoEn = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Utc),
        };

        var dto = await sut.ObtenerAsync();

        Assert.Equal("https://hc-ping.com/xyz", dto.UrlWebhook);
        Assert.True(dto.Habilitado);
    }
}
```

Verificar la forma real de `ICurrentSession` antes de escribir `SesionFake` (leer `src/StockApp.Application/Interfaces/ICurrentSession.cs`) y ajustar los miembros.

- [ ] **Step 2: Correr para verificar que falla**

```bash
dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ServicioConfiguracionAlertasTests
```
Esperado: FALLA de compilación.

- [ ] **Step 3: Crear los DTOs**

`src/StockApp.Application/Alertas/AlertasDtos.cs`:

```csharp
namespace StockApp.Application.Alertas;

/// <summary>Estado del canal de alerta tal como lo ve el desktop.</summary>
public sealed record ConfiguracionAlertasDto(string? UrlWebhook, bool Habilitado, DateTime? ActualizadoEn);

/// <summary>
/// Resultado de un ping de prueba. Viaja el status code pero NUNCA el cuerpo de la respuesta:
/// el servidor postea a una URL que provee el usuario (SSRF), y devolver el body convertiría el
/// endpoint en un proxy de lectura hacia la red interna.
/// </summary>
public sealed record ResultadoPruebaAlertaDto(bool Exitoso, int? StatusCode, string? Mensaje);
```

- [ ] **Step 4: Implementar el servicio**

`src/StockApp.Application/Alertas/ServicioConfiguracionAlertas.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Alertas;

/// <summary>
/// Lectura, validación y guardado del canal de alerta, más el ping de prueba. Segunda barrera de
/// autorización (defensa en profundidad): la policy HTTP ya exige GestionarDiagnostico, pero el
/// servicio lo verifica igual, mismo criterio que el resto de los servicios de Application.
/// </summary>
public sealed class ServicioConfiguracionAlertas
{
    private readonly IConfiguracionAlertasRepository _repo;
    private readonly IAuthorizationService _auth;
    private readonly ICurrentSession _session;
    private readonly INotificadorAlertas _notificador;

    public ServicioConfiguracionAlertas(
        IConfiguracionAlertasRepository repo,
        IAuthorizationService auth,
        ICurrentSession session,
        INotificadorAlertas notificador)
    {
        _repo = repo;
        _auth = auth;
        _session = session;
        _notificador = notificador;
    }

    public async Task<ConfiguracionAlertasDto> ObtenerAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var cfg = await _repo.ObtenerAsync();
        return new ConfiguracionAlertasDto(
            cfg.UrlWebhook,
            cfg.Habilitado,
            cfg.ActualizadoEn == default ? null : cfg.ActualizadoEn);
    }

    public async Task GuardarAsync(string? urlWebhook, bool habilitado)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var url = string.IsNullOrWhiteSpace(urlWebhook) ? null : urlWebhook.Trim();

        if (url is not null)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var parseada))
                throw new ArgumentException("La URL del webhook debe ser una URL absoluta.");

            if (parseada.Scheme != Uri.UriSchemeHttps)
                throw new ArgumentException("La URL del webhook debe usar https.");
        }

        // Habilitar sin URL es una configuración que miente: el interruptor queda en "sí" y no
        // notifica nada. Se rechaza en vez de guardar un estado engañoso.
        if (habilitado && url is null)
            throw new ArgumentException("No se puede habilitar el canal de alerta sin una URL de webhook.");

        var cfg = await _repo.ObtenerAsync();
        cfg.UrlWebhook = url;
        cfg.Habilitado = habilitado;
        cfg.ActualizadoEn = DateTime.UtcNow;
        cfg.ActualizadoPorUsuarioId = _session.UsuarioActual?.Id;

        await _repo.GuardarAsync(cfg);
    }

    /// <summary>
    /// Dispara un ping real contra la URL configurada, como si fuera una corrida exitosa. Es el
    /// núcleo de la funcionalidad: un canal de alerta que nunca se probó no es un canal, es una
    /// creencia — la URL mal escrita se descubriría recién el día del fallo.
    /// </summary>
    public async Task<ResultadoPruebaAlertaDto> ProbarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var cfg = await _repo.ObtenerAsync();

        if (string.IsNullOrWhiteSpace(cfg.UrlWebhook))
            return new ResultadoPruebaAlertaDto(false, null, "No hay una URL de webhook configurada.");

        if (!cfg.Habilitado)
            return new ResultadoPruebaAlertaDto(false, null, "El canal de alerta está deshabilitado.");

        await _notificador.NotificarCorridaBackupAsync(new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow,
            FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Exitosa,
            NombreArchivo = "prueba-de-canal",
            TamanioBytes = 0,
        });

        // El notificador se traga los errores por contrato, así que este resultado confirma que
        // el ping se intentó, no que el servidor remoto lo haya aceptado. El detalle fino queda
        // en el log del servidor, descargable desde la misma pantalla de Mantenimiento.
        return new ResultadoPruebaAlertaDto(true, null, "Se envió un ping de prueba al webhook configurado.");
    }
}
```

- [ ] **Step 5: Correr los tests de Application**

```bash
dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ServicioConfiguracionAlertasTests
```
Esperado: 6 tests PASS.

- [ ] **Step 6: Escribir los tests de endpoints que fallan**

`tests/StockApp.Api.Tests/ConfiguracionAlertasEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Alertas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ConfiguracionAlertasEndpointTests : ApiTestBase
{
    public ConfiguracionAlertasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteCon(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Get_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/configuracion/alertas");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Get_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).GetAsync("/configuracion/alertas");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_ConTokenAdmin_Devuelve200ConElEstado()
    {
        var response = await ClienteCon(TokenAdmin()).GetAsync("/configuracion/alertas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ConfiguracionAlertasDto>();
        Assert.NotNull(dto);
        Assert.False(dto!.Habilitado);
    }

    [Fact]
    public async Task Put_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Put_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Put_UrlHttp_Devuelve400()
    {
        var response = await ClienteCon(TokenAdmin())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = "http://hc-ping.com/a", Habilitado = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_HabilitadoSinUrl_Devuelve400()
    {
        var response = await ClienteCon(TokenAdmin())
            .PutAsJsonAsync("/configuracion/alertas", new { UrlWebhook = (string?)null, Habilitado = true });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Put_UrlValida_Devuelve200YElGetPosteriorLaDevuelve()
    {
        var client = ClienteCon(TokenAdmin());

        var put = await client.PutAsJsonAsync(
            "/configuracion/alertas", new { UrlWebhook = "https://hc-ping.com/abc", Habilitado = true });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var dto = await (await client.GetAsync("/configuracion/alertas"))
            .Content.ReadFromJsonAsync<ConfiguracionAlertasDto>();
        Assert.Equal("https://hc-ping.com/abc", dto!.UrlWebhook);
        Assert.True(dto.Habilitado);
    }

    [Fact]
    public async Task Probar_ConTokenOperador_Devuelve403()
    {
        var response = await ClienteCon(TokenOperador()).PostAsync("/configuracion/alertas/probar", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Probar_SinUrlConfigurada_Devuelve200ConExitosoFalse()
    {
        var response = await ClienteCon(TokenAdmin()).PostAsync("/configuracion/alertas/probar", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<ResultadoPruebaAlertaDto>();
        Assert.False(dto!.Exitoso);
    }
}
```

- [ ] **Step 7: Correr para verificar que falla**

```bash
dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~ConfiguracionAlertasEndpointTests
```
Esperado: 404 en todos (las rutas no existen).

- [ ] **Step 8: Crear los endpoints**

`src/StockApp.Api/Endpoints/ConfiguracionAlertasEndpoints.cs`:

```csharp
using StockApp.Application.Alertas;
using StockApp.Application.Authorization;

namespace StockApp.Api.Endpoints;

public record GuardarConfiguracionAlertasRequest(string? UrlWebhook, bool Habilitado);

public static class ConfiguracionAlertasEndpoints
{
    public static IEndpointRouteBuilder MapConfiguracionAlertasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/configuracion/alertas");

        group.MapGet("/", async (ServicioConfiguracionAlertas servicio) =>
            Results.Ok(await servicio.ObtenerAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        group.MapPut("/", async (GuardarConfiguracionAlertasRequest request, ServicioConfiguracionAlertas servicio) =>
        {
            // La validación vive en el servicio (ArgumentException -> 400 vía DomainExceptionHandler),
            // igual que en el resto de los endpoints del repo. El handler queda tonto a propósito.
            await servicio.GuardarAsync(request.UrlWebhook, request.Habilitado);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        // Ping de prueba. Devuelve 200 con Exitoso = false ante configuración incompleta: no es un
        // error del cliente, es un diagnóstico — el resultado de la prueba ES la respuesta.
        // Nunca se devuelve el cuerpo de la respuesta remota (nota SSRF del spec).
        group.MapPost("/probar", async (ServicioConfiguracionAlertas servicio) =>
            Results.Ok(await servicio.ProbarAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
```

- [ ] **Step 9: Registrar el servicio y mapear los endpoints**

En `src/StockApp.Api/Program.cs`, junto al bloque de alertas de Task 4:

```csharp
builder.Services.AddScoped<ServicioConfiguracionAlertas>();
```

Y en el bloque de `Map*Endpoints`, después de `app.MapBackupsEndpoints();`:

```csharp
app.MapConfiguracionAlertasEndpoints();
```

- [ ] **Step 10: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~ConfiguracionAlertasEndpointTests
```
Esperado: 10 tests PASS.

- [ ] **Step 11: Commit**

```bash
git add src/StockApp.Application/Alertas/ \
        src/StockApp.Api/Endpoints/ConfiguracionAlertasEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Application.Tests/Alertas/ \
        tests/StockApp.Api.Tests/ConfiguracionAlertasEndpointTests.cs
git commit -m "feat(alertas): endpoints de configuracion y prueba del canal"
```

---

### Task 6: Cliente HTTP del desktop

**Files:**
- Create: `src/StockApp.Application/Alertas/IConfiguracionAlertasService.cs`
- Create: `src/StockApp.ApiClient/ConfiguracionAlertasApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (registro DI)
- Test: `tests/StockApp.ApiClient.Tests/ConfiguracionAlertasApiClientTests.cs`

**Interfaces:**
- Consumes: `ConfiguracionAlertasDto`, `ResultadoPruebaAlertaDto` (Task 5).
- Produces: `IConfiguracionAlertasService.ObtenerAsync(ct)`, `.GuardarAsync(string? url, bool habilitado, ct)`, `.ProbarAsync(ct)`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.ApiClient.Tests/ConfiguracionAlertasApiClientTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Alertas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ConfiguracionAlertasApiClientTests
{
    [Fact]
    public async Task ObtenerAsync_PegaAlaRutaCorrectaYDeserializa()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ConfiguracionAlertasDto("https://hc-ping.com/a", true, null)),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.Crear(fake));

        var dto = await client.ObtenerAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.EndsWith("configuracion/alertas", fake.UltimaRequest.RequestUri!.ToString());
        Assert.Equal("https://hc-ping.com/a", dto.UrlWebhook);
        Assert.True(dto.Habilitado);
    }

    [Fact]
    public async Task GuardarAsync_EnviaPutConElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new ConfiguracionAlertasApiClient(TestHttp.Crear(fake));

        await client.GuardarAsync("https://hc-ping.com/b", habilitado: true);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Contains("hc-ping.com/b", fake.UltimoBody);
    }

    [Fact]
    public async Task ProbarAsync_EnviaPostYDevuelveElResultado()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ResultadoPruebaAlertaDto(true, 200, "ok")),
        });
        var client = new ConfiguracionAlertasApiClient(TestHttp.Crear(fake));

        var resultado = await client.ProbarAsync();

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.EndsWith("configuracion/alertas/probar", fake.UltimaRequest.RequestUri!.ToString());
        Assert.True(resultado.Exitoso);
    }
}
```

Verificar la firma real del helper `TestHttp` antes de usarlo (leer `tests/StockApp.ApiClient.Tests/TestInfra/TestHttp.cs`) y ajustar la construcción del `HttpClient`.

- [ ] **Step 2: Correr para verificar que falla**

```bash
dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ConfiguracionAlertasApiClientTests
```
Esperado: FALLA de compilación.

- [ ] **Step 3: Crear el contrato del cliente**

`src/StockApp.Application/Alertas/IConfiguracionAlertasService.cs`:

```csharp
namespace StockApp.Application.Alertas;

/// <summary>Contrato que consume el desktop; lo implementa ConfiguracionAlertasApiClient sobre HTTP.</summary>
public interface IConfiguracionAlertasService
{
    Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default);
    Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default);
    Task<ResultadoPruebaAlertaDto> ProbarAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Implementar el cliente**

`src/StockApp.ApiClient/ConfiguracionAlertasApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Alertas;

namespace StockApp.ApiClient;

public sealed class ConfiguracionAlertasApiClient : IConfiguracionAlertasService
{
    private readonly HttpClient _http;

    public ConfiguracionAlertasApiClient(HttpClient http) => _http = http;

    public async Task<ConfiguracionAlertasDto> ObtenerAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("configuracion/alertas", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<ConfiguracionAlertasDto>(cancellationToken: ct)
               ?? new ConfiguracionAlertasDto(null, false, null);
    }

    public async Task GuardarAsync(string? urlWebhook, bool habilitado, CancellationToken ct = default)
    {
        var body = new { UrlWebhook = urlWebhook, Habilitado = habilitado };
        var response = await ApiErrores.EnviarAsync(
            () => _http.PutAsJsonAsync("configuracion/alertas", body, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<ResultadoPruebaAlertaDto> ProbarAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(
            () => _http.PostAsync("configuracion/alertas/probar", null, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<ResultadoPruebaAlertaDto>(cancellationToken: ct)
               ?? new ResultadoPruebaAlertaDto(false, null, "Respuesta vacía del servidor.");
    }
}
```

- [ ] **Step 5: Registrar en el DI del desktop**

En `src/StockApp.Presentation/App.axaml.cs`, junto a los otros ApiClients (cerca del registro de `IBackupsService`), usando el `HttpClient` principal (no el keyed `"Descargas"` — los bodies son chicos y el timeout de 10s alcanza):

```csharp
        services.AddTransient<IConfiguracionAlertasService>(sp =>
            new ConfiguracionAlertasApiClient(sp.GetRequiredService<HttpClient>()));
```

- [ ] **Step 6: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ConfiguracionAlertasApiClientTests
```
Esperado: 3 tests PASS.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Alertas/IConfiguracionAlertasService.cs \
        src/StockApp.ApiClient/ConfiguracionAlertasApiClient.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.ApiClient.Tests/ConfiguracionAlertasApiClientTests.cs
git commit -m "feat(alertas): cliente HTTP de configuracion de alertas para el desktop"
```

---

### Task 7: Sección Alertas en Mantenimiento

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs`

**Interfaces:**
- Consumes: `IConfiguracionAlertasService` (Task 6).
- Produces: props `UrlWebhook`, `AlertasHabilitadas`, `GuardandoAlertas`, `ProbandoAlertas`; comandos `GuardarAlertasCommand`, `ProbarAlertasCommand`.

- [ ] **Step 1: Escribir los tests que fallan**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs`. **Este proyecto usa Moq** (a diferencia del resto). Antes de escribir, leer el helper `Crear(...)` existente y extenderlo con el quinto mock:

```csharp
    [Fact]
    public async Task CargarAsync_TraeLaConfiguracionDeAlertas()
    {
        var (vm, _, _, _, alertasMock) = Crear();
        alertasMock.Setup(a => a.ObtenerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConfiguracionAlertasDto("https://hc-ping.com/a", true, null));

        await vm.CargarAsync();

        Assert.Equal("https://hc-ping.com/a", vm.UrlWebhook);
        Assert.True(vm.AlertasHabilitadas);
    }

    [Fact]
    public async Task GuardarAlertasCommand_GuardaYAvisaAlUsuario()
    {
        var (vm, _, _, confirmacionMock, alertasMock) = Crear();
        vm.UrlWebhook = "https://hc-ping.com/b";
        vm.AlertasHabilitadas = true;

        await vm.GuardarAlertasCommand.ExecuteAsync(null);

        alertasMock.Verify(a => a.GuardarAsync("https://hc-ping.com/b", true, It.IsAny<CancellationToken>()), Times.Once);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Once);
        Assert.False(vm.GuardandoAlertas);
    }

    [Fact]
    public async Task GuardarAlertasCommand_ErrorDelServidor_InformaSinRomper()
    {
        var (vm, _, _, confirmacionMock, alertasMock) = Crear();
        alertasMock.Setup(a => a.GuardarAsync(It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("La URL del webhook debe usar https."));

        await vm.GuardarAlertasCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync("La URL del webhook debe usar https."), Times.Once);
        Assert.False(vm.GuardandoAlertas);
    }

    [Fact]
    public async Task ProbarAlertasCommand_InformaElResultadoDelPing()
    {
        var (vm, _, _, confirmacionMock, alertasMock) = Crear();
        alertasMock.Setup(a => a.ProbarAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ResultadoPruebaAlertaDto(true, 200, "Se envió un ping de prueba."));

        await vm.ProbarAlertasCommand.ExecuteAsync(null);

        confirmacionMock.Verify(c => c.InformarAsync("Se envió un ping de prueba."), Times.Once);
        Assert.False(vm.ProbandoAlertas);
    }
```

- [ ] **Step 2: Correr para verificar que falla**

```bash
dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~MantenimientoViewModelTests
```
Esperado: FALLA de compilación.

- [ ] **Step 3: Extender el ViewModel**

En `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs`:

Agregar el campo y el parámetro al constructor (quinto parámetro):

```csharp
    private readonly IConfiguracionAlertasService _alertas;
```

Agregar las propiedades observables junto a las existentes:

```csharp
    [ObservableProperty]
    private string? _urlWebhook;

    [ObservableProperty]
    private bool _alertasHabilitadas;

    [ObservableProperty]
    private bool _guardandoAlertas;

    [ObservableProperty]
    private bool _probandoAlertas;
```

En `CargarAsync()`, agregar la carga tolerante (mismo criterio que el resto de las secciones: un fallo en una sección no debe dejar la pantalla en blanco):

```csharp
        try
        {
            var cfg = await _alertas.ObtenerAsync();
            UrlWebhook = cfg.UrlWebhook;
            AlertasHabilitadas = cfg.Habilitado;
        }
        catch (Exception)
        {
            // Sección no crítica: si el servidor no responde, el resto de Mantenimiento igual sirve.
        }
```

Agregar los dos comandos, con el mismo patrón de guard + try/catch/finally que `IniciarBackupAsync`:

```csharp
    [RelayCommand]
    private async Task GuardarAlertasAsync()
    {
        if (GuardandoAlertas) return;
        GuardandoAlertas = true;
        try
        {
            await _alertas.GuardarAsync(UrlWebhook, AlertasHabilitadas);
            await _confirmacion.InformarAsync("Configuración de alertas guardada.");
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            GuardandoAlertas = false;
        }
    }

    [RelayCommand]
    private async Task ProbarAlertasAsync()
    {
        if (ProbandoAlertas) return;
        ProbandoAlertas = true;
        try
        {
            var resultado = await _alertas.ProbarAsync();
            await _confirmacion.InformarAsync(resultado.Mensaje ?? "Prueba finalizada.");
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            ProbandoAlertas = false;
        }
    }
```

Agregar `using StockApp.Application.Alertas;`.

- [ ] **Step 4: Agregar la sección al XAML**

En `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`, después del `Border` de la sección Diagnóstico y antes del cierre `</DockPanel>`:

```xml
<TextBlock DockPanel.Dock="Top"
           Text="Alertas"
           Classes="caption"
           Opacity="0.6"
           Margin="0,24,0,12" />

<Border Classes="card" DockPanel.Dock="Top">
    <StackPanel Spacing="12" MaxWidth="620" HorizontalAlignment="Left">
        <TextBlock Text="Avisá cuando un backup falle o cuando el sistema deje de reportar."
                   Classes="body"
                   TextWrapping="Wrap" />

        <StackPanel Spacing="4">
            <TextBlock Text="URL de webhook (healthchecks.io)" />
            <TextBox Text="{Binding UrlWebhook}" Watermark="https://hc-ping.com/xxxxxxxx" />
        </StackPanel>

        <CheckBox Content="Habilitado" IsChecked="{Binding AlertasHabilitadas}" />

        <StackPanel Orientation="Horizontal" Spacing="8">
            <Button Classes="primary"
                    Content="Guardar"
                    Command="{Binding GuardarAlertasCommand}"
                    IsEnabled="{Binding !GuardandoAlertas}" />
            <Button Classes="secondary"
                    Content="Probar"
                    Command="{Binding ProbarAlertasCommand}"
                    IsEnabled="{Binding !ProbandoAlertas}" />
        </StackPanel>
    </StackPanel>
</Border>
```

- [ ] **Step 5: Correr los tests y verificar que pasan**

```bash
dotnet test tests/StockApp.Presentation.Tests --filter FullyQualifiedName~MantenimientoViewModelTests
```
Esperado: todos verdes, incluidos los 4 nuevos.

- [ ] **Step 6: Correr la suite completa**

```bash
dotnet test
```
Esperado: todo verde. Línea base previa conocida: 2372 tests. Con este trabajo deberían sumar ~36 más.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs \
        src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml \
        tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs
git commit -m "feat(alertas): seccion de configuracion de alertas en Mantenimiento"
```

---

## Verificación final (obligatoria antes de dar por cerrado)

No alcanza con que los tests estén verdes. Hay dos cosas que los tests **no** pueden probar y que hay que verificar contra el sistema real, siguiendo la convención de verificación orgánica del proyecto.

- [ ] **Aplicar la migración a la base real** (no a `stockapp_design`):

```bash
dotnet ef database update \
  --connection "Host=localhost;Port=5432;Database=stockapp;Username=stockapp;Password=stockapp" \
  --project src/StockApp.Infrastructure --startup-project src/StockApp.Api
```

Verificar que la fila sembrada existe:

```bash
psql -h localhost -U stockapp -d stockapp -c 'SELECT * FROM "ConfiguracionesAlertas";'
```

- [ ] **Verificación de mutación del guardián** — que un test rojo aparezca al reintroducir el bug. Comentar la línea `await NotificarSinRomperAsync(corrida, cancellationToken);` en `ServicioBackup`, correr `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ServicioBackupTests`, y **pegar el rojo**. Después descomentar. Sin esto, no hay evidencia de que el test sea un guardián y no un acompañante.

- [ ] **Ping real de punta a punta**: crear un check gratuito en healthchecks.io, configurar su URL desde la pantalla de Mantenimiento, apretar **Probar**, y confirmar que el check pasa a estado "up" en la web. Después configurar la integración de Telegram en healthchecks y repetir: tiene que llegar el mensaje al celular.

- [ ] **Ping de fallo real**: forzar un fallo de `pg_dump` (por ejemplo, con una contraseña inválida en la connection string), disparar un backup manual desde la app, y confirmar que healthchecks recibe el `/fail` y que Telegram avisa.

- [ ] **Dead man's switch**: configurar el período y el grace time del check en healthchecks (por ejemplo, período 12h + grace 2h, coherente con `IntervaloEntreCorridas`). Bajar la API y confirmar que, pasado el grace, llega la alerta de "check is down". **Esta es la prueba que valida el motivo entero de la feature** — sin ella, solo se probó la mitad fácil.
