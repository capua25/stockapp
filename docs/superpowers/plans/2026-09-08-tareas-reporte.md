# Reporte estadístico de Tareas — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar el reporte-matriz de tareas (agregación por zona/dimensión/organismo/financiamiento/expediente, con conteos por estado) copiando capa por capa el patrón ya validado de `IReporteStockService`, pero SIN cache (decisión 14 del spec).

**Architecture:** Un único servicio de reporte (`IReporteTareasService`/`ReporteTareasService`) con el mismo patrón `auth fail-closed → validación → repo → total`. Toda la agregación (conteos por estado, fila "(sin asignar)", orden alfabético) se hace en SQL con `GROUP BY` dentro de `ReporteTareasRepository` (Postgres/EF Core), igual que `ReporteStockRepository.ObtenerStockPorCategoriaAsync`. El flujo atraviesa Application (servicio + DTOs + contrato de repo) → Infrastructure (repo EF) → Api (endpoint dentro del grupo `/reportes` existente) → ApiClient (cliente HTTP tipado) → Presentation (ViewModel + View con `DataGrid`, junto a los 5 reportes existentes). El DTO es agnóstico de presentación (decisión 23): el ViewModel solo asigna lo que recibe, nunca calcula.

**Tech Stack:** .NET 10, C#, EF Core 10 + Npgsql/PostgreSQL, ASP.NET Core Minimal API, Avalonia 12.0.5 + CommunityToolkit.Mvvm, xUnit (Testcontainers/Postgres real para Infrastructure/Api, Moq en Application/Presentation.Tests).

**Spec:** docs/superpowers/specs/2026-09-08-tareas-clasificadores-design.md

**Depende de:** docs/superpowers/plans/2026-09-08-tareas-catalogos.md y docs/superpowers/plans/2026-09-08-tareas-clasificacion.md (ambos completos antes de empezar). Este plan asume que, para cuando se ejecuta, `Tarea` ya tiene las propiedades `ZonaId`, `DimensionTematicaId`, `OrganismoResponsableId`, `OrigenFinanciamientoId`, `DocumentoAdministrativoId` (todas `int?`) con sus navegaciones `Zona`, `DimensionTematica`, `OrganismoResponsable`, `OrigenFinanciamiento`, `DocumentoAdministrativo` — **verificar los nombres exactos de esas navegaciones contra el `Tarea.cs` real antes de escribir el Task 2**, ya que este plan se escribió antes de que el Plan B se ejecutara.

## Global Constraints

- Rama de trabajo: crear `feat/tareas-reporte` desde `main` (después de mergeados los planes de catálogos y clasificación). No pushear.
- Commits: conventional commits en español, uno por tarea como mínimo. **NUNCA agregar "Co-Authored-By" ni atribución a IA.**
- TDD estricto y sin atajos: escribir el test, **correrlo y verlo fallar**, implementar lo mínimo, correrlo y verlo pasar, commitear.
- **NUNCA correr `StockApp.Application.Tests` y `StockApp.Api.Tests` en paralelo**: colisionan por Testcontainers/Postgres y producen falsos rojos. Siempre secuencial.
- `StockApp.Infrastructure.Tests` corre contra PostgreSQL real; el contenedor `stockapp-pg` tiene que estar levantado.
- `dotnet build` con varios `.csproj` puede dar falso verde si uno de ellos falla en silencio: para verificar compilación de un proyecto puntual usar `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`, no un build agregado.
- **Verificar antes del Task 2**: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` tiene un `TRUNCATE TABLE` hardcodeado. Si el Plan A no agregó ahí las tablas `Zonas`, `DimensionesTematicas`, `OrganismosResponsables`, `OrigenesFinanciamiento` (o los nombres reales que haya usado), agregarlas antes de correr los tests de este plan — si no, los tests de este plan van a arrastrar filas de zonas/dimensiones de una corrida anterior.
- Las Views de Avalonia **no se auto-inicializan**: toda vista nueva engancha `DataContextChanged` para disparar su carga (bug recurrente del proyecto).
- `UnauthorizedAccessException` en los ViewModels se captura **en silencio** vía `ViewModelBase.EjecutarCargaProtegidaAsync` (deja `SinPermiso`/`MensajeSinPermiso` bindeables) — nunca un try/catch propio que la vuelva a mostrar.
- Fila "(sin asignar)": el texto exacto, con paréntesis, es `"(sin asignar)"` — se usa tal cual en Infrastructure y en los tests. No inventar variantes.
- **D14, no negociable**: `ReporteTareasService` NO se envuelve en un decorator cacheado y NO toca `IVersionReportes`. Si alguien "arregla" esto copiando `ReporteStockServiceCacheado`, está rompiendo la decisión 14 del spec — las tareas cambian de estado todo el día.
- **D23, no negociable**: el ViewModel de Presentation nunca hace `.Sum()`, `.Count()` ni ningún cálculo sobre `Items`/`Filas` — solo asigna lo que devuelve el servicio. El único lugar donde se calcula `TotalGeneral` es `ReporteTareasService` (sumando una lista ya agregada por el repo, mismo criterio que `ReporteStockService.ObtenerValorizacionAsync` con `TotalValorCosto`).
- Los conteos del reporte son siempre enteros (`int`): se formatean con `CantidadConverter.Instance` (mismo converter que `StockCategoriaDto.CantidadProductos`/`MasMovidoDto.CantidadMovimientos`), que no muestra separador decimal para valores sin parte fraccionaria — no hace falta el converter de punto decimal (`DecimalPuntoConverter`, reservado a campos `decimal` editables).

---

### Task 1: Application — DTOs, enums, `IReporteTareasService`, `IReporteTareasRepository` y `ReporteTareasService`

**Files:**
- Create: `src/StockApp.Application/Reportes/TareasReporteDtos.cs`
- Create: `src/StockApp.Application/Reportes/IReporteTareasService.cs`
- Create: `src/StockApp.Application/Interfaces/IReporteTareasRepository.cs`
- Create: `src/StockApp.Application/Reportes/ReporteTareasService.cs`
- Test: `tests/StockApp.Application.Tests/Reportes/ReporteTareasServiceTests.cs`

**Interfaces:**
- Consumes: `Permisos.VerReportes` (`src/StockApp.Application/Authorization/Permisos.cs`), `ICurrentSession`/`IAuthorizationService` (`src/StockApp.Application/Interfaces/`, `src/StockApp.Application/Authorization/`).
- Produces (consumido por Tasks 2-5):
  - `enum AgrupadorTareas { Zona, DimensionTematica, OrganismoResponsable, OrigenFinanciamiento, Expediente }`
  - `enum CriterioFechaTareas { Creacion, Cierre }`
  - `record FiltroReporteTareas(AgrupadorTareas Agrupador, CriterioFechaTareas Criterio, DateTime Desde, DateTime Hasta)`
  - `record FilaReporteTareas(string Clasificador, int Pendientes, int EnCurso, int Terminadas, int Canceladas, int Total)`
  - `record ReporteTareasDto(IReadOnlyList<FilaReporteTareas> Filas, int TotalGeneral)`
  - `interface IReporteTareasService { Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro); }`
  - `interface IReporteTareasRepository { Task<IReadOnlyList<FilaReporteTareas>> ObtenerAsync(FiltroReporteTareas filtro); }`
  - `class ReporteTareasService : IReporteTareasService`

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Application.Tests/Reportes/ReporteTareasServiceTests.cs`:

```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Reportes;

public class ReporteTareasServiceTests
{
    // ── helpers de setup ──────────────────────────────────────────────────────

    private static (ReporteTareasService svc,
                    Mock<IReporteTareasRepository> repoMock,
                    Mock<ICurrentSession> sessionMock,
                    Mock<IAuthSvc> authMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IReporteTareasRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        // Por defecto auth no lanza (permiso concedido)
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), It.IsAny<string>()));

        var svc = new ReporteTareasService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    private static FiltroReporteTareas FiltroValido(
        AgrupadorTareas agrupador = AgrupadorTareas.Zona,
        CriterioFechaTareas criterio = CriterioFechaTareas.Creacion) =>
        new(agrupador, criterio, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

    private static FilaReporteTareas Fila(
        string clasificador, int pendientes = 0, int enCurso = 0, int terminadas = 0, int canceladas = 0) =>
        new(clasificador, pendientes, enCurso, terminadas, canceladas,
            pendientes + enCurso + terminadas + canceladas);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerAsync_Operador_LanzaUnauthorized_YNuncaLlamaAlRepo()
    {
        var (svc, repo, _, auth) = Crear(RolUsuario.Operador);
        auth.Setup(a => a.Verificar(It.IsAny<ICurrentSession>(), Permisos.VerReportes))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.ObtenerAsync(FiltroValido()));

        // Fail-closed: el repo NUNCA debe ser invocado.
        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_RangoAusente_LanzaArgumentException()
    {
        // D18: el rango de fechas es obligatorio y se valida ACÁ, no solo en la UI.
        var (svc, repo, _, _) = Crear();
        var filtroSinRango = new FiltroReporteTareas(
            AgrupadorTareas.Zona, CriterioFechaTareas.Creacion, default, default);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ObtenerAsync(filtroSinRango));

        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_RangoInvertido_LanzaArgumentException()
    {
        var (svc, repo, _, _) = Crear();
        var filtroInvertido = new FiltroReporteTareas(
            AgrupadorTareas.Zona, CriterioFechaTareas.Creacion,
            new DateTime(2026, 12, 31), new DateTime(2026, 1, 1));

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.ObtenerAsync(filtroInvertido));

        repo.Verify(r => r.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
    }

    [Fact]
    public async Task ObtenerAsync_DelegaElFiltroExactoAlRepo()
    {
        var (svc, repo, _, _) = Crear();
        var filtro = FiltroValido(AgrupadorTareas.Expediente, CriterioFechaTareas.Cierre);
        repo.Setup(r => r.ObtenerAsync(filtro)).ReturnsAsync(Array.Empty<FilaReporteTareas>());

        await svc.ObtenerAsync(filtro);

        repo.Verify(r => r.ObtenerAsync(filtro), Times.Once);
    }

    [Fact]
    public async Task ObtenerAsync_CalculaTotalGeneral_SumandoLasFilasYaAgregadasPorElRepo()
    {
        // D23: el ÚNICO cálculo del reporte pasa por acá, sumando una lista ya agregada
        // por el repo (mismo criterio que ReporteStockService.ObtenerValorizacionAsync).
        var (svc, repo, _, _) = Crear();
        var filas = new[]
        {
            Fila("Centro", pendientes: 2, enCurso: 1, terminadas: 3, canceladas: 0),
            Fila("Este", pendientes: 0, enCurso: 0, terminadas: 1, canceladas: 1),
            Fila("(sin asignar)", pendientes: 1, enCurso: 0, terminadas: 0, canceladas: 0),
        };
        repo.Setup(r => r.ObtenerAsync(It.IsAny<FiltroReporteTareas>())).ReturnsAsync(filas);

        var resultado = await svc.ObtenerAsync(FiltroValido());

        Assert.Equal(3, resultado.Filas.Count);
        Assert.Same(filas, resultado.Filas);
        Assert.Equal(6 + 2 + 1, resultado.TotalGeneral); // 6 + 2 + 1 = 9
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ReporteTareasServiceTests`
Expected: FALLA de compilación — `StockApp.Application.Reportes.AgrupadorTareas`, `CriterioFechaTareas`, `FiltroReporteTareas`, `FilaReporteTareas`, `ReporteTareasDto`, `IReporteTareasService`, `IReporteTareasRepository` y `ReporteTareasService` no existen.

- [ ] **Step 3: Implementación mínima**

`src/StockApp.Application/Reportes/TareasReporteDtos.cs`:

```csharp
namespace StockApp.Application.Reportes;

/// <summary>
/// Eje de agrupamiento del reporte-matriz de tareas (spec 2026-09-08, decisión 15): agrupar
/// por zona, dimensión, organismo, financiamiento o expediente es la misma pregunta con
/// distinto eje -- una sola pantalla en vez de cinco casi idénticas.
/// </summary>
public enum AgrupadorTareas
{
    Zona = 0,
    DimensionTematica = 1,
    OrganismoResponsable = 2,
    OrigenFinanciamiento = 3,
    Expediente = 4,
}

/// <summary>
/// Qué fecha de la tarea decide si cae dentro del rango del reporte (decisión 16). Con
/// <see cref="Cierre"/> el reporte cuenta ÚNICAMENTE tareas cerradas (Terminada/Cancelada):
/// FechaFin es null en Pendiente/EnCurso, así que esas columnas quedan en cero.
/// </summary>
public enum CriterioFechaTareas
{
    Creacion = 0,
    Cierre = 1,
}

/// <summary>
/// Filtro del reporte-matriz de tareas. Desde/Hasta son obligatorios (decisión 18):
/// se validan en <see cref="ReporteTareasService"/>, no solo con un default en la UI.
/// </summary>
public record FiltroReporteTareas(
    AgrupadorTareas Agrupador,
    CriterioFechaTareas Criterio,
    DateTime Desde,
    DateTime Hasta);

/// <summary>
/// Fila del reporte-matriz: un clasificador con sus conteos por estado. Pendientes + EnCurso +
/// Terminadas + Canceladas == Total siempre (decisión 17): Total es la cuenta completa del
/// grupo y esos cuatro son los únicos valores posibles de <c>EstadoTarea</c>.
/// </summary>
public record FilaReporteTareas(
    string Clasificador,
    int Pendientes,
    int EnCurso,
    int Terminadas,
    int Canceladas,
    int Total);

/// <summary>
/// DTO de matriz agnóstico de presentación (decisión 23): filas de clasificador con sus
/// conteos, sin nada atado a la grilla. Incorporar gráficos más adelante es escribir una vista
/// nueva que consuma este mismo DTO -- no toca Application, Api ni ApiClient.
/// </summary>
public record ReporteTareasDto(
    IReadOnlyList<FilaReporteTareas> Filas,
    int TotalGeneral);
```

`src/StockApp.Application/Reportes/IReporteTareasService.cs`:

```csharp
namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio del reporte-matriz de tareas (spec 2026-09-08). Patrón: auth fail-closed →
/// validación de rango de fechas → repo (agregación completa en SQL) → total general.
/// SIN CACHE (decisión 14): a diferencia de <see cref="IReporteStockService"/>, este servicio
/// NO se envuelve en un decorator cacheado ni participa de <see cref="IVersionReportes"/> --
/// las tareas cambian de estado todo el día y un reporte cacheado una hora informa mal.
/// </summary>
public interface IReporteTareasService
{
    /// <exception cref="UnauthorizedAccessException">Si el rol no tiene permiso para ver reportes.</exception>
    /// <exception cref="ArgumentException">Si el rango de fechas está ausente o invertido (Desde &gt; Hasta).</exception>
    Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro);
}
```

`src/StockApp.Application/Interfaces/IReporteTareasRepository.cs`:

```csharp
using StockApp.Application.Reportes;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Contrato de persistencia del reporte-matriz de tareas (solo lectura). La agregación
/// completa (conteos por estado, fila "(sin asignar)", orden alfabético con esa fila siempre
/// al final) la hace el repo en SQL con GROUP BY -- el service solo valida y suma el total.
/// </summary>
public interface IReporteTareasRepository
{
    /// <summary>Filas ya agregadas y ordenadas (decisión 22). Lista vacía si no hay tareas
    /// dentro del rango/criterio filtrado.</summary>
    Task<IReadOnlyList<FilaReporteTareas>> ObtenerAsync(FiltroReporteTareas filtro);
}
```

`src/StockApp.Application/Reportes/ReporteTareasService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Reportes;

/// <summary>
/// Servicio del reporte-matriz de tareas. Patrón: auth fail-closed → validación de rango de
/// fechas → repo (agregación completa en SQL) → total general. SIN CACHE (decisión 14) --
/// no copiar el patrón de <c>ReporteStockServiceCacheado</c> acá.
/// </summary>
public class ReporteTareasService : IReporteTareasService
{
    private readonly IReporteTareasRepository _repo;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ReporteTareasService(
        IReporteTareasRepository repo, ICurrentSession session, IAuthorizationService auth)
    {
        _repo = repo;
        _session = session;
        _auth = auth;
    }

    public async Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro)
    {
        // Autorización fail-closed: PRIMERO, antes de tocar el repo.
        _auth.Verificar(_session, Permisos.VerReportes);

        // D18: el rango de fechas es obligatorio y se valida ACÁ, no solo en la UI -- mismo
        // criterio que DocumentoAdministrativoService.ListarHistorialAsync con el año.
        if (filtro.Desde == default || filtro.Hasta == default)
            throw new ArgumentException(
                "El rango de fechas es obligatorio para el reporte de tareas.", nameof(filtro));
        if (filtro.Desde > filtro.Hasta)
            throw new ArgumentException(
                "La fecha 'Desde' no puede ser posterior a 'Hasta'.", nameof(filtro));

        var filas = await _repo.ObtenerAsync(filtro);

        // D23: el ViewModel no calcula nada -- este total se calcula ACÁ, sumando una lista
        // ya agregada por el repo (mismo criterio que ReporteStockService.ObtenerValorizacionAsync
        // con TotalValorCosto). No es un cálculo por-entidad: Filas ya viene agregada por SQL.
        var totalGeneral = filas.Sum(f => f.Total);

        return new ReporteTareasDto(filas, totalGeneral);
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter FullyQualifiedName~ReporteTareasServiceTests`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Reportes/TareasReporteDtos.cs \
        src/StockApp.Application/Reportes/IReporteTareasService.cs \
        src/StockApp.Application/Interfaces/IReporteTareasRepository.cs \
        src/StockApp.Application/Reportes/ReporteTareasService.cs \
        tests/StockApp.Application.Tests/Reportes/ReporteTareasServiceTests.cs
git commit -m "feat(reportes): agrega servicio del reporte-matriz de tareas, sin cache (D14)"
```

---

### Task 2: Infrastructure — `ReporteTareasRepository` con `GROUP BY` real contra Postgres

**Files:**
- Create: `src/StockApp.Infrastructure/Repositories/ReporteTareasRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ReporteTareasRepositoryTests.cs`

**Interfaces:**
- Consumes: `IReporteTareasRepository` (Task 1), `AppDbContext.Tareas` (`src/StockApp.Infrastructure/Persistence/AppDbContext.cs`), `Tarea` con `ZonaId`/`Zona`, `DimensionTematicaId`/`DimensionTematica`, `OrganismoResponsableId`/`OrganismoResponsable`, `OrigenFinanciamientoId`/`OrigenFinanciamiento`, `DocumentoAdministrativoId`/`DocumentoAdministrativo` (Plan A/B), `DocumentoAdministrativo.Numero`/`.Anio` (`src/StockApp.Domain/Entities/DocumentoAdministrativo.cs`).
- Produces: `class ReporteTareasRepository : IReporteTareasRepository`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Infrastructure.Tests/Repositories/ReporteTareasRepositoryTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// Tests de integración para ReporteTareasRepository.ObtenerAsync contra PostgreSQL real.
/// </summary>
public class ReporteTareasRepositoryTests : PostgresRepositoryTestBase
{
    private readonly ReporteTareasRepository _repo;

    public ReporteTareasRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ReporteTareasRepository(Context);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static readonly DateTime Desde2026 = new(2026, 1, 1);
    private static readonly DateTime Hasta2026 = new(2026, 12, 31);

    private async Task<int> SembrarUsuarioAsync()
    {
        var usuario = new Usuario
        {
            NombreUsuario = "operario1",
            HashContrasena = "x",
            Rol = RolUsuario.Operador,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuario);
        await Context.SaveChangesAsync();
        return usuario.Id;
    }

    private static Tarea NuevaTarea(
        int creadaPorUsuarioId,
        EstadoTarea estado,
        DateTime fechaCreacion,
        DateTime? fechaFin = null,
        int? zonaId = null,
        int? documentoAdministrativoId = null,
        string titulo = "Tarea de prueba") => new()
    {
        Titulo = titulo,
        CreadaPorUsuarioId = creadaPorUsuarioId,
        FechaCreacion = fechaCreacion,
        Estado = estado,
        FechaFin = fechaFin,
        ZonaId = zonaId,
        DocumentoAdministrativoId = documentoAdministrativoId,
    };

    private static FiltroReporteTareas Filtro(
        AgrupadorTareas agrupador,
        CriterioFechaTareas criterio = CriterioFechaTareas.Creacion,
        DateTime? desde = null,
        DateTime? hasta = null) =>
        new(agrupador, criterio, desde ?? Desde2026, hasta ?? Hasta2026);

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerAsync_AgrupaPorZona_ConSinAsignar_LasFilasSumanElTotal()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var centro = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(centro);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 3, 1), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, new DateTime(2026, 3, 2), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, new DateTime(2026, 3, 3),
                fechaFin: new DateTime(2026, 3, 10), zonaId: centro.Id),
            // sin zona: va a "(sin asignar)"
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, new DateTime(2026, 3, 4),
                fechaFin: new DateTime(2026, 3, 11), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        Assert.Equal(2, filas.Count);

        var fCentro = filas.Single(f => f.Clasificador == "Centro");
        Assert.Equal(1, fCentro.Pendientes);
        Assert.Equal(1, fCentro.EnCurso);
        Assert.Equal(1, fCentro.Terminadas);
        Assert.Equal(0, fCentro.Canceladas);
        Assert.Equal(3, fCentro.Total);
        Assert.Equal(fCentro.Pendientes + fCentro.EnCurso + fCentro.Terminadas + fCentro.Canceladas, fCentro.Total);

        var fSinAsignar = filas.Single(f => f.Clasificador == "(sin asignar)");
        Assert.Equal(0, fSinAsignar.Pendientes);
        Assert.Equal(0, fSinAsignar.EnCurso);
        Assert.Equal(0, fSinAsignar.Terminadas);
        Assert.Equal(1, fSinAsignar.Canceladas);
        Assert.Equal(1, fSinAsignar.Total);
    }

    [Fact]
    public async Task ObtenerAsync_OrdenAlfabetico_ConSinAsignarSiempreAlFinal()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var oeste = new Zona { Nombre = "Oeste", Activo = true };
        var centro = new Zona { Nombre = "Centro", Activo = true };
        var este = new Zona { Nombre = "Este", Activo = true };
        Context.AddRange(oeste, centro, este);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 3, 1), zonaId: oeste.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 3, 1), zonaId: centro.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 3, 1), zonaId: este.Id),
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 3, 1), zonaId: null));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        Assert.Equal(
            new[] { "Centro", "Este", "Oeste", "(sin asignar)" },
            filas.Select(f => f.Clasificador).ToArray());
    }

    [Fact]
    public async Task ObtenerAsync_CriterioCreacion_CuentaLosCuatroEstados()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 5, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, new DateTime(2026, 5, 2), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, new DateTime(2026, 5, 3),
                fechaFin: new DateTime(2026, 5, 10), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, new DateTime(2026, 5, 4),
                fechaFin: new DateTime(2026, 5, 11), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona, CriterioFechaTareas.Creacion));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Pendientes);
        Assert.Equal(1, fila.EnCurso);
        Assert.Equal(1, fila.Terminadas);
        Assert.Equal(1, fila.Canceladas);
        Assert.Equal(4, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_CriterioCierre_SoloCuentaCerradas_PendientesYEnCursoEnCero()
    {
        // D16: con criterio Cierre, FechaFin es null en Pendiente/EnCurso -- esas dos tareas
        // quedan FUERA del universo filtrado, así que las columnas dan cero.
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 5, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.EnCurso, new DateTime(2026, 5, 2), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Terminada, new DateTime(2026, 5, 3),
                fechaFin: new DateTime(2026, 6, 1), zonaId: zona.Id),
            NuevaTarea(usuarioId, EstadoTarea.Cancelada, new DateTime(2026, 5, 4),
                fechaFin: new DateTime(2026, 6, 2), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona, CriterioFechaTareas.Cierre));

        var fila = Assert.Single(filas);
        Assert.Equal(0, fila.Pendientes);
        Assert.Equal(0, fila.EnCurso);
        Assert.Equal(1, fila.Terminadas);
        Assert.Equal(1, fila.Canceladas);
        Assert.Equal(2, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_RangoDeFechas_ExcluyeTareasFueraDelRango()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var zona = new Zona { Nombre = "Centro", Activo = true };
        Context.Add(zona);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 6, 15), zonaId: zona.Id),
            // fuera del rango 2026: no debe contarse
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2025, 12, 31), zonaId: zona.Id));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Zona));

        var fila = Assert.Single(filas);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorExpediente_EtiquetaNumeroBarraAnio()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var documento = new DocumentoAdministrativo
        {
            Numero = "0087",
            Anio = 2026,
            Tipo = TipoDocumento.Expediente,
            FechaEmision = new DateTime(2026, 1, 15),
            Descripcion = "Expediente de prueba",
            RegistradoPorUsuarioId = usuarioId,
            FechaRegistro = DateTime.UtcNow,
        };
        Context.Add(documento);
        await Context.SaveChangesAsync();

        Context.Tareas.AddRange(
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 4, 1),
                documentoAdministrativoId: documento.Id),
            // sin expediente: "(sin asignar)"
            NuevaTarea(usuarioId, EstadoTarea.Pendiente, new DateTime(2026, 4, 2)));
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.Expediente));

        Assert.Equal(2, filas.Count);
        Assert.Contains(filas, f => f.Clasificador == "0087/2026" && f.Total == 1);
        Assert.Contains(filas, f => f.Clasificador == "(sin asignar)" && f.Total == 1);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorDimensionTematica_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var dimension = new DimensionTematica { Nombre = "Infraestructura", Activo = true };
        Context.Add(dimension);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Bacheo",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = new DateTime(2026, 4, 1),
            DimensionTematicaId = dimension.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.DimensionTematica));

        var fila = Assert.Single(filas);
        Assert.Equal("Infraestructura", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorOrganismoResponsable_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var organismo = new OrganismoResponsable { Nombre = "Intendencia", Activo = true };
        Context.Add(organismo);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Poda",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = new DateTime(2026, 4, 1),
            OrganismoResponsableId = organismo.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.OrganismoResponsable));

        var fila = Assert.Single(filas);
        Assert.Equal("Intendencia", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }

    [Fact]
    public async Task ObtenerAsync_AgrupaPorOrigenFinanciamiento_DevuelveNombreDelCatalogo()
    {
        var usuarioId = await SembrarUsuarioAsync();
        var origen = new OrigenFinanciamiento { Nombre = "Fondo nacional", Activo = true };
        Context.Add(origen);
        await Context.SaveChangesAsync();

        var tarea = new Tarea
        {
            Titulo = "Iluminación",
            CreadaPorUsuarioId = usuarioId,
            FechaCreacion = new DateTime(2026, 4, 1),
            OrigenFinanciamientoId = origen.Id,
        };
        Context.Tareas.Add(tarea);
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var filas = await _repo.ObtenerAsync(Filtro(AgrupadorTareas.OrigenFinanciamiento));

        var fila = Assert.Single(filas);
        Assert.Equal("Fondo nacional", fila.Clasificador);
        Assert.Equal(1, fila.Total);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ReporteTareasRepositoryTests`
Expected: FALLA de compilación — `StockApp.Infrastructure.Repositories.ReporteTareasRepository` no existe (y, si el Plan A/B todavía no corrió, también faltarán `Zona`/`DimensionTematica`/`OrganismoResponsable`/`OrigenFinanciamiento` y las props de `Tarea` — en ese caso, PARAR: es la señal de que este plan se está ejecutando fuera de orden).

- [ ] **Step 3: Implementación mínima**

`src/StockApp.Infrastructure/Repositories/ReporteTareasRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Application.Reportes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Repositorio de solo lectura del reporte-matriz de tareas (EF Core / PostgreSQL). Toda la
/// agregación (conteos por estado, fila "(sin asignar)") se hace en SQL con GROUP BY -- mismo
/// criterio que ReporteStockRepository.ObtenerStockPorCategoriaAsync: se agrupa directamente
/// por el nombre YA resuelto (con el fallback "(sin asignar)"), no por el Id, para que la
/// query completa traduzca a un único GROUP BY sin postprocesado.
/// </summary>
public class ReporteTareasRepository : IReporteTareasRepository
{
    private const string SinAsignar = "(sin asignar)";

    private readonly AppDbContext _ctx;

    public ReporteTareasRepository(AppDbContext ctx) => _ctx = ctx;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<FilaReporteTareas>> ObtenerAsync(FiltroReporteTareas filtro)
    {
        var query = AplicarFiltro(_ctx.Tareas, filtro);

        var filas = filtro.Agrupador switch
        {
            AgrupadorTareas.Zona => await AgruparPorZonaAsync(query),
            AgrupadorTareas.DimensionTematica => await AgruparPorDimensionAsync(query),
            AgrupadorTareas.OrganismoResponsable => await AgruparPorOrganismoAsync(query),
            AgrupadorTareas.OrigenFinanciamiento => await AgruparPorOrigenFinanciamientoAsync(query),
            AgrupadorTareas.Expediente => await AgruparPorExpedienteAsync(query),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Agrupador, "Agrupador de tareas no soportado."),
        };

        // D22: orden alfabético por Clasificador, con "(sin asignar)" siempre al final.
        // Se ordena EN MEMORIA sobre la lista ya agregada (una fila por clasificador, nunca
        // por tarea): no viola "la agregación se hace en SQL" -- solo el orden final de un
        // puñado de filas, mismo criterio que ObtenerMasMovidosAsync con el Take(topN).
        return filas
            .OrderBy(f => f.Clasificador == SinAsignar ? 1 : 0)
            .ThenBy(f => f.Clasificador, StringComparer.InvariantCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Filtro por rango de fechas según el criterio (decisión 16). Con Cierre, se exige
    /// FechaFin no nulo -- eso excluye por construcción a Pendiente/EnCurso (FechaFin solo lo
    /// setean TerminarAsync/CancelarAsync), así que sus conteos dan cero sin lógica aparte.
    /// </summary>
    private static IQueryable<Tarea> AplicarFiltro(IQueryable<Tarea> query, FiltroReporteTareas filtro)
    {
        var hastaFinDia = filtro.Hasta.Date.AddDays(1).AddTicks(-1);

        return filtro.Criterio switch
        {
            CriterioFechaTareas.Creacion => query.Where(t =>
                t.FechaCreacion >= filtro.Desde && t.FechaCreacion <= hastaFinDia),
            CriterioFechaTareas.Cierre => query.Where(t =>
                t.FechaFin != null && t.FechaFin >= filtro.Desde && t.FechaFin <= hastaFinDia),
            _ => throw new ArgumentOutOfRangeException(
                nameof(filtro), filtro.Criterio, "Criterio de fecha no soportado."),
        };
    }

    private static Task<List<FilaReporteTareas>> AgruparPorZonaAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.Zona != null ? t.Zona.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorDimensionAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.DimensionTematica != null ? t.DimensionTematica.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorOrganismoAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.OrganismoResponsable != null ? t.OrganismoResponsable.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    private static Task<List<FilaReporteTareas>> AgruparPorOrigenFinanciamientoAsync(IQueryable<Tarea> query) =>
        query
            .GroupBy(t => t.OrigenFinanciamiento != null ? t.OrigenFinanciamiento.Nombre : SinAsignar)
            .Select(g => new FilaReporteTareas(
                g.Key,
                g.Count(t => t.Estado == EstadoTarea.Pendiente),
                g.Count(t => t.Estado == EstadoTarea.EnCurso),
                g.Count(t => t.Estado == EstadoTarea.Terminada),
                g.Count(t => t.Estado == EstadoTarea.Cancelada),
                g.Count()))
            .ToListAsync();

    /// <summary>
    /// Expediente es un caso aparte: la agregación (conteos por estado) SÍ traduce a SQL
    /// agrupando por DocumentoAdministrativoId, pero el armado de la etiqueta "Numero/Anio"
    /// se resuelve DESPUÉS, en memoria, sobre la lista ya agregada (mismo criterio de
    /// "ADAPTACIÓN" que ReporteStockRepository.ObtenerMasMovidosAsync con Codigo/Nombre):
    /// evita depender de que el proveedor traduzca int.ToString() dentro del GROUP BY.
    /// </summary>
    private static async Task<List<FilaReporteTareas>> AgruparPorExpedienteAsync(IQueryable<Tarea> query)
    {
        var agregados = await query
            .GroupBy(t => new
            {
                t.DocumentoAdministrativoId,
                Numero = t.DocumentoAdministrativo != null ? t.DocumentoAdministrativo.Numero : null,
                Anio = t.DocumentoAdministrativo != null ? t.DocumentoAdministrativo.Anio : (int?)null,
            })
            .Select(g => new
            {
                g.Key.DocumentoAdministrativoId,
                g.Key.Numero,
                g.Key.Anio,
                Pendientes = g.Count(t => t.Estado == EstadoTarea.Pendiente),
                EnCurso = g.Count(t => t.Estado == EstadoTarea.EnCurso),
                Terminadas = g.Count(t => t.Estado == EstadoTarea.Terminada),
                Canceladas = g.Count(t => t.Estado == EstadoTarea.Cancelada),
                Total = g.Count(),
            })
            .ToListAsync();

        return agregados
            .Select(a => new FilaReporteTareas(
                a.DocumentoAdministrativoId is null ? SinAsignar : $"{a.Numero}/{a.Anio}",
                a.Pendientes, a.EnCurso, a.Terminadas, a.Canceladas, a.Total))
            .ToList();
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter FullyQualifiedName~ReporteTareasRepositoryTests`
Expected: PASS (9/9). Si `AgruparPorExpedienteAsync` falla con `InvalidOperationException` de traducción, aplicar la misma estrategia que `ObtenerMasMovidosAsync`: separar la agregación (`GroupBy` + `Count`, que sí traduce) de cualquier post-procesado que no traduzca, y mover ESE post-procesado a la lista ya materializada — el código de arriba ya sigue ese patrón, pero si el proveedor Npgsql instalado se comporta distinto, no forzar la traducción con trucos: mover más lógica a memoria sobre la lista corta.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Infrastructure/Repositories/ReporteTareasRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/ReporteTareasRepositoryTests.cs
git commit -m "feat(reportes): agrega ReporteTareasRepository con agregación GROUP BY en Postgres"
```

---

### Task 3: Api — `GET /reportes/tareas`

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ReportesEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (sección "Reportes", cerca de la línea 176-192)
- Test: `tests/StockApp.Api.Tests/ReportesEndpointTests.cs`

**Interfaces:**
- Consumes: `IReporteTareasService.ObtenerAsync(FiltroReporteTareas)` (Task 1), `IReporteTareasRepository`/`ReporteTareasRepository` (Task 2, para el DI).
- Produces: `GET /reportes/tareas?agrupador={AgrupadorTareas}&criterio={CriterioFechaTareas}&desde={DateTime}&hasta={DateTime}` → 200 `ReporteTareasDto` | 401 | 403 | 400 (rango ausente/inválido, vía `ArgumentException` → `DomainExceptionHandler`). Ya protegido por el `RequireAuthorization(Permisos.VerReportes)` del grupo `/reportes` existente — no hace falta política propia.

- [ ] **Step 1: Escribir el test que falla**

Agregar al final de `tests/StockApp.Api.Tests/ReportesEndpointTests.cs` (antes del cierre de la clase), reusando `TokenAdmin()`/`TokenOperador()` ya definidos en el archivo:

```csharp
    // ── GET /reportes/tareas ──────────────────────────────────────────────

    [Fact]
    public async Task GetReporteTareas_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_ConTokenOperadorSinPermiso_Devuelve403()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenOperador());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_ConTokenAdmin_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Zona&criterio=Creacion&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var reporte = await response.Content.ReadFromJsonAsync<ReporteTareasDto>();
        Assert.NotNull(reporte);
    }

    [Fact]
    public async Task GetReporteTareas_SinRangoDeFechas_Devuelve400()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync("/reportes/tareas?agrupador=Zona&criterio=Creacion");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetReporteTareas_AgrupadoPorExpediente_Devuelve200()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TokenAdmin());

        var response = await client.GetAsync(
            "/reportes/tareas?agrupador=Expediente&criterio=Cierre&desde=2026-01-01&hasta=2026-12-31");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

(El `using StockApp.Application.Reportes;` ya está en el archivo, agregado por Task original de reportes de stock — cubre `ReporteTareasDto`.)

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~ReportesEndpointTests`
Expected: FALLA de compilación o 404 en los `GetReporteTareas_*` — la ruta `/reportes/tareas` no existe todavía.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Api/Endpoints/ReportesEndpoints.cs`, agregar el endpoint dentro del grupo existente, antes del `return app;`:

```csharp
        group.MapGet("/tareas", async (
            IReporteTareasService reportes,
            AgrupadorTareas agrupador, CriterioFechaTareas criterio, DateTime desde, DateTime hasta) =>
            Results.Ok(await reportes.ObtenerAsync(
                new FiltroReporteTareas(agrupador, criterio, desde, hasta))));

        return app;
```

En `src/StockApp.Api/Program.cs`, agregar después del bloque de DI de Reportes existente (después de la línea `sp.GetRequiredService<IVersionReportes>()));`, línea ~192):

```csharp
// Reporte de tareas (slice: GET /reportes/tareas) -- D14: SIN cache, a diferencia del
// bloque de arriba. No envolver en un decorator ni tocar IVersionReportes.
builder.Services.AddScoped<IReporteTareasRepository, ReporteTareasRepository>();
builder.Services.AddScoped<IReporteTareasService, ReporteTareasService>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter FullyQualifiedName~ReportesEndpointTests`
Expected: PASS (todos los Facts del archivo, incluidos los preexistentes de valorización/stock-por-categoría/más-movidos/historial y los 5 nuevos de tareas).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Api/Endpoints/ReportesEndpoints.cs src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/ReportesEndpointTests.cs
git commit -m "feat(reportes): expone GET /reportes/tareas"
```

---

### Task 4: ApiClient — `ReporteTareasApiClient`

**Files:**
- Create: `src/StockApp.ApiClient/ReporteTareasApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (cerca de la línea 229, junto a `IReporteStockService`)
- Test: `tests/StockApp.ApiClient.Tests/ReporteTareasApiClientTests.cs`

**Interfaces:**
- Consumes: `IReporteTareasService` (Task 1), `ApiQuery.Construir` (`src/StockApp.ApiClient/ApiQuery.cs`), `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` (`src/StockApp.ApiClient/ApiErrores.cs`).
- Produces: `class ReporteTareasApiClient : IReporteTareasService`, registrado en el DI del cliente desktop.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.ApiClient.Tests/ReporteTareasApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Reportes;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ReporteTareasApiClientTests
{
    [Fact]
    public async Task ObtenerAsync_GETReportesTareas_ConQueryDeFiltro()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            filas = new[]
            {
                new { clasificador = "Centro", pendientes = 1, enCurso = 0, terminadas = 2, canceladas = 0, total = 3 },
            },
            totalGeneral = 3,
        }));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        var filtro = new FiltroReporteTareas(
            AgrupadorTareas.Zona, CriterioFechaTareas.Creacion,
            new DateTime(2026, 1, 1), new DateTime(2026, 12, 31));

        var reporte = await client.ObtenerAsync(filtro);

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.StartsWith("/reportes/tareas?", pathAndQuery);
        Assert.Contains("agrupador=Zona", pathAndQuery);
        Assert.Contains("criterio=Creacion", pathAndQuery);
        Assert.Contains("desde=2026-01-01T00%3A00%3A00.0000000", pathAndQuery);
        Assert.Contains("hasta=2026-12-31T00%3A00%3A00.0000000", pathAndQuery);

        var fila = Assert.Single(reporte.Filas);
        Assert.Equal("Centro", fila.Clasificador);
        Assert.Equal(3, fila.Total);
        Assert.Equal(3, reporte.TotalGeneral);
    }

    [Fact]
    public async Task ObtenerAsync_ConAgrupadorExpedienteYCriterioCierre_ArmaLaQueryCorrecta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { filas = Array.Empty<object>(), totalGeneral = 0 }));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        await client.ObtenerAsync(new FiltroReporteTareas(
            AgrupadorTareas.Expediente, CriterioFechaTareas.Cierre,
            new DateTime(2026, 6, 1), new DateTime(2026, 6, 30)));

        var pathAndQuery = fake.UltimaRequest!.RequestUri!.PathAndQuery;
        Assert.Contains("agrupador=Expediente", pathAndQuery);
        Assert.Contains("criterio=Cierre", pathAndQuery);
    }

    [Fact]
    public async Task ObtenerAsync_403Operador_LanzaUnauthorizedAccess()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Forbidden, "El rol autenticado no tiene permiso para esta acción."));
        var client = new ReporteTareasApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => client.ObtenerAsync(new FiltroReporteTareas(
                AgrupadorTareas.Zona, CriterioFechaTareas.Creacion,
                new DateTime(2026, 1, 1), new DateTime(2026, 12, 31))));
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ReporteTareasApiClientTests`
Expected: FALLA de compilación — `StockApp.ApiClient.ReporteTareasApiClient` no existe.

- [ ] **Step 3: Implementación mínima**

`src/StockApp.ApiClient/ReporteTareasApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Reportes;

namespace StockApp.ApiClient;

/// <summary>IReporteTareasService contra /reportes/tareas (reportes.ver). Sin cache local:
/// el servidor tampoco cachea este reporte (D14) -- cada búsqueda pega al servidor.</summary>
public sealed class ReporteTareasApiClient : IReporteTareasService
{
    private readonly HttpClient _http;

    public ReporteTareasApiClient(HttpClient http) => _http = http;

    public async Task<ReporteTareasDto> ObtenerAsync(FiltroReporteTareas filtro)
    {
        var query = ApiQuery.Construir(
            ("agrupador", filtro.Agrupador.ToString()),
            ("criterio", filtro.Criterio.ToString()),
            ("desde", filtro.Desde.ToString("O")),
            ("hasta", filtro.Hasta.ToString("O")));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("reportes/tareas" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ReporteTareasDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor en el reporte de tareas.");
    }
}
```

En `src/StockApp.Presentation/App.axaml.cs`, agregar junto a la línea `services.AddTransient<IReporteStockService, ReporteStockApiClient>();`:

```csharp
        services.AddTransient<IReporteTareasService, ReporteTareasApiClient>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter FullyQualifiedName~ReporteTareasApiClientTests`
Expected: PASS (3/3).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.ApiClient/ReporteTareasApiClient.cs src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.ApiClient.Tests/ReporteTareasApiClientTests.cs
git commit -m "feat(reportes): agrega ReporteTareasApiClient y lo registra en el DI del desktop"
```

---

### Task 5: Presentation — `ReporteTareasViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs`

**Interfaces:**
- Consumes: `IReporteTareasService.ObtenerAsync(FiltroReporteTareas)` (Task 1), `ViewModelBase.EjecutarCargaProtegidaAsync` (`src/StockApp.Presentation/ViewModels/ViewModelBase.cs`).
- Produces: `record OpcionAgrupador(string Nombre, AgrupadorTareas Valor)`, `class ReporteTareasViewModel : ViewModelBase` con `AgrupadoresDisponibles`, `AgrupadorSeleccionado`, `CriterioSeleccionado`, `EsCriterioCreacion`/`EsCriterioCierre`, `FechaDesde`/`FechaHasta` (default año en curso), `Items`, `TotalGeneral`, `MensajeError`, `BuscarCommand`, `CargarAsync()`.

- [ ] **Step 1: Escribir el test que falla**

Crear `tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Reportes;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Reportes;

public class ReporteTareasViewModelTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private static (ReporteTareasViewModel vm, Mock<IReporteTareasService> servicioMock)
        Crear(ReporteTareasDto? respuesta = null)
    {
        var servicioMock = new Mock<IReporteTareasService>();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ReturnsAsync(respuesta ?? new ReporteTareasDto(new List<FilaReporteTareas>(), 0));

        var vm = new ReporteTareasViewModel(servicioMock.Object);
        return (vm, servicioMock);
    }

    // ── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_DefaultRango_EsElAnioEnCurso()
    {
        var (vm, _) = Crear();

        Assert.Equal(new DateTime(DateTime.Now.Year, 1, 1), vm.FechaDesde);
        Assert.Equal(new DateTime(DateTime.Now.Year, 12, 31), vm.FechaHasta);
    }

    [Fact]
    public void Constructor_DefaultAgrupadorEsZona_YCriterioEsCreacion()
    {
        var (vm, _) = Crear();

        Assert.Equal(AgrupadorTareas.Zona, vm.AgrupadorSeleccionado.Valor);
        Assert.True(vm.EsCriterioCreacion);
        Assert.False(vm.EsCriterioCierre);
    }

    [Fact]
    public void EsCriterioCierre_AlPonerloEnTrue_CambiaCriterioSeleccionado_YDesmarcaElOtro()
    {
        var (vm, _) = Crear();

        vm.EsCriterioCierre = true;

        Assert.Equal(CriterioFechaTareas.Cierre, vm.CriterioSeleccionado);
        Assert.True(vm.EsCriterioCierre);
        Assert.False(vm.EsCriterioCreacion);
    }

    [Fact]
    public async Task CargarAsync_PasaTalCualLasFilasYElTotalGeneral_SinCalcularNada()
    {
        // D23: el ViewModel no calcula nada -- verifica passthrough exacto (misma instancia).
        var filas = new List<FilaReporteTareas>
        {
            new("Centro", 1, 0, 2, 0, 3),
            new("(sin asignar)", 0, 0, 0, 1, 1),
        };
        var dto = new ReporteTareasDto(filas, 4);
        var (vm, _) = Crear(dto);

        await vm.CargarAsync();

        Assert.Same(filas, vm.Items);
        Assert.Equal(4, vm.TotalGeneral);
    }

    [Fact]
    public async Task CargarAsync_ConvierteFechasLocalesAUtc_AntesDeLlamarAlServicio()
    {
        var (vm, servicioMock) = Crear();
        var desde = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var hasta = new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Unspecified);
        vm.FechaDesde = desde;
        vm.FechaHasta = hasta;

        await vm.CargarAsync();

        var offsetDesde = TimeZoneInfo.Local.GetUtcOffset(desde);
        var offsetHasta = TimeZoneInfo.Local.GetUtcOffset(hasta);
        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteTareas>(f =>
            f.Desde == desde - offsetDesde && f.Hasta == hasta - offsetHasta)), Times.Once);
    }

    [Fact]
    public async Task BuscarCommand_ConRangoInvertido_NoLlamaAlServicioYSeteaMensajeError()
    {
        var (vm, servicioMock) = Crear();
        vm.FechaDesde = new DateTime(2026, 12, 1);
        vm.FechaHasta = new DateTime(2026, 1, 1);

        await vm.BuscarCommand.ExecuteAsync(null);

        servicioMock.Verify(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()), Times.Never);
        Assert.False(string.IsNullOrEmpty(vm.MensajeError));
    }

    [Fact]
    public async Task CargarAsync_SiElServicioLanzaUnauthorized_NoPropagaYDejaSinPermiso()
    {
        // bugfix "pantalla muda ante un 403": mismo criterio que el resto de los reportes.
        var (vm, servicioMock) = Crear();
        servicioMock
            .Setup(s => s.ObtenerAsync(It.IsAny<FiltroReporteTareas>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var ex = await Record.ExceptionAsync(() => vm.CargarAsync());

        Assert.Null(ex);
        Assert.True(vm.SinPermiso);
        Assert.False(string.IsNullOrWhiteSpace(vm.MensajeSinPermiso));
    }

    [Fact]
    public async Task CargarAsync_UsaElAgrupadorYCriterioSeleccionados()
    {
        var (vm, servicioMock) = Crear();
        vm.AgrupadorSeleccionado = vm.AgrupadoresDisponibles.Single(o => o.Valor == AgrupadorTareas.Expediente);
        vm.EsCriterioCierre = true;

        await vm.CargarAsync();

        servicioMock.Verify(s => s.ObtenerAsync(It.Is<FiltroReporteTareas>(f =>
            f.Agrupador == AgrupadorTareas.Expediente && f.Criterio == CriterioFechaTareas.Cierre)), Times.Once);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ReporteTareasViewModelTests"`
Expected: FALLA de compilación — `StockApp.Presentation.ViewModels.Reportes.ReporteTareasViewModel` no existe.

- [ ] **Step 3: Implementación mínima**

`src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Reportes;

namespace StockApp.Presentation.ViewModels.Reportes;

/// <summary>Opción del combo de agrupador (mismo patrón que OpcionTipoDocumento en
/// DocumentoListViewModel: un Nombre legible junto al valor real del enum).</summary>
public sealed record OpcionAgrupador(string Nombre, AgrupadorTareas Valor);

/// <summary>
/// ViewModel del reporte-matriz de Tareas (spec 2026-09-08). Consulta
/// <see cref="IReporteTareasService"/> con el agrupador/criterio/rango elegidos.
///
/// D23: este ViewModel NO calcula nada -- ni totales, ni porcentajes, ni conteos. Solo asigna
/// <see cref="Items"/>/<see cref="TotalGeneral"/> con lo que devuelve el servicio, tal cual.
/// </summary>
public partial class ReporteTareasViewModel : ViewModelBase
{
    private readonly IReporteTareasService _servicio;

    public IReadOnlyList<OpcionAgrupador> AgrupadoresDisponibles { get; } = new[]
    {
        new OpcionAgrupador("Zona", AgrupadorTareas.Zona),
        new OpcionAgrupador("Dimensión temática", AgrupadorTareas.DimensionTematica),
        new OpcionAgrupador("Organismo responsable", AgrupadorTareas.OrganismoResponsable),
        new OpcionAgrupador("Origen de financiamiento", AgrupadorTareas.OrigenFinanciamiento),
        new OpcionAgrupador("Expediente", AgrupadorTareas.Expediente),
    };

    [ObservableProperty]
    private OpcionAgrupador _agrupadorSeleccionado;

    [ObservableProperty]
    private CriterioFechaTareas _criterioSeleccionado;

    [ObservableProperty]
    private DateTime _fechaDesde;

    [ObservableProperty]
    private DateTime _fechaHasta;

    [ObservableProperty]
    private IReadOnlyList<FilaReporteTareas> _items = new List<FilaReporteTareas>();

    [ObservableProperty]
    private int _totalGeneral;

    [ObservableProperty]
    private string? _mensajeError;

    /// <summary>Binding de los dos RadioButton (decisión 16): getter/setter propios, no
    /// [ObservableProperty], porque derivan de/mutan CriterioSeleccionado en vez de tener
    /// backing field propio.</summary>
    public bool EsCriterioCreacion
    {
        get => CriterioSeleccionado == CriterioFechaTareas.Creacion;
        set { if (value) CriterioSeleccionado = CriterioFechaTareas.Creacion; }
    }

    public bool EsCriterioCierre
    {
        get => CriterioSeleccionado == CriterioFechaTareas.Cierre;
        set { if (value) CriterioSeleccionado = CriterioFechaTareas.Cierre; }
    }

    public ReporteTareasViewModel(IReporteTareasService servicio)
    {
        _servicio = servicio;
        _agrupadorSeleccionado = AgrupadoresDisponibles[0];
        _criterioSeleccionado = CriterioFechaTareas.Creacion;

        // D18: default del año en curso -- el rango sigue siendo obligatorio y se revalida
        // en el servicio, esto es solo la comodidad inicial de la UI.
        var anioActual = DateTime.Now.Year;
        _fechaDesde = new DateTime(anioActual, 1, 1);
        _fechaHasta = new DateTime(anioActual, 12, 31);
    }

    partial void OnCriterioSeleccionadoChanged(CriterioFechaTareas value)
    {
        OnPropertyChanged(nameof(EsCriterioCreacion));
        OnPropertyChanged(nameof(EsCriterioCierre));
    }

    /// <summary>Dispara la búsqueda del reporte y puebla <see cref="Items"/>/<see cref="TotalGeneral"/>.</summary>
    [RelayCommand]
    private async Task BuscarAsync() => await CargarAsync();

    /// <summary>
    /// Público para poder engancharse desde el auto-load de la vista (<c>DataContextChanged</c>
    /// en <c>ReporteTareasView.axaml.cs</c>), además de desde <see cref="BuscarCommand"/>.
    /// </summary>
    public async Task CargarAsync()
    {
        if (FechaDesde > FechaHasta)
        {
            MensajeError = "La fecha 'Desde' no puede ser posterior a 'Hasta'.";
            return;
        }

        MensajeError = null;
        await EjecutarCargaProtegidaAsync(async () =>
        {
            var filtro = new FiltroReporteTareas(
                AgrupadorSeleccionado.Valor,
                CriterioSeleccionado,
                ALocalAUtc(FechaDesde),
                ALocalAUtc(FechaHasta));

            var reporte = await _servicio.ObtenerAsync(filtro);

            // D23: passthrough exacto -- ni Sum, ni Count, ni Select acá.
            Items = reporte.Filas;
            TotalGeneral = reporte.TotalGeneral;
        }, "No tenés permiso para ver el reporte de tareas.");
    }

    /// <summary>Convierte una fecha LOCAL (la que produce el CalendarDatePicker bindeado a
    /// FechaDesde/FechaHasta) a UTC antes de pasarla al servicio -- mismo criterio que
    /// MasMovidosViewModel.ALocalAUtc: el repositorio compara contra columnas timestamptz.</summary>
    private static DateTime ALocalAUtc(DateTime fechaLocal)
        => DateTime.SpecifyKind(fechaLocal, DateTimeKind.Local).ToUniversalTime();
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ReporteTareasViewModelTests"`
Expected: PASS (8/8).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Reportes/ReporteTareasViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Reportes/ReporteTareasViewModelTests.cs
git commit -m "feat(reportes): agrega ReporteTareasViewModel (D23: sin cálculos, solo passthrough)"
```

---

### Task 6: Presentation — `ReporteTareasView.axaml`, DI y sidebar

**Files:**
- Create: `src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml`
- Create: `src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (cerca de la línea 327, junto a `StockCategoriaViewModel`)
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (grupo "Reportes", cerca de la línea 258-265, y el bloque de comandos `Nav*`, cerca de la línea 520-525)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs` (modify)

**Interfaces:**
- Consumes: `ReporteTareasViewModel` (Task 5), `INavigationService.Navegar<T>()`, `c:HeaderVista`/`c:CampoFormulario`/`c:EstadoVacio` (`src/StockApp.Presentation/Controls/`), `conv:CantidadConverter` (`src/StockApp.Presentation/Converters/CantidadConverter.cs`), `beh:CalendarDatePickerFechaBehavior` (`src/StockApp.Presentation/Behaviors/`).
- Produces: `class ReporteTareasView : UserControl`, item de sidebar "Estadística de tareas" en el grupo "Reportes", comando `NavReporteTareasCommand`.

- [ ] **Step 1: Escribir el test que falla**

Agregar en `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs`, dentro de `NavReportes_LlamaNavegar_ConViewModelCorrecto` (Modify, agregando la línea nueva al mismo Fact — es navegación pura, no necesita un Fact separado):

```csharp
    [Fact]
    public void NavReportes_LlamaNavegar_ConViewModelCorrecto()
    {
        var (vm, _, navMock) = Crear(RolUsuario.Admin);

        vm.NavValorizacionCommand.Execute(null);
        vm.NavStockCategoriaCommand.Execute(null);
        vm.NavHistorialPorProductoCommand.Execute(null);
        vm.NavMasMovidosCommand.Execute(null);
        vm.NavAuditoriaLogCommand.Execute(null);
        vm.NavReporteTareasCommand.Execute(null);

        navMock.Verify(n => n.Navegar<ValorizacionViewModel>(),        Times.Once);
        navMock.Verify(n => n.Navegar<StockCategoriaViewModel>(),      Times.Once);
        navMock.Verify(n => n.Navegar<HistorialPorProductoViewModel>(), Times.Once);
        navMock.Verify(n => n.Navegar<MasMovidosViewModel>(),          Times.Once);
        navMock.Verify(n => n.Navegar<AuditoriaLogViewModel>(),        Times.Once);
        navMock.Verify(n => n.Navegar<ReporteTareasViewModel>(),       Times.Once);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainViewModelReportesTests"`
Expected: FALLA de compilación — `ShellMainViewModel.NavReporteTareasCommand` no existe.

- [ ] **Step 3: Implementación mínima**

En `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, agregar el item al grupo "Reportes" (junto a los demás `CrearItem` de ese grupo, cerca de la línea 264):

```csharp
                CrearItem("Estadística de tareas", "mdi-chart-bar", NavReporteTareasCommand, "ReporteTareas", () => PuedeVerReportes),
```

Y el comando, junto a los demás `Nav*` de Reportes (cerca de la línea 525):

```csharp
    [RelayCommand]
    private void NavReporteTareas()
    {
        SeccionActiva = "ReporteTareas";
        _navigation.Navegar<ReporteTareasViewModel>();
    }
```

`src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.Views.Reportes;

public partial class ReporteTareasView : UserControl
{
    public ReporteTareasView()
    {
        InitializeComponent();

        // No hay un hook de navegación que dispare la carga de datos al mostrar la vista
        // (bug recurrente del proyecto): se cablea acá, igual que StockCategoriaView.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ReporteTareasViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Reportes"
             xmlns:dto="using:StockApp.Application.Reportes"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:c="using:StockApp.Presentation.Controls"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Reportes.ReporteTareasView"
             x:DataType="vm:ReporteTareasViewModel">

    <Grid>
    <DockPanel Margin="{DynamicResource MargenVista}">

        <c:HeaderVista DockPanel.Dock="Top" Eyebrow="REPORTES" Titulo="Estadística de tareas">
            <StackPanel Orientation="Horizontal" Spacing="{DynamicResource Espacio2}">
                <Button Classes="primary" Content="Buscar" Command="{Binding BuscarCommand}" />
            </StackPanel>
        </c:HeaderVista>

        <Border Classes="card">
            <Grid RowDefinitions="Auto,Auto,Auto,*,Auto">

                <!-- Filtros: agrupador, criterio de fecha, rango -->
                <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="{DynamicResource Espacio3}"
                            VerticalAlignment="Bottom" Margin="0,0,0,12">
                    <StackPanel MinWidth="220">
                        <c:CampoFormulario Etiqueta="Agrupar por">
                            <ComboBox ItemsSource="{Binding AgrupadoresDisponibles}"
                                      SelectedItem="{Binding AgrupadorSeleccionado}"
                                      HorizontalAlignment="Stretch">
                                <ComboBox.ItemTemplate>
                                    <DataTemplate>
                                        <TextBlock Text="{Binding Nombre}" />
                                    </DataTemplate>
                                </ComboBox.ItemTemplate>
                            </ComboBox>
                        </c:CampoFormulario>
                    </StackPanel>
                    <TextBlock Text="Desde:" VerticalAlignment="Center" />
                    <CalendarDatePicker SelectedDate="{Binding FechaDesde}"
                                        PlaceholderText="dd/mm/aaaa"
                                        SelectedDateFormat="Custom"
                                        CustomDateFormatString="dd/MM/yyyy"
                                        beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                    <TextBlock Text="Hasta:" VerticalAlignment="Center" />
                    <CalendarDatePicker SelectedDate="{Binding FechaHasta}"
                                        PlaceholderText="dd/mm/aaaa"
                                        SelectedDateFormat="Custom"
                                        CustomDateFormatString="dd/MM/yyyy"
                                        beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />
                </StackPanel>

                <!-- D16: criterio de fecha, radio button de dos opciones -->
                <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="{DynamicResource Espacio3}" Margin="0,0,0,12">
                    <TextBlock Text="Contar por:" VerticalAlignment="Center" />
                    <RadioButton GroupName="CriterioFecha" Content="Fecha de creación"
                                 IsChecked="{Binding EsCriterioCreacion, Mode=TwoWay}" />
                    <RadioButton GroupName="CriterioFecha" Content="Fecha de cierre"
                                 IsChecked="{Binding EsCriterioCierre, Mode=TwoWay}" />
                </StackPanel>

                <!-- D16: aviso explícito -- con criterio Cierre, Pendientes/En curso dan cero -->
                <TextBlock Grid.Row="2"
                           Text="Criterio 'Fecha de cierre': solo se cuentan tareas Terminadas o Canceladas. Las columnas 'Pendientes' y 'En curso' muestran 0 a propósito, no faltan datos."
                           Foreground="{DynamicResource WarningBrush}"
                           IsVisible="{Binding EsCriterioCierre}"
                           Margin="0,0,0,12"
                           TextWrapping="Wrap" />

                <!-- Mensaje de error de rango -->
                <TextBlock Grid.Row="2"
                           Text="{Binding MensajeError}"
                           Foreground="{DynamicResource DangerBrush}"
                           IsVisible="{Binding MensajeError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
                           Margin="0,0,0,12"
                           TextWrapping="Wrap" />

                <!-- Grilla del reporte-matriz -->
                <DataGrid Grid.Row="3"
                          ItemsSource="{Binding Items}"
                          IsReadOnly="True"
                          CanUserResizeColumns="True"
                          GridLinesVisibility="Horizontal"
                          ScrollViewer.HorizontalScrollBarVisibility="Auto">
                    <DataGrid.Columns>
                        <DataGridTemplateColumn Header="Clasificador" Width="*" SortMemberPath="Clasificador">
                            <DataGridTemplateColumn.CellTemplate>
                                <DataTemplate x:DataType="dto:FilaReporteTareas">
                                    <TextBlock Text="{Binding Clasificador}"
                                               TextTrimming="CharacterEllipsis"
                                               ToolTip.Tip="{Binding Clasificador}"
                                               VerticalAlignment="Center" Margin="4,0" />
                                </DataTemplate>
                            </DataGridTemplateColumn.CellTemplate>
                        </DataGridTemplateColumn>
                        <DataGridTextColumn Binding="{Binding Pendientes, Converter={x:Static conv:CantidadConverter.Instance}}"
                                            Width="Auto" MinWidth="90" CellStyleClasses="num">
                            <DataGridTextColumn.Header>
                                <TextBlock Text="Pendientes" HorizontalAlignment="Right" TextAlignment="Right" />
                            </DataGridTextColumn.Header>
                        </DataGridTextColumn>
                        <DataGridTextColumn Binding="{Binding EnCurso, Converter={x:Static conv:CantidadConverter.Instance}}"
                                            Width="Auto" MinWidth="90" CellStyleClasses="num">
                            <DataGridTextColumn.Header>
                                <TextBlock Text="En curso" HorizontalAlignment="Right" TextAlignment="Right" />
                            </DataGridTextColumn.Header>
                        </DataGridTextColumn>
                        <DataGridTextColumn Binding="{Binding Terminadas, Converter={x:Static conv:CantidadConverter.Instance}}"
                                            Width="Auto" MinWidth="90" CellStyleClasses="num">
                            <DataGridTextColumn.Header>
                                <TextBlock Text="Terminadas" HorizontalAlignment="Right" TextAlignment="Right" />
                            </DataGridTextColumn.Header>
                        </DataGridTextColumn>
                        <DataGridTextColumn Binding="{Binding Canceladas, Converter={x:Static conv:CantidadConverter.Instance}}"
                                            Width="Auto" MinWidth="90" CellStyleClasses="num">
                            <DataGridTextColumn.Header>
                                <TextBlock Text="Canceladas" HorizontalAlignment="Right" TextAlignment="Right" />
                            </DataGridTextColumn.Header>
                        </DataGridTextColumn>
                        <DataGridTextColumn Binding="{Binding Total, Converter={x:Static conv:CantidadConverter.Instance}}"
                                            Width="Auto" MinWidth="90" CellStyleClasses="num">
                            <DataGridTextColumn.Header>
                                <TextBlock Text="Total" HorizontalAlignment="Right" TextAlignment="Right" />
                            </DataGridTextColumn.Header>
                        </DataGridTextColumn>
                    </DataGrid.Columns>
                </DataGrid>

                <!-- Total general: passthrough directo de ReporteTareasDto.TotalGeneral (D23) -->
                <StackPanel Grid.Row="4" Orientation="Horizontal" Spacing="{DynamicResource Espacio2}"
                            HorizontalAlignment="Right" Margin="0,12,0,0">
                    <TextBlock Text="Total general:" FontWeight="Bold" />
                    <TextBlock Text="{Binding TotalGeneral, Converter={x:Static conv:CantidadConverter.Instance}}"
                               FontWeight="Bold" />
                </StackPanel>

            </Grid>
        </Border>

    </DockPanel>

        <!-- bugfix "pantalla muda ante un 403": ver ViewModelBase.EjecutarCargaProtegidaAsync. -->
        <c:EstadoVacio Titulo="Sin permiso"
                       Mensaje="{Binding MensajeSinPermiso}"
                       EsError="True"
                       IsVisible="{Binding SinPermiso}" />
    </Grid>

</UserControl>
```

En `src/StockApp.Presentation/App.axaml.cs`, agregar junto a `services.AddTransient<StockCategoriaViewModel>();`:

```csharp
        services.AddTransient<ReporteTareasViewModel>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `timeout 180 dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ShellMainViewModelReportesTests"`
Expected: PASS.

Run: `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`
Expected: build exitoso — confirma que `ReporteTareasView.axaml` compila con compiled bindings (`x:DataType`) sin errores AVLN1001/binding.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml \
        src/StockApp.Presentation/Views/Reportes/ReporteTareasView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs
git commit -m "feat(reportes): agrega la vista del reporte-matriz de tareas y su entrada en el sidebar"
```

---

## Índice de tareas

1. [Task 1: Application — DTOs, enums, `IReporteTareasService`, `IReporteTareasRepository` y `ReporteTareasService`](#task-1-application--dtos-enums-ireportetareasservice-ireportetareasrepository-y-reportetareasservice)
2. [Task 2: Infrastructure — `ReporteTareasRepository` con `GROUP BY` real contra Postgres](#task-2-infrastructure--reportetareasrepository-con-group-by-real-contra-postgres)
3. [Task 3: Api — `GET /reportes/tareas`](#task-3-api--get-reportestareas)
4. [Task 4: ApiClient — `ReporteTareasApiClient`](#task-4-apiclient--reportetareasapiclient)
5. [Task 5: Presentation — `ReporteTareasViewModel`](#task-5-presentation--reportetareasviewmodel)
6. [Task 6: Presentation — `ReporteTareasView.axaml`, DI y sidebar](#task-6-presentation--reportetareasviewaxaml-di-y-sidebar)
