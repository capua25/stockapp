# Módulo Finanzas — Fase 1: Maestros de finanzas — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Crear los cuatro maestros del módulo Finanzas (`FuenteFinanciamiento`, `RubroGasto`, `LineaPoa`, `AsignacionPresupuestal`) end-to-end: entidades + migración, repositorios, servicios ABM con permisos y auditoría, endpoints `/finanzas/*`, ApiClients y la pantalla "Maestros de finanzas" en el desktop (nueva sección "Finanzas" en el sidebar) — según el spec `docs/superpowers/specs/2026-07-15-modulo-finanzas-design.md` (§4 maestros, §9 permisos).

**Architecture:** El mismo slice vertical canónico de `Categoria` repetido para tres recursos: entidad de Domain sin lógica → repo EF Core en Infrastructure → servicio ABM en Application (autorización + reglas + auditoría) → endpoint Minimal API con policy por permiso → ApiClient que implementa la MISMA interfaz `IXxxService` → ViewModels/Views Avalonia. La única pieza nueva de diseño es el agregado `LineaPoa`: sus `AsignacionPresupuestal` (presupuesto por fuente — resuelve el financiamiento mixto B+C) se gestionan como hijas del agregado, con alta/modificación recibiendo la lista completa de asignaciones y el repo reemplazándolas físicamente.

**Tech Stack:** .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api / ApiClient / Presentation), ASP.NET Core Minimal API + JWT + policies derivadas de `AuthorizationService`, EF Core + Npgsql (PostgreSQL), xUnit + Moq + Testcontainers (Api/Infrastructure contra Postgres real), Avalonia 12 + CommunityToolkit.Mvvm (desktop).

## Global Constraints

Convenciones REALES del repo — verificadas contra el código existente:

- **Español en todo**: entidades, servicios, métodos (`AltaAsync`, `ModificarAsync`, `BajaLogicaAsync`), mensajes de error, comentarios y commits.
- **Baja lógica siempre**: `Activo = false`, nunca DELETE físico de maestros. Excepción deliberada de esta fase: `AsignacionPresupuestal` es hija del agregado `LineaPoa`, NO tiene `Activo` (spec §4) — modificar la línea reemplaza sus asignaciones con delete+insert físico. Nada más las referencia por FK (Gasto referenciará LineaPoa+Fuente en fases posteriores, no la asignación), así que no se pierde historia referencial.
- **Género gramatical en los nombres**: `Proveedor` usa `ListarTodosAsync` (masculino) y `Categoria` `ListarTodasAsync` (femenino). Acá: `FuenteFinanciamiento`/`LineaPoa` → `ListarTodasAsync`/`ListarActivasAsync`; `RubroGasto` → `ListarTodosAsync`/`ListarActivosAsync` (y ruta `/finanzas/rubros/activos`).
- **decimal 18,4** para montos (`HasPrecision(18, 4)`), fechas UTC (no aplican a esta fase: los maestros no tienen fechas).
- **Auditoría append-only**: valores nuevos de `AccionAuditada` SIEMPRE al final del enum (están persistidos como int en BD); acá arrancan en 22.
- **Excepciones de dominio**: `ArgumentException` para input inválido (→ 400), `ReglaDeNegocioException` para reglas violadas (→ 409), `EntidadNoEncontradaException` (→ 404). El `DomainExceptionHandler` de la API ya mapea las tres; los endpoints NO hacen try/catch.
- **Doble barrera de autorización**: policy HTTP en el endpoint (derivada automáticamente de `Permisos.Todos` + `AuthorizationService.TienePermiso` en `Program.cs`) + `_auth.Verificar(...)` dentro del servicio de Application.
- **Permisos de esta fase** (spec §9): `VerFinanzas` y `GestionarMaestrosFinanzas`, otorgados a **Admin Y Operador**. Consecuencia para los tests de API: NO existe caso 403 por rol para estos endpoints (ningún rol carece del permiso) — la matriz es 401 / 200-201 / 404 / 409.
- **`IVersionReportes` NO se invalida** desde los servicios de finanzas: ese contador solo versiona el caché de reportes de stock, que estos maestros no afectan.
- **TDD estricto**: test que falla primero (el fallo esperado inicial es error de compilación `CS0246` porque el tipo no existe — cuenta como rojo), implementación mínima, verde, commit. Un commit por task, conventional commit en español, SIN `Co-Authored-By` ni atribución de IA.
- **Nunca buildear porque sí**: solo `dotnet test` del proyecto afectado por task; `dotnet test` de la solución completa recién en la task final. No buildear la app desktop.
- **Tests de Infrastructure/Api requieren Docker** (Testcontainers levanta Postgres). El contenedor `stockapp-pg` del entorno de desarrollo queda siempre corriendo (convención del repo) pero los tests usan su propio contenedor efímero.
- **Migración EF**: el startup project es `src/StockApp.Api` (desde Fase 3b el desktop ya NO referencia Infrastructure — el plan viejo de Fase 1 Postgres usaba `src/StockApp.Presentation`, ya no vale). Existe `AppDbContextFactory` (design-time) así que `migrations add` no necesita un Postgres corriendo. La migración se aplica sola al arrancar la API (`MigrateAsync` en `Program.cs`).
- **Sin seed de datos**: los maestros arrancan vacíos; el usuario los carga (o los importa en la fase del importador).
- Los ViewModels atrapan `ReglaDeNegocioException`/`EntidadNoEncontradaException` y las muestran (`MensajeError`/`IConfirmacionService.InformarAsync`) — regresión real documentada: dejarlas propagar crashea la app.
- Las Views de Avalonia NO se auto-inicializan: cablear `DataContextChanged` para disparar `CargarAsync` (gotcha recurrente documentado en memoria del proyecto).

---

### Task 1: Domain — entidades de maestros, acciones de auditoría y migración `FinanzasMaestros`

**Files:**
- Create: `src/StockApp.Domain/Entities/FuenteFinanciamiento.cs`
- Create: `src/StockApp.Domain/Entities/RubroGasto.cs`
- Create: `src/StockApp.Domain/Entities/LineaPoa.cs`
- Create: `src/StockApp.Domain/Entities/AsignacionPresupuestal.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs` (valores 22–30, append-only)
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (DbSets + config fluida)
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_FinanzasMaestros.cs` (generada por `dotnet ef`)

**Interfaces:**
- Consumes: `StockApp.Domain.Entities` (namespace existente), `Microsoft.EntityFrameworkCore`.
- Produces:
  - `class FuenteFinanciamiento { int Id; string Nombre; bool Activo = true; }`
  - `class RubroGasto { int Id; int Codigo; string Nombre; bool Activo = true; }`
  - `class LineaPoa { int Id; string Nombre; string Programa; int Ejercicio; bool Activo = true; List<AsignacionPresupuestal> Asignaciones; }`
  - `class AsignacionPresupuestal { int Id; int LineaPoaId; int FuenteFinanciamientoId; FuenteFinanciamiento? FuenteFinanciamiento; decimal Monto; }`
  - `AccionAuditada.AltaFuenteFinanciamiento = 22` … `BajaLineaPoa = 30`
  - `AppDbContext.FuentesFinanciamiento`, `.RubrosGasto`, `.LineasPoa`, `.AsignacionesPresupuestales`

- [ ] **Step 1: Crear las cuatro entidades**

`src/StockApp.Domain/Entities/FuenteFinanciamiento.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Fuente de financiamiento ("literal" FIGM: A, B, C, Multas, Excedentes/Préstamos).
/// Maestro cerrado del módulo Finanzas — hoy texto libre en la planilla de gastos.
/// </summary>
public class FuenteFinanciamiento
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;  // obligatorio, único
    public bool Activo { get; set; } = true;             // baja lógica
}
```

`src/StockApp.Domain/Entities/RubroGasto.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Rubro de gasto (los 17 rubros de la hoja Variables de la planilla).
/// El código numérico es el identificador de negocio con el que se importa/reporta.
/// </summary>
public class RubroGasto
{
    public int Id { get; set; }
    public int Codigo { get; set; }                       // obligatorio, único
    public string Nombre { get; set; } = string.Empty;    // obligatorio
    public bool Activo { get; set; } = true;              // baja lógica
}
```

`src/StockApp.Domain/Entities/LineaPoa.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Línea de proyecto del POA (Rambla, Carpeta Asfáltica, Eventos, Prensa, ...).
/// Agregado: sus <see cref="AsignacionPresupuestal"/> (presupuesto por fuente de
/// financiamiento) se gestionan SIEMPRE a través de la línea, nunca sueltas.
/// </summary>
public class LineaPoa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;    // obligatorio, único por ejercicio
    public string Programa { get; set; } = string.Empty;  // obligatorio
    public int Ejercicio { get; set; }                    // año, ej. 2026
    public bool Activo { get; set; } = true;              // baja lógica
    public List<AsignacionPresupuestal> Asignaciones { get; set; } = new();
}
```

`src/StockApp.Domain/Entities/AsignacionPresupuestal.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Presupuesto de una línea POA POR fuente de financiamiento — resuelve el
/// financiamiento mixto B+C (caso real COMPOSTERAS). Hija del agregado LineaPoa:
/// sin Activo propio; modificar la línea reemplaza su lista completa de asignaciones.
/// </summary>
public class AsignacionPresupuestal
{
    public int Id { get; set; }
    public int LineaPoaId { get; set; }
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public decimal Monto { get; set; }  // precisión 18,4
}
```

- [ ] **Step 2: Agregar las acciones de auditoría (append-only)**

En `src/StockApp.Domain/Enums/AccionAuditada.cs`, agregar AL FINAL (después de `ResetAdminFirmado = 21,`):

```csharp
    // ── Finanzas — Fase 1: maestros (append-only a partir de 22) ─────────────
    AltaFuenteFinanciamiento         = 22,
    ModificacionFuenteFinanciamiento = 23,
    BajaFuenteFinanciamiento         = 24,
    AltaRubroGasto                   = 25,
    ModificacionRubroGasto           = 26,
    BajaRubroGasto                   = 27,
    AltaLineaPoa                     = 28,
    ModificacionLineaPoa             = 29,
    BajaLineaPoa                     = 30,
```

- [ ] **Step 3: DbSets + configuración fluida en AppDbContext**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, agregar después de `public DbSet<LogAuditoria> LogsAuditoria => Set<LogAuditoria>();`:

```csharp
    public DbSet<FuenteFinanciamiento> FuentesFinanciamiento => Set<FuenteFinanciamiento>();
    public DbSet<RubroGasto> RubrosGasto => Set<RubroGasto>();
    public DbSet<LineaPoa> LineasPoa => Set<LineaPoa>();
    public DbSet<AsignacionPresupuestal> AsignacionesPresupuestales => Set<AsignacionPresupuestal>();
```

Y al final de `OnModelCreating` (después del bloque de `LogAuditoria`):

```csharp
        // ── Finanzas: maestros (Fase 1 módulo Finanzas) ───────────────────────
        modelBuilder.Entity<FuenteFinanciamiento>(e =>
        {
            e.Property(f => f.Nombre).IsRequired();
            e.HasIndex(f => f.Nombre).IsUnique();
            e.Property(f => f.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<RubroGasto>(e =>
        {
            e.HasIndex(r => r.Codigo).IsUnique();
            e.Property(r => r.Nombre).IsRequired();
            e.Property(r => r.Activo).HasDefaultValue(true);
        });

        modelBuilder.Entity<LineaPoa>(e =>
        {
            e.Property(l => l.Nombre).IsRequired();
            e.Property(l => l.Programa).IsRequired();
            e.HasIndex(l => new { l.Nombre, l.Ejercicio }).IsUnique();
            e.Property(l => l.Activo).HasDefaultValue(true);
        });

        // AsignacionPresupuestal: hija del agregado LineaPoa. FKs Restrict porque los
        // maestros usan baja lógica (nunca se borra una LineaPoa o Fuente físicamente);
        // el reemplazo de asignaciones es un delete explícito en el repo, que Restrict
        // NO impide (Restrict solo bloquea cascadas desde el padre).
        modelBuilder.Entity<AsignacionPresupuestal>(e =>
        {
            e.Property(a => a.Monto).HasPrecision(18, 4);
            e.HasIndex(a => new { a.LineaPoaId, a.FuenteFinanciamientoId }).IsUnique();
            e.HasOne<LineaPoa>().WithMany(l => l.Asignaciones)
                .HasForeignKey(a => a.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.FuenteFinanciamiento).WithMany()
                .HasForeignKey(a => a.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 4: Compilar Infrastructure**

Run: `dotnet build src/StockApp.Infrastructure`
Expected: `Build succeeded` sin warnings nuevos.

- [ ] **Step 5: Generar la migración FinanzasMaestros**

Run:
```bash
dotnet ef migrations add FinanzasMaestros \
  --project src/StockApp.Infrastructure \
  --startup-project src/StockApp.Api
```
Expected: `Done.` y dos archivos nuevos en `src/StockApp.Infrastructure/Migrations/` (`<timestamp>_FinanzasMaestros.cs` + `.Designer.cs`) más el snapshot actualizado. EF usa el `AppDbContextFactory` de design-time (Npgsql); no requiere un Postgres corriendo.

- [ ] **Step 6: Verificar la migración generada**

Run: `rg -n "FuentesFinanciamiento|RubrosGasto|LineasPoa|AsignacionesPresupuestales" src/StockApp.Infrastructure/Migrations/*FinanzasMaestros.cs | head -20`
Expected: `CreateTable` para las cuatro tablas.

Run: `rg -n "IsUnique|Restrict|18,4|precision" src/StockApp.Infrastructure/Migrations/*FinanzasMaestros.cs | head -20`
Expected: índices únicos (`Nombre`; `Codigo`; `Nombre, Ejercicio`; `LineaPoaId, FuenteFinanciamientoId`), FKs con `ReferentialAction.Restrict` y `Monto` con `precision: 18, scale: 4`.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Domain src/StockApp.Infrastructure
git commit -m "feat(finanzas): entidades de maestros de finanzas + migración FinanzasMaestros"
```

---

### Task 2: Infrastructure — repositorios de los tres maestros (tests contra Postgres real)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IFuenteFinanciamientoRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IRubroGastoRepository.cs`
- Create: `src/StockApp.Application/Interfaces/ILineaPoaRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/FuenteFinanciamientoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/RubroGastoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/LineaPoaRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/FuenteFinanciamientoRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/RubroGastoRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/LineaPoaRepositoryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 1), `PostgresRepositoryTestBase`/`PostgresFixture` (fixtures existentes).
- Produces:
  - `interface IFuenteFinanciamientoRepository`: `Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id)`, `Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()`, `Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)`, `Task<int> AgregarAsync(FuenteFinanciamiento fuente)`, `Task ActualizarAsync(FuenteFinanciamiento fuente)`
  - `interface IRubroGastoRepository`: `Task<RubroGasto?> ObtenerPorIdAsync(int id)`, `Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()`, `Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null)`, `Task<int> AgregarAsync(RubroGasto rubro)`, `Task ActualizarAsync(RubroGasto rubro)`
  - `interface ILineaPoaRepository`: `Task<LineaPoa?> ObtenerPorIdAsync(int id)`, `Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()`, `Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null)`, `Task<int> AgregarAsync(LineaPoa linea)`, `Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones)`

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Infrastructure.Tests/Repositories/FuenteFinanciamientoRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class FuenteFinanciamientoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly FuenteFinanciamientoRepository _repo;

    public FuenteFinanciamientoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new FuenteFinanciamientoRepository(Context);
    }

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal B" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Literal B", found!.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodasAsync_RetornaTodasOrdenadasPorNombre_SinFiltroDeActivo()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Multas" });
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal A", Activo = false });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Literal A", result[0].Nombre);
        Assert.Equal("Multas", result[1].Nombre);
    }

    [Fact]
    public async Task ExisteNombreAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.True(await _repo.ExisteNombreAsync("Literal C"));
        Assert.False(await _repo.ExisteNombreAsync("NoExiste"));
    }

    [Fact]
    public async Task ExisteNombreAsync_ExcluyendoId_MismaFuente_RetornaFalse()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Literal C" });

        Assert.False(await _repo.ExisteNombreAsync("Literal C", excluyendoId: id));
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_Persiste()
    {
        var id = await _repo.AgregarAsync(new FuenteFinanciamiento { Nombre = "Excedentes" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.False(updated!.Activo);
    }
}
```

`tests/StockApp.Infrastructure.Tests/Repositories/RubroGastoRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class RubroGastoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly RubroGastoRepository _repo;

    public RubroGastoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new RubroGastoRepository(Context);
    }

    [Fact]
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal(3, found!.Codigo);
        Assert.Equal("Combustibles", found.Nombre);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodosAsync_RetornaOrdenadosPorCodigo()
    {
        await _repo.AgregarAsync(new RubroGasto { Codigo = 9, Nombre = "Eventos" });
        await _repo.AgregarAsync(new RubroGasto { Codigo = 1, Nombre = "Sueldos" });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodosAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal(1, result[0].Codigo);
        Assert.Equal(9, result[1].Codigo);
    }

    [Fact]
    public async Task ExisteCodigoAsync_Existente_RetornaTrue()
    {
        await _repo.AgregarAsync(new RubroGasto { Codigo = 5, Nombre = "Papelería" });

        Assert.True(await _repo.ExisteCodigoAsync(5));
        Assert.False(await _repo.ExisteCodigoAsync(99));
    }

    [Fact]
    public async Task ExisteCodigoAsync_ExcluyendoId_MismoRubro_RetornaFalse()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 5, Nombre = "Papelería" });

        Assert.False(await _repo.ExisteCodigoAsync(5, excluyendoId: id));
    }

    [Fact]
    public async Task ActualizarAsync_ModificaNombreYBajaLogica_Persiste()
    {
        var id = await _repo.AgregarAsync(new RubroGasto { Codigo = 7, Nombre = "Original" });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);
        found!.Nombre = "Modificado";
        found.Activo = false;
        await _repo.ActualizarAsync(found);
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Modificado", updated!.Nombre);
        Assert.False(updated.Activo);
    }
}
```

`tests/StockApp.Infrastructure.Tests/Repositories/LineaPoaRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class LineaPoaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly LineaPoaRepository _repo;
    private readonly FuenteFinanciamientoRepository _fuentes;

    public LineaPoaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new LineaPoaRepository(Context);
        _fuentes = new FuenteFinanciamientoRepository(Context);
    }

    private async Task<int> SeedFuenteAsync(string nombre)
        => await _fuentes.AgregarAsync(new FuenteFinanciamiento { Nombre = nombre });

    [Fact]
    public async Task AgregarAsync_ConAsignaciones_Y_ObtenerPorId_IncluyeAsignaciones()
    {
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "COMPOSTERAS",
            Programa = "Ambiente",
            Ejercicio = 2026,
            Asignaciones =
            {
                new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteB, Monto = 100000m },
                new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteC, Monto = 50000.5000m },
            },
        });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("COMPOSTERAS", found!.Nombre);
        Assert.Equal(2, found.Asignaciones.Count);
        // El Include trae la nav de la fuente para poder mostrar su nombre en la grilla
        Assert.All(found.Asignaciones, a => Assert.NotNull(a.FuenteFinanciamiento));
        Assert.Contains(found.Asignaciones, a => a.Monto == 50000.5000m);
    }

    [Fact]
    public async Task ActualizarAsync_ReemplazaLasAsignaciones()
    {
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");
        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "PRENSA",
            Programa = "Comunicación",
            Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteB, Monto = 80000m } },
        });
        Context.ChangeTracker.Clear();

        var original = await _repo.ObtenerPorIdAsync(id);
        original!.Programa = "Prensa y Comunicación";
        await _repo.ActualizarAsync(original, new List<AsignacionPresupuestal>
        {
            new() { FuenteFinanciamientoId = fuenteC, Monto = 120000m },
        });
        Context.ChangeTracker.Clear();

        var updated = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal("Prensa y Comunicación", updated!.Programa);
        var asignacion = Assert.Single(updated.Asignaciones);
        Assert.Equal(fuenteC, asignacion.FuenteFinanciamientoId);
        Assert.Equal(120000m, asignacion.Monto);
    }

    [Fact]
    public async Task ListarTodasAsync_OrdenaPorEjercicioDescYNombre_ConAsignaciones()
    {
        var fuente = await SeedFuenteAsync("Literal B");
        await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Rambla", Programa = "Obras", Ejercicio = 2025,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 1m } },
        });
        await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Eventos", Programa = "Cultura", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 2m } },
        });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodasAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Eventos", result[0].Nombre);   // 2026 antes que 2025
        Assert.Equal("Rambla", result[1].Nombre);
        Assert.NotEmpty(result[0].Asignaciones);
    }

    [Fact]
    public async Task ExisteNombreEjercicioAsync_DistingueEjercicios()
    {
        var fuente = await SeedFuenteAsync("Literal B");
        var id = await _repo.AgregarAsync(new LineaPoa
        {
            Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuente, Monto = 1m } },
        });

        Assert.True(await _repo.ExisteNombreEjercicioAsync("Rambla", 2026));
        Assert.False(await _repo.ExisteNombreEjercicioAsync("Rambla", 2027));
        Assert.False(await _repo.ExisteNombreEjercicioAsync("Rambla", 2026, excluyendoId: id));
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~FuenteFinanciamientoRepository|FullyQualifiedName~RubroGastoRepository|FullyQualifiedName~LineaPoaRepository"`
Expected: FALLA la compilación con `CS0246` (`FuenteFinanciamientoRepository` no existe) — rojo confirmado.

- [ ] **Step 3: Interfaces de repos en Application**

`src/StockApp.Application/Interfaces/IFuenteFinanciamientoRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IFuenteFinanciamientoRepository
{
    Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync();
    Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null);
    Task<int> AgregarAsync(FuenteFinanciamiento fuente);
    Task ActualizarAsync(FuenteFinanciamiento fuente);
}
```

`src/StockApp.Application/Interfaces/IRubroGastoRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IRubroGastoRepository
{
    Task<RubroGasto?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<RubroGasto>> ListarTodosAsync();
    Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null);
    Task<int> AgregarAsync(RubroGasto rubro);
    Task ActualizarAsync(RubroGasto rubro);
}
```

`src/StockApp.Application/Interfaces/ILineaPoaRepository.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ILineaPoaRepository
{
    /// <summary>Incluye las Asignaciones con su FuenteFinanciamiento navegable.</summary>
    Task<LineaPoa?> ObtenerPorIdAsync(int id);

    /// <summary>Incluye las Asignaciones. Ordena por Ejercicio desc, luego Nombre.</summary>
    Task<IReadOnlyList<LineaPoa>> ListarTodasAsync();

    Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null);

    /// <summary>Inserta la línea CON sus asignaciones (grafo completo).</summary>
    Task<int> AgregarAsync(LineaPoa linea);

    /// <summary>
    /// Actualiza los campos de la línea y REEMPLAZA sus asignaciones por
    /// <paramref name="nuevasAsignaciones"/> (delete + insert físico — son hijas del
    /// agregado, sin baja lógica propia). <paramref name="linea"/> debe ser la instancia
    /// tracked obtenida vía <see cref="ObtenerPorIdAsync"/>.
    /// </summary>
    Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones);
}
```

- [ ] **Step 4: Implementar los repos**

`src/StockApp.Infrastructure/Repositories/FuenteFinanciamientoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class FuenteFinanciamientoRepository : IFuenteFinanciamientoRepository
{
    private readonly AppDbContext _ctx;

    public FuenteFinanciamientoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id)
        => _ctx.FuentesFinanciamiento.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()
        => await _ctx.FuentesFinanciamiento.OrderBy(f => f.Nombre).ToListAsync();

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.FuentesFinanciamiento.AnyAsync(f => f.Nombre == nombre && f.Id != excluyendoId.Value)
            : _ctx.FuentesFinanciamiento.AnyAsync(f => f.Nombre == nombre);

    public async Task<int> AgregarAsync(FuenteFinanciamiento fuente)
    {
        _ctx.FuentesFinanciamiento.Add(fuente);
        await _ctx.SaveChangesAsync();
        return fuente.Id;
    }

    public Task ActualizarAsync(FuenteFinanciamiento fuente)
    {
        _ctx.FuentesFinanciamiento.Update(fuente);
        return _ctx.SaveChangesAsync();
    }
}
```

`src/StockApp.Infrastructure/Repositories/RubroGastoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class RubroGastoRepository : IRubroGastoRepository
{
    private readonly AppDbContext _ctx;

    public RubroGastoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<RubroGasto?> ObtenerPorIdAsync(int id)
        => _ctx.RubrosGasto.FindAsync(id).AsTask();

    public async Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()
        => await _ctx.RubrosGasto.OrderBy(r => r.Codigo).ToListAsync();

    public Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.RubrosGasto.AnyAsync(r => r.Codigo == codigo && r.Id != excluyendoId.Value)
            : _ctx.RubrosGasto.AnyAsync(r => r.Codigo == codigo);

    public async Task<int> AgregarAsync(RubroGasto rubro)
    {
        _ctx.RubrosGasto.Add(rubro);
        await _ctx.SaveChangesAsync();
        return rubro.Id;
    }

    public Task ActualizarAsync(RubroGasto rubro)
    {
        _ctx.RubrosGasto.Update(rubro);
        return _ctx.SaveChangesAsync();
    }
}
```

`src/StockApp.Infrastructure/Repositories/LineaPoaRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class LineaPoaRepository : ILineaPoaRepository
{
    private readonly AppDbContext _ctx;

    public LineaPoaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<LineaPoa?> ObtenerPorIdAsync(int id)
        => _ctx.LineasPoa
            .Include(l => l.Asignaciones)
            .ThenInclude(a => a.FuenteFinanciamiento)
            .FirstOrDefaultAsync(l => l.Id == id);

    public async Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()
        => await _ctx.LineasPoa
            .Include(l => l.Asignaciones)
            .ThenInclude(a => a.FuenteFinanciamiento)
            .OrderByDescending(l => l.Ejercicio)
            .ThenBy(l => l.Nombre)
            .ToListAsync();

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null)
        => excluyendoId.HasValue
            ? _ctx.LineasPoa.AnyAsync(l => l.Nombre == nombre && l.Ejercicio == ejercicio && l.Id != excluyendoId.Value)
            : _ctx.LineasPoa.AnyAsync(l => l.Nombre == nombre && l.Ejercicio == ejercicio);

    public async Task<int> AgregarAsync(LineaPoa linea)
    {
        _ctx.LineasPoa.Add(linea);  // inserta el grafo completo (línea + asignaciones)
        await _ctx.SaveChangesAsync();
        return linea.Id;
    }

    public async Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones)
    {
        // Las asignaciones son hijas del agregado: se reemplazan con delete explícito +
        // insert (DeleteBehavior.Restrict NO impide deletes explícitos de las hijas;
        // solo bloquea cascadas desde el padre). Se crean instancias frescas con Id = 0
        // para que EF las inserte, sin arrastrar tracking previo.
        _ctx.AsignacionesPresupuestales.RemoveRange(linea.Asignaciones);
        linea.Asignaciones = nuevasAsignaciones
            .Select(a => new AsignacionPresupuestal
            {
                LineaPoaId = linea.Id,
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList();

        _ctx.LineasPoa.Update(linea);
        await _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~FuenteFinanciamientoRepository|FullyQualifiedName~RubroGastoRepository|FullyQualifiedName~LineaPoaRepository"`
Expected: los 14 tests nuevos en verde (requiere Docker para Testcontainers).

- [ ] **Step 6: Suite completa de Infrastructure**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: toda la suite verde (sin regresiones).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Interfaces src/StockApp.Infrastructure/Repositories tests/StockApp.Infrastructure.Tests
git commit -m "feat(finanzas): repositorios de maestros de finanzas con tests contra Postgres"
```

---

### Task 3: Application — permisos de finanzas + servicios ABM con auditoría

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Create: `src/StockApp.Application/Finanzas/IFuenteFinanciamientoService.cs`
- Create: `src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs`
- Create: `src/StockApp.Application/Finanzas/IRubroGastoService.cs`
- Create: `src/StockApp.Application/Finanzas/RubroGastoService.cs`
- Create: `src/StockApp.Application/Finanzas/ILineaPoaService.cs`
- Create: `src/StockApp.Application/Finanzas/LineaPoaService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/FuenteFinanciamientoServiceTests.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/RubroGastoServiceTests.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/LineaPoaServiceTests.cs`

**Interfaces:**
- Consumes: `IFuenteFinanciamientoRepository`/`IRubroGastoRepository`/`ILineaPoaRepository` (Task 2), `ICurrentSession`, `IAuthorizationService` (`void Verificar(RolUsuario? rolActual, string accion)`), `IAuditLogger` (`Task RegistrarAsync(int usuarioId, AccionAuditada accion, string entidad, int entidadId, string detalle)`), `AccionAuditada` 22–30 (Task 1).
- Produces:
  - `Permisos.VerFinanzas = "finanzas.ver"`, `Permisos.GestionarMaestrosFinanzas = "finanzas.maestros"` (agregados también a `Permisos.Todos` — de ahí se derivan solas las policies HTTP en `Program.cs`).
  - `AuthorizationService.AccionesOperador` incluye ambos permisos nuevos (spec §9: Admin Y Operador por ahora).
  - `interface IFuenteFinanciamientoService`: `Task<int> AltaAsync(FuenteFinanciamiento fuente)`, `Task ModificarAsync(FuenteFinanciamiento fuente)`, `Task BajaLogicaAsync(int id)`, `Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()`, `Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync()`
  - `interface IRubroGastoService`: `Task<int> AltaAsync(RubroGasto rubro)`, `Task ModificarAsync(RubroGasto rubro)`, `Task BajaLogicaAsync(int id)`, `Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()`, `Task<IReadOnlyList<RubroGasto>> ListarActivosAsync()`
  - `interface ILineaPoaService`: `Task<int> AltaAsync(LineaPoa linea)`, `Task ModificarAsync(LineaPoa linea)`, `Task BajaLogicaAsync(int id)`, `Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()`, `Task<IReadOnlyList<LineaPoa>> ListarActivasAsync()`

Reglas de autorización: mutaciones y `ListarTodas/Todos` exigen `GestionarMaestrosFinanzas`; `ListarActivas/Activos` exige solo `VerFinanzas` (mismo split que `Categoria`: gestionar vs. seleccionar).

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Application.Tests/Finanzas/FuenteFinanciamientoServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class FuenteFinanciamientoServiceTests
{
    private static (FuenteFinanciamientoService svc,
                    Mock<IFuenteFinanciamientoRepository> repoMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        var svc = new FuenteFinanciamientoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, audit);
    }

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new FuenteFinanciamiento { Nombre = "  " }));
    }

    [Fact]
    public async Task AltaAsync_NombreDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Literal B", null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new FuenteFinanciamiento { Nombre = "Literal B" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaFuenteFinanciamiento()
    {
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ExisteNombreAsync("Multas", null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<FuenteFinanciamiento>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" });

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaFuenteFinanciamiento,
            "FuenteFinanciamiento", 5, It.Is<string>(d => d.Contains("Multas"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((FuenteFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new FuenteFinanciamiento { Id = 99, Nombre = "X" }));
    }

    [Fact]
    public async Task ModificarAsync_CambiaNombre_ActualizaYAudita()
    {
        var original = new FuenteFinanciamiento { Id = 1, Nombre = "Literal A", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreAsync("Literal A (FIGM)", 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Literal A (FIGM)" });

        repo.Verify(r => r.ActualizarAsync(It.Is<FuenteFinanciamiento>(f => f.Nombre == "Literal A (FIGM)")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionFuenteFinanciamiento,
            "FuenteFinanciamiento", 1, It.Is<string>(d => d.Contains("Nombre"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = new FuenteFinanciamiento { Id = 1, Nombre = "Literal A", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        await svc.ModificarAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Literal A" });

        repo.Verify(r => r.ActualizarAsync(It.IsAny<FuenteFinanciamiento>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBaja()
    {
        var fuente = new FuenteFinanciamiento { Id = 2, Nombre = "Multas", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fuente);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<FuenteFinanciamiento>(f => f.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaFuenteFinanciamiento,
            "FuenteFinanciamiento", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var fuente = new FuenteFinanciamiento { Id = 2, Nombre = "Multas", Activo = false };
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(fuente);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B", Activo = true },
            new() { Id = 2, Nombre = "Vieja", Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Literal B", activas[0].Nombre);
    }
}
```

`tests/StockApp.Application.Tests/Finanzas/RubroGastoServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class RubroGastoServiceTests
{
    private static (RubroGastoService svc,
                    Mock<IRubroGastoRepository> repoMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IRubroGastoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        var svc = new RubroGastoService(repo.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, audit);
    }

    [Fact]
    public async Task AltaAsync_CodigoNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 0, Nombre = "Combustibles" }));
    }

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _) = Crear();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = " " }));
    }

    [Fact]
    public async Task AltaAsync_CodigoDuplicado_LanzaReglaDeNegocio()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(3, null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" }));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaRubroGasto()
    {
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ExisteCodigoAsync(3, null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<RubroGasto>())).ReturnsAsync(7);

        var id = await svc.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });

        Assert.Equal(7, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaRubroGasto,
            "RubroGasto", 7, It.Is<string>(d => d.Contains("Combustibles") && d.Contains("3"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_CambiaCodigoYNombre_ActualizaYAudita()
    {
        var original = new RubroGasto { Id = 1, Codigo = 3, Nombre = "Combustible", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteCodigoAsync(4, 1)).ReturnsAsync(false);

        await svc.ModificarAsync(new RubroGasto { Id = 1, Codigo = 4, Nombre = "Combustibles y Lubricantes" });

        repo.Verify(r => r.ActualizarAsync(
            It.Is<RubroGasto>(x => x.Codigo == 4 && x.Nombre == "Combustibles y Lubricantes")), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionRubroGasto,
            "RubroGasto", 1, It.Is<string>(d => d.Contains("Código") && d.Contains("Nombre"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((RubroGasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => svc.ModificarAsync(new RubroGasto { Id = 99, Codigo = 1, Nombre = "X" }));
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaRubroGasto()
    {
        var rubro = new RubroGasto { Id = 2, Codigo = 5, Nombre = "Papelería", Activo = true };
        var (svc, repo, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(rubro);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<RubroGasto>(x => x.Activo == false)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaRubroGasto,
            "RubroGasto", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactivo_LanzaReglaDeNegocio()
    {
        var rubro = new RubroGasto { Id = 2, Codigo = 5, Nombre = "Papelería", Activo = false };
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(rubro);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarActivosAsync_FiltraInactivos()
    {
        var (svc, repo, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Sueldos", Activo = true },
            new() { Id = 2, Codigo = 2, Nombre = "Viejo", Activo = false },
        });

        var activos = await svc.ListarActivosAsync();

        Assert.Single(activos);
        Assert.Equal("Sueldos", activos[0].Nombre);
    }
}
```

`tests/StockApp.Application.Tests/Finanzas/LineaPoaServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class LineaPoaServiceTests
{
    private static (LineaPoaService svc,
                    Mock<ILineaPoaRepository> repoMock,
                    Mock<IFuenteFinanciamientoRepository> fuentesMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<ILineaPoaRepository>();
        var fuentes = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        // Por defecto, cualquier fuente consultada existe (los tests de fuente
        // inexistente lo pisan).
        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });

        var svc = new LineaPoaService(repo.Object, fuentes.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, fuentes, audit);
    }

    private static LineaPoa LineaValida() => new()
    {
        Nombre = "COMPOSTERAS",
        Programa = "Ambiente",
        Ejercicio = 2026,
        Asignaciones =
        {
            new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m },
            new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 50000m },
        },
    };

    [Fact]
    public async Task AltaAsync_NombreVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Nombre = " ";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_ProgramaVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Programa = "";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_EjercicioNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Ejercicio = 0;

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_SinAsignaciones_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones.Clear();

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
        Assert.Contains("al menos una asignación", ex.Message);
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones[0].Monto = 0m;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
    }

    [Fact]
    public async Task AltaAsync_FuenteRepetida_LanzaReglaDeNegocio()
    {
        var (svc, _, _, _) = Crear();
        var linea = LineaValida();
        linea.Asignaciones[1].FuenteFinanciamientoId = linea.Asignaciones[0].FuenteFinanciamientoId;

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(linea));
        Assert.Contains("repetida", ex.Message);
    }

    [Fact]
    public async Task AltaAsync_FuenteInexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, _, fuentes, _) = Crear();
        fuentes.Setup(f => f.ObtenerPorIdAsync(2)).ReturnsAsync((FuenteFinanciamiento?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.AltaAsync(LineaValida()));
    }

    [Fact]
    public async Task AltaAsync_NombreYEjercicioDuplicados_LanzaReglaDeNegocio()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS", 2026, null)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(LineaValida()));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaLineaPoa()
    {
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS", 2026, null)).ReturnsAsync(false);
        repo.Setup(r => r.AgregarAsync(It.IsAny<LineaPoa>())).ReturnsAsync(4);

        var id = await svc.AltaAsync(LineaValida());

        Assert.Equal(4, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaLineaPoa,
            "LineaPoa", 4, It.Is<string>(d => d.Contains("COMPOSTERAS") && d.Contains("2026"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((LineaPoa?)null);

        var linea = LineaValida();
        linea.Id = 99;

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.ModificarAsync(linea));
    }

    [Fact]
    public async Task ModificarAsync_CambiaCamposYAsignaciones_ActualizaYAudita()
    {
        var original = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(original);
        repo.Setup(r => r.ExisteNombreEjercicioAsync("COMPOSTERAS II", 2026, 4)).ReturnsAsync(false);

        var editada = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS II", Programa = "Ambiente", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 80000m } },
        };
        await svc.ModificarAsync(editada);

        repo.Verify(r => r.ActualizarAsync(
            It.Is<LineaPoa>(l => l.Id == 4 && l.Nombre == "COMPOSTERAS II"),
            It.Is<IReadOnlyList<AsignacionPresupuestal>>(a =>
                a.Count == 1 && a[0].FuenteFinanciamientoId == 2 && a[0].Monto == 80000m)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionLineaPoa,
            "LineaPoa", 4, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(4)).ReturnsAsync(original);

        var igual = new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m } },
        };
        await svc.ModificarAsync(igual);

        repo.Verify(r => r.ActualizarAsync(It.IsAny<LineaPoa>(), It.IsAny<IReadOnlyList<AsignacionPresupuestal>>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBajaLineaPoa()
    {
        var linea = new LineaPoa { Id = 3, Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026, Activo = true };
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(linea);

        await svc.BajaLogicaAsync(3);

        repo.Verify(r => r.ActualizarAsync(
            It.Is<LineaPoa>(l => l.Activo == false),
            It.Is<IReadOnlyList<AsignacionPresupuestal>>(a => a.Count == 0)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaLineaPoa,
            "LineaPoa", 3, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactiva_LanzaReglaDeNegocio()
    {
        var linea = new LineaPoa { Id = 3, Nombre = "PRENSA", Programa = "Comunicación", Ejercicio = 2026, Activo = false };
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(3)).ReturnsAsync(linea);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(3));
    }

    [Fact]
    public async Task ListarActivasAsync_FiltraInactivas()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026, Activo = true },
            new() { Id = 2, Nombre = "Vieja", Programa = "Obras", Ejercicio = 2025, Activo = false },
        });

        var activas = await svc.ListarActivasAsync();

        Assert.Single(activas);
        Assert.Equal("Rambla", activas[0].Nombre);
    }
}
```

Nota sobre la baja lógica de LineaPoa: la baja NO toca las asignaciones — pero como `ActualizarAsync` del repo exige la lista, la baja pasa la lista actual mapeada. Para simplificar el contrato, la implementación de abajo pasa las asignaciones existentes tal cual (`linea.Asignaciones` reconstruidas), y el test lo fija con `a.Count == 0` para el caso sin asignaciones.

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~StockApp.Application.Tests.Finanzas"`
Expected: FALLA la compilación con `CS0246` (`FuenteFinanciamientoService` no existe) — rojo confirmado.

- [ ] **Step 3: Permisos nuevos**

En `src/StockApp.Application/Authorization/Permisos.cs`, agregar después de `RecalcularStock`:

```csharp
    // Finanzas — Fase 1: por ahora Admin Y Operador tienen ambos (spec Finanzas §9);
    // el futuro sistema de permisos por usuario solo cambia el mapeo rol→permiso.
    public const string VerFinanzas              = "finanzas.ver";
    public const string GestionarMaestrosFinanzas = "finanzas.maestros";
```

Y en `Permisos.Todos`, agregar al final de la lista:

```csharp
        VerFinanzas,
        GestionarMaestrosFinanzas,
```

En `src/StockApp.Application/Authorization/AuthorizationService.cs`, agregar a `AccionesOperador`:

```csharp
        Permisos.VerFinanzas,
        Permisos.GestionarMaestrosFinanzas,
```

(Al estar en `Permisos.Todos`, `Program.cs` deriva las policies HTTP automáticamente — cero cambios manuales de policies.)

- [ ] **Step 4: Implementar los servicios**

`src/StockApp.Application/Finanzas/IFuenteFinanciamientoService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface IFuenteFinanciamientoService
{
    Task<int> AltaAsync(FuenteFinanciamiento fuente);
    Task ModificarAsync(FuenteFinanciamiento fuente);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync();

    /// <summary>
    /// Fuentes activas para selección (combos de gastos/asignaciones). A diferencia de
    /// <see cref="ListarTodasAsync"/>, exige solo VerFinanzas, no GestionarMaestrosFinanzas.
    /// </summary>
    Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync();
}
```

`src/StockApp.Application/Finanzas/FuenteFinanciamientoService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de FuenteFinanciamiento (los "literales" FIGM). Baja lógica con Activo=false.
/// No invalida IVersionReportes: ese caché versiona solo reportes de stock.
/// </summary>
public class FuenteFinanciamientoService : IFuenteFinanciamientoService
{
    private readonly IFuenteFinanciamientoRepository _repo;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public FuenteFinanciamientoService(
        IFuenteFinanciamientoRepository repo,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<int> AltaAsync(FuenteFinanciamiento fuente)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        if (string.IsNullOrWhiteSpace(fuente.Nombre))
            throw new ArgumentException("El nombre de la fuente de financiamiento es obligatorio.");

        if (await _repo.ExisteNombreAsync(fuente.Nombre, null))
            throw new ReglaDeNegocioException(
                $"Ya existe una fuente de financiamiento con el nombre '{fuente.Nombre}'.");

        var id = await _repo.AgregarAsync(fuente);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaFuenteFinanciamiento,
            "FuenteFinanciamiento", id,
            $"Nombre: {fuente.Nombre}");

        return id;
    }

    public async Task ModificarAsync(FuenteFinanciamiento fuente)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        if (string.IsNullOrWhiteSpace(fuente.Nombre))
            throw new ArgumentException("El nombre de la fuente de financiamiento es obligatorio.");

        var original = await _repo.ObtenerPorIdAsync(fuente.Id)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {fuente.Id} no encontrada.");

        if (original.Nombre != fuente.Nombre
            && await _repo.ExisteNombreAsync(fuente.Nombre, fuente.Id))
            throw new ReglaDeNegocioException(
                $"Ya existe una fuente de financiamiento con el nombre '{fuente.Nombre}'.");

        var cambios = new List<string>();
        if (original.Nombre != fuente.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {fuente.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Nombre = fuente.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionFuenteFinanciamiento,
            "FuenteFinanciamiento", fuente.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var fuente = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Fuente de financiamiento {id} no encontrada.");

        if (!fuente.Activo)
            throw new ReglaDeNegocioException($"La fuente de financiamiento {id} ya está inactiva.");

        fuente.Activo = false;
        await _repo.ActualizarAsync(fuente);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaFuenteFinanciamiento,
            "FuenteFinanciamiento", id,
            $"Baja lógica de '{fuente.Nombre}'");
    }

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(f => f.Activo).ToList();
    }
}
```

`src/StockApp.Application/Finanzas/IRubroGastoService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface IRubroGastoService
{
    Task<int> AltaAsync(RubroGasto rubro);
    Task ModificarAsync(RubroGasto rubro);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<RubroGasto>> ListarTodosAsync();

    /// <summary>
    /// Rubros activos para selección (combo de gastos). Exige solo VerFinanzas.
    /// </summary>
    Task<IReadOnlyList<RubroGasto>> ListarActivosAsync();
}
```

`src/StockApp.Application/Finanzas/RubroGastoService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de RubroGasto (los 17 rubros de la hoja Variables). El código numérico es único.
/// Baja lógica con Activo=false.
/// </summary>
public class RubroGastoService : IRubroGastoService
{
    private readonly IRubroGastoRepository _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public RubroGastoService(
        IRubroGastoRepository repo,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    private static void ValidarCampos(RubroGasto rubro)
    {
        if (rubro.Codigo <= 0)
            throw new ArgumentException("El código del rubro debe ser mayor a cero.");
        if (string.IsNullOrWhiteSpace(rubro.Nombre))
            throw new ArgumentException("El nombre del rubro es obligatorio.");
    }

    public async Task<int> AltaAsync(RubroGasto rubro)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        ValidarCampos(rubro);

        if (await _repo.ExisteCodigoAsync(rubro.Codigo, null))
            throw new ReglaDeNegocioException($"Ya existe un rubro con el código {rubro.Codigo}.");

        var id = await _repo.AgregarAsync(rubro);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaRubroGasto,
            "RubroGasto", id,
            $"Código: {rubro.Codigo}; Nombre: {rubro.Nombre}");

        return id;
    }

    public async Task ModificarAsync(RubroGasto rubro)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        ValidarCampos(rubro);

        var original = await _repo.ObtenerPorIdAsync(rubro.Id)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {rubro.Id} no encontrado.");

        if (original.Codigo != rubro.Codigo
            && await _repo.ExisteCodigoAsync(rubro.Codigo, rubro.Id))
            throw new ReglaDeNegocioException($"Ya existe un rubro con el código {rubro.Codigo}.");

        var cambios = new List<string>();
        if (original.Codigo != rubro.Codigo)
            cambios.Add($"Código: {original.Codigo} → {rubro.Codigo}");
        if (original.Nombre != rubro.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {rubro.Nombre}");

        if (cambios.Count == 0)
            return;

        original.Codigo = rubro.Codigo;
        original.Nombre = rubro.Nombre;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionRubroGasto,
            "RubroGasto", rubro.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var rubro = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {id} no encontrado.");

        if (!rubro.Activo)
            throw new ReglaDeNegocioException($"El rubro de gasto {id} ya está inactivo.");

        rubro.Activo = false;
        await _repo.ActualizarAsync(rubro);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaRubroGasto,
            "RubroGasto", id,
            $"Baja lógica de '{rubro.Nombre}' (código {rubro.Codigo})");
    }

    public async Task<IReadOnlyList<RubroGasto>> ListarTodosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodosAsync();
    }

    public async Task<IReadOnlyList<RubroGasto>> ListarActivosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todos = await _repo.ListarTodosAsync();
        return todos.Where(r => r.Activo).ToList();
    }
}
```

`src/StockApp.Application/Finanzas/ILineaPoaService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

public interface ILineaPoaService
{
    /// <summary>
    /// Alta de la línea CON sus asignaciones presupuestales (agregado completo).
    /// Reglas: al menos una asignación, montos &gt; 0, sin fuentes repetidas.
    /// </summary>
    Task<int> AltaAsync(LineaPoa linea);

    /// <summary>Modifica campos y REEMPLAZA las asignaciones por las de <paramref name="linea"/>.</summary>
    Task ModificarAsync(LineaPoa linea);

    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<LineaPoa>> ListarTodasAsync();

    /// <summary>Líneas activas para selección (combo de gastos). Exige solo VerFinanzas.</summary>
    Task<IReadOnlyList<LineaPoa>> ListarActivasAsync();
}
```

`src/StockApp.Application/Finanzas/LineaPoaService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM del agregado LineaPoa + AsignacionPresupuestal. Las asignaciones (presupuesto por
/// fuente — financiamiento mixto B+C) viven SIEMPRE dentro de la línea: alta y modificación
/// reciben la lista completa; el repo las reemplaza físicamente (no tienen baja lógica propia).
/// </summary>
public class LineaPoaService : ILineaPoaService
{
    private readonly ILineaPoaRepository             _repo;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public LineaPoaService(
        ILineaPoaRepository repo,
        IFuenteFinanciamientoRepository fuentes,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _fuentes = fuentes;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    private async Task ValidarAsync(LineaPoa linea)
    {
        if (string.IsNullOrWhiteSpace(linea.Nombre))
            throw new ArgumentException("El nombre de la línea POA es obligatorio.");
        if (string.IsNullOrWhiteSpace(linea.Programa))
            throw new ArgumentException("El programa de la línea POA es obligatorio.");
        if (linea.Ejercicio <= 0)
            throw new ArgumentException("El ejercicio de la línea POA debe ser un año válido.");

        if (linea.Asignaciones.Count == 0)
            throw new ReglaDeNegocioException(
                "La línea POA debe tener al menos una asignación presupuestal.");

        if (linea.Asignaciones.Any(a => a.Monto <= 0))
            throw new ReglaDeNegocioException(
                "Todas las asignaciones presupuestales deben tener un monto mayor a cero.");

        var fuentesRepetidas = linea.Asignaciones
            .GroupBy(a => a.FuenteFinanciamientoId)
            .Any(g => g.Count() > 1);
        if (fuentesRepetidas)
            throw new ReglaDeNegocioException(
                "Hay una fuente de financiamiento repetida en las asignaciones presupuestales.");

        foreach (var asignacion in linea.Asignaciones)
        {
            _ = await _fuentes.ObtenerPorIdAsync(asignacion.FuenteFinanciamientoId)
                ?? throw new EntidadNoEncontradaException(
                    $"Fuente de financiamiento {asignacion.FuenteFinanciamientoId} no encontrada.");
        }
    }

    public async Task<int> AltaAsync(LineaPoa linea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        await ValidarAsync(linea);

        if (await _repo.ExisteNombreEjercicioAsync(linea.Nombre, linea.Ejercicio, null))
            throw new ReglaDeNegocioException(
                $"Ya existe una línea POA '{linea.Nombre}' para el ejercicio {linea.Ejercicio}.");

        var id = await _repo.AgregarAsync(linea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.AltaLineaPoa,
            "LineaPoa", id,
            $"Nombre: {linea.Nombre}; Ejercicio: {linea.Ejercicio}; " +
            $"Asignaciones: {linea.Asignaciones.Count}");

        return id;
    }

    public async Task ModificarAsync(LineaPoa linea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        await ValidarAsync(linea);

        var original = await _repo.ObtenerPorIdAsync(linea.Id)
            ?? throw new EntidadNoEncontradaException($"Línea POA {linea.Id} no encontrada.");

        if ((original.Nombre != linea.Nombre || original.Ejercicio != linea.Ejercicio)
            && await _repo.ExisteNombreEjercicioAsync(linea.Nombre, linea.Ejercicio, linea.Id))
            throw new ReglaDeNegocioException(
                $"Ya existe una línea POA '{linea.Nombre}' para el ejercicio {linea.Ejercicio}.");

        var cambios = new List<string>();
        if (original.Nombre != linea.Nombre)
            cambios.Add($"Nombre: {original.Nombre} → {linea.Nombre}");
        if (original.Programa != linea.Programa)
            cambios.Add($"Programa: {original.Programa} → {linea.Programa}");
        if (original.Ejercicio != linea.Ejercicio)
            cambios.Add($"Ejercicio: {original.Ejercicio} → {linea.Ejercicio}");

        // Asignaciones: comparación por conjunto (fuente, monto) — el orden no importa.
        var setOriginal = original.Asignaciones
            .Select(a => (a.FuenteFinanciamientoId, a.Monto)).OrderBy(x => x).ToList();
        var setNuevo = linea.Asignaciones
            .Select(a => (a.FuenteFinanciamientoId, a.Monto)).OrderBy(x => x).ToList();
        if (!setOriginal.SequenceEqual(setNuevo))
            cambios.Add($"Asignaciones: {original.Asignaciones.Count} → {linea.Asignaciones.Count} " +
                        $"(total {linea.Asignaciones.Sum(a => a.Monto)})");

        if (cambios.Count == 0)
            return;

        original.Nombre    = linea.Nombre;
        original.Programa  = linea.Programa;
        original.Ejercicio = linea.Ejercicio;
        await _repo.ActualizarAsync(original, linea.Asignaciones);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.ModificacionLineaPoa,
            "LineaPoa", linea.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);

        var linea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Línea POA {id} no encontrada.");

        if (!linea.Activo)
            throw new ReglaDeNegocioException($"La línea POA {id} ya está inactiva.");

        linea.Activo = false;
        // La baja lógica NO toca las asignaciones: se re-pasan las existentes tal cual.
        var asignacionesActuales = linea.Asignaciones
            .Select(a => new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList();
        await _repo.ActualizarAsync(linea, asignacionesActuales);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id,
            AccionAuditada.BajaLineaPoa,
            "LineaPoa", id,
            $"Baja lógica de '{linea.Nombre}' ({linea.Ejercicio})");
    }

    public async Task<IReadOnlyList<LineaPoa>> ListarTodasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarMaestrosFinanzas);
        return await _repo.ListarTodasAsync();
    }

    public async Task<IReadOnlyList<LineaPoa>> ListarActivasAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        var todas = await _repo.ListarTodasAsync();
        return todas.Where(l => l.Activo).ToList();
    }
}
```

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~StockApp.Application.Tests.Finanzas"`
Expected: los ~28 tests nuevos en verde.

- [ ] **Step 6: Suite completa de Application (incluye tests existentes de AuthorizationService/Permisos que podrían fijar la lista de permisos)**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: verde. Si algún test existente fija el contenido exacto de `Permisos.Todos` o `AccionesOperador`, actualizarlo agregando los dos permisos nuevos (es un cambio de comportamiento DESEADO por el spec §9, no una regresión).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application tests/StockApp.Application.Tests
git commit -m "feat(finanzas): servicios ABM de maestros con permisos y auditoría"
```

---

### Task 4: Api — endpoints `/finanzas/fuentes`, `/finanzas/rubros`, `/finanzas/lineas-poa`

**Files:**
- Create: `src/StockApp.Api/Endpoints/FuentesFinanciamientoEndpoints.cs`
- Create: `src/StockApp.Api/Endpoints/RubrosGastoEndpoints.cs`
- Create: `src/StockApp.Api/Endpoints/LineasPoaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (registro DI + MapXxxEndpoints)
- Test: `tests/StockApp.Api.Tests/FuentesFinanciamientoEndpointTests.cs`
- Test: `tests/StockApp.Api.Tests/RubrosGastoEndpointTests.cs`
- Test: `tests/StockApp.Api.Tests/LineasPoaEndpointTests.cs`

**Interfaces:**
- Consumes: `IFuenteFinanciamientoService`/`IRubroGastoService`/`ILineaPoaService` (Task 3), repos (Task 2), `Permisos.VerFinanzas`/`GestionarMaestrosFinanzas` (Task 3 — las policies HTTP ya se derivan solas de `Permisos.Todos`), `DomainExceptionHandler` (409/404/400 automáticos), fixtures `ApiFactory`/`ApiTestBase`/`DatosDePrueba`.
- Produces (contratos wire — los consumen los ApiClients de Task 5):
  - `record FuenteFinanciamientoDto(int Id, string Nombre, bool Activo)`; `record CrearFuenteFinanciamientoRequest(string Nombre)`; `record ModificarFuenteFinanciamientoRequest(string Nombre)`
  - `record RubroGastoDto(int Id, int Codigo, string Nombre, bool Activo)`; `record CrearRubroGastoRequest(int Codigo, string Nombre)`; `record ModificarRubroGastoRequest(int Codigo, string Nombre)`
  - `record AsignacionPresupuestalDto(int Id, int FuenteFinanciamientoId, string? FuenteFinanciamientoNombre, decimal Monto)`; `record AsignacionPresupuestalRequest(int FuenteFinanciamientoId, decimal Monto)`; `record LineaPoaDto(int Id, string Nombre, string Programa, int Ejercicio, bool Activo, List<AsignacionPresupuestalDto> Asignaciones)`; `record CrearLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones)`; `record ModificarLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones)`
  - Rutas: `GET|POST /finanzas/fuentes`, `PUT|DELETE /finanzas/fuentes/{id}`, `GET /finanzas/fuentes/activas`; ídem `/finanzas/rubros` (+ `GET /finanzas/rubros/activos`); ídem `/finanzas/lineas-poa` (+ `GET /finanzas/lineas-poa/activas`).

**Matriz de tests**: 401 sin token; 200/201 con token (Admin y Operador — AMBOS roles tienen los permisos de finanzas, así que acá **no existe caso 403 por rol**, a diferencia de `/categorias`); 409 duplicado; DELETE = baja lógica verificada en DB.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Api.Tests/FuentesFinanciamientoEndpointTests.cs`:

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

public class FuentesFinanciamientoEndpointTests : ApiTestBase
{
    public FuentesFinanciamientoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetFuentes_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/fuentes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetFuentes_ConTokenOperador_Devuelve200()
    {
        // Spec Finanzas §9: GestionarMaestrosFinanzas lo tienen Admin Y Operador por
        // ahora — no hay caso 403 por rol para estos endpoints.
        await using var ctx = Factory.CrearContexto();
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal B", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/finanzas/fuentes");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fuentes = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoDto>>();
        Assert.Contains(fuentes!, f => f.Nombre == "Literal B");
    }

    [Fact]
    public async Task PostFuentes_ConTokenAdmin_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Multas"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null(response.Headers.Location); // no existe GET /finanzas/fuentes/{id}

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.FuentesFinanciamiento.AnyAsync(f => f.Nombre == "Multas"));
    }

    [Fact]
    public async Task PostFuentes_NombreDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Literal C", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/fuentes",
            new CrearFuenteFinanciamientoRequest("Literal C"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PutFuentes_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Original", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/finanzas/fuentes/{fuente.Id}",
            new ModificarFuenteFinanciamientoRequest("Modificada"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteFuentes_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = new FuenteFinanciamiento { Nombre = "Para Baja", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/finanzas/fuentes/{fuente.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizada = await verificacion.FuentesFinanciamiento.SingleAsync(f => f.Id == fuente.Id);
        Assert.False(actualizada.Activo);
    }

    [Fact]
    public async Task GetFuentesActivas_ConTokenOperador_FiltraInactivas()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Activa", Activo = true });
        ctx.FuentesFinanciamiento.Add(new FuenteFinanciamiento { Nombre = "Inactiva", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/finanzas/fuentes/activas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fuentes = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoDto>>();
        Assert.Contains(fuentes!, f => f.Nombre == "Activa");
        Assert.DoesNotContain(fuentes!, f => f.Nombre == "Inactiva");
    }
}
```

`tests/StockApp.Api.Tests/RubrosGastoEndpointTests.cs`:

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

public class RubrosGastoEndpointTests : ApiTestBase
{
    public RubrosGastoEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    [Fact]
    public async Task GetRubros_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/rubros");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRubros_ConTokenOperador_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(3, "Combustibles"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.RubrosGasto.AnyAsync(r => r.Codigo == 3 && r.Nombre == "Combustibles"));
    }

    [Fact]
    public async Task PostRubros_CodigoDuplicado_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 5, Nombre = "Papelería", Activo = true });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(5, "Otro nombre"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostRubros_CodigoInvalido_Devuelve400()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/rubros",
            new CrearRubroGastoRequest(0, "Sin código"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PutRubros_ConTokenAdmin_ModificaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var rubro = new RubroGasto { Codigo = 7, Nombre = "Original", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PutAsJsonAsync($"/finanzas/rubros/{rubro.Id}",
            new ModificarRubroGastoRequest(7, "Modificado"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.RubrosGasto.SingleAsync(r => r.Id == rubro.Id);
        Assert.Equal("Modificado", actualizado.Nombre);
    }

    [Fact]
    public async Task DeleteRubros_ConTokenAdmin_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var rubro = new RubroGasto { Codigo = 8, Nombre = "Para Baja", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.DeleteAsync($"/finanzas/rubros/{rubro.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var actualizado = await verificacion.RubrosGasto.SingleAsync(r => r.Id == rubro.Id);
        Assert.False(actualizado.Activo);
    }

    [Fact]
    public async Task GetRubrosActivos_ConTokenOperador_FiltraInactivos()
    {
        await using var ctx = Factory.CrearContexto();
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 1, Nombre = "Activo", Activo = true });
        ctx.RubrosGasto.Add(new RubroGasto { Codigo = 2, Nombre = "Inactivo", Activo = false });
        await ctx.SaveChangesAsync();

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync("/finanzas/rubros/activos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rubros = await response.Content.ReadFromJsonAsync<List<RubroGastoDto>>();
        Assert.Contains(rubros!, r => r.Nombre == "Activo");
        Assert.DoesNotContain(rubros!, r => r.Nombre == "Inactivo");
    }
}
```

`tests/StockApp.Api.Tests/LineasPoaEndpointTests.cs`:

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

public class LineasPoaEndpointTests : ApiTestBase
{
    public LineasPoaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private async Task<int> SeedFuenteAsync(string nombre)
    {
        await using var ctx = Factory.CrearContexto();
        var fuente = new FuenteFinanciamiento { Nombre = nombre, Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        await ctx.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task GetLineasPoa_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/lineas-poa");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostLineasPoa_ConAsignaciones_CreaYDevuelve201()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("COMPOSTERAS", "Ambiente", 2026, new List<AsignacionPresupuestalRequest>
            {
                new(fuenteB, 100000m),
                new(fuenteC, 50000m),
            }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa
            .Include(l => l.Asignaciones)
            .SingleAsync(l => l.Nombre == "COMPOSTERAS");
        Assert.Equal(2, linea.Asignaciones.Count);
    }

    [Fact]
    public async Task PostLineasPoa_SinAsignaciones_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("PRENSA", "Comunicación", 2026, new List<AsignacionPresupuestalRequest>()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostLineasPoa_NombreYEjercicioDuplicados_Devuelve409()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        var request = new CrearLineaPoaRequest("Rambla", "Obras", 2026,
            new List<AsignacionPresupuestalRequest> { new(fuente, 1000m) });

        var primera = await client.PostAsJsonAsync("/finanzas/lineas-poa", request);
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);

        var segunda = await client.PostAsJsonAsync("/finanzas/lineas-poa", request);
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }

    [Fact]
    public async Task PutLineasPoa_ReemplazaAsignaciones_Devuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuenteB = await SeedFuenteAsync("Literal B");
        var fuenteC = await SeedFuenteAsync("Literal C");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var alta = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("PRENSA", "Comunicación", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuenteB, 80000m) }));
        Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
        var creado = await alta.Content.ReadFromJsonAsync<IdCreadoResponse>();

        var response = await client.PutAsJsonAsync($"/finanzas/lineas-poa/{creado!.Id}",
            new ModificarLineaPoaRequest("PRENSA", "Prensa y Comunicación", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuenteC, 120000m) }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa
            .Include(l => l.Asignaciones)
            .SingleAsync(l => l.Id == creado.Id);
        Assert.Equal("Prensa y Comunicación", linea.Programa);
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal(fuenteC, asignacion.FuenteFinanciamientoId);
        Assert.Equal(120000m, asignacion.Monto);
    }

    [Fact]
    public async Task DeleteLineasPoa_HaceBajaLogicaYDevuelve200()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var alta = await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("Eventos", "Cultura", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuente, 5000m) }));
        var creado = await alta.Content.ReadFromJsonAsync<IdCreadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/lineas-poa/{creado!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var linea = await verificacion.LineasPoa.SingleAsync(l => l.Id == creado.Id);
        Assert.False(linea.Activo);
    }

    [Fact]
    public async Task GetLineasPoa_DevuelveAsignacionesConNombreDeFuente()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        var fuente = await SeedFuenteAsync("Literal B");

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/lineas-poa",
            new CrearLineaPoaRequest("Rambla", "Obras", 2026,
                new List<AsignacionPresupuestalRequest> { new(fuente, 300000m) }));

        var response = await client.GetAsync("/finanzas/lineas-poa");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lineas = await response.Content.ReadFromJsonAsync<List<LineaPoaDto>>();
        var linea = Assert.Single(lineas!, l => l.Nombre == "Rambla");
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal("Literal B", asignacion.FuenteFinanciamientoNombre);
        Assert.Equal(300000m, asignacion.Monto);
    }
}

/// <summary>Shape del body de los 201 ({ "id": n }) para deserializar en los tests.</summary>
public record IdCreadoResponse(int Id);
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~FuentesFinanciamientoEndpoint|FullyQualifiedName~RubrosGastoEndpoint|FullyQualifiedName~LineasPoaEndpoint"`
Expected: FALLA la compilación con `CS0246` (`FuenteFinanciamientoDto` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los endpoints**

`src/StockApp.Api/Endpoints/FuentesFinanciamientoEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearFuenteFinanciamientoRequest(string Nombre);
public record ModificarFuenteFinanciamientoRequest(string Nombre);
public record FuenteFinanciamientoDto(int Id, string Nombre, bool Activo);

public static class FuentesFinanciamientoEndpoints
{
    public static IEndpointRouteBuilder MapFuentesFinanciamientoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/fuentes");

        group.MapGet("/", async (IFuenteFinanciamientoService fuentes) =>
            Results.Ok((await fuentes.ListarTodasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearFuenteFinanciamientoRequest request, IFuenteFinanciamientoService fuentes) =>
        {
            var id = await fuentes.AltaAsync(new FuenteFinanciamiento { Nombre = request.Nombre });
            // Sin Location: no existe GET /finanzas/fuentes/{id} (mismo criterio que /categorias).
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarFuenteFinanciamientoRequest request, IFuenteFinanciamientoService fuentes) =>
        {
            await fuentes.ModificarAsync(new FuenteFinanciamiento { Id = id, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, IFuenteFinanciamientoService fuentes) =>
        {
            await fuentes.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activas", async (IFuenteFinanciamientoService fuentes) =>
            Results.Ok((await fuentes.ListarActivasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static FuenteFinanciamientoDto ADto(FuenteFinanciamiento f) => new(f.Id, f.Nombre, f.Activo);
}
```

`src/StockApp.Api/Endpoints/RubrosGastoEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record CrearRubroGastoRequest(int Codigo, string Nombre);
public record ModificarRubroGastoRequest(int Codigo, string Nombre);
public record RubroGastoDto(int Id, int Codigo, string Nombre, bool Activo);

public static class RubrosGastoEndpoints
{
    public static IEndpointRouteBuilder MapRubrosGastoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/rubros");

        group.MapGet("/", async (IRubroGastoService rubros) =>
            Results.Ok((await rubros.ListarTodosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearRubroGastoRequest request, IRubroGastoService rubros) =>
        {
            var id = await rubros.AltaAsync(new RubroGasto { Codigo = request.Codigo, Nombre = request.Nombre });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarRubroGastoRequest request, IRubroGastoService rubros) =>
        {
            await rubros.ModificarAsync(new RubroGasto { Id = id, Codigo = request.Codigo, Nombre = request.Nombre });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, IRubroGastoService rubros) =>
        {
            await rubros.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activos", async (IRubroGastoService rubros) =>
            Results.Ok((await rubros.ListarActivosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static RubroGastoDto ADto(RubroGasto r) => new(r.Id, r.Codigo, r.Nombre, r.Activo);
}
```

`src/StockApp.Api/Endpoints/LineasPoaEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record AsignacionPresupuestalRequest(int FuenteFinanciamientoId, decimal Monto);
public record CrearLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones);
public record ModificarLineaPoaRequest(string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalRequest> Asignaciones);
public record AsignacionPresupuestalDto(int Id, int FuenteFinanciamientoId, string? FuenteFinanciamientoNombre, decimal Monto);
public record LineaPoaDto(int Id, string Nombre, string Programa, int Ejercicio, bool Activo, List<AsignacionPresupuestalDto> Asignaciones);

public static class LineasPoaEndpoints
{
    public static IEndpointRouteBuilder MapLineasPoaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/lineas-poa");

        group.MapGet("/", async (ILineaPoaService lineas) =>
            Results.Ok((await lineas.ListarTodasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPost("/", async (CrearLineaPoaRequest request, ILineaPoaService lineas) =>
        {
            var id = await lineas.AltaAsync(AEntidad(0, request.Nombre, request.Programa, request.Ejercicio, request.Asignaciones));
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapPut("/{id:int}", async (int id, ModificarLineaPoaRequest request, ILineaPoaService lineas) =>
        {
            await lineas.ModificarAsync(AEntidad(id, request.Nombre, request.Programa, request.Ejercicio, request.Asignaciones));
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapDelete("/{id:int}", async (int id, ILineaPoaService lineas) =>
        {
            await lineas.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarMaestrosFinanzas);

        group.MapGet("/activas", async (ILineaPoaService lineas) =>
            Results.Ok((await lineas.ListarActivasAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }

    private static LineaPoa AEntidad(int id, string nombre, string programa, int ejercicio,
        List<AsignacionPresupuestalRequest> asignaciones) => new()
    {
        Id = id,
        Nombre = nombre,
        Programa = programa,
        Ejercicio = ejercicio,
        Asignaciones = asignaciones
            .Select(a => new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
            })
            .ToList(),
    };

    private static LineaPoaDto ADto(LineaPoa l) => new(
        l.Id, l.Nombre, l.Programa, l.Ejercicio, l.Activo,
        l.Asignaciones
            .Select(a => new AsignacionPresupuestalDto(
                a.Id, a.FuenteFinanciamientoId, a.FuenteFinanciamiento?.Nombre, a.Monto))
            .ToList());
}
```

- [ ] **Step 4: Registrar DI y mapear endpoints en Program.cs**

En `src/StockApp.Api/Program.cs`:

1. Agregar el using junto a los demás de Application: `using StockApp.Application.Finanzas;`
2. Después del bloque "Catálogo — tablas maestras (Fase 2b)" (tras la línea del comentario de `IUnidadMedidaRepository`), agregar:

```csharp
// Finanzas — Fase 1: maestros (fuentes, rubros, líneas POA + asignaciones)
builder.Services.AddScoped<IFuenteFinanciamientoRepository, FuenteFinanciamientoRepository>();
builder.Services.AddScoped<IFuenteFinanciamientoService, FuenteFinanciamientoService>();
builder.Services.AddScoped<IRubroGastoRepository, RubroGastoRepository>();
builder.Services.AddScoped<IRubroGastoService, RubroGastoService>();
builder.Services.AddScoped<ILineaPoaRepository, LineaPoaRepository>();
builder.Services.AddScoped<ILineaPoaService, LineaPoaService>();
```

3. Después de `app.MapUnidadesMedidaEndpoints();`, agregar:

```csharp
app.MapFuentesFinanciamientoEndpoints();
app.MapRubrosGastoEndpoints();
app.MapLineasPoaEndpoints();
```

(Las policies `finanzas.ver` y `finanzas.maestros` NO se declaran a mano: el `foreach (var permiso in Permisos.Todos)` existente las deriva de `AuthorizationService.TienePermiso` — Task 3 ya agregó ambos permisos a `Permisos.Todos` y a `AccionesOperador`.)

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~FuentesFinanciamientoEndpoint|FullyQualifiedName~RubrosGastoEndpoint|FullyQualifiedName~LineasPoaEndpoint"`
Expected: los 21 tests nuevos en verde (requiere Docker).

- [ ] **Step 6: Suite completa de Api**

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: verde (sin regresiones — en particular los tests existentes de policies).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(finanzas): endpoints /finanzas/fuentes, /finanzas/rubros y /finanzas/lineas-poa"
```

---

### Task 5: ApiClient — clients HTTP que implementan las mismas interfaces de Application

**Files:**
- Create: `src/StockApp.ApiClient/FuenteFinanciamientoApiClient.cs`
- Create: `src/StockApp.ApiClient/RubroGastoApiClient.cs`
- Create: `src/StockApp.ApiClient/LineaPoaApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/FuenteFinanciamientoApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/RubroGastoApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/LineaPoaApiClientTests.cs`

**Interfaces:**
- Consumes: `IFuenteFinanciamientoService`/`IRubroGastoService`/`ILineaPoaService` (Task 3), contratos wire de Task 4, `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` e `IdCreado` (infra existente de `StockApp.ApiClient`), `FakeHttpHandler`/`TestHttp` (test infra existente).
- Produces: `FuenteFinanciamientoApiClient : IFuenteFinanciamientoService`, `RubroGastoApiClient : IRubroGastoService`, `LineaPoaApiClient : ILineaPoaService` — los ViewModels de Task 6 los consumen SOLO vía las interfaces.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.ApiClient.Tests/FuenteFinanciamientoApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class FuenteFinanciamientoApiClientTests
{
    [Fact]
    public async Task ListarTodas_GETFinanzasFuentes_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, nombre = "Literal B", activo = true },
            new { id = 2, nombre = "Multas", activo = false },
        }));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var fuentes = await client.ListarTodasAsync();

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Equal(2, fuentes.Count);
        Assert.Equal("Literal B", fuentes[0].Nombre);
        Assert.False(fuentes[1].Activo);
    }

    [Fact]
    public async Task ListarActivas_GETFinanzasFuentesActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var fuentes = await client.ListarActivasAsync();

        Assert.Equal("/finanzas/fuentes/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(fuentes);
    }

    [Fact]
    public async Task Alta_POSTFinanzasFuentes_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Multas\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new FuenteFinanciamiento { Id = 3, Nombre = "Literal C" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"Literal C\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasFuentesId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/fuentes/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe una fuente de financiamiento con el nombre 'Multas'."));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new FuenteFinanciamiento { Nombre = "Multas" }));

        Assert.Equal("Ya existe una fuente de financiamiento con el nombre 'Multas'.", ex.Message);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Fuente de financiamiento 99 no encontrada."));
        var client = new FuenteFinanciamientoApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

`tests/StockApp.ApiClient.Tests/RubroGastoApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class RubroGastoApiClientTests
{
    [Fact]
    public async Task ListarTodos_GETFinanzasRubros_MapeaLasEntidades()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new { id = 1, codigo = 3, nombre = "Combustibles", activo = true },
            new { id = 2, codigo = 5, nombre = "Papelería", activo = false },
        }));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var rubros = await client.ListarTodosAsync();

        Assert.Equal("/finanzas/rubros", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Equal(2, rubros.Count);
        Assert.Equal(3, rubros[0].Codigo);
        Assert.Equal("Combustibles", rubros[0].Nombre);
        Assert.False(rubros[1].Activo);
    }

    [Fact]
    public async Task ListarActivos_GETFinanzasRubrosActivos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var rubros = await client.ListarActivosAsync();

        Assert.Equal("/finanzas/rubros/activos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(rubros);
    }

    [Fact]
    public async Task Alta_POSTFinanzasRubros_EnviaCodigoYNombre_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 9 }, HttpStatusCode.Created));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" });

        Assert.Equal("/finanzas/rubros", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":3", fake.UltimoBody);
        Assert.Contains("\"nombre\":\"Combustibles\"", fake.UltimoBody);
        Assert.Equal(9, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new RubroGasto { Id = 3, Codigo = 4, Nombre = "Lubricantes" });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/rubros/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"codigo\":4", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasRubrosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/rubros/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe un rubro con el código 3."));
        var client = new RubroGastoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(new RubroGasto { Codigo = 3, Nombre = "Combustibles" }));

        Assert.Equal("Ya existe un rubro con el código 3.", ex.Message);
    }
}
```

`tests/StockApp.ApiClient.Tests/LineaPoaApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class LineaPoaApiClientTests
{
    private static LineaPoa LineaConAsignaciones() => new()
    {
        Nombre = "COMPOSTERAS",
        Programa = "Ambiente",
        Ejercicio = 2026,
        Asignaciones =
        {
            new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 100000m },
            new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 50000m },
        },
    };

    [Fact]
    public async Task ListarTodas_GETFinanzasLineasPoa_MapeaLineaYAsignaciones()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, nombre = "COMPOSTERAS", programa = "Ambiente", ejercicio = 2026, activo = true,
                asignaciones = new[]
                {
                    new { id = 10, fuenteFinanciamientoId = 1, fuenteFinanciamientoNombre = "Literal B", monto = 100000m },
                },
            },
        }));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var lineas = await client.ListarTodasAsync();

        Assert.Equal("/finanzas/lineas-poa", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var linea = Assert.Single(lineas);
        Assert.Equal("COMPOSTERAS", linea.Nombre);
        Assert.Equal(2026, linea.Ejercicio);
        var asignacion = Assert.Single(linea.Asignaciones);
        Assert.Equal(1, asignacion.FuenteFinanciamientoId);
        Assert.Equal(100000m, asignacion.Monto);
        // El nombre de la fuente llega mapeado a la nav para que la grilla lo muestre
        Assert.Equal("Literal B", asignacion.FuenteFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task ListarActivas_GETFinanzasLineasPoaActivas()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var lineas = await client.ListarActivasAsync();

        Assert.Equal("/finanzas/lineas-poa/activas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(lineas);
    }

    [Fact]
    public async Task Alta_POSTFinanzasLineasPoa_EnviaAsignaciones_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 4 }, HttpStatusCode.Created));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(LineaConAsignaciones());

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"nombre\":\"COMPOSTERAS\"", fake.UltimoBody);
        Assert.Contains("\"ejercicio\":2026", fake.UltimoBody);
        Assert.Contains("\"fuenteFinanciamientoId\":1", fake.UltimoBody);
        Assert.Contains("\"fuenteFinanciamientoId\":2", fake.UltimoBody);
        Assert.Equal(4, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta_SinIdEnElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var linea = LineaConAsignaciones();
        linea.Id = 4;
        await client.ModificarAsync(linea);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasLineasPoaId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/lineas-poa/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "La línea POA debe tener al menos una asignación presupuestal."));
        var client = new LineaPoaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(LineaConAsignaciones()));

        Assert.Equal("La línea POA debe tener al menos una asignación presupuestal.", ex.Message);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~FuenteFinanciamientoApiClient|FullyQualifiedName~RubroGastoApiClient|FullyQualifiedName~LineaPoaApiClient"`
Expected: FALLA la compilación con `CS0246` (`FuenteFinanciamientoApiClient` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los clients**

`src/StockApp.ApiClient/FuenteFinanciamientoApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record FuenteFinanciamientoWire(int Id, string Nombre, bool Activo);
internal sealed record FuenteFinanciamientoBody(string Nombre);

/// <summary>
/// IFuenteFinanciamientoService contra /finanzas/fuentes. La interfaz habla en entidades
/// de dominio (así la consumen los VMs) y el wire habla en FuenteFinanciamientoDto: mapea.
/// </summary>
public sealed class FuenteFinanciamientoApiClient : IFuenteFinanciamientoService
{
    private readonly HttpClient _http;

    public FuenteFinanciamientoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(FuenteFinanciamiento fuente)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/fuentes", new FuenteFinanciamientoBody(fuente.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la fuente de financiamiento.");
        return creado.Id;
    }

    public async Task ModificarAsync(FuenteFinanciamiento fuente)
    {
        // El id de ruta es la única fuente; el body no lleva Id (3a, D1).
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/fuentes/{fuente.Id}", new FuenteFinanciamientoBody(fuente.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/fuentes/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => ListarAsync("finanzas/fuentes");

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() => ListarAsync("finanzas/fuentes/activas");

    private async Task<IReadOnlyList<FuenteFinanciamiento>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<FuenteFinanciamientoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static FuenteFinanciamiento AEntidad(FuenteFinanciamientoWire dto)
        => new() { Id = dto.Id, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

`src/StockApp.ApiClient/RubroGastoApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record RubroGastoWire(int Id, int Codigo, string Nombre, bool Activo);
internal sealed record RubroGastoBody(int Codigo, string Nombre);

/// <summary>IRubroGastoService contra /finanzas/rubros.</summary>
public sealed class RubroGastoApiClient : IRubroGastoService
{
    private readonly HttpClient _http;

    public RubroGastoApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(RubroGasto rubro)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/rubros", new RubroGastoBody(rubro.Codigo, rubro.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el rubro de gasto.");
        return creado.Id;
    }

    public async Task ModificarAsync(RubroGasto rubro)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/rubros/{rubro.Id}", new RubroGastoBody(rubro.Codigo, rubro.Nombre)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/rubros/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => ListarAsync("finanzas/rubros");

    public Task<IReadOnlyList<RubroGasto>> ListarActivosAsync() => ListarAsync("finanzas/rubros/activos");

    private async Task<IReadOnlyList<RubroGasto>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<RubroGastoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static RubroGasto AEntidad(RubroGastoWire dto)
        => new() { Id = dto.Id, Codigo = dto.Codigo, Nombre = dto.Nombre, Activo = dto.Activo };
}
```

`src/StockApp.ApiClient/LineaPoaApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record AsignacionPresupuestalWire(
    int Id, int FuenteFinanciamientoId, string? FuenteFinanciamientoNombre, decimal Monto);
internal sealed record LineaPoaWire(
    int Id, string Nombre, string Programa, int Ejercicio, bool Activo,
    List<AsignacionPresupuestalWire> Asignaciones);
internal sealed record AsignacionPresupuestalBody(int FuenteFinanciamientoId, decimal Monto);
internal sealed record LineaPoaBody(
    string Nombre, string Programa, int Ejercicio, List<AsignacionPresupuestalBody> Asignaciones);

/// <summary>
/// ILineaPoaService contra /finanzas/lineas-poa. El agregado viaja completo: alta y
/// modificación mandan la línea CON su lista de asignaciones presupuestales.
/// </summary>
public sealed class LineaPoaApiClient : ILineaPoaService
{
    private readonly HttpClient _http;

    public LineaPoaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(LineaPoa linea)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/lineas-poa", ABody(linea)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la línea POA.");
        return creado.Id;
    }

    public async Task ModificarAsync(LineaPoa linea)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/lineas-poa/{linea.Id}", ABody(linea)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/lineas-poa/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => ListarAsync("finanzas/lineas-poa");

    public Task<IReadOnlyList<LineaPoa>> ListarActivasAsync() => ListarAsync("finanzas/lineas-poa/activas");

    private async Task<IReadOnlyList<LineaPoa>> ListarAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync(ruta));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<LineaPoaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static LineaPoaBody ABody(LineaPoa linea) => new(
        linea.Nombre, linea.Programa, linea.Ejercicio,
        linea.Asignaciones
            .Select(a => new AsignacionPresupuestalBody(a.FuenteFinanciamientoId, a.Monto))
            .ToList());

    private static LineaPoa AEntidad(LineaPoaWire dto) => new()
    {
        Id = dto.Id,
        Nombre = dto.Nombre,
        Programa = dto.Programa,
        Ejercicio = dto.Ejercicio,
        Activo = dto.Activo,
        Asignaciones = dto.Asignaciones
            .Select(a => new AsignacionPresupuestal
            {
                Id = a.Id,
                LineaPoaId = dto.Id,
                FuenteFinanciamientoId = a.FuenteFinanciamientoId,
                Monto = a.Monto,
                // El nombre de la fuente se materializa en la nav para que la grilla
                // del desktop lo muestre sin otra llamada.
                FuenteFinanciamiento = a.FuenteFinanciamientoNombre is null
                    ? null
                    : new FuenteFinanciamiento
                    {
                        Id = a.FuenteFinanciamientoId,
                        Nombre = a.FuenteFinanciamientoNombre,
                    },
            })
            .ToList(),
    };
}
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~FuenteFinanciamientoApiClient|FullyQualifiedName~RubroGastoApiClient|FullyQualifiedName~LineaPoaApiClient"`
Expected: los 19 tests nuevos en verde.

- [ ] **Step 5: Suite completa de ApiClient**

Run: `dotnet test tests/StockApp.ApiClient.Tests`
Expected: verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.ApiClient tests/StockApp.ApiClient.Tests
git commit -m "feat(finanzas): api clients de maestros de finanzas"
```

---

### Task 6: Presentation — ViewModels de "Maestros de finanzas"

Estructura: `MaestrosFinanzasViewModel` (host de la pantalla, con tres sub-listas) + tres list VMs (patrón `CategoriaListViewModel`, con Editar al estilo `ProductoListViewModel.EditarCommand` + `CargarParaEditar`) + tres form VMs. Los formularios navegan de vuelta a `MaestrosFinanzasViewModel` al guardar.

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaListViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FuenteFinanciamientoViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/RubroGastoViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LineaPoaViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/MaestrosFinanzasViewModelTests.cs`

**Interfaces:**
- Consumes: `IFuenteFinanciamientoService`/`IRubroGastoService`/`ILineaPoaService` (Task 3), `INavigationService` (`Navegar<TVm>()` y `Navegar<TVm>(Action<TVm>)`), `IConfirmacionService` (`PreguntarAsync`/`InformarAsync`), `ViewModelBase`, `ReglaDeNegocioException`/`EntidadNoEncontradaException`.
- Produces:
  - `MaestrosFinanzasViewModel`: props `FuentesVm`, `RubrosVm`, `LineasPoaVm`; `Task CargarAsync()`
  - List VMs: `ObservableCollection<T> Items`, `T? ItemSeleccionado`, `Task CargarAsync()`, `NuevoCommand`, `EditarCommand`, `BajaCommand`
  - Form VMs: `GuardarCommand`, `string? MensajeError`, `bool EsEdicion`, `string Titulo`, `void CargarParaEditar(T item)`; LineaPoa además `ObservableCollection<AsignacionItemViewModel> Asignaciones`, `Task InicializarAsync()`, `AgregarAsignacionCommand`, `QuitarAsignacionCommand`
  - `AsignacionItemViewModel`: `FuenteFinanciamiento? FuenteSeleccionada`, `string MontoTexto`

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FuenteFinanciamientoViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FuenteFinanciamientoListViewModelTests
{
    private static (FuenteFinanciamientoListViewModel vm,
                    Mock<IFuenteFinanciamientoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<FuenteFinanciamiento>? fuentes = null)
    {
        var svcMock = new Mock<IFuenteFinanciamientoService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ReturnsAsync(fuentes ?? new List<FuenteFinanciamiento>());
        svcMock.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirmMock.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new FuenteFinanciamientoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock, confirmMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, svcMock, _, _) = Crear(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B" },
            new() { Id = 2, Nombre = "Multas" },
        });

        await vm.CargarAsync();

        svcMock.Verify(s => s.ListarTodasAsync(), Times.Once);
        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Literal B", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, navMock, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<FuenteFinanciamientoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaAlFormularioEnModoEdicion()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Literal C", Activo = true };
        var (vm, _, navMock, _) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        await vm.EditarCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<FuenteFinanciamientoFormViewModel>(
            It.IsAny<System.Action<FuenteFinanciamientoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
    }

    [Fact]
    public async Task BajaCommand_ConfirmaYLlamaServicio()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Once);
        svcMock.Verify(s => s.BajaLogicaAsync(5), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ExcepcionDeDominio_NoPropagaYInforma()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = true };
        var (vm, svcMock, _, confirmMock) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        var mensaje = "La fuente de financiamiento 5 ya está inactiva.";
        svcMock.Setup(s => s.BajaLogicaAsync(5)).ThrowsAsync(new ReglaDeNegocioException(mensaje));

        await vm.BajaCommand.ExecuteAsync(null);

        confirmMock.Verify(c => c.InformarAsync(mensaje), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ItemInactivo_EstaDeshabilitado()
    {
        var fuente = new FuenteFinanciamiento { Id = 5, Nombre = "Multas", Activo = false };
        var (vm, _, _, _) = Crear(new List<FuenteFinanciamiento> { fuente });
        await vm.CargarAsync();
        vm.ItemSeleccionado = fuente;

        Assert.False(vm.BajaCommand.CanExecute(null));
    }
}

public class FuenteFinanciamientoFormViewModelTests
{
    private static (FuenteFinanciamientoFormViewModel vm,
                    Mock<IFuenteFinanciamientoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IFuenteFinanciamientoService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<FuenteFinanciamiento>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<FuenteFinanciamiento>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new FuenteFinanciamientoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_SinEdicion_LlamaAltaYVuelveAMaestros()
    {
        var (vm, svcMock, navMock) = Crear();
        vm.Nombre = "Literal B";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<FuenteFinanciamiento>(f => f.Nombre == "Literal B")), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificarConElId()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new FuenteFinanciamiento { Id = 3, Nombre = "Literal C", Activo = true });
        vm.Nombre = "Literal C (FIGM)";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<FuenteFinanciamiento>(f => f.Id == 3 && f.Nombre == "Literal C (FIGM)")), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_ReglaDeNegocio_MuestraMensajeSinNavegar()
    {
        var (vm, svcMock, navMock) = Crear();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<FuenteFinanciamiento>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe una fuente de financiamiento con el nombre 'Multas'."));
        vm.Nombre = "Multas";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe una fuente de financiamiento con el nombre 'Multas'.", vm.MensajeError);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Never);
    }

    [Fact]
    public void GuardarCommand_SinNombre_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
```

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/RubroGastoViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class RubroGastoListViewModelTests
{
    private static (RubroGastoListViewModel vm,
                    Mock<IRubroGastoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear(IReadOnlyList<RubroGasto>? rubros = null)
    {
        var svcMock = new Mock<IRubroGastoService>();
        svcMock.Setup(s => s.ListarTodosAsync()).ReturnsAsync(rubros ?? new List<RubroGasto>());

        var navMock = new Mock<INavigationService>();
        var confirmMock = new Mock<IConfirmacionService>();
        confirmMock.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new RubroGastoListViewModel(svcMock.Object, navMock.Object, confirmMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, _, _) = Crear(new List<RubroGasto>
        {
            new() { Id = 1, Codigo = 1, Nombre = "Sueldos" },
            new() { Id = 2, Codigo = 3, Nombre = "Combustibles" },
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Sueldos", vm.Items[0].Nombre);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, navMock) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        navMock.Verify(n => n.Navegar<RubroGastoFormViewModel>(), Times.Once);
    }
}

public class RubroGastoFormViewModelTests
{
    private static (RubroGastoFormViewModel vm,
                    Mock<IRubroGastoService> svcMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<IRubroGastoService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<RubroGasto>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<RubroGasto>())).Returns(Task.CompletedTask);

        var navMock = new Mock<INavigationService>();
        var vm = new RubroGastoFormViewModel(svcMock.Object, navMock.Object);
        return (vm, svcMock, navMock);
    }

    [Fact]
    public async Task GuardarCommand_ConCodigoYNombre_LlamaAltaConElCodigoParseado()
    {
        var (vm, svcMock, navMock) = Crear();
        vm.CodigoTexto = "3";
        vm.Nombre = "Combustibles";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(
            It.Is<RubroGasto>(r => r.Codigo == 3 && r.Nombre == "Combustibles")), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public void GuardarCommand_CodigoNoNumerico_EstaDeshabilitado()
    {
        var (vm, _, _) = Crear();
        vm.CodigoTexto = "abc";
        vm.Nombre = "Combustibles";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarCommand_EnEdicion_LlamaModificar()
    {
        var (vm, svcMock, _) = Crear();
        vm.CargarParaEditar(new RubroGasto { Id = 4, Codigo = 5, Nombre = "Papelería", Activo = true });
        vm.Nombre = "Papelería y Librería";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.True(vm.EsEdicion);
        svcMock.Verify(s => s.ModificarAsync(
            It.Is<RubroGasto>(r => r.Id == 4 && r.Codigo == 5 && r.Nombre == "Papelería y Librería")), Times.Once);
    }
}
```

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LineaPoaViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class LineaPoaListViewModelTests
{
    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var svcMock = new Mock<ILineaPoaService>();
        svcMock.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 },
        });
        var vm = new LineaPoaListViewModel(
            svcMock.Object, new Mock<INavigationService>().Object, new Mock<IConfirmacionService>().Object);

        await vm.CargarAsync();

        Assert.Single(vm.Items);
        Assert.Equal("Rambla", vm.Items[0].Nombre);
    }
}

public class LineaPoaFormViewModelTests
{
    private static (LineaPoaFormViewModel vm,
                    Mock<ILineaPoaService> svcMock,
                    Mock<IFuenteFinanciamientoService> fuentesMock,
                    Mock<INavigationService> navMock)
        Crear()
    {
        var svcMock = new Mock<ILineaPoaService>();
        svcMock.Setup(s => s.AltaAsync(It.IsAny<LineaPoa>())).ReturnsAsync(1);
        svcMock.Setup(s => s.ModificarAsync(It.IsAny<LineaPoa>())).Returns(Task.CompletedTask);

        var fuentesMock = new Mock<IFuenteFinanciamientoService>();
        fuentesMock.Setup(s => s.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 1, Nombre = "Literal B", Activo = true },
            new() { Id = 2, Nombre = "Literal C", Activo = true },
        });

        var navMock = new Mock<INavigationService>();
        var vm = new LineaPoaFormViewModel(svcMock.Object, fuentesMock.Object, navMock.Object);
        return (vm, svcMock, fuentesMock, navMock);
    }

    [Fact]
    public async Task InicializarAsync_CargaFuentesDisponiblesYUnaFilaVacia()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        Assert.Equal(2, vm.FuentesDisponibles.Count);
        Assert.Single(vm.Asignaciones);  // arranca con una fila lista para completar
    }

    [Fact]
    public async Task AgregarYQuitarAsignacion_ModificanLaColeccion()
    {
        var (vm, _, _, _) = Crear();
        await vm.InicializarAsync();

        vm.AgregarAsignacionCommand.Execute(null);
        Assert.Equal(2, vm.Asignaciones.Count);

        vm.QuitarAsignacionCommand.Execute(vm.Asignaciones[1]);
        Assert.Single(vm.Asignaciones);
    }

    [Fact]
    public async Task GuardarCommand_ConDatosValidos_LlamaAltaConLasAsignaciones()
    {
        var (vm, svcMock, _, navMock) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "COMPOSTERAS";
        vm.Programa = "Ambiente";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.Asignaciones[0].MontoTexto = "100000";

        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.AltaAsync(It.Is<LineaPoa>(l =>
            l.Nombre == "COMPOSTERAS"
            && l.Ejercicio == 2026
            && l.Asignaciones.Count == 1
            && l.Asignaciones[0].FuenteFinanciamientoId == 1
            && l.Asignaciones[0].Monto == 100000m)), Times.Once);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }

    [Fact]
    public async Task GuardarCommand_AsignacionSinFuente_MuestraErrorSinLlamarServicio()
    {
        var (vm, svcMock, _, _) = Crear();
        await vm.InicializarAsync();
        vm.Nombre = "PRENSA";
        vm.Programa = "Comunicación";
        vm.EjercicioTexto = "2026";
        vm.Asignaciones[0].MontoTexto = "100";  // sin FuenteSeleccionada

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svcMock.Verify(s => s.AltaAsync(It.IsAny<LineaPoa>()), Times.Never);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaCamposYAsignaciones()
    {
        var (vm, svcMock, _, _) = Crear();
        vm.CargarParaEditar(new LineaPoa
        {
            Id = 4, Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, Activo = true,
            Asignaciones =
            {
                new AsignacionPresupuestal
                {
                    Id = 10, LineaPoaId = 4, FuenteFinanciamientoId = 2, Monto = 50000m,
                    FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal C" },
                },
            },
        });
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("COMPOSTERAS", vm.Nombre);
        Assert.Equal("2026", vm.EjercicioTexto);
        var fila = Assert.Single(vm.Asignaciones);
        Assert.Equal(2, fila.FuenteSeleccionada!.Id);
        Assert.Equal("50000", fila.MontoTexto);

        vm.Nombre = "COMPOSTERAS II";
        await vm.GuardarCommand.ExecuteAsync(null);

        svcMock.Verify(s => s.ModificarAsync(It.Is<LineaPoa>(l =>
            l.Id == 4 && l.Nombre == "COMPOSTERAS II" && l.Asignaciones.Count == 1)), Times.Once);
    }

    [Fact]
    public void GuardarCommand_EjercicioNoNumerico_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();
        vm.Nombre = "Rambla";
        vm.Programa = "Obras";
        vm.EjercicioTexto = "no-es-un-año";

        Assert.False(vm.GuardarCommand.CanExecute(null));
    }
}
```

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/MaestrosFinanzasViewModelTests.cs`:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class MaestrosFinanzasViewModelTests
{
    [Fact]
    public async Task CargarAsync_CargaLasTresSubListas()
    {
        var fuentesSvc = new Mock<IFuenteFinanciamientoService>();
        fuentesSvc.Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "Literal B" } });
        var rubrosSvc = new Mock<IRubroGastoService>();
        rubrosSvc.Setup(s => s.ListarTodosAsync())
            .ReturnsAsync(new List<RubroGasto> { new() { Id = 1, Codigo = 1, Nombre = "Sueldos" } });
        var lineasSvc = new Mock<ILineaPoaService>();
        lineasSvc.Setup(s => s.ListarTodasAsync())
            .ReturnsAsync(new List<LineaPoa> { new() { Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 } });

        var nav = new Mock<INavigationService>().Object;
        var confirm = new Mock<IConfirmacionService>().Object;

        var vm = new MaestrosFinanzasViewModel(
            new FuenteFinanciamientoListViewModel(fuentesSvc.Object, nav, confirm),
            new RubroGastoListViewModel(rubrosSvc.Object, nav, confirm),
            new LineaPoaListViewModel(lineasSvc.Object, nav, confirm));

        await vm.CargarAsync();

        Assert.Single(vm.FuentesVm.Items);
        Assert.Single(vm.RubrosVm.Items);
        Assert.Single(vm.LineasPoaVm.Items);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~StockApp.Presentation.Tests.ViewModels.Finanzas"`
Expected: FALLA la compilación con `CS0246` (`FuenteFinanciamientoListViewModel` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los ViewModels**

`src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoListViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Sub-lista de fuentes de financiamiento dentro de "Maestros de finanzas".
/// Alta/edición navegan al formulario; baja lógica con confirmación.
/// </summary>
public partial class FuenteFinanciamientoListViewModel : ViewModelBase
{
    private readonly IFuenteFinanciamientoService _service;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private FuenteFinanciamiento? _itemSeleccionado;

    public ObservableCollection<FuenteFinanciamiento> Items { get; } = new();

    public FuenteFinanciamientoListViewModel(
        IFuenteFinanciamientoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodasAsync();
        Items.Clear();
        foreach (var f in resultados)
            Items.Add(f);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<FuenteFinanciamientoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<FuenteFinanciamientoFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la fuente de financiamiento \"{ItemSeleccionado.Nombre}\"?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/FuenteFinanciamientoFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de una fuente de financiamiento.</summary>
public partial class FuenteFinanciamientoFormViewModel : ViewModelBase
{
    private readonly IFuenteFinanciamientoService _service;
    private readonly INavigationService           _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar fuente de financiamiento" : "Nueva fuente de financiamiento";

    public FuenteFinanciamientoFormViewModel(IFuenteFinanciamientoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    /// <summary>Precarga el formulario en modo edición (llamado por el overload de Navegar).</summary>
    public void CargarParaEditar(FuenteFinanciamiento fuente)
    {
        _idEdicion = fuente.Id;
        Nombre     = fuente.Nombre;
        EsEdicion  = true;
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Nombre);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new FuenteFinanciamiento { Id = _idEdicion, Nombre = Nombre });
            else
                await _service.AltaAsync(new FuenteFinanciamiento { Nombre = Nombre });

            _navigation.Navegar<MaestrosFinanzasViewModel>();
        }
        catch (System.Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or System.ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<MaestrosFinanzasViewModel>();
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoListViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Sub-lista de rubros de gasto dentro de "Maestros de finanzas".</summary>
public partial class RubroGastoListViewModel : ViewModelBase
{
    private readonly IRubroGastoService   _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private RubroGasto? _itemSeleccionado;

    public ObservableCollection<RubroGasto> Items { get; } = new();

    public RubroGastoListViewModel(
        IRubroGastoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodosAsync();
        Items.Clear();
        foreach (var r in resultados)
            Items.Add(r);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<RubroGastoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<RubroGastoFormViewModel>(vm => vm.CargarParaEditar(seleccionado)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el rubro \"{ItemSeleccionado.Nombre}\" (código {ItemSeleccionado.Codigo})?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/RubroGastoFormViewModel.cs`:

```csharp
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de un rubro de gasto (código numérico + nombre).</summary>
public partial class RubroGastoFormViewModel : ViewModelBase
{
    private readonly IRubroGastoService _service;
    private readonly INavigationService _navigation;

    private int _idEdicion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _codigoTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar rubro de gasto" : "Nuevo rubro de gasto";

    public RubroGastoFormViewModel(IRubroGastoService service, INavigationService navigation)
    {
        _service    = service;
        _navigation = navigation;
    }

    public void CargarParaEditar(RubroGasto rubro)
    {
        _idEdicion  = rubro.Id;
        CodigoTexto = rubro.Codigo.ToString();
        Nombre      = rubro.Nombre;
        EsEdicion   = true;
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Nombre)
           && int.TryParse(CodigoTexto, out var codigo)
           && codigo > 0;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        var codigo = int.Parse(CodigoTexto);
        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(new RubroGasto { Id = _idEdicion, Codigo = codigo, Nombre = Nombre });
            else
                await _service.AltaAsync(new RubroGasto { Codigo = codigo, Nombre = Nombre });

            _navigation.Navegar<MaestrosFinanzasViewModel>();
        }
        catch (System.Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or System.ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<MaestrosFinanzasViewModel>();
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaListViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Sub-lista de líneas POA dentro de "Maestros de finanzas".</summary>
public partial class LineaPoaListViewModel : ViewModelBase
{
    private readonly ILineaPoaService     _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private LineaPoa? _itemSeleccionado;

    public ObservableCollection<LineaPoa> Items { get; } = new();

    public LineaPoaListViewModel(
        ILineaPoaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        var resultados = await _service.ListarTodasAsync();
        Items.Clear();
        foreach (var l in resultados)
            Items.Add(l);
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<LineaPoaFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionada = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<LineaPoaFormViewModel>(vm => vm.CargarParaEditar(seleccionada)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja la línea POA \"{ItemSeleccionado.Nombre}\" ({ItemSeleccionado.Ejercicio})?");
        if (!confirmar) return;

        try
        {
            await _service.BajaLogicaAsync(ItemSeleccionado.Id);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/LineaPoaFormViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Fila editable de la grilla de asignaciones presupuestales (fuente + monto).</summary>
public partial class AsignacionItemViewModel : ObservableObject
{
    [ObservableProperty]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    private string _montoTexto = string.Empty;
}

/// <summary>
/// Formulario de alta / edición de una línea POA con su grilla de asignaciones
/// presupuestales por fuente (financiamiento mixto B+C). El agregado viaja completo
/// al servicio; las reglas finas (montos &gt; 0, sin fuentes repetidas) las valida
/// el servidor y acá se muestran vía MensajeError.
/// </summary>
public partial class LineaPoaFormViewModel : ViewModelBase
{
    private readonly ILineaPoaService             _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly INavigationService           _navigation;

    private int _idEdicion;
    private LineaPoa? _lineaParaEditar;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _nombre = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _programa = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _ejercicioTexto = System.DateTime.Now.Year.ToString();

    [ObservableProperty]
    private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar línea POA" : "Nueva línea POA";

    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<AsignacionItemViewModel> Asignaciones { get; } = new();

    public LineaPoaFormViewModel(
        ILineaPoaService service,
        IFuenteFinanciamientoService fuentesService,
        INavigationService navigation)
    {
        _service        = service;
        _fuentesService = fuentesService;
        _navigation     = navigation;
    }

    /// <summary>
    /// Precarga el modo edición. Corre ANTES de InicializarAsync (mismo contrato que
    /// ProductoFormViewModel.CargarParaEditar): guarda la línea y difiere el mapeo de las
    /// filas de asignaciones a InicializarAsync, que necesita FuentesDisponibles cargadas
    /// para resolver FuenteSeleccionada por Id.
    /// </summary>
    public void CargarParaEditar(LineaPoa linea)
    {
        _idEdicion       = linea.Id;
        _lineaParaEditar = linea;
        Nombre           = linea.Nombre;
        Programa         = linea.Programa;
        EjercicioTexto   = linea.Ejercicio.ToString();
        EsEdicion        = true;
    }

    /// <summary>Carga el combo de fuentes activas y arma las filas de asignaciones.</summary>
    public async Task InicializarAsync()
    {
        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        Asignaciones.Clear();
        if (_lineaParaEditar is not null)
        {
            foreach (var a in _lineaParaEditar.Asignaciones)
            {
                // Resuelve por Id contra el combo; si la fuente fue dada de baja después,
                // cae al objeto de la nav para no perder la fila histórica.
                var fuente = FuentesDisponibles.FirstOrDefault(f => f.Id == a.FuenteFinanciamientoId)
                    ?? a.FuenteFinanciamiento;
                if (fuente is not null && !FuentesDisponibles.Contains(fuente)
                    && FuentesDisponibles.All(f => f.Id != fuente.Id))
                    FuentesDisponibles.Add(fuente);

                Asignaciones.Add(new AsignacionItemViewModel
                {
                    FuenteSeleccionada = fuente,
                    MontoTexto = a.Monto.ToString("0.####"),
                });
            }
        }

        if (Asignaciones.Count == 0)
            Asignaciones.Add(new AsignacionItemViewModel());  // una fila lista para completar
    }

    [RelayCommand]
    private void AgregarAsignacion() => Asignaciones.Add(new AsignacionItemViewModel());

    [RelayCommand]
    private void QuitarAsignacion(AsignacionItemViewModel fila) => Asignaciones.Remove(fila);

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Nombre)
           && !string.IsNullOrWhiteSpace(Programa)
           && int.TryParse(EjercicioTexto, out var ejercicio)
           && ejercicio > 0;

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        var asignaciones = new List<AsignacionPresupuestal>();
        foreach (var fila in Asignaciones)
        {
            if (fila.FuenteSeleccionada is null
                || !decimal.TryParse(fila.MontoTexto, out var monto))
            {
                MensajeError = "Cada asignación necesita una fuente de financiamiento y un monto válido.";
                return;
            }

            asignaciones.Add(new AsignacionPresupuestal
            {
                FuenteFinanciamientoId = fila.FuenteSeleccionada.Id,
                Monto = monto,
            });
        }

        var linea = new LineaPoa
        {
            Id = EsEdicion ? _idEdicion : 0,
            Nombre = Nombre,
            Programa = Programa,
            Ejercicio = int.Parse(EjercicioTexto),
            Asignaciones = asignaciones,
        };

        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(linea);
            else
                await _service.AltaAsync(linea);

            _navigation.Navegar<MaestrosFinanzasViewModel>();
        }
        catch (System.Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or System.ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<MaestrosFinanzasViewModel>();
}
```

`src/StockApp.Presentation/ViewModels/Finanzas/MaestrosFinanzasViewModel.cs`:

```csharp
using System.Threading.Tasks;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Maestros de finanzas": hostea las tres sub-listas (fuentes, rubros,
/// líneas POA) que la vista muestra en tabs. Los formularios de alta/edición navegan
/// a pantalla completa y vuelven acá al guardar o cancelar.
/// </summary>
public partial class MaestrosFinanzasViewModel : ViewModelBase
{
    public FuenteFinanciamientoListViewModel FuentesVm { get; }
    public RubroGastoListViewModel RubrosVm { get; }
    public LineaPoaListViewModel LineasPoaVm { get; }

    public MaestrosFinanzasViewModel(
        FuenteFinanciamientoListViewModel fuentesVm,
        RubroGastoListViewModel rubrosVm,
        LineaPoaListViewModel lineasPoaVm)
    {
        FuentesVm   = fuentesVm;
        RubrosVm    = rubrosVm;
        LineasPoaVm = lineasPoaVm;
    }

    public async Task CargarAsync()
    {
        await FuentesVm.CargarAsync();
        await RubrosVm.CargarAsync();
        await LineasPoaVm.CargarAsync();
    }
}
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~StockApp.Presentation.Tests.ViewModels.Finanzas"`
Expected: los ~20 tests nuevos en verde.

- [ ] **Step 5: Suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas tests/StockApp.Presentation.Tests/ViewModels/Finanzas
git commit -m "feat(finanzas): viewmodels de maestros de finanzas"
```

---

### Task 7: Presentation — Views, navegación en el shell y registro DI

**Files:**
- Create: `src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/RubroGastoListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/LineaPoaListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoFormView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/RubroGastoFormView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Finanzas/LineaPoaFormView.axaml` (+ `.axaml.cs`)
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (comando NavMaestrosFinanzas)
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml` (sección "Finanzas" en el sidebar)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (registro DI de services + VMs)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ShellMainFinanzasTests.cs`

**Interfaces:**
- Consumes: VMs de Task 6, ApiClients de Task 5 (`FuenteFinanciamientoApiClient` etc.), `INavigationService`, ViewLocator por convención (`StockApp.Presentation.ViewModels.Finanzas.XxxViewModel` → `StockApp.Presentation.Views.Finanzas.XxxView` — el `Replace("ViewModel", "View")` sobre el FullName convierte también el segmento de namespace `ViewModels` → `Views`).
- Produces: `ShellMainViewModel.NavMaestrosFinanzasCommand` (`SeccionActiva = "MaestrosFinanzas"`).

- [ ] **Step 1: Escribir el test del shell que falla**

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ShellMainFinanzasTests.cs`:

```csharp
using Moq;
using StockApp.Application.Interfaces;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ShellMainFinanzasTests
{
    [Fact]
    public void NavMaestrosFinanzas_NavegaYMarcaSeccionActiva()
    {
        var navMock = new Mock<INavigationService>();
        var vm = new ShellMainViewModel(
            new Mock<ICurrentSession>().Object,
            navMock.Object,
            Mock.Of<IInfoApp>(i => i.Version == "0.0.0"));

        vm.NavMaestrosFinanzasCommand.Execute(null);

        Assert.Equal("MaestrosFinanzas", vm.SeccionActiva);
        navMock.Verify(n => n.Navegar<MaestrosFinanzasViewModel>(), Times.Once);
    }
}
```

Nota: `IInfoApp` vive en `StockApp.Application.Interfaces` (lo consume el ctor de `ShellMainViewModel` existente) — si el using difiere al compilar, copiar los usings del test existente más cercano de `ShellMainViewModel` o de `InicioViewModel`.

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainFinanzas"`
Expected: FALLA con `CS1061` (`NavMaestrosFinanzasCommand` no existe) — rojo confirmado.

- [ ] **Step 2: Comando de navegación en ShellMainViewModel**

En `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`:

1. Agregar el using: `using StockApp.Presentation.ViewModels.Finanzas;`
2. Agregar al final de la clase (después de `NavAuditoriaLog`):

```csharp
    // ── Finanzas — Fase 1: Admin y Operador ───────────────────────────────────

    [RelayCommand]
    private void NavMaestrosFinanzas()
    {
        SeccionActiva = "MaestrosFinanzas";
        _navigation.Navegar<MaestrosFinanzasViewModel>();
    }
```

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainFinanzas"`
Expected: verde.

- [ ] **Step 3: Sección "Finanzas" en el sidebar**

En `src/StockApp.Presentation/Views/ShellMainView.axaml`, después del botón de `NavHistorialMovimientosCommand` (antes del header "Tablas maestras"), agregar — visible para AMBOS roles (spec §9), por eso SIN `IsVisible="{Binding EsAdmin}"`:

```xml
                <!-- Finanzas: visible para Admin y Operador (spec Finanzas §9) -->
                <TextBlock Text="Finanzas"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           Opacity="0.6" />

                <Button Command="{Binding NavMaestrosFinanzasCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=MaestrosFinanzas}"
                        HorizontalAlignment="Stretch">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-cash-multiple" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Maestros de finanzas" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>
```

- [ ] **Step 4: Views de Finanzas**

`src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:views="using:StockApp.Presentation.Views.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="800" d:DesignHeight="600"
             x:Class="StockApp.Presentation.Views.Finanzas.MaestrosFinanzasView"
             x:DataType="vm:MaestrosFinanzasViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="Maestros de finanzas"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <TabControl>
            <TabItem Header="Fuentes de financiamiento">
                <views:FuenteFinanciamientoListView DataContext="{Binding FuentesVm}" />
            </TabItem>
            <TabItem Header="Rubros de gasto">
                <views:RubroGastoListView DataContext="{Binding RubrosVm}" />
            </TabItem>
            <TabItem Header="Líneas POA">
                <views:LineaPoaListView DataContext="{Binding LineasPoaVm}" />
            </TabItem>
        </TabControl>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/MaestrosFinanzasView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class MaestrosFinanzasView : UserControl
{
    public MaestrosFinanzasView()
    {
        InitializeComponent();
        // La carga de datos la cablea cada sub-vista (XxxListView) en su propio
        // DataContextChanged — acá no hay nada que inicializar.
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Finanzas.FuenteFinanciamientoListView"
             x:DataType="vm:FuenteFinanciamientoListViewModel">

    <Border Classes="card" Margin="0,12,0,0">
        <DockPanel>

            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
                <Button Classes="primary"
                        Content="Nueva fuente"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>

            <ListBox ItemsSource="{Binding Items}"
                     SelectedItem="{Binding ItemSeleccionado}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="16" Margin="4">
                            <TextBlock Text="{Binding Nombre}"
                                       Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}" />
                            <Border Classes="badge-inactiva" IsVisible="{Binding !Activo}">
                                <TextBlock Text="Inactiva" Classes="badge-inactiva-texto" />
                            </Border>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

        </DockPanel>
    </Border>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class FuenteFinanciamientoListView : UserControl
{
    public FuenteFinanciamientoListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan: cuando MaestrosFinanzasView asigna el
        // DataContext (binding a FuentesVm), se dispara la carga del listado.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is FuenteFinanciamientoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/RubroGastoListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Finanzas.RubroGastoListView"
             x:DataType="vm:RubroGastoListViewModel">

    <Border Classes="card" Margin="0,12,0,0">
        <DockPanel>

            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
                <Button Classes="primary"
                        Content="Nuevo rubro"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>

            <ListBox ItemsSource="{Binding Items}"
                     SelectedItem="{Binding ItemSeleccionado}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="16" Margin="4">
                            <StackPanel Orientation="Horizontal" Spacing="8"
                                        Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}">
                                <TextBlock Text="{Binding Codigo}" FontWeight="SemiBold" MinWidth="32" />
                                <TextBlock Text="{Binding Nombre}" />
                            </StackPanel>
                            <Border Classes="badge-inactiva" IsVisible="{Binding !Activo}">
                                <TextBlock Text="Inactivo" Classes="badge-inactiva-texto" />
                            </Border>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

        </DockPanel>
    </Border>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/RubroGastoListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class RubroGastoListView : UserControl
{
    public RubroGastoListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is RubroGastoListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/LineaPoaListView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Finanzas.LineaPoaListView"
             x:DataType="vm:LineaPoaListViewModel">

    <Border Classes="card" Margin="0,12,0,0">
        <DockPanel>

            <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
                <Button Classes="primary"
                        Content="Nueva línea POA"
                        Command="{Binding NuevoCommand}" />
                <Button Classes="secondary"
                        Content="Editar"
                        Command="{Binding EditarCommand}" />
                <Button Classes="secondary"
                        Content="Dar de baja"
                        Command="{Binding BajaCommand}" />
            </StackPanel>

            <ListBox ItemsSource="{Binding Items}"
                     SelectedItem="{Binding ItemSeleccionado}">
                <ListBox.ItemTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="16" Margin="4">
                            <StackPanel Orientation="Horizontal" Spacing="8"
                                        Opacity="{Binding Activo, Converter={x:Static conv:ActivoOpacidadConverter.Instance}}">
                                <TextBlock Text="{Binding Ejercicio}" FontWeight="SemiBold" />
                                <TextBlock Text="{Binding Nombre}" />
                                <TextBlock Text="{Binding Programa}" Opacity="0.7" />
                            </StackPanel>
                            <Border Classes="badge-inactiva" IsVisible="{Binding !Activo}">
                                <TextBlock Text="Inactiva" Classes="badge-inactiva-texto" />
                            </Border>
                        </StackPanel>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>

        </DockPanel>
    </Border>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/LineaPoaListView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LineaPoaListView : UserControl
{
    public LineaPoaListView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LineaPoaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Finanzas.FuenteFinanciamientoFormView"
             x:DataType="vm:FuenteFinanciamientoFormViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="{Binding Titulo}"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <Border Classes="card" VerticalAlignment="Top">
            <StackPanel Spacing="12" MaxWidth="420" HorizontalAlignment="Left">

                <TextBlock Text="Nombre" />
                <TextBox Text="{Binding Nombre}" Watermark="Ej.: Literal B" />

                <TextBlock Text="{Binding MensajeError}"
                           Foreground="Red"
                           TextWrapping="Wrap"
                           IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />

                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                    <Button Classes="secondary" Content="Cancelar" Command="{Binding CancelarCommand}" />
                </StackPanel>

            </StackPanel>
        </Border>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/FuenteFinanciamientoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class FuenteFinanciamientoFormView : UserControl
{
    public FuenteFinanciamientoFormView()
    {
        InitializeComponent();
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/RubroGastoFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="600" d:DesignHeight="400"
             x:Class="StockApp.Presentation.Views.Finanzas.RubroGastoFormView"
             x:DataType="vm:RubroGastoFormViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="{Binding Titulo}"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <Border Classes="card" VerticalAlignment="Top">
            <StackPanel Spacing="12" MaxWidth="420" HorizontalAlignment="Left">

                <TextBlock Text="Código" />
                <TextBox Text="{Binding CodigoTexto}" Watermark="Ej.: 3" />

                <TextBlock Text="Nombre" />
                <TextBox Text="{Binding Nombre}" Watermark="Ej.: Combustibles" />

                <TextBlock Text="{Binding MensajeError}"
                           Foreground="Red"
                           TextWrapping="Wrap"
                           IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />

                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                    <Button Classes="secondary" Content="Cancelar" Command="{Binding CancelarCommand}" />
                </StackPanel>

            </StackPanel>
        </Border>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/RubroGastoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class RubroGastoFormView : UserControl
{
    public RubroGastoFormView()
    {
        InitializeComponent();
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/LineaPoaFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="700" d:DesignHeight="600"
             x:Class="StockApp.Presentation.Views.Finanzas.LineaPoaFormView"
             x:DataType="vm:LineaPoaFormViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="{Binding Titulo}"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <Border Classes="card" VerticalAlignment="Top">
            <StackPanel Spacing="12" MaxWidth="560" HorizontalAlignment="Left">

                <TextBlock Text="Nombre" />
                <TextBox Text="{Binding Nombre}" Watermark="Ej.: COMPOSTERAS" />

                <TextBlock Text="Programa" />
                <TextBox Text="{Binding Programa}" Watermark="Ej.: Ambiente" />

                <TextBlock Text="Ejercicio" />
                <TextBox Text="{Binding EjercicioTexto}" Watermark="Ej.: 2026" MaxWidth="120" HorizontalAlignment="Left" />

                <!-- Grilla de asignaciones presupuestales por fuente (mixto B+C) -->
                <TextBlock Text="Asignaciones presupuestales" FontWeight="SemiBold" Margin="0,8,0,0" />

                <ItemsControl ItemsSource="{Binding Asignaciones}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="vm:AsignacionItemViewModel">
                            <Grid ColumnDefinitions="*,140,Auto" Margin="0,0,0,8">
                                <ComboBox Grid.Column="0"
                                          ItemsSource="{Binding $parent[UserControl].((vm:LineaPoaFormViewModel)DataContext).FuentesDisponibles}"
                                          SelectedItem="{Binding FuenteSeleccionada}"
                                          PlaceholderText="Fuente de financiamiento"
                                          HorizontalAlignment="Stretch">
                                    <ComboBox.ItemTemplate>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding Nombre}" />
                                        </DataTemplate>
                                    </ComboBox.ItemTemplate>
                                </ComboBox>
                                <TextBox Grid.Column="1"
                                         Text="{Binding MontoTexto}"
                                         Watermark="Monto"
                                         Margin="8,0,0,0" />
                                <Button Grid.Column="2"
                                        Classes="secondary"
                                        Margin="8,0,0,0"
                                        Command="{Binding $parent[UserControl].((vm:LineaPoaFormViewModel)DataContext).QuitarAsignacionCommand}"
                                        CommandParameter="{Binding}">
                                    <i:Icon Value="mdi-delete" />
                                </Button>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <Button Classes="secondary"
                        Content="Agregar asignación"
                        Command="{Binding AgregarAsignacionCommand}"
                        HorizontalAlignment="Left" />

                <TextBlock Text="{Binding MensajeError}"
                           Foreground="Red"
                           TextWrapping="Wrap"
                           IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />

                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                    <Button Classes="secondary" Content="Cancelar" Command="{Binding CancelarCommand}" />
                </StackPanel>

            </StackPanel>
        </Border>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/LineaPoaFormView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LineaPoaFormView : UserControl
{
    public LineaPoaFormView()
    {
        InitializeComponent();

        // InicializarAsync carga el combo de fuentes activas y arma las filas de
        // asignaciones (incluido el modo edición precargado por CargarParaEditar,
        // que corre ANTES vía el overload de Navegar).
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LineaPoaFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
```

- [ ] **Step 5: Registro DI en App.axaml.cs**

En `src/StockApp.Presentation/App.axaml.cs`:

1. Agregar usings: `using StockApp.Application.Finanzas;` y `using StockApp.Presentation.ViewModels.Finanzas;`
2. Después del bloque de ApiClients existente (tras `services.AddTransient<IAuditoriaQueryService, AuditoriaQueryApiClient>();`), agregar:

```csharp
        // ── Módulo Finanzas — Fase 1: maestros ────────────────────────────────
        services.AddTransient<IFuenteFinanciamientoService, FuenteFinanciamientoApiClient>();
        services.AddTransient<IRubroGastoService, RubroGastoApiClient>();
        services.AddTransient<ILineaPoaService, LineaPoaApiClient>();
```

3. Después de los VMs de catálogo (tras `services.AddTransient<UnidadMedidaFormViewModel>();`), agregar:

```csharp
        // ── Módulo Finanzas — Fase 1: VMs de maestros ─────────────────────────
        services.AddTransient<MaestrosFinanzasViewModel>();
        services.AddTransient<FuenteFinanciamientoListViewModel>();
        services.AddTransient<FuenteFinanciamientoFormViewModel>();
        services.AddTransient<RubroGastoListViewModel>();
        services.AddTransient<RubroGastoFormViewModel>();
        services.AddTransient<LineaPoaListViewModel>();
        services.AddTransient<LineaPoaFormViewModel>();
```

- [ ] **Step 6: Suite completa de Presentation en verde**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: verde (compila las Views nuevas vía el build del proyecto de tests; sin regresiones).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation tests/StockApp.Presentation.Tests
git commit -m "feat(finanzas): pantalla maestros de finanzas y sección Finanzas en el sidebar"
```

---

### Task 8: Verificación final — suite completa + arranque real

**Files:** ninguno nuevo (solo verificación; ajustes menores si algo falla).

- [ ] **Step 1: Suite completa de la solución**

Run: `dotnet test`
Expected: TODOS los proyectos de test en verde (Application, Infrastructure, Api, ApiClient, Presentation, Domain si existe). Cero tests fallando, cero errores de compilación.

- [ ] **Step 2: Arranque real de la API con la migración nueva**

Con el contenedor `stockapp-pg` corriendo (convención: queda siempre andando):

```bash
docker start stockapp-pg 2>/dev/null; ASPNETCORE_URLS=http://localhost:5000 timeout 30 dotnet run --project src/StockApp.Api &
sleep 12
curl -s -o /dev/null -w "%{http_code}\n" http://localhost:5000/finanzas/fuentes
```

Expected: la API arranca aplicando la migración `FinanzasMaestros` sin error (revisar el log de arranque) y el curl devuelve `401` (endpoint protegido responde — la ruta existe y exige token). `404` indicaría que faltó el `MapFuentesFinanciamientoEndpoints()`; `500` indicaría un problema de migración/DI.

- [ ] **Step 3: Smoke test autenticado (opcional pero recomendado)**

```bash
TOKEN=$(curl -s -X POST http://localhost:5000/auth/login \
  -H "Content-Type: application/json" \
  -d '{"nombreUsuario":"admin","contrasena":"test123"}' | jq -r .token)
curl -s -X POST http://localhost:5000/finanzas/fuentes \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"nombre":"Literal B"}' -w "\n%{http_code}\n"
curl -s http://localhost:5000/finanzas/fuentes -H "Authorization: Bearer $TOKEN"
```

Expected: `201` con `{"id":N}` y el GET devuelve la fuente creada. (Credenciales del admin de desarrollo: `admin`/`test123` — si difieren, usar las del entorno local.)

- [ ] **Step 4: Verificación orgánica (manual, convención del repo)**

Levantar la app desktop real contra la API y verificar: login → aparece la sección "Finanzas" en el sidebar (para Admin Y Operador) → "Maestros de finanzas" abre con los tres tabs → crear una fuente, un rubro y una línea POA con dos asignaciones (una por fuente) → editar cada uno → dar de baja → los badges "Inactiva/Inactivo" aparecen. Este paso lo cierra el usuario; no bloquear el cierre de la ejecución automática por él, pero dejarlo EXPLÍCITO como pendiente al reportar.

- [ ] **Step 5: Cierre**

Si hubo ajustes en esta task, commitearlos con un mensaje `fix(finanzas): ...` descriptivo. Verificar `git status` limpio y reportar: suite completa verde + arranque OK + verificación orgánica pendiente del usuario.

---

## Self-review del plan (hecho antes de commitear)

- **Cobertura del alcance**: entidades ✔ (4, spec §4) · migración ✔ (`FinanzasMaestros`, startup `src/StockApp.Api`) · índices únicos ✔ (Nombre; Codigo; Nombre+Ejercicio; LineaPoaId+FuenteFinanciamientoId; FKs Restrict) · repos ✔ · servicios ABM ✔ (agregado LineaPoa con reglas: ≥1 asignación, montos > 0, sin fuentes repetidas) · permisos ✔ (`VerFinanzas`, `GestionarMaestrosFinanzas`, ambos roles) · auditoría ✔ (22–30 append-only) · endpoints ✔ (3 grupos con matriz 401/409 — no hay 403 por diseño) · ApiClients ✔ · desktop ✔ (sidebar + tabs + forms + grilla de asignaciones + DI) · tests en las 5 capas ✔.
- **Cero placeholders**: no hay "TBD" ni "similar a Task N"; cada test y cada clase están completos.
- **Consistencia de firmas**: `ILineaPoaRepository.ActualizarAsync(LineaPoa, IReadOnlyList<AsignacionPresupuestal>)` se usa igual en Task 2 (repo/tests), Task 3 (servicio/tests); DTOs wire de Task 4 coinciden con los records deserializados en Task 5; `Navegar<MaestrosFinanzasViewModel>()` de los forms existe como VM en Task 6 y vista en Task 7.





