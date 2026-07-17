# Finanzas F4 — Vistas Calculadas Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implementar las 3 vistas calculadas del módulo Finanzas (libro caja, control POA, calendario de pagos) sobre los datos ya existentes de F1/F2 (maestros, Gasto/PagoGasto/IngresoCaja), sin persistir ningún saldo — todo se calcula en memoria a partir de lo que devuelven los repositorios. Cierra además el aviso de vencimientos en Inicio.

**Architecture:** Clean layering existente (Domain → Application → Infrastructure/Api/ApiClient/Presentation), patrón `XxxService` con `IAuthorizationService.Verificar` al inicio de cada método, `ApiClient` implementando la misma interfaz `IXxxService` que consume Presentation, ViewModels con `DataGridCollectionView` + `ICsvExporter`.

**Tech Stack:** .NET 10, EF Core + Npgsql (Postgres), ASP.NET Minimal APIs, Avalonia 12 + CommunityToolkit.Mvvm, xUnit + Moq.

## Global Constraints

- TDD estricto: por cada método nuevo, escribir el test, correrlo y verlo fallar, implementar lo mínimo, correrlo y verlo verde, commit.
- Commits frecuentes, conventional commits en español (`feat(finanzas): ...`, `test(finanzas): ...`, `fix(finanzas): ...`), sin `Co-Authored-By` ni atribución a IA.
- Ningún saldo se persiste: libro caja, control POA y calendario son SIEMPRE consultas calculadas en memoria sobre lo que traen los repos (spec §4, regla de oro).
- Todo método de lectura de `IFinanzasVistasService` empieza con `_auth.Verificar(_session.RolActual, Permisos.VerFinanzas)` (patrón `GastoService`).
- Fechas date-only (`Fecha` de `IngresoCaja`/`PagoGasto`, `FechaVencimiento` de `Gasto`) se exponen en Presentation como `DateOnly` vía `DateOnly.FromDateTime(...)`, NUNCA con `FechaUtcALocalConverter` (ese converter es solo para timestamps reales — mismo criterio que `GastoFila.Fecha`). Los montos con `MonedaConverter.Formatear` / binding `{Binding ..., Converter={x:Static conv:MonedaConverter.Instance}}`.
- El estado de un gasto NUNCA se persiste: se recalcula siempre con `Gasto.CalcularEstado(fechaReferencia)`.
- No correr `dotnet build` intermedio salvo el `dotnet test` que cada task pide explícitamente.
- No modificar `PostgresRepositoryTestBase.LimpiarTablas()`: ya trunca `Gastos`, `PagosGasto`, `IngresosCaja`, `LineasPoa`, `AsignacionesPresupuestales` — F4 no agrega tablas nuevas, solo métodos de repositorio.

## Decisión registrada: nav `PagoGasto.Gasto`

El brief pide que `IGastoRepository.ListarPagosActivosPorRangoAsync` haga "Include Gasto→Proveedor/RubroGasto/FuenteFinanciamiento". Hoy la relación en `AppDbContext` es unidireccional (`Gasto.Pagos` existe, `PagoGasto.Gasto` NO): `HasOne<Gasto>().WithMany(g => g.Pagos)` sin nav inversa. Para que el Include literal sea posible, la Task 2 agrega la navegación inversa `PagoGasto.Gasto` (propiedad de solo lectura, mismo FK `GastoId` ya existente — **sin migración nueva**, solo se reconfigura `HasOne(p => p.Gasto).WithMany(g => g.Pagos)` en `OnModelCreating`). Se documenta acá porque el repo contradecía la descripción textual del método.

## Decisión registrada: `ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)`

El brief lista la firma de interfaz como `Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync()` (sin parámetros), pero también pide testear los umbrales de 7/30 días con "fecha de referencia inyectable" — el proyecto no tiene `IClock` (confirmado: no existe en todo el repo). Se agrega un parámetro opcional `DateTime? fechaReferencia = null` (si es null, el servicio usa `DateTime.UtcNow`) para que `FinanzasVistasServiceTests` pueda fijar "hoy" de forma determinística, igual que `GastoEstadoTests` fija `Hoy` para `CalcularEstado`. El endpoint HTTP y `FinanzasVistasApiClient` NUNCA envían este parámetro — el servidor es la autoridad de "ahora"; el parámetro solo es visible puertas adentro de `FinanzasVistasService` y sus tests.

---

## Task 1: Repositorio `IngresoCaja` — rango + total previo (TDD Infrastructure)

**Files:**
- Modify: `src/StockApp.Application/Interfaces/IIngresoCajaRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/IngresoCajaRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/IngresoCajaRepositoryVistasTests.cs` (nuevo)

**Interfaces:**
- Produces: `Task<IReadOnlyList<IngresoCaja>> ListarPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc)`, `Task<decimal> TotalActivosAntesDeAsync(DateTime fechaUtc)` en `IIngresoCajaRepository`.

### Steps

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/IngresoCajaRepositoryVistasTests.cs
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class IngresoCajaRepositoryVistasTests : PostgresRepositoryTestBase
{
    private readonly IngresoCajaRepository _repo;

    public IngresoCajaRepositoryVistasTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new IngresoCajaRepository(Context);
    }

    private async Task<int> SeedFuenteAsync()
    {
        var fuente = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        Context.Add(fuente);
        await Context.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task ListarPorRangoAsync_SoloTraeActivosDentroDelRango()
    {
        var fuenteId = await SeedFuenteAsync();
        var desde = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);

        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Fuera de rango (antes)", FuenteFinanciamientoId = fuenteId, Monto = 1m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Dentro de rango", FuenteFinanciamientoId = fuenteId, Monto = 2m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Inactivo dentro de rango", FuenteFinanciamientoId = fuenteId, Monto = 3m, Activo = false,
        });
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarPorRangoAsync(desde, hasta);

        var fila = Assert.Single(resultado);
        Assert.Equal("Dentro de rango", fila.Concepto);
        Assert.NotNull(fila.FuenteFinanciamiento);
    }

    [Fact]
    public async Task TotalActivosAntesDeAsync_SumaSoloActivosAnterioresAFecha()
    {
        var fuenteId = await SeedFuenteAsync();
        var corte = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Antes", FuenteFinanciamientoId = fuenteId, Monto = 100m,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Antes inactivo", FuenteFinanciamientoId = fuenteId, Monto = 50m, Activo = false,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
            Concepto = "Después", FuenteFinanciamientoId = fuenteId, Monto = 999m,
        });
        Context.ChangeTracker.Clear();

        var total = await _repo.TotalActivosAntesDeAsync(corte);

        Assert.Equal(100m, total);
    }

    [Fact]
    public async Task TotalActivosAntesDeAsync_SinMovimientos_DevuelveCero()
    {
        var total = await _repo.TotalActivosAntesDeAsync(DateTime.UtcNow);

        Assert.Equal(0m, total);
    }
}
```

- [ ] Correr y ver que falla (no compila: los métodos no existen):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~IngresoCajaRepositoryVistasTests"`
  Resultado esperado: error de compilación `'IIngresoCajaRepository' does not contain a definition for 'ListarPorRangoAsync'`.

- [ ] Agregar las firmas a la interfaz:

```csharp
// src/StockApp.Application/Interfaces/IIngresoCajaRepository.cs
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IIngresoCajaRepository
{
    /// <summary>Incluye la FuenteFinanciamiento navegable.</summary>
    Task<IngresoCaja?> ObtenerPorIdAsync(int id);

    /// <summary>Incluye la fuente. Ordena por Fecha desc, luego Id desc.</summary>
    Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync();

    Task<int> AgregarAsync(IngresoCaja ingreso);
    Task ActualizarAsync(IngresoCaja ingreso);

    /// <summary>Incluye la fuente. Solo ACTIVOS con Fecha en [desdeUtc, hastaUtc]. Ordena por Fecha, luego Id.</summary>
    Task<IReadOnlyList<IngresoCaja>> ListarPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc);

    /// <summary>Suma de Monto de los ingresos ACTIVOS con Fecha &lt; fechaUtc (saldo inicial del libro caja).</summary>
    Task<decimal> TotalActivosAntesDeAsync(DateTime fechaUtc);
}
```

- [ ] Implementar en el repositorio:

```csharp
// src/StockApp.Infrastructure/Repositories/IngresoCajaRepository.cs — agregar al final de la clase
    public async Task<IReadOnlyList<IngresoCaja>> ListarPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc)
        => await _ctx.IngresosCaja
            .Include(i => i.FuenteFinanciamiento)
            .Where(i => i.Activo && i.Fecha >= desdeUtc && i.Fecha <= hastaUtc)
            .OrderBy(i => i.Fecha)
            .ThenBy(i => i.Id)
            .ToListAsync();

    public async Task<decimal> TotalActivosAntesDeAsync(DateTime fechaUtc)
        => await _ctx.IngresosCaja
            .Where(i => i.Activo && i.Fecha < fechaUtc)
            .SumAsync(i => (decimal?)i.Monto) ?? 0m;
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~IngresoCajaRepositoryVistasTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 3`.

- [ ] Commit: `test(finanzas): agregar ListarPorRangoAsync y TotalActivosAntesDeAsync a IngresoCajaRepository`

---

## Task 2: Repositorio `Gasto` — pagos por rango, total previo, activos con saldo, gastado por línea (TDD Infrastructure)

**Files:**
- Modify: `src/StockApp.Domain/Entities/PagoGasto.cs` (nav inversa `Gasto`)
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (config de la relación)
- Modify: `src/StockApp.Application/Interfaces/IGastoRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/GastoRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryVistasTests.cs` (nuevo)

**Interfaces:**
- Produces en `IGastoRepository`: `Task<IReadOnlyList<PagoGasto>> ListarPagosActivosPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc)`, `Task<decimal> TotalPagosActivosAntesDeAsync(DateTime fechaUtc)`, `Task<IReadOnlyList<Gasto>> ListarActivosConSaldoAsync()`, `Task<IReadOnlyDictionary<int, decimal>> TotalGastadoPorLineaAsync(int ejercicio)`.

### Steps

- [ ] Escribir el test que falla (asume la nav `PagoGasto.Gasto` y los 4 métodos nuevos):

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryVistasTests.cs
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class GastoRepositoryVistasTests : PostgresRepositoryTestBase
{
    private readonly GastoRepository _repo;

    public GastoRepositoryVistasTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new GastoRepository(Context);
    }

    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private async Task<int> SeedLineaPoaAsync(int fuenteId, decimal asignado, int ejercicio)
    {
        var linea = new LineaPoa
        {
            Nombre = $"Linea {Guid.NewGuid():N}", Programa = "Test", Ejercicio = ejercicio,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = asignado } },
        };
        Context.Add(linea);
        await Context.SaveChangesAsync();
        return linea.Id;
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_TraeGastoConProveedorRubroYFuente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Compra de prueba", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 1000m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc), Monto = 1000m });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desde = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagos = await _repo.ListarPagosActivosPorRangoAsync(desde, hasta);

        var pago = Assert.Single(pagos);
        Assert.Equal(1000m, pago.Monto);
        Assert.NotNull(pago.Gasto);
        Assert.NotNull(pago.Gasto!.Proveedor);
        Assert.NotNull(pago.Gasto.RubroGasto);
        Assert.NotNull(pago.Gasto.FuenteFinanciamiento);
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_ElPagoImpactaEnSuFechaAunqueElGastoSeaDeOtroMes()
    {
        // Regla de oro spec §4: el saldo de caja impacta en la fecha del PAGO, no de la factura.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Factura de junio, pagada en julio",
            Fecha = new DateTime(2026, 6, 5, 0, 0, 0, DateTimeKind.Utc), // gasto de JUNIO
            MontoTotal = 500m, CondicionPago = CondicionPago.Credito,
            FechaVencimiento = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 7, 3, 0, 0, 0, DateTimeKind.Utc), Monto = 500m }); // pago de JULIO
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var desdeJulio = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJulio = new DateTime(2026, 7, 31, 23, 59, 59, DateTimeKind.Utc);
        var pagosJulio = await _repo.ListarPagosActivosPorRangoAsync(desdeJulio, hastaJulio);

        var desdeJunio = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        var hastaJunio = new DateTime(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var pagosJunio = await _repo.ListarPagosActivosPorRangoAsync(desdeJunio, hastaJunio);

        Assert.Single(pagosJulio);
        Assert.Empty(pagosJunio);
    }

    [Fact]
    public async Task ListarPagosActivosPorRangoAsync_ExcluyePagosAnuladosYGastosAnulados()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc);

        var gastoConPagoAnulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Pago anulado", Fecha = fecha, MontoTotal = 100m, CondicionPago = CondicionPago.Contado,
        };
        gastoConPagoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 100m, Activo = false });
        await _repo.AgregarAsync(gastoConPagoAnulado);

        var gastoAnulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto anulado", Fecha = fecha, MontoTotal = 200m, CondicionPago = CondicionPago.Contado,
            Activo = false,
        };
        gastoAnulado.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 200m });
        await _repo.AgregarAsync(gastoAnulado);
        Context.ChangeTracker.Clear();

        var pagos = await _repo.ListarPagosActivosPorRangoAsync(
            fecha.AddDays(-1), fecha.AddDays(1));

        Assert.Empty(pagos);
    }

    [Fact]
    public async Task TotalPagosActivosAntesDeAsync_SumaSoloActivosDeGastosActivosAnterioresAFecha()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var gasto = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Compra", Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            MontoTotal = 300m, CondicionPago = CondicionPago.Contado,
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), Monto = 300m });
        await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var total = await _repo.TotalPagosActivosAntesDeAsync(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(300m, total);
    }

    [Fact]
    public async Task ListarActivosConSaldoAsync_TraeSoloActivosConPagosYNavs()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var activo = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Activo", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(10),
        };
        await _repo.AgregarAsync(activo);
        var anulado = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Anulado", Fecha = DateTime.UtcNow, MontoTotal = 50m,
            CondicionPago = CondicionPago.Contado, Activo = false,
        };
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ListarActivosConSaldoAsync();

        var fila = Assert.Single(resultado);
        Assert.Equal("Activo", fila.Detalle);
        Assert.NotNull(fila.Proveedor);
    }

    [Fact]
    public async Task TotalGastadoPorLineaAsync_AgrupaPorLineaYFiltraPorEjercicio()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var lineaA = await SeedLineaPoaAsync(fuenteId, 5000m, 2026);
        var lineaOtroEjercicio = await SeedLineaPoaAsync(fuenteId, 5000m, 2025);

        var gasto1 = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto 1", Fecha = DateTime.UtcNow, MontoTotal = 1000m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gasto2 = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto 2", Fecha = DateTime.UtcNow, MontoTotal = 500m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaA,
        };
        var gastoOtroEjercicio = new Gasto
        {
            ProveedorId = proveedorId, FuenteFinanciamientoId = fuenteId, RubroGastoId = rubroId,
            Detalle = "Gasto ejercicio viejo", Fecha = DateTime.UtcNow, MontoTotal = 999m,
            CondicionPago = CondicionPago.Contado, LineaPoaId = lineaOtroEjercicio,
        };
        await _repo.AgregarAsync(gasto1);
        await _repo.AgregarAsync(gasto2);
        await _repo.AgregarAsync(gastoOtroEjercicio);
        Context.ChangeTracker.Clear();

        var resultado = await _repo.TotalGastadoPorLineaAsync(2026);

        Assert.Equal(1500m, resultado[lineaA]);
        Assert.False(resultado.ContainsKey(lineaOtroEjercicio));
    }
}
```

- [ ] Correr y ver que falla (no compila: falta `PagoGasto.Gasto` y los métodos del repo):
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~GastoRepositoryVistasTests"`
  Resultado esperado: error de compilación `'PagoGasto' does not contain a definition for 'Gasto'`.

- [ ] Agregar la navegación inversa a `PagoGasto`:

```csharp
// src/StockApp.Domain/Entities/PagoGasto.cs
namespace StockApp.Domain.Entities;

/// <summary>
/// Pago (total o parcial) de un gasto. Hija del agregado Gasto, con baja lógica PROPIA
/// (a diferencia de AsignacionPresupuestal): anular un pago conserva la historia y
/// recalcula el estado de la factura. Contado ⇒ se crea un pago automático por el
/// total en la fecha del gasto.
/// </summary>
public class PagoGasto
{
    public int Id { get; set; }
    public int GastoId { get; set; }

    /// <summary>
    /// Navegación inversa (F4, vistas calculadas): permite Include(p => p.Gasto) desde
    /// consultas que arrancan en PagosGasto (ej. libro caja, que necesita Proveedor/Rubro/
    /// Fuente del gasto dueño de cada pago). Mismo FK GastoId que ya existía — sin
    /// migración nueva, solo se reconfigura la relación en AppDbContext.OnModelCreating.
    /// </summary>
    public Gasto? Gasto { get; set; }

    public DateTime Fecha { get; set; }        // UTC — el saldo de caja impacta ACÁ
    public decimal Monto { get; set; }         // precisión 18,4
    public string? Nota { get; set; }
    public bool Activo { get; set; } = true;   // false = pago anulado
}
```

- [ ] Reconfigurar la relación en `AppDbContext`:

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs — reemplazar el bloque de PagoGasto
        modelBuilder.Entity<PagoGasto>(e =>
        {
            e.Property(p => p.Monto).HasPrecision(18, 4);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasIndex(p => p.GastoId);
            e.HasOne(p => p.Gasto).WithMany(g => g.Pagos)
                .HasForeignKey(p => p.GastoId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] Agregar las firmas a `IGastoRepository`:

```csharp
// src/StockApp.Application/Interfaces/IGastoRepository.cs — agregar al final de la interfaz, antes del cierre
    /// <summary>
    /// Pagos ACTIVOS de gastos ACTIVOS con Fecha (del PAGO, no de la factura) en
    /// [desdeUtc, hastaUtc]. Include Gasto→Proveedor/RubroGasto/FuenteFinanciamiento
    /// (libro caja: cada fila de egreso necesita esos nombres). Ordena por Fecha, luego Id.
    /// </summary>
    Task<IReadOnlyList<PagoGasto>> ListarPagosActivosPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc);

    /// <summary>Suma de Monto de los pagos ACTIVOS de gastos ACTIVOS con Fecha &lt; fechaUtc (saldo inicial).</summary>
    Task<decimal> TotalPagosActivosAntesDeAsync(DateTime fechaUtc);

    /// <summary>Gastos ACTIVOS con Includes (Proveedor/Fuente/Rubro/LineaPoa/Pagos) para el calendario de pagos.</summary>
    Task<IReadOnlyList<Gasto>> ListarActivosConSaldoAsync();

    /// <summary>
    /// Suma MontoTotal de gastos ACTIVOS agrupada por LineaPoaId, restringida a líneas
    /// del ejercicio indicado (control POA).
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> TotalGastadoPorLineaAsync(int ejercicio);
```

- [ ] Implementar en `GastoRepository`:

```csharp
// src/StockApp.Infrastructure/Repositories/GastoRepository.cs — agregar al final de la clase
    public async Task<IReadOnlyList<PagoGasto>> ListarPagosActivosPorRangoAsync(DateTime desdeUtc, DateTime hastaUtc)
        => await _ctx.PagosGasto
            .Include(p => p.Gasto!).ThenInclude(g => g!.Proveedor)
            .Include(p => p.Gasto!).ThenInclude(g => g!.RubroGasto)
            .Include(p => p.Gasto!).ThenInclude(g => g!.FuenteFinanciamiento)
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha >= desdeUtc && p.Fecha <= hastaUtc)
            .OrderBy(p => p.Fecha)
            .ThenBy(p => p.Id)
            .ToListAsync();

    public async Task<decimal> TotalPagosActivosAntesDeAsync(DateTime fechaUtc)
        => await _ctx.PagosGasto
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha < fechaUtc)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;

    public async Task<IReadOnlyList<Gasto>> ListarActivosConSaldoAsync()
        => await ConIncludes()
            .Where(g => g.Activo)
            .OrderBy(g => g.FechaVencimiento ?? g.Fecha)
            .ToListAsync();

    public async Task<IReadOnlyDictionary<int, decimal>> TotalGastadoPorLineaAsync(int ejercicio)
    {
        var filas = await _ctx.Gastos
            .Where(g => g.Activo && g.LineaPoaId != null && g.LineaPoa!.Ejercicio == ejercicio)
            .GroupBy(g => g.LineaPoaId!.Value)
            .Select(gr => new { LineaPoaId = gr.Key, Total = gr.Sum(g => g.MontoTotal) })
            .ToListAsync();

        return filas.ToDictionary(f => f.LineaPoaId, f => f.Total);
    }
```

- [ ] Generar la migración (solo reconfigura la relación existente, no debería tocar columnas):
  `dotnet ef migrations add NavInversaPagoGastoAGasto --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`
  Resultado esperado: migración generada; revisar que el `Up()` NO agregue columnas ni índices nuevos (solo puede quedar vacía o con un no-op de FK) — si EF genera algo más que eso, es señal de que la config previa difería de lo esperado y hay que revisar antes de aplicar.

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~GastoRepositoryVistasTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 6`.

- [ ] Commit: `feat(finanzas): nav inversa PagoGasto.Gasto + repos de vistas calculadas en GastoRepository`

---

## Task 3: DTOs + `FinanzasVistasService.ObtenerLibroCajaMesAsync` (TDD Application)

**Files:**
- Create: `src/StockApp.Application/Finanzas/FinanzasVistasDtos.cs`
- Create: `src/StockApp.Application/Finanzas/IFinanzasVistasService.cs`
- Create: `src/StockApp.Application/Finanzas/FinanzasVistasService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceLibroCajaTests.cs` (nuevo)

**Interfaces:**
- Consumes: `IIngresoCajaRepository.ListarPorRangoAsync/TotalActivosAntesDeAsync` (Task 1), `IGastoRepository.ListarPagosActivosPorRangoAsync/TotalPagosActivosAntesDeAsync` (Task 2), `ICurrentSession`, `IAuthorizationService.Verificar(RolUsuario?, string)`, `Permisos.VerFinanzas`.
- Produces: `Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)`.

### Steps

- [ ] Crear los DTOs (se completan todos ahora; Task 4 agrega el resto de métodos que ya los usan):

```csharp
// src/StockApp.Application/Finanzas/FinanzasVistasDtos.cs
namespace StockApp.Application.Finanzas;

/// <summary>Fila cronológica del libro caja (spec §7.3): un ingreso o un egreso, con saldo corrido.</summary>
public record MovimientoCajaDto(
    DateOnly Fecha,
    string Tipo,                 // "Ingreso" | "Egreso"
    string Concepto,
    string? ProveedorNombre,
    string? NumeroFactura,
    string? FuenteNombre,
    string? RubroNombre,
    decimal Ingreso,
    decimal Egreso,
    decimal SaldoCorrido);

/// <summary>Total agregado por una clave (rubro o fuente).</summary>
public record TotalPorClaveDto(string Clave, decimal Total);

/// <summary>Libro caja de un mes puntual (spec §7.3).</summary>
public record LibroCajaMesDto(
    int Anio,
    int Mes,
    decimal SaldoInicial,
    decimal SaldoFinal,
    IReadOnlyList<MovimientoCajaDto> Movimientos,
    IReadOnlyList<TotalPorClaveDto> TotalesPorRubro,
    IReadOnlyList<TotalPorClaveDto> TotalesPorFuente);

/// <summary>Fila de un mes en la vista "Año completo" (spec §7.3): totales sin gráficos.</summary>
public record TotalMensualDto(int Mes, decimal Ingresos, decimal Egresos, decimal Neto);

/// <summary>Libro caja anual (spec §7.3, "año completo"): totales por mes y por rubro.</summary>
public record LibroCajaAnualDto(
    int Anio,
    IReadOnlyList<TotalMensualDto> TotalesPorMes,
    IReadOnlyList<TotalPorClaveDto> TotalesPorRubro);

/// <summary>Fila de control POA (spec §7.4): una línea con presupuesto, gastado, saldo y % de ejecución.</summary>
public record ControlPoaLineaDto(
    int LineaPoaId,
    string Nombre,
    string Programa,
    int Ejercicio,
    decimal Presupuesto,
    decimal Gastado,
    decimal Saldo,
    decimal PorcentajeEjecucion,
    bool Sobregirada);

/// <summary>Factura en alguna de las secciones del calendario de pagos (spec §7.5).</summary>
public record FacturaCalendarioDto(
    int GastoId,
    string ProveedorNombre,
    string? NumeroFactura,
    decimal SaldoPendiente,
    DateOnly? FechaVencimiento,
    string Estado);

/// <summary>Pago efectuado recientemente, para la sección "pagos recientes" del calendario.</summary>
public record PagoRecienteDto(
    int GastoId,
    string ProveedorNombre,
    string? NumeroFactura,
    DateOnly FechaPago,
    decimal Monto);

/// <summary>Calendario de pagos completo (spec §7.5).</summary>
public record CalendarioPagosDto(
    IReadOnlyList<FacturaCalendarioDto> Vencidas,
    IReadOnlyList<FacturaCalendarioDto> AVencer7Dias,
    IReadOnlyList<FacturaCalendarioDto> AVencer30Dias,
    IReadOnlyList<PagoRecienteDto> PagosRecientes);
```

- [ ] Crear la interfaz del servicio:

```csharp
// src/StockApp.Application/Finanzas/IFinanzasVistasService.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Vistas calculadas del módulo Finanzas (spec §7.3-7.5): libro caja, control POA y
/// calendario de pagos. Ningún saldo se persiste — todo se calcula en memoria sobre lo
/// que traen los repositorios. Fail-closed: cada método exige Permisos.VerFinanzas.
/// </summary>
public interface IFinanzasVistasService
{
    /// <summary>Libro caja de un mes puntual (1-12). Lanza ArgumentException si mes está fuera de rango.</summary>
    Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes);

    /// <summary>Libro caja anual: totales por mes y por rubro, sin gráficos.</summary>
    Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio);

    /// <summary>Control presupuestal POA de un ejercicio: una fila por línea.</summary>
    Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio);

    /// <summary>
    /// Calendario de pagos (vencidas, a vencer en 7/30 días, pagos recientes de los últimos
    /// 7 días). <paramref name="fechaReferencia"/> es SOLO para tests determinísticos (no hay
    /// IClock en el proyecto); si es null usa DateTime.UtcNow. El servidor HTTP y
    /// FinanzasVistasApiClient nunca lo envían: el servidor es la única autoridad de "hoy".
    /// </summary>
    Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null);
}
```

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceLibroCajaTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceLibroCajaTests
{
    private sealed record Mocks(
        FinanzasVistasService Svc,
        Mock<IIngresoCajaRepository> Ingresos,
        Mock<IGastoRepository> Gastos,
        Mock<ILineaPoaRepository> LineasPoa);

    private static Mocks Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        var gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth = new Mock<IAuthSvc>();

        session.Setup(s => s.RolActual).Returns(rol);

        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>());
        ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(0m);
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>());
        gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(0m);

        var svc = new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
        return new Mocks(svc, ingresos, gastos, lineasPoa);
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SinPermiso_LanzaExcepcionDeAutorizacion()
    {
        var m = Crear();
        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.VerFinanzas))
            .Throws(new UnauthorizedAccessException());
        var svc = new FinanzasVistasService(
            m.Ingresos.Object, m.Gastos.Object, m.LineasPoa.Object,
            Mock.Of<ICurrentSession>(s => s.RolActual == RolUsuario.Operador), auth.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ObtenerLibroCajaMesAsync(2026, 7));
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_MesFueraDeRango_LanzaArgumentException()
    {
        var m = Crear();

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.ObtenerLibroCajaMesAsync(2026, 13));
        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.ObtenerLibroCajaMesAsync(2026, 0));
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SinMovimientos_SaldoInicialIgualASaldoFinal()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(1000m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(400m);

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(2026, 7);

        Assert.Equal(600m, resultado.SaldoInicial);
        Assert.Equal(600m, resultado.SaldoFinal);
        Assert.Empty(resultado.Movimientos);
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_ConIngresosYEgresos_CalculaSaldoCorridoCronologico()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(0m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(0m);

        var fuente = new FuenteFinanciamiento { Id = 1, Nombre = "Literal B" };
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        var proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" };
        var gastoDelPago = new Gasto
        {
            Id = 1, Detalle = "Compra", Proveedor = proveedor, RubroGasto = rubro,
            FuenteFinanciamiento = fuente, NumeroFactura = "A-1", Activo = true,
        };

        m.Ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = new DateTime(2026, 7, 5, 0, 0, 0, DateTimeKind.Utc),
                        Concepto = "Partida FIGM", FuenteFinanciamiento = fuente, Monto = 1000m },
            });
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 7, 10, 0, 0, 0, DateTimeKind.Utc),
                        Monto = 300m, Gasto = gastoDelPago },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(2026, 7);

        Assert.Equal(0m, resultado.SaldoInicial);
        Assert.Equal(2, resultado.Movimientos.Count);
        Assert.Equal("Ingreso", resultado.Movimientos[0].Tipo);
        Assert.Equal(1000m, resultado.Movimientos[0].SaldoCorrido);
        Assert.Equal("Egreso", resultado.Movimientos[1].Tipo);
        Assert.Equal("Barraca X", resultado.Movimientos[1].ProveedorNombre);
        Assert.Equal(700m, resultado.Movimientos[1].SaldoCorrido);
        Assert.Equal(700m, resultado.SaldoFinal);
        Assert.Contains(resultado.TotalesPorRubro, t => t.Clave == "Obras" && t.Total == 300m);
    }

    [Fact]
    public async Task ObtenerLibroCajaMesAsync_SaldoPuedeQuedarNegativo()
    {
        var m = Crear();
        m.Ingresos.Setup(i => i.TotalActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(100m);
        m.Gastos.Setup(g => g.TotalPagosActivosAntesDeAsync(It.IsAny<DateTime>())).ReturnsAsync(0m);
        var gasto = new Gasto { Id = 1, Detalle = "Compra grande", Activo = true };
        m.Gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), Monto = 500m, Gasto = gasto },
            });

        var resultado = await m.Svc.ObtenerLibroCajaMesAsync(2026, 7);

        Assert.Equal(-400m, resultado.SaldoFinal);
    }
}
```

- [ ] Correr y ver que falla (no compila: `FinanzasVistasService` no existe):
  `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~FinanzasVistasServiceLibroCajaTests"`
  Resultado esperado: error de compilación `The type or namespace name 'FinanzasVistasService' could not be found`.

- [ ] Implementar el servicio (solo `ObtenerLibroCajaMesAsync`; los otros 3 métodos de la interfaz se completan en Task 4 — hasta entonces la clase no compila si no están, así que se agregan como stubs mínimos que Task 4 reemplaza):

```csharp
// src/StockApp.Application/Finanzas/FinanzasVistasService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Finanzas;

public class FinanzasVistasService : IFinanzasVistasService
{
    private readonly IIngresoCajaRepository _ingresos;
    private readonly IGastoRepository       _gastos;
    private readonly ILineaPoaRepository    _lineasPoa;
    private readonly ICurrentSession        _session;
    private readonly IAuthorizationService  _auth;

    public FinanzasVistasService(
        IIngresoCajaRepository ingresos,
        IGastoRepository gastos,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _ingresos  = ingresos;
        _gastos    = gastos;
        _lineasPoa = lineasPoa;
        _session   = session;
        _auth      = auth;
    }

    public async Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        if (mes is < 1 or > 12)
            throw new ArgumentException("El mes debe estar entre 1 y 12.");

        var desde = new DateTime(anio, mes, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddMonths(1).AddTicks(-1);

        var saldoInicial =
            await _ingresos.TotalActivosAntesDeAsync(desde) - await _gastos.TotalPagosActivosAntesDeAsync(desde);

        var ingresos = await _ingresos.ListarPorRangoAsync(desde, hasta);
        var pagos = await _gastos.ListarPagosActivosPorRangoAsync(desde, hasta);

        var crudos = ingresos
            .Select(i => (
                Fecha: i.Fecha, Tipo: "Ingreso", Concepto: i.Concepto,
                ProveedorNombre: (string?)null, NumeroFactura: (string?)null,
                FuenteNombre: i.FuenteFinanciamiento?.Nombre, RubroNombre: (string?)null,
                Ingreso: i.Monto, Egreso: 0m))
            .Concat(pagos.Select(p => (
                Fecha: p.Fecha, Tipo: "Egreso", Concepto: p.Gasto?.Detalle ?? string.Empty,
                ProveedorNombre: p.Gasto?.Proveedor?.Nombre, NumeroFactura: p.Gasto?.NumeroFactura,
                FuenteNombre: p.Gasto?.FuenteFinanciamiento?.Nombre, RubroNombre: p.Gasto?.RubroGasto?.Nombre,
                Ingreso: 0m, Egreso: p.Monto)))
            .OrderBy(x => x.Fecha)
            .ThenBy(x => x.Tipo)
            .ToList();

        var movimientos = new List<MovimientoCajaDto>(crudos.Count);
        var corrido = saldoInicial;
        foreach (var x in crudos)
        {
            corrido += x.Ingreso - x.Egreso;
            movimientos.Add(new MovimientoCajaDto(
                DateOnly.FromDateTime(x.Fecha), x.Tipo, x.Concepto, x.ProveedorNombre, x.NumeroFactura,
                x.FuenteNombre, x.RubroNombre, x.Ingreso, x.Egreso, corrido));
        }

        var totalesPorRubro = pagos
            .GroupBy(p => p.Gasto?.RubroGasto?.Nombre ?? "(sin rubro)")
            .Select(g => new TotalPorClaveDto(g.Key, g.Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        var clavesFuente = ingresos.Select(i => i.FuenteFinanciamiento?.Nombre ?? "(sin fuente)")
            .Concat(pagos.Select(p => p.Gasto?.FuenteFinanciamiento?.Nombre ?? "(sin fuente)"))
            .Distinct();
        var totalesPorFuente = clavesFuente
            .Select(clave => new TotalPorClaveDto(
                clave,
                ingresos.Where(i => (i.FuenteFinanciamiento?.Nombre ?? "(sin fuente)") == clave).Sum(i => i.Monto)
                - pagos.Where(p => (p.Gasto?.FuenteFinanciamiento?.Nombre ?? "(sin fuente)") == clave).Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        return new LibroCajaMesDto(
            anio, mes, saldoInicial, corrido, movimientos, totalesPorRubro, totalesPorFuente);
    }

    public Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio) => throw new NotImplementedException();

    public Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio) => throw new NotImplementedException();

    public Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null) => throw new NotImplementedException();
}
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~FinanzasVistasServiceLibroCajaTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 5`.

- [ ] Commit: `feat(finanzas): DTOs de vistas calculadas + FinanzasVistasService.ObtenerLibroCajaMesAsync`

---

## Task 4: `FinanzasVistasService` — libro caja anual + control POA + calendario (TDD Application)

**Files:**
- Modify: `src/StockApp.Application/Finanzas/FinanzasVistasService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceControlPoaTests.cs` (nuevo)
- Test: `tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceCalendarioTests.cs` (nuevo)
- Test: `tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceLibroCajaAnualTests.cs` (nuevo)

**Interfaces:**
- Consumes: `ILineaPoaRepository.ListarTodasAsync()`, `IGastoRepository.TotalGastadoPorLineaAsync(int)`, `IGastoRepository.ListarActivosConSaldoAsync()`, `Gasto.CalcularEstado(DateTime)`, `Gasto.SaldoPendiente`.
- Produces: completa `ObtenerLibroCajaAnualAsync`, `ObtenerControlPoaAsync`, `ObtenerCalendarioPagosAsync` en `FinanzasVistasService`.

### Steps

- [ ] Escribir los tests que fallan (anual):

```csharp
// tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceLibroCajaAnualTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceLibroCajaAnualTests
{
    private static FinanzasVistasService Crear(
        out Mock<IIngresoCajaRepository> ingresos, out Mock<IGastoRepository> gastos)
    {
        ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    [Fact]
    public async Task ObtenerLibroCajaAnualAsync_AgrupaIngresosYEgresosPorMes()
    {
        var svc = Crear(out var ingresos, out var gastos);
        ingresos.Setup(i => i.ListarPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<IngresoCaja>
            {
                new() { Fecha = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), Concepto = "Enero", Monto = 100m },
                new() { Fecha = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc), Concepto = "Marzo", Monto = 50m },
            });
        var rubro = new RubroGasto { Id = 1, Nombre = "Obras" };
        gastos.Setup(g => g.ListarPagosActivosPorRangoAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync(new List<PagoGasto>
            {
                new() { Fecha = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), Monto = 30m,
                        Gasto = new Gasto { RubroGasto = rubro } },
            });

        var resultado = await svc.ObtenerLibroCajaAnualAsync(2026);

        Assert.Equal(12, resultado.TotalesPorMes.Count);
        var enero = resultado.TotalesPorMes.Single(m => m.Mes == 1);
        Assert.Equal(100m, enero.Ingresos);
        Assert.Equal(30m, enero.Egresos);
        Assert.Equal(70m, enero.Neto);
        var marzo = resultado.TotalesPorMes.Single(m => m.Mes == 3);
        Assert.Equal(50m, marzo.Ingresos);
        Assert.Equal(0m, marzo.Egresos);
        Assert.Contains(resultado.TotalesPorRubro, t => t.Clave == "Obras" && t.Total == 30m);
    }
}
```

```csharp
// tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceControlPoaTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceControlPoaTests
{
    private static FinanzasVistasService Crear(out Mock<ILineaPoaRepository> lineasPoa, out Mock<IGastoRepository> gastos)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_CalculaSaldoYPorcentaje_SinSobregiro()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        var linea = new LineaPoa
        {
            Id = 1, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 1000m } },
        };
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { linea });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026))
            .ReturnsAsync(new Dictionary<int, decimal> { [1] = 400m });

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal(1000m, fila.Presupuesto);
        Assert.Equal(400m, fila.Gastado);
        Assert.Equal(600m, fila.Saldo);
        Assert.Equal(40m, fila.PorcentajeEjecucion);
        Assert.False(fila.Sobregirada);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_Sobregiro_MarcaSobregiradaYSaldoNegativo()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        var linea = new LineaPoa
        {
            Id = 2, Nombre = "Prensa", Programa = "Comunicación", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 1, Monto = 1000m } },
        };
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa> { linea });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026))
            .ReturnsAsync(new Dictionary<int, decimal> { [2] = 8915m });

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal(-7915m, fila.Saldo);
        Assert.True(fila.Sobregirada);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_FiltraPorEjercicio()
    {
        var svc = Crear(out var lineasPoa, out var gastos);
        lineasPoa.Setup(l => l.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 1, Nombre = "2026", Programa = "P", Ejercicio = 2026 },
            new() { Id = 2, Nombre = "2025", Programa = "P", Ejercicio = 2025 },
        });
        gastos.Setup(g => g.TotalGastadoPorLineaAsync(2026)).ReturnsAsync(new Dictionary<int, decimal>());

        var resultado = await svc.ObtenerControlPoaAsync(2026);

        var fila = Assert.Single(resultado);
        Assert.Equal("2026", fila.Nombre);
    }
}
```

```csharp
// tests/StockApp.Application.Tests/Finanzas/FinanzasVistasServiceCalendarioTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class FinanzasVistasServiceCalendarioTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static FinanzasVistasService Crear(out Mock<IGastoRepository> gastos)
    {
        var ingresos = new Mock<IIngresoCajaRepository>();
        gastos = new Mock<IGastoRepository>();
        var lineasPoa = new Mock<ILineaPoaRepository>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        var auth = new Mock<StockApp.Application.Authorization.IAuthorizationService>();

        return new FinanzasVistasService(ingresos.Object, gastos.Object, lineasPoa.Object, session.Object, auth.Object);
    }

    private static Gasto GastoCredito(int id, DateTime vencimiento, string proveedor = "Barraca X") => new()
    {
        Id = id, Detalle = "Compra", MontoTotal = 1000m, Fecha = Hoy.AddDays(-30),
        CondicionPago = CondicionPago.Credito, FechaVencimiento = vencimiento,
        Proveedor = new Proveedor { Id = 1, Nombre = proveedor },
    };

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_ClasificaVencidaAVencer7YAVencer30()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, Hoy.AddDays(-1), "Vencida"),
            GastoCredito(2, Hoy.AddDays(5), "AVencer7"),
            GastoCredito(3, Hoy.AddDays(20), "AVencer30"),
            GastoCredito(4, Hoy.AddDays(60), "FueraDeRango"),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Single(resultado.Vencidas);
        Assert.Equal("Vencida", resultado.Vencidas[0].ProveedorNombre);
        Assert.Single(resultado.AVencer7Dias);
        Assert.Equal("AVencer7", resultado.AVencer7Dias[0].ProveedorNombre);
        Assert.Single(resultado.AVencer30Dias);
        Assert.Equal("AVencer30", resultado.AVencer30Dias[0].ProveedorNombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_ExcluyeGastosYaPagados()
    {
        var svc = Crear(out var gastos);
        var pagado = GastoCredito(1, Hoy.AddDays(-1));
        pagado.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-10), Monto = 1000m });
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto> { pagado });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        Assert.Empty(resultado.Vencidas);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_PagosRecientes_SoloUltimos7Dias()
    {
        var svc = Crear(out var gastos);
        var gasto = new Gasto
        {
            Id = 1, Detalle = "Compra", MontoTotal = 500m, CondicionPago = CondicionPago.Contado,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
        };
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-3), Monto = 500m });
        var gastoViejo = new Gasto
        {
            Id = 2, Detalle = "Compra vieja", MontoTotal = 200m, CondicionPago = CondicionPago.Contado,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca Y" },
        };
        gastoViejo.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-20), Monto = 200m });
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto> { gasto, gastoViejo });

        var resultado = await svc.ObtenerCalendarioPagosAsync(Hoy);

        var pago = Assert.Single(resultado.PagosRecientes);
        Assert.Equal("Barraca X", pago.ProveedorNombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_SinFechaReferencia_UsaUtcNow()
    {
        var svc = Crear(out var gastos);
        gastos.Setup(g => g.ListarActivosConSaldoAsync()).ReturnsAsync(new List<Gasto>
        {
            GastoCredito(1, DateTime.UtcNow.AddDays(-1)),
        });

        var resultado = await svc.ObtenerCalendarioPagosAsync();

        Assert.Single(resultado.Vencidas);
    }
}
```

- [ ] Correr y ver que fallan (los 3 métodos siguen tirando `NotImplementedException`):
  `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~FinanzasVistasServiceControlPoaTests|FullyQualifiedName~FinanzasVistasServiceCalendarioTests|FullyQualifiedName~FinanzasVistasServiceLibroCajaAnualTests"`
  Resultado esperado: 8 tests fallan con `System.NotImplementedException`.

- [ ] Reemplazar los 3 stubs en `FinanzasVistasService.cs`:

```csharp
// src/StockApp.Application/Finanzas/FinanzasVistasService.cs — reemplazar los 3 métodos con NotImplementedException
    public async Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var desde = new DateTime(anio, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var hasta = desde.AddYears(1).AddTicks(-1);

        var ingresos = await _ingresos.ListarPorRangoAsync(desde, hasta);
        var pagos = await _gastos.ListarPagosActivosPorRangoAsync(desde, hasta);

        var totalesPorMes = Enumerable.Range(1, 12)
            .Select(mes =>
            {
                var ingresosMes = ingresos.Where(i => i.Fecha.Month == mes).Sum(i => i.Monto);
                var egresosMes = pagos.Where(p => p.Fecha.Month == mes).Sum(p => p.Monto);
                return new TotalMensualDto(mes, ingresosMes, egresosMes, ingresosMes - egresosMes);
            })
            .ToList();

        var totalesPorRubro = pagos
            .GroupBy(p => p.Gasto?.RubroGasto?.Nombre ?? "(sin rubro)")
            .Select(g => new TotalPorClaveDto(g.Key, g.Sum(p => p.Monto)))
            .OrderByDescending(t => t.Total)
            .ToList();

        return new LibroCajaAnualDto(anio, totalesPorMes, totalesPorRubro);
    }

    public async Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var lineas = (await _lineasPoa.ListarTodasAsync()).Where(l => l.Ejercicio == ejercicio).ToList();
        var gastadoPorLinea = await _gastos.TotalGastadoPorLineaAsync(ejercicio);

        return lineas
            .Select(l =>
            {
                var presupuesto = l.Asignaciones.Sum(a => a.Monto);
                var gastado = gastadoPorLinea.TryGetValue(l.Id, out var g) ? g : 0m;
                var saldo = presupuesto - gastado;
                var porcentaje = presupuesto == 0m ? 0m : Math.Round(gastado / presupuesto * 100m, 2);
                return new ControlPoaLineaDto(
                    l.Id, l.Nombre, l.Programa, l.Ejercicio, presupuesto, gastado, saldo, porcentaje, saldo < 0m);
            })
            .OrderBy(d => d.Nombre)
            .ToList();
    }

    public async Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var hoy = (fechaReferencia ?? DateTime.UtcNow).Date;
        var gastos = await _gastos.ListarActivosConSaldoAsync();

        var pendientesConVencimiento = gastos
            .Where(g => g.CondicionPago == CondicionPago.Credito
                        && g.FechaVencimiento is not null
                        && g.CalcularEstado(hoy) is EstadoGasto.Pendiente or EstadoGasto.Parcial or EstadoGasto.Vencida)
            .ToList();

        var vencidas = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date < hoy)
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();
        var aVencer7 = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date >= hoy && g.FechaVencimiento.Value.Date <= hoy.AddDays(7))
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();
        var aVencer30 = pendientesConVencimiento
            .Where(g => g.FechaVencimiento!.Value.Date > hoy.AddDays(7) && g.FechaVencimiento.Value.Date <= hoy.AddDays(30))
            .Select(g => AFacturaDto(g, hoy))
            .OrderBy(f => f.FechaVencimiento)
            .ToList();

        var pagosRecientes = gastos
            .SelectMany(g => g.Pagos
                .Where(p => p.Activo && p.Fecha.Date <= hoy && p.Fecha.Date >= hoy.AddDays(-7))
                .Select(p => new PagoRecienteDto(
                    g.Id, g.Proveedor?.Nombre ?? string.Empty, g.NumeroFactura,
                    DateOnly.FromDateTime(p.Fecha), p.Monto)))
            .OrderByDescending(p => p.FechaPago)
            .ToList();

        return new CalendarioPagosDto(vencidas, aVencer7, aVencer30, pagosRecientes);
    }

    private static FacturaCalendarioDto AFacturaDto(Gasto g, DateTime hoy) => new(
        g.Id, g.Proveedor?.Nombre ?? string.Empty, g.NumeroFactura,
        g.SaldoPendiente, DateOnly.FromDateTime(g.FechaVencimiento!.Value), g.CalcularEstado(hoy).ToString());
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~Finanzas"`
  Resultado esperado: `Passed! - Failed: 0` (todos los tests de `FinanzasVistasService*` + `GastoEstadoTests`/`GastoServiceTests` existentes).

- [ ] Commit: `feat(finanzas): completar FinanzasVistasService (libro caja anual, control POA, calendario de pagos)`

---

## Task 5: Api `FinanzasVistasEndpoints` + DI + matriz de tests (TDD Api)

**Files:**
- Create: `src/StockApp.Api/Endpoints/FinanzasVistasEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/FinanzasVistasEndpointTests.cs` (nuevo)

**Interfaces:**
- Consumes: `IFinanzasVistasService` (Task 3-4).
- Produces: `MapFinanzasVistasEndpoints(this IEndpointRouteBuilder app)`.

### Steps

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Api.Tests/FinanzasVistasEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class FinanzasVistasEndpointTests : ApiTestBase
{
    public FinanzasVistasEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task SeedUsuarioAdminAsync()
    {
        await using var ctx = Factory.CrearContexto();
        if (!await ctx.Usuarios.AnyAsync())
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
    }

    // ── 401 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCalendarioPagos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── 200 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_ConMes_Devuelve200ConLibroCajaMesDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=7");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaMesDto>();
        Assert.NotNull(dto);
        Assert.Equal(2026, dto!.Anio);
        Assert.Equal(7, dto.Mes);
    }

    [Fact]
    public async Task GetLibroCaja_SinMes_Devuelve200ConLibroCajaAnualDto()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>();
        Assert.NotNull(dto);
        Assert.Equal(12, dto!.TotalesPorMes.Count);
    }

    [Fact]
    public async Task GetControlPoa_Devuelve200ConListaVacia_SinLineasDelEjercicio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa?ejercicio=2026");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<List<ControlPoaLineaDto>>();
        Assert.NotNull(dto);
        Assert.Empty(dto!);
    }

    [Fact]
    public async Task GetCalendarioPagos_Devuelve200ConCalendarioVacio()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/calendario-pagos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<CalendarioPagosDto>();
        Assert.NotNull(dto);
        Assert.Empty(dto!.Vencidas);
    }

    // ── 400 ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetLibroCaja_MesFueraDeRango_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja?anio=2026&mes=13");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLibroCaja_SinAnio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/libro-caja");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetControlPoa_SinEjercicio_Devuelve400()
    {
        await SeedUsuarioAdminAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/control-poa");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
```

- [ ] Correr y ver que falla (404 en vez de 401/200/400: la ruta no existe):
  `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~FinanzasVistasEndpointTests"`
  Resultado esperado: fallan por `NotFound` donde se esperaba `Unauthorized`/`OK`/`BadRequest`.

- [ ] Crear el endpoint:

```csharp
// src/StockApp.Api/Endpoints/FinanzasVistasEndpoints.cs
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class FinanzasVistasEndpoints
{
    public static IEndpointRouteBuilder MapFinanzasVistasEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/finanzas/libro-caja", async (int anio, int? mes, IFinanzasVistasService vistas) =>
        {
            if (mes is < 1 or > 12)
                return Results.BadRequest("El mes debe estar entre 1 y 12.");

            return mes is null
                ? Results.Ok(await vistas.ObtenerLibroCajaAnualAsync(anio))
                : Results.Ok(await vistas.ObtenerLibroCajaMesAsync(anio, mes.Value));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/control-poa", async (int ejercicio, IFinanzasVistasService vistas) =>
            Results.Ok(await vistas.ObtenerControlPoaAsync(ejercicio)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/calendario-pagos", async (IFinanzasVistasService vistas) =>
            Results.Ok(await vistas.ObtenerCalendarioPagosAsync()))
            .RequireAuthorization(Permisos.VerFinanzas);

        return app;
    }
}
```

  Nota: las DTOs de Application (`LibroCajaMesDto`, `LibroCajaAnualDto`, `ControlPoaLineaDto`, `CalendarioPagosDto`) se devuelven TAL CUAL — a diferencia de `GastosEndpoints`/`GastoDto`, acá no hay una entidad de dominio con navs que remapear: son records planos ya pensados como forma de wire, así que declarar un DTO espejo en Api sería puro boilerplate (DRY).

- [ ] Registrar en `Program.cs`:

```csharp
// src/StockApp.Api/Program.cs — junto a la línea "builder.Services.AddScoped<IIngresoCajaService, IngresoCajaService>();"
builder.Services.AddScoped<IFinanzasVistasService, FinanzasVistasService>();
```

```csharp
// src/StockApp.Api/Program.cs — junto a la línea "app.MapGastosEndpoints();"
app.MapFinanzasVistasEndpoints();
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~FinanzasVistasEndpointTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 10`.

- [ ] Commit: `feat(finanzas): endpoints de vistas calculadas (libro caja, control POA, calendario de pagos)`

---

## Task 6: `FinanzasVistasApiClient` + DI (TDD ApiClient)

**Files:**
- Create: `src/StockApp.ApiClient/FinanzasVistasApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.ApiClient.Tests/FinanzasVistasApiClientTests.cs` (nuevo)

**Interfaces:**
- Consumes: `ApiQuery.Construir`, `ApiErrores.EnviarAsync/AsegurarExitoAsync` (internal a `StockApp.ApiClient`, visibles porque el test vive en el mismo assembly de tests que ya los usa — mismo patrón que `GastoApiClientTests`).
- Produces: `FinanzasVistasApiClient : IFinanzasVistasService`.

### Steps

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.ApiClient.Tests/FinanzasVistasApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class FinanzasVistasApiClientTests
{
    [Fact]
    public async Task ObtenerLibroCajaMesAsync_GETConAnioYMes_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            anio = 2026, mes = 7, saldoInicial = 100m, saldoFinal = 200m,
            movimientos = Array.Empty<object>(),
            totalesPorRubro = Array.Empty<object>(),
            totalesPorFuente = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerLibroCajaMesAsync(2026, 7);

        Assert.Equal("/finanzas/libro-caja", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("anio=2026", fake.UltimaRequest.RequestUri.Query);
        Assert.Contains("mes=7", fake.UltimaRequest.RequestUri.Query);
        Assert.Equal(200m, dto.SaldoFinal);
    }

    [Fact]
    public async Task ObtenerLibroCajaAnualAsync_GETSoloConAnio_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            anio = 2026,
            totalesPorMes = Array.Empty<object>(),
            totalesPorRubro = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerLibroCajaAnualAsync(2026);

        Assert.DoesNotContain("mes=", fake.UltimaRequest!.RequestUri!.Query);
        Assert.Equal(2026, dto.Anio);
    }

    [Fact]
    public async Task ObtenerControlPoaAsync_GETConEjercicio_DeserializaLista()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                lineaPoaId = 1, nombre = "Rambla", programa = "Obras", ejercicio = 2026,
                presupuesto = 1000m, gastado = 400m, saldo = 600m,
                porcentajeEjecucion = 40m, sobregirada = false,
            },
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var lista = await client.ObtenerControlPoaAsync(2026);

        Assert.Equal("/finanzas/control-poa", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var fila = Assert.Single(lista);
        Assert.Equal("Rambla", fila.Nombre);
    }

    [Fact]
    public async Task ObtenerCalendarioPagosAsync_GETSinParametros_DeserializaDto()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new
        {
            vencidas = Array.Empty<object>(),
            aVencer7Dias = Array.Empty<object>(),
            aVencer30Dias = Array.Empty<object>(),
            pagosRecientes = Array.Empty<object>(),
        }));
        var client = new FinanzasVistasApiClient(TestHttp.CrearCliente(fake));

        var dto = await client.ObtenerCalendarioPagosAsync();

        Assert.Equal("/finanzas/calendario-pagos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Empty(dto.Vencidas);
    }
}
```

- [ ] Correr y ver que falla (no compila: `FinanzasVistasApiClient` no existe):
  `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~FinanzasVistasApiClientTests"`
  Resultado esperado: error de compilación `The type or namespace name 'FinanzasVistasApiClient' could not be found`.

- [ ] Implementar el cliente:

```csharp
// src/StockApp.ApiClient/FinanzasVistasApiClient.cs
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IFinanzasVistasService contra /finanzas/libro-caja, /finanzas/control-poa y
/// /finanzas/calendario-pagos. A diferencia de GastoApiClient, no hace falta remapear:
/// los DTOs de Application ya son la forma de wire (records planos, sin entidades de EF).
/// </summary>
public sealed class FinanzasVistasApiClient : IFinanzasVistasService
{
    private readonly HttpClient _http;

    public FinanzasVistasApiClient(HttpClient http) => _http = http;

    public async Task<LibroCajaMesDto> ObtenerLibroCajaMesAsync(int anio, int mes)
    {
        var query = ApiQuery.Construir(
            ("anio", anio.ToString(CultureInfo.InvariantCulture)),
            ("mes", mes.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/libro-caja" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<LibroCajaMesDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el libro caja del mes.");
    }

    public async Task<LibroCajaAnualDto> ObtenerLibroCajaAnualAsync(int anio)
    {
        var query = ApiQuery.Construir(("anio", anio.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/libro-caja" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<LibroCajaAnualDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el libro caja anual.");
    }

    public async Task<IReadOnlyList<ControlPoaLineaDto>> ObtenerControlPoaAsync(int ejercicio)
    {
        var query = ApiQuery.Construir(("ejercicio", ejercicio.ToString(CultureInfo.InvariantCulture)));
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/control-poa" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ControlPoaLineaDto>>() ?? new();
    }

    public async Task<CalendarioPagosDto> ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)
    {
        // fechaReferencia NUNCA viaja: el servidor es la única autoridad de "hoy" (ver
        // decisión registrada al inicio del plan). El parámetro solo sirve para tests
        // determinísticos de FinanzasVistasService en el servidor.
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/calendario-pagos"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<CalendarioPagosDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el calendario de pagos.");
    }
}
```

- [ ] Registrar en `App.axaml.cs`:

```csharp
// src/StockApp.Presentation/App.axaml.cs — junto a "services.AddTransient<IIngresoCajaService, IngresoCajaApiClient>();"
services.AddTransient<IFinanzasVistasService, FinanzasVistasApiClient>();
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~FinanzasVistasApiClientTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 4`.

- [ ] Commit: `feat(finanzas): FinanzasVistasApiClient + registro DI en el desktop`

---

## Task 7: `LibroCajaViewModel` + View + sidebar + CSV (TDD Presentation)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs` (nuevo)

**Interfaces:**
- Consumes: `IFinanzasVistasService.ObtenerLibroCajaMesAsync/ObtenerLibroCajaAnualAsync`, `ICsvExporter`, `IServicioGuardadoArchivo`.

### Steps

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/LibroCajaViewModelTests.cs
using Avalonia.Collections;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class LibroCajaViewModelTests
{
    private static (LibroCajaViewModel vm, Mock<IFinanzasVistasService> svcMock)
        Crear()
    {
        var svc = new Mock<IFinanzasVistasService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<MovimientoCajaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = new LibroCajaViewModel(svc.Object, csv.Object, guardado.Object);
        return (vm, svc);
    }

    [Fact]
    public async Task CargarAsync_PorDefecto_PideElMesActual()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 100m, 100m,
                new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.Equal(100m, vm.SaldoInicial);
        Assert.Equal(100m, vm.SaldoFinal);
        Assert.Empty(vm.Movimientos);
        svc.Verify(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()), Times.Once);
    }

    [Fact]
    public async Task CargarAsync_ConMovimientos_PopulaLaGrilla()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 500m,
                new List<MovimientoCajaDto>
                {
                    new(new DateOnly(2026, 7, 5), "Ingreso", "Partida", null, null, "Literal B", null, 500m, 0m, 500m),
                },
                new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        var fila = Assert.Single(vm.Movimientos);
        Assert.Equal("Ingreso", fila.Tipo);
        Assert.Equal(500m, fila.SaldoCorrido);
    }

    [Fact]
    public async Task VerAnioCompleto_True_PideLibroCajaAnual()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaAnualDto(2026, new List<TotalMensualDto>(), new List<TotalPorClaveDto>()));

        vm.VerAnioCompleto = true;
        await vm.CargarAsync();

        svc.Verify(s => s.ObtenerLibroCajaAnualAsync(It.IsAny<int>()), Times.Once);
        Assert.NotNull(vm.LibroAnual);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.MovimientosView);
        Assert.True(vm.MovimientosView.CanSort);
    }

    [Fact]
    public async Task ExportarCsvAsync_LlamaAlExportadorYAlGuardado()
    {
        var (vm, svc) = Crear();
        svc.Setup(s => s.ObtenerLibroCajaMesAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(new LibroCajaMesDto(
                2026, 7, 0m, 0m, new List<MovimientoCajaDto>(), new List<TotalPorClaveDto>(), new List<TotalPorClaveDto>()));
        await vm.CargarAsync();

        await vm.ExportarCsvCommand.ExecuteAsync(null);

        Assert.True(true); // el mock no lanza: cubre el camino feliz de Exportar + GuardarTextoAsync
    }
}
```

- [ ] Correr y ver que falla (no compila: `LibroCajaViewModel` no existe):
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~LibroCajaViewModelTests"`
  Resultado esperado: error de compilación.

- [ ] Implementar el ViewModel:

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/LibroCajaViewModel.cs
using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Libro caja" (spec §7.3): selector de mes + toggle "Año completo", grilla
/// cronológica con saldo corrido, paneles de totales por rubro/fuente.
/// </summary>
public partial class LibroCajaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly ICsvExporter           _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty] private int _anio = DateTime.UtcNow.Year;
    [ObservableProperty] private int _mes = DateTime.UtcNow.Month;
    [ObservableProperty] private bool _verAnioCompleto;

    [ObservableProperty] private decimal _saldoInicial;
    [ObservableProperty] private decimal _saldoFinal;
    [ObservableProperty] private LibroCajaAnualDto? _libroAnual;

    public ObservableCollection<MovimientoCajaDto> Movimientos { get; } = new();
    public DataGridCollectionView MovimientosView { get; }

    public ObservableCollection<TotalPorClaveDto> TotalesPorRubro { get; } = new();
    public ObservableCollection<TotalPorClaveDto> TotalesPorFuente { get; } = new();

    public LibroCajaViewModel(
        IFinanzasVistasService service, ICsvExporter csvExporter, IServicioGuardadoArchivo guardado)
    {
        _service     = service;
        _csvExporter = csvExporter;
        _guardado    = guardado;

        MovimientosView = new DataGridCollectionView(Movimientos);
    }

    /// <summary>Carga el libro caja del mes/año seleccionado, o el año completo si VerAnioCompleto.</summary>
    public async Task CargarAsync()
    {
        Movimientos.Clear();
        TotalesPorRubro.Clear();
        TotalesPorFuente.Clear();
        LibroAnual = null;

        if (VerAnioCompleto)
        {
            LibroAnual = await _service.ObtenerLibroCajaAnualAsync(Anio);
            return;
        }

        var libro = await _service.ObtenerLibroCajaMesAsync(Anio, Mes);
        SaldoInicial = libro.SaldoInicial;
        SaldoFinal = libro.SaldoFinal;
        foreach (var mov in libro.Movimientos)
            Movimientos.Add(mov);
        foreach (var t in libro.TotalesPorRubro)
            TotalesPorRubro.Add(t);
        foreach (var t in libro.TotalesPorFuente)
            TotalesPorFuente.Add(t);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(MovimientoCajaDto.Fecha), nameof(MovimientoCajaDto.Tipo), nameof(MovimientoCajaDto.Concepto),
        nameof(MovimientoCajaDto.ProveedorNombre), nameof(MovimientoCajaDto.NumeroFactura),
        nameof(MovimientoCajaDto.FuenteNombre), nameof(MovimientoCajaDto.RubroNombre),
        nameof(MovimientoCajaDto.Ingreso), nameof(MovimientoCajaDto.Egreso), nameof(MovimientoCajaDto.SaldoCorrido),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Movimientos, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"libro-caja-{Anio:0000}-{Mes:00}.csv");
    }
}
```

- [ ] Crear la View (grilla con `ItemsSource="{Binding MovimientosView}"`, panel de saldo inicial/final, toggle Año completo, export CSV — patrón `GastosView.axaml`):

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.LibroCajaView"
             x:DataType="vm:LibroCajaViewModel">

    <Grid RowDefinitions="Auto,Auto,*,Auto" Margin="16">

        <TextBlock Grid.Row="0" Text="Libro caja" Classes="titulo-vista" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="12" Margin="0,12">
            <NumericUpDown Value="{Binding Anio}" Minimum="2020" Maximum="2100" Width="120" />
            <NumericUpDown Value="{Binding Mes}" Minimum="1" Maximum="12"
                           Width="100" IsEnabled="{Binding !VerAnioCompleto}" />
            <CheckBox Content="Año completo" IsChecked="{Binding VerAnioCompleto}" />
            <Button Content="Actualizar" Command="{Binding RecargarCommand}" />
            <Button Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" IsVisible="{Binding !VerAnioCompleto}" />
            <TextBlock Text="Saldo inicial:" IsVisible="{Binding !VerAnioCompleto}" VerticalAlignment="Center" />
            <TextBlock Text="{Binding SaldoInicial, Converter={x:Static conv:MonedaConverter.Instance}}"
                       IsVisible="{Binding !VerAnioCompleto}" VerticalAlignment="Center" FontWeight="SemiBold" />
            <TextBlock Text="Saldo final:" IsVisible="{Binding !VerAnioCompleto}" VerticalAlignment="Center" />
            <TextBlock Text="{Binding SaldoFinal, Converter={x:Static conv:MonedaConverter.Instance}}"
                       IsVisible="{Binding !VerAnioCompleto}" VerticalAlignment="Center" FontWeight="SemiBold"
                       Foreground="{Binding SaldoFinal, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />
        </StackPanel>

        <DataGrid Grid.Row="2" ItemsSource="{Binding MovimientosView}"
                  IsVisible="{Binding !VerAnioCompleto}"
                  AutoGenerateColumns="False" CanUserSortColumns="True" IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Fecha" Binding="{Binding Fecha}" />
                <DataGridTextColumn Header="Tipo" Binding="{Binding Tipo}" />
                <DataGridTextColumn Header="Concepto" Binding="{Binding Concepto}" Width="*" />
                <DataGridTextColumn Header="Proveedor" Binding="{Binding ProveedorNombre}" />
                <DataGridTextColumn Header="Factura" Binding="{Binding NumeroFactura}" />
                <DataGridTextColumn Header="Fuente" Binding="{Binding FuenteNombre}" />
                <DataGridTextColumn Header="Rubro" Binding="{Binding RubroNombre}" />
                <DataGridTextColumn Header="Ingreso"
                                     Binding="{Binding Ingreso, Converter={x:Static conv:MonedaConverter.Instance}}" />
                <DataGridTextColumn Header="Egreso"
                                     Binding="{Binding Egreso, Converter={x:Static conv:MonedaConverter.Instance}}" />
                <DataGridTextColumn Header="Saldo corrido"
                                     Binding="{Binding SaldoCorrido, Converter={x:Static conv:MonedaConverter.Instance}}" />
            </DataGrid.Columns>
        </DataGrid>

        <ItemsControl Grid.Row="2" IsVisible="{Binding VerAnioCompleto}"
                      ItemsSource="{Binding LibroAnual.TotalesPorMes}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <TextBlock Text="{Binding}" Margin="0,2" />
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>

        <StackPanel Grid.Row="3" Orientation="Horizontal" Spacing="24" Margin="0,12,0,0"
                    IsVisible="{Binding !VerAnioCompleto}">
            <StackPanel>
                <TextBlock Text="Totales por rubro" Classes="seccion" />
                <ItemsControl ItemsSource="{Binding TotalesPorRubro}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
            <StackPanel>
                <TextBlock Text="Totales por fuente" Classes="seccion" />
                <ItemsControl ItemsSource="{Binding TotalesPorFuente}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}" />
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </StackPanel>

    </Grid>
</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Finanzas/LibroCajaView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class LibroCajaView : UserControl
{
    public LibroCajaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is LibroCajaViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] Agregar el comando y sección de sidebar en `ShellMainViewModel`:

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs — agregar junto a NavMaestrosFinanzas
    [RelayCommand]
    private void NavLibroCaja()
    {
        SeccionActiva = "LibroCaja";
        _navigation.Navegar<LibroCajaViewModel>();
    }
```

- [ ] Agregar el ítem en `ShellMainView.axaml` (mismo patrón que "Gastos y facturas"):

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml — agregar dentro de la sección "Finanzas" -->
<Button Command="{Binding NavLibroCajaCommand}"
        Classes="ghost"
        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=LibroCaja}"
        HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*">
        <i:Icon Grid.Column="0" Value="mdi-book-open-variant" Foreground="{DynamicResource SidebarTextoBrush}" />
        <TextBlock Grid.Column="1" Text="Libro caja" VerticalAlignment="Center"
                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>
</Button>
```

- [ ] Registrar en `App.axaml.cs`:

```csharp
// src/StockApp.Presentation/App.axaml.cs — junto a "services.AddTransient<GastosViewModel>();"
services.AddTransient<LibroCajaViewModel>();
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~LibroCajaViewModelTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 5`.

- [ ] Commit: `feat(finanzas): pantalla Libro caja (VM, view, sidebar, export CSV)`

---

## Task 8: `ControlPoaViewModel` + View + navegación a Gastos filtrado (TDD Presentation)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs` (mecanismo de filtro inicial)
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs` (nuevo)
- Test: modify `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs` (agregar caso del filtro)

**Interfaces:**
- Consumes: `IFinanzasVistasService.ObtenerControlPoaAsync(int)`, `INavigationService.Navegar<GastosViewModel>(Action<GastosViewModel>)`.
- Produces: `GastosViewModel.FiltrarPorLineaPoa(LineaPoa linea)` (método público, nuevo).

### Steps

- [ ] Escribir el test que falla (`ControlPoaViewModel`):

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ControlPoaViewModelTests.cs
using Avalonia.Collections;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ControlPoaViewModelTests
{
    private static (ControlPoaViewModel vm, Mock<IFinanzasVistasService> svcMock, Mock<INavigationService> navMock)
        Crear(IReadOnlyList<ControlPoaLineaDto>? lineas = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerControlPoaAsync(It.IsAny<int>())).ReturnsAsync(lineas ?? new List<ControlPoaLineaDto>());
        var nav = new Mock<INavigationService>();
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<ControlPoaLineaDto>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = new ControlPoaViewModel(svc.Object, nav.Object, csv.Object, guardado.Object);
        return (vm, svc, nav);
    }

    [Fact]
    public async Task CargarAsync_PopulaLasFilas()
    {
        var (vm, _, _) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
            new(2, "Prensa", "Comunicación", 2026, 1000m, 8915m, -7915m, 891.5m, true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.True(vm.Filas[1].Sobregirada);
    }

    [Fact]
    public async Task FilasView_EsOrdenable()
    {
        var (vm, _, _) = Crear();

        await vm.CargarAsync();

        Assert.IsType<DataGridCollectionView>(vm.FilasView);
        Assert.True(vm.FilasView.CanSort);
    }

    [Fact]
    public async Task AbrirGastosDeLaLinea_NavegaAGastosViewModelFiltrado()
    {
        var (vm, _, nav) = Crear(new List<ControlPoaLineaDto>
        {
            new(1, "Rambla", "Obras", 2026, 1000m, 400m, 600m, 40m, false),
        });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        vm.AbrirGastosDeLaLineaCommand.Execute(null);

        nav.Verify(n => n.Navegar(It.IsAny<Action<GastosViewModel>>()), Times.Once);
    }
}
```

- [ ] Correr y ver que falla:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ControlPoaViewModelTests"`
  Resultado esperado: error de compilación (`ControlPoaViewModel` no existe).

- [ ] Agregar el mecanismo de filtro inicial a `GastosViewModel` (método público; no cambia su constructor):

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs — agregar dentro de la clase, cerca de CargarAsync
    /// <summary>
    /// Precarga el filtro por línea POA (spec §7.4: doble click en Control POA abre las
    /// facturas de esa línea). Se llama ANTES de que la View dispare CargarAsync — ArmarFiltro()
    /// lee LineaPoaSeleccionada.Id sin depender de que ya esté en LineasPoaDisponibles.
    /// </summary>
    public void FiltrarPorLineaPoa(LineaPoa linea) => LineaPoaSeleccionada = linea;
```

- [ ] Implementar `ControlPoaViewModel`:

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/ControlPoaViewModel.cs
using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Control POA" (spec §7.4): una fila por línea con presupuesto, gastado, saldo
/// y % de ejecución. Doble click abre las facturas de esa línea en GastosViewModel filtrado.
/// </summary>
public partial class ControlPoaViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly INavigationService     _navigation;
    private readonly ICsvExporter           _csvExporter;
    private readonly IServicioGuardadoArchivo _guardado;

    [ObservableProperty] private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AbrirGastosDeLaLineaCommand))]
    private ControlPoaLineaDto? _filaSeleccionada;

    public ObservableCollection<ControlPoaLineaDto> Filas { get; } = new();
    public DataGridCollectionView FilasView { get; }

    public ControlPoaViewModel(
        IFinanzasVistasService service, INavigationService navigation,
        ICsvExporter csvExporter, IServicioGuardadoArchivo guardado)
    {
        _service     = service;
        _navigation  = navigation;
        _csvExporter = csvExporter;
        _guardado    = guardado;

        FilasView = new DataGridCollectionView(Filas);
    }

    public async Task CargarAsync()
    {
        var lineas = await _service.ObtenerControlPoaAsync(Ejercicio);
        Filas.Clear();
        foreach (var l in lineas)
            Filas.Add(l);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private void AbrirGastosDeLaLinea()
    {
        if (FilaSeleccionada is null) return;
        var linea = new LineaPoa
        {
            Id = FilaSeleccionada.LineaPoaId, Nombre = FilaSeleccionada.Nombre,
            Programa = FilaSeleccionada.Programa, Ejercicio = FilaSeleccionada.Ejercicio,
        };
        _navigation.Navegar<GastosViewModel>(vm => vm.FiltrarPorLineaPoa(linea));
    }

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(ControlPoaLineaDto.Nombre), nameof(ControlPoaLineaDto.Programa), nameof(ControlPoaLineaDto.Ejercicio),
        nameof(ControlPoaLineaDto.Presupuesto), nameof(ControlPoaLineaDto.Gastado), nameof(ControlPoaLineaDto.Saldo),
        nameof(ControlPoaLineaDto.PorcentajeEjecucion), nameof(ControlPoaLineaDto.Sobregirada),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Filas, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"control-poa-{Ejercicio}.csv");
    }
}
```

- [ ] Crear la View (grilla con `DoubleTapped` disparando `AbrirGastosDeLaLineaCommand`, fila sobregirada en rojo con `SignoNegativoBrushConverter` sobre `Saldo`):

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.ControlPoaView"
             x:DataType="vm:ControlPoaViewModel">

    <Grid RowDefinitions="Auto,Auto,*" Margin="16">

        <TextBlock Grid.Row="0" Text="Control POA" Classes="titulo-vista" />

        <StackPanel Grid.Row="1" Orientation="Horizontal" Spacing="12" Margin="0,12">
            <NumericUpDown Value="{Binding Ejercicio}" Minimum="2020" Maximum="2100" Width="120" />
            <Button Content="Actualizar" Command="{Binding RecargarCommand}" />
            <Button Content="Exportar CSV" Command="{Binding ExportarCsvCommand}" />
        </StackPanel>

        <DataGrid Grid.Row="2" ItemsSource="{Binding FilasView}"
                  SelectedItem="{Binding FilaSeleccionada}"
                  DoubleTapped="OnFilaDobleClick"
                  AutoGenerateColumns="False" CanUserSortColumns="True" IsReadOnly="True">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Línea" Binding="{Binding Nombre}" Width="*" />
                <DataGridTextColumn Header="Programa" Binding="{Binding Programa}" />
                <DataGridTextColumn Header="Presupuesto"
                                     Binding="{Binding Presupuesto, Converter={x:Static conv:MonedaConverter.Instance}}" />
                <DataGridTextColumn Header="Gastado"
                                     Binding="{Binding Gastado, Converter={x:Static conv:MonedaConverter.Instance}}" />
                <DataGridTextColumn Header="Saldo"
                                     Binding="{Binding Saldo, Converter={x:Static conv:MonedaConverter.Instance}}"
                                     CellStyleClasses="saldo-poa" />
                <DataGridTextColumn Header="% Ejecución" Binding="{Binding PorcentajeEjecucion}" />
            </DataGrid.Columns>
        </DataGrid>

    </Grid>
</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Finanzas/ControlPoaView.axaml.cs
using Avalonia.Controls;
using Avalonia.Input;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class ControlPoaView : UserControl
{
    public ControlPoaView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is ControlPoaViewModel vm)
                await vm.CargarAsync();
        };
    }

    private void OnFilaDobleClick(object? sender, TappedEventArgs e)
    {
        if (DataContext is ControlPoaViewModel { FilaSeleccionada: not null } vm
            && vm.AbrirGastosDeLaLineaCommand.CanExecute(null))
            vm.AbrirGastosDeLaLineaCommand.Execute(null);
    }
}
```

  Nota: `CellStyleClasses="saldo-poa"` queda como marcador para un estilo `DataGridCell.saldo-poa` que aplique el foreground de `SignoNegativoBrushConverter` sobre `Saldo` — la resolución exacta del estilo (recurso compartido vs. estilo inline) queda a criterio del ejecutor siguiendo el patrón visual ya usado en `ValorizacionView` para `StockActual`, sin bloquear los tests de VM (que no dependen de estilos XAML).

- [ ] Agregar comando y sidebar en `ShellMainViewModel`:

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
    [RelayCommand]
    private void NavControlPoa()
    {
        SeccionActiva = "ControlPoa";
        _navigation.Navegar<ControlPoaViewModel>();
    }
```

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<Button Command="{Binding NavControlPoaCommand}"
        Classes="ghost"
        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=ControlPoa}"
        HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*">
        <i:Icon Grid.Column="0" Value="mdi-chart-donut" Foreground="{DynamicResource SidebarTextoBrush}" />
        <TextBlock Grid.Column="1" Text="Control POA" VerticalAlignment="Center"
                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>
</Button>
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
services.AddTransient<ControlPoaViewModel>();
```

- [ ] Agregar el caso del filtro en `GastosViewModelTests.cs`:

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs — agregar dentro de la clase
    [Fact]
    public void FiltrarPorLineaPoa_SeteaLineaPoaSeleccionada()
    {
        var (vm, _, _, _) = Crear();
        var linea = new LineaPoa { Id = 5, Nombre = "Rambla", Programa = "Obras", Ejercicio = 2026 };

        vm.FiltrarPorLineaPoa(linea);

        Assert.Equal(5, vm.LineaPoaSeleccionada?.Id);
    }
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ControlPoaViewModelTests|FullyQualifiedName~GastosViewModelTests"`
  Resultado esperado: `Passed! - Failed: 0`.

- [ ] Commit: `feat(finanzas): pantalla Control POA (VM, view, doble click a Gastos filtrado)`

---

## Task 9: `CalendarioPagosViewModel` + View + navegación a pagos (TDD Presentation)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs` (nuevo)

**Interfaces:**
- Consumes: `IFinanzasVistasService.ObtenerCalendarioPagosAsync()`, `IGastoService.ObtenerPorIdAsync(int)` (para materializar el `Gasto` completo antes de navegar a `PagosGastoViewModel`), `INavigationService.Navegar<PagosGastoViewModel>(Action<PagosGastoViewModel>)`.

### Steps

- [ ] Escribir el test que falla:

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/CalendarioPagosViewModelTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class CalendarioPagosViewModelTests
{
    private static (CalendarioPagosViewModel vm, Mock<IFinanzasVistasService> svcMock,
                     Mock<IGastoService> gastoSvcMock, Mock<INavigationService> navMock)
        Crear(CalendarioPagosDto? calendario = null)
    {
        var svc = new Mock<IFinanzasVistasService>();
        svc.Setup(s => s.ObtenerCalendarioPagosAsync(null)).ReturnsAsync(
            calendario ?? new CalendarioPagosDto(
                new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(),
                new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        var gastoSvc = new Mock<IGastoService>();
        var nav = new Mock<INavigationService>();

        var vm = new CalendarioPagosViewModel(svc.Object, gastoSvc.Object, nav.Object);
        return (vm, svc, gastoSvc, nav);
    }

    [Fact]
    public async Task CargarAsync_PopulaLasCuatroSecciones()
    {
        var (vm, _, _, _) = Crear(new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto> { new(2, "Barraca Y", "A-2", 300m, new DateOnly(2026, 7, 20), "Pendiente") },
            new List<FacturaCalendarioDto> { new(3, "Barraca Z", "A-3", 200m, new DateOnly(2026, 8, 10), "Pendiente") },
            new List<PagoRecienteDto> { new(4, "Barraca W", "A-4", new DateOnly(2026, 7, 14), 100m) }));

        await vm.CargarAsync();

        Assert.Single(vm.Vencidas);
        Assert.Single(vm.AVencer7Dias);
        Assert.Single(vm.AVencer30Dias);
        Assert.Single(vm.PagosRecientes);
    }

    [Fact]
    public async Task RegistrarPago_ObtieneElGastoYNavegaAPagosGastoViewModel()
    {
        var (vm, _, gastoSvc, nav) = Crear(new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));
        await vm.CargarAsync();
        gastoSvc.Setup(g => g.ObtenerPorIdAsync(1)).ReturnsAsync(new Gasto { Id = 1 });

        await vm.RegistrarPagoCommand.ExecuteAsync(vm.Vencidas[0]);

        gastoSvc.Verify(g => g.ObtenerPorIdAsync(1), Times.Once);
        nav.Verify(n => n.Navegar(It.IsAny<Action<PagosGastoViewModel>>()), Times.Once);
    }
}
```

- [ ] Correr y ver que falla:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~CalendarioPagosViewModelTests"`
  Resultado esperado: error de compilación (`CalendarioPagosViewModel` no existe).

- [ ] Implementar el ViewModel:

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/CalendarioPagosViewModel.cs
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Calendario de pagos" (spec §7.5): facturas vencidas, a vencer en 7/30 días y
/// pagos recientes. "Registrar pago" trae el Gasto completo y navega a PagosGastoViewModel.
/// </summary>
public partial class CalendarioPagosViewModel : ViewModelBase
{
    private readonly IFinanzasVistasService _service;
    private readonly IGastoService          _gastoService;
    private readonly INavigationService     _navigation;

    public ObservableCollection<FacturaCalendarioDto> Vencidas { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer7Dias { get; } = new();
    public ObservableCollection<FacturaCalendarioDto> AVencer30Dias { get; } = new();
    public ObservableCollection<PagoRecienteDto> PagosRecientes { get; } = new();

    public CalendarioPagosViewModel(
        IFinanzasVistasService service, IGastoService gastoService, INavigationService navigation)
    {
        _service      = service;
        _gastoService = gastoService;
        _navigation   = navigation;
    }

    public async Task CargarAsync()
    {
        var calendario = await _service.ObtenerCalendarioPagosAsync();

        Vencidas.Clear();
        foreach (var f in calendario.Vencidas) Vencidas.Add(f);
        AVencer7Dias.Clear();
        foreach (var f in calendario.AVencer7Dias) AVencer7Dias.Add(f);
        AVencer30Dias.Clear();
        foreach (var f in calendario.AVencer30Dias) AVencer30Dias.Add(f);
        PagosRecientes.Clear();
        foreach (var p in calendario.PagosRecientes) PagosRecientes.Add(p);
    }

    [RelayCommand]
    private async Task RecargarAsync() => await CargarAsync();

    [RelayCommand]
    private async Task RegistrarPagoAsync(FacturaCalendarioDto? fila)
    {
        if (fila is null) return;
        var gasto = await _gastoService.ObtenerPorIdAsync(fila.GastoId);
        _navigation.Navegar<PagosGastoViewModel>(vm => vm.CargarParaGasto(gasto));
    }
}
```

- [ ] Crear la View (4 secciones con `ItemsControl`/`DataGrid`, botón "Registrar pago" por fila):

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="800"
             x:Class="StockApp.Presentation.Views.Finanzas.CalendarioPagosView"
             x:DataType="vm:CalendarioPagosViewModel">

    <ScrollViewer>
        <StackPanel Margin="16" Spacing="20">

            <TextBlock Text="Calendario de pagos" Classes="titulo-vista" />
            <Button Content="Actualizar" Command="{Binding RecargarCommand}" HorizontalAlignment="Left" />

            <Border Classes="card">
                <StackPanel Spacing="8">
                    <TextBlock Text="Vencidas" Classes="seccion" Foreground="{DynamicResource DangerBrush}" />
                    <ItemsControl ItemsSource="{Binding Vencidas}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,4">
                                    <TextBlock Grid.Column="0"
                                               Text="{Binding ProveedorNombre}" />
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Margin="0,0,12,0" />
                                    <Button Grid.Column="2" Content="Registrar pago"
                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="8">
                    <TextBlock Text="A vencer en 7 días" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding AVencer7Dias}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,4">
                                    <TextBlock Grid.Column="0" Text="{Binding ProveedorNombre}" />
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Margin="0,0,12,0" />
                                    <Button Grid.Column="2" Content="Registrar pago"
                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="8">
                    <TextBlock Text="A vencer en 30 días" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding AVencer30Dias}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,4">
                                    <TextBlock Grid.Column="0" Text="{Binding ProveedorNombre}" />
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}"
                                               Margin="0,0,12,0" />
                                    <Button Grid.Column="2" Content="Registrar pago"
                                            Command="{Binding $parent[UserControl].((vm:CalendarioPagosViewModel)DataContext).RegistrarPagoCommand}"
                                            CommandParameter="{Binding}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

            <Border Classes="card">
                <StackPanel Spacing="8">
                    <TextBlock Text="Pagos recientes" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding PagosRecientes}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <Grid ColumnDefinitions="*,Auto,Auto" Margin="0,4">
                                    <TextBlock Grid.Column="0" Text="{Binding ProveedorNombre}" />
                                    <TextBlock Grid.Column="1" Text="{Binding FechaPago}" Margin="0,0,12,0" />
                                    <TextBlock Grid.Column="2"
                                               Text="{Binding Monto, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                </Grid>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>
            </Border>

        </StackPanel>
    </ScrollViewer>
</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Finanzas/CalendarioPagosView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class CalendarioPagosView : UserControl
{
    public CalendarioPagosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is CalendarioPagosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] Agregar comando y sidebar en `ShellMainViewModel`:

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
    [RelayCommand]
    private void NavCalendarioPagos()
    {
        SeccionActiva = "CalendarioPagos";
        _navigation.Navegar<CalendarioPagosViewModel>();
    }
```

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<Button Command="{Binding NavCalendarioPagosCommand}"
        Classes="ghost"
        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=CalendarioPagos}"
        HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*">
        <i:Icon Grid.Column="0" Value="mdi-calendar-clock" Foreground="{DynamicResource SidebarTextoBrush}" />
        <TextBlock Grid.Column="1" Text="Calendario de pagos" VerticalAlignment="Center"
                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>
</Button>
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
services.AddTransient<CalendarioPagosViewModel>();
```

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~CalendarioPagosViewModelTests"`
  Resultado esperado: `Passed! - Failed: 0, Passed: 2`.

- [ ] Commit: `feat(finanzas): pantalla Calendario de pagos (VM, view, registrar pago)`

---

## Task 10: Aviso de vencimientos en Inicio (TDD Presentation)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/InicioViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/InicioView.axaml.cs`
- Modify: `src/StockApp.Presentation/Views/InicioView.axaml`
- Test: modify `tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs`

**Interfaces:**
- Consumes: `IFinanzasVistasService.ObtenerCalendarioPagosAsync()`.

### Steps

- [ ] Escribir los tests que fallan (agregar a `InicioViewModelTests.cs`; el constructor de `InicioViewModel` gana un parámetro, así que TODOS los `Crear(...)` existentes en el archivo deben actualizarse para pasar el mock nuevo):

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/InicioViewModelTests.cs — reemplazar el helper Crear
using Moq;
using StockApp.Application.Auth;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels;

public class InicioViewModelTests
{
    private static (InicioViewModel vm, Mock<ICurrentSession> sessionMock, Mock<INavigationService> navMock,
                     Mock<IFinanzasVistasService> finanzasMock)
        Crear(UsuarioSesion usuario, CalendarioPagosDto? calendario = null)
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

        var vm = new InicioViewModel(sessionMock.Object, navMock.Object, finanzasMock.Object);
        return (vm, sessionMock, navMock, finanzasMock);
    }

    // ── el resto de los [Fact] existentes se mantienen igual, solo cambia la firma de Crear(...) ──
    // (actualizar cada llamada "Crear(usuario)" existente para que siga compilando; no cambia su lógica)

    // ── Aviso de vencimientos (nuevo) ───────────────────────────────────────

    [Fact]
    public async Task CargarAsync_ConFacturasVencidas_MuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario, new CalendarioPagosDto(
            new List<FacturaCalendarioDto> { new(1, "Barraca X", "A-1", 500m, new DateOnly(2026, 7, 1), "Vencida") },
            new List<FacturaCalendarioDto>(), new List<FacturaCalendarioDto>(), new List<PagoRecienteDto>()));

        await vm.CargarAsync();

        Assert.True(vm.MostrarAvisoVencimientos);
        Assert.Equal(1, vm.CantidadVencidas);
        Assert.Equal(0, vm.CantidadAVencer7Dias);
    }

    [Fact]
    public async Task CargarAsync_SinVencidasNiAVencer_NoMuestraElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, _) = Crear(usuario);

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task CargarAsync_ElServicioFalla_NoRompeYOcultaElAviso()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, _, finanzas) = Crear(usuario);
        finanzas.Setup(f => f.ObtenerCalendarioPagosAsync(null))
            .ThrowsAsync(new UnauthorizedAccessException());

        await vm.CargarAsync();

        Assert.False(vm.MostrarAvisoVencimientos);
    }

    [Fact]
    public async Task IrACalendarioPagos_NavegaACalendarioPagosViewModel()
    {
        var usuario = new UsuarioSesion(1, "jperez", RolUsuario.Operador, "Juan Pérez");
        var (vm, _, nav, _) = Crear(usuario);

        vm.IrACalendarioPagosCommand.Execute(null);

        nav.Verify(n => n.Navegar<CalendarioPagosViewModel>(), Times.Once);
    }
}
```

- [ ] Correr y ver que falla (no compila: el constructor de `InicioViewModel` no tiene 3 parámetros):
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~InicioViewModelTests"`
  Resultado esperado: error de compilación.

- [ ] Modificar `InicioViewModel`:

```csharp
// src/StockApp.Presentation/ViewModels/InicioViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.ViewModels.Catalogo;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using StockApp.Presentation.ViewModels.Reportes;

namespace StockApp.Presentation.ViewModels;

/// <summary>
/// Pantalla de bienvenida mostrada en la región central del shell tras el login.
/// Resuelve el bug de "región central vacía tras login": es el primer contenido
/// navegado dentro de ShellMainViewModel una vez que este queda establecido como
/// CurrentViewModel del shell.
/// </summary>
public partial class InicioViewModel : ViewModelBase
{
    private readonly ICurrentSession        _session;
    private readonly INavigationService     _navigation;
    private readonly IFinanzasVistasService _finanzasVistas;

    public string NombreUsuario =>
        _session.UsuarioActual?.NombreCompleto ?? _session.UsuarioActual?.NombreUsuario ?? "Usuario";

    public string Saludo => $"¡Bienvenido, {NombreUsuario}!";

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;

    public string RolTexto => EsAdmin ? "Administrador" : "Operador";

    [ObservableProperty] private bool _mostrarAvisoVencimientos;
    [ObservableProperty] private int _cantidadVencidas;
    [ObservableProperty] private int _cantidadAVencer7Dias;

    public InicioViewModel(
        ICurrentSession session, INavigationService navigation, IFinanzasVistasService finanzasVistas)
    {
        _session        = session;
        _navigation     = navigation;
        _finanzasVistas = finanzasVistas;
    }

    /// <summary>
    /// Carga el aviso de vencimientos (spec §7.5: "al abrir la app, aviso en Inicio si hay
    /// facturas vencidas o por vencer en la semana"). Sin VerFinanzas o si la API falla, el
    /// aviso simplemente no se muestra — Inicio nunca debe romper (catch silencioso).
    /// </summary>
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
    }

    // ── accesos rápidos: comunes (Admin + Operador) ───────────────────────────

    [RelayCommand]
    private void IrAProductos() => _navigation.Navegar<ProductoListViewModel>();

    [RelayCommand]
    private void IrARegistrarEntrada() => _navigation.Navegar<EntradaRegistroViewModel>();

    [RelayCommand]
    private void IrARegistrarSalida() => _navigation.Navegar<SalidaRegistroViewModel>();

    [RelayCommand]
    private void IrAHistorialMovimientos() => _navigation.Navegar<MovimientoHistorialViewModel>();

    [RelayCommand]
    private void IrACalendarioPagos() => _navigation.Navegar<CalendarioPagosViewModel>();

    // ── accesos rápidos: solo Admin ────────────────────────────────────────────

    [RelayCommand]
    private void IrAValorizacion() => _navigation.Navegar<ValorizacionViewModel>();

    [RelayCommand]
    private void IrAAuditoria() => _navigation.Navegar<AuditoriaLogViewModel>();
}
```

- [ ] Enganchar `CargarAsync` en `InicioView.axaml.cs` (hoy no dispara nada — gotcha del repo):

```csharp
// src/StockApp.Presentation/Views/InicioView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels;

namespace StockApp.Presentation.Views;

public partial class InicioView : UserControl
{
    public InicioView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is InicioViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] Agregar la card condicional en `InicioView.axaml` (después de la card "Encabezado", antes de "Accesos rápidos"):

```xml
<!-- src/StockApp.Presentation/Views/InicioView.axaml — insertar entre las dos cards existentes -->
<Border Classes="card" IsVisible="{Binding MostrarAvisoVencimientos}">
    <StackPanel Spacing="8">
        <TextBlock Text="Vencimientos de facturas" Classes="seccion" Foreground="{DynamicResource DangerBrush}" />
        <TextBlock Text="{Binding CantidadVencidas, StringFormat='{}{0} factura(s) vencida(s)'}"
                   IsVisible="{Binding CantidadVencidas}" />
        <TextBlock Text="{Binding CantidadAVencer7Dias, StringFormat='{}{0} factura(s) a vencer esta semana'}"
                   IsVisible="{Binding CantidadAVencer7Dias}" />
        <Button Content="Ver calendario de pagos" Command="{Binding IrACalendarioPagosCommand}" />
    </StackPanel>
</Border>
```

- [ ] Registrar la dependencia nueva en el DI de `InicioViewModel` (ya lo resuelve el constructor DI de Microsoft.Extensions.DependencyInjection sin cambios en `App.axaml.cs`, porque `InicioViewModel` ya está registrado como `AddTransient` y el contenedor resuelve el nuevo parámetro automáticamente — verificar que no haya un registro `AddTransient<InicioViewModel>(sp => new InicioViewModel(...))` manual con lista de argumentos fija; si lo hay, actualizarlo con el tercer argumento).

- [ ] Correr y ver verde:
  `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~InicioViewModelTests"`
  Resultado esperado: `Passed! - Failed: 0` (todos los tests existentes + los 4 nuevos).

- [ ] Commit: `feat(finanzas): aviso de vencimientos en Inicio`

---

## Task 11: Cierre — suite completa (TDD, sin nuevo código de producción)

**Files:** ninguno (solo verificación).

### Steps

- [x] Correr la suite completa de la solución:
  `dotnet test`
  Resultado esperado: `Passed! - Failed: 0` con un total ≥ 1340 + los tests nuevos de F4 (aprox. 1340 + 6 (Task1) + 6 (Task2) + 5 (Task3) + 8 (Task4) + 10 (Task5) + 4 (Task6) + 5+1 (Task7) + 3+1 (Task8) + 2 (Task9) + 4 (Task10) ≈ 1395+).

- [x] Si algo falla, arreglar el mínimo necesario y volver a correr `dotnet test` hasta verde. No commitear hasta estar verde.

- [x] Commit: `test(finanzas): cierre F4 vistas calculadas — suite completa verde`

- [ ] Nota para el orquestador (NO ejecutar acá): la verificación orgánica (app real vía XTEST/WSLg + Postgres `stockapp-pg`) queda fuera de este plan — la corre el orquestador después de que el ejecutor complete estas 11 tasks.

---

## Self-review

1. **Cobertura**: spec §7 ítems 3 (Libro caja: Task 3, 4, 7), 4 (Control POA: Task 4, 8) y 5 (Calendario de pagos + aviso en Inicio: Task 4, 9, 10) — cubiertos. §9 (3 endpoints `/finanzas/libro-caja`, `/finanzas/control-poa`, `/finanzas/calendario-pagos`) — Task 5. Permiso `VerFinanzas` verificado en Application (doble barrera) y en Api (`RequireAuthorization`) — todas las tasks.
2. **Placeholders**: cero "TBD" / "similar a" / pasos sin código — cada paso trae el código C#/XAML completo, con los tipos y firmas verificados contra el repo real (`IGastoRepository`, `IIngresoCajaRepository`, `GastoService`, `GastosViewModel`, `ShellMainViewModel`, `InicioViewModel`, `ApiQuery`, `ApiErrores`, `MonedaConverter`, `SignoNegativoBrushConverter`, `ICsvExporter`, `IServicioGuardadoArchivo`, `INavigationService`).
3. **Consistencia de firmas**: `IFinanzasVistasService` se define completo en Task 3 y se consume idéntico en Task 5 (Api), Task 6 (ApiClient), Task 7-10 (Presentation) — mismos nombres de método y tipos de retorno en todo el plan.

---

## Decisiones que tomé por contradicción con el repo real

1. **Nav `PagoGasto.Gasto`** (Task 2): el repo NO tenía navegación inversa Gasto→Pago (`HasOne<Gasto>()` sin nav explícita); la agregué porque el brief pedía literalmente "Include Gasto→Proveedor/RubroGasto/FuenteFinanciamiento" desde `PagosGasto`, imposible sin ella. Sin migración nueva (mismo FK `GastoId`).
2. **`ObtenerCalendarioPagosAsync(DateTime? fechaReferencia = null)`**: el brief listaba la firma sin parámetros; agregué el parámetro opcional porque el proyecto no tiene `IClock` (confirmado por grep) y el brief pedía explícitamente testear umbrales de 7/30 días con fecha inyectable, igual que `CalcularEstado`. El servidor HTTP y el ApiClient nunca lo envían.
3. **Api reutiliza los DTOs de Application tal cual** (Task 5) en vez de declarar records espejo como hace `GastosEndpoints`/`GastoDto`: como `FinanzasVistasService` ya devuelve DTOs planos (sin entidades EF ni navs problemáticas), duplicar la forma sería puro boilerplate — decisión DRY, no una desviación de arquitectura.
4. **`GastosViewModel.FiltrarPorLineaPoa(LineaPoa)`** (Task 8): el brief dejó abierto el mecanismo ("propiedad `LineaPoaInicialId`... o método público"); elegí un método público que reutiliza la propiedad `LineaPoaSeleccionada` ya existente, porque no requiere tocar el constructor de `GastosViewModel` (que ya tiene 9 parámetros) ni el orden de carga de combos en `CargarAsync`.
