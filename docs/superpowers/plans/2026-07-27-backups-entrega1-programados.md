# Backups programados (Entrega 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Backups automáticos de PostgreSQL cada 12h, con retención escalonada y sin intervención manual, descargables desde el desktop (solo Admin) y con aviso de salud al iniciar sesión — tapando el agujero de "hoy no hay ningún backup".
**Architecture:** Primer `BackgroundService` del repo (`BackupProgramadoService`, StockApp.Api) crea su propio `IServiceScope` por corrida y orquesta `ServicioBackup` (Application) → `IEjecutorPgDump` (abstracción sobre `pg_dump`, implementación real `EjecutorPgDumpProceso` en Infrastructure) → `ICorridaBackupRepository` (metadatos en Postgres) → `PoliticaRetencion` (función pura, retención grandfather-father-son). `BackupsEndpoints` expone listado/descarga/salud con DOBLE barrera de autorización — policy HTTP (`.RequireAuthorization`) + `_auth.Verificar` en `ServicioConsultaBackups` (Application) — mismo patrón de defensa en profundidad que el resto de Finanzas (`RubroGastoService`/`FinanzasVistasService`); estos endpoints exponen un dump COMPLETO de la base, el activo más sensible del sistema, así que NO se saltea esa segunda capa aunque el spec original no la mencionara. El desktop agrega `MantenimientoView` (primera pantalla de Administración, Admin-only) y un banner de salud en `InicioViewModel`.
**Tech Stack:** .NET 10, EF Core 10 + Npgsql (PostgreSQL), ASP.NET Core Minimal APIs, `BackgroundService`/`PeriodicTimer`, Avalonia 12.0.5 (desktop), CommunityToolkit.Mvvm 8.4.1, xUnit + Moq + Testcontainers.PostgreSql.

## Global Constraints

- Ningún paquete NuGet nuevo en esta entrega (Serilog es Entrega 2). `System.Diagnostics.Process` y `NpgsqlConnectionStringBuilder` ya están disponibles transitivamente (Npgsql.EntityFrameworkCore.PostgreSQL en StockApp.Infrastructure).
- **CRÍTICO — tests de StockApp.Presentation (Avalonia, lentos):** cualquier `dotnet build`/`dotnet test` sobre StockApp.Presentation o sus proyectos de test (StockApp.Presentation.Tests, StockApp.Presentation.UiTests) va **SIEMPRE en foreground, con `timeout` como prefijo del comando (ej. `timeout 180 dotnet test ...`), NUNCA `run_in_background`**. Si da timeout, reportar y seguir — no reintentar en loop.
- Tests headless de Avalonia (`StockApp.Presentation.UiTests`, `[AvaloniaFact]`): el guard estático de `IconProvider.Current.Register` en `TestAppBuilder.cs`/`TestApp` ya existe — se reutiliza tal cual, no se reinventa.
- Toda View nueva de Avalonia debe enganchar `DataContextChanged` para disparar su carga inicial (bug recurrente confirmado del proyecto — ver `InicioView.axaml.cs` como referencia).
- Convención de reloj: el proyecto **no tiene** una abstracción `IClock`/`IReloj`. `PoliticaRetencion` recibe `DateTime ahoraUtc` como parámetro directo (determinismo en tests, sin mock de reloj). El resto del código nuevo usa `DateTime.UtcNow` inline donde no se requiere ese nivel de determinismo (mismo criterio que `ProductoService`/`AdjuntoService`).
- Commits: conventional commits en ESPAÑOL, sin `Co-Authored-By` ni atribución de IA. Un commit por Task (o por sub-bloque test+impl dentro de la Task cuando el Task es grande).
- Bash: nunca `cat`/`grep`/`find`/`sed`/`ls` — usar `bat`/`rg`/`fd`/`sd`/`eza`.
- Migraciones EF: `dotnet ef migrations add <Nombre> --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`. El contenido de la migración generada (Designer + snapshot) NO se transcribe a mano en este plan — se genera con el comando y se inspecciona.
- **Gotcha de test hermético (Api.Tests):** `IUserDataPathProvider` real resuelve a `%LOCALAPPDATA%\StockApp\`/`~/.local/share/StockApp/` — la carpeta REAL del usuario que corre los tests. `BackupsEndpoints` lee/escribe archivos ahí. Sin un fake, los tests de integración ensuciarían el filesystem real de quien los corre. Task 6 agrega `UserDataPathProviderFake` a `ApiFactory`, mismo patrón que `FingerprintMaquinaFake`/`AlmacenLicenciaEnMemoria` ya usados ahí.
- **Gotcha de TRUNCATE (Api.Tests y Infrastructure.Tests):** `ApiTestBase.LimpiarTablas()` y `PostgresRepositoryTestBase.LimpiarTablas()` truncan una lista fija de tablas. `"CorridasBackup"` se agrega a AMBAS listas en Task 1 — si se olvida, los tests de Task 6/12 tienen fuga de datos entre tests (bug real ya visto en el repo con otras entidades nuevas).
- Seguridad de `pg_dump`/`pg_restore`: la contraseña de conexión NUNCA viaja como argumento de línea de comandos (visible en `ps aux`) — se pasa por la variable de entorno `PGPASSWORD` del proceso hijo; host/puerto/usuario/base viajan como argumentos separados (`ArgumentList`), nunca interpolados en un solo string.
- El test de restaurabilidad (Task 12) **requiere los binarios `pg_dump` y `pg_restore` en el PATH de la máquina que corre los tests** (paquete `postgresql-client` en Linux), ADEMÁS de Docker. Corren en el host, no dentro del contenedor de Testcontainers.

## File Structure

**Domain:**
- `src/StockApp.Domain/Entities/CorridaBackup.cs` (crear)
- `src/StockApp.Domain/Enums/ResultadoBackup.cs` (crear)

**Application:**
- `src/StockApp.Application/Interfaces/ICorridaBackupRepository.cs` (crear)
- `src/StockApp.Application/Backups/PoliticaRetencion.cs` (crear)
- `src/StockApp.Application/Backups/IEjecutorPgDump.cs` (crear)
- `src/StockApp.Application/Backups/ServicioBackup.cs` (crear)
- `src/StockApp.Application/Backups/ServicioConsultaBackups.cs` (crear) — segunda barrera de autorización para listado/descarga/salud (defensa en profundidad, spec §6 + decisión del usuario)
- `src/StockApp.Application/Backups/BackupDtos.cs` (crear) — `CorridaBackupDto`, `SaludBackupDto`, `BackupDescargaDto`
- `src/StockApp.Application/Backups/IBackupsService.cs` (crear)
- `src/StockApp.Application/Authorization/Permisos.cs` (modificar)

**Infrastructure:**
- `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (modificar)
- `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaCorridaBackup.cs` (generada)
- `src/StockApp.Infrastructure/Repositories/CorridaBackupRepository.cs` (crear)
- `src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs` (crear)

**Api:**
- `src/StockApp.Api/Backups/BackupProgramadoService.cs` (crear)
- `src/StockApp.Api/Endpoints/BackupsEndpoints.cs` (crear)
- `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs` (modificar)
- `src/StockApp.Api/Program.cs` (modificar)
- `src/StockApp.Api/StockApp.Api.csproj` (modificar) — `InternalsVisibleTo` a `StockApp.Api.Tests`

**ApiClient:**
- `src/StockApp.ApiClient/BackupsApiClient.cs` (crear)

**Presentation:**
- `src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs` (modificar)
- `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs` (modificar)
- `src/StockApp.Presentation/App.axaml.cs` (modificar)
- `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs` (crear)
- `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml` (crear)
- `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml.cs` (crear)
- `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (modificar)
- `src/StockApp.Presentation/Views/ShellMainView.axaml` (modificar)
- `src/StockApp.Presentation/ViewModels/InicioViewModel.cs` (modificar)

**Tests:**
- `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` (modificar)
- `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` (modificar)
- `tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs` (crear)
- `tests/StockApp.Api.Tests/BackupsEndpointTests.cs` (crear)
- `tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs` (modificar)
- `tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs` (crear)
- `tests/StockApp.Application.Tests/Backups/PoliticaRetencionTests.cs` (crear)
- `tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs` (crear)
- `tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs` (crear)
- `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` (modificar)
- `tests/StockApp.Infrastructure.Tests/Repositories/CorridaBackupRepositoryTests.cs` (crear)
- `tests/StockApp.Infrastructure.Tests/Backups/RestaurabilidadBackupTests.cs` (crear)
- `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs` (modificar)
- `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs` (crear)
- `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs` (modificar)
- `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs` (crear)

---

### Task 1: Persistencia — entidad `CorridaBackup` + repositorio

**Files:**
- Create: `src/StockApp.Domain/Entities/CorridaBackup.cs`
- Create: `src/StockApp.Domain/Enums/ResultadoBackup.cs`
- Create: `src/StockApp.Application/Interfaces/ICorridaBackupRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/CorridaBackupRepository.cs`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/CorridaBackupRepositoryTests.cs`
- Generate: `src/StockApp.Infrastructure/Migrations/<timestamp>_AgregaCorridaBackup.cs`

**Interfaces:**
- Consumes: patrón de repositorio existente (`IGastoRepository`/`GastoRepository`, `IIngresoCajaRepository`/`IngresoCajaRepository`), convención de `DbSet` (`Gasto`→`Gastos`, acá `CorridaBackup`→`CorridasBackup`), convención de migraciones (`dotnet ef migrations add ... --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`).
- Produces: entidad `CorridaBackup` (`Id`, `IniciadaEn`, `FinalizadaEn`, `Resultado`, `NombreArchivo`, `TamanioBytes`, `MotivoFallo`), enum `ResultadoBackup { Exitosa, Fallida }`, `ICorridaBackupRepository` con `AgregarAsync`, `ListarTodasAsync`, `ListarExitosasAsync`, `ObtenerPorIdAsync`, `ObtenerUltimaExitosaAsync`, `EliminarAsync` — consumidos por Task 2 (tipo `CorridaBackup` en `PoliticaRetencion`), Task 4 (`ServicioBackup`), Task 5 (`BackupProgramadoService`), Task 6 (`ServicioConsultaBackups` — NO directo por `BackupsEndpoints`, ver decisión de diseño del Task 6).

**Decisión de diseño (spec §4.1 no fija el tipo exacto de `FinalizadaEn`):** `IniciadaEn`/`FinalizadaEn` son `DateTime` NO nullable — `ServicioBackup` (Task 4) solo persiste una `CorridaBackup` DESPUÉS de que el intento de dump termina (éxito o fallo), nunca un registro "en progreso". Ambos campos se setean en el mismo momento de la persistencia; no existe una fila a medio terminar en la base.

- [ ] **Step 1: Enum `ResultadoBackup`**

```csharp
// src/StockApp.Domain/Enums/ResultadoBackup.cs
namespace StockApp.Domain.Enums;

/// <summary>Resultado de una corrida de backup programado (spec Backups §4.1).</summary>
public enum ResultadoBackup
{
    Exitosa = 0,
    Fallida = 1,
}
```

- [ ] **Step 2: Entidad `CorridaBackup`**

```csharp
// src/StockApp.Domain/Entities/CorridaBackup.cs
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Metadato de una corrida del backup programado (spec Backups §4.1). Los BYTES del dump
/// nunca entran a la base (guardar el respaldo de la base dentro de la base es circular) —
/// solo se persiste el nombre de archivo, y el archivo real vive en
/// IUserDataPathProvider.GetBackupsDirectory() del servidor.
/// </summary>
public class CorridaBackup
{
    public int Id { get; set; }
    public DateTime IniciadaEn { get; set; }              // UTC
    public DateTime FinalizadaEn { get; set; }             // UTC
    public ResultadoBackup Resultado { get; set; }

    /// <summary>Nombre del archivo .dump en GetBackupsDirectory(). Null si Resultado == Fallida.</summary>
    public string? NombreArchivo { get; set; }

    /// <summary>Tamaño del archivo generado. Null si Resultado == Fallida.</summary>
    public long? TamanioBytes { get; set; }

    /// <summary>stderr de pg_dump (o el motivo del fallo). Null si Resultado == Exitosa.</summary>
    public string? MotivoFallo { get; set; }
}
```

- [ ] **Step 3: Interfaz del repositorio**

```csharp
// src/StockApp.Application/Interfaces/ICorridaBackupRepository.cs
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ICorridaBackupRepository
{
    Task<int> AgregarAsync(CorridaBackup corrida);

    /// <summary>Todas las corridas (Exitosa y Fallida), ordenadas por FinalizadaEn desc.</summary>
    Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync();

    /// <summary>Solo Resultado == Exitosa, ordenadas por FinalizadaEn desc. Input de PoliticaRetencion.</summary>
    Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync();

    Task<CorridaBackup?> ObtenerPorIdAsync(int id);

    /// <summary>La corrida Exitosa más reciente por FinalizadaEn, o null si nunca hubo una. Usado
    /// por el endpoint de salud (Task 6), el banner de InicioViewModel (Task 11) y el catch-up
    /// al arrancar BackupProgramadoService (Task 5).</summary>
    Task<CorridaBackup?> ObtenerUltimaExitosaAsync();

    /// <summary>Baja FÍSICA (no lógica): usada por la retención (Task 4) para descartar corridas
    /// viejas junto con su archivo en disco — a diferencia del resto del dominio, no tiene
    /// sentido conservar el metadato de un backup cuyo archivo ya no existe.</summary>
    Task EliminarAsync(int id);
}
```

- [ ] **Step 4: `DbSet` + configuración en `AppDbContext`**

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs
// Agregar junto a los demás DbSet (después de la línea 25, "IngresosCaja"):
    public DbSet<CorridaBackup> CorridasBackup => Set<CorridaBackup>();
```

```csharp
// Agregar dentro de OnModelCreating, al final (después del bloque "Vínculo stock ↔ finanzas",
// antes del cierre del método):

        // ── Backups programados (Entrega 1) ───────────────────────────────────
        modelBuilder.Entity<CorridaBackup>(e =>
        {
            e.HasIndex(c => c.FinalizadaEn);
        });
```

- [ ] **Step 5: Actualizar el TRUNCATE de los dos fixtures de test (gotcha conocido del repo)**

```csharp
// tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs
// Reemplazar LimpiarTablas():
    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"Usuarios\" RESTART IDENTITY CASCADE;");
    }
```

```csharp
// tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs
// Reemplazar LimpiarTablas():
    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\" RESTART IDENTITY CASCADE;");
    }
```

- [ ] **Step 6: Generar la migración e inspeccionarla**

Run: `dotnet ef migrations add AgregaCorridaBackup --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`
Expected: crea `<timestamp>_AgregaCorridaBackup.cs` + `.Designer.cs` y actualiza `AppDbContextModelSnapshot.cs`. Abrir el `.cs` generado y confirmar que `Up()` tiene un `migrationBuilder.CreateTable(name: "CorridasBackup", ...)` con las 7 columnas (`Id`, `IniciadaEn`, `FinalizadaEn`, `Resultado`, `NombreArchivo`, `TamanioBytes`, `MotivoFallo`) y un `CreateIndex` sobre `FinalizadaEn`.

- [ ] **Step 7: Implementación del repositorio**

```csharp
// src/StockApp.Infrastructure/Repositories/CorridaBackupRepository.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class CorridaBackupRepository : ICorridaBackupRepository
{
    private readonly AppDbContext _ctx;

    public CorridaBackupRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<int> AgregarAsync(CorridaBackup corrida)
    {
        _ctx.CorridasBackup.Add(corrida);
        await _ctx.SaveChangesAsync();
        return corrida.Id;
    }

    public async Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
        => await _ctx.CorridasBackup.OrderByDescending(c => c.FinalizadaEn).ToListAsync();

    public async Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
        => await _ctx.CorridasBackup
            .Where(c => c.Resultado == ResultadoBackup.Exitosa)
            .OrderByDescending(c => c.FinalizadaEn)
            .ToListAsync();

    public Task<CorridaBackup?> ObtenerPorIdAsync(int id)
        => _ctx.CorridasBackup.FirstOrDefaultAsync(c => c.Id == id);

    public Task<CorridaBackup?> ObtenerUltimaExitosaAsync()
        => _ctx.CorridasBackup
            .Where(c => c.Resultado == ResultadoBackup.Exitosa)
            .OrderByDescending(c => c.FinalizadaEn)
            .FirstOrDefaultAsync();

    public async Task EliminarAsync(int id)
    {
        var corrida = await _ctx.CorridasBackup.FirstOrDefaultAsync(c => c.Id == id);
        if (corrida is null) return;
        _ctx.CorridasBackup.Remove(corrida);
        await _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 8: Test que falla — escribirlo ANTES de confiar en el Step 7 (TDD real: correr ahora)**

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/CorridaBackupRepositoryTests.cs
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class CorridaBackupRepositoryTests : PostgresRepositoryTestBase
{
    public CorridaBackupRepositoryTests(PostgresFixture fixture) : base(fixture) { }

    private CorridaBackupRepository Crear() => new(Context);

    [Fact]
    public async Task AgregarAsync_AsignaIdYPersiste()
    {
        var repo = Crear();
        var corrida = new CorridaBackup
        {
            IniciadaEn = new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc),
            FinalizadaEn = new DateTime(2026, 7, 27, 3, 1, 0, DateTimeKind.Utc),
            Resultado = ResultadoBackup.Exitosa,
            NombreArchivo = "backup_20260727_030000.dump",
            TamanioBytes = 1024,
        };

        var id = await repo.AgregarAsync(corrida);

        Assert.True(id > 0);
    }

    [Fact]
    public async Task ListarTodasAsync_OrdenaPorFinalizadaEnDesc()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 26, 3, 0, 0, DateTimeKind.Utc)));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Fallida, new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc)));

        var todas = await repo.ListarTodasAsync();

        Assert.Equal(2, todas.Count);
        Assert.Equal(ResultadoBackup.Fallida, todas[0].Resultado);
    }

    [Fact]
    public async Task ListarExitosasAsync_ExcluyeFallidas()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Fallida, DateTime.UtcNow));

        var exitosas = await repo.ListarExitosasAsync();

        Assert.Single(exitosas);
        Assert.Equal(ResultadoBackup.Exitosa, exitosas[0].Resultado);
    }

    [Fact]
    public async Task ObtenerUltimaExitosaAsync_SinCorridas_DevuelveNull()
    {
        var repo = Crear();

        Assert.Null(await repo.ObtenerUltimaExitosaAsync());
    }

    [Fact]
    public async Task ObtenerUltimaExitosaAsync_ConVariasExitosas_DevuelveLaMasReciente()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc), "vieja.dump"));
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Utc), "nueva.dump"));

        var ultima = await repo.ObtenerUltimaExitosaAsync();

        Assert.Equal("nueva.dump", ultima!.NombreArchivo);
    }

    [Fact]
    public async Task EliminarAsync_BorraLaFilaFisicamente()
    {
        var repo = Crear();
        var id = await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));

        await repo.EliminarAsync(id);

        Assert.Null(await repo.ObtenerPorIdAsync(id));
    }

    [Fact]
    public async Task EliminarAsync_IdInexistente_NoLanzaYNoAfectaOtrasFilas()
    {
        var repo = Crear();
        await repo.AgregarAsync(Corrida(ResultadoBackup.Exitosa, DateTime.UtcNow));

        await repo.EliminarAsync(999999);

        // Assert explícito (pre-flight scan, corregido): antes este test solo probaba que NO
        // lanzaba, sin verificar ningún estado — la intención real (un id inexistente no toca
        // ninguna fila real) quedaba implícita, no escrita.
        Assert.Single(await repo.ListarTodasAsync());
    }

    private static CorridaBackup Corrida(ResultadoBackup resultado, DateTime finalizadaEn, string? nombreArchivo = null) => new()
    {
        IniciadaEn = finalizadaEn.AddMinutes(-1),
        FinalizadaEn = finalizadaEn,
        Resultado = resultado,
        NombreArchivo = resultado == ResultadoBackup.Exitosa ? (nombreArchivo ?? "backup.dump") : null,
        TamanioBytes = resultado == ResultadoBackup.Exitosa ? 1024 : null,
        MotivoFallo = resultado == ResultadoBackup.Fallida ? "pg_dump: fallo simulado" : null,
    };
}
```

- [ ] **Step 9: Correr — deben fallar en compilación antes del Step 7 si se siguió el orden estricto; con el Step 7 ya aplicado, deben pasar**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~CorridaBackupRepositoryTests"`
Expected: PASS (7/7). Requiere Docker (PostgresFixture).

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Domain/Entities/CorridaBackup.cs \
        src/StockApp.Domain/Enums/ResultadoBackup.cs \
        src/StockApp.Application/Interfaces/ICorridaBackupRepository.cs \
        src/StockApp.Infrastructure/Repositories/CorridaBackupRepository.cs \
        src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        src/StockApp.Infrastructure/Migrations/ \
        tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs \
        tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/CorridaBackupRepositoryTests.cs
git commit -m "feat(backups): agrega entidad CorridaBackup, migración y repositorio (Entrega 1)"
```

---

### Task 2: `PoliticaRetencion` (función pura)

**Files:**
- Create: `src/StockApp.Application/Backups/PoliticaRetencion.cs`
- Test: `tests/StockApp.Application.Tests/Backups/PoliticaRetencionTests.cs`

**Interfaces:**
- Consumes: `CorridaBackup` (Task 1) — solo lee `FinalizadaEn`/`Resultado`, no toca DB ni filesystem.
- Produces: `PoliticaRetencion.DeterminarABorrar(IReadOnlyList<CorridaBackup> corridasExitosas, DateTime ahoraUtc) : IReadOnlyList<CorridaBackup>` — consumido por Task 4 (`ServicioBackup.EjecutarCorridaAsync`, tras cada corrida exitosa).

**Decisión de diseño (spec §3 decisión 5 fija la POLÍTICA, no el algoritmo — se documenta acá):** retención grandfather-father-son implementada como unión de 3 conjuntos a RETENER (el resto se borra):
1. Las `CantidadRecientes = 6` corridas exitosas más recientes.
2. La última corrida exitosa de cada uno de los últimos `DiasRetencionDiaria = 7` días (bucket por `FinalizadaEn.Date`).
3. La última corrida exitosa de cada una de las últimas `SemanasRetencionSemanal = 4` semanas — bloques RODANTES de 7 días relativos a `ahoraUtc` (no semanas de calendario ISO), para que el "cruce de mes" no requiera ningún caso especial: el bloque `[ahoraUtc.Date-13, ahoraUtc.Date-7]` es válido sin importar si esos días caen en meses distintos.

- [ ] **Step 1: Test que falla — casos base (menos de 6, exactamente 6)**

```csharp
// tests/StockApp.Application.Tests/Backups/PoliticaRetencionTests.cs
using StockApp.Application.Backups;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Backups;

public class PoliticaRetencionTests
{
    private static readonly DateTime Ahora = new(2026, 7, 27, 15, 0, 0, DateTimeKind.Utc);

    private static CorridaBackup Corrida(DateTime finalizadaEn, string nombre) => new()
    {
        IniciadaEn = finalizadaEn.AddMinutes(-1),
        FinalizadaEn = finalizadaEn,
        Resultado = ResultadoBackup.Exitosa,
        NombreArchivo = nombre,
        TamanioBytes = 1024,
    };

    [Fact]
    public void DeterminarABorrar_MenosDeSeisCorridas_NoBorraNinguna()
    {
        var corridas = new List<CorridaBackup>
        {
            Corrida(Ahora.AddHours(-12), "c1"),
            Corrida(Ahora.AddHours(-24), "c2"),
            Corrida(Ahora.AddHours(-36), "c3"),
        };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_ExactamenteSeisCorridas_NoBorraNinguna()
    {
        var corridas = Enumerable.Range(0, 6)
            .Select(i => Corrida(Ahora.AddHours(-12 * i), $"c{i}"))
            .ToList();

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }
}
```

- [ ] **Step 2: Correr — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~PoliticaRetencionTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'PoliticaRetencion' no se encontró`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Backups/PoliticaRetencion.cs
using StockApp.Domain.Entities;

namespace StockApp.Application.Backups;

/// <summary>
/// Retención grandfather-father-son de backups exitosos (spec Backups §3 decisión 5): los 6
/// más recientes + el último de cada uno de los últimos 7 días + el último de cada una de las
/// últimas 4 semanas (bloques rodantes de 7 días desde ahoraUtc, no calendario). Función pura:
/// sin DB, sin filesystem, sin reloj real — ahoraUtc entra como parámetro para que los tests
/// sean 100% determinísticos.
/// </summary>
public static class PoliticaRetencion
{
    private const int CantidadRecientes = 6;
    private const int DiasRetencionDiaria = 7;
    private const int SemanasRetencionSemanal = 4;

    /// <summary>Recibe SOLO corridas Exitosas (las Fallidas no participan de la retención —
    /// nunca tuvieron archivo que conservar). Devuelve las que hay que borrar de disco + DB.</summary>
    public static IReadOnlyList<CorridaBackup> DeterminarABorrar(
        IReadOnlyList<CorridaBackup> corridasExitosas, DateTime ahoraUtc)
    {
        var ordenadas = corridasExitosas.OrderByDescending(c => c.FinalizadaEn).ToList();
        var retener = new HashSet<CorridaBackup>();

        foreach (var c in ordenadas.Take(CantidadRecientes))
            retener.Add(c);

        for (var offsetDias = 0; offsetDias < DiasRetencionDiaria; offsetDias++)
        {
            var dia = ahoraUtc.Date.AddDays(-offsetDias);
            var delDia = ordenadas.FirstOrDefault(c => c.FinalizadaEn.Date == dia);
            if (delDia is not null)
                retener.Add(delDia);
        }

        for (var semana = 0; semana < SemanasRetencionSemanal; semana++)
        {
            var hasta = ahoraUtc.Date.AddDays(-7 * semana);
            var desde = hasta.AddDays(-6);
            var deLaSemana = ordenadas.FirstOrDefault(c => c.FinalizadaEn.Date >= desde && c.FinalizadaEn.Date <= hasta);
            if (deLaSemana is not null)
                retener.Add(deLaSemana);
        }

        return ordenadas.Where(c => !retener.Contains(c)).ToList();
    }
}
```

- [ ] **Step 4: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~PoliticaRetencionTests"`
Expected: PASS (2/2).

- [ ] **Step 5: Test que falla — huecos por corridas fallidas (las fallidas ni siquiera entran a la lista de entrada) y borrado real cuando hay muchas más de 6+7+4**

```csharp
// Agregar a PoliticaRetencionTests.cs:

    [Fact]
    public void DeterminarABorrar_MuchasCorridasDistribuidasEnMeses_RetieneLaCombinacionDeLosTresConjuntosYBorraElResto()
    {
        // 90 corridas exitosas, una cada 12h, desde 45 días atrás hasta ahora — cubre de sobra
        // los 6 recientes + 7 días + 4 semanas, y dista mucho de esos rangos para el resto.
        var corridas = Enumerable.Range(0, 90)
            .Select(i => Corrida(Ahora.AddHours(-12 * i), $"c{i}"))
            .ToList();

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);
        var nombresABorrar = aBorrar.Select(c => c.NombreArchivo).ToHashSet();

        // Las 6 más recientes (i=0..5) SIEMPRE se retienen.
        Assert.DoesNotContain("c0", nombresABorrar);
        Assert.DoesNotContain("c5", nombresABorrar);

        // Una corrida de hace 40 días no cae en ningún conjunto retenido (fuera de 6 recientes,
        // fuera de los últimos 7 días, fuera de las últimas 4 semanas -bloques rodantes de
        // 7-13/14-20/21-27/28-34 días, horizonte real de 35 días-) -> se borra.
        var indiceViejo = corridas.FindIndex(c => (Ahora - c.FinalizadaEn).TotalDays >= 40);
        Assert.Contains($"c{indiceViejo}", nombresABorrar);

        // Se retuvo ALGO en el rango de la semana 3 (días 21-27 atrás) y ALGO en el rango de hace
        // 40 días no debería estar — confirma que hay borrado real, no "retener todo por las dudas".
        Assert.True(aBorrar.Count > 0);
        Assert.True(aBorrar.Count < corridas.Count);
    }

    [Fact]
    public void DeterminarABorrar_CruceDeMes_AgrupaPorBloqueRodanteSinImportarElMes()
    {
        // ahoraUtc = 2 de agosto. Corridas el 29, 30, 31 de julio y el 1, 2 de agosto: mismo
        // "día" cada una (offsetDias 0..4), deben quedar TODAS retenidas por la regla diaria
        // aunque el mes cambie a mitad del rango — sin caso especial en la implementación.
        var ahoraCruceDeMes = new DateTime(2026, 8, 2, 10, 0, 0, DateTimeKind.Utc);
        var corridas = new List<CorridaBackup>
        {
            Corrida(new DateTime(2026, 7, 29, 3, 0, 0, DateTimeKind.Utc), "jul29"),
            Corrida(new DateTime(2026, 7, 30, 3, 0, 0, DateTimeKind.Utc), "jul30"),
            Corrida(new DateTime(2026, 7, 31, 3, 0, 0, DateTimeKind.Utc), "jul31"),
            Corrida(new DateTime(2026, 8, 1, 3, 0, 0, DateTimeKind.Utc), "ago1"),
            Corrida(new DateTime(2026, 8, 2, 3, 0, 0, DateTimeKind.Utc), "ago2"),
        };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, ahoraCruceDeMes);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_SemanaActualParcial_RetieneLaUnicaCorridaDeEsaSemanaAunqueSeaUnaSola()
    {
        // Una sola corrida en toda la semana 0 (los últimos 7 días) — "semana parcial", debe
        // retenerse igual (no hace falta que la semana esté completa para que la regla aplique).
        var corridas = new List<CorridaBackup> { Corrida(Ahora.AddDays(-2), "unica") };

        var aBorrar = PoliticaRetencion.DeterminarABorrar(corridas, Ahora);

        Assert.Empty(aBorrar);
    }

    [Fact]
    public void DeterminarABorrar_ListaVacia_NoLanzaYDevuelveVacio()
    {
        var aBorrar = PoliticaRetencion.DeterminarABorrar(new List<CorridaBackup>(), Ahora);

        Assert.Empty(aBorrar);
    }
```

- [ ] **Step 6: Correr — deben pasar (la implementación del Step 3 ya cubre estos casos; si algo falla, ES el bug a corregir, no el test)**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~PoliticaRetencionTests"`
Expected: PASS (7/7).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Backups/PoliticaRetencion.cs \
        tests/StockApp.Application.Tests/Backups/PoliticaRetencionTests.cs
git commit -m "feat(backups): agrega PoliticaRetencion (grandfather-father-son) para backups (Entrega 1)"
```

---

### Task 3: `IEjecutorPgDump` + `EjecutorPgDumpProceso` (adaptador de proceso real)

**Files:**
- Create: `src/StockApp.Application/Backups/IEjecutorPgDump.cs`
- Create: `src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs`

**Interfaces:**
- Consumes: nada nuevo — `NpgsqlConnectionStringBuilder` (ya disponible transitivamente vía `Npgsql.EntityFrameworkCore.PostgreSQL` en `StockApp.Infrastructure`), `System.Diagnostics.Process` (BCL), `ILogger<EjecutorPgDumpProceso>` (`Microsoft.Extensions.Logging`, resuelto automático por el hosting de ASP.NET Core — sin registro explícito en `Program.cs`, mismo mecanismo que el resto de los `ILogger<T>` del proyecto).
- Produces: `ResultadoEjecucionPgDump(bool Exitoso, string? MensajeError)` (record), `IEjecutorPgDump.EjecutarAsync(string connectionString, string rutaDestino, CancellationToken cancellationToken) : Task<ResultadoEjecucionPgDump>` — consumido por Task 4 (`ServicioBackup`, vía fake en tests) y Task 12 (test de restaurabilidad, usando la implementación REAL, construida con `new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance)` — el 2do parámetro del constructor).

**Decisión de diseño (deviación documentada, NO diluida):** `EjecutorPgDumpProceso` es un adaptador de I/O externo real (invoca un proceso del sistema operativo) — mismo tipo de clase que `ServicioGuardadoArchivo` (`src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs`, comentario propio: "No se testea unitariamente (es UI)") o `AlmacenLicenciaArchivo`: sin lógica de negocio propia, no hay nada que un mock de `Process` verificaría que no sea "se llamó a `Process.Start`". Este Task NO tiene ciclo red-green — se implementa directo contra la firma de `IEjecutorPgDump` y se verifica con `dotnet build`. La única verificación real de que `EjecutorPgDumpProceso` funciona es el test de integración de restaurabilidad (Task 12), que lo ejercita con un `pg_dump` de verdad contra un Postgres real. **`TryKill` loguea con `_logger.LogWarning` (pre-flight scan, corregido)** — un kill fallido sin diagnóstico es exactamente el caso que necesita rastro; por eso el constructor gana `ILogger<EjecutorPgDumpProceso>` aunque el resto de la clase no lo necesite para nada más.

- [ ] **Step 1: Interfaz + record de resultado**

```csharp
// src/StockApp.Application/Backups/IEjecutorPgDump.cs
namespace StockApp.Application.Backups;

/// <summary>Resultado de una ejecución de pg_dump (spec Backups §4.2).</summary>
public sealed record ResultadoEjecucionPgDump(bool Exitoso, string? MensajeError);

/// <summary>
/// Abstracción del proceso hijo pg_dump (mismo espíritu que IFingerprintMaquina/IAlmacenLicencia
/// en Licenciamiento/: interfaz en Application, adaptador real en Infrastructure). Nunca lanza
/// por fallos esperables del proceso (binario ausente, credenciales rechazadas, timeout, disco
/// lleno) — los reporta en el resultado para que ServicioBackup los persista como CorridaBackup
/// Fallida sin interrumpir el BackgroundService.
/// </summary>
public interface IEjecutorPgDump
{
    Task<ResultadoEjecucionPgDump> EjecutarAsync(
        string connectionString, string rutaDestino, CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Implementación real**

```csharp
// src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using StockApp.Application.Backups;

namespace StockApp.Infrastructure.Backups;

/// <summary>
/// Implementación real de IEjecutorPgDump (spec §4.2): invoca pg_dump como proceso hijo,
/// formato -Fc (custom, comprimido nativo — un dump plano se infla por los bytea de adjuntos
/// ya presentes en el esquema de Finanzas). La ruta del binario se resuelve por PATH del
/// proceso, con override por configuración Backups:PgDumpPath (spec §3 decisión 1) para el caso
/// en que no esté en el PATH del servicio. Timeout configurable vía Backups:TimeoutSegundos
/// (default 300s).
///
/// SEGURIDAD: la contraseña NUNCA viaja como argumento de línea de comandos (visible en
/// `ps aux`/Task Manager de cualquier usuario de la máquina) — se pasa por la variable de
/// entorno PGPASSWORD del proceso hijo; host/puerto/usuario/base viajan como argumentos
/// separados (ArgumentList), no interpolados en un solo string (evita además el quoting del
/// shell). Ver decisión de diseño en el Task: no unit-testeada directamente (adaptador de I/O
/// real, mismo criterio que ServicioGuardadoArchivo) — cubierta por RestaurabilidadBackupTests.
/// </summary>
public sealed class EjecutorPgDumpProceso : IEjecutorPgDump
{
    private readonly string? _pgDumpPathOverride;
    private readonly TimeSpan _timeout;
    private readonly ILogger<EjecutorPgDumpProceso> _logger;

    public EjecutorPgDumpProceso(IConfiguration configuration, ILogger<EjecutorPgDumpProceso> logger)
    {
        _pgDumpPathOverride = configuration["Backups:PgDumpPath"];
        var timeoutSegundos = configuration.GetValue<int?>("Backups:TimeoutSegundos") ?? 300;
        _timeout = TimeSpan.FromSeconds(timeoutSegundos);
        _logger = logger;
    }

    public async Task<ResultadoEjecucionPgDump> EjecutarAsync(
        string connectionString, string rutaDestino, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString);
        var pgDumpPath = _pgDumpPathOverride ?? "pg_dump";

        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pgDumpPath,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password;
        proceso.StartInfo.ArgumentList.Add($"--host={builder.Host}");
        proceso.StartInfo.ArgumentList.Add($"--port={builder.Port}");
        proceso.StartInfo.ArgumentList.Add($"--username={builder.Username}");
        proceso.StartInfo.ArgumentList.Add($"--dbname={builder.Database}");
        proceso.StartInfo.ArgumentList.Add("--format=custom");
        proceso.StartInfo.ArgumentList.Add($"--file={rutaDestino}");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_timeout);

        try
        {
            proceso.Start();
            var stderrTask = proceso.StandardError.ReadToEndAsync(timeoutCts.Token);
            await proceso.WaitForExitAsync(timeoutCts.Token);
            var stderr = await stderrTask;

            if (proceso.ExitCode != 0)
            {
                return new ResultadoEjecucionPgDump(false,
                    string.IsNullOrWhiteSpace(stderr)
                        ? $"pg_dump terminó con código {proceso.ExitCode}."
                        : stderr.Trim());
            }

            return new ResultadoEjecucionPgDump(true, null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(proceso);
            return new ResultadoEjecucionPgDump(
                false, $"pg_dump excedió el timeout de {_timeout.TotalSeconds:0} segundos.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Binario no encontrado en el PATH ni en el override configurado.
            return new ResultadoEjecucionPgDump(
                false, $"No se pudo iniciar pg_dump ('{pgDumpPath}'): {ex.Message}");
        }
    }

    private void TryKill(Process proceso)
    {
        try
        {
            if (!proceso.HasExited)
                proceso.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException ex)
        {
            // El proceso ya había terminado entre el chequeo y el Kill (carrera benigna) — NO
            // es un error real, pero se loguea igual (pre-flight scan corregido): un kill
            // fallido que además siguiera vivo (el caso realmente anómalo, distinto de la
            // carrera benigna) no debe desaparecer sin dejar rastro. Va a stdout en esta
            // entrega (mismo criterio que el resto de los logs, Serilog llega en la E2).
            _logger.LogWarning(ex, "No se pudo matar el proceso pg_dump (pid {Pid}).", proceso.Id);
        }
    }
}
```

- [ ] **Step 3: Verificar que compila (sin ciclo red-green — ver decisión de diseño arriba)**

Run: `dotnet build src/StockApp.Infrastructure`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Application/Backups/IEjecutorPgDump.cs \
        src/StockApp.Infrastructure/Backups/EjecutorPgDumpProceso.cs
git commit -m "feat(backups): agrega IEjecutorPgDump y su adaptador real sobre pg_dump (Entrega 1)"
```

---

### Task 4: `ServicioBackup` (orquestación)

**Files:**
- Create: `src/StockApp.Application/Backups/ServicioBackup.cs`
- Test: `tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs`

**Interfaces:**
- Consumes: `IEjecutorPgDump.EjecutarAsync(string, string, CancellationToken) : Task<ResultadoEjecucionPgDump>` (Task 3, fake en tests), `ICorridaBackupRepository.AgregarAsync/ListarExitosasAsync/EliminarAsync` (Task 1, fake en tests), `PoliticaRetencion.DeterminarABorrar` (Task 2, invocada directo — es pura, no necesita fake), `CorridaBackup`/`ResultadoBackup` (Task 1).
- Produces: `ServicioBackup.EjecutarCorridaAsync(string connectionString, string directorioBackups, DateTime ahoraUtc, CancellationToken cancellationToken) : Task` y `ServicioBackup.LimpiarTmpHuerfanos(string directorioBackups) : void` — consumidos por Task 5 (`BackupProgramadoService`, resuelto vía scope).

**Decisión de diseño (spec no fija si `IUserDataPathProvider` se inyecta en `ServicioBackup` — se documenta acá):** `ServicioBackup` vive en `StockApp.Application`, que NO puede referenciar `StockApp.Infrastructure` (`IUserDataPathProvider` está definido en `Infrastructure/Platform/`, no en `Application/Interfaces/` — es deliberado, ver cómo lo consume `AlmacenLicenciaArchivo` en Infrastructure, nunca desde Application). Por eso `EjecutarCorridaAsync` recibe `directorioBackups` como `string` (resuelto por el LLAMADOR — `BackupProgramadoService`, que sí puede referenciar Infrastructure vía `IUserDataPathProvider.GetBackupsDirectory()`), no como una dependencia inyectada. Mismo criterio para `connectionString`: `ServicioBackup` no conoce `IConfiguration` de la API, la recibe ya resuelta.

**Decisión de diseño (retención: ¿borra la fila de DB o solo el archivo?):** ambos. Si solo se borrara el archivo y se conservara la fila, el endpoint de listado (Task 6) mostraría un backup "fantasma" cuya descarga daría 404. `EliminarAsync` (Task 1) es hard-delete a propósito.

- [ ] **Step 1: Test que falla — corrida exitosa**

```csharp
// tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Backups;

public class ServicioBackupTests
{
    private static readonly DateTime Ahora = new(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc);

    private sealed class EjecutorPgDumpFake : IEjecutorPgDump
    {
        private readonly bool _exitoso;
        private readonly string? _mensajeError;
        public bool Invocado { get; private set; }

        public EjecutorPgDumpFake(bool exitoso, string? mensajeError = null)
        {
            _exitoso = exitoso;
            _mensajeError = mensajeError;
        }

        public Task<ResultadoEjecucionPgDump> EjecutarAsync(
            string connectionString, string rutaDestino, CancellationToken cancellationToken)
        {
            Invocado = true;
            if (_exitoso)
                File.WriteAllBytes(rutaDestino, new byte[] { 1, 2, 3, 4 });
            return Task.FromResult(new ResultadoEjecucionPgDump(_exitoso, _mensajeError));
        }
    }

    private sealed class CorridaBackupRepositoryFake : ICorridaBackupRepository
    {
        public List<CorridaBackup> Corridas { get; } = new();
        private int _siguienteId = 1;

        public Task<int> AgregarAsync(CorridaBackup corrida)
        {
            corrida.Id = _siguienteId++;
            Corridas.Add(corrida);
            return Task.FromResult(corrida.Id);
        }

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)Corridas.OrderByDescending(c => c.FinalizadaEn).ToList());

        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)Corridas
                .Where(c => c.Resultado == ResultadoBackup.Exitosa)
                .OrderByDescending(c => c.FinalizadaEn).ToList());

        public Task<CorridaBackup?> ObtenerPorIdAsync(int id)
            => Task.FromResult(Corridas.FirstOrDefault(c => c.Id == id));

        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync()
            => Task.FromResult(Corridas
                .Where(c => c.Resultado == ResultadoBackup.Exitosa)
                .OrderByDescending(c => c.FinalizadaEn).FirstOrDefault());

        public Task EliminarAsync(int id)
        {
            Corridas.RemoveAll(c => c.Id == id);
            return Task.CompletedTask;
        }
    }

    private static string CrearDirectorioTemporal()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ServicioBackupTests_" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Exitosa_PersisteCorridaExitosaConArchivoYTamanio()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: true);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Exitosa, corrida.Resultado);
        Assert.NotNull(corrida.NombreArchivo);
        Assert.True(corrida.TamanioBytes > 0);
        Assert.Null(corrida.MotivoFallo);
        Assert.True(File.Exists(Path.Combine(directorio, corrida.NombreArchivo!)));
    }
}
```

- [ ] **Step 2: Correr — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioBackupTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'ServicioBackup' no se encontró`.

- [ ] **Step 3: Implementación mínima — corrida exitosa**

```csharp
// src/StockApp.Application/Backups/ServicioBackup.cs
using Microsoft.Extensions.Logging;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Backups;

/// <summary>
/// Orquesta una corrida de backup (spec §4.2): dump -> registrar corrida -> aplicar
/// PoliticaRetencion -> borrar huérfanos en disco. No conoce Process ni timers — eso vive en
/// IEjecutorPgDump y en BackupProgramadoService (Task 5) respectivamente. connectionString y
/// directorioBackups entran como parámetros (no inyectados) porque Application no puede
/// referenciar IConfiguration de la API ni IUserDataPathProvider de Infrastructure — ver
/// decisión de diseño documentada en el plan.
/// </summary>
public sealed class ServicioBackup
{
    private readonly IEjecutorPgDump _ejecutor;
    private readonly ICorridaBackupRepository _corridas;
    private readonly ILogger<ServicioBackup> _logger;

    public ServicioBackup(IEjecutorPgDump ejecutor, ICorridaBackupRepository corridas, ILogger<ServicioBackup> logger)
    {
        _ejecutor = ejecutor;
        _corridas = corridas;
        _logger = logger;
    }

    public async Task EjecutarCorridaAsync(
        string connectionString, string directorioBackups, DateTime ahoraUtc, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directorioBackups);

        var nombreArchivo = $"backup_{ahoraUtc:yyyyMMdd_HHmmss}.dump";
        var rutaFinal = Path.Combine(directorioBackups, nombreArchivo);
        var rutaTmp = rutaFinal + ".tmp";

        var resultado = await _ejecutor.EjecutarAsync(connectionString, rutaTmp, cancellationToken);

        CorridaBackup corrida;
        if (resultado.Exitoso)
        {
            File.Move(rutaTmp, rutaFinal); // rename atómico al cerrar con éxito (spec §4.3)
            corrida = new CorridaBackup
            {
                IniciadaEn = ahoraUtc,
                FinalizadaEn = DateTime.UtcNow,
                Resultado = ResultadoBackup.Exitosa,
                NombreArchivo = nombreArchivo,
                TamanioBytes = new FileInfo(rutaFinal).Length,
                MotivoFallo = null,
            };
        }
        else
        {
            BorrarSiExiste(rutaTmp);
            _logger.LogWarning("Backup fallido: {Motivo}", resultado.MensajeError);
            corrida = new CorridaBackup
            {
                IniciadaEn = ahoraUtc,
                FinalizadaEn = DateTime.UtcNow,
                Resultado = ResultadoBackup.Fallida,
                NombreArchivo = null,
                TamanioBytes = null,
                MotivoFallo = resultado.MensajeError,
            };
        }

        await _corridas.AgregarAsync(corrida);

        if (corrida.Resultado == ResultadoBackup.Exitosa)
            await AplicarRetencionAsync(directorioBackups, ahoraUtc);
    }

    private async Task AplicarRetencionAsync(string directorioBackups, DateTime ahoraUtc)
    {
        var exitosas = await _corridas.ListarExitosasAsync();
        var aBorrar = PoliticaRetencion.DeterminarABorrar(exitosas, ahoraUtc);

        foreach (var corrida in aBorrar)
        {
            if (corrida.NombreArchivo is not null)
                BorrarSiExiste(Path.Combine(directorioBackups, corrida.NombreArchivo));
            await _corridas.EliminarAsync(corrida.Id);
        }
    }

    /// <summary>Barrido de archivos .tmp huérfanos (dump interrumpido a mitad, ej. el proceso
    /// murió antes del rename atómico). Llamado al arrancar BackupProgramadoService (Task 5).</summary>
    public void LimpiarTmpHuerfanos(string directorioBackups)
    {
        if (!Directory.Exists(directorioBackups))
            return;

        foreach (var tmp in Directory.GetFiles(directorioBackups, "*.tmp"))
            BorrarSiExiste(tmp);
    }

    private void BorrarSiExiste(string ruta)
    {
        try
        {
            if (File.Exists(ruta))
                File.Delete(ruta);
        }
        catch (IOException ex)
        {
            // Mejor esfuerzo: un archivo bloqueado no debe tumbar la corrida ni el arranque.
            // LogWarning (no silencioso, pre-flight scan corregido): un .tmp/.dump que no se
            // pudo borrar es exactamente el caso que necesita diagnóstico — sin este log, un
            // huérfano que se acumula corrida tras corrida no deja ningún rastro. Va a stdout
            // en esta entrega (Serilog llega en la E2, que lo captura retroactivamente — no
            // hace falta tocar este código en la E2).
            _logger.LogWarning(ex, "No se pudo borrar el archivo '{Ruta}'.", ruta);
        }
    }
}
```

- [ ] **Step 4: Correr — debe pasar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioBackupTests"`
Expected: PASS (1/1).

- [ ] **Step 5: Test que falla — fallos del ejecutor (binario ausente / credenciales rechazadas / timeout / disco lleno son, para ServicioBackup, el MISMO camino: `IEjecutorPgDump` opaco con un mensaje distinto — la distinción real de causa vive en `EjecutorPgDumpProceso`, Task 3, no testeada unitariamente)**

```csharp
// Agregar a ServicioBackupTests.cs:

    [Theory]
    [InlineData("pg_dump: no se encontró el ejecutable")]
    [InlineData("pg_dump: password authentication failed for user \"stockapp\"")]
    [InlineData("pg_dump excedió el timeout de 300 segundos.")]
    [InlineData("pg_dump: error: could not write to output file: No space left on device")]
    public async Task EjecutarCorridaAsync_Fallida_PersisteCorridaFallidaConMotivoYSinArchivo(string motivo)
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFake(exitoso: false, mensajeError: motivo);
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        var corrida = Assert.Single(repo.Corridas);
        Assert.Equal(ResultadoBackup.Fallida, corrida.Resultado);
        Assert.Null(corrida.NombreArchivo);
        Assert.Null(corrida.TamanioBytes);
        Assert.Equal(motivo, corrida.MotivoFallo);
    }

    [Fact]
    public async Task EjecutarCorridaAsync_Fallida_NoDejaArchivoTmpHuerfano()
    {
        var directorio = CrearDirectorioTemporal();
        var ejecutor = new EjecutorPgDumpFakeQueEscribeYFalla();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(ejecutor, repo, NullLogger<ServicioBackup>.Instance);

        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        Assert.Empty(Directory.GetFiles(directorio, "*.tmp"));
    }

    private sealed class EjecutorPgDumpFakeQueEscribeYFalla : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(
            string connectionString, string rutaDestino, CancellationToken cancellationToken)
        {
            // Simula un pg_dump que alcanzó a escribir bytes parciales antes de fallar (ej.
            // disco lleno a mitad de la escritura) — exactamente el caso que .tmp existe para.
            File.WriteAllBytes(rutaDestino, new byte[] { 9, 9 });
            return Task.FromResult(new ResultadoEjecucionPgDump(false, "disco lleno a mitad de escritura"));
        }
    }

    [Fact]
    public void LimpiarTmpHuerfanos_BorraSoloArchivosTmp()
    {
        var directorio = CrearDirectorioTemporal();
        File.WriteAllBytes(Path.Combine(directorio, "huerfano1.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "huerfano2.tmp"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(directorio, "backup_valido.dump"), new byte[] { 1 });
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), NullLogger<ServicioBackup>.Instance);

        svc.LimpiarTmpHuerfanos(directorio);

        var restantes = Directory.GetFiles(directorio).Select(Path.GetFileName).ToList();
        Assert.DoesNotContain("huerfano1.tmp", restantes);
        Assert.DoesNotContain("huerfano2.tmp", restantes);
        Assert.Contains("backup_valido.dump", restantes);
    }

    [Fact]
    public void LimpiarTmpHuerfanos_DirectorioInexistente_NoLanzaYNoCreaElDirectorio()
    {
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: true), new CorridaBackupRepositoryFake(), NullLogger<ServicioBackup>.Instance);
        var directorioInexistente = Path.Combine(Path.GetTempPath(), "no-existe-" + Guid.NewGuid());

        svc.LimpiarTmpHuerfanos(directorioInexistente);

        // Assert explícito (pre-flight scan, corregido): el guard "if (!Directory.Exists(...))
        // return;" no debe tener el efecto secundario de crear el directorio al comprobarlo.
        Assert.False(Directory.Exists(directorioInexistente));
    }
```

- [ ] **Step 6: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioBackupTests"`
Expected: PASS (8/8).

- [ ] **Step 7: Test que falla — aplica retención tras una corrida exitosa (integra `PoliticaRetencion`, Task 2)**

```csharp
// Agregar a ServicioBackupTests.cs:

    [Fact]
    public async Task EjecutarCorridaAsync_TrasCorridaExitosa_AplicaRetencionYBorraLoQueSobra()
    {
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();

        // Sembrar 90 corridas exitosas viejas (cada 12h, desde hace 45 días) con su archivo real
        // en disco, para que la política tenga algo real que borrar tras la corrida de HOY.
        for (var i = 1; i <= 90; i++)
        {
            var finalizadaEn = Ahora.AddHours(-12 * i);
            var nombre = $"vieja_{i}.dump";
            File.WriteAllBytes(Path.Combine(directorio, nombre), new byte[] { 1 });
            await repo.AgregarAsync(new CorridaBackup
            {
                IniciadaEn = finalizadaEn.AddMinutes(-1), FinalizadaEn = finalizadaEn,
                Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombre, TamanioBytes = 1,
            });
        }
        var cantidadAntes = repo.Corridas.Count;

        var svc = new ServicioBackup(new EjecutorPgDumpFake(exitoso: true), repo, NullLogger<ServicioBackup>.Instance);
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        // +1 por la corrida de hoy, pero MENOS que antes porque la retención barrió las viejas.
        Assert.True(repo.Corridas.Count < cantidadAntes + 1);
        // Las filas borradas de la DB tampoco dejan archivo huérfano en disco.
        foreach (var nombreBorrado in Enumerable.Range(1, 90).Select(i => $"vieja_{i}.dump")
                     .Except(repo.Corridas.Select(c => c.NombreArchivo)))
        {
            Assert.False(File.Exists(Path.Combine(directorio, nombreBorrado)));
        }
    }

    [Fact]
    public async Task EjecutarCorridaAsync_TrasCorridaFallida_NoAplicaRetencion()
    {
        var directorio = CrearDirectorioTemporal();
        var repo = new CorridaBackupRepositoryFake();
        var svc = new ServicioBackup(
            new EjecutorPgDumpFake(exitoso: false, mensajeError: "falló"), repo, NullLogger<ServicioBackup>.Instance);

        // No debe lanzar ni intentar listar/borrar nada más allá de agregar la corrida fallida.
        await svc.EjecutarCorridaAsync("Host=x;Database=y", directorio, Ahora, CancellationToken.None);

        Assert.Single(repo.Corridas);
    }
```

- [ ] **Step 8: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioBackupTests"`
Expected: PASS (10/10).

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Application/Backups/ServicioBackup.cs \
        tests/StockApp.Application.Tests/Backups/ServicioBackupTests.cs
git commit -m "feat(backups): agrega ServicioBackup (orquestacion dump + retencion + limpieza tmp) (Entrega 1)"
```

---

### Task 5: `BackupProgramadoService` (primer `BackgroundService` del repo)

**Files:**
- Create: `src/StockApp.Api/Backups/BackupProgramadoService.cs`
- Modify: `src/StockApp.Api/StockApp.Api.csproj` — `InternalsVisibleTo` a `StockApp.Api.Tests` (mismo patrón que `StockApp.Infrastructure.csproj`/`StockApp.Presentation.csproj`)
- Modify: `src/StockApp.Api/Program.cs` — registro de `ICorridaBackupRepository`, `IEjecutorPgDump`, `ServicioBackup`, `BackupProgramadoService`
- Test: `tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs`

**Interfaces:**
- Consumes: `ServicioBackup.EjecutarCorridaAsync`/`LimpiarTmpHuerfanos` (Task 4), `ICorridaBackupRepository.ObtenerUltimaExitosaAsync` (Task 1, vía scope), `IUserDataPathProvider.GetBackupsDirectory()` (ya existe, `src/StockApp.Infrastructure/Platform/`).
- Produces: `BackupProgramadoService : BackgroundService`, con `internal Task<bool> DebeCorrerAhoraAsync()` e `internal Task EjecutarCorridaSeguraAsync(string directorio, CancellationToken stoppingToken)` — testeables directamente (no consumidos por otras Tasks, es el punto final de la cadena de orquestación del servidor).

**Decisión de diseño (spec exige mostrar el código completo del scope-per-corrida — es el primer `BackgroundService` del repo, sin patrón previo):** se usa `IServiceScopeFactory` (inyectado, NO `IServiceProvider` raíz con `CreateScope()` manual — `IServiceScopeFactory` es la abstracción idiomática para esto en ASP.NET Core) y se crea un scope NUEVO en cada llamada a `EjecutarCorridaSeguraAsync`/`DebeCorrerAhoraAsync`, nunca uno compartido para toda la vida del `BackgroundService` — `AppDbContext` (vía `ICorridaBackupRepository`) es Scoped y el hosted service es Singleton por diseño de ASP.NET Core; reusar un solo scope reusaría el MISMO `AppDbContext` durante toda la vida del proceso, acumulando el change tracker de EF Core indefinidamente.

- [ ] **Step 1: Habilitar `InternalsVisibleTo` (necesario para testear `DebeCorrerAhoraAsync`/`EjecutarCorridaSeguraAsync` sin exponerlos como API pública del `BackgroundService`)**

```xml
<!-- src/StockApp.Api/StockApp.Api.csproj -->
<!-- Agregar un nuevo ItemGroup antes del cierre de </Project>: -->
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
      <_Parameter1>StockApp.Api.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

- [ ] **Step 2: Implementación de `BackupProgramadoService`**

```csharp
// src/StockApp.Api/Backups/BackupProgramadoService.cs
using Microsoft.Extensions.Configuration;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Backups;

/// <summary>
/// PRIMER BackgroundService del repo (spec Backups §3 decisión 2: scheduler dentro de la API,
/// cero superficie de instalación nueva). Crea su PROPIO IServiceScope por corrida — ver
/// decisión de diseño del Task: AppDbContext es Scoped, este servicio es Singleton.
/// </summary>
public sealed class BackupProgramadoService : BackgroundService
{
    private static readonly TimeSpan IntervaloEntreCorridas = TimeSpan.FromHours(12);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IUserDataPathProvider _paths;
    private readonly ILogger<BackupProgramadoService> _logger;

    public BackupProgramadoService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IUserDataPathProvider paths,
        ILogger<BackupProgramadoService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _paths = paths;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directorio = _paths.GetBackupsDirectory();
        Directory.CreateDirectory(directorio);

        await using (var scopeArranque = _scopeFactory.CreateAsyncScope())
        {
            // Barrido de .tmp huérfanos (spec §4.3): un dump interrumpido a mitad (ej. la API se
            // reinició) deja un .tmp que nadie más va a limpiar.
            scopeArranque.ServiceProvider.GetRequiredService<ServicioBackup>().LimpiarTmpHuerfanos(directorio);
        }

        // Catch-up al arrancar (spec §4.2): si la última corrida exitosa tiene más de 12h (o no
        // hay ninguna), dispara enseguida en vez de esperar el primer tick del PeriodicTimer —
        // cubre el caso "servidor apagado durante la ventana".
        if (await DebeCorrerAhoraAsync())
            await EjecutarCorridaSeguraAsync(directorio, stoppingToken);

        using var timer = new PeriodicTimer(IntervaloEntreCorridas);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EjecutarCorridaSeguraAsync(directorio, stoppingToken);
        }
    }

    internal async Task<bool> DebeCorrerAhoraAsync()
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var corridas = scope.ServiceProvider.GetRequiredService<ICorridaBackupRepository>();
        var ultima = await corridas.ObtenerUltimaExitosaAsync();
        return ultima is null || DateTime.UtcNow - ultima.FinalizadaEn >= IntervaloEntreCorridas;
    }

    internal async Task EjecutarCorridaSeguraAsync(string directorio, CancellationToken stoppingToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var servicio = scope.ServiceProvider.GetRequiredService<ServicioBackup>();
            var connectionString = _configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("Falta ConnectionStrings:Default.");

            await servicio.EjecutarCorridaAsync(connectionString, directorio, DateTime.UtcNow, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Red de última resistencia: ServicioBackup ya captura los fallos esperables de
            // pg_dump y los persiste como CorridaBackup Fallida (spec §4.3); esto solo cubre un
            // error realmente inesperado (ej. la propia BD de la app caída al registrar la
            // corrida). Nunca debe tumbar el BackgroundService — el PeriodicTimer sigue vivo y
            // reintenta en la ventana siguiente.
            _logger.LogError(ex, "Corrida de backup programado falló de forma inesperada.");
        }
    }
}
```

- [ ] **Step 3: Registrar en `Program.cs`**

```csharp
// src/StockApp.Api/Program.cs
// Agregar después de "builder.Services.AddScoped<ServicioResetAdmin>();" (bloque de
// Licenciamiento, Inc 7 Fase B), antes del comentario "// JwtOptions: misma razón que...":
using StockApp.Api.Backups;
using StockApp.Application.Backups;
```

```csharp
// (mismo archivo, en el cuerpo, después de "builder.Services.AddScoped<ServicioResetAdmin>();")

// Backups programados (Entrega 1): primer BackgroundService del repo. IUserDataPathProvider ya
// está registrado Singleton más arriba (Licenciamiento). ICorridaBackupRepository/ServicioBackup
// Scoped porque usan AppDbContext; BackupProgramadoService crea su propio scope por corrida
// (ver Backups/BackupProgramadoService.cs) así que no importa que él mismo sea Singleton.
builder.Services.AddScoped<ICorridaBackupRepository, CorridaBackupRepository>();
builder.Services.AddScoped<IEjecutorPgDump, EjecutorPgDumpProceso>();
builder.Services.AddScoped<ServicioBackup>();
builder.Services.AddHostedService<BackupProgramadoService>();
```

Nota: el `using StockApp.Infrastructure.Repositories;`/`StockApp.Infrastructure.Backups;` para `CorridaBackupRepository`/`EjecutorPgDumpProceso` ya deberían resolver porque `Program.cs` ya tiene `using StockApp.Infrastructure.Repositories;` (ver el archivo real); si `StockApp.Infrastructure.Backups` no está importado, agregarlo junto a los demás `using StockApp.Infrastructure.*;` del encabezado.

- [ ] **Step 4: Test que falla — cada corrida usa un scope nuevo (test de composición, `new ServiceCollection()`)**

```csharp
// tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using StockApp.Api.Backups;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Api.Tests.Backups;

public class BackupProgramadoServiceTests
{
    private sealed class CorridaBackupRepositoryEspiaFake : ICorridaBackupRepository
    {
        private readonly List<object> _instanciasQueAgregaron;
        public CorridaBackup? UltimaExitosa { get; set; }

        public CorridaBackupRepositoryEspiaFake(List<object> instanciasQueAgregaron)
            => _instanciasQueAgregaron = instanciasQueAgregaron;

        public Task<int> AgregarAsync(CorridaBackup corrida)
        {
            _instanciasQueAgregaron.Add(this);
            return Task.FromResult(1);
        }

        public Task<IReadOnlyList<CorridaBackup>> ListarTodasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());

        public Task<IReadOnlyList<CorridaBackup>> ListarExitosasAsync()
            => Task.FromResult((IReadOnlyList<CorridaBackup>)new List<CorridaBackup>());

        public Task<CorridaBackup?> ObtenerPorIdAsync(int id) => Task.FromResult<CorridaBackup?>(null);
        public Task<CorridaBackup?> ObtenerUltimaExitosaAsync() => Task.FromResult(UltimaExitosa);
        public Task EliminarAsync(int id) => Task.CompletedTask;
    }

    private sealed class EjecutorPgDumpFake : IEjecutorPgDump
    {
        public Task<ResultadoEjecucionPgDump> EjecutarAsync(string c, string r, CancellationToken ct)
            => Task.FromResult(new ResultadoEjecucionPgDump(true, null));
    }

    private sealed class UserDataPathProviderFake : IUserDataPathProvider
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_" + Guid.NewGuid());
        public string GetDataDirectory() => _dir;
        public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
        public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
        public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
    }

    private static (BackupProgramadoService servicio, List<object> instanciasQueAgregaron, CorridaBackup? ultimaExitosaSemilla)
        Crear(CorridaBackup? ultimaExitosaSemilla = null)
    {
        var instancias = new List<object>();
        var services = new ServiceCollection();
        services.AddScoped<ICorridaBackupRepository>(_ => new CorridaBackupRepositoryEspiaFake(instancias) { UltimaExitosa = ultimaExitosaSemilla });
        services.AddScoped<IEjecutorPgDump, EjecutorPgDumpFake>();
        services.AddScoped<ServicioBackup>();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ServicioBackup>>(NullLogger<ServicioBackup>.Instance);

        var sp = services.BuildServiceProvider();
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x;Database=y" })
            .Build();

        var servicio = new BackupProgramadoService(
            sp.GetRequiredService<IServiceScopeFactory>(),
            configuracion,
            new UserDataPathProviderFake(),
            NullLogger<BackupProgramadoService>.Instance);

        return (servicio, instancias, ultimaExitosaSemilla);
    }

    [Fact]
    public async Task EjecutarCorridaSeguraAsync_DosCorridas_UsaUnScopeDistintoEnCadaUna()
    {
        var (servicio, instancias, _) = Crear();
        var directorio = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_dir_" + Guid.NewGuid());

        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);
        await servicio.EjecutarCorridaSeguraAsync(directorio, CancellationToken.None);

        Assert.Equal(2, instancias.Count);
        Assert.NotSame(instancias[0], instancias[1]);
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_SinCorridaPrevia_DevuelveTrue()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: null);

        Assert.True(await servicio.DebeCorrerAhoraAsync());
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_UltimaCorridaHaceMasDeDoceHoras_DevuelveTrue()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: new CorridaBackup
        {
            FinalizadaEn = DateTime.UtcNow.AddHours(-13), Resultado = ResultadoBackup.Exitosa, NombreArchivo = "x.dump",
        });

        Assert.True(await servicio.DebeCorrerAhoraAsync());
    }

    [Fact]
    public async Task DebeCorrerAhoraAsync_UltimaCorridaHaceMenosDeDoceHoras_DevuelveFalse()
    {
        var (servicio, _, _) = Crear(ultimaExitosaSemilla: new CorridaBackup
        {
            FinalizadaEn = DateTime.UtcNow.AddHours(-1), Resultado = ResultadoBackup.Exitosa, NombreArchivo = "x.dump",
        });

        Assert.False(await servicio.DebeCorrerAhoraAsync());
    }
}
```

- [ ] **Step 5: Correr — debe fallar en compilación hasta que exista `BackupProgramadoService` con `internal` accesible (requiere el Step 1 aplicado)**

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BackupProgramadoServiceTests"`
Expected: FAIL antes del Step 1/2, PASS (4/4) una vez aplicados.

- [ ] **Step 6: Correr de nuevo tras aplicar Steps 1-3**

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BackupProgramadoServiceTests"`
Expected: PASS (4/4).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api/Backups/BackupProgramadoService.cs \
        src/StockApp.Api/StockApp.Api.csproj \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs
git commit -m "feat(backups): agrega BackupProgramadoService, primer BackgroundService del repo (Entrega 1)"
```

---

### Task 6: Permiso `GestionarDiagnostico` + `ServicioConsultaBackups` + `BackupsEndpoints` + exención de licencia

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Create: `src/StockApp.Application/Backups/BackupDtos.cs`
- Create: `src/StockApp.Application/Backups/ServicioConsultaBackups.cs`
- Test: `tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs`
- Create: `src/StockApp.Api/Endpoints/BackupsEndpoints.cs`
- Modify: `src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Create: `tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs`
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs`
- Test: `tests/StockApp.Api.Tests/BackupsEndpointTests.cs`
- Modify: `tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs`

**Interfaces:**
- Consumes: `ICorridaBackupRepository` (Task 1, consumido AHORA por `ServicioConsultaBackups`, NO directo por `BackupsEndpoints`), `IUserDataPathProvider.GetBackupsDirectory()` (existente, resuelto por `BackupsEndpoints` y pasado como parámetro — ver decisión de diseño 2), `ICurrentSession`/`IAuthorizationService.Verificar(RolUsuario?, string)` (`StockApp.Application.Interfaces`/`StockApp.Application.Authorization`, patrón real confirmado en `RubroGastoService`/`FinanzasVistasService`/`AdjuntoService`), `AuthorizationService`/`Permisos.Todos` (derivación automática de políticas HTTP, `Program.cs`).
- Produces: `Permisos.GestionarDiagnostico = "diagnostico.gestionar"`; `CorridaBackupDto(int Id, DateTime FinalizadaEn, string Resultado, string? NombreArchivo, long? TamanioBytes, string? MotivoFallo)`; `SaludBackupDto(DateTime? UltimoExitoEn, bool Vencido, int UmbralHoras)` (el umbral viaja EN el DTO — única fuente de verdad, ver Nota de consistencia más abajo); `ServicioConsultaBackups` (sin interfaz, ver decisión de diseño 1) con `ListarAsync() : Task<IReadOnlyList<CorridaBackupDto>>`, `ObtenerSaludAsync() : Task<SaludBackupDto>`, `ResolverArchivoParaDescargaAsync(int id, string directorioBackups) : Task<(string RutaCompleta, string NombreArchivo)>`; endpoints `GET /backups`, `GET /backups/{id:int}/contenido`, `GET /backups/salud` — consumidos por Task 8 (`BackupsApiClient`, SOLO vía HTTP — el contrato externo no cambió).

**Nota de consistencia (pre-flight scan — corregido):** el umbral de "backup vencido" (26h) estaba duplicado: `ServicioConsultaBackups.UmbralAviso` (Application) y el texto hardcodeado en `InicioViewModel` (Presentation, Task 11). Si el umbral cambiara en un solo lugar, el banner que el admin lee para decidir hubiera quedado mintiendo en silencio. Fix: `UmbralAviso` sigue siendo la única CONSTANTE (vive en `ServicioConsultaBackups`), pero su valor en horas viaja en `SaludBackupDto.UmbralHoras` en cada respuesta — `InicioViewModel` (Task 11) interpola ese campo en el texto del banner en vez de hardcodear "26 horas". Propagado a Tasks 6, 8 (sin cambio de código — `BackupsApiClient` deserializa el record tal cual, gana el campo gratis), 9 (sin cambio — no construye `SaludBackupDto`), 10 y 11 (fakes/tests que construyen el DTO a mano, actualizados).

**Decisión de diseño 1 (CORRECCIÓN sobre la primera versión de este plan — decisión del usuario, no del spec original):** `BackupsEndpoints` NO lee directo de `ICorridaBackupRepository`. Estos endpoints exponen un dump COMPLETO de la base de datos — el activo más sensible del sistema — así que llevan la MISMA doble barrera que el resto de Finanzas: policy HTTP (`.RequireAuthorization`, primera barrera, corta antes de llegar al código) + `_auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico)` dentro de un service de Application (segunda barrera, defensa en profundidad — protege del caso en que alguien más adelante toque la policy HTTP sin darse cuenta de lo que deja abierto). Patrón confirmado línea por línea contra `RubroGastoService` (`ICurrentSession`/`IAuthorizationService` inyectados por constructor, `_auth.Verificar(_session.RolActual, Permisos.X)` como primera línea de cada método público) y `FinanzasVistasService` (mismo patrón, service puramente de lectura sin `IAuditLogger` — es el precedente más cercano a `ServicioConsultaBackups`, que tampoco audita lecturas). `_auth.Verificar` lanza `UnauthorizedAccessException`, que `DomainExceptionHandler` ya mapea a 403 (sin cambios ahí). Que el spec original (§6) dijera "respaldado por ICorridaBackupRepository" fue una omisión del spec, no una decisión de diseño — se corrige acá.

**Decisión de diseño 2 (frontera Application↔Infrastructure, la misma que ya resolvió `ServicioBackup` en la Task 4):** `ServicioConsultaBackups` vive en `StockApp.Application` y NO puede inyectar `IUserDataPathProvider` (vive en `Infrastructure.Platform`, no en `Application/Interfaces/` — deliberado, ver Task 4). Se evaluaron dos vías:
  - (a) **Parámetro de método** — el LLAMADOR (`BackupsEndpoints`, que sí puede referenciar `Infrastructure.Platform`) resuelve `paths.GetBackupsDirectory()` y lo pasa como argumento a `ResolverArchivoParaDescargaAsync(id, directorioBackups)`.
  - (b) Agregar una abstracción nueva en `Application/Interfaces/` (ej. `IProveedorDirectorioBackups`) que `UserDataPathProvider` (o un adaptador nuevo en Infrastructure) implemente, para poder inyectarla directo en `ServicioConsultaBackups`.

  Se elige **(a)** por coherencia: es EXACTAMENTE el mismo mecanismo que `ServicioBackup.EjecutarCorridaAsync(connectionString, directorioBackups, ...)` ya usa para el mismo problema (Task 4) — mismo tipo de dato (`string`), mismo llamador de la capa Api resolviendo la ruta real. Introducir la opción (b) para UN solo método de UN solo directorio hubiera significado dos soluciones distintas al mismo problema arquitectónico dentro del mismo módulo (`Backups/`), más confuso que una regla única "el directorio de backups siempre entra como parámetro, nunca inyectado en Application". Si en la Entrega 2 aparece un segundo caso de esto (logs), recién ahí se justificaría evaluar (b) como abstracción compartida — hoy sería especular sobre un caso de uso que no existe.

- [ ] **Step 1: Permiso nuevo**

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar la constante (después de ImportarPlanillas):
    // Backups programados (Entrega 1) — Admin-only desde el vamos, mismo criterio que
    // ImportarPlanillas: superficie sensible (backups y, en la Entrega 2, logs del servidor).
    public const string GestionarDiagnostico = "diagnostico.gestionar";
```

```csharp
// Y agregarla al array Todos (después de ImportarPlanillas):
        ImportarPlanillas,
        GestionarDiagnostico,
    ];
```

Nota: `GestionarDiagnostico` NO se agrega a `AccionesOperador` en `AuthorizationService.cs` — queda fail-closed (Admin-only) por la lógica ya existente de `Verificar`/`TienePermiso`, mismo patrón que `GestionarUsuarios`/`ImportarPlanillas`. No se modifica `AuthorizationService.cs`.

- [ ] **Step 2: DTOs de respuesta**

```csharp
// src/StockApp.Application/Backups/BackupDtos.cs
namespace StockApp.Application.Backups;

/// <summary>Fila de metadatos de una corrida de backup (spec §6, listado /backups). Resultado
/// viaja como string (ToString() del enum) — mismo criterio que FacturaCalendarioDto.Estado en
/// FinanzasVistasService, para no acoplar al desktop con el enum de Domain.</summary>
public sealed record CorridaBackupDto(
    int Id, DateTime FinalizadaEn, string Resultado, string? NombreArchivo, long? TamanioBytes, string? MotivoFallo);

/// <summary>Estado de salud del backup programado (spec §6 /backups/salud, spec §3 decisión 6
/// banner de InicioViewModel). Vencido = más de UmbralHoras sin una corrida exitosa (dos
/// ventanas de 12h + 2h de margen, para no disparar falsas alarmas por reinicios del servidor).
/// UmbralHoras viaja en el DTO (no se hardcodea el número en el texto del banner del desktop)
/// para que el umbral tenga una sola fuente de verdad: si cambia en ServicioConsultaBackups, el
/// texto que lee el admin en InicioViewModel no puede quedar mintiendo en silencio.</summary>
public sealed record SaludBackupDto(DateTime? UltimoExitoEn, bool Vencido, int UmbralHoras);
```

- [ ] **Step 3: Test que falla — `ServicioConsultaBackups`, segunda barrera de autorización (patrón calcado de `AdjuntoServiceTests`/`FinanzasVistasServiceLibroCajaTests`: `_auth.Setup(...).Throws<UnauthorizedAccessException>()` + `Verify(repo, Times.Never)`)**

```csharp
// tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Backups;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;
using Permisos = StockApp.Application.Authorization.Permisos;

namespace StockApp.Application.Tests.Backups;

public class ServicioConsultaBackupsTests
{
    private static (ServicioConsultaBackups svc, Mock<ICorridaBackupRepository> repoMock, Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo = new Mock<ICorridaBackupRepository>();
        var session = new Mock<ICurrentSession>();
        var auth = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(1, "admin", rol, null));

        var svc = new ServicioConsultaBackups(repo.Object, session.Object, auth.Object);
        return (svc, repo, auth);
    }

    private static CorridaBackup CorridaExitosa(int id = 1) => new()
    {
        Id = id, IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
        Resultado = ResultadoBackup.Exitosa, NombreArchivo = "backup.dump", TamanioBytes = 1024,
    };

    // ── ListarAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_VerificaPermisoGestionarDiagnostico()
    {
        var (svc, repo, auth) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<CorridaBackup>());

        await svc.ListarAsync();

        auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.GestionarDiagnostico), Times.Once);
    }

    [Fact]
    public async Task ListarAsync_MapeaCorridasADto()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<CorridaBackup> { CorridaExitosa() });

        var resultado = await svc.ListarAsync();

        var dto = Assert.Single(resultado);
        Assert.Equal("Exitosa", dto.Resultado);
        Assert.Equal("backup.dump", dto.NombreArchivo);
    }

    [Fact]
    public async Task ListarAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarAsync());

        repo.Verify(r => r.ListarTodasAsync(), Times.Never);
    }

    // ── ObtenerSaludAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerSaludAsync_VerificaPermiso_YSinCorridasDevuelveVencidoTrue()
    {
        var (svc, repo, auth) = Crear();
        repo.Setup(r => r.ObtenerUltimaExitosaAsync()).ReturnsAsync((CorridaBackup?)null);

        var salud = await svc.ObtenerSaludAsync();

        auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.GestionarDiagnostico), Times.Once);
        Assert.True(salud.Vencido);
        Assert.Null(salud.UltimoExitoEn);
        Assert.Equal(26, salud.UmbralHoras);
    }

    [Fact]
    public async Task ObtenerSaludAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerSaludAsync());

        repo.Verify(r => r.ObtenerUltimaExitosaAsync(), Times.Never);
    }

    // ── ResolverArchivoParaDescargaAsync ─────────────────────────────────────

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_SinPermiso_PropagaExcepcionYNuncaTocaElRepositorio()
    {
        var (svc, repo, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.GestionarDiagnostico))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ResolverArchivoParaDescargaAsync(1, "/tmp/no-importa"));

        repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_IdInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(999)).ReturnsAsync((CorridaBackup?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(999, "/tmp/no-importa"));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_CorridaFallidaSinArchivo_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        var fallida = new CorridaBackup
        {
            Id = 2, IniciadaEn = DateTime.UtcNow, FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Fallida, MotivoFallo = "simulado",
        };
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fallida);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(2, "/tmp/no-importa"));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_ArchivoNoExisteEnDisco_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(CorridaExitosa());
        var directorioVacio = Path.Combine(Path.GetTempPath(), "ServicioConsultaBackupsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directorioVacio);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ResolverArchivoParaDescargaAsync(1, directorioVacio));
    }

    [Fact]
    public async Task ResolverArchivoParaDescargaAsync_ArchivoExiste_DevuelveRutaCompletaYNombre()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(CorridaExitosa());
        var directorio = Path.Combine(Path.GetTempPath(), "ServicioConsultaBackupsTests_" + Guid.NewGuid());
        Directory.CreateDirectory(directorio);
        File.WriteAllBytes(Path.Combine(directorio, "backup.dump"), new byte[] { 1, 2, 3 });

        var (rutaCompleta, nombreArchivo) = await svc.ResolverArchivoParaDescargaAsync(1, directorio);

        Assert.Equal(Path.Combine(directorio, "backup.dump"), rutaCompleta);
        Assert.Equal("backup.dump", nombreArchivo);
    }
}
```

- [ ] **Step 4: Correr — debe fallar en compilación**

Run: `timeout 180 dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioConsultaBackupsTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'ServicioConsultaBackups' no se encontró`.

- [ ] **Step 5: Implementación de `ServicioConsultaBackups`**

```csharp
// src/StockApp.Application/Backups/ServicioConsultaBackups.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Backups;

/// <summary>
/// Segunda barrera de autorización (defensa en profundidad) para la lectura de backups —
/// mismo patrón que RubroGastoService/FinanzasVistasService: _auth.Verificar ADEMÁS de la
/// policy HTTP de BackupsEndpoints. GestionarDiagnostico protege el activo más sensible del
/// sistema (un dump COMPLETO de la base), por eso esta capa no se saltea aunque el spec
/// original no la mencionara explícitamente (decisión del usuario, ver Task 6 del plan).
/// Sin interfaz — mismo criterio que ServicioLicencia/ServicioResetAdmin (naming "Servicio+Xxx"
/// del módulo de Licenciamiento: sin abstracción, inyectado como clase concreta directo en los
/// endpoints). Puramente de lectura, sin IAuditLogger (igual que FinanzasVistasService).
/// </summary>
public sealed class ServicioConsultaBackups
{
    private static readonly TimeSpan UmbralAviso = TimeSpan.FromHours(26);

    private readonly ICorridaBackupRepository _corridas;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ServicioConsultaBackups(ICorridaBackupRepository corridas, ICurrentSession session, IAuthorizationService auth)
    {
        _corridas = corridas;
        _session = session;
        _auth = auth;
    }

    public async Task<IReadOnlyList<CorridaBackupDto>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        return (await _corridas.ListarTodasAsync()).Select(ADto).ToList();
    }

    public async Task<SaludBackupDto> ObtenerSaludAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var ultima = await _corridas.ObtenerUltimaExitosaAsync();
        var vencido = ultima is null || DateTime.UtcNow - ultima.FinalizadaEn >= UmbralAviso;
        return new SaludBackupDto(ultima?.FinalizadaEn, vencido, (int)UmbralAviso.TotalHours);
    }

    /// <summary>Resuelve la ruta completa a servir. <paramref name="directorioBackups"/> lo
    /// resuelve el LLAMADOR (BackupsEndpoints, Api) vía IUserDataPathProvider.GetBackupsDirectory()
    /// — Application no puede referenciar Infrastructure.Platform (misma frontera ya resuelta
    /// así en ServicioBackup, Task 4: parámetro de método, no inyección — ver decisión de
    /// diseño 2 del Task 6).</summary>
    public async Task<(string RutaCompleta, string NombreArchivo)> ResolverArchivoParaDescargaAsync(
        int id, string directorioBackups)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarDiagnostico);

        var corrida = await _corridas.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"No existe la corrida de backup {id}.");
        if (corrida.Resultado != ResultadoBackup.Exitosa || corrida.NombreArchivo is null)
            throw new EntidadNoEncontradaException($"La corrida de backup {id} no tiene un archivo de backup asociado.");

        var ruta = Path.Combine(directorioBackups, corrida.NombreArchivo);
        if (!File.Exists(ruta))
            throw new EntidadNoEncontradaException($"El archivo del backup {id} no está disponible en el servidor.");

        return (ruta, corrida.NombreArchivo);
    }

    private static CorridaBackupDto ADto(CorridaBackup c) => new(
        c.Id, c.FinalizadaEn, c.Resultado.ToString(), c.NombreArchivo, c.TamanioBytes, c.MotivoFallo);
}
```

- [ ] **Step 6: Correr — deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ServicioConsultaBackupsTests"`
Expected: PASS (10/10).

- [ ] **Step 7: `BackupsEndpoints` — consume `ServicioConsultaBackups`, ya NO `ICorridaBackupRepository` directo**

```csharp
// src/StockApp.Api/Endpoints/BackupsEndpoints.cs
using StockApp.Application.Authorization;
using StockApp.Application.Backups;
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Endpoints;

public static class BackupsEndpoints
{
    public static IEndpointRouteBuilder MapBackupsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/backups", async (ServicioConsultaBackups servicio) =>
            Results.Ok(await servicio.ListarAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        app.MapGet("/backups/{id:int}/contenido",
            async (int id, ServicioConsultaBackups servicio, IUserDataPathProvider paths) =>
        {
            var (rutaCompleta, nombreArchivo) =
                await servicio.ResolverArchivoParaDescargaAsync(id, paths.GetBackupsDirectory());
            return Results.File(rutaCompleta, "application/octet-stream", nombreArchivo);
        })
        .RequireAuthorization(Permisos.GestionarDiagnostico);

        app.MapGet("/backups/salud", async (ServicioConsultaBackups servicio) =>
            Results.Ok(await servicio.ObtenerSaludAsync()))
            .RequireAuthorization(Permisos.GestionarDiagnostico);

        return app;
    }
}
```

Nota: `.RequireAuthorization(Permisos.GestionarDiagnostico)` se mantiene en los 3 endpoints — es la PRIMERA barrera (HTTP), independiente de `_auth.Verificar` dentro de `ServicioConsultaBackups` (segunda barrera, Application). Ambas coexisten, ninguna reemplaza a la otra (defensa en profundidad real, no redundancia decorativa).

- [ ] **Step 8: Registrar `ServicioConsultaBackups`, mapear el endpoint y exentarlo del bloqueo por licencia**

```csharp
// src/StockApp.Api/Program.cs
// Agregar junto al registro de ICorridaBackupRepository/ServicioBackup (Task 5, bloque
// "Backups programados"):
builder.Services.AddScoped<ServicioConsultaBackups>();
```

```csharp
// (mismo archivo) Agregar junto al resto de app.MapXxxEndpoints() (después de
// "app.MapResetAdminEndpoints();", antes de "app.Run();"):
app.MapBackupsEndpoints();
```

```csharp
// src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs
// Reemplazar EsRutaPermitida:
    private static bool EsRutaPermitida(PathString path)
        => path.StartsWithSegments("/licencia")
        || path.StartsWithSegments("/auth/reset-admin")
        || path.StartsWithSegments("/backups");
```

- [ ] **Step 9: Fake de `IUserDataPathProvider` para tests (gotcha de hermeticidad — ver Global Constraints)**

```csharp
// tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs
using StockApp.Infrastructure.Platform;

namespace StockApp.Api.Tests.Fixtures;

/// <summary>
/// Reemplaza a UserDataPathProvider en los tests de integración: sin este fake,
/// BackupsEndpoints (que resuelve GetBackupsDirectory() para leer archivos) apuntaría al
/// directorio REAL de datos de usuario de la máquina que corre los tests (%LOCALAPPDATA%\StockApp\
/// / ~/.local/share/StockApp/), ensuciando el filesystem real. Directorio temporal único por
/// instancia de ApiFactory (compartida por toda la colección "Api") — mismo criterio que
/// AlmacenLicenciaEnMemoria reemplaza el almacén real de licencia.
/// </summary>
public sealed class UserDataPathProviderFake : IUserDataPathProvider
{
    private readonly string _directorioDatos =
        Path.Combine(Path.GetTempPath(), "StockAppApiTests_" + Guid.NewGuid());

    public string GetDataDirectory() => _directorioDatos;
    public string GetDatabasePath() => Path.Combine(_directorioDatos, "stockapp.db");
    public string GetBackupsDirectory() => Path.Combine(_directorioDatos, "backups");
    public string GetLicenciaPath() => Path.Combine(_directorioDatos, "licencia.lic");
}
```

```csharp
// tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs
// Dentro de ConfigureWebHost, en el bloque builder.ConfigureTestServices(services => { ... }),
// agregar la tercera línea (después del Replace de IAlmacenLicencia):
        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<IFingerprintMaquina, FingerprintMaquinaFake>());
            services.Replace(ServiceDescriptor.Singleton<IAlmacenLicencia>(
                _ => new AlmacenLicenciaEnMemoria(ClavesDePrueba.EmitirLicencia())));
            services.Replace(ServiceDescriptor.Singleton<IUserDataPathProvider>(
                _ => new UserDataPathProviderFake()));
        });
```

Nota: `IUserDataPathProvider` vive en `StockApp.Infrastructure.Platform` — agregar `using StockApp.Infrastructure.Platform;` al encabezado de `ApiFactory.cs` si no está ya.

- [ ] **Step 10: Test que falla — matriz de autorizacion + contenido real (los 401/403 aca ejercitan la PRIMERA barrera, la policy HTTP; no pueden aislar la segunda porque la primera corta antes de llegar al codigo — la segunda barrera de ServicioConsultaBackups ya quedo probada de forma independiente en el Step 3)**

```csharp
// tests/StockApp.Api.Tests/BackupsEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Backups;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Platform;
using Xunit;

namespace StockApp.Api.Tests;

public class BackupsEndpointTests : ApiTestBase
{
    public BackupsEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private async Task<CorridaBackup> SembrarCorridaExitosaConArchivoAsync(byte[]? contenido = null)
    {
        var paths = Factory.Services.GetRequiredService<IUserDataPathProvider>();
        var directorio = paths.GetBackupsDirectory();
        Directory.CreateDirectory(directorio);
        var nombreArchivo = $"backup_test_{Guid.NewGuid():N}.dump";
        File.WriteAllBytes(Path.Combine(directorio, nombreArchivo), contenido ?? new byte[] { 1, 2, 3 });

        await using var ctx = Factory.CrearContexto();
        var corrida = new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Exitosa, NombreArchivo = nombreArchivo, TamanioBytes = 3,
        };
        ctx.CorridasBackup.Add(corrida);
        await ctx.SaveChangesAsync();
        return corrida;
    }

    // ── GET /backups ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBackups_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetBackups_ConTokenOperador_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/backups");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBackups_ConTokenAdmin_Devuelve200ConLaLista()
    {
        await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<CorridaBackupDto>>();
        Assert.Single(lista!);
        Assert.Equal("Exitosa", lista![0].Resultado);
    }

    // ── GET /backups/{id}/contenido ─────────────────────────────────────────

    [Fact]
    public async Task GetContenido_ConTokenAdmin_DevuelveLosBytesRealesYElNombreDeArchivo()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync(new byte[] { 9, 8, 7, 6 });
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, bytes);
        Assert.Equal(corrida.NombreArchivo, response.Content.Headers.ContentDisposition!.FileNameStar);
    }

    [Fact]
    public async Task GetContenido_IdInexistente_Devuelve404()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/999999/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_CorridaFallidaSinArchivo_Devuelve404()
    {
        await using var ctx = Factory.CrearContexto();
        var corrida = new CorridaBackup
        {
            IniciadaEn = DateTime.UtcNow.AddMinutes(-1), FinalizadaEn = DateTime.UtcNow,
            Resultado = ResultadoBackup.Fallida, MotivoFallo = "simulado",
        };
        ctx.CorridasBackup.Add(corrida);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ArchivoRegistradoPeroBorradoDelDisco_Devuelve404()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync();
        var paths = Factory.Services.GetRequiredService<IUserDataPathProvider>();
        File.Delete(Path.Combine(paths.GetBackupsDirectory(), corrida.NombreArchivo!));

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups/1/contenido");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetContenido_ConTokenOperador_Devuelve403()
    {
        var corrida = await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync($"/backups/{corrida.Id}/contenido");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── GET /backups/salud ──────────────────────────────────────────────────

    [Fact]
    public async Task GetSalud_SinCorridas_DevuelveVencidoTrueYUltimoExitoNull()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/salud");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var salud = await response.Content.ReadFromJsonAsync<SaludBackupDto>();
        Assert.True(salud!.Vencido);
        Assert.Null(salud.UltimoExitoEn);
        Assert.Equal(26, salud.UmbralHoras);
    }

    [Fact]
    public async Task GetSalud_ConCorridaRecienteExitosa_DevuelveVencidoFalse()
    {
        await SembrarCorridaExitosaConArchivoAsync();
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/backups/salud");

        var salud = await response.Content.ReadFromJsonAsync<SaludBackupDto>();
        Assert.False(salud!.Vencido);
        Assert.NotNull(salud.UltimoExitoEn);
    }

    [Fact]
    public async Task GetSalud_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/backups/salud");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
```

- [ ] **Step 11: Correr — deben fallar (endpoints no existen todavía)**

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BackupsEndpointTests"`
Expected: FAIL — 404 en vez de los status esperados (o error de compilación si `CorridaBackupDto`/`SaludBackupDto`/`ServicioConsultaBackups` todavía no existen).

- [ ] **Step 12: Aplicar Steps 1-2 y 7-9, correr de nuevo**

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BackupsEndpointTests"`
Expected: PASS (11/11).

- [ ] **Step 13: Test que falla — exención del bloqueo por licencia**

```csharp
// tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs
// Agregar el método (junto a Bloqueada_EstadoDeLicencia_Pasa):

    [Fact]
    public async Task Bloqueada_Backups_Pasa()
    {
        Bloquear();
        var client = Factory.CreateClient();

        // Sin token -> 401 (no 423): confirma que la ruta atraviesa el middleware de licencia
        // (que la dejaría pasar) y llega hasta el de autenticación.
        var response = await client.GetAsync("/backups");

        Assert.NotEqual((HttpStatusCode)423, response.StatusCode);
    }
```

- [ ] **Step 14: Correr — debe fallar antes del Step 8, pasar después**

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BloqueoLicenciaTests"`
Expected: PASS (6/6) una vez aplicado el Step 8.

- [ ] **Step 15: Correr la suite completa de `StockApp.Api.Tests` — confirmar que no hay ripple (el TRUNCATE de Task 1 y el fake de Task 6 no rompieron nada preexistente)**

Run: `timeout 300 dotnet test tests/StockApp.Api.Tests`
Expected: PASS (todo verde).

- [ ] **Step 16: Correr la suite de `StockApp.Application.Tests` — confirmar `ServicioConsultaBackupsTests` (Step 3-6) sigue verde tras el resto de los cambios**

Run: `timeout 180 dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Backups"`
Expected: PASS (todo verde — incluye `PoliticaRetencionTests`, `ServicioBackupTests` y `ServicioConsultaBackupsTests`).

- [ ] **Step 17: Consolidar el fake de IUserDataPathProvider — eliminar el duplicado de la Task 5 (pre-flight scan, corregido)**

`BackupProgramadoServiceTests.cs` (Task 5) definio su propia clase anidada privada `UserDataPathProviderFake` ANTES de que esta Task existiera. Ahora que `tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs` (Step 9) existe y es del mismo proyecto de test (`StockApp.Api.Tests`), la copia anidada es una duplicacion real (dos implementaciones identicas de la misma interfaz fake) — se elimina, no se mantienen las dos.

```csharp
// tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs
// Eliminar la clase anidada completa (agregada en la Task 5, ya no hace falta):
//
//     private sealed class UserDataPathProviderFake : IUserDataPathProvider
//     {
//         private readonly string _dir = Path.Combine(Path.GetTempPath(), "BackupProgramadoServiceTests_" + Guid.NewGuid());
//         public string GetDataDirectory() => _dir;
//         public string GetDatabasePath() => Path.Combine(_dir, "stockapp.db");
//         public string GetBackupsDirectory() => Path.Combine(_dir, "backups");
//         public string GetLicenciaPath() => Path.Combine(_dir, "licencia.lic");
//     }
```

```csharp
// Reemplazar el using StockApp.Infrastructure.Platform; (ya sin uso en el archivo una vez
// borrada la clase anidada de arriba — era el unico lugar que referenciaba IUserDataPathProvider
// por nombre) por:
using StockApp.Api.Tests.Fixtures;
```

Nota: el resto del archivo NO cambia — `Crear()` sigue llamando `new UserDataPathProviderFake()` textualmente igual (linea sin tocar); con el `using StockApp.Api.Tests.Fixtures;` de arriba, ese `new UserDataPathProviderFake()` ahora resuelve a la clase de `Fixtures/` en vez de a la anidada (mismo nombre de tipo, elegido a proposito para que el diff sea minimo).

Run: `timeout 180 dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~BackupProgramadoServiceTests"`
Expected: PASS (4/4) — sin cambios de comportamiento, solo consolidacion del fake.

- [ ] **Step 18: Commit**

```bash
git add src/StockApp.Application/Authorization/Permisos.cs \
        src/StockApp.Application/Backups/BackupDtos.cs \
        src/StockApp.Application/Backups/ServicioConsultaBackups.cs \
        src/StockApp.Api/Endpoints/BackupsEndpoints.cs \
        src/StockApp.Api/Licenciamiento/BloqueoLicenciaMiddleware.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Application.Tests/Backups/ServicioConsultaBackupsTests.cs \
        tests/StockApp.Api.Tests/Fixtures/UserDataPathProviderFake.cs \
        tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs \
        tests/StockApp.Api.Tests/BackupsEndpointTests.cs \
        tests/StockApp.Api.Tests/Licenciamiento/BloqueoLicenciaTests.cs \
        tests/StockApp.Api.Tests/Backups/BackupProgramadoServiceTests.cs
git commit -m "feat(backups): agrega ServicioConsultaBackups (segunda barrera de autorizacion), BackupsEndpoints y exencion de licencia (Entrega 1)"
```

---

### Task 7: `GuardarBytesAsync` + `HttpClient` de descargas

**Files:**
- Modify: `src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs`
- Modify: `src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`

**Interfaces:**
- Consumes: nada nuevo — `Avalonia.Platform.Storage.IStorageProvider` (ya usado por `GuardarTextoAsync`).
- Produces: `IServicioGuardadoArchivo.GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) : Task<bool>`, `HttpClient` con key `"Descargas"` registrado vía `AddKeyedSingleton`, **timeout finito de 30 minutos** (ver decisión de diseño 2) — consumidos por Task 8 (`BackupsApiClient`) y Task 9 (`MantenimientoViewModel`).

**Decisión de diseño 1 (spec §7 no fija el mecanismo de registro del segundo `HttpClient` — se documenta acá):** el proyecto NO usa `IHttpClientFactory`/`AddHttpClient` en ningún lado (`App.axaml.cs` registra el `HttpClient` principal como singleton manual vía factory lambda). Para el segundo cliente se usa `AddKeyedSingleton<HttpClient>("Descargas", ...)` (soportado desde `Microsoft.Extensions.DependencyInjection` 8+, el proyecto ya está en 10.0.9) resuelto con `GetRequiredKeyedService<HttpClient>("Descargas")` dentro de una factory lambda para `IBackupsService` — mismo estilo manual que el resto de `ConfigurarServicios()`, sin introducir un patrón nuevo (`[FromKeyedServices]` por atributo no se usa en ningún otro lado del proyecto).

**Decisión de diseño 2 (CORRECCIÓN post-review — decisión del usuario, no del spec original):** `Timeout.InfiniteTimeSpan` (versión ya implementada de esta Task) queda REEMPLAZADO por un timeout finito de **30 minutos** (`TimeSpan.FromMinutes(30)`), Y `GuardarBytesAsync` gana un `CancellationToken` que se propaga hasta `Stream.CopyToAsync`. El problema real: con timeout infinito y sin ningún `CancellationToken` en toda la cadena (`GuardarBytesAsync` → `IBackupsService.DescargarAsync` → `DescargarCommand`), si el servidor colgaba a mitad de una descarga (proceso muerto, red cortada sin FIN, proxy que traga la conexión) el botón de esa fila quedaba trabado indefinidamente y la única salida del usuario era cerrar la aplicación. Fix de dos partes, ambas necesarias:
  - **Timeout finito holgado** (red de seguridad pasiva, sin que el usuario tenga que hacer nada): 30 minutos. Justificación del valor: el despliegue real de este sistema es LAN local (mismo criterio que el comentario ya existente sobre el timeout de 10s del `HttpClient` principal — "LAN local... el default de 100s colgaría la UI"); en una LAN, incluso un dump de varios GB baja en segundos a pocos minutos. 30 minutos cubre con margen generoso un acceso remoto por VPN, un disco del servidor con carga alta, o un dump diez veces más grande que cualquier cosa realista para esta base — y sigue garantizando que la UI se libere sola aunque el usuario nunca toque el botón de cancelar.
  - **Cancelación activa desde la UI** (vía Tasks 8-10): para el caso en que 30 minutos siguen siendo demasiada espera para el usuario, o el usuario simplemente se arrepiente.

- [ ] **Step 1: Extender la interfaz con `CancellationToken`**

```csharp
// src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs
// Agregar el método (sin tocar GuardarTextoAsync):
using System.IO;
using System.Threading;

    /// <summary>
    /// Muestra el selector de archivo y copia <paramref name="contenido"/> directo al Stream de
    /// escritura del archivo elegido — sin bufferear el contenido completo en memoria (Inc
    /// Backups: un dump de varios MB/GB no puede pasar por un solo byte[]).
    /// </summary>
    /// <param name="contenido">Stream de origen (ej. el body de la respuesta HTTP de descarga).</param>
    /// <param name="nombreSugerido">Nombre de archivo sugerido en el selector.</param>
    /// <param name="ct">Token de cancelación — propagado hasta el CopyToAsync final, para que
    /// cancelar la descarga desde la UI (Task 9) corte la copia a disco, no solo la lectura HTTP.</param>
    /// <returns><c>true</c> si el usuario eligió una ubicación y el archivo se guardó; <c>false</c> si canceló el selector.</returns>
    /// <exception cref="OperationCanceledException">Si <paramref name="ct"/> se cancela durante la copia.</exception>
    Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default);
```

- [ ] **Step 2: Implementación (sin ciclo red-green — mismo criterio ya documentado en el archivo para `GuardarTextoAsync`: "No se testea unitariamente (es UI)")**

```csharp
// src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs
// Agregar el método a la clase:
    /// <inheritdoc />
    public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default)
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult(false);

        return Dispatcher.UIThread.InvokeAsync(() => GuardarBytesInternoAsync(contenido, nombreSugerido, ct));
    }

    private static async Task<bool> GuardarBytesInternoAsync(Stream contenido, string nombreSugerido, CancellationToken ct)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;
        var storageProvider = lifetime?.MainWindow?.StorageProvider;

        if (storageProvider is null)
            return false;

        var archivo = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = nombreSugerido,
        });

        if (archivo is null)
            return false;

        await using var destino = await archivo.OpenWriteAsync();
        await contenido.CopyToAsync(destino, ct);

        return true;
    }
```

- [ ] **Step 3: Verificar que compila**

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 4: `HttpClient` de descargas en `App.axaml.cs` — timeout finito (CORRECCIÓN sobre `Timeout.InfiniteTimeSpan`)**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DESPUÉS del bloque que registra el HttpClient principal (después del cierre del
// services.AddSingleton(sp => { ... return new HttpClient(handler) { ... }; }); del HttpClient
// con Timeout de 10s), un HttpClient keyed con timeout extendido (pero FINITO) para descargas
// de dumps (pueden ser varios MB/GB, spec §7):

        // HttpClient "Descargas": mismo BaseAddress/AuthTokenHandler que el principal, pero con
        // timeout de 30 MINUTOS (no 10s como el principal, y NO infinito — ver decisión de
        // diseño 2 del Task). En una LAN (despliegue real de este sistema, mismo criterio que el
        // timeout de 10s del HttpClient principal) hasta un dump de varios GB baja en minutos;
        // 30 minutos es margen de sobra para VPN/disco lento del servidor, pero sigue siendo un
        // límite finito: si el servidor cuelga a mitad de una descarga, la UI se libera sola aun
        // si el usuario nunca toca "Cancelar" (Task 9).
        services.AddKeyedSingleton<HttpClient>("Descargas", (sp, _) =>
        {
            var baseUrl = configuration["Api:BaseUrl"];
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                baseUrl = "http://localhost:5000";
            }

            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };

            return new HttpClient(handler)
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(30),
            };
        });
```

- [ ] **Step 5: Verificar que compila**

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

Nota: `ComposicionDIApiTests.cs` (Task 8) tiene su PROPIA copia del registro de este `HttpClient` keyed "Descargas" y un test que asumía `Timeout.InfiniteTimeSpan` — se corrige en la Task 8 (Step 6 de esa Task), no acá, porque ese archivo de test todavía no existe en este punto de la ejecución secuencial del plan (Task 7 corre antes que Task 8).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/Services/IServicioGuardadoArchivo.cs \
        src/StockApp.Presentation/Services/ServicioGuardadoArchivo.cs \
        src/StockApp.Presentation/App.axaml.cs
git commit -m "fix(backups): timeout finito (30min) + CancellationToken en GuardarBytesAsync y HttpClient de descargas (Entrega 1)"
```

---

### Task 8: `IBackupsService` + `BackupsApiClient`

**Files:**
- Modify: `src/StockApp.Application/Backups/BackupDtos.cs`
- Create: `src/StockApp.Application/Backups/IBackupsService.cs`
- Modify: `src/StockApp.ApiClient/ApiErrores.cs`
- Create: `src/StockApp.ApiClient/BackupsApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Modify: `tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs`

**Interfaces:**
- Consumes: `CorridaBackupDto`/`SaludBackupDto` (Task 6, deserializados directo — "records planos, sin entidades de EF", mismo criterio que `FinanzasVistasApiClient`), `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` (`StockApp.ApiClient`, patrón establecido), `HttpClient` keyed `"Descargas"` (Task 7, timeout finito de 30 minutos).
- Produces: `IBackupsService.ListarAsync(CancellationToken ct = default)`/`DescargarAsync(int id, CancellationToken ct = default)`/`ObtenerSaludAsync(CancellationToken ct = default)`, `BackupDescargaDto` (sealed class, `IAsyncDisposable`, `NombreArchivo`+`Contenido: Stream`), `BackupsApiClient : IBackupsService` — consumidos por Task 9 (`MantenimientoViewModel`) y Task 11 (`InicioViewModel`).

**Decisión de diseño 1 (spec no fija el tipo de retorno de la descarga — se documenta acá):** `DescargarAsync` devuelve `BackupDescargaDto` con un `Stream` (NO `byte[]`) — un dump de varios GB no puede bufferearse completo en memoria (mismo motivo que `GuardarBytesAsync`, Task 7). `BackupDescargaDto` implementa `IAsyncDisposable` (dispone el `Stream`, lo que libera la conexión HTTP subyacente — comportamiento estándar de `HttpContent.ReadAsStreamAsync`); no retiene una referencia al `HttpResponseMessage` para no filtrar concerns de transporte HTTP en el contrato de `StockApp.Application`.

**Decisión de diseño 2 (CORRECCIÓN post-review — `CancellationToken` en los 3 métodos, no solo en `DescargarAsync`):** el problema reportado era específico de la descarga, pero se decide agregar `CancellationToken ct = default` a las TRES operaciones de `IBackupsService` (`ListarAsync`, `ObtenerSaludAsync` incluidas), no solo a `DescargarAsync`. Razón: consistencia con el único precedente real de una interfaz async con cancelación en este proyecto, `IVelopackGateway`/`VelopackUpdateService` (`Actualizaciones/`), que aplica `CancellationToken ct = default` de forma UNIFORME a todos sus métodos (`BuscarUpdateAsync`, `DescargarUpdateAsync`) en vez de solo al que más lo necesita — evita que la interfaz tenga "algunos métodos cancelables y otros no" sin un criterio claro para quien la lea después. Costo cero para los callers actuales: `ct = default` es un parámetro opcional, así que `CargarAsync()` en `MantenimientoViewModel` (Task 9) e `InicioViewModel` (Task 11) siguen compilando sin pasar el argumento — ninguno de los dos tiene hoy una fuente natural de cancelación para "cargar la lista"/"consultar salud" (a diferencia de la descarga, que si la tiene: el botón Cancelar de Task 9). Fuera de alcance de esta corrección: propagar el token hasta `ServicioConsultaBackups`/`BackupsEndpoints` (servidor) vía `HttpContext.RequestAborted` — no fue pedido y es una mejora independiente para un momento futuro, no de esta entrega.

**Decisión de diseño 3 (bug real, no estaba en la primera versión de este plan):** propagar el `CancellationToken` expuso que `ApiErrores.EnviarAsync` (código ya existente, compartido por los ~10 `XxxApiClient`) siempre trata una cancelación como timeout del servidor — ver Step 1 más abajo para el detalle completo del arreglo, necesario para que "cancelar" no se reporte como error.

- [ ] **Step 1: Arreglar `ApiErrores.EnviarAsync` — distinguir cancelación deliberada de timeout real (bug encontrado al propagar `CancellationToken`, CORRECCIÓN)**

`ApiErrores.EnviarAsync` (código YA EXISTENTE, compartido por los ~10 `XxxApiClient` del desktop) atrapa `TaskCanceledException` y SIEMPRE la envuelve en `ServidorNoDisponibleException` — el comentario propio del código dice literalmente "Los clients no pasan CancellationToken propio → toda cancelación es timeout". Esa asunción era CIERTA hasta este Task: `BackupsApiClient` (Step 3 de este Task) es el primer `XxxApiClient` que pasa un `CancellationToken` real y cancelable por el usuario (el botón Cancelar de Task 9). Sin este fix, cancelar una descarga en curso NO lanzaría `OperationCanceledException` hasta `MantenimientoViewModel` — lanzaría `ServidorNoDisponibleException`, indistinguible de un timeout real, y se reportaría al usuario como error (violando el requisito explícito de la corrección: "cancelar es una acción deliberada, no una falla").

```csharp
// src/StockApp.ApiClient/ApiErrores.cs
// Reemplazar el método EnviarAsync (firma y cuerpo):

    /// <summary>
    /// Ejecuta el envío HTTP convirtiendo los fallos de transporte en
    /// <see cref="ServidorNoDisponibleException"/> (conexión rechazada, DNS, timeout).
    /// <paramref name="ct"/> es OPCIONAL — los ~10 XxxApiClient que no pasan un token propio
    /// siguen con el comportamiento de siempre (toda cancelación es timeout). BackupsApiClient
    /// (Task Backups) es el primero en pasar un ct real, cancelable desde la UI: con ct
    /// explícito, una cancelación deliberada del CALLER se distingue de un timeout real y se
    /// repropaga tal cual en vez de envolverse.
    /// </summary>
    internal static async Task<HttpResponseMessage> EnviarAsync(
        Func<Task<HttpResponseMessage>> enviar, CancellationToken ct = default)
    {
        try
        {
            return await enviar();
        }
        catch (HttpRequestException ex)
        {
            throw new ServidorNoDisponibleException(ex);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Cancelación deliberada del caller (ej. BackupsApiClient.DescargarAsync con el
            // CancellationToken del botón "Cancelar" de MantenimientoViewModel) — se repropaga
            // tal cual para que el caller la distinga de una falla real del servidor. Mismo
            // criterio que EjecutorPgDumpProceso.EjecutarAsync del lado servidor (Task 3):
            // catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested).
            throw;
        }
        catch (TaskCanceledException ex)
        {
            // HttpClient.Timeout vencido, o cualquier cancelación SIN que el ct propio del
            // caller esté marcado — se sigue tratando como indisponibilidad del servidor.
            // Comportamiento SIN CAMBIOS para los ~10 ApiClients que no pasan ct (entran acá
            // con ct = default, que nunca está cancelado).
            throw new ServidorNoDisponibleException(ex);
        }
    }
```

Agregar `using System.Threading;` al encabezado de `ApiErrores.cs` si no está.

Run: `timeout 120 dotnet build src/StockApp.ApiClient`
Expected: `Build succeeded.` — los ~10 `XxxApiClient` existentes llaman `EnviarAsync(() => ...)` sin el nuevo parámetro, compilan sin cambios (parámetro opcional).

- [ ] **Step 2: `BackupDescargaDto` + `IBackupsService`**

```csharp
// src/StockApp.Application/Backups/BackupDtos.cs
// Agregar al final del archivo (después de SaludBackupDto):
using System.IO;

/// <summary>
/// Backup descargado del servidor: el contenido viaja como Stream (no byte[]) para no
/// bufferear un dump de varios MB/GB completo en memoria — GuardarBytesAsync lo copia directo
/// al Stream de escritura del archivo elegido por el usuario (spec §7). IAsyncDisposable:
/// liberar Contenido también libera la conexión HTTP subyacente.
/// </summary>
public sealed class BackupDescargaDto : IAsyncDisposable
{
    public string NombreArchivo { get; }
    public Stream Contenido { get; }

    public BackupDescargaDto(string nombreArchivo, Stream contenido)
    {
        NombreArchivo = nombreArchivo;
        Contenido = contenido;
    }

    public ValueTask DisposeAsync() => Contenido.DisposeAsync();
}
```

```csharp
// src/StockApp.Application/Backups/IBackupsService.cs
using System.Threading;

namespace StockApp.Application.Backups;

/// <summary>
/// Consumido SOLO por el desktop (implementado unicamente por BackupsApiClient) — el servidor
/// resuelve las mismas 3 operaciones via ServicioConsultaBackups (Application, Task 6), con
/// segunda barrera de autorizacion (_auth.Verificar) ademas de la policy HTTP. No hay una
/// implementacion server-side de ESTA interfaz: BackupsEndpoints inyecta ServicioConsultaBackups
/// directo, no IBackupsService — son dos tipos distintos (cliente HTTP vs. service de dominio)
/// que comparten forma pero no identidad.
///
/// Los 3 métodos aceptan CancellationToken (mismo criterio uniforme que IVelopackGateway,
/// Actualizaciones/) — ver decisión de diseño 2 del Task. DescargarAsync es el único que un
/// caller de esta entrega realmente cancela (Task 9, botón Cancelar); Listar/ObtenerSalud lo
/// exponen por consistencia de la interfaz, con default para no romper a los callers actuales.
/// </summary>
public interface IBackupsService
{
    Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default);
    Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default);
    Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: `BackupsApiClient`**

```csharp
// src/StockApp.ApiClient/BackupsApiClient.cs
using System.Net.Http.Json;
using System.Threading;
using StockApp.Application.Backups;

namespace StockApp.ApiClient;

/// <summary>IBackupsService contra /backups. Usa el HttpClient keyed "Descargas" (Task 7,
/// App.axaml.cs) — timeout extendido, necesario para el body de DescargarAsync (dump de
/// varios MB/GB), y por simplicidad se usa el mismo cliente también para Listar/Salud (bodies
/// chicos, el timeout extendido no los perjudica).</summary>
public sealed class BackupsApiClient : IBackupsService
{
    private readonly HttpClient _http;

    public BackupsApiClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default)
    {
        // ct viaja DOBLE: al propio GetAsync (corta la llamada HTTP) y a EnviarAsync (para que
        // distinga "yo lo cancelé" de "se venció el timeout" — ver Step 1 de este Task).
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("backups", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<CorridaBackupDto>>(cancellationToken: ct) ?? new();
    }

    public async Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(
            () => _http.GetAsync($"backups/{id}/contenido", HttpCompletionOption.ResponseHeadersRead, ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        var contentDisposition = response.Content.Headers.ContentDisposition;
        var nombreArchivo = contentDisposition?.FileNameStar?.Trim('"')
            ?? contentDisposition?.FileName?.Trim('"')
            ?? $"backup_{id}.dump";

        // Si el servidor ya mandó headers y se cancela DESPUÉS (mientras se lee el body), no
        // pasa por ApiErrores — ReadAsStreamAsync(ct) y el CopyToAsync de GuardarBytesAsync
        // (Task 7) lanzan OperationCanceledException directo, sin envoltorio.
        var contenido = await response.Content.ReadAsStreamAsync(ct);
        return new BackupDescargaDto(nombreArchivo, contenido);
    }

    public async Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("backups/salud", ct), ct);
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<SaludBackupDto>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener la salud del backup.");
    }
}
```

- [ ] **Step 4: Verificar que compila**

Run: `timeout 120 dotnet build src/StockApp.ApiClient`
Expected: `Build succeeded.`

- [ ] **Step 5: Registrar en `App.axaml.cs`**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar junto al resto de los ApiClients del módulo Finanzas (después de
// "services.AddTransient<IAdjuntoService, AdjuntoApiClient>();"):

        // ── Backups programados (Entrega 1) ────────────────────────────────────
        services.AddTransient<IBackupsService>(sp =>
            new BackupsApiClient(sp.GetRequiredKeyedService<HttpClient>("Descargas")));
```

Agregar `using StockApp.Application.Backups;` al encabezado si no está.

- [ ] **Step 6: Verificar que compila**

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 7: Test que falla — red de seguridad de composición DI**

```csharp
// tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs
// Agregar el using:
using StockApp.Application.Backups;
```

```csharp
// Dentro de CrearContenedor(), agregar junto al HttpClient principal (después del
// services.AddSingleton(sp => { ... new HttpClient(handler) { ... Timeout = TimeSpan.FromSeconds(10) }; });):

        services.AddKeyedSingleton<HttpClient>("Descargas", (sp, _) =>
        {
            var handler = new AuthTokenHandler(sp.GetRequiredService<ApiSession>())
            {
                InnerHandler = new SocketsHttpHandler(),
            };
            return new HttpClient(handler)
            {
                BaseAddress = new Uri("http://localhost:5000/"),
                Timeout = TimeSpan.FromMinutes(30),
            };
        });
        services.AddTransient<IBackupsService>(sp =>
            new BackupsApiClient(sp.GetRequiredKeyedService<HttpClient>("Descargas")));
```

Nota (CORRECCIÓN post-review, timeout finito en vez de infinito — ver Task 7, decisión de diseño 2): este registro espeja exactamente al de `App.axaml.cs` (Task 7, Step 4) — `Timeout.InfiniteTimeSpan` original queda reemplazado por `TimeSpan.FromMinutes(30)`.

```csharp
// Agregar el test (junto a Contenedor_Resuelve_CadaInterfazConSuApiClient — como caso aparte
// porque IBackupsService no tiene interfaz InlineData compartida con los demás, al no ir por
// el HttpClient principal):

    [Fact]
    public void Contenedor_Resuelve_IBackupsService_ConBackupsApiClient()
    {
        var sp = CrearContenedor();

        var servicio = sp.GetRequiredService<IBackupsService>();

        Assert.IsType<BackupsApiClient>(servicio);
    }

    [Fact]
    public void HttpClient_Descargas_TieneTimeoutFinitoDeTreintaMinutosYBaseAddressTerminadaEnBarra()
    {
        var sp = CrearContenedor();

        var http = sp.GetRequiredKeyedService<HttpClient>("Descargas");

        Assert.Equal(TimeSpan.FromMinutes(30), http.Timeout);
        Assert.EndsWith("/", http.BaseAddress!.ToString());
    }
```

- [ ] **Step 8: Correr - deben fallar en compilacion (AddKeyedSingleton faltante en el helper) hasta aplicar el Step anterior; luego deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ComposicionDIApiTests"`
Expected: PASS (todo verde, incluidos los 2 tests nuevos) tras aplicar el Step 7 completo.

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Application/Backups/BackupDtos.cs \
        src/StockApp.Application/Backups/IBackupsService.cs \
        src/StockApp.ApiClient/ApiErrores.cs \
        src/StockApp.ApiClient/BackupsApiClient.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/DI/ComposicionDIApiTests.cs
git commit -m "feat(backups): agrega IBackupsService y BackupsApiClient con CancellationToken, registro DI (Entrega 1)"
```

---

### Task 9: `MantenimientoViewModel` (zona Backups) + cancelación de descarga

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Administracion/FilaCorridaBackupVm.cs`
- Create: `src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs`

**Interfaces:**
- Consumes: `IBackupsService.ListarAsync(CancellationToken ct = default)/DescargarAsync(int id, CancellationToken ct = default)` (Task 8), `IServicioGuardadoArchivo.GuardarBytesAsync(Stream, string, CancellationToken ct = default)` (Task 7), `IConfirmacionService.InformarAsync(string)` (existente).
- Produces:
  - `FilaCorridaBackupVm` (`ObservableObject`): `int Id`, `DateTime FinalizadaEn`, `string Resultado`, `string? NombreArchivo`, `long? TamanioBytes`, `string? MotivoFallo` (get-only, mapeados 1:1 desde `CorridaBackupDto` en el constructor), `bool Descargando` (`[ObservableProperty]`), `internal CancellationTokenSource? Cts` — consumido por Task 10 (XAML de la fila).
  - `MantenimientoViewModel` con `ObservableCollection<FilaCorridaBackupVm> Corridas` (YA NO `ObservableCollection<CorridaBackupDto>` — ver decisión de diseño), `bool Cargando`, `Task CargarAsync()`, `[RelayCommand] DescargarAsync(FilaCorridaBackupVm fila)` (genera `DescargarCommand`), `[RelayCommand] Cancelar(FilaCorridaBackupVm fila)` (genera `CancelarCommand`) — consumidos por Task 10 (`MantenimientoView.axaml`).

**Decisión de diseño (CORRECCIÓN post-review — cancelación de descarga desde la UI, no estaba en la primera versión de este plan):** con timeout infinito y sin `CancellationToken` en toda la cadena (versión original de Tasks 7-9), si el servidor colgaba a mitad de una descarga el botón de esa fila quedaba trabado indefinidamente sin salida salvo cerrar la app. Fix de dos partes (Task 7 ya cubrió la primera — timeout finito de 30 min): esta Task agrega la cancelación ACTIVA desde la UI.
  - **`FilaCorridaBackupVm` envuelve cada `CorridaBackupDto`** — mismo criterio que los `FilaXxxVm` de Finanzas (F5d: `FilaGastoEditableVm` et al.): el DTO es un record inmutable sin estado de UI; la fila agrega el ÚNICO campo mutable que la vista necesita (`Descargando`), para que el botón "Cancelar" de CADA fila se muestre/oculte con un binding directo (`{Binding Descargando}`) SIN comparar ids entre la fila y el ViewModel padre en XAML — se evalúa y descarta explícitamente comparar `IdEnDescarga: int?` (VM padre) contra `Id` (por fila) vía `MultiBinding`/`IMultiValueConverter`: sin precedente en el repo (cero usos de `MultiBinding` en todo `StockApp.Presentation`), sería el primer uso de un patrón de binding más complejo para resolver algo que un campo booleano por fila resuelve más simple.
  - **Cancelación es POR FILA, no global**: cada `FilaCorridaBackupVm` es dueña de su propio `CancellationTokenSource` (propiedad `Cts`, `internal set` — el ViewModel padre la crea/cancela/descarta, la fila solo la retiene). Esto permite, sin código adicional, que dos descargas de filas distintas corran en paralelo sin pisarse — no se restringe a "una descarga a la vez" porque el pedido original no lo exige y agregar esa restricción sería una segunda decisión de producto no pedida.
  - **`OperationCanceledException` NO se informa como error** (requisito explícito): `DescargarAsync` tiene un `catch (OperationCanceledException)` separado del `catch (Exception ex)` genérico, vacío a propósito — cancelar es una acción DELIBERADA del usuario (`CancelarCommand`), no una falla. Ver también Task 8, decisión de diseño 3: sin el fix de `ApiErrores.EnviarAsync` ahí, esta distinción sería imposible (todo `TaskCanceledException` llegaría disfrazado de `ServidorNoDisponibleException`).
  - **`finally` deja la fila en estado consistente siempre** (éxito, error O cancelación): `Descargando = false` y `Cts` se dispone y se limpia a `null` — el requisito mínimo de testing pedido ("cancelar... deja al ViewModel en un estado consistente, sin quedar 'descargando' para siempre") se cubre exactamente acá.

- [ ] **Step 1: `FilaCorridaBackupVm` (sin ciclo red-green propio — wrapper de mapeo simple, cubierto indirectamente por el Step 3 de este Task; mismo criterio que los DTOs de `BackupDtos.cs`, que tampoco tienen test file dedicado)**

```csharp
// src/StockApp.Presentation/ViewModels/Administracion/FilaCorridaBackupVm.cs
using System;
using System.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Backups;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Envoltorio de fila sobre CorridaBackupDto (mismo criterio que los FilaXxxVm de Finanzas,
/// F5d): el DTO es un record inmutable sin estado de UI; esta fila agrega el ÚNICO campo mutable
/// que la vista necesita — Descargando — para que el botón "Cancelar" de ESA fila se muestre/
/// oculte con un binding directo, sin comparar ids entre la fila y el ViewModel padre en XAML.
/// Cts es propiedad de MantenimientoViewModel durante la descarga (la crea, la cancela, la
/// dispone) — la fila solo la retiene para que CancelarCommand la encuentre sin un diccionario
/// aparte en el padre. Cada fila es dueña de SU PROPIO CancellationTokenSource: dos descargas de
/// filas distintas pueden correr en paralelo sin pisarse (no se restringe a "una a la vez",
/// nadie lo pidió).
/// </summary>
public partial class FilaCorridaBackupVm : ObservableObject
{
    public int Id { get; }
    public DateTime FinalizadaEn { get; }
    public string Resultado { get; }
    public string? NombreArchivo { get; }
    public long? TamanioBytes { get; }
    public string? MotivoFallo { get; }

    [ObservableProperty]
    private bool _descargando;

    internal CancellationTokenSource? Cts { get; set; }

    public FilaCorridaBackupVm(CorridaBackupDto dto)
    {
        Id = dto.Id;
        FinalizadaEn = dto.FinalizadaEn;
        Resultado = dto.Resultado;
        NombreArchivo = dto.NombreArchivo;
        TamanioBytes = dto.TamanioBytes;
        MotivoFallo = dto.MotivoFallo;
    }
}
```

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 2: Test que falla — carga (colección de `FilaCorridaBackupVm`, no de `CorridaBackupDto`)**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Backups;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Administracion;

public class MantenimientoViewModelTests
{
    private static (MantenimientoViewModel vm,
                    Mock<IBackupsService> backupsMock,
                    Mock<IServicioGuardadoArchivo> guardadoMock,
                    Mock<IConfirmacionService> confirmacionMock)
        Crear(IReadOnlyList<CorridaBackupDto>? corridas = null)
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).ReturnsAsync(corridas ?? new List<CorridaBackupDto>());

        var guardadoMock = new Mock<IServicioGuardadoArchivo>();
        var confirmacionMock = new Mock<IConfirmacionService>();

        var vm = new MantenimientoViewModel(backupsMock.Object, guardadoMock.Object, confirmacionMock.Object);
        return (vm, backupsMock, guardadoMock, confirmacionMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaCorridas()
    {
        var (vm, _, _, _) = Crear(new List<CorridaBackupDto>
        {
            new(1, new DateTime(2026, 7, 27, 3, 0, 0, DateTimeKind.Utc), "Exitosa", "backup_1.dump", 1024, null),
            new(2, new DateTime(2026, 7, 26, 15, 0, 0, DateTimeKind.Utc), "Fallida", null, null, "pg_dump falló"),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Corridas.Count);
        Assert.Equal("Exitosa", vm.Corridas[0].Resultado);
        Assert.Equal(1, vm.Corridas[0].Id);
        Assert.False(vm.Corridas[0].Descargando);
    }

    [Fact]
    public async Task CargarAsync_MientrasCarga_CargandoEsTrueYLuegoFalse()
    {
        var (vm, _, _, _) = Crear();

        var tarea = vm.CargarAsync();
        await tarea;

        Assert.False(vm.Cargando);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ListarAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("servidor caído"));
        var confirmacionMock = new Mock<IConfirmacionService>();
        var vm = new MantenimientoViewModel(backupsMock.Object, new Mock<IServicioGuardadoArchivo>().Object, confirmacionMock.Object);

        await vm.CargarAsync();

        confirmacionMock.Verify(c => c.InformarAsync("servidor caído"), Times.Once);
        Assert.False(vm.Cargando);
    }
}
```

- [ ] **Step 3: Correr — debe fallar en compilación**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'MantenimientoViewModel' no se encontró`.

- [ ] **Step 4: Implementación — carga**

```csharp
// src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Backups;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Administracion;

/// <summary>
/// Zona "Backups" de la pantalla de Mantenimiento (spec Backups §7, Entrega 1 — la única zona
/// de esta entrega; la Entrega 2 agrega "Diagnóstico" al mismo VM/View, no crea uno nuevo).
/// Primera pantalla de la sección "Administración" del sidebar, Admin-only.
/// </summary>
public partial class MantenimientoViewModel : ViewModelBase
{
    private readonly IBackupsService _backups;
    private readonly IServicioGuardadoArchivo _guardado;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<FilaCorridaBackupVm> Corridas { get; } = new();

    [ObservableProperty]
    private bool _cargando;

    public MantenimientoViewModel(IBackupsService backups, IServicioGuardadoArchivo guardado, IConfirmacionService confirmacion)
    {
        _backups = backups;
        _guardado = guardado;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        Cargando = true;
        try
        {
            var lista = await _backups.ListarAsync();
            Corridas.Clear();
            foreach (var c in lista)
                Corridas.Add(new FilaCorridaBackupVm(c));
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            Cargando = false;
        }
    }
}
```

- [ ] **Step 5: Correr — deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: PASS (3/3).

- [ ] **Step 6: Test que falla — descarga (con `FilaCorridaBackupVm` como parámetro del comando, y `CancellationToken` en los mocks)**

```csharp
// Agregar a MantenimientoViewModelTests.cs:

    [Fact]
    public async Task DescargarCommand_CopiaElStreamAlServicioDeGuardadoConElNombreCorrecto()
    {
        var (vm, backupsMock, guardadoMock, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var streamFake = new MemoryStream(new byte[] { 1, 2, 3 });
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupDescargaDto("backup_5.dump", streamFake));
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "backup_5.dump", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await vm.DescargarCommand.ExecuteAsync(fila);

        guardadoMock.Verify(g => g.GuardarBytesAsync(It.IsAny<Stream>(), "backup_5.dump", It.IsAny<CancellationToken>()), Times.Once);
        Assert.False(fila.Descargando);
    }

    [Fact]
    public async Task DescargarCommand_ElServicioFalla_InformaElErrorYNoRompe()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("archivo no disponible"));

        await vm.DescargarCommand.ExecuteAsync(fila);

        confirmacionMock.Verify(c => c.InformarAsync("archivo no disponible"), Times.Once);
        Assert.False(fila.Descargando);
    }

    [Fact]
    public async Task DescargarCommand_UsuarioCancelaElSelector_NoInformaError()
    {
        var (vm, backupsMock, guardadoMock, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BackupDescargaDto("backup_5.dump", new MemoryStream()));
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await vm.DescargarCommand.ExecuteAsync(fila);

        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DescargarCommand_MientrasDescarga_FilaQuedaEnDescargando()
    {
        var (vm, backupsMock, guardadoMock, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                tcsIniciada.SetResult();
                return new BackupDescargaDto("backup_5.dump", new MemoryStream());
            });
        guardadoMock.Setup(g => g.GuardarBytesAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tarea = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;

        Assert.True(fila.Descargando);

        await tarea;
        Assert.False(fila.Descargando);
    }
```

- [ ] **Step 7: Correr — debe fallar en compilación (`DescargarCommand` no existe)**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: FAIL — `CS1061: 'MantenimientoViewModel' no contiene una definición para 'DescargarCommand'`.

- [ ] **Step 8: Implementación — comando de descarga con `CancellationTokenSource` por fila**

```csharp
// src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs
// Agregar el using System.IO; al encabezado, y el método a la clase:

    [RelayCommand]
    private async Task DescargarAsync(FilaCorridaBackupVm fila)
    {
        fila.Cts = new CancellationTokenSource();
        fila.Descargando = true;
        try
        {
            await using var descarga = await _backups.DescargarAsync(fila.Id, fila.Cts.Token);
            await _guardado.GuardarBytesAsync(descarga.Contenido, descarga.NombreArchivo, fila.Cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancelación deliberada del usuario (CancelarCommand, más abajo) — NO es un error,
            // no se informa (ver decisión de diseño del Task).
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
        finally
        {
            fila.Descargando = false;
            fila.Cts?.Dispose();
            fila.Cts = null;
        }
    }
```

- [ ] **Step 9: Correr — deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: PASS (7/7).

- [ ] **Step 10: Test que falla — `CancelarCommand` corta una descarga en curso y deja la fila consistente**

```csharp
// Agregar a MantenimientoViewModelTests.cs:

    [Fact]
    public async Task CancelarCommand_CancelaElTokenDeLaDescargaEnCurso_DejaLaFilaConsistente()
    {
        var (vm, backupsMock, _, confirmacionMock) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));
        var tcsIniciada = new TaskCompletionSource();
        var tcsNuncaCompleta = new TaskCompletionSource<BackupDescargaDto>();
        backupsMock.Setup(b => b.DescargarAsync(5, It.IsAny<CancellationToken>()))
            .Returns(async (int id, CancellationToken ct) =>
            {
                tcsIniciada.SetResult();
                // Simula el servidor colgado: nunca completa por su cuenta, solo el ct lo corta.
                return await tcsNuncaCompleta.Task.WaitAsync(ct);
            });

        var tareaDescarga = vm.DescargarCommand.ExecuteAsync(fila);
        await tcsIniciada.Task;
        Assert.True(fila.Descargando);

        vm.CancelarCommand.Execute(fila);
        await tareaDescarga;

        // Estado consistente: no queda "descargando" para siempre, y la cancelación deliberada
        // no se reporta como error (requisito explícito de la corrección).
        Assert.False(fila.Descargando);
        Assert.Null(fila.Cts);
        confirmacionMock.Verify(c => c.InformarAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void CancelarCommand_SinDescargaEnCurso_NoLanza()
    {
        var (vm, _, _, _) = Crear();
        var fila = new FilaCorridaBackupVm(new CorridaBackupDto(5, DateTime.UtcNow, "Exitosa", "backup_5.dump", 2048, null));

        vm.CancelarCommand.Execute(fila);

        Assert.False(fila.Descargando);
    }
```

- [ ] **Step 11: Correr — debe fallar en compilación (`CancelarCommand` no existe)**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: FAIL — `CS1061: 'MantenimientoViewModel' no contiene una definición para 'CancelarCommand'`.

- [ ] **Step 12: Implementación — comando de cancelación**

```csharp
// src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs
// Agregar el método a la clase (junto a DescargarAsync):

    [RelayCommand]
    private void Cancelar(FilaCorridaBackupVm fila) => fila.Cts?.Cancel();
```

- [ ] **Step 13: Correr — deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~MantenimientoViewModelTests"`
Expected: PASS (9/9).

- [ ] **Step 14: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Administracion/FilaCorridaBackupVm.cs \
        src/StockApp.Presentation/ViewModels/Administracion/MantenimientoViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Administracion/MantenimientoViewModelTests.cs
git commit -m "feat(backups): agrega MantenimientoViewModel con cancelacion de descarga por fila (Entrega 1)"
```

---

### Task 10: `MantenimientoView.axaml` + navegación (Admin-only)

**Files:**
- Create: `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml`
- Create: `src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs`

**Interfaces:**
- Consumes: `MantenimientoViewModel.Corridas: ObservableCollection<FilaCorridaBackupVm>`, `DescargarCommand`, `CancelarCommand` (Task 9), patron `DataContextChanged` a `CargarAsync()` (`InicioView.axaml.cs`), patron de nav command + `IsVisible="{Binding EsAdmin}"` (`ShellMainViewModel`/`ShellMainView.axaml`), patron de `ItemsControl` + card + `CommandParameter="{Binding}"` con `$parent[UserControl]` (`AdjuntosPanelView.axaml`).
- Produces: `ShellMainViewModel.NavMantenimientoCommand`, `MantenimientoView` registrada en el `ViewLocator`/DI — no consumidos por otras Tasks (ultima pieza de navegacion de la Entrega 1).

**Decision de diseño (CORRECCION post-review — boton Cancelar, no estaba en la primera version de este plan):** el `DataTemplate` de cada fila pasa a tipar contra `vm:FilaCorridaBackupVm` (Task 9), no `dto:CorridaBackupDto` — la fila ahora expone `Descargando`, que la vista usa para alternar "Descargar"/"Cancelar" con un binding directo (`{Binding Descargando}`/`{Binding !Descargando}`), sin comparar ids entre filas (ver decision de diseño de la Task 9). `xmlns:dto="using:StockApp.Application.Backups"` queda sin uso en el archivo y se retira.

- [ ] **Step 1: Registrar `MantenimientoViewModel` en DI**

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar junto al resto de VMs transient (despues de "services.AddTransient<InicioViewModel>();"):
        services.AddTransient<StockApp.Presentation.ViewModels.Administracion.MantenimientoViewModel>();
```

- [ ] **Step 2: `MantenimientoView.axaml` + code-behind**

```csharp
// src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Administracion;

namespace StockApp.Presentation.Views.Administracion;

public partial class MantenimientoView : UserControl
{
    public MantenimientoView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is MantenimientoViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

```xml
<!-- src/StockApp.Presentation/Views/Administracion/MantenimientoView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Administracion"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="700" d:DesignHeight="500"
             x:Class="StockApp.Presentation.Views.Administracion.MantenimientoView"
             x:DataType="vm:MantenimientoViewModel">

    <DockPanel Margin="16">

        <TextBlock DockPanel.Dock="Top"
                   Text="Mantenimiento"
                   Classes="titulo-vista"
                   Margin="0,0,0,4" />
        <TextBlock DockPanel.Dock="Top"
                   Text="Backups"
                   Classes="caption"
                   Opacity="0.6"
                   Margin="0,0,0,12" />

        <ItemsControl ItemsSource="{Binding Corridas}">
            <ItemsControl.ItemTemplate>
                <DataTemplate x:DataType="vm:FilaCorridaBackupVm">
                    <Border Classes="card" Margin="0,0,0,8">
                        <Grid ColumnDefinitions="Auto,*,Auto,Auto,Auto">
                            <i:Icon Grid.Column="0"
                                    Value="mdi-check-circle"
                                    Foreground="{DynamicResource ExitoBrush}"
                                    IsVisible="{Binding Resultado, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Exitosa}"
                                    Margin="0,0,8,0"
                                    VerticalAlignment="Center" />
                            <i:Icon Grid.Column="0"
                                    Value="mdi-close-circle"
                                    Foreground="{DynamicResource ErrorBrush}"
                                    IsVisible="{Binding Resultado, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Fallida}"
                                    Margin="0,0,8,0"
                                    VerticalAlignment="Center" />
                            <StackPanel Grid.Column="1" VerticalAlignment="Center">
                                <TextBlock Text="{Binding FinalizadaEn, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy HH:mm'}" />
                                <TextBlock Text="{Binding MotivoFallo}"
                                           Classes="caption"
                                           Foreground="{DynamicResource ErrorBrush}"
                                           IsVisible="{Binding MotivoFallo, Converter={x:Static ObjectConverters.IsNotNull}}" />
                            </StackPanel>
                            <TextBlock Grid.Column="2"
                                       Text="{Binding TamanioBytes, StringFormat={}{0:N0} B}"
                                       IsVisible="{Binding TamanioBytes, Converter={x:Static ObjectConverters.IsNotNull}}"
                                       Margin="8,0"
                                       VerticalAlignment="Center" />
                            <!-- Descargar: visible mientras esta fila NO esta descargando. IsEnabled
                                 sigue atado a NombreArchivo (corridas Fallidas no tienen archivo). -->
                            <Button Grid.Column="3"
                                    Classes="secondary"
                                    Content="Descargar"
                                    Margin="8,0,0,0"
                                    IsVisible="{Binding !Descargando}"
                                    IsEnabled="{Binding NombreArchivo, Converter={x:Static ObjectConverters.IsNotNull}}"
                                    Command="{Binding $parent[UserControl].((vm:MantenimientoViewModel)DataContext).DescargarCommand}"
                                    CommandParameter="{Binding}" />
                            <!-- Cancelar: visible SOLO mientras esta fila puntual esta descargando
                                 (Descargando es por-fila, Task 9 — no hay comparacion de ids acá). -->
                            <Button Grid.Column="4"
                                    Classes="secondary"
                                    Content="Cancelar"
                                    Margin="8,0,0,0"
                                    IsVisible="{Binding Descargando}"
                                    Command="{Binding $parent[UserControl].((vm:MantenimientoViewModel)DataContext).CancelarCommand}"
                                    CommandParameter="{Binding}" />
                        </Grid>
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

    </DockPanel>

</UserControl>
```

Nota: `Resultado` es `string` ("Exitosa"/"Fallida") — dos `<i:Icon>` condicionados por `ObjectConverters.Equal` (mismo mecanismo que `ShellMainView` usa para resaltar la sección activa del sidebar vía `Classes.active`), sin necesidad de un `IValueConverter` dedicado para dos valores. `{Binding !Descargando}` es sintaxis de negación ya usada en el repo (`ProductoFormView.axaml`, `CategoriaListView.axaml`, etc. — `IsVisible="{Binding !Activo}"`), no un patrón nuevo.

- [ ] **Step 3: Comando de navegacion en `ShellMainViewModel`**

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar el using:
using StockApp.Presentation.ViewModels.Administracion;
```

```csharp
// Agregar el comando (despues de NavAuditoriaLog, al final del bloque de Reportes):

    // ── Administracion (Entrega 1 Backups): solo Admin ────────────────────────

    [RelayCommand]
    private void NavMantenimiento()
    {
        SeccionActiva = "Mantenimiento";
        _navigation.Navegar<MantenimientoViewModel>();
    }
```

- [ ] **Step 4: Entrada en el sidebar**

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<!-- Agregar DESPUES del bloque "Reportes" (despues del ultimo Button, NavAuditoriaLogCommand),
     antes del cierre </StackPanel> -->

                <!-- Administracion: solo Admin (Entrega 1 Backups) -->
                <TextBlock Text="Administración"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           IsVisible="{Binding EsAdmin}"
                           Opacity="0.6" />

                <Button Command="{Binding NavMantenimientoCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Mantenimiento}"
                        HorizontalAlignment="Stretch"
                        IsVisible="{Binding EsAdmin}">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-database-cog" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Mantenimiento" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>
```

- [ ] **Step 5: Verificar que compila (Avalonia compila XAML como parte del build)**

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 6: Test headless que falla — la vista real carga, el candado de "Descargar" respeta `NombreArchivo`, y "Descargar"/"Cancelar" alternan con `Descargando`**

```csharp
// tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Backups;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Administracion;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Monta MantenimientoView real (no un banco de pruebas aislado) con un VM real + fakes hechos
/// a mano (mismo patron que MovimientoFormControlValidacionTests.cs) para confirmar que la
/// carga via DataContextChanged funciona end-to-end y que el boton "Descargar" queda
/// deshabilitado para una corrida Fallida (NombreArchivo null). ConfirmacionServiceFake ya
/// existe en MovimientoRegistroFakes.cs (mismo proyecto, internal) — se reutiliza tal cual.
/// </summary>
public class MantenimientoViewTests
{
    private sealed class BackupsServiceFake : IBackupsService
    {
        private readonly IReadOnlyList<CorridaBackupDto> _corridas;
        public BackupsServiceFake(IReadOnlyList<CorridaBackupDto> corridas) => _corridas = corridas;

        public Task<IReadOnlyList<CorridaBackupDto>> ListarAsync(CancellationToken ct = default) => Task.FromResult(_corridas);
        public Task<BackupDescargaDto> DescargarAsync(int id, CancellationToken ct = default) =>
            Task.FromResult(new BackupDescargaDto("x.dump", new MemoryStream()));
        public Task<SaludBackupDto> ObtenerSaludAsync(CancellationToken ct = default) => Task.FromResult(new SaludBackupDto(null, true, 26));
    }

    private sealed class ServicioGuardadoArchivoFake : IServicioGuardadoArchivo
    {
        public Task<bool> GuardarTextoAsync(string contenido, string nombreSugerido) => Task.FromResult(true);
        public Task<bool> GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default) => Task.FromResult(true);
    }

    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:admin="clr-namespace:StockApp.Presentation.Views.Administracion;assembly=StockApp.Presentation"
                Width="700" Height="500">
            <admin:MantenimientoView />
        </Window>
        """;

    private static (Window Window, MantenimientoViewModel Vm) Montar(IReadOnlyList<CorridaBackupDto> corridas)
    {
        var vm = new MantenimientoViewModel(
            new BackupsServiceFake(corridas), new ServicioGuardadoArchivoFake(), new ConfirmacionServiceFake());

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs(); // segunda pasada: deja completar el await CargarAsync() del DataContextChanged

        return (window, vm);
    }

    [AvaloniaFact]
    public void Montar_ConCorridas_MuestraLasFilasCargadas()
    {
        var corridas = new List<CorridaBackupDto>
        {
            new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null),
            new(2, DateTime.UtcNow.AddHours(-12), "Fallida", null, null, "pg_dump fallo"),
        };

        var (window, vm) = Montar(corridas);

        Assert.Equal(2, vm.Corridas.Count);
        var botones = window.GetVisualDescendants().OfType<Button>()
            .Where(b => b.Content as string == "Descargar").ToList();
        Assert.Equal(2, botones.Count);
    }

    [AvaloniaFact]
    public void Montar_CorridaFallidaSinArchivo_BotonDescargarQuedaDeshabilitado()
    {
        var corridas = new List<CorridaBackupDto> { new(2, DateTime.UtcNow, "Fallida", null, null, "pg_dump fallo") };

        var (window, _) = Montar(corridas);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        Assert.False(boton.IsEnabled);
    }

    [AvaloniaFact]
    public void Montar_CorridaExitosa_BotonDescargarQuedaHabilitado()
    {
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };

        var (window, _) = Montar(corridas);

        var boton = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        Assert.True(boton.IsEnabled);
    }

    [AvaloniaFact]
    public void Montar_FilaSinDescargaEnCurso_MuestraDescargarYOcultaCancelar()
    {
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };

        var (window, _) = Montar(corridas);

        var botonDescargar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        var botonCancelar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancelar");
        Assert.True(botonDescargar.IsVisible);
        Assert.False(botonCancelar.IsVisible);
    }

    [AvaloniaFact]
    public void Montar_FilaConDescargaEnCurso_MuestraCancelarYOcultaDescargar()
    {
        // Setea Descargando directo sobre la fila (ObservableObject) en vez de orquestar una
        // descarga async real dentro del dispatcher headless — la coordinacion async de
        // DescargarCommand/CancelarCommand ya esta cubierta a nivel logico por los tests de
        // MantenimientoViewModelTests (Task 9); aca solo se verifica el WIRING de XAML (que
        // Descargando efectivamente alterna que boton se ve), que es lo que le toca a esta Task.
        var corridas = new List<CorridaBackupDto> { new(1, DateTime.UtcNow, "Exitosa", "backup_1.dump", 1024, null) };
        var (window, vm) = Montar(corridas);

        vm.Corridas[0].Descargando = true;
        Dispatcher.UIThread.RunJobs();

        var botonDescargar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Descargar");
        var botonCancelar = window.GetVisualDescendants().OfType<Button>().First(b => b.Content as string == "Cancelar");
        Assert.False(botonDescargar.IsVisible);
        Assert.True(botonCancelar.IsVisible);
    }
}
```

- [ ] **Step 7: Correr — FOREGROUND, con `timeout`, NUNCA en background (Global Constraints)**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~MantenimientoViewTests"`
Expected: PASS (5/5). Si da timeout, reportar y seguir — no reintentar en loop.

- [ ] **Step 8: Verificacion organica** — levantar la API + el desktop reales (WSLg/Windows), loguearse como Admin, entrar a "Mantenimiento" en el sidebar, confirmar que la lista carga y que "Descargar" abre el selector de archivo nativo.

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Presentation/Views/Administracion/ \
        src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.UiTests/MantenimientoViewTests.cs
git commit -m "feat(backups): agrega MantenimientoView y navegacion Admin-only (Entrega 1)"
```

---

### Task 11: Banner de salud en `InicioViewModel`

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/InicioViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs`

**Interfaces:**
- Consumes: `IBackupsService.ObtenerSaludAsync(CancellationToken ct = default) : Task<SaludBackupDto>` (Task 8) — `InicioViewModel` NO pasa el token (sin fuente de cancelación propia; `ct` queda en su default), sin ripple a los tests existentes de esta Task.
- Produces: `InicioViewModel` gana el 4to parámetro de constructor `IBackupsService backups`, propiedades `bool MostrarAvisoBackup`, `string? TextoAvisoBackup` — no consumidos por otras Tasks (última pieza funcional de la Entrega 1). `InicioView.axaml.cs` NO se modifica: ya llama `await vm.CargarAsync()` en `DataContextChanged`, y este Task extiende `CargarAsync()`, no agrega un método nuevo.

**Decisión de diseño (spec §3 decisión 6, "Visible solo para Admin" — el mecanismo de guardado no está en el spec, se documenta acá):** la consulta a `ObtenerSaludAsync()` solo se dispara si `EsAdmin` — `Permisos.GestionarDiagnostico` es Admin-only fail-closed (Task 6), así que un Operador llamando a `/backups/salud` recibiría 403. Evitar la llamada innecesaria (y su excepción esperable) para Operador es más limpio que dejar que el catch-silencioso de `CargarAsync` la absorba.

- [ ] **Step 1: Test que falla — banner visible para Admin con backup vencido**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs
// Agregar el using:
using StockApp.Application.Backups;
```

```csharp
// Reemplazar el helper Crear() (firma y cuerpo actuales):
    private static (InicioViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock,
                     Mock<IFinanzasVistasService> finanzasMock, Mock<IBackupsService> backupsMock)
        Crear(UsuarioSesion usuario, CalendarioPagosDto? calendario = null, SaludBackupDto? salud = null)
    {
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.UsuarioActual).Returns(usuario);
        sessionMock.Setup(s => s.RolActual).Returns(usuario.Rol);

        var navMock = new Mock<INavigationService>();
        var finanzasMock = new Mock<IFinanzasVistasService>();
        finanzasMock.Setup(f => f.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ObtenerSaludAsync()).ReturnsAsync(salud ?? new SaludBackupDto(DateTime.UtcNow, false, 26));

        var vm = new InicioViewModel(sessionMock.Object, navMock.Object, finanzasMock.Object, backupsMock.Object);
        return (vm, sessionMock, navMock, finanzasMock, backupsMock);
    }
```

Nota (self-review — único call-site de `new InicioViewModel(` en el archivo, confirmado por búsqueda previa: no hay ripple a otros archivos de test).

```csharp
// Agregar los tests nuevos, junto a los de CargarAsync_ConFacturasVencidas_MuestraElAviso:

    [Fact]
    public async Task CargarAsync_AdminConBackupVencido_MuestraAvisoBackup()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(DateTime.UtcNow.AddHours(-30), true, 26));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoBackup);
        Assert.NotNull(vm.TextoAvisoBackup);
        Assert.Contains("26", vm.TextoAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_AdminConBackupAlDia_NoMuestraAvisoBackup()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var (vm, _, _, _, _) = Crear(usuario, salud: new SaludBackupDto(DateTime.UtcNow, false, 26));

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_Operador_NuncaConsultaSaludDeBackup()
    {
        var usuario = new UsuarioSesion(2, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _, backupsMock) = Crear(usuario);

        await vm.CargarAsync();

        backupsMock.Verify(b => b.ObtenerSaludAsync(), Times.Never);
        Assert.False(vm.MostrarAvisoBackup);
    }

    [Fact]
    public async Task CargarAsync_ServicioDeBackupFalla_NoRompeYOcultaElAviso()
    {
        var usuario = new UsuarioSesion(1, "admin", RolUsuario.Admin, "Administrador General");
        var sessionMock = new Mock<ICurrentSession>();
        sessionMock.Setup(s => s.UsuarioActual).Returns(usuario);
        sessionMock.Setup(s => s.RolActual).Returns(usuario.Rol);
        var navMock = new Mock<INavigationService>();
        var finanzasMock = new Mock<IFinanzasVistasService>();
        finanzasMock.Setup(f => f.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            new CalendarioPagosDto(new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        var backupsMock = new Mock<IBackupsService>();
        backupsMock.Setup(b => b.ObtenerSaludAsync()).ThrowsAsync(new InvalidOperationException("servidor caído"));

        var vm = new InicioViewModel(sessionMock.Object, navMock.Object, finanzasMock.Object, backupsMock.Object);

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoBackup);
    }
```

- [ ] **Step 2: Correr — debe fallar en compilación (constructor de 4 args no existe todavía)**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~InicioViewModelTests"`
Expected: FAIL — `CS7036: no se proporcionó ningún argumento que corresponda al parámetro requerido 'backups'`.

- [ ] **Step 3: Implementación**

```csharp
// src/StockApp.Presentation/ViewModels/InicioViewModel.cs
// Agregar el using:
using StockApp.Application.Backups;
```

```csharp
// Reemplazar el campo/constructor (agregar el 4to parámetro y campo):
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;
    private readonly IFinanzasVistasService _finanzasVistas;
    private readonly IBackupsService        _backups;

    // ... (Saludo/EsAdmin/RolTexto/propiedades de vencimientos sin cambios) ...

    [ObservableProperty] private bool _mostrarAvisoBackup;
    [ObservableProperty] private string? _textoAvisoBackup;

    public InicioViewModel(
        ICurrentSession session, INavigationService navigation,
        IFinanzasVistasService finanzasVistas, IBackupsService backups)
    {
        _session        = session;
        _navigation     = navigation;
        _finanzasVistas = finanzasVistas;
        _backups        = backups;
    }
```

```csharp
// Extender CargarAsync() (agregar la consulta de salud DESPUÉS del bloque try/catch existente
// de vencimientos, como un segundo try/catch independiente — un fallo de backups no debe pisar
// el resultado ya cargado de vencimientos, ni viceversa):

    public async Task CargarAsync()
    {
        try
        {
            var calendario = await _finanzasVistas.ObtenerCalendarioPagosAsync();
            CantidadVencidas = calendario.Vencidas.Count;
            CantidadAVencer7Dias = calendario.AVencer7Dias.Count;
            MostrarAvisoVencimientos = CantidadVencidas > 0 || CantidadAVencer7Dias > 0;
        }
        catch (Exception)
        {
            MostrarAvisoVencimientos = false;
        }

        if (!EsAdmin)
        {
            MostrarAvisoBackup = false;
            return;
        }

        try
        {
            var salud = await _backups.ObtenerSaludAsync();
            MostrarAvisoBackup = salud.Vencido;
            // UmbralHoras viaja en el DTO (SaludBackupDto, Task 6) — NUNCA hardcodear el número
            // acá: si el umbral cambia en ServicioConsultaBackups, este texto tiene que reflejarlo
            // solo, sin quedar mintiendo en silencio (pre-flight scan, corregido).
            TextoAvisoBackup = salud.UltimoExitoEn is DateTime ultimo
                ? $"El último backup exitoso fue el {ultimo:dd/MM/yyyy HH:mm} UTC (hace más de {salud.UmbralHoras} horas)."
                : "Todavía no se registró ningún backup exitoso.";
        }
        catch (Exception)
        {
            MostrarAvisoBackup = false;
        }
    }
```

- [ ] **Step 4: Correr — deben pasar**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~InicioViewModelTests"`
Expected: PASS (todo verde, incluidos los 4 tests nuevos).

- [ ] **Step 5: Correr la suite completa de `StockApp.Presentation.Tests` — confirmar que el nuevo parámetro de `InicioViewModel` no rompió nada más**

Run: `timeout 240 dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS (todo verde).

- [ ] **Step 6: Registrar `IBackupsService` como dependencia disponible para `InicioViewModel` (ya registrado en Task 8 — solo confirmar que el build de la app real resuelve la cadena completa)**

Run: `timeout 180 dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.` (la resolución de DI de `InicioViewModel` es automática por tipo — `services.AddTransient<InicioViewModel>()` ya registrado desde antes de este plan, sin cambios).

- [ ] **Step 7: Verificación orgánica** — con la base sin ninguna `CorridaBackup` sembrada, loguearse como Admin y confirmar que el banner de "backup vencido" aparece en Inicio; loguearse como Operador y confirmar que NO aparece.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/InicioViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs
git commit -m "feat(backups): agrega banner de salud del backup a InicioViewModel, solo Admin (Entrega 1)"
```

---

### Task 12: Test de integración de restaurabilidad

**Files:**
- Test: `tests/StockApp.Infrastructure.Tests/Backups/RestaurabilidadBackupTests.cs`

**Interfaces:**
- Consumes: `EjecutorPgDumpProceso` (Task 3, implementación REAL, no un fake), `PostgresFixture`/`PostgresCollection` (`tests/StockApp.Infrastructure.Tests/Fixtures/PostgresFixture.cs`, ya existe), `AppDbContext` (Task 1, `CorridasBackup`/`Categorias`/`RubrosGasto`).
- Produces: nada consumido por otra Task — es el punto final de la cadena de verificación de la Entrega 1.

**Categoría del test (spec §8 — "definí cómo se marca/filtra"):** el repo NO tiene hoy ningún mecanismo de `[Trait]`/categorías para filtrar tests (confirmado: cero usos de `Trait(` en toda la suite). TODA la colección `"Postgres"` de `StockApp.Infrastructure.Tests` ya requiere Docker (vía `PostgresFixture`) — este test vive ahí, sin marca especial, porque el filtro real que ya usa el repo es "¿está en un proyecto que depende de Testcontainers?", no un atributo. La única diferencia real de este test es un requisito ADICIONAL no cubierto por Docker: los binarios `pg_dump`/`pg_restore` en el PATH del host (ver Global Constraints) — se documenta en el comentario de la clase, no se intenta un skip condicional (un skip silencioso derrotaría el propósito del test: es el ÚNICO que prueba que el feature sirve para algo real).

- [ ] **Step 1: Escribir el test (sin ciclo red-green tradicional — es un test de integración de punta a punta, no una unidad con implementación a escribir: TODAS las piezas que ejercita ya existen de Tasks 1 y 3)**

```csharp
// tests/StockApp.Infrastructure.Tests/Backups/RestaurabilidadBackupTests.cs
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Backups;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Backups;

/// <summary>
/// ÚNICO test que prueba que un backup generado por EjecutorPgDumpProceso sirve para algo real
/// (spec Backups §8): dump contra la base sembrada por PostgresFixture -> restore en una base
/// temporal nueva del MISMO servidor -> verificar que tablas y conteos coinciden. El resto de
/// la suite (ServicioBackupTests con IEjecutorPgDump fake) solo prueba que SE INVOCA el
/// proceso, nunca que el archivo resultante es un backup restaurable.
///
/// REQUIERE los binarios pg_dump Y pg_restore en el PATH de la máquina que corre los tests
/// (paquete postgresql-client en Linux) ADEMÁS de Docker (que ya requiere toda la colección
/// "Postgres"). Corren en el HOST, no dentro del contenedor de Testcontainers — Testcontainers
/// solo expone el puerto. Sin esos binarios, el test falla con un mensaje claro (Win32Exception
/// envuelto por EjecutorPgDumpProceso / InvalidOperationException de pg_restore) — deliberado,
/// sin skip condicional (ver Task 12 del plan).
/// </summary>
[Collection("Postgres")]
public class RestaurabilidadBackupTests : IAsyncLifetime
{
    private readonly PostgresFixture _fixture;
    private readonly string _directorioTrabajo =
        Path.Combine(Path.GetTempPath(), "StockAppRestaurabilidadTests_" + Guid.NewGuid());

    public RestaurabilidadBackupTests(PostgresFixture fixture) => _fixture = fixture;

    public Task InitializeAsync()
    {
        Directory.CreateDirectory(_directorioTrabajo);
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_directorioTrabajo))
            Directory.Delete(_directorioTrabajo, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Dump_RestauradoEnBaseNueva_TieneLasMismasTablasYConteos()
    {
        // 1) Sembrar datos reconocibles en la base de la fixture.
        await using (var ctx = _fixture.CrearContexto())
        {
            ctx.Categorias.Add(new Categoria { Nombre = "Restaurabilidad-Cat", Activo = true });
            ctx.RubrosGasto.Add(new RubroGasto { Codigo = 999, Nombre = "Restaurabilidad-Rubro", Activo = true });
            await ctx.SaveChangesAsync();
        }

        int categoriasEsperadas, rubrosEsperados;
        await using (var ctx = _fixture.CrearContexto())
        {
            categoriasEsperadas = await ctx.Categorias.CountAsync();
            rubrosEsperados = await ctx.RubrosGasto.CountAsync();
        }

        // 2) Dump real con el ejecutor de PRODUCCIÓN (Task 3), no un fake.
        var configuracion = new ConfigurationBuilder().Build(); // sin Backups:PgDumpPath -> resuelve "pg_dump" por PATH
        var ejecutor = new EjecutorPgDumpProceso(configuracion, NullLogger<EjecutorPgDumpProceso>.Instance);
        var rutaDump = Path.Combine(_directorioTrabajo, "restaurabilidad.dump");

        var resultado = await ejecutor.EjecutarAsync(_fixture.ConnectionString, rutaDump, CancellationToken.None);

        Assert.True(resultado.Exitoso, $"pg_dump falló (¿está instalado 'postgresql-client'?): {resultado.MensajeError}");
        Assert.True(File.Exists(rutaDump));

        // 3) Crear una base nueva en el MISMO servidor y restaurar el dump ahí.
        var builder = new NpgsqlConnectionStringBuilder(_fixture.ConnectionString);
        var baseRestaurada = "restaurabilidad_" + Guid.NewGuid().ToString("N")[..8];

        builder.Database = "postgres"; // base de mantenimiento, siempre existe
        await using (var admin = new NpgsqlConnection(builder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var crear = new NpgsqlCommand($"CREATE DATABASE \"{baseRestaurada}\";", admin);
            await crear.ExecuteNonQueryAsync();
        }

        builder.Database = baseRestaurada;
        var restoreExitCode = await EjecutarPgRestoreAsync(builder.ConnectionString, rutaDump);
        Assert.Equal(0, restoreExitCode);

        // 4) Verificar tablas y conteos en la base restaurada.
        var opcionesRestaurada = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(builder.ConnectionString).Options;
        await using var restaurada = new AppDbContext(opcionesRestaurada);

        Assert.Equal(categoriasEsperadas, await restaurada.Categorias.CountAsync());
        Assert.Equal(rubrosEsperados, await restaurada.RubrosGasto.CountAsync());
        Assert.Contains(await restaurada.Categorias.ToListAsync(), c => c.Nombre == "Restaurabilidad-Cat");
    }

    /// <summary>
    /// NOTA (pre-flight scan — duplicación DELIBERADA, no corregida): este bloque de invocación
    /// de proceso (ArgumentList, PGPASSWORD, RedirectStandardError) es ~15 líneas casi idénticas
    /// a EjecutorPgDumpProceso.EjecutarAsync (Task 3). NO se extrajo un helper común a propósito:
    /// hacerlo acoplaría este test de integración al código de producción (EjecutorPgDumpProceso
    /// es SOLO para pg_dump, nunca ejecuta pg_restore — no hay una abstracción de producción que
    /// este método debiera reusar sin inventar una interfaz nueva solo para un test). Es una
    /// decisión del usuario, no un descuido — si un reviewer futuro señala esta duplicación como
    /// hallazgo nuevo, este comentario ya explica por qué se dejó así.
    /// </summary>
    private static async Task<int> EjecutarPgRestoreAsync(string connectionStringDestino, string rutaDump)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionStringDestino);
        using var proceso = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pg_restore",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        proceso.StartInfo.EnvironmentVariables["PGPASSWORD"] = builder.Password;
        proceso.StartInfo.ArgumentList.Add($"--host={builder.Host}");
        proceso.StartInfo.ArgumentList.Add($"--port={builder.Port}");
        proceso.StartInfo.ArgumentList.Add($"--username={builder.Username}");
        proceso.StartInfo.ArgumentList.Add($"--dbname={builder.Database}");
        proceso.StartInfo.ArgumentList.Add("--no-owner");
        proceso.StartInfo.ArgumentList.Add(rutaDump);

        proceso.Start();
        var stderr = await proceso.StandardError.ReadToEndAsync();
        await proceso.WaitForExitAsync();

        if (proceso.ExitCode != 0)
            throw new InvalidOperationException($"pg_restore falló: {stderr}");

        return proceso.ExitCode;
    }
}
```

- [ ] **Step 2: Correr**

Run: `timeout 300 dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~RestaurabilidadBackupTests"`
Expected: PASS (1/1) si `pg_dump`/`pg_restore` están en el PATH del host. Si falla con `Win32Exception`/mensaje de "no se pudo iniciar pg_dump", instalar `postgresql-client` (Linux: `apt-get install postgresql-client` o equivalente) y reintentar — NO es un bug del código, es el entorno faltante que el test está diseñado para exigir.

- [ ] **Step 3: Correr la suite completa de `StockApp.Infrastructure.Tests` — confirmar que no hay ripple**

Run: `timeout 300 dotnet test tests/StockApp.Infrastructure.Tests`
Expected: PASS (todo verde).

- [ ] **Step 4: Commit**

```bash
git add tests/StockApp.Infrastructure.Tests/Backups/RestaurabilidadBackupTests.cs
git commit -m "test(backups): agrega test de integracion de restaurabilidad (dump real -> restore) (Entrega 1)"
```

---

## Self-Review

**1. Cobertura del spec** (`docs/superpowers/specs/2026-07-27-backups-programados-y-diagnostico-design.md`, Entrega 1), sección por sección:

- §4.1 Persistencia (`CorridaBackup`, `DbSet`, migración, `ICorridaBackupRepository`/`CorridaBackupRepository`) → Task 1, completo.
- §4.2 Piezas nuevas: `PoliticaRetencion` → Task 2. `IEjecutorPgDump`/`EjecutorPgDumpProceso` → Task 3. `ServicioBackup` → Task 4. `BackupProgramadoService` con scope propio por corrida y catch-up al arrancar → Task 5. Directorio vía `IUserDataPathProvider.GetBackupsDirectory()` (ya existía) → consumido en Task 5/6, sin cambios a la interfaz.
- §4.3 Manejo de errores: fallo capturado en `ServicioBackup`, persistido como `CorridaBackup Fallida` con `MotivoFallo`, logueado `ILogger<ServicioBackup>.LogWarning` (va a stdout, no a archivo — Entrega 2), `PeriodicTimer` sigue vivo → Task 4/5. Escritura a `.tmp` + rename atómico + barrido de huérfanos → Task 4 (`ServicioBackup.LimpiarTmpHuerfanos`) + Task 5 (invocado al arrancar).
- §6 Permiso `GestionarDiagnostico` + endpoints (listado/descarga/salud) + exención de `BloqueoLicenciaMiddleware` → Task 6, completo, con matriz 401/403/200/404/423-exento. **Ampliado sobre la versión original del plan**: doble barrera de autorización (`ServicioConsultaBackups` con `_auth.Verificar`, además de la policy HTTP) — decisión del usuario, no estaba en el spec original (omisión del spec, corregida acá).
- §7 Desktop: `MantenimientoView`/`MantenimientoViewModel` con la zona Backups → Tasks 9-10. `DataContextChanged` → Task 10. `GuardarBytesAsync` → Task 7. `HttpClient` de descargas con timeout **finito de 30 minutos** (corregido sobre la versión original, que era infinito) → Task 7. `BackupsApiClient` → Task 8. Banner de salud en `InicioViewModel` → Task 11. **Ampliado sobre la versión original**: cancelación activa de una descarga en curso desde la UI, por fila (`FilaCorridaBackupVm.Descargando` + `CancelarCommand`) → Task 9-10, con el fix necesario de `ApiErrores.EnviarAsync` para que la cancelación no se reporte como error → Task 8.
- §8 Testing: `PoliticaRetencion` con los 6 casos borde exigidos (huecos por fallidas — implícito en que solo se pasan exitosas; cruce de mes; semanas parciales; menos de 6; exactamente 6; borrado real con muchas corridas) → Task 2. `ServicioBackup` con fake (éxito/binario ausente/credenciales/timeout/disco lleno vía `[Theory]` de mensajes opacos + limpieza de `.tmp`) → Task 4. `BackupProgramadoService` (scope propio por corrida + catch-up al arrancar) → Task 5. `ServicioConsultaBackups` (segunda barrera de autorización, unit tests con `_auth` mockeado) + endpoints `/backups` (matriz completa) → Task 6. Desktop (VM con fakes + test headless) → Tasks 9-10. Test de integración de restaurabilidad → Task 12.
- §9 Fuera de alcance: ningún Task agrega backup manual bajo demanda, nivel de log ajustable, descarga selectiva de logs, envío fuera del servidor, ni un flujo de restore expuesto al usuario — respetado.

**2. Barrido de placeholders:** sin resultados — no hay "TBD", "TODO" (en inglés), "agregar manejo de errores apropiado" ni "similar a la Task N" sin código. La única simplificación señalada explícitamente (el converter de ícono del Task 10) se corrigió en el propio Task con código completo, no se dejó pendiente.

**3. Consistencia de tipos — verificado línea por línea contra las firmas fijadas en Task 1:**
   - `ICorridaBackupRepository` (`AgregarAsync`, `ListarTodasAsync`, `ListarExitosasAsync`, `ObtenerPorIdAsync`, `ObtenerUltimaExitosaAsync`, `EliminarAsync`) — mismos 6 métodos usados idénticamente en Tasks 4, 5, 6 (ahora vía `ServicioConsultaBackups`, ya no directo desde `BackupsEndpoints`) y 12 (fakes y real).
   - `ServicioBackup.EjecutarCorridaAsync(string connectionString, string directorioBackups, DateTime ahoraUtc, CancellationToken)` y `LimpiarTmpHuerfanos(string)` — firmas idénticas en Task 4 (definición) y Task 5 (consumo vía scope).
   - `ServicioConsultaBackups.ListarAsync()`/`ObtenerSaludAsync()`/`ResolverArchivoParaDescargaAsync(int id, string directorioBackups)` — mismo nombre de parámetro `directorioBackups` que `ServicioBackup` (Task 4), coherencia deliberada entre ambos servicios del módulo `Backups/` (misma frontera Application↔Infrastructure, misma solución). Definido y consumido únicamente dentro de Task 6 (`BackupsEndpoints`), sin ripple a otras Tasks — es server-side, no forma parte del contrato HTTP externo que consume el desktop.
   - `IBackupsService` (`ListarAsync(CancellationToken ct = default)`, `DescargarAsync(int id, CancellationToken ct = default)`, `ObtenerSaludAsync(CancellationToken ct = default)`) — SIN relación de tipo con `ServicioConsultaBackups` (nombres de método parecidos por diseño, pero son dos interfaces/clases independientes: una del lado cliente HTTP en `StockApp.ApiClient`, otra del lado servidor en `StockApp.Api`/`StockApp.Application`, comunicadas solo por el contrato HTTP de `BackupsEndpoints`). Los 3 métodos ganan `CancellationToken ct = default` (Task 8, decisión de diseño 2) — firma idéntica en la definición (Task 8), `BackupsApiClient` (Task 8), consumo en `MantenimientoViewModel` (Task 9, `ct` real vía `fila.Cts.Token`) e `InicioViewModel` (Task 11, `ct` implícito/default, sin ripple).
   - `IServicioGuardadoArchivo.GuardarBytesAsync(Stream contenido, string nombreSugerido, CancellationToken ct = default)` — firma idéntica en Task 7 (definición + impl) y Task 9 (`MantenimientoViewModel.DescargarAsync`, pasa `fila.Cts.Token`).
   - `CorridaBackupDto`/`SaludBackupDto`/`BackupDescargaDto` — mismo orden posicional de campos en Task 6 (definición), Task 8 (`BackupsApiClient`), Tasks 9-11 (consumo). `SaludBackupDto` con 3er campo `UmbralHoras` consistente en las 6 construcciones del archivo (Tasks 6, 10, 11).
   - `FilaCorridaBackupVm` (Task 9: `Id`, `FinalizadaEn`, `Resultado`, `NombreArchivo`, `TamanioBytes`, `MotivoFallo`, `Descargando`, `internal Cts`) — mismo tipo consumido por `MantenimientoViewModel.Corridas: ObservableCollection<FilaCorridaBackupVm>` (Task 9) y por el `DataTemplate x:DataType="vm:FilaCorridaBackupVm"` de Task 10 (ya NO `dto:CorridaBackupDto`, `xmlns:dto` retirado del archivo por quedar sin uso).
   - `MantenimientoViewModel.DescargarCommand` (generado desde `DescargarAsync(FilaCorridaBackupVm fila)`, YA NO `CorridaBackupDto corrida`) y `CancelarCommand` (generado desde `Cancelar(FilaCorridaBackupVm fila)`, nuevo) — usados consistentemente en Task 9 (tests, ambos toman `FilaCorridaBackupVm`) y Task 10 (XAML, ambos con `CommandParameter="{Binding}"` sobre la fila).
   - `ApiErrores.EnviarAsync(Func<Task<HttpResponseMessage>> enviar, CancellationToken ct = default)` (Task 8) — los 3 call-sites de `BackupsApiClient` pasan `ct` en AMBAS posiciones (al `GetAsync` y a `EnviarAsync`); los ~9 `XxxApiClient` restantes del proyecto NO se tocan (parámetro opcional, comportamiento sin cambios).
   - `InicioViewModel` gana el 4to parámetro `IBackupsService backups` — un único call-site del constructor en los tests (`Crear()`, confirmado por búsqueda antes de escribir Task 11), sin ripple a otros archivos.

**4. Decisiones no especificadas por el spec, documentadas explícitamente en el plan (para que el implementador no las reinterprete):** `IniciadaEn`/`FinalizadaEn` no-nullable, sin fila "en progreso" (Task 1); retención hard-delete de la fila DB junto con el archivo (Task 1/4); `ServicioBackup` recibe `connectionString`/`directorioBackups` como parámetros, no inyectados, por la frontera Application↔Infrastructure (Task 4); `BackupProgramadoService` usa `IServiceScopeFactory` + scope nuevo por corrida, con `internal` + `InternalsVisibleTo` para poder testearlo sin exponerlo como API pública (Task 5); **`BackupsEndpoints` usa doble barrera de autorización vía `ServicioConsultaBackups` (`_auth.Verificar` + policy HTTP), CORRIGIENDO la deviación de la versión original de este plan — decisión del usuario, spec §6 tenía una omisión (Task 6)**; `ServicioConsultaBackups` recibe `directorioBackups` como parámetro de método (no inyectado), misma frontera y misma solución que `ServicioBackup` — elegida por coherencia sobre agregar una abstracción nueva en `Application/Interfaces/` para un solo caso de uso (Task 6); `ServicioConsultaBackups` sin interfaz, mismo criterio "Servicio+Xxx" que `ServicioLicencia`/`ServicioResetAdmin` (Task 6); seguridad de `pg_dump`/`pg_restore` vía `PGPASSWORD` + `ArgumentList` en vez de interpolar la connection string (Task 3/12); `EjecutorPgDumpProceso` sin ciclo red-green (mismo criterio que `ServicioGuardadoArchivo`) — verificado por Task 12 (Task 3); `AddKeyedSingleton`/`GetRequiredKeyedService` para el segundo `HttpClient` (Task 7/8); `BackupDescargaDto` con `Stream` (no `byte[]`) para no bufferear dumps grandes (Task 8); banner de `InicioViewModel` solo consulta salud si `EsAdmin` (Task 11); categoría de test de restaurabilidad = ninguna marca especial, mismo criterio que ya usa el repo (Task 12).

**5. Huecos/riesgos detectados (no diluidos, señalados para el usuario):**
   - El Task 10 tuvo un ajuste inline (converter de ícono de un solo uso reemplazado por dos `<i:Icon>` condicionados) — quedó resuelto con código completo en el propio Task, no es una deuda pendiente.
   - Task 12 depende de un binario externo (`postgresql-client`) que puede no estar instalado en el entorno que ejecute el plan — es un riesgo de ejecución real, documentado explícitamente en el Task y en Global Constraints, no un placeholder.
   - `ILogger<ServicioBackup>`/`ILogger<BackupProgramadoService>` van a stdout únicamente en esta entrega (spec §4.3 lo aclara explícitamente: "sin Serilog todavía") — la única fuente de verdad para diagnosticar un fallo es `CorridaBackup.MotivoFallo` vía el endpoint/UI, no el log. Correcto y ya documentado en Global Constraints y en el propio spec; no es un hueco de este plan.
   - **Resuelto en esta revisión** (ya no es un hueco): la primera versión de este plan aceptaba que `BackupsEndpoints` leyera directo de `ICorridaBackupRepository`, sin segunda barrera de autorización — señalado explícitamente en esa versión y corregido acá por decisión del usuario (Task 6, `ServicioConsultaBackups`). Se deja esta nota para trazabilidad: la razón real (endpoints que exponen un dump completo de la base) no estaba en el spec original y no deberia volver a diluirse en una futura revision de este plan.

**6. Pre-flight scan (revision de code-review sobre el plan) — 5 hallazgos, 4 corregidos y 1 dejado deliberadamente:**
   - **Corregido**: umbral de "backup vencido" (26h) duplicado entre `ServicioConsultaBackups.UmbralAviso` (Application) y el texto hardcodeado de `InicioViewModel` (Presentation). Fix: `SaludBackupDto` gana el campo `UmbralHoras`, unica fuente de verdad — si el umbral cambia en un solo lugar, el banner que lee el admin ya no puede quedar mintiendo en silencio (Tasks 6, 10, 11).
   - **Corregido**: dos `catch` que descartaban la excepcion sin loguear — `ServicioBackup.BorrarSiExiste` (`catch (IOException)`, Task 4) y `EjecutorPgDumpProceso.TryKill` (`catch (InvalidOperationException)`, Task 3). Ambos ganan `LogWarning` con contexto (ruta del archivo / pid del proceso); `EjecutorPgDumpProceso` gana `ILogger<EjecutorPgDumpProceso>` inyectado (no lo tenia) — resuelto automatico por el hosting de ASP.NET Core, sin cambios de registro en `Program.cs`. Van a stdout en esta entrega (Serilog llega en la E2, que los captura retroactivamente).
   - **Corregido**: dos tests sin assert (`EliminarAsync_IdInexistente_NoLanza`, Task 1; `LimpiarTmpHuerfanos_DirectorioInexistente_NoLanza`, Task 4) que solo probaban ausencia de excepcion. Ambos ganan un assert explicito sobre el estado resultante (conteo de filas sin cambios; el directorio sigue sin existir) — la intencion queda escrita, no implicita.
   - **Corregido**: `UserDataPathProviderFake` duplicado — clase anidada privada en `BackupProgramadoServiceTests` (Task 5, creada antes de que existiera la version compartida) y la de `tests/StockApp.Api.Tests/Fixtures/` (Task 6). Se mantiene el orden de creacion (Task 5 sigue creando la suya primero), pero Task 6 agrega un Step de consolidacion al final que la elimina y deja un solo fake compartido en `Fixtures/`.
   - **Dejado deliberadamente, NO corregido** (decision del usuario): la duplicacion de ~15 lineas de invocacion de proceso entre `EjecutorPgDumpProceso.EjecutarAsync` (Task 3, produccion) y `EjecutarPgRestoreAsync` (Task 12, test de integracion). Extraer un helper comun acoplaria un test de integracion al codigo de produccion para una sola funcion (`pg_restore` no es parte del feature, solo se usa para verificar el dump en el test). Nota explicita agregada en el propio metodo de Task 12 para que un reviewer futuro no la reporte como hallazgo nuevo.

**7. Cancelacion de descarga (Task 7 ya implementada, revisada post-review) — timeout finito + cancelacion activa desde la UI:**
   - **Problema real**: `Timeout.InfiniteTimeSpan` (Task 7) + cero `CancellationToken` en toda la cadena (`GuardarBytesAsync` -> `IBackupsService.DescargarAsync` -> `DescargarCommand`) significaba que un servidor colgado a mitad de una descarga trababa el boton de esa fila para siempre — unica salida, cerrar la app.
   - **Fix de dos partes**: (a) timeout finito de 30 minutos en el `HttpClient` "Descargas" (Task 7, red de seguridad pasiva — justificacion del valor: LAN local, igual criterio que el timeout de 10s del `HttpClient` principal, 30 min cubre VPN/disco lento con margen generoso); (b) `CancellationToken` propagado extremo a extremo — `IServicioGuardadoArchivo.GuardarBytesAsync` (Task 7) -> `IBackupsService` completo, los 3 metodos por consistencia con `IVelopackGateway` (Task 8) -> `FilaCorridaBackupVm.Cts` por fila + `MantenimientoViewModel.CancelarCommand` (Task 9) -> boton "Cancelar" por fila (Task 10).
   - **Bug real encontrado al propagar el token, no anticipado en la primera version de este plan**: `ApiErrores.EnviarAsync` (codigo YA EXISTENTE, compartido por ~10 XxxApiClient) envolvia TODA `TaskCanceledException` en `ServidorNoDisponibleException` bajo el supuesto documentado en su propio comentario de que "los clients no pasan CancellationToken propio". `BackupsApiClient` rompe ese supuesto — sin el fix (Task 8, decision de diseño 3), cancelar se hubiera reportado al usuario como error de servidor, violando el requisito explicito de la correccion. Arreglado con un parametro opcional (`ct = default`) que no afecta a ningun otro XxxApiClient existente.
   - **Diseño de la cancelacion**: por FILA, no global — cada `FilaCorridaBackupVm` es dueña de su propio `CancellationTokenSource` (se evaluo y descarto explicitamente un `IdEnDescarga: int?` a nivel del VM padre comparado via `MultiBinding`/`IMultiValueConverter` en XAML, por no tener precedente en el repo y ser mas complejo que un booleano por fila). Dos descargas de filas distintas pueden correr en paralelo — no se restringio a "una a la vez" porque no fue pedido.
   - **Cobertura de tests de la cancelacion** (requisito minimo explicito del pedido): Task 9 tiene un test que fuerza la cancelacion con un `TaskCompletionSource` + `Task.WaitAsync(ct)` (simula un servidor colgado real, no solo un mock que tira la excepcion) y verifica `Descargando == false`, `Cts == null` y `InformarAsync` NUNCA llamado tras cancelar. Task 10 verifica el wiring visual (que boton se ve) seteando `Descargando` directo sobre la fila, sin duplicar la coordinacion async ya cubierta en Task 9.

---
