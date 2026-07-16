# Módulo Finanzas — Fase 2: Gastos, pagos e ingresos de caja — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Registrar los documentos del módulo Finanzas (`Gasto` con sus `PagoGasto` e `IngresoCaja`) end-to-end: entidades + migración (incluida la columna `GastoId` nullable en `MovimientoStock`), repositorios, servicios con reglas de negocio (estado de factura CALCULADO, sobregiro POA que advierte sin bloquear, crédito con vencimiento, pagos que no superan el saldo, anulación solo sin pagos activos), endpoints `/finanzas/gastos` y `/finanzas/ingresos`, ApiClients, y las pantallas "Gastos y facturas" e "Ingresos de caja" del desktop + el paso OPCIONAL de "Asociar factura" en la entrada de stock — según el spec `docs/superpowers/specs/2026-07-15-modulo-finanzas-design.md` (§4 documentos, §5 vínculo stock, §9 permisos, §10 reglas). Fuera de alcance de esta fase: adjuntos (F3), vistas calculadas Libro caja / Control POA / Calendario (F4), importador (F5).

**Architecture:** El mismo slice vertical canónico de Fase 1 (entidad → repo EF → servicio Application con autorización+reglas+auditoría → Minimal API con policy → ApiClient que implementa la MISMA interfaz `IXxxService` → ViewModels/Views Avalonia), con dos piezas nuevas de diseño: (1) `Gasto` es un agregado con hijas `PagoGasto` (baja lógica propia, a diferencia de `AsignacionPresupuestal`) y su **estado** (`Pendiente`/`Parcial`/`Pagada`/`Vencida`/`Anulada`) es un cálculo de dominio sobre `sum(pagos activos)` + `FechaVencimiento` + `Activo` — NUNCA una columna; (2) el vínculo con stock es la FK opcional `MovimientoStock.GastoId`, que asigna el `GastoService` al crear/asociar la factura — el flujo de registro de movimientos NO cambia.

**Tech Stack:** .NET 10, Clean Architecture (Domain / Application / Infrastructure / Api / ApiClient / Presentation), ASP.NET Core Minimal API + JWT + policies derivadas de `Permisos.Todos`, EF Core + Npgsql (PostgreSQL), xUnit + Moq + Testcontainers (Api/Infrastructure contra Postgres real), Avalonia 12 + CommunityToolkit.Mvvm (desktop).

## Global Constraints

Convenciones REALES del repo — verificadas contra el código mergeado de Fase 1:

- **Español en todo**: entidades, servicios, métodos, mensajes de error, comentarios y commits.
- **Cultura FIJA es-UY para montos en el desktop**: todo parseo/formateo de montos desde texto usa el patrón de `LineaPoaFormViewModel.CulturaMonto` (con fallback manual `,`/`.` si "es-UY" no existe en el runtime). La app NO fija cultura global — parsear con la cultura ambiente es un bug ("1500.50" puede leerse 150050).
- **Estado de factura CALCULADO, nunca persistido**: `Gasto.CalcularEstado(fechaReferencia)` + `TotalPagado`/`SaldoPendiente` derivados de los pagos activos. NO existe columna Estado en la BD ni en la migración.
- **Baja lógica siempre** (`Activo = false`): `Gasto` se ANULA (regla: sin pagos activos; al anular se desvinculan sus movimientos de stock), `PagoGasto` se anula individualmente, `IngresoCaja` baja lógica clásica. Nunca DELETE físico.
- **decimal 18,4** para montos (`HasPrecision(18, 4)`); **fechas UTC** en BD (el desktop convierte fecha elegida → medianoche UTC, patrón de MovimientoHistorial).
- **Auditoría append-only**: los valores nuevos de `AccionAuditada` arrancan en **31** (el último real en `src/StockApp.Domain/Enums/AccionAuditada.cs` es `BajaLineaPoa = 30`) y van SIEMPRE al final del enum.
- **Excepciones de dominio**: `ArgumentException` (→ 400), `ReglaDeNegocioException` (→ 409), `EntidadNoEncontradaException` (→ 404). El `DomainExceptionHandler` de la API ya las mapea; los endpoints NO hacen try/catch.
- **Doble barrera de autorización**: policy HTTP en el endpoint (derivada automáticamente de `Permisos.Todos` + `AuthorizationService.TienePermiso`) + `_auth.Verificar(...)` dentro del servicio.
- **Permisos de esta fase** (spec §9): `RegistrarGastos`, `RegistrarPagos`, `RegistrarIngresos` — otorgados a **Admin Y Operador** (se agregan a `AccionesOperador`). `VerFinanzas` (ya existe) protege los listados. Consecuencia para los tests de API: NO hay caso 403 por rol — la matriz es 401 / 200-201 / 400 / 404 / 409.
- **`IVersionReportes` NO se invalida** desde finanzas: ese contador solo versiona el caché de reportes de stock. Asociar `GastoId` a un movimiento NO altera cantidades ni precios del stock.
- **TDD estricto**: test que falla primero (fallo inicial esperado: `CS0246` porque el tipo no existe — cuenta como rojo), implementación mínima, verde, commit. Un commit por task, conventional commit en español, SIN `Co-Authored-By` ni atribución de IA.
- **Nunca buildear porque sí**: solo `dotnet test` del proyecto afectado por task; la solución completa recién en la task final. No buildear la app desktop.
- **Tests de Infrastructure/Api requieren Docker** (Testcontainers levanta su Postgres efímero; el `stockapp-pg` de desarrollo queda corriendo aparte).
- **Migración EF**: startup project `src/StockApp.Api`; `AppDbContextFactory` de design-time hace innecesario un Postgres corriendo; la migración se aplica sola al arrancar la API (`MigrateAsync`).
- Los ViewModels atrapan `ReglaDeNegocioException`/`EntidadNoEncontradaException`/`ArgumentException` y las muestran (`MensajeError` / `IConfirmacionService.InformarAsync`) — dejarlas propagar crashea la app (regresión documentada).
- Las Views de Avalonia NO se auto-inicializan: cablear `DataContextChanged` para disparar `CargarAsync`/`InicializarAsync` (gotcha recurrente documentado).
- **Verificación orgánica** al final: app real + Postgres (contenedor `stockapp-pg`) antes de dar por terminado.

---

### Task 1: Domain — Gasto/PagoGasto/IngresoCaja, estado calculado, auditoría 31–39, GastoId en MovimientoStock y migración `FinanzasGastos`

**Files:**
- Create: `src/StockApp.Domain/Enums/CondicionPago.cs`
- Create: `src/StockApp.Domain/Enums/EstadoGasto.cs`
- Create: `src/StockApp.Domain/Entities/Gasto.cs`
- Create: `src/StockApp.Domain/Entities/PagoGasto.cs`
- Create: `src/StockApp.Domain/Entities/IngresoCaja.cs`
- Modify: `src/StockApp.Domain/Entities/MovimientoStock.cs` (`GastoId?` + nav)
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs` (valores 31–39, append-only)
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (DbSets + config fluida)
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_FinanzasGastos.cs` (generada por `dotnet ef`)
- Test: `tests/StockApp.Application.Tests/Finanzas/GastoEstadoTests.cs` (el cálculo de estado es lógica de dominio; se testea desde Application.Tests, que ya referencia Domain — no existe proyecto Domain.Tests)

**Interfaces:**
- Consumes: `StockApp.Domain.Entities` (Proveedor, FuenteFinanciamiento, RubroGasto, LineaPoa existentes), `Microsoft.EntityFrameworkCore`.
- Produces:
  - `enum CondicionPago { Contado = 0, Credito = 1 }`
  - `enum EstadoGasto { Pendiente = 0, Parcial = 1, Pagada = 2, Vencida = 3, Anulada = 4 }`
  - `class Gasto` con `decimal TotalPagado`, `decimal SaldoPendiente`, `EstadoGasto CalcularEstado(DateTime fechaReferencia)`
  - `class PagoGasto { int Id; int GastoId; DateTime Fecha; decimal Monto; string? Nota; bool Activo = true; }`
  - `class IngresoCaja { int Id; DateTime Fecha; string Concepto; int FuenteFinanciamientoId; FuenteFinanciamiento? FuenteFinanciamiento; decimal Monto; bool Activo = true; }`
  - `MovimientoStock.GastoId` (`int?`) + `MovimientoStock.Gasto` (`Gasto?`)
  - `AccionAuditada.AltaGasto = 31` … `AccionAuditada.AsociacionMovimientosAGasto = 39`
  - `AppDbContext.Gastos`, `.PagosGasto`, `.IngresosCaja`

- [ ] **Step 1: Escribir el test del estado calculado que falla**

`tests/StockApp.Application.Tests/Finanzas/GastoEstadoTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

/// <summary>
/// El estado de la factura NUNCA se persiste: se calcula de sum(pagos activos) vs
/// MontoTotal + FechaVencimiento + Activo (spec Finanzas §4, "Enums nuevos").
/// </summary>
public class GastoEstadoTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static Gasto GastoCredito(decimal monto, DateTime vencimiento) => new()
    {
        Detalle = "Compra de prueba",
        MontoTotal = monto,
        Fecha = Hoy.AddDays(-30),
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = vencimiento,
    };

    [Fact]
    public void SinPagos_CreditoNoVencido_Pendiente()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.TotalPagado);
        Assert.Equal(1000m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagoParcial_Parcial()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 400m });

        Assert.Equal(EstadoGasto.Parcial, gasto.CalcularEstado(Hoy));
        Assert.Equal(600m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagosCubrenElTotal_Pagada()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 400m });
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 600m });

        Assert.Equal(EstadoGasto.Pagada, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.SaldoPendiente);
    }

    [Fact]
    public void PagosAnulados_NoCuentan()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(10));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy, Monto = 1000m, Activo = false });

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
        Assert.Equal(0m, gasto.TotalPagado);
    }

    [Fact]
    public void CreditoVencidoSinCubrir_Vencida_InclusoConPagoParcial()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-5), Monto = 400m });

        Assert.Equal(EstadoGasto.Vencida, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void CreditoVencidoPeroPagado_Pagada()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Pagos.Add(new PagoGasto { Fecha = Hoy.AddDays(-5), Monto = 1000m });

        Assert.Equal(EstadoGasto.Pagada, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void ContadoNuncaVence_SinPagosEsPendiente()
    {
        // Caso teórico (el contado se crea con pago automático), pero el cálculo
        // no debe marcar Vencida sin FechaVencimiento.
        var gasto = new Gasto
        {
            Detalle = "Contado",
            MontoTotal = 500m,
            Fecha = Hoy.AddDays(-90),
            CondicionPago = CondicionPago.Contado,
        };

        Assert.Equal(EstadoGasto.Pendiente, gasto.CalcularEstado(Hoy));
    }

    [Fact]
    public void GastoInactivo_Anulada_SinImportarPagos()
    {
        var gasto = GastoCredito(1000m, Hoy.AddDays(-1));
        gasto.Activo = false;

        Assert.Equal(EstadoGasto.Anulada, gasto.CalcularEstado(Hoy));
    }
}
```

- [ ] **Step 2: Correr el test y verlo fallar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoEstadoTests"`
Expected: FALLA la compilación con `CS0246` (`Gasto` / `EstadoGasto` no existen) — rojo confirmado.

- [ ] **Step 3: Crear los enums y las tres entidades**

`src/StockApp.Domain/Enums/CondicionPago.cs`:

```csharp
namespace StockApp.Domain.Enums;

/// <summary>Condición de pago de un gasto. Contado crea un pago automático por el total.</summary>
public enum CondicionPago
{
    Contado = 0,
    Credito = 1,
}
```

`src/StockApp.Domain/Enums/EstadoGasto.cs`:

```csharp
namespace StockApp.Domain.Enums;

/// <summary>
/// Estado CALCULADO de un gasto/factura — nunca se persiste (spec Finanzas §4):
/// se deriva de sum(pagos activos) vs MontoTotal + FechaVencimiento + Activo,
/// así jamás queda inconsistente.
/// </summary>
public enum EstadoGasto
{
    Pendiente = 0,
    Parcial   = 1,
    Pagada    = 2,
    Vencida   = 3,
    Anulada   = 4,
}
```

`src/StockApp.Domain/Entities/Gasto.cs`:

```csharp
using StockApp.Domain.Enums;

namespace StockApp.Domain.Entities;

/// <summary>
/// Gasto de la caja municipal (cabecera única — enfoque A del spec): cada factura o
/// compromiso se registra UNA sola vez con sus dimensiones (fuente, rubro, línea POA
/// opcional). Agregado: sus <see cref="PagoGasto"/> se gestionan a través del gasto.
/// El número de factura es opcional (compromisos sin factura: solicitudes de
/// suministro, expedientes).
/// </summary>
public class Gasto
{
    public int Id { get; set; }
    public int ProveedorId { get; set; }
    public Proveedor? Proveedor { get; set; }
    public string? NumeroFactura { get; set; }
    public string? NumeroOrden { get; set; }              // orden de compra
    public string Detalle { get; set; } = string.Empty;   // obligatorio
    public string? Destino { get; set; }
    public DateTime Fecha { get; set; }                   // UTC
    public decimal MontoTotal { get; set; }               // precisión 18,4
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public int RubroGastoId { get; set; }
    public RubroGasto? RubroGasto { get; set; }
    public int? LineaPoaId { get; set; }
    public LineaPoa? LineaPoa { get; set; }
    public CondicionPago CondicionPago { get; set; }
    public DateTime? FechaVencimiento { get; set; }       // obligatoria si crédito
    public bool Activo { get; set; } = true;              // false = anulado
    public List<PagoGasto> Pagos { get; set; } = new();

    /// <summary>Suma de los pagos ACTIVOS (los anulados no cuentan).</summary>
    public decimal TotalPagado => Pagos.Where(p => p.Activo).Sum(p => p.Monto);

    /// <summary>Lo que falta pagar de la factura.</summary>
    public decimal SaldoPendiente => MontoTotal - TotalPagado;

    /// <summary>
    /// Estado calculado (spec §4): Anulada si el gasto está inactivo; Pagada si los
    /// pagos activos cubren el total; Vencida si es crédito con vencimiento anterior a
    /// la fecha de referencia y no está pagada; Parcial si hay pagos que no cubren el
    /// total; Pendiente en el resto. Recibe la fecha de referencia (hoy) por parámetro
    /// para ser determinístico y testeable.
    /// </summary>
    public EstadoGasto CalcularEstado(DateTime fechaReferencia)
    {
        if (!Activo)
            return EstadoGasto.Anulada;
        if (TotalPagado >= MontoTotal)
            return EstadoGasto.Pagada;
        if (CondicionPago == CondicionPago.Credito
            && FechaVencimiento is not null
            && FechaVencimiento.Value.Date < fechaReferencia.Date)
            return EstadoGasto.Vencida;
        return TotalPagado > 0 ? EstadoGasto.Parcial : EstadoGasto.Pendiente;
    }
}
```

`src/StockApp.Domain/Entities/PagoGasto.cs`:

```csharp
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
    public DateTime Fecha { get; set; }        // UTC — el saldo de caja impacta ACÁ
    public decimal Monto { get; set; }         // precisión 18,4
    public string? Nota { get; set; }
    public bool Activo { get; set; } = true;   // false = pago anulado
}
```

`src/StockApp.Domain/Entities/IngresoCaja.cs`:

```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Ingreso de la caja municipal: partidas mensuales FIGM, multas, préstamos.
/// El saldo inicial del ejercicio entra como un ingreso "Saldo inicial".
/// </summary>
public class IngresoCaja
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }                    // UTC
    public string Concepto { get; set; } = string.Empty;   // obligatorio
    public int FuenteFinanciamientoId { get; set; }
    public FuenteFinanciamiento? FuenteFinanciamiento { get; set; }
    public decimal Monto { get; set; }                     // precisión 18,4
    public bool Activo { get; set; } = true;               // baja lógica
}
```

- [ ] **Step 4: Vincular MovimientoStock con Gasto**

En `src/StockApp.Domain/Entities/MovimientoStock.cs`, agregar al final de la clase (después de `public string? Comentario { get; set; }`):

```csharp
    // ── Vínculo con Finanzas (Fase 2): una factura agrupa N movimientos de entrada.
    //    Opcional a propósito: ajustes y devoluciones no tienen factura y la operativa
    //    de stock no se bloquea por un dato financiero (spec Finanzas §5).
    public int? GastoId { get; set; }
    public Gasto? Gasto { get; set; }
```

- [ ] **Step 5: Agregar las acciones de auditoría (append-only)**

En `src/StockApp.Domain/Enums/AccionAuditada.cs`, agregar AL FINAL (después de `BajaLineaPoa = 30,`):

```csharp
    // ── Finanzas — Fase 2: gastos, pagos e ingresos (append-only a partir de 31) ──
    AltaGasto                   = 31,
    ModificacionGasto           = 32,
    AnulacionGasto              = 33,
    AltaPagoGasto               = 34,
    AnulacionPagoGasto          = 35,
    AltaIngresoCaja             = 36,
    ModificacionIngresoCaja     = 37,
    BajaIngresoCaja             = 38,
    AsociacionMovimientosAGasto = 39,
```

- [ ] **Step 6: Correr el test de estado y ver verde**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoEstadoTests"`
Expected: 8 tests en verde.

- [ ] **Step 7: DbSets + configuración fluida en AppDbContext**

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, agregar después de `public DbSet<AsignacionPresupuestal> AsignacionesPresupuestales => Set<AsignacionPresupuestal>();`:

```csharp
    public DbSet<Gasto> Gastos => Set<Gasto>();
    public DbSet<PagoGasto> PagosGasto => Set<PagoGasto>();
    public DbSet<IngresoCaja> IngresosCaja => Set<IngresoCaja>();
```

Y al final de `OnModelCreating` (después del bloque de `AsignacionPresupuestal`):

```csharp
        // ── Finanzas: documentos (Fase 2 módulo Finanzas) ─────────────────────
        // FKs Restrict en todos lados: los maestros y los gastos usan baja lógica,
        // nunca DELETE físico — no hay cascadas que propagar.
        modelBuilder.Entity<Gasto>(e =>
        {
            e.Property(g => g.Detalle).IsRequired();
            e.Property(g => g.MontoTotal).HasPrecision(18, 4);
            e.Property(g => g.Activo).HasDefaultValue(true);
            e.HasIndex(g => g.Fecha);
            // No único: la unicidad proveedor+factura es regla de negocio SOLO entre
            // gastos activos (un gasto anulado libera su número de factura).
            e.HasIndex(g => new { g.ProveedorId, g.NumeroFactura });
            e.HasOne(g => g.Proveedor).WithMany()
                .HasForeignKey(g => g.ProveedorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.FuenteFinanciamiento).WithMany()
                .HasForeignKey(g => g.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.RubroGasto).WithMany()
                .HasForeignKey(g => g.RubroGastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(g => g.LineaPoa).WithMany()
                .HasForeignKey(g => g.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PagoGasto>(e =>
        {
            e.Property(p => p.Monto).HasPrecision(18, 4);
            e.Property(p => p.Activo).HasDefaultValue(true);
            e.HasIndex(p => p.GastoId);
            e.HasOne<Gasto>().WithMany(g => g.Pagos)
                .HasForeignKey(p => p.GastoId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<IngresoCaja>(e =>
        {
            e.Property(i => i.Concepto).IsRequired();
            e.Property(i => i.Monto).HasPrecision(18, 4);
            e.Property(i => i.Activo).HasDefaultValue(true);
            e.HasIndex(i => i.Fecha);
            e.HasOne(i => i.FuenteFinanciamiento).WithMany()
                .HasForeignKey(i => i.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
        });

        // ── Vínculo stock ↔ finanzas: FK opcional en MovimientoStock ─────────
        modelBuilder.Entity<MovimientoStock>(e =>
        {
            e.HasIndex(m => m.GastoId);
            e.HasOne(m => m.Gasto).WithMany()
                .HasForeignKey(m => m.GastoId).OnDelete(DeleteBehavior.Restrict);
        });
```

- [ ] **Step 8: Compilar Infrastructure**

Run: `dotnet build src/StockApp.Infrastructure`
Expected: `Build succeeded` sin warnings nuevos.

- [ ] **Step 9: Generar la migración FinanzasGastos**

Run:
```bash
dotnet ef migrations add FinanzasGastos \
  --project src/StockApp.Infrastructure \
  --startup-project src/StockApp.Api
```
Expected: `Done.` y dos archivos nuevos en `src/StockApp.Infrastructure/Migrations/` (`<timestamp>_FinanzasGastos.cs` + `.Designer.cs`) más el snapshot actualizado.

- [ ] **Step 10: Verificar la migración generada**

Run: `rg -n "CreateTable|AddColumn" src/StockApp.Infrastructure/Migrations/*FinanzasGastos.cs | head -10`
Expected: `CreateTable` para `Gastos`, `PagosGasto` e `IngresosCaja` + `AddColumn` de `GastoId` (int nullable) en `MovimientosStock`.

Run: `rg -n "Restrict|precision: 18|GastoId" src/StockApp.Infrastructure/Migrations/*FinanzasGastos.cs | head -20`
Expected: FKs con `ReferentialAction.Restrict`, montos con `precision: 18, scale: 4`, FK `MovimientosStock.GastoId → Gastos` nullable, y NINGUNA columna de estado.

- [ ] **Step 11: Commit**

```bash
git add src/StockApp.Domain src/StockApp.Infrastructure tests/StockApp.Application.Tests
git commit -m "feat(finanzas): entidades Gasto/PagoGasto/IngresoCaja con estado calculado + migración FinanzasGastos"
```

---

### Task 2: Infrastructure — GastoRepository e IngresoCajaRepository (tests contra Postgres real)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IGastoRepository.cs`
- Create: `src/StockApp.Application/Interfaces/IIngresoCajaRepository.cs`
- Create: `src/StockApp.Application/Finanzas/GastosDtos.cs` (el filtro vive en Application, lo consume el repo)
- Create: `src/StockApp.Infrastructure/Repositories/GastoRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/IngresoCajaRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryTests.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/IngresoCajaRepositoryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` (Task 1), `PostgresRepositoryTestBase`/`PostgresFixture` (fixtures existentes).
- Produces:
  - `record GastoFiltro(DateTime? FechaDesde = null, DateTime? FechaHasta = null, int? ProveedorId = null, int? FuenteFinanciamientoId = null, int? RubroGastoId = null, int? LineaPoaId = null)`
  - `record ResultadoGastoDto(int Id, string? AdvertenciaSobregiro)` (lo usa el servicio en Task 3; se define acá para que el archivo de DTOs quede completo)
  - `interface IGastoRepository` (ver Step 3)
  - `interface IIngresoCajaRepository`: `Task<IngresoCaja?> ObtenerPorIdAsync(int id)`, `Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()`, `Task<int> AgregarAsync(IngresoCaja ingreso)`, `Task ActualizarAsync(IngresoCaja ingreso)`

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Infrastructure.Tests/Repositories/GastoRepositoryTests.cs`:

```csharp
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class GastoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly GastoRepository _repo;

    public GastoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new GastoRepository(Context);
    }

    // ── Seeds mínimos: el gasto exige proveedor + fuente + rubro por FK ──────

    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro test" };
        Context.AddRange(proveedor, fuente, rubro);
        await Context.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private async Task<int> SeedLineaPoaAsync(int fuenteId, decimal asignado)
    {
        var linea = new LineaPoa
        {
            Nombre = $"Linea {Guid.NewGuid():N}", Programa = "Test", Ejercicio = 2026,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = asignado } },
        };
        Context.Add(linea);
        await Context.SaveChangesAsync();
        return linea.Id;
    }

    private static Gasto NuevoGasto(int proveedorId, int fuenteId, int rubroId, DateTime fecha,
        decimal monto = 1000m, string? factura = null, int? lineaPoaId = null) => new()
    {
        ProveedorId = proveedorId,
        NumeroFactura = factura,
        Detalle = "Gasto de prueba",
        Fecha = fecha,
        MontoTotal = monto,
        FuenteFinanciamientoId = fuenteId,
        RubroGastoId = rubroId,
        LineaPoaId = lineaPoaId,
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = fecha.AddDays(30),
    };

    [Fact]
    public async Task AgregarAsync_ConPagos_Y_ObtenerPorId_TraeGrafoCompleto()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var fecha = DateTime.UtcNow;
        var gasto = NuevoGasto(proveedorId, fuenteId, rubroId, fecha, factura: "A-0001");
        gasto.Pagos.Add(new PagoGasto { Fecha = fecha, Monto = 400.5000m, Nota = "seña" });

        var id = await _repo.AgregarAsync(gasto);
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("A-0001", found!.NumeroFactura);
        Assert.NotNull(found.Proveedor);
        Assert.NotNull(found.FuenteFinanciamiento);
        Assert.NotNull(found.RubroGasto);
        var pago = Assert.Single(found.Pagos);
        Assert.Equal(400.5000m, pago.Monto);
        Assert.Equal(400.5000m, found.TotalPagado);
    }

    [Fact]
    public async Task ListarAsync_FiltraPorFechasYProveedor_OrdenaFechaDesc()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var (otroProveedorId, _, _) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy.AddDays(-10)));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy.AddDays(-1)));
        await _repo.AgregarAsync(NuevoGasto(otroProveedorId, fuenteId, rubroId, hoy.AddDays(-1)));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarAsync(new GastoFiltro(
            FechaDesde: hoy.AddDays(-5), ProveedorId: proveedorId));

        var gasto = Assert.Single(result);
        Assert.Equal(proveedorId, gasto.ProveedorId);

        var todos = await _repo.ListarAsync(new GastoFiltro(ProveedorId: proveedorId));
        Assert.Equal(2, todos.Count);
        Assert.True(todos[0].Fecha > todos[1].Fecha); // desc
    }

    [Fact]
    public async Task ListarAsync_FiltraPorLineaPoa()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var lineaId = await SeedLineaPoaAsync(fuenteId, 10000m);
        var hoy = DateTime.UtcNow;

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, lineaPoaId: lineaId));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy));
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarAsync(new GastoFiltro(LineaPoaId: lineaId));

        var gasto = Assert.Single(result);
        Assert.Equal(lineaId, gasto.LineaPoaId);
        Assert.NotNull(gasto.LineaPoa);
    }

    [Fact]
    public async Task ObtenerPorProveedorYFactura_SoloActivos()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var hoy = DateTime.UtcNow;
        var anulado = NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "B-0100");
        anulado.Activo = false;
        await _repo.AgregarAsync(anulado);
        Context.ChangeTracker.Clear();

        Assert.Null(await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, "B-0100"));

        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, factura: "B-0100"));
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, "B-0100");
        Assert.NotNull(found);
        Assert.True(found!.Activo);
    }

    [Fact]
    public async Task TotalGastadoLineaFuente_SumaSoloActivosDeEsaFuente_YExcluye()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var (_, otraFuenteId, _) = await SeedMaestrosAsync();
        var lineaId = await SeedLineaPoaAsync(fuenteId, 10000m);
        var hoy = DateTime.UtcNow;

        var id1 = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 1000m, lineaPoaId: lineaId));
        await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 2000m, lineaPoaId: lineaId));
        var anulado = NuevoGasto(proveedorId, fuenteId, rubroId, hoy, 5000m, lineaPoaId: lineaId);
        anulado.Activo = false;
        await _repo.AgregarAsync(anulado);
        await _repo.AgregarAsync(NuevoGasto(proveedorId, otraFuenteId, rubroId, hoy, 7000m, lineaPoaId: lineaId));
        Context.ChangeTracker.Clear();

        Assert.Equal(3000m, await _repo.TotalGastadoLineaFuenteAsync(lineaId, fuenteId));
        Assert.Equal(2000m, await _repo.TotalGastadoLineaFuenteAsync(lineaId, fuenteId, excluyendoGastoId: id1));
    }

    [Fact]
    public async Task AgregarPago_Y_AnularPago_Roundtrip()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var id = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow));
        Context.ChangeTracker.Clear();

        var pagoId = await _repo.AgregarPagoAsync(new PagoGasto
        {
            GastoId = id, Fecha = DateTime.UtcNow, Monto = 300m, Nota = "primer pago",
        });
        Context.ChangeTracker.Clear();

        var gasto = await _repo.ObtenerPorIdAsync(id);
        var pago = Assert.Single(gasto!.Pagos);
        Assert.Equal(pagoId, pago.Id);
        Assert.Equal(300m, gasto.TotalPagado);

        pago.Activo = false;
        await _repo.ActualizarPagoAsync(pago);
        Context.ChangeTracker.Clear();

        var releido = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal(0m, releido!.TotalPagado);
    }

    [Fact]
    public async Task AsignarYDesvincularMovimientos_ActualizaGastoId()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();

        // Seed de un movimiento de stock real (exige unidad + producto + usuario)
        var unidad = new UnidadMedida { Nombre = $"Unidad {Guid.NewGuid():N}", Abreviatura = Guid.NewGuid().ToString("N")[..8] };
        var usuario = new Usuario { NombreUsuario = $"user{Guid.NewGuid():N}"[..20], HashContrasena = "x", Rol = RolUsuario.Operador };
        Context.AddRange(unidad, usuario);
        await Context.SaveChangesAsync();
        var producto = new Producto
        {
            Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Prod test",
            UnidadMedidaId = unidad.Id,
        };
        Context.Add(producto);
        await Context.SaveChangesAsync();
        var movimiento = new MovimientoStock
        {
            ProductoId = producto.Id, UsuarioId = usuario.Id,
            Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra,
            Cantidad = 5m, PrecioUnitario = 100m, Fecha = DateTime.UtcNow,
        };
        Context.Add(movimiento);
        await Context.SaveChangesAsync();

        var gastoId = await _repo.AgregarAsync(NuevoGasto(proveedorId, fuenteId, rubroId, DateTime.UtcNow));
        Context.ChangeTracker.Clear();

        await _repo.AsignarGastoAMovimientosAsync(gastoId, new[] { movimiento.Id });
        Context.ChangeTracker.Clear();

        var vinculados = await _repo.ObtenerMovimientosAsync(new[] { movimiento.Id });
        Assert.Equal(gastoId, Assert.Single(vinculados).GastoId);

        await _repo.DesvincularMovimientosAsync(gastoId);
        Context.ChangeTracker.Clear();

        var desvinculados = await _repo.ObtenerMovimientosAsync(new[] { movimiento.Id });
        Assert.Null(Assert.Single(desvinculados).GastoId);
    }
}
```

`tests/StockApp.Infrastructure.Tests/Repositories/IngresoCajaRepositoryTests.cs`:

```csharp
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class IngresoCajaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly IngresoCajaRepository _repo;

    public IngresoCajaRepositoryTests(PostgresFixture fixture) : base(fixture)
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
    public async Task AgregarAsync_Y_ObtenerPorId_Roundtrip_ConFuenteNavegable()
    {
        var fuenteId = await SeedFuenteAsync();
        var id = await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Partida mensual FIGM",
            FuenteFinanciamientoId = fuenteId, Monto = 250000.5000m,
        });
        Context.ChangeTracker.Clear();

        var found = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(found);
        Assert.Equal("Partida mensual FIGM", found!.Concepto);
        Assert.Equal(250000.5000m, found.Monto);
        Assert.NotNull(found.FuenteFinanciamiento);
        Assert.True(found.Activo);
    }

    [Fact]
    public async Task ListarTodosAsync_OrdenaFechaDesc_SinFiltrarInactivos()
    {
        var fuenteId = await SeedFuenteAsync();
        var hoy = DateTime.UtcNow;
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = hoy.AddDays(-30), Concepto = "Viejo", FuenteFinanciamientoId = fuenteId, Monto = 1m, Activo = false,
        });
        await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = hoy, Concepto = "Nuevo", FuenteFinanciamientoId = fuenteId, Monto = 2m,
        });
        Context.ChangeTracker.Clear();

        var result = await _repo.ListarTodosAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Nuevo", result[0].Concepto);   // más reciente primero
        Assert.Equal("Viejo", result[1].Concepto);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_Persiste()
    {
        var fuenteId = await SeedFuenteAsync();
        var id = await _repo.AgregarAsync(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Multas", FuenteFinanciamientoId = fuenteId, Monto = 100m,
        });
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

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~GastoRepository|FullyQualifiedName~IngresoCajaRepository"`
Expected: FALLA la compilación con `CS0246` (`GastoRepository` no existe) — rojo confirmado.

- [ ] **Step 3: DTOs de filtro/resultado e interfaces de repos en Application**

`src/StockApp.Application/Finanzas/GastosDtos.cs`:

```csharp
namespace StockApp.Application.Finanzas;

/// <summary>Filtro combinable de la grilla "Gastos y facturas" (spec §7.1). Fechas en UTC.</summary>
public record GastoFiltro(
    DateTime? FechaDesde = null,
    DateTime? FechaHasta = null,
    int? ProveedorId = null,
    int? FuenteFinanciamientoId = null,
    int? RubroGastoId = null,
    int? LineaPoaId = null);

/// <summary>
/// Resultado de alta/modificación de gasto. AdvertenciaSobregiro viene no-nula cuando la
/// línea POA queda sobregirada para la fuente del gasto: el spec §10 manda ADVERTIR pero
/// NO bloquear (la app avisa, el humano decide) — por eso es un dato del resultado y no
/// una excepción.
/// </summary>
public record ResultadoGastoDto(int Id, string? AdvertenciaSobregiro);
```

`src/StockApp.Application/Interfaces/IGastoRepository.cs`:

```csharp
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IGastoRepository
{
    /// <summary>Incluye Proveedor, Fuente, Rubro, LineaPoa y TODOS los pagos (activos y anulados).</summary>
    Task<Gasto?> ObtenerPorIdAsync(int id);

    /// <summary>Busca un gasto ACTIVO por proveedor + número de factura (conciliación del vínculo stock).</summary>
    Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura);

    /// <summary>Con includes. Ordena por Fecha desc, luego Id desc. Los filtros nulos no aplican.</summary>
    Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro);

    /// <summary>Inserta el gasto CON sus pagos (grafo completo — pago contado automático incluido).</summary>
    Task<int> AgregarAsync(Gasto gasto);

    /// <summary>Actualiza la cabecera. <paramref name="gasto"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Gasto gasto);

    Task<int> AgregarPagoAsync(PagoGasto pago);

    /// <summary><paramref name="pago"/> debe ser una instancia tracked (hija de ObtenerPorIdAsync).</summary>
    Task ActualizarPagoAsync(PagoGasto pago);

    /// <summary>Suma MontoTotal de los gastos ACTIVOS de esa línea POA + fuente (para la advertencia de sobregiro).</summary>
    Task<decimal> TotalGastadoLineaFuenteAsync(int lineaPoaId, int fuenteFinanciamientoId, int? excluyendoGastoId = null);

    /// <summary>Trae los movimientos de stock por id (para validar el vínculo antes de asignar).</summary>
    Task<IReadOnlyList<MovimientoStock>> ObtenerMovimientosAsync(IReadOnlyList<int> movimientoIds);

    /// <summary>Setea GastoId en los movimientos indicados.</summary>
    Task AsignarGastoAMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds);

    /// <summary>Pone GastoId = null en todos los movimientos del gasto (al anularlo).</summary>
    Task DesvincularMovimientosAsync(int gastoId);
}
```

`src/StockApp.Application/Interfaces/IIngresoCajaRepository.cs`:

```csharp
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
}
```

- [ ] **Step 4: Implementar los repos**

`src/StockApp.Infrastructure/Repositories/GastoRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class GastoRepository : IGastoRepository
{
    private readonly AppDbContext _ctx;

    public GastoRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<Gasto> ConIncludes() =>
        _ctx.Gastos
            .Include(g => g.Proveedor)
            .Include(g => g.FuenteFinanciamiento)
            .Include(g => g.RubroGasto)
            .Include(g => g.LineaPoa)
            .Include(g => g.Pagos);

    public Task<Gasto?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(g => g.Id == id);

    public Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)
        => ConIncludes().FirstOrDefaultAsync(g =>
            g.Activo && g.ProveedorId == proveedorId && g.NumeroFactura == numeroFactura);

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        var query = ConIncludes();

        if (filtro.FechaDesde is not null)
            query = query.Where(g => g.Fecha >= filtro.FechaDesde);
        if (filtro.FechaHasta is not null)
            query = query.Where(g => g.Fecha <= filtro.FechaHasta);
        if (filtro.ProveedorId is not null)
            query = query.Where(g => g.ProveedorId == filtro.ProveedorId);
        if (filtro.FuenteFinanciamientoId is not null)
            query = query.Where(g => g.FuenteFinanciamientoId == filtro.FuenteFinanciamientoId);
        if (filtro.RubroGastoId is not null)
            query = query.Where(g => g.RubroGastoId == filtro.RubroGastoId);
        if (filtro.LineaPoaId is not null)
            query = query.Where(g => g.LineaPoaId == filtro.LineaPoaId);

        return await query
            .OrderByDescending(g => g.Fecha)
            .ThenByDescending(g => g.Id)
            .ToListAsync();
    }

    public async Task<int> AgregarAsync(Gasto gasto)
    {
        _ctx.Gastos.Add(gasto);  // inserta el grafo completo (gasto + pagos)
        await _ctx.SaveChangesAsync();
        return gasto.Id;
    }

    public Task ActualizarAsync(Gasto gasto)
    {
        _ctx.Gastos.Update(gasto);
        return _ctx.SaveChangesAsync();
    }

    public async Task<int> AgregarPagoAsync(PagoGasto pago)
    {
        _ctx.PagosGasto.Add(pago);
        await _ctx.SaveChangesAsync();
        return pago.Id;
    }

    public Task ActualizarPagoAsync(PagoGasto pago)
    {
        _ctx.PagosGasto.Update(pago);
        return _ctx.SaveChangesAsync();
    }

    public async Task<decimal> TotalGastadoLineaFuenteAsync(
        int lineaPoaId, int fuenteFinanciamientoId, int? excluyendoGastoId = null)
        => await _ctx.Gastos
            .Where(g => g.Activo
                        && g.LineaPoaId == lineaPoaId
                        && g.FuenteFinanciamientoId == fuenteFinanciamientoId
                        && (excluyendoGastoId == null || g.Id != excluyendoGastoId))
            .SumAsync(g => (decimal?)g.MontoTotal) ?? 0m;

    public async Task<IReadOnlyList<MovimientoStock>> ObtenerMovimientosAsync(IReadOnlyList<int> movimientoIds)
        => await _ctx.MovimientosStock.Where(m => movimientoIds.Contains(m.Id)).ToListAsync();

    public async Task AsignarGastoAMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        var movimientos = await _ctx.MovimientosStock
            .Where(m => movimientoIds.Contains(m.Id)).ToListAsync();
        foreach (var movimiento in movimientos)
            movimiento.GastoId = gastoId;
        await _ctx.SaveChangesAsync();
    }

    public async Task DesvincularMovimientosAsync(int gastoId)
    {
        var movimientos = await _ctx.MovimientosStock
            .Where(m => m.GastoId == gastoId).ToListAsync();
        foreach (var movimiento in movimientos)
            movimiento.GastoId = null;
        await _ctx.SaveChangesAsync();
    }
}
```

`src/StockApp.Infrastructure/Repositories/IngresoCajaRepository.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class IngresoCajaRepository : IIngresoCajaRepository
{
    private readonly AppDbContext _ctx;

    public IngresoCajaRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<IngresoCaja?> ObtenerPorIdAsync(int id)
        => _ctx.IngresosCaja
            .Include(i => i.FuenteFinanciamiento)
            .FirstOrDefaultAsync(i => i.Id == id);

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
        => await _ctx.IngresosCaja
            .Include(i => i.FuenteFinanciamiento)
            .OrderByDescending(i => i.Fecha)
            .ThenByDescending(i => i.Id)
            .ToListAsync();

    public async Task<int> AgregarAsync(IngresoCaja ingreso)
    {
        _ctx.IngresosCaja.Add(ingreso);
        await _ctx.SaveChangesAsync();
        return ingreso.Id;
    }

    public Task ActualizarAsync(IngresoCaja ingreso)
    {
        _ctx.IngresosCaja.Update(ingreso);
        return _ctx.SaveChangesAsync();
    }
}
```

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~GastoRepository|FullyQualifiedName~IngresoCajaRepository"`
Expected: los 10 tests nuevos en verde (requiere Docker para Testcontainers).

- [ ] **Step 6: Suite completa de Infrastructure**

Run: `dotnet test tests/StockApp.Infrastructure.Tests`
Expected: toda la suite verde (sin regresiones).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application src/StockApp.Infrastructure tests/StockApp.Infrastructure.Tests
git commit -m "feat(finanzas): repositorios de gastos e ingresos de caja con tests contra Postgres"
```

---

### Task 3: Application — permisos nuevos + GastoService (alta/modificación/anulación/pagos/vínculo) con auditoría

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Create: `src/StockApp.Application/Finanzas/IGastoService.cs`
- Create: `src/StockApp.Application/Finanzas/GastoService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs`

**Interfaces:**
- Consumes: `IGastoRepository` (Task 2), `IProveedorRepository`, `IFuenteFinanciamientoRepository`, `IRubroGastoRepository`, `ILineaPoaRepository`, `ICurrentSession`, `IAuthorizationService`, `IAuditLogger`, `AccionAuditada` 31–35 y 39.
- Produces:
  - `Permisos.RegistrarGastos = "finanzas.gastos"`, `Permisos.RegistrarPagos = "finanzas.pagos"`, `Permisos.RegistrarIngresos = "finanzas.ingresos"` (agregados a `Permisos.Todos` — las policies HTTP se derivan solas) y a `AuthorizationService.AccionesOperador` (spec §9: Admin Y Operador).
  - `interface IGastoService`:
    - `Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)`
    - `Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)`
    - `Task AnularAsync(int id)`
    - `Task<Gasto> ObtenerPorIdAsync(int id)` (lanza `EntidadNoEncontradaException`)
    - `Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)`
    - `Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)`
    - `Task<int> RegistrarPagoAsync(PagoGasto pago)`
    - `Task AnularPagoAsync(int gastoId, int pagoId)`
    - `Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)`

Reglas de autorización: mutaciones de gasto y vínculo → `RegistrarGastos`; pagos → `RegistrarPagos`; lecturas → `VerFinanzas`.

Reglas de negocio implementadas (spec §10):
1. Crédito exige `FechaVencimiento`; contado NO la lleva (409 en ambos sentidos).
2. Contado ⇒ pago automático por el total en la fecha del gasto.
3. No existe otro gasto ACTIVO del mismo proveedor con el mismo `NumeroFactura` (los anulados liberan el número — necesario para la conciliación del importador y el flujo "asociar a factura existente").
4. Gasto con línea POA: la fuente DEBE tener asignación presupuestal en esa línea (409); si el total gastado supera lo asignado → **advertencia** en el resultado, NUNCA bloqueo.
5. Maestros dados de baja no se aceptan para gastos nuevos; en modificación solo si el campo cambió (los históricos se conservan).
6. Modificar: no sobre gastos anulados; el monto no puede quedar bajo lo ya pagado; la condición de pago NO se cambia (anular y recrear).
7. Pagos: monto > 0, fecha obligatoria, no superar el saldo pendiente, solo sobre gastos activos.
8. Anular gasto: sin pagos activos (primero anular los pagos); al anular se desvinculan sus movimientos de stock (quedan libres para re-facturar).
9. Vincular movimientos: solo ENTRADAS sin gasto previo.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Application.Tests/Finanzas/GastoServiceTests.cs`:

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

public class GastoServiceTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private sealed record Mocks(
        GastoService Svc,
        Mock<IGastoRepository> Repo,
        Mock<IProveedorRepository> Proveedores,
        Mock<IFuenteFinanciamientoRepository> Fuentes,
        Mock<IRubroGastoRepository> Rubros,
        Mock<ILineaPoaRepository> LineasPoa,
        Mock<IAuditLogger> Audit);

    private static Mocks Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo       = new Mock<IGastoRepository>();
        var proveedores = new Mock<IProveedorRepository>();
        var fuentes    = new Mock<IFuenteFinanciamientoRepository>();
        var rubros     = new Mock<IRubroGastoRepository>();
        var lineasPoa  = new Mock<ILineaPoaRepository>();
        var session    = new Mock<ICurrentSession>();
        var auth       = new Mock<IAuthSvc>();
        var audit      = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        // Maestros por defecto: existen y están activos (los tests puntuales los pisan)
        proveedores.Setup(p => p.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new Proveedor { Id = id, Nombre = $"Proveedor {id}", Activo = true });
        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });
        rubros.Setup(r => r.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new RubroGasto { Id = id, Codigo = id, Nombre = $"Rubro {id}", Activo = true });

        var svc = new GastoService(
            repo.Object, proveedores.Object, fuentes.Object, rubros.Object, lineasPoa.Object,
            session.Object, auth.Object, audit.Object);
        return new Mocks(svc, repo, proveedores, fuentes, rubros, lineasPoa, audit);
    }

    private static Gasto GastoValido(CondicionPago condicion = CondicionPago.Credito) => new()
    {
        ProveedorId = 1,
        NumeroFactura = "A-0001",
        Detalle = "Materiales de obra",
        Fecha = Hoy,
        MontoTotal = 1000m,
        FuenteFinanciamientoId = 2,
        RubroGastoId = 3,
        CondicionPago = condicion,
        FechaVencimiento = condicion == CondicionPago.Credito ? Hoy.AddDays(30) : null,
    };

    // ── Alta ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AltaAsync_DetalleVacio_LanzaArgumentException()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Detalle = "  ";

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaArgumentException()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.MontoTotal = 0m;

        await Assert.ThrowsAsync<ArgumentException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_CreditoSinVencimiento_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.FechaVencimiento = null;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_ContadoConVencimiento_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido(CondicionPago.Contado);
        gasto.FechaVencimiento = Hoy.AddDays(10);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_Contado_CreaPagoAutomaticoPorElTotal()
    {
        var m = Crear();
        Gasto? persistido = null;
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>()))
            .Callback<Gasto>(g => persistido = g)
            .ReturnsAsync(7);

        var resultado = await m.Svc.AltaAsync(GastoValido(CondicionPago.Contado));

        Assert.Equal(7, resultado.Id);
        Assert.Null(resultado.AdvertenciaSobregiro);
        var pago = Assert.Single(persistido!.Pagos);
        Assert.Equal(1000m, pago.Monto);
        Assert.Equal(Hoy, pago.Fecha);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaGasto, "Gasto", 7, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AltaAsync_Credito_NoCreaPagoAutomatico()
    {
        var m = Crear();
        Gasto? persistido = null;
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>()))
            .Callback<Gasto>(g => persistido = g)
            .ReturnsAsync(8);

        await m.Svc.AltaAsync(GastoValido());

        Assert.Empty(persistido!.Pagos);
    }

    [Fact]
    public async Task AltaAsync_FacturaDuplicadaDeProveedorActiva_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerPorProveedorYFacturaAsync(1, "A-0001"))
            .ReturnsAsync(new Gasto { Id = 99, ProveedorId = 1, NumeroFactura = "A-0001" });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(GastoValido()));
    }

    [Fact]
    public async Task AltaAsync_FuenteInactiva_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Fuentes.Setup(f => f.ObtenerPorIdAsync(2))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 2, Nombre = "Vieja", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(GastoValido()));
    }

    [Fact]
    public async Task AltaAsync_LineaPoaSinAsignacionParaLaFuente_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.LineasPoa.Setup(l => l.ObtenerPorIdAsync(5)).ReturnsAsync(new LineaPoa
        {
            Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 99, Monto = 1000m } },
        });
        var gasto = GastoValido();
        gasto.LineaPoaId = 5;  // fuente 2 no tiene asignación en la línea

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AltaAsync(gasto));
    }

    [Fact]
    public async Task AltaAsync_SobregiroDeLinea_AdvierteYNoBloquea()
    {
        var m = Crear();
        m.LineasPoa.Setup(l => l.ObtenerPorIdAsync(5)).ReturnsAsync(new LineaPoa
        {
            Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true,
            Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = 2, Monto = 5000m } },
        });
        m.Repo.Setup(r => r.TotalGastadoLineaFuenteAsync(5, 2, null)).ReturnsAsync(4500m);
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>())).ReturnsAsync(10);
        var gasto = GastoValido();          // 1000: 4500 + 1000 > 5000 ⇒ sobregiro 500
        gasto.LineaPoaId = 5;

        var resultado = await m.Svc.AltaAsync(gasto);

        Assert.Equal(10, resultado.Id);     // se registró IGUAL (spec §10: advierte, no bloquea)
        Assert.NotNull(resultado.AdvertenciaSobregiro);
        Assert.Contains("PRENSA", resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task AltaAsync_ConMovimientos_ValidaYAsigna()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = null },
            });
        m.Repo.Setup(r => r.AgregarAsync(It.IsAny<Gasto>())).ReturnsAsync(11);

        await m.Svc.AltaAsync(GastoValido(), new[] { 40 });

        m.Repo.Verify(r => r.AsignarGastoAMovimientosAsync(11,
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
    }

    [Fact]
    public async Task AltaAsync_MovimientoDeSalida_LanzaReglaDeNegocio()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 41, Tipo = TipoMovimiento.Salida, GastoId = null },
            });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => m.Svc.AltaAsync(GastoValido(), new[] { 41 }));
    }

    // ── Modificación ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ModificarAsync_GastoAnulado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        original.Activo = false;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_CambiaCondicionDePago_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido(CondicionPago.Contado);
        editado.Id = 1;

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_MontoMenorALoPagado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        original.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 800m });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;
        editado.MontoTotal = 500m;   // < 800 pagado

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.ModificarAsync(editado));
    }

    [Fact]
    public async Task ModificarAsync_CambiaDetalleYMonto_ActualizaYAudita()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;
        editado.Detalle = "Materiales de obra (ampliación)";
        editado.MontoTotal = 1500m;

        var resultado = await m.Svc.ModificarAsync(editado);

        Assert.Equal(1, resultado.Id);
        m.Repo.Verify(r => r.ActualizarAsync(It.Is<Gasto>(g =>
            g.Detalle == "Materiales de obra (ampliación)" && g.MontoTotal == 1500m)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionGasto, "Gasto", 1,
            It.Is<string>(d => d.Contains("Detalle") && d.Contains("Monto"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var m = Crear();
        var original = GastoValido();
        original.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);
        var editado = GastoValido();
        editado.Id = 1;

        await m.Svc.ModificarAsync(editado);

        m.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Gasto>()), Times.Never);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RegistrarPagoAsync_SuperaElSaldo_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 800m });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 300m }));  // saldo = 200
    }

    [Fact]
    public async Task RegistrarPagoAsync_GastoAnulado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Activo = false;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m }));
    }

    [Fact]
    public async Task RegistrarPagoAsync_Valido_PersisteYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.Repo.Setup(r => r.AgregarPagoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(21);

        var pagoId = await m.Svc.RegistrarPagoAsync(
            new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 1000m, Nota = "pago total" });

        Assert.Equal(21, pagoId);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaPagoGasto, "PagoGasto", 21,
            It.Is<string>(d => d.Contains("1000"))), Times.Once);
    }

    [Fact]
    public async Task AnularPagoAsync_PagoActivo_AnulaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        var pago = new PagoGasto { Id = 21, GastoId = 1, Fecha = Hoy, Monto = 500m, Activo = true };
        gasto.Pagos.Add(pago);
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await m.Svc.AnularPagoAsync(1, 21);

        m.Repo.Verify(r => r.ActualizarPagoAsync(It.Is<PagoGasto>(p => p.Id == 21 && !p.Activo)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionPagoGasto, "PagoGasto", 21, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AnularPagoAsync_PagoYaAnulado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { Id = 21, GastoId = 1, Fecha = Hoy, Monto = 500m, Activo = false });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AnularPagoAsync(1, 21));
    }

    // ── Anulación del gasto ──────────────────────────────────────────────────

    [Fact]
    public async Task AnularAsync_ConPagosActivos_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => m.Svc.AnularAsync(1));
    }

    [Fact]
    public async Task AnularAsync_SinPagosActivos_AnulaDesvinculaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        gasto.Pagos.Add(new PagoGasto { GastoId = 1, Fecha = Hoy, Monto = 100m, Activo = false });
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);

        await m.Svc.AnularAsync(1);

        m.Repo.Verify(r => r.ActualizarAsync(It.Is<Gasto>(g => !g.Activo)), Times.Once);
        m.Repo.Verify(r => r.DesvincularMovimientosAsync(1), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AnulacionGasto, "Gasto", 1, It.IsAny<string>()), Times.Once);
    }

    // ── Asociación de movimientos a factura existente ────────────────────────

    [Fact]
    public async Task AsociarMovimientosAsync_MovimientoYaFacturado_LanzaReglaDeNegocio()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = 99 },
            });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => m.Svc.AsociarMovimientosAsync(1, new[] { 40 }));
    }

    [Fact]
    public async Task AsociarMovimientosAsync_Valido_AsignaYAudita()
    {
        var m = Crear();
        var gasto = GastoValido();
        gasto.Id = 1;
        m.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(gasto);
        m.Repo.Setup(r => r.ObtenerMovimientosAsync(It.IsAny<IReadOnlyList<int>>()))
            .ReturnsAsync(new List<MovimientoStock>
            {
                new() { Id = 40, Tipo = TipoMovimiento.Entrada, GastoId = null },
            });

        await m.Svc.AsociarMovimientosAsync(1, new[] { 40 });

        m.Repo.Verify(r => r.AsignarGastoAMovimientosAsync(1,
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
        m.Audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AsociacionMovimientosAGasto, "Gasto", 1,
            It.IsAny<string>()), Times.Once);
    }

    // ── Lecturas ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ObtenerPorIdAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var m = Crear();
        m.Repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => m.Svc.ObtenerPorIdAsync(99));
    }

    [Fact]
    public async Task ListarAsync_DelegaAlRepo()
    {
        var m = Crear();
        var filtro = new GastoFiltro(ProveedorId: 1);
        m.Repo.Setup(r => r.ListarAsync(filtro)).ReturnsAsync(new List<Gasto> { GastoValido() });

        var result = await m.Svc.ListarAsync(filtro);

        Assert.Single(result);
        m.Repo.Verify(r => r.ListarAsync(filtro), Times.Once);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoServiceTests"`
Expected: FALLA la compilación con `CS0246` (`GastoService` no existe) — rojo confirmado.

- [ ] **Step 3: Permisos nuevos**

En `src/StockApp.Application/Authorization/Permisos.cs`, reemplazar el bloque de Finanzas y `Todos` así:

```csharp
    // Finanzas — Fases 1 y 2: por ahora Admin Y Operador tienen todos (spec Finanzas §9);
    // el futuro sistema de permisos por usuario solo cambia el mapeo rol→permiso.
    public const string VerFinanzas              = "finanzas.ver";
    public const string GestionarMaestrosFinanzas = "finanzas.maestros";
    public const string RegistrarGastos           = "finanzas.gastos";
    public const string RegistrarPagos            = "finanzas.pagos";
    public const string RegistrarIngresos         = "finanzas.ingresos";
```

y en `Todos`, agregar al final de la lista (después de `GestionarMaestrosFinanzas,`):

```csharp
        RegistrarGastos,
        RegistrarPagos,
        RegistrarIngresos,
```

En `src/StockApp.Application/Authorization/AuthorizationService.cs`, agregar a `AccionesOperador` (después de `Permisos.GestionarMaestrosFinanzas,`):

```csharp
        Permisos.RegistrarGastos,
        Permisos.RegistrarPagos,
        Permisos.RegistrarIngresos,
```

- [ ] **Step 4: Interfaz e implementación del servicio**

`src/StockApp.Application/Finanzas/IGastoService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Gastos y facturas del módulo Finanzas (spec §4-§5, §10). El estado de la factura
/// NUNCA se persiste: lo calcula la entidad. La advertencia de sobregiro POA viaja en
/// el resultado (advierte, no bloquea). Fail-closed: cada método verifica autorización.
/// </summary>
public interface IGastoService
{
    /// <summary>
    /// Alta del gasto. Contado ⇒ crea el pago automático por el total. Si vienen
    /// <paramref name="movimientoIds"/> (flujo "Asociar factura" de la entrada de stock),
    /// los valida (entradas sin gasto previo) y los vincula al gasto creado.
    /// </summary>
    Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null);

    Task<ResultadoGastoDto> ModificarAsync(Gasto gasto);

    /// <summary>Anulación (baja lógica). Exige que no haya pagos activos; desvincula sus movimientos.</summary>
    Task AnularAsync(int id);

    /// <summary>Lanza EntidadNoEncontradaException si no existe.</summary>
    Task<Gasto> ObtenerPorIdAsync(int id);

    /// <summary>Busca la factura ACTIVA de un proveedor (flujo "asociar a factura existente"). Null si no hay.</summary>
    Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura);

    Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro);

    /// <summary>Registra un pago del gasto. Devuelve el id del pago creado.</summary>
    Task<int> RegistrarPagoAsync(PagoGasto pago);

    Task AnularPagoAsync(int gastoId, int pagoId);

    /// <summary>Vincula movimientos de ENTRADA sin factura a un gasto existente.</summary>
    Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds);
}
```

`src/StockApp.Application/Finanzas/GastoService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

public class GastoService : IGastoService
{
    private readonly IGastoRepository                _repo;
    private readonly IProveedorRepository            _proveedores;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly IRubroGastoRepository           _rubros;
    private readonly ILineaPoaRepository             _lineasPoa;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public GastoService(
        IGastoRepository repo,
        IProveedorRepository proveedores,
        IFuenteFinanciamientoRepository fuentes,
        IRubroGastoRepository rubros,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo        = repo;
        _proveedores = proveedores;
        _fuentes     = fuentes;
        _rubros      = rubros;
        _lineasPoa   = lineasPoa;
        _session     = session;
        _auth        = auth;
        _audit       = audit;
    }

    // ── Alta ──────────────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var linea = await ValidarAsync(gasto, esAlta: true, original: null);
        await ValidarFacturaUnicaAsync(gasto);
        var advertencia = await AdvertirSobregiroAsync(gasto, linea, excluyendoGastoId: null);

        // Contado ⇒ pago automático por el total en la fecha del gasto (spec §4)
        if (gasto.CondicionPago == CondicionPago.Contado)
            gasto.Pagos = new List<PagoGasto>
            {
                new() { Fecha = gasto.Fecha, Monto = gasto.MontoTotal, Nota = "Pago contado (automático)" },
            };

        if (movimientoIds is { Count: > 0 })
            await ValidarMovimientosAsync(movimientoIds);

        var id = await _repo.AgregarAsync(gasto);

        if (movimientoIds is { Count: > 0 })
            await _repo.AsignarGastoAMovimientosAsync(id, movimientoIds);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaGasto, "Gasto", id,
            $"Proveedor: {gasto.ProveedorId}; Factura: {gasto.NumeroFactura ?? "(sin factura)"}; " +
            $"Monto: {gasto.MontoTotal}; Condición: {gasto.CondicionPago}" +
            (movimientoIds is { Count: > 0 } ? $"; Movimientos vinculados: {movimientoIds.Count}" : string.Empty));

        return new ResultadoGastoDto(id, advertencia);
    }

    // ── Modificación ─────────────────────────────────────────────────────────

    public async Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var original = await _repo.ObtenerPorIdAsync(gasto.Id)
            ?? throw new EntidadNoEncontradaException($"Gasto {gasto.Id} no encontrado.");

        if (!original.Activo)
            throw new ReglaDeNegocioException("No se puede modificar un gasto anulado.");
        if (gasto.CondicionPago != original.CondicionPago)
            throw new ReglaDeNegocioException(
                "No se puede cambiar la condición de pago de un gasto registrado: anulalo y cargalo de nuevo.");

        var linea = await ValidarAsync(gasto, esAlta: false, original);

        if (gasto.MontoTotal < original.TotalPagado)
            throw new ReglaDeNegocioException(
                $"El monto total no puede quedar por debajo de lo ya pagado ({original.TotalPagado}).");

        if (!string.IsNullOrWhiteSpace(gasto.NumeroFactura)
            && (gasto.NumeroFactura != original.NumeroFactura || gasto.ProveedorId != original.ProveedorId))
            await ValidarFacturaUnicaAsync(gasto);

        var advertencia = await AdvertirSobregiroAsync(gasto, linea, excluyendoGastoId: gasto.Id);

        var cambios = new List<string>();
        void Comparar<T>(string campo, T viejo, T nuevo)
        {
            if (!EqualityComparer<T>.Default.Equals(viejo, nuevo))
                cambios.Add($"{campo}: {viejo} → {nuevo}");
        }

        Comparar("Proveedor", original.ProveedorId, gasto.ProveedorId);
        Comparar("Factura", original.NumeroFactura, gasto.NumeroFactura);
        Comparar("Orden", original.NumeroOrden, gasto.NumeroOrden);
        Comparar("Detalle", original.Detalle, gasto.Detalle);
        Comparar("Destino", original.Destino, gasto.Destino);
        Comparar("Fecha", original.Fecha, gasto.Fecha);
        Comparar("Monto", original.MontoTotal, gasto.MontoTotal);
        Comparar("Fuente", original.FuenteFinanciamientoId, gasto.FuenteFinanciamientoId);
        Comparar("Rubro", original.RubroGastoId, gasto.RubroGastoId);
        Comparar("Línea POA", original.LineaPoaId, gasto.LineaPoaId);
        Comparar("Vencimiento", original.FechaVencimiento, gasto.FechaVencimiento);

        if (cambios.Count == 0)
            return new ResultadoGastoDto(gasto.Id, null);

        // Solo se tocan las FKs, NUNCA las navs: cambiar la nav a null en una instancia
        // tracked haría que el fixup de EF pise el FK nuevo. Con la nav intacta y el FK
        // modificado, EF da precedencia al FK (comportamiento documentado de DetectChanges).
        original.ProveedorId            = gasto.ProveedorId;
        original.NumeroFactura          = gasto.NumeroFactura;
        original.NumeroOrden            = gasto.NumeroOrden;
        original.Detalle                = gasto.Detalle;
        original.Destino                = gasto.Destino;
        original.Fecha                  = gasto.Fecha;
        original.MontoTotal             = gasto.MontoTotal;
        original.FuenteFinanciamientoId = gasto.FuenteFinanciamientoId;
        original.RubroGastoId           = gasto.RubroGastoId;
        original.LineaPoaId             = gasto.LineaPoaId;
        original.FechaVencimiento       = gasto.FechaVencimiento;

        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionGasto, "Gasto", gasto.Id,
            string.Join("; ", cambios));

        return new ResultadoGastoDto(gasto.Id, advertencia);
    }

    // ── Anulación ────────────────────────────────────────────────────────────

    public async Task AnularAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException($"El gasto {id} ya está anulado.");
        if (gasto.Pagos.Any(p => p.Activo))
            throw new ReglaDeNegocioException(
                "No se puede anular un gasto con pagos activos: primero anulá los pagos.");

        gasto.Activo = false;
        await _repo.ActualizarAsync(gasto);
        // Los movimientos quedan libres para re-facturar (el gasto anulado no los retiene)
        await _repo.DesvincularMovimientosAsync(id);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionGasto, "Gasto", id,
            $"Anulación de '{gasto.Detalle}' (factura {gasto.NumeroFactura ?? "s/n"}, monto {gasto.MontoTotal})");
    }

    // ── Pagos ────────────────────────────────────────────────────────────────

    public async Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

        if (pago.Monto <= 0)
            throw new ArgumentException("El monto del pago debe ser mayor a cero.");
        if (pago.Fecha == default)
            throw new ArgumentException("La fecha del pago es obligatoria.");

        var gasto = await _repo.ObtenerPorIdAsync(pago.GastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {pago.GastoId} no encontrado.");

        if (!gasto.Activo)
            throw new ReglaDeNegocioException("No se pueden registrar pagos sobre un gasto anulado.");
        if (pago.Monto > gasto.SaldoPendiente)
            throw new ReglaDeNegocioException(
                $"El pago ({pago.Monto}) supera el saldo pendiente de la factura ({gasto.SaldoPendiente}).");

        var pagoId = await _repo.AgregarPagoAsync(pago);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaPagoGasto, "PagoGasto", pagoId,
            $"Gasto: {pago.GastoId}; Monto: {pago.Monto}; Fecha: {pago.Fecha:yyyy-MM-dd}");

        return pagoId;
    }

    public async Task AnularPagoAsync(int gastoId, int pagoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

        var gasto = await _repo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");
        var pago = gasto.Pagos.FirstOrDefault(p => p.Id == pagoId)
            ?? throw new EntidadNoEncontradaException($"Pago {pagoId} no encontrado en el gasto {gastoId}.");

        if (!pago.Activo)
            throw new ReglaDeNegocioException($"El pago {pagoId} ya está anulado.");

        pago.Activo = false;
        await _repo.ActualizarPagoAsync(pago);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AnulacionPagoGasto, "PagoGasto", pagoId,
            $"Gasto: {gastoId}; Monto anulado: {pago.Monto}");
    }

    // ── Vínculo con movimientos de stock ─────────────────────────────────────

    public async Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _repo.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"Gasto {gastoId} no encontrado.");
        if (!gasto.Activo)
            throw new ReglaDeNegocioException("No se pueden asociar movimientos a un gasto anulado.");

        await ValidarMovimientosAsync(movimientoIds);
        await _repo.AsignarGastoAMovimientosAsync(gastoId, movimientoIds);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AsociacionMovimientosAGasto, "Gasto", gastoId,
            $"Movimientos vinculados: {string.Join(", ", movimientoIds)}");
    }

    // ── Lecturas ─────────────────────────────────────────────────────────────

    public async Task<Gasto> ObtenerPorIdAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Gasto {id} no encontrado.");
    }

    public async Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ObtenerPorProveedorYFacturaAsync(proveedorId, numeroFactura);
    }

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ListarAsync(filtro);
    }

    // ── Validaciones privadas ────────────────────────────────────────────────

    /// <summary>
    /// Valida input + existencia/actividad de los maestros. Devuelve la LineaPoa cargada
    /// (con asignaciones) si el gasto la tiene, para reutilizarla en el chequeo de sobregiro.
    /// En modificación, un maestro inactivo solo se rechaza si el campo CAMBIÓ (los
    /// históricos se conservan — spec §10).
    /// </summary>
    private async Task<LineaPoa?> ValidarAsync(Gasto gasto, bool esAlta, Gasto? original)
    {
        if (string.IsNullOrWhiteSpace(gasto.Detalle))
            throw new ArgumentException("El detalle del gasto es obligatorio.");
        if (gasto.MontoTotal <= 0)
            throw new ArgumentException("El monto total del gasto debe ser mayor a cero.");
        if (gasto.Fecha == default)
            throw new ArgumentException("La fecha del gasto es obligatoria.");

        if (gasto.CondicionPago == CondicionPago.Credito && gasto.FechaVencimiento is null)
            throw new ReglaDeNegocioException("Un gasto a crédito exige fecha de vencimiento.");
        if (gasto.CondicionPago == CondicionPago.Contado && gasto.FechaVencimiento is not null)
            throw new ReglaDeNegocioException("Un gasto de contado no lleva fecha de vencimiento.");

        var proveedor = await _proveedores.ObtenerPorIdAsync(gasto.ProveedorId)
            ?? throw new EntidadNoEncontradaException($"Proveedor {gasto.ProveedorId} no encontrado.");
        if (!proveedor.Activo && (esAlta || original!.ProveedorId != gasto.ProveedorId))
            throw new ReglaDeNegocioException($"El proveedor '{proveedor.Nombre}' está dado de baja.");

        var fuente = await _fuentes.ObtenerPorIdAsync(gasto.FuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException(
                $"Fuente de financiamiento {gasto.FuenteFinanciamientoId} no encontrada.");
        if (!fuente.Activo && (esAlta || original!.FuenteFinanciamientoId != gasto.FuenteFinanciamientoId))
            throw new ReglaDeNegocioException($"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");

        var rubro = await _rubros.ObtenerPorIdAsync(gasto.RubroGastoId)
            ?? throw new EntidadNoEncontradaException($"Rubro de gasto {gasto.RubroGastoId} no encontrado.");
        if (!rubro.Activo && (esAlta || original!.RubroGastoId != gasto.RubroGastoId))
            throw new ReglaDeNegocioException($"El rubro '{rubro.Nombre}' está dado de baja.");

        if (gasto.LineaPoaId is null)
            return null;

        var linea = await _lineasPoa.ObtenerPorIdAsync(gasto.LineaPoaId.Value)
            ?? throw new EntidadNoEncontradaException($"Línea POA {gasto.LineaPoaId} no encontrada.");
        if (!linea.Activo && (esAlta || original!.LineaPoaId != gasto.LineaPoaId))
            throw new ReglaDeNegocioException($"La línea POA '{linea.Nombre}' está dada de baja.");

        return linea;
    }

    private async Task ValidarFacturaUnicaAsync(Gasto gasto)
    {
        if (string.IsNullOrWhiteSpace(gasto.NumeroFactura))
            return;

        var existente = await _repo.ObtenerPorProveedorYFacturaAsync(gasto.ProveedorId, gasto.NumeroFactura!);
        if (existente is not null && existente.Id != gasto.Id)
            throw new ReglaDeNegocioException(
                $"Ya existe la factura '{gasto.NumeroFactura}' para ese proveedor.");
    }

    /// <summary>
    /// Sobregiro POA (spec §10): la fuente sin asignación en la línea es regla DURA (409);
    /// superar el presupuesto asignado solo ADVIERTE — la app avisa, el humano decide.
    /// </summary>
    private async Task<string?> AdvertirSobregiroAsync(Gasto gasto, LineaPoa? linea, int? excluyendoGastoId)
    {
        if (linea is null)
            return null;

        var asignacion = linea.Asignaciones
            .FirstOrDefault(a => a.FuenteFinanciamientoId == gasto.FuenteFinanciamientoId)
            ?? throw new ReglaDeNegocioException(
                $"La línea POA '{linea.Nombre}' no tiene asignación presupuestal " +
                "para la fuente de financiamiento seleccionada.");

        var gastado = await _repo.TotalGastadoLineaFuenteAsync(
            linea.Id, gasto.FuenteFinanciamientoId, excluyendoGastoId);
        var restante = asignacion.Monto - gastado - gasto.MontoTotal;

        return restante >= 0
            ? null
            : $"Atención: la línea POA '{linea.Nombre}' queda sobregirada en {Math.Abs(restante):0.##} " +
              "para esa fuente de financiamiento. El gasto se registra igual.";
    }

    private async Task ValidarMovimientosAsync(IReadOnlyList<int> movimientoIds)
    {
        var movimientos = await _repo.ObtenerMovimientosAsync(movimientoIds);
        if (movimientos.Count != movimientoIds.Distinct().Count())
            throw new EntidadNoEncontradaException("Alguno de los movimientos de stock a asociar no existe.");
        if (movimientos.Any(m => m.Tipo != TipoMovimiento.Entrada))
            throw new ReglaDeNegocioException(
                "Solo se pueden asociar a una factura movimientos de ENTRADA de stock.");
        if (movimientos.Any(m => m.GastoId is not null))
            throw new ReglaDeNegocioException(
                "Alguno de los movimientos ya está asociado a otra factura.");
    }
}
```

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~GastoServiceTests"`
Expected: los 28 tests nuevos en verde.

- [ ] **Step 6: Suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: toda la suite verde (sin regresiones — en particular los tests de `AuthorizationService` existentes).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application tests/StockApp.Application.Tests
git commit -m "feat(finanzas): GastoService con reglas de negocio, permisos nuevos y auditoría"
```

---

### Task 4: Application — IngresoCajaService con auditoría

**Files:**
- Create: `src/StockApp.Application/Finanzas/IIngresoCajaService.cs`
- Create: `src/StockApp.Application/Finanzas/IngresoCajaService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/IngresoCajaServiceTests.cs`

**Interfaces:**
- Consumes: `IIngresoCajaRepository` (Task 2), `IFuenteFinanciamientoRepository`, `ICurrentSession`, `IAuthorizationService`, `IAuditLogger`, `AccionAuditada` 36–38, `Permisos.RegistrarIngresos`/`VerFinanzas` (Task 3).
- Produces:
  - `interface IIngresoCajaService`: `Task<int> AltaAsync(IngresoCaja ingreso)`, `Task ModificarAsync(IngresoCaja ingreso)`, `Task BajaLogicaAsync(int id)`, `Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()`

Reglas: mutaciones exigen `RegistrarIngresos`; el listado exige `VerFinanzas`. Concepto obligatorio, monto > 0, fecha obligatoria; la fuente debe existir y (en alta o si cambia) estar activa.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Application.Tests/Finanzas/IngresoCajaServiceTests.cs`:

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

public class IngresoCajaServiceTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static (IngresoCajaService svc,
                    Mock<IIngresoCajaRepository> repoMock,
                    Mock<IFuenteFinanciamientoRepository> fuentesMock,
                    Mock<IAuditLogger> auditMock)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var repo    = new Mock<IIngresoCajaRepository>();
        var fuentes = new Mock<IFuenteFinanciamientoRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthSvc>();
        var audit   = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual)
            .Returns(new StockApp.Application.Auth.UsuarioSesion(1, "usuario", rol, null));

        fuentes.Setup(f => f.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => new FuenteFinanciamiento { Id = id, Nombre = $"Fuente {id}", Activo = true });

        var svc = new IngresoCajaService(repo.Object, fuentes.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, fuentes, audit);
    }

    private static IngresoCaja IngresoValido() => new()
    {
        Fecha = Hoy, Concepto = "Partida mensual FIGM", FuenteFinanciamientoId = 1, Monto = 250000m,
    };

    [Fact]
    public async Task AltaAsync_ConceptoVacio_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var ingreso = IngresoValido();
        ingreso.Concepto = " ";

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(ingreso));
    }

    [Fact]
    public async Task AltaAsync_MontoNoPositivo_LanzaArgumentException()
    {
        var (svc, _, _, _) = Crear();
        var ingreso = IngresoValido();
        ingreso.Monto = 0m;

        await Assert.ThrowsAsync<ArgumentException>(() => svc.AltaAsync(ingreso));
    }

    [Fact]
    public async Task AltaAsync_FuenteInactiva_LanzaReglaDeNegocio()
    {
        var (svc, _, fuentes, _) = Crear();
        fuentes.Setup(f => f.ObtenerPorIdAsync(1))
            .ReturnsAsync(new FuenteFinanciamiento { Id = 1, Nombre = "Vieja", Activo = false });

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.AltaAsync(IngresoValido()));
    }

    [Fact]
    public async Task AltaAsync_Exitosa_RegistraAltaIngresoCaja()
    {
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.AgregarAsync(It.IsAny<IngresoCaja>())).ReturnsAsync(5);

        var id = await svc.AltaAsync(IngresoValido());

        Assert.Equal(5, id);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.AltaIngresoCaja, "IngresoCaja", 5,
            It.Is<string>(d => d.Contains("Partida mensual FIGM"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_Inexistente_LanzaEntidadNoEncontrada()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((IngresoCaja?)null);
        var ingreso = IngresoValido();
        ingreso.Id = 99;

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => svc.ModificarAsync(ingreso));
    }

    [Fact]
    public async Task ModificarAsync_CambiaConceptoYMonto_ActualizaYAudita()
    {
        var original = IngresoValido();
        original.Id = 1;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        var editado = IngresoValido();
        editado.Id = 1;
        editado.Concepto = "Multas junio";
        editado.Monto = 12000m;
        await svc.ModificarAsync(editado);

        repo.Verify(r => r.ActualizarAsync(It.Is<IngresoCaja>(i =>
            i.Concepto == "Multas junio" && i.Monto == 12000m)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.ModificacionIngresoCaja, "IngresoCaja", 1,
            It.Is<string>(d => d.Contains("Concepto") && d.Contains("Monto"))), Times.Once);
    }

    [Fact]
    public async Task ModificarAsync_SinCambios_NoActualizaNiAudita()
    {
        var original = IngresoValido();
        original.Id = 1;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(original);

        var editado = IngresoValido();
        editado.Id = 1;
        await svc.ModificarAsync(editado);

        repo.Verify(r => r.ActualizarAsync(It.IsAny<IngresoCaja>()), Times.Never);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), It.IsAny<AccionAuditada>(), It.IsAny<string>(),
            It.IsAny<int>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BajaLogicaAsync_ActivoFalse_RegistraBaja()
    {
        var ingreso = IngresoValido();
        ingreso.Id = 2;
        var (svc, repo, _, audit) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(ingreso);

        await svc.BajaLogicaAsync(2);

        repo.Verify(r => r.ActualizarAsync(It.Is<IngresoCaja>(i => !i.Activo)), Times.Once);
        audit.Verify(a => a.RegistrarAsync(
            It.IsAny<int>(), AccionAuditada.BajaIngresoCaja, "IngresoCaja", 2, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task BajaLogicaAsync_YaInactivo_LanzaReglaDeNegocio()
    {
        var ingreso = IngresoValido();
        ingreso.Id = 2;
        ingreso.Activo = false;
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ObtenerPorIdAsync(2)).ReturnsAsync(ingreso);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => svc.BajaLogicaAsync(2));
    }

    [Fact]
    public async Task ListarTodosAsync_DelegaAlRepo()
    {
        var (svc, repo, _, _) = Crear();
        repo.Setup(r => r.ListarTodosAsync()).ReturnsAsync(new List<IngresoCaja> { IngresoValido() });

        var result = await svc.ListarTodosAsync();

        Assert.Single(result);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoCajaServiceTests"`
Expected: FALLA la compilación con `CS0246` (`IngresoCajaService` no existe) — rojo confirmado.

- [ ] **Step 3: Interfaz e implementación**

`src/StockApp.Application/Finanzas/IIngresoCajaService.cs`:

```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de ingresos de caja (partidas mensuales, multas, préstamos, saldo inicial).
/// Mutaciones exigen RegistrarIngresos; el listado, VerFinanzas.
/// </summary>
public interface IIngresoCajaService
{
    Task<int> AltaAsync(IngresoCaja ingreso);
    Task ModificarAsync(IngresoCaja ingreso);
    Task BajaLogicaAsync(int id);
    Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync();
}
```

`src/StockApp.Application/Finanzas/IngresoCajaService.cs`:

```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

public class IngresoCajaService : IIngresoCajaService
{
    private readonly IIngresoCajaRepository          _repo;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ICurrentSession                 _session;
    private readonly IAuthorizationService           _auth;
    private readonly IAuditLogger                    _audit;

    public IngresoCajaService(
        IIngresoCajaRepository repo,
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

    private async Task ValidarAsync(IngresoCaja ingreso, IngresoCaja? original)
    {
        if (string.IsNullOrWhiteSpace(ingreso.Concepto))
            throw new ArgumentException("El concepto del ingreso es obligatorio.");
        if (ingreso.Monto <= 0)
            throw new ArgumentException("El monto del ingreso debe ser mayor a cero.");
        if (ingreso.Fecha == default)
            throw new ArgumentException("La fecha del ingreso es obligatoria.");

        var fuente = await _fuentes.ObtenerPorIdAsync(ingreso.FuenteFinanciamientoId)
            ?? throw new EntidadNoEncontradaException(
                $"Fuente de financiamiento {ingreso.FuenteFinanciamientoId} no encontrada.");
        var fuenteCambio = original is null
            || original.FuenteFinanciamientoId != ingreso.FuenteFinanciamientoId;
        if (!fuente.Activo && fuenteCambio)
            throw new ReglaDeNegocioException(
                $"La fuente de financiamiento '{fuente.Nombre}' está dada de baja.");
    }

    public async Task<int> AltaAsync(IngresoCaja ingreso)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarIngresos);
        await ValidarAsync(ingreso, original: null);

        var id = await _repo.AgregarAsync(ingreso);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaIngresoCaja, "IngresoCaja", id,
            $"Concepto: {ingreso.Concepto}; Monto: {ingreso.Monto}; Fecha: {ingreso.Fecha:yyyy-MM-dd}");

        return id;
    }

    public async Task ModificarAsync(IngresoCaja ingreso)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarIngresos);

        var original = await _repo.ObtenerPorIdAsync(ingreso.Id)
            ?? throw new EntidadNoEncontradaException($"Ingreso de caja {ingreso.Id} no encontrado.");
        if (!original.Activo)
            throw new ReglaDeNegocioException("No se puede modificar un ingreso dado de baja.");

        await ValidarAsync(ingreso, original);

        var cambios = new List<string>();
        if (original.Concepto != ingreso.Concepto)
            cambios.Add($"Concepto: {original.Concepto} → {ingreso.Concepto}");
        if (original.Fecha != ingreso.Fecha)
            cambios.Add($"Fecha: {original.Fecha:yyyy-MM-dd} → {ingreso.Fecha:yyyy-MM-dd}");
        if (original.Monto != ingreso.Monto)
            cambios.Add($"Monto: {original.Monto} → {ingreso.Monto}");
        if (original.FuenteFinanciamientoId != ingreso.FuenteFinanciamientoId)
            cambios.Add($"Fuente: {original.FuenteFinanciamientoId} → {ingreso.FuenteFinanciamientoId}");

        if (cambios.Count == 0)
            return;

        // Solo la FK, no la nav (mismo criterio que GastoService: el fixup de EF
        // daría vuelta el FK si la nav tracked se pone en null).
        original.Concepto               = ingreso.Concepto;
        original.Fecha                  = ingreso.Fecha;
        original.Monto                  = ingreso.Monto;
        original.FuenteFinanciamientoId = ingreso.FuenteFinanciamientoId;
        await _repo.ActualizarAsync(original);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.ModificacionIngresoCaja, "IngresoCaja", ingreso.Id,
            string.Join("; ", cambios));
    }

    public async Task BajaLogicaAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarIngresos);

        var ingreso = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Ingreso de caja {id} no encontrado.");
        if (!ingreso.Activo)
            throw new ReglaDeNegocioException($"El ingreso de caja {id} ya está dado de baja.");

        ingreso.Activo = false;
        await _repo.ActualizarAsync(ingreso);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaIngresoCaja, "IngresoCaja", id,
            $"Baja de '{ingreso.Concepto}' (monto {ingreso.Monto})");
    }

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return await _repo.ListarTodosAsync();
    }
}
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~IngresoCajaServiceTests"`
Expected: los 10 tests nuevos en verde.

- [ ] **Step 5: Suite completa de Application**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: toda la suite verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application tests/StockApp.Application.Tests
git commit -m "feat(finanzas): IngresoCajaService con validaciones y auditoría"
```

---

### Task 5: Api — GastosEndpoints (`/finanzas/gastos` + pagos + vínculo) con DI y matriz de tests

**Files:**
- Create: `src/StockApp.Api/Endpoints/GastosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (DI de repos/servicios de Fase 2 + `MapGastosEndpoints`)
- Test: `tests/StockApp.Api.Tests/GastosEndpointTests.cs`

**Interfaces:**
- Consumes: `IGastoService` (Task 3), `Permisos` (las policies HTTP se derivan solas de `Permisos.Todos`), `ApiTestBase`/`ApiFactory`/`DatosDePrueba` (fixtures existentes), `DomainExceptionHandler` (mapea 400/404/409 — los endpoints NO hacen try/catch).
- Produces (contratos wire):
  - `record PagoGastoDto(int Id, DateTime Fecha, decimal Monto, string? Nota, bool Activo)`
  - `record GastoDto(int Id, int ProveedorId, string? ProveedorNombre, string? NumeroFactura, string? NumeroOrden, string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal, int FuenteFinanciamientoId, string? FuenteNombre, int RubroGastoId, string? RubroNombre, int? LineaPoaId, string? LineaPoaNombre, CondicionPago CondicionPago, DateTime? FechaVencimiento, bool Activo, decimal TotalPagado, string Estado, List<PagoGastoDto> Pagos)` — `Estado` viene calculado por el servidor con `DateTime.UtcNow`, como cortesía para clientes; el desktop lo recalcula de los mismos datos.
  - `record CrearGastoRequest(...)` / `record ModificarGastoRequest(...)` / `record GastoGuardadoResponse(int Id, string? AdvertenciaSobregiro)`
  - `record RegistrarPagoRequest(DateTime Fecha, decimal Monto, string? Nota)` / `record PagoCreadoResponse(int Id)`
  - `record AsociarMovimientosRequest(List<int> MovimientoIds)`
  - Rutas: `GET /finanzas/gastos` (filtros query, `VerFinanzas`), `GET /finanzas/gastos/{id}` (`VerFinanzas`), `GET /finanzas/gastos/por-factura?proveedorId&numeroFactura` (`VerFinanzas`, 404 si no hay), `POST /finanzas/gastos` (`RegistrarGastos`, 201), `PUT /finanzas/gastos/{id}` (`RegistrarGastos`), `DELETE /finanzas/gastos/{id}` (anulación, `RegistrarGastos`), `POST /finanzas/gastos/{id}/pagos` (`RegistrarPagos`, 201), `DELETE /finanzas/gastos/{id}/pagos/{pagoId}` (`RegistrarPagos`), `POST /finanzas/gastos/{id}/movimientos` (`RegistrarGastos`).

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Api.Tests/GastosEndpointTests.cs`:

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

public class GastosEndpointTests : ApiTestBase
{
    public GastosEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Seed de los maestros que el gasto exige por FK + los DOS usuarios auditores:
    /// la auditoría escribe con el usuarioId del token (1 = Admin, 2 = Operador) y su
    /// FK Restrict a Usuarios exige que ambos existan.
    /// </summary>
    private async Task<(int proveedorId, int fuenteId, int rubroId)> SeedMaestrosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        if (!await ctx.Usuarios.AnyAsync())
        {
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
            await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        }

        var proveedor = new Proveedor { Nombre = $"Proveedor {Guid.NewGuid():N}" };
        var fuente    = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        var rubro     = new RubroGasto { Codigo = Random.Shared.Next(1, 1_000_000), Nombre = "Rubro api" };
        ctx.AddRange(proveedor, fuente, rubro);
        await ctx.SaveChangesAsync();
        return (proveedor.Id, fuente.Id, rubro.Id);
    }

    private static CrearGastoRequest RequestValido(
        int proveedorId, int fuenteId, int rubroId,
        CondicionPago condicion = CondicionPago.Contado, string? factura = null) => new(
        ProveedorId: proveedorId,
        NumeroFactura: factura,
        NumeroOrden: null,
        Detalle: "Gasto vía API",
        Destino: null,
        Fecha: DateTime.UtcNow,
        MontoTotal: 1500m,
        FuenteFinanciamientoId: fuenteId,
        RubroGastoId: rubroId,
        LineaPoaId: null,
        CondicionPago: condicion,
        FechaVencimiento: condicion == CondicionPago.Credito ? DateTime.UtcNow.AddDays(30) : null,
        MovimientoIds: null);

    [Fact]
    public async Task GetGastos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/gastos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/finanzas/gastos", RequestValido(1, 1, 1));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_Contado_Crea201ConPagoAutomatico()
    {
        // Spec Finanzas §9: RegistrarGastos lo tienen Admin Y Operador — no hay 403 por rol.
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "API-0001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        Assert.NotNull(creado);
        Assert.Null(creado!.AdvertenciaSobregiro);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.Include(g => g.Pagos)
            .SingleAsync(g => g.Id == creado.Id);
        Assert.Equal("API-0001", gasto.NumeroFactura);
        var pago = Assert.Single(gasto.Pagos);           // pago contado automático
        Assert.Equal(1500m, pago.Monto);
    }

    [Fact]
    public async Task PostGastos_CreditoSinVencimiento_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)
            with { FechaVencimiento = null };

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_MontoNoPositivo_Devuelve400()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId) with { MontoTotal = 0m };

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_FacturaDuplicada_Devuelve409()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId, factura: "DUP-01");

        Assert.Equal(HttpStatusCode.Created,
            (await client.PostAsJsonAsync("/finanzas/gastos", request)).StatusCode);
        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostGastos_SobregiroLineaPoa_Crea201ConAdvertencia()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        await using (var ctx = Factory.CrearContexto())
        {
            ctx.Add(new LineaPoa
            {
                Nombre = $"PRENSA {Guid.NewGuid():N}", Programa = "Com", Ejercicio = 2026,
                Asignaciones = { new AsignacionPresupuestal { FuenteFinanciamientoId = fuenteId, Monto = 1000m } },
            });
            await ctx.SaveChangesAsync();
        }
        int lineaId;
        await using (var ctx = Factory.CrearContexto())
            lineaId = await ctx.LineasPoa.OrderByDescending(l => l.Id).Select(l => l.Id).FirstAsync();

        var client = ClienteAutenticado(TokenAdmin());
        var request = RequestValido(proveedorId, fuenteId, rubroId) with { LineaPoaId = lineaId };  // 1500 > 1000

        var response = await client.PostAsJsonAsync("/finanzas/gastos", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);  // advierte pero NO bloquea
        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        Assert.NotNull(creado!.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task GetGastos_FiltraPorProveedor_YDevuelveEstadoCalculado()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId));  // contado ⇒ Pagada

        var response = await client.GetAsync($"/finanzas/gastos?proveedorId={proveedorId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var gastos = await response.Content.ReadFromJsonAsync<List<GastoDto>>();
        var gasto = Assert.Single(gastos!);
        Assert.Equal("Pagada", gasto.Estado);
        Assert.Equal(1500m, gasto.TotalPagado);
        Assert.NotNull(gasto.ProveedorNombre);
    }

    [Fact]
    public async Task GetGastoPorId_Inexistente_Devuelve404()
    {
        await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/gastos/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetGastoPorFactura_ExistenteEInexistente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/gastos",
            RequestValido(proveedorId, fuenteId, rubroId, factura: "BUSCA-01"));

        var ok = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=BUSCA-01");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var dto = await ok.Content.ReadFromJsonAsync<GastoDto>();
        Assert.Equal("BUSCA-01", dto!.NumeroFactura);

        var notFound = await client.GetAsync(
            $"/finanzas/gastos/por-factura?proveedorId={proveedorId}&numeroFactura=NO-EXISTE");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
    }

    [Fact]
    public async Task PostPagos_RegistraYRespetaSaldo()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var pago = await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/pagos",
            new RegistrarPagoRequest(DateTime.UtcNow, 1000m, "primer pago"));
        Assert.Equal(HttpStatusCode.Created, pago.StatusCode);

        // El saldo quedó en 500: pagar 600 debe dar 409 (no pagar más que el saldo)
        var excedido = await client.PostAsJsonAsync($"/finanzas/gastos/{creado.Id}/pagos",
            new RegistrarPagoRequest(DateTime.UtcNow, 600m, null));
        Assert.Equal(HttpStatusCode.Conflict, excedido.StatusCode);
    }

    [Fact]
    public async Task DeletePago_AnulaYElGastoVuelveAPendiente()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        var pago = await (await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/pagos",
                new RegistrarPagoRequest(DateTime.UtcNow, 1500m, null)))
            .Content.ReadFromJsonAsync<PagoCreadoResponse>();

        var response = await client.DeleteAsync($"/finanzas/gastos/{creado.Id}/pagos/{pago!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await (await client.GetAsync($"/finanzas/gastos/{creado.Id}"))
            .Content.ReadFromJsonAsync<GastoDto>();
        Assert.Equal("Pendiente", dto!.Estado);
        Assert.Equal(0m, dto.TotalPagado);
    }

    [Fact]
    public async Task DeleteGasto_ConPagosActivos409_SinPagosAnula()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var contado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId)))   // contado ⇒ pago activo
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var conPagos = await client.DeleteAsync($"/finanzas/gastos/{contado!.Id}");
        Assert.Equal(HttpStatusCode.Conflict, conPagos.StatusCode);

        var credito = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();
        var sinPagos = await client.DeleteAsync($"/finanzas/gastos/{credito!.Id}");
        Assert.Equal(HttpStatusCode.OK, sinPagos.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.False((await verificacion.Gastos.SingleAsync(g => g.Id == credito.Id)).Activo);
    }

    [Fact]
    public async Task PutGasto_Modifica200ConCambios()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.PutAsJsonAsync($"/finanzas/gastos/{creado!.Id}",
            new ModificarGastoRequest(
                ProveedorId: proveedorId, NumeroFactura: null, NumeroOrden: "OC-77",
                Detalle: "Gasto vía API (editado)", Destino: "Corralón",
                Fecha: DateTime.UtcNow, MontoTotal: 1800m,
                FuenteFinanciamientoId: fuenteId, RubroGastoId: rubroId, LineaPoaId: null,
                CondicionPago: CondicionPago.Credito, FechaVencimiento: DateTime.UtcNow.AddDays(60)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        var gasto = await verificacion.Gastos.SingleAsync(g => g.Id == creado.Id);
        Assert.Equal("Gasto vía API (editado)", gasto.Detalle);
        Assert.Equal(1800m, gasto.MontoTotal);
        Assert.Equal("OC-77", gasto.NumeroOrden);
    }

    [Fact]
    public async Task PostMovimientos_AsociaEntradasAlGasto()
    {
        var (proveedorId, fuenteId, rubroId) = await SeedMaestrosAsync();

        int movimientoId;
        await using (var ctx = Factory.CrearContexto())
        {
            var unidad = new UnidadMedida
            {
                Nombre = $"Unidad {Guid.NewGuid():N}", Abreviatura = Guid.NewGuid().ToString("N")[..8],
            };
            var usuario = await ctx.Usuarios.FirstAsync();
            ctx.Add(unidad);
            await ctx.SaveChangesAsync();
            var producto = new Producto
            {
                Codigo = Guid.NewGuid().ToString("N")[..12], Nombre = "Prod api", UnidadMedidaId = unidad.Id,
            };
            ctx.Add(producto);
            await ctx.SaveChangesAsync();
            var movimiento = new MovimientoStock
            {
                ProductoId = producto.Id, UsuarioId = usuario.Id,
                Tipo = TipoMovimiento.Entrada, Motivo = MotivoMovimiento.Compra,
                Cantidad = 3m, PrecioUnitario = 500m, Fecha = DateTime.UtcNow,
            };
            ctx.Add(movimiento);
            await ctx.SaveChangesAsync();
            movimientoId = movimiento.Id;
        }

        var client = ClienteAutenticado(TokenAdmin());
        var creado = await (await client.PostAsJsonAsync("/finanzas/gastos",
                RequestValido(proveedorId, fuenteId, rubroId, CondicionPago.Credito)))
            .Content.ReadFromJsonAsync<GastoGuardadoResponse>();

        var response = await client.PostAsJsonAsync($"/finanzas/gastos/{creado!.Id}/movimientos",
            new AsociarMovimientosRequest(new List<int> { movimientoId }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        var vinculado = await verificacion.MovimientosStock.SingleAsync(m => m.Id == movimientoId);
        Assert.Equal(creado.Id, vinculado.GastoId);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~GastosEndpointTests"`
Expected: FALLA la compilación con `CS0246` (`CrearGastoRequest`/`GastoGuardadoResponse` no existen) — rojo confirmado.

- [ ] **Step 3: Implementar los endpoints**

`src/StockApp.Api/Endpoints/GastosEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record PagoGastoDto(int Id, DateTime Fecha, decimal Monto, string? Nota, bool Activo);

public record GastoDto(
    int Id,
    int ProveedorId, string? ProveedorNombre,
    string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino,
    DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, string? FuenteNombre,
    int RubroGastoId, string? RubroNombre,
    int? LineaPoaId, string? LineaPoaNombre,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    bool Activo,
    decimal TotalPagado,
    string Estado,                       // calculado por el servidor (DateTime.UtcNow)
    List<PagoGastoDto> Pagos);

public record CrearGastoRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<int>? MovimientoIds);

public record ModificarGastoRequest(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento);

public record GastoGuardadoResponse(int Id, string? AdvertenciaSobregiro);
public record RegistrarPagoRequest(DateTime Fecha, decimal Monto, string? Nota);
public record PagoCreadoResponse(int Id);
public record AsociarMovimientosRequest(List<int> MovimientoIds);

public static class GastosEndpoints
{
    public static IEndpointRouteBuilder MapGastosEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/gastos");

        group.MapGet("/", async (
            DateTime? fechaDesde, DateTime? fechaHasta, int? proveedorId,
            int? fuenteFinanciamientoId, int? rubroGastoId, int? lineaPoaId,
            IGastoService gastos) =>
        {
            var filtro = new GastoFiltro(
                fechaDesde, fechaHasta, proveedorId, fuenteFinanciamientoId, rubroGastoId, lineaPoaId);
            return Results.Ok((await gastos.ListarAsync(filtro)).Select(ADto));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        group.MapGet("/{id:int}", async (int id, IGastoService gastos) =>
            Results.Ok(ADto(await gastos.ObtenerPorIdAsync(id))))
            .RequireAuthorization(Permisos.VerFinanzas);

        // Conciliación del vínculo stock: ¿ya existe la factura de este proveedor?
        group.MapGet("/por-factura", async (int proveedorId, string numeroFactura, IGastoService gastos) =>
        {
            var gasto = await gastos.ObtenerPorProveedorYFacturaAsync(proveedorId, numeroFactura);
            return gasto is null ? Results.NotFound() : Results.Ok(ADto(gasto));
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        group.MapPost("/", async (CrearGastoRequest request, IGastoService gastos) =>
        {
            var resultado = await gastos.AltaAsync(AEntidad(request), request.MovimientoIds);
            // Sin Location: no hay convención de Location en los POST del proyecto.
            return Results.Created((string?)null,
                new GastoGuardadoResponse(resultado.Id, resultado.AdvertenciaSobregiro));
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        group.MapPut("/{id:int}", async (int id, ModificarGastoRequest request, IGastoService gastos) =>
        {
            var gasto = AEntidad(new CrearGastoRequest(
                request.ProveedorId, request.NumeroFactura, request.NumeroOrden,
                request.Detalle, request.Destino, request.Fecha, request.MontoTotal,
                request.FuenteFinanciamientoId, request.RubroGastoId, request.LineaPoaId,
                request.CondicionPago, request.FechaVencimiento, null));
            gasto.Id = id;
            var resultado = await gastos.ModificarAsync(gasto);
            return Results.Ok(new GastoGuardadoResponse(resultado.Id, resultado.AdvertenciaSobregiro));
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        group.MapDelete("/{id:int}", async (int id, IGastoService gastos) =>
        {
            await gastos.AnularAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        group.MapPost("/{id:int}/pagos", async (int id, RegistrarPagoRequest request, IGastoService gastos) =>
        {
            var pagoId = await gastos.RegistrarPagoAsync(new PagoGasto
            {
                GastoId = id, Fecha = request.Fecha, Monto = request.Monto, Nota = request.Nota,
            });
            return Results.Created((string?)null, new PagoCreadoResponse(pagoId));
        })
        .RequireAuthorization(Permisos.RegistrarPagos);

        group.MapDelete("/{id:int}/pagos/{pagoId:int}", async (int id, int pagoId, IGastoService gastos) =>
        {
            await gastos.AnularPagoAsync(id, pagoId);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarPagos);

        group.MapPost("/{id:int}/movimientos", async (int id, AsociarMovimientosRequest request, IGastoService gastos) =>
        {
            await gastos.AsociarMovimientosAsync(id, request.MovimientoIds);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarGastos);

        return app;
    }

    private static Gasto AEntidad(CrearGastoRequest r) => new()
    {
        ProveedorId = r.ProveedorId,
        NumeroFactura = string.IsNullOrWhiteSpace(r.NumeroFactura) ? null : r.NumeroFactura.Trim(),
        NumeroOrden = string.IsNullOrWhiteSpace(r.NumeroOrden) ? null : r.NumeroOrden.Trim(),
        Detalle = r.Detalle,
        Destino = r.Destino,
        Fecha = r.Fecha,
        MontoTotal = r.MontoTotal,
        FuenteFinanciamientoId = r.FuenteFinanciamientoId,
        RubroGastoId = r.RubroGastoId,
        LineaPoaId = r.LineaPoaId,
        CondicionPago = r.CondicionPago,
        FechaVencimiento = r.FechaVencimiento,
    };

    private static GastoDto ADto(Gasto g) => new(
        g.Id,
        g.ProveedorId, g.Proveedor?.Nombre,
        g.NumeroFactura, g.NumeroOrden,
        g.Detalle, g.Destino,
        g.Fecha, g.MontoTotal,
        g.FuenteFinanciamientoId, g.FuenteFinanciamiento?.Nombre,
        g.RubroGastoId, g.RubroGasto?.Nombre,
        g.LineaPoaId, g.LineaPoa?.Nombre,
        g.CondicionPago, g.FechaVencimiento,
        g.Activo,
        g.TotalPagado,
        g.CalcularEstado(DateTime.UtcNow).ToString(),
        g.Pagos.OrderBy(p => p.Fecha).ThenBy(p => p.Id)
            .Select(p => new PagoGastoDto(p.Id, p.Fecha, p.Monto, p.Nota, p.Activo))
            .ToList());
}
```

- [ ] **Step 4: Registrar DI y mapear los endpoints en Program.cs**

En `src/StockApp.Api/Program.cs`, después del bloque `// Finanzas — Fase 1: maestros (...)`:

```csharp
// Finanzas — Fase 2: gastos, pagos e ingresos de caja
builder.Services.AddScoped<IGastoRepository, GastoRepository>();
builder.Services.AddScoped<IGastoService, GastoService>();
builder.Services.AddScoped<IIngresoCajaRepository, IngresoCajaRepository>();
builder.Services.AddScoped<IIngresoCajaService, IngresoCajaService>();
```

Y después de `app.MapLineasPoaEndpoints();`:

```csharp
app.MapGastosEndpoints();
```

(El mapeo de `MapIngresosCajaEndpoints` se agrega en la Task 6; los registros DI de ingresos ya quedan hechos acá para no tocar dos veces el mismo bloque.)

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~GastosEndpointTests"`
Expected: los 15 tests nuevos en verde (requiere Docker).

- [ ] **Step 6: Suite completa de Api**

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: toda la suite verde.

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(finanzas): endpoints /finanzas/gastos con pagos, vínculo stock y matriz 401/400/404/409"
```

---

### Task 6: Api — IngresosCajaEndpoints (`/finanzas/ingresos`)

**Files:**
- Create: `src/StockApp.Api/Endpoints/IngresosCajaEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (`MapIngresosCajaEndpoints` — el DI quedó hecho en Task 5)
- Test: `tests/StockApp.Api.Tests/IngresosCajaEndpointTests.cs`

**Interfaces:**
- Consumes: `IIngresoCajaService` (Task 4).
- Produces:
  - `record IngresoCajaDto(int Id, DateTime Fecha, string Concepto, int FuenteFinanciamientoId, string? FuenteNombre, decimal Monto, bool Activo)`
  - `record CrearIngresoCajaRequest(DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto)` / `record ModificarIngresoCajaRequest(...)` (mismos campos)
  - Rutas: `GET /finanzas/ingresos` (`VerFinanzas`), `POST` (201, `RegistrarIngresos`), `PUT /{id}` y `DELETE /{id}` (`RegistrarIngresos`).

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Api.Tests/IngresosCajaEndpointTests.cs`:

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

public class IngresosCajaEndpointTests : ApiTestBase
{
    public IngresosCajaEndpointTests(ApiFactory factory) : base(factory) { }

    private string TokenAdmin() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);

    private string TokenOperador() =>
        Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(2, RolUsuario.Operador);

    private HttpClient ClienteAutenticado(string token)
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private async Task<int> SeedFuenteAsync()
    {
        await using var ctx = Factory.CrearContexto();
        // Ambos usuarios auditores (1 = Admin, 2 = Operador): la auditoría escribe con
        // el usuarioId del token y su FK Restrict exige que existan.
        if (!await ctx.Usuarios.AnyAsync())
        {
            await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
            await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
        }
        var fuente = new FuenteFinanciamiento { Nombre = $"Fuente {Guid.NewGuid():N}" };
        ctx.Add(fuente);
        await ctx.SaveChangesAsync();
        return fuente.Id;
    }

    [Fact]
    public async Task GetIngresos_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient().GetAsync("/finanzas/ingresos");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostIngresos_ConTokenOperador_Crea201()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Partida FIGM julio", fuenteId, 250000m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        Assert.True(await verificacion.IngresosCaja.AnyAsync(i => i.Concepto == "Partida FIGM julio"));
    }

    [Fact]
    public async Task PostIngresos_MontoNoPositivo_Devuelve400()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Inválido", fuenteId, 0m));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetIngresos_DevuelveConNombreDeFuente()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenOperador());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Multas junio", fuenteId, 12000m));

        var response = await client.GetAsync("/finanzas/ingresos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ingresos = await response.Content.ReadFromJsonAsync<List<IngresoCajaDto>>();
        var ingreso = ingresos!.First(i => i.Concepto == "Multas junio");
        Assert.NotNull(ingreso.FuenteNombre);
        Assert.Equal(12000m, ingreso.Monto);
    }

    [Fact]
    public async Task PutIngresos_Modifica200()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Original", fuenteId, 100m));
        await using var ctx = Factory.CrearContexto();
        var id = await ctx.IngresosCaja.Where(i => i.Concepto == "Original")
            .Select(i => i.Id).SingleAsync();

        var response = await client.PutAsJsonAsync($"/finanzas/ingresos/{id}",
            new ModificarIngresoCajaRequest(DateTime.UtcNow, "Editado", fuenteId, 200m));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificacion = Factory.CrearContexto();
        var ingreso = await verificacion.IngresosCaja.SingleAsync(i => i.Id == id);
        Assert.Equal("Editado", ingreso.Concepto);
        Assert.Equal(200m, ingreso.Monto);
    }

    [Fact]
    public async Task DeleteIngresos_HaceBajaLogica_YRepetido409()
    {
        var fuenteId = await SeedFuenteAsync();
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/ingresos",
            new CrearIngresoCajaRequest(DateTime.UtcNow, "Para baja", fuenteId, 100m));
        await using var ctx = Factory.CrearContexto();
        var id = await ctx.IngresosCaja.Where(i => i.Concepto == "Para baja")
            .Select(i => i.Id).SingleAsync();

        var primera = await client.DeleteAsync($"/finanzas/ingresos/{id}");
        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);

        await using var verificacion = Factory.CrearContexto();
        Assert.False((await verificacion.IngresosCaja.SingleAsync(i => i.Id == id)).Activo);

        var segunda = await client.DeleteAsync($"/finanzas/ingresos/{id}");
        Assert.Equal(HttpStatusCode.Conflict, segunda.StatusCode);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~IngresosCajaEndpointTests"`
Expected: FALLA la compilación con `CS0246` (`CrearIngresoCajaRequest` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los endpoints**

`src/StockApp.Api/Endpoints/IngresosCajaEndpoints.cs`:

```csharp
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.Api.Endpoints;

public record IngresoCajaDto(
    int Id, DateTime Fecha, string Concepto,
    int FuenteFinanciamientoId, string? FuenteNombre,
    decimal Monto, bool Activo);

public record CrearIngresoCajaRequest(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto);

public record ModificarIngresoCajaRequest(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto);

public static class IngresosCajaEndpoints
{
    public static IEndpointRouteBuilder MapIngresosCajaEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/finanzas/ingresos");

        group.MapGet("/", async (IIngresoCajaService ingresos) =>
            Results.Ok((await ingresos.ListarTodosAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.VerFinanzas);

        group.MapPost("/", async (CrearIngresoCajaRequest request, IIngresoCajaService ingresos) =>
        {
            var id = await ingresos.AltaAsync(new IngresoCaja
            {
                Fecha = request.Fecha,
                Concepto = request.Concepto,
                FuenteFinanciamientoId = request.FuenteFinanciamientoId,
                Monto = request.Monto,
            });
            return Results.Created((string?)null, new { id });
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        group.MapPut("/{id:int}", async (int id, ModificarIngresoCajaRequest request, IIngresoCajaService ingresos) =>
        {
            await ingresos.ModificarAsync(new IngresoCaja
            {
                Id = id,
                Fecha = request.Fecha,
                Concepto = request.Concepto,
                FuenteFinanciamientoId = request.FuenteFinanciamientoId,
                Monto = request.Monto,
            });
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        group.MapDelete("/{id:int}", async (int id, IIngresoCajaService ingresos) =>
        {
            await ingresos.BajaLogicaAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarIngresos);

        return app;
    }

    private static IngresoCajaDto ADto(IngresoCaja i) => new(
        i.Id, i.Fecha, i.Concepto,
        i.FuenteFinanciamientoId, i.FuenteFinanciamiento?.Nombre,
        i.Monto, i.Activo);
}
```

En `src/StockApp.Api/Program.cs`, después de `app.MapGastosEndpoints();`:

```csharp
app.MapIngresosCajaEndpoints();
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~IngresosCajaEndpointTests"`
Expected: los 6 tests nuevos en verde.

- [ ] **Step 5: Suite completa de Api**

Run: `dotnet test tests/StockApp.Api.Tests`
Expected: toda la suite verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Api tests/StockApp.Api.Tests
git commit -m "feat(finanzas): endpoints /finanzas/ingresos con ABM y baja lógica"
```

---

### Task 7: ApiClient — GastoApiClient e IngresoCajaApiClient

**Files:**
- Create: `src/StockApp.ApiClient/GastoApiClient.cs`
- Create: `src/StockApp.ApiClient/IngresoCajaApiClient.cs`
- Test: `tests/StockApp.ApiClient.Tests/GastoApiClientTests.cs`
- Test: `tests/StockApp.ApiClient.Tests/IngresoCajaApiClientTests.cs`

**Interfaces:**
- Consumes: `IGastoService` / `IIngresoCajaService` (las MISMAS interfaces de Application — los VMs no distinguen), `ApiErrores` / `ApiQuery` / `IdCreado` (helpers existentes), `FakeHttpHandler` / `TestHttp` (infra de tests existente).
- Produces: `GastoApiClient : IGastoService` contra `/finanzas/gastos`; `IngresoCajaApiClient : IIngresoCajaService` contra `/finanzas/ingresos`. Los wire-records materializan los nombres de proveedor/fuente/rubro/línea en las navs para que las grillas del desktop los muestren sin otra llamada (mismo criterio que `LineaPoaApiClient`).

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.ApiClient.Tests/GastoApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class GastoApiClientTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    private static object GastoJson(int id = 1, string estado = "Pendiente", bool activo = true) => new
    {
        id,
        proveedorId = 3,
        proveedorNombre = "Barraca X",
        numeroFactura = "A-0001",
        numeroOrden = (string?)null,
        detalle = "Materiales",
        destino = (string?)null,
        fecha = Hoy,
        montoTotal = 1000m,
        fuenteFinanciamientoId = 2,
        fuenteNombre = "Literal B",
        rubroGastoId = 4,
        rubroNombre = "Materiales",
        lineaPoaId = (int?)null,
        lineaPoaNombre = (string?)null,
        condicionPago = 1,             // Credito
        fechaVencimiento = Hoy.AddDays(30),
        activo,
        totalPagado = 0m,
        estado,
        pagos = new[]
        {
            new { id = 9, fecha = Hoy, monto = 0m, nota = (string?)null, activo = true },
        },
    };

    private static Gasto GastoEntidad() => new()
    {
        ProveedorId = 3,
        NumeroFactura = "A-0001",
        Detalle = "Materiales",
        Fecha = Hoy,
        MontoTotal = 1000m,
        FuenteFinanciamientoId = 2,
        RubroGastoId = 4,
        CondicionPago = CondicionPago.Credito,
        FechaVencimiento = Hoy.AddDays(30),
    };

    [Fact]
    public async Task Listar_GETConFiltros_MapeaEntidadesConNavsYPagos()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[] { GastoJson() }));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var gastos = await client.ListarAsync(new GastoFiltro(
            FechaDesde: Hoy.AddDays(-30), ProveedorId: 3));

        Assert.Equal(HttpMethod.Get, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("fechaDesde=", fake.UltimaRequest.RequestUri.Query);
        Assert.Contains("proveedorId=3", fake.UltimaRequest.RequestUri.Query);

        var gasto = Assert.Single(gastos);
        Assert.Equal("Barraca X", gasto.Proveedor!.Nombre);
        Assert.Equal("Literal B", gasto.FuenteFinanciamiento!.Nombre);
        Assert.Equal(CondicionPago.Credito, gasto.CondicionPago);
        Assert.Single(gasto.Pagos);
    }

    [Fact]
    public async Task Listar_SinFiltros_NoAgregaQuery()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(Array.Empty<object>()));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.ListarAsync(new GastoFiltro());

        Assert.Equal(string.Empty, fake.UltimaRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Alta_POSTConMovimientos_DevuelveIdYAdvertencia()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 7, advertenciaSobregiro = "Atención: sobregiro" }, HttpStatusCode.Created));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AltaAsync(GastoEntidad(), new[] { 40, 41 });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"movimientoIds\":[40,41]", fake.UltimoBody);
        Assert.Equal(7, resultado.Id);
        Assert.Equal("Atención: sobregiro", resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(
            new { id = 5, advertenciaSobregiro = (string?)null }));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));
        var gasto = GastoEntidad();
        gasto.Id = 5;

        var resultado = await client.ModificarAsync(gasto);

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/5", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Null(resultado.AdvertenciaSobregiro);
    }

    [Fact]
    public async Task Anular_DELETEGastos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task RegistrarPago_POSTPagos_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 21 }, HttpStatusCode.Created));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var pagoId = await client.RegistrarPagoAsync(new PagoGasto
        {
            GastoId = 5, Fecha = Hoy, Monto = 300m, Nota = "parcial",
        });

        Assert.Equal("/finanzas/gastos/5/pagos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"monto\":300", fake.UltimoBody);
        Assert.Equal(21, pagoId);
    }

    [Fact]
    public async Task AnularPago_DELETEPagos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AnularPagoAsync(5, 21);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/gastos/5/pagos/21", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AsociarMovimientos_POSTMovimientos()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        await client.AsociarMovimientosAsync(5, new[] { 40 });

        Assert.Equal("/finanzas/gastos/5/movimientos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"movimientoIds\":[40]", fake.UltimoBody);
    }

    [Fact]
    public async Task ObtenerPorProveedorYFactura_404_DevuelveNull()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "No existe."));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var gasto = await client.ObtenerPorProveedorYFacturaAsync(3, "NO-EXISTE");

        Assert.Null(gasto);
        Assert.Contains("/finanzas/gastos/por-factura", fake.UltimaRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Alta_409_LanzaReglaDeNegocioConElDetail()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "Ya existe la factura 'A-0001' para ese proveedor."));
        var client = new GastoApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => client.AltaAsync(GastoEntidad()));

        Assert.Equal("Ya existe la factura 'A-0001' para ese proveedor.", ex.Message);
    }
}
```

`tests/StockApp.ApiClient.Tests/IngresoCajaApiClientTests.cs`:

```csharp
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient.Tests;

public class IngresoCajaApiClientTests
{
    private static readonly DateTime Hoy = new(2026, 7, 16, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ListarTodos_GETFinanzasIngresos_MapeaConFuenteNavegable()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, fecha = Hoy, concepto = "Partida FIGM",
                fuenteFinanciamientoId = 2, fuenteNombre = "Literal B",
                monto = 250000m, activo = true,
            },
        }));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        var ingresos = await client.ListarTodosAsync();

        Assert.Equal("/finanzas/ingresos", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        var ingreso = Assert.Single(ingresos);
        Assert.Equal("Partida FIGM", ingreso.Concepto);
        Assert.Equal("Literal B", ingreso.FuenteFinanciamiento!.Nombre);
    }

    [Fact]
    public async Task Alta_POSTFinanzasIngresos_DevuelveElId()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.AltaAsync(new IngresoCaja
        {
            Fecha = Hoy, Concepto = "Multas", FuenteFinanciamientoId = 2, Monto = 12000m,
        });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Contains("\"concepto\":\"Multas\"", fake.UltimoBody);
        Assert.DoesNotContain("\"id\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task Modificar_PUTConIdDeRuta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await client.ModificarAsync(new IngresoCaja
        {
            Id = 3, Fecha = Hoy, Concepto = "Editado", FuenteFinanciamientoId = 2, Monto = 100m,
        });

        Assert.Equal(HttpMethod.Put, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/ingresos/3", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_DELETEFinanzasIngresosId()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await client.BajaLogicaAsync(4);

        Assert.Equal(HttpMethod.Delete, fake.UltimaRequest!.Method);
        Assert.Equal("/finanzas/ingresos/4", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task Baja_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.NotFound, "Ingreso de caja 99 no encontrado."));
        var client = new IngresoCajaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.BajaLogicaAsync(99));
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~GastoApiClient|FullyQualifiedName~IngresoCajaApiClient"`
Expected: FALLA la compilación con `CS0246` (`GastoApiClient` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los ApiClients**

`src/StockApp.ApiClient/GastoApiClient.cs`:

```csharp
using System.Globalization;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.ApiClient;

internal sealed record PagoGastoWire(int Id, DateTime Fecha, decimal Monto, string? Nota, bool Activo);

internal sealed record GastoWire(
    int Id,
    int ProveedorId, string? ProveedorNombre,
    string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino,
    DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, string? FuenteNombre,
    int RubroGastoId, string? RubroNombre,
    int? LineaPoaId, string? LineaPoaNombre,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    bool Activo, decimal TotalPagado, string Estado,
    List<PagoGastoWire> Pagos);

internal sealed record GastoBody(
    int ProveedorId, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateTime Fecha, decimal MontoTotal,
    int FuenteFinanciamientoId, int RubroGastoId, int? LineaPoaId,
    CondicionPago CondicionPago, DateTime? FechaVencimiento,
    List<int>? MovimientoIds);

internal sealed record GastoGuardadoWire(int Id, string? AdvertenciaSobregiro);
internal sealed record RegistrarPagoBody(DateTime Fecha, decimal Monto, string? Nota);
internal sealed record AsociarMovimientosBody(List<int> MovimientoIds);

/// <summary>
/// IGastoService contra /finanzas/gastos. Los nombres de proveedor/fuente/rubro/línea
/// vuelven materializados en las navs (para las grillas, sin otra llamada); el estado
/// lo recalcula la entidad en el cliente a partir de los mismos datos.
/// </summary>
public sealed class GastoApiClient : IGastoService
{
    private readonly HttpClient _http;

    public GastoApiClient(HttpClient http) => _http = http;

    public async Task<ResultadoGastoDto> AltaAsync(Gasto gasto, IReadOnlyList<int>? movimientoIds = null)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/gastos", ABody(gasto, movimientoIds)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<GastoGuardadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el gasto.");
        return new ResultadoGastoDto(creado.Id, creado.AdvertenciaSobregiro);
    }

    public async Task<ResultadoGastoDto> ModificarAsync(Gasto gasto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/gastos/{gasto.Id}", ABody(gasto, movimientoIds: null)));
        await ApiErrores.AsegurarExitoAsync(response);

        var guardado = await response.Content.ReadFromJsonAsync<GastoGuardadoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al modificar el gasto.");
        return new ResultadoGastoDto(guardado.Id, guardado.AdvertenciaSobregiro);
    }

    public async Task AnularAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/gastos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<Gasto> ObtenerPorIdAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/gastos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dto = await response.Content.ReadFromJsonAsync<GastoWire>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al obtener el gasto.");
        return AEntidad(dto);
    }

    public async Task<Gasto?> ObtenerPorProveedorYFacturaAsync(int proveedorId, string numeroFactura)
    {
        var query = ApiQuery.Construir(
            ("proveedorId", proveedorId.ToString(CultureInfo.InvariantCulture)),
            ("numeroFactura", numeroFactura));
        try
        {
            var response = await ApiErrores.EnviarAsync(() =>
                _http.GetAsync("finanzas/gastos/por-factura" + query));
            await ApiErrores.AsegurarExitoAsync(response);

            var dto = await response.Content.ReadFromJsonAsync<GastoWire>();
            return dto is null ? null : AEntidad(dto);
        }
        catch (EntidadNoEncontradaException)
        {
            return null;  // 404 = no existe la factura: contrato de la interfaz (null)
        }
    }

    public async Task<IReadOnlyList<Gasto>> ListarAsync(GastoFiltro filtro)
    {
        var query = ApiQuery.Construir(
            ("fechaDesde", ApiQuery.Fecha(filtro.FechaDesde)),
            ("fechaHasta", ApiQuery.Fecha(filtro.FechaHasta)),
            ("proveedorId", filtro.ProveedorId?.ToString(CultureInfo.InvariantCulture)),
            ("fuenteFinanciamientoId", filtro.FuenteFinanciamientoId?.ToString(CultureInfo.InvariantCulture)),
            ("rubroGastoId", filtro.RubroGastoId?.ToString(CultureInfo.InvariantCulture)),
            ("lineaPoaId", filtro.LineaPoaId?.ToString(CultureInfo.InvariantCulture)));

        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/gastos" + query));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<GastoWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public async Task<int> RegistrarPagoAsync(PagoGasto pago)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"finanzas/gastos/{pago.GastoId}/pagos",
                new RegistrarPagoBody(pago.Fecha, pago.Monto, pago.Nota)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al registrar el pago.");
        return creado.Id;
    }

    public async Task AnularPagoAsync(int gastoId, int pagoId)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.DeleteAsync($"finanzas/gastos/{gastoId}/pagos/{pagoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AsociarMovimientosAsync(int gastoId, IReadOnlyList<int> movimientoIds)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"finanzas/gastos/{gastoId}/movimientos",
                new AsociarMovimientosBody(movimientoIds.ToList())));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static GastoBody ABody(Gasto gasto, IReadOnlyList<int>? movimientoIds) => new(
        gasto.ProveedorId, gasto.NumeroFactura, gasto.NumeroOrden,
        gasto.Detalle, gasto.Destino, gasto.Fecha, gasto.MontoTotal,
        gasto.FuenteFinanciamientoId, gasto.RubroGastoId, gasto.LineaPoaId,
        gasto.CondicionPago, gasto.FechaVencimiento,
        movimientoIds?.ToList());

    private static Gasto AEntidad(GastoWire dto) => new()
    {
        Id = dto.Id,
        ProveedorId = dto.ProveedorId,
        Proveedor = dto.ProveedorNombre is null
            ? null : new Proveedor { Id = dto.ProveedorId, Nombre = dto.ProveedorNombre },
        NumeroFactura = dto.NumeroFactura,
        NumeroOrden = dto.NumeroOrden,
        Detalle = dto.Detalle,
        Destino = dto.Destino,
        Fecha = dto.Fecha,
        MontoTotal = dto.MontoTotal,
        FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
        FuenteFinanciamiento = dto.FuenteNombre is null
            ? null : new FuenteFinanciamiento { Id = dto.FuenteFinanciamientoId, Nombre = dto.FuenteNombre },
        RubroGastoId = dto.RubroGastoId,
        RubroGasto = dto.RubroNombre is null
            ? null : new RubroGasto { Id = dto.RubroGastoId, Nombre = dto.RubroNombre },
        LineaPoaId = dto.LineaPoaId,
        LineaPoa = dto.LineaPoaId is null || dto.LineaPoaNombre is null
            ? null : new LineaPoa { Id = dto.LineaPoaId.Value, Nombre = dto.LineaPoaNombre },
        CondicionPago = dto.CondicionPago,
        FechaVencimiento = dto.FechaVencimiento,
        Activo = dto.Activo,
        Pagos = dto.Pagos.Select(p => new PagoGasto
        {
            Id = p.Id, GastoId = dto.Id, Fecha = p.Fecha, Monto = p.Monto, Nota = p.Nota, Activo = p.Activo,
        }).ToList(),
    };
}
```

`src/StockApp.ApiClient/IngresoCajaApiClient.cs`:

```csharp
using System.Net.Http.Json;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;

namespace StockApp.ApiClient;

internal sealed record IngresoCajaWire(
    int Id, DateTime Fecha, string Concepto,
    int FuenteFinanciamientoId, string? FuenteNombre,
    decimal Monto, bool Activo);

internal sealed record IngresoCajaBody(
    DateTime Fecha, string Concepto, int FuenteFinanciamientoId, decimal Monto);

/// <summary>IIngresoCajaService contra /finanzas/ingresos.</summary>
public sealed class IngresoCajaApiClient : IIngresoCajaService
{
    private readonly HttpClient _http;

    public IngresoCajaApiClient(HttpClient http) => _http = http;

    public async Task<int> AltaAsync(IngresoCaja ingreso)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/ingresos", ABody(ingreso)));
        await ApiErrores.AsegurarExitoAsync(response);

        var creado = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear el ingreso.");
        return creado.Id;
    }

    public async Task ModificarAsync(IngresoCaja ingreso)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PutAsJsonAsync($"finanzas/ingresos/{ingreso.Id}", ABody(ingreso)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task BajaLogicaAsync(int id)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/ingresos/{id}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task<IReadOnlyList<IngresoCaja>> ListarTodosAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/ingresos"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<IngresoCajaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    private static IngresoCajaBody ABody(IngresoCaja i) => new(
        i.Fecha, i.Concepto, i.FuenteFinanciamientoId, i.Monto);

    private static IngresoCaja AEntidad(IngresoCajaWire dto) => new()
    {
        Id = dto.Id,
        Fecha = dto.Fecha,
        Concepto = dto.Concepto,
        FuenteFinanciamientoId = dto.FuenteFinanciamientoId,
        FuenteFinanciamiento = dto.FuenteNombre is null
            ? null
            : new FuenteFinanciamiento { Id = dto.FuenteFinanciamientoId, Nombre = dto.FuenteNombre },
        Monto = dto.Monto,
        Activo = dto.Activo,
    };
}
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~GastoApiClient|FullyQualifiedName~IngresoCajaApiClient"`
Expected: los 15 tests nuevos en verde.

- [ ] **Step 5: Suite completa de ApiClient**

Run: `dotnet test tests/StockApp.ApiClient.Tests`
Expected: toda la suite verde.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.ApiClient tests/StockApp.ApiClient.Tests
git commit -m "feat(finanzas): ApiClients de gastos e ingresos de caja con mapeos wire"
```

---

### Task 8: Presentation — ViewModels de "Gastos y facturas" (grilla + formulario + pagos)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs`

**Interfaces:**
- Consumes: `IGastoService`, `IIngresoCajaService` (no acá — Task 9), `IProveedorService.ListarTodosAsync()`, `IFuenteFinanciamientoService.ListarActivasAsync()`, `IRubroGastoService.ListarActivosAsync()`, `ILineaPoaService.ListarActivasAsync()`, `INavigationService`, `IConfirmacionService` (`PreguntarAsync`/`InformarAsync`), `ICsvExporter.Exportar<T>(items, columnOrder)`, `IServicioGuardadoArchivo.GuardarTextoAsync(contenido, nombreSugerido)`.
- Produces:
  - `class GastoFila` — fila de solo lectura para el DataGrid y el CSV (`Fecha`, `ProveedorNombre`, `NumeroFactura`, `Detalle`, `FuenteNombre`, `RubroNombre`, `LineaPoaNombre`, `MontoTotal`, `TotalPagado`, `Saldo`, `Estado`, `Gasto` subyacente).
  - `GastosViewModel`: filtros (fechas, proveedor, fuente, rubro, línea POA, estado — estado se filtra EN MEMORIA porque es calculado), `CargarAsync`, `FiltrarCommand`, `LimpiarFiltrosCommand`, `NuevoCommand`, `EditarCommand`, `PagosCommand`, `AnularCommand`, `ExportarCsvCommand`.
  - `GastoFormViewModel`: `CargarParaEditar(Gasto)`, `CargarDesdeEntrada(int movimientoId, decimal montoSugerido)` (lo usa la Task 10), `InicializarAsync`, `GuardarCommand`, `CancelarCommand`. Montos con cultura FIJA es-UY.
  - `PagosGastoViewModel`: `CargarParaGasto(Gasto)`, lista de pagos + `RegistrarPagoCommand` + `AnularPagoCommand` + `VolverCommand`.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastosViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class GastosViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoDe(int id, string detalle, bool pagado = false, bool activo = true)
    {
        var gasto = new Gasto
        {
            Id = id,
            ProveedorId = 1,
            Proveedor = new Proveedor { Id = 1, Nombre = "Barraca X" },
            Detalle = detalle,
            Fecha = Hoy,
            MontoTotal = 1000m,
            FuenteFinanciamientoId = 2,
            RubroGastoId = 3,
            CondicionPago = CondicionPago.Credito,
            FechaVencimiento = Hoy.AddDays(30),
            Activo = activo,
        };
        if (pagado)
            gasto.Pagos.Add(new PagoGasto { GastoId = id, Fecha = Hoy, Monto = 1000m });
        return gasto;
    }

    private static (GastosViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<Gasto>? gastos = null)
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ListarAsync(It.IsAny<GastoFiltro>()))
            .ReturnsAsync(gastos ?? new List<Gasto>());

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>());

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        var csv = new Mock<ICsvExporter>();
        csv.Setup(c => c.Exportar(It.IsAny<IEnumerable<GastoFila>>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns("csv");
        var guardado = new Mock<IServicioGuardadoArchivo>();
        guardado.Setup(g => g.GuardarTextoAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

        var vm = new GastosViewModel(
            svc.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object, csv.Object, guardado.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaFilasConEstadoCalculado()
    {
        var (vm, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal("Pendiente", vm.Filas[0].Estado);
        Assert.Equal("Pagada", vm.Filas[1].Estado);
        Assert.Equal("Barraca X", vm.Filas[0].ProveedorNombre);
    }

    [Fact]
    public async Task FiltroDeEstado_FiltraEnMemoria()
    {
        var (vm, _, _, _) = Crear(new List<Gasto>
        {
            GastoDe(1, "Pendiente de pago"),
            GastoDe(2, "Ya pagado", pagado: true),
        });
        await vm.CargarAsync();

        vm.EstadoSeleccionado = "Pagada";
        await vm.FiltrarCommand.ExecuteAsync(null);

        var fila = Assert.Single(vm.Filas);
        Assert.Equal("Pagada", fila.Estado);
    }

    [Fact]
    public async Task FiltrarCommand_PasaLosFiltrosAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await vm.CargarAsync();
        vm.FechaDesde = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];

        await vm.FiltrarCommand.ExecuteAsync(null);

        svc.Verify(s => s.ListarAsync(It.Is<GastoFiltro>(f =>
            f.ProveedorId == 1 && f.FechaDesde != null)), Times.AtLeastOnce);
    }

    [Fact]
    public async Task AnularCommand_ConConfirmacion_AnulaYRecarga()
    {
        var (vm, svc, _, _) = Crear(new List<Gasto> { GastoDe(1, "Para anular") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        svc.Verify(s => s.AnularAsync(1), Times.Once);
        svc.Verify(s => s.ListarAsync(It.IsAny<GastoFiltro>()), Times.AtLeast(2));
    }

    [Fact]
    public async Task AnularCommand_ErrorDeRegla_SeInformaSinCrashear()
    {
        var (vm, svc, _, confirm) = Crear(new List<Gasto> { GastoDe(1, "Con pagos", pagado: true) });
        svc.Setup(s => s.AnularAsync(1))
            .ThrowsAsync(new StockApp.Domain.Exceptions.ReglaDeNegocioException("Tiene pagos activos."));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.AnularCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Tiene pagos activos."), Times.Once);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarYPagos_ConSeleccion_NaveganConElGasto()
    {
        var (vm, _, nav, _) = Crear(new List<Gasto> { GastoDe(1, "Editable") });
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.EditarCommand.ExecuteAsync(null);
        await vm.PagosCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Once);
        nav.Verify(n => n.Navegar<PagosGastoViewModel>(
            It.IsAny<Action<PagosGastoViewModel>>()), Times.Once);
    }

    [Fact]
    public void EditarCommand_SinSeleccion_EstaDeshabilitado()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.PagosCommand.CanExecute(null));
        Assert.False(vm.AnularCommand.CanExecute(null));
    }
}
```

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;
using ICategoriaProveedorService = StockApp.Application.Catalogo.IProveedorService;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class GastoFormViewModelTests
{
    private static (GastoFormViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));
        svc.Setup(s => s.ModificarAsync(It.IsAny<Gasto>()))
            .ReturnsAsync(new ResultadoGastoDto(7, null));

        var proveedores = new Mock<ICategoriaProveedorService>();
        proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>
        {
            new() { Id = 1, Nombre = "Barraca X", Activo = true },
            new() { Id = 2, Nombre = "Dado de baja", Activo = false },
        });
        var fuentes = new Mock<IFuenteFinanciamientoService>();
        fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>
        {
            new() { Id = 2, Nombre = "Literal B", Activo = true },
        });
        var rubros = new Mock<IRubroGastoService>();
        rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>
        {
            new() { Id = 3, Codigo = 3, Nombre = "Materiales", Activo = true },
        });
        var lineas = new Mock<ILineaPoaService>();
        lineas.Setup(l => l.ListarActivasAsync()).ReturnsAsync(new List<LineaPoa>
        {
            new() { Id = 5, Nombre = "PRENSA", Programa = "Com", Ejercicio = 2026, Activo = true },
        });

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);

        var vm = new GastoFormViewModel(
            svc.Object, proveedores.Object, fuentes.Object, rubros.Object, lineas.Object,
            nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    private static async Task CompletarFormularioValidoAsync(GastoFormViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Materiales de obra";
        vm.MontoTexto = "1.500,50";   // es-UY: miles con punto, decimales con coma
    }

    [Fact]
    public async Task InicializarAsync_SoloOfreceProveedoresActivos()
    {
        var (vm, _, _, _) = Crear();

        await vm.InicializarAsync();

        var proveedor = Assert.Single(vm.ProveedoresDisponibles);
        Assert.Equal("Barraca X", proveedor.Nombre);
    }

    [Fact]
    public async Task Guardar_ParseaElMontoConCulturaEsUY()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(
            It.Is<Gasto>(g => g.MontoTotal == 1500.50m && g.CondicionPago == CondicionPago.Contado),
            null), Times.Once);
    }

    [Fact]
    public async Task Guardar_MontoIlegible_MuestraErrorSinLlamarAlServicio()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.MontoTexto = "abc";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>?>()), Times.Never);
    }

    [Fact]
    public async Task Guardar_Credito_MandaVencimiento()
    {
        var (vm, svc, _, _) = Crear();
        await CompletarFormularioValidoAsync(vm);
        vm.EsCredito = true;
        vm.FechaVencimientoSeleccionada = DateTimeOffset.UtcNow.AddDays(30);

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.Is<Gasto>(g =>
            g.CondicionPago == CondicionPago.Credito && g.FechaVencimiento != null), null), Times.Once);
    }

    [Fact]
    public async Task Guardar_ConAdvertenciaDeSobregiro_LaInformaYNavega()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), null))
            .ReturnsAsync(new ResultadoGastoDto(7, "Atención: sobregiro POA"));
        await CompletarFormularioValidoAsync(vm);

        await vm.GuardarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Atención: sobregiro POA"), Times.Once);
        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
    }

    [Fact]
    public async Task Guardar_ReglaDeNegocio_MuestraMensajeError()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), null))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-1' para ese proveedor."));
        await CompletarFormularioValidoAsync(vm);
        vm.NumeroFactura = "A-1";

        await vm.GuardarCommand.ExecuteAsync(null);

        Assert.Equal("Ya existe la factura 'A-1' para ese proveedor.", vm.MensajeError);
    }

    [Fact]
    public async Task CargarParaEditar_PrecargaLosCampos()
    {
        var (vm, svc, _, _) = Crear();
        var gasto = new Gasto
        {
            Id = 9, ProveedorId = 1, NumeroFactura = "A-9", Detalle = "Histórico",
            Fecha = DateTime.UtcNow, MontoTotal = 2000m,
            FuenteFinanciamientoId = 2, RubroGastoId = 3, LineaPoaId = 5,
            CondicionPago = CondicionPago.Credito, FechaVencimiento = DateTime.UtcNow.AddDays(10),
        };
        vm.CargarParaEditar(gasto);
        await vm.InicializarAsync();

        Assert.True(vm.EsEdicion);
        Assert.Equal("Histórico", vm.Detalle);
        Assert.True(vm.EsCredito);
        Assert.Equal("A-9", vm.NumeroFactura);
        Assert.NotNull(vm.LineaPoaSeleccionada);

        await vm.GuardarCommand.ExecuteAsync(null);
        svc.Verify(s => s.ModificarAsync(It.Is<Gasto>(g => g.Id == 9)), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_PrecargaMontoYVinculaMovimiento()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 2500m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Factura de la entrada";

        Assert.Equal("2.500,00", vm.MontoTexto);   // precargado, editable (fletes, redondeos)

        await vm.GuardarCommand.ExecuteAsync(null);

        svc.Verify(s => s.AltaAsync(It.IsAny<Gasto>(),
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids[0] == 40)), Times.Once);
    }

    [Fact]
    public async Task CargarDesdeEntrada_FacturaYaExistente_OfreceAsociarLaExistente()
    {
        var (vm, svc, nav, confirm) = Crear();
        svc.Setup(s => s.AltaAsync(It.IsAny<Gasto>(), It.IsAny<IReadOnlyList<int>>()))
            .ThrowsAsync(new ReglaDeNegocioException("Ya existe la factura 'A-1' para ese proveedor."));
        svc.Setup(s => s.ObtenerPorProveedorYFacturaAsync(1, "A-1"))
            .ReturnsAsync(new Gasto { Id = 55, ProveedorId = 1, NumeroFactura = "A-1", Detalle = "Existente" });
        vm.CargarDesdeEntrada(movimientoId: 40, montoSugerido: 100m);
        await vm.InicializarAsync();
        vm.ProveedorSeleccionado = vm.ProveedoresDisponibles[0];
        vm.FuenteSeleccionada = vm.FuentesDisponibles[0];
        vm.RubroSeleccionado = vm.RubrosDisponibles[0];
        vm.Detalle = "Factura repetida";
        vm.NumeroFactura = "A-1";

        await vm.GuardarCommand.ExecuteAsync(null);

        // Preguntó, el usuario aceptó (mock devuelve true) ⇒ asocia el movimiento a la existente
        svc.Verify(s => s.AsociarMovimientosAsync(55,
            It.Is<IReadOnlyList<int>>(ids => ids[0] == 40)), Times.Once);
        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
    }
}
```

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class PagosGastoViewModelTests
{
    private static readonly DateTime Hoy = DateTime.UtcNow;

    private static Gasto GastoConPago() => new()
    {
        Id = 5, ProveedorId = 1, Detalle = "Materiales", Fecha = Hoy, MontoTotal = 1000m,
        FuenteFinanciamientoId = 2, RubroGastoId = 3,
        CondicionPago = CondicionPago.Credito, FechaVencimiento = Hoy.AddDays(30),
        Pagos = { new PagoGasto { Id = 21, GastoId = 5, Fecha = Hoy, Monto = 400m } },
    };

    private static (PagosGastoViewModel vm,
                    Mock<IGastoService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear()
    {
        var svc = new Mock<IGastoService>();
        svc.Setup(s => s.ObtenerPorIdAsync(5)).ReturnsAsync(GastoConPago());
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>())).ReturnsAsync(22);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new PagosGastoViewModel(svc.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task Inicializar_MuestraPagosYSaldo()
    {
        var (vm, _, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        await vm.InicializarAsync();

        Assert.Single(vm.Pagos);
        Assert.Equal(600m, vm.SaldoPendiente);
        Assert.Contains("Materiales", vm.TituloGasto);
    }

    [Fact]
    public async Task RegistrarPago_ParseaEsUY_RegistraYRefresca()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "600,00";
        vm.Nota = "saldo final";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        svc.Verify(s => s.RegistrarPagoAsync(It.Is<PagoGasto>(p =>
            p.GastoId == 5 && p.Monto == 600m && p.Nota == "saldo final")), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);  // refresco post-pago
    }

    [Fact]
    public async Task RegistrarPago_MontoIlegible_MuestraError()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "no-es-numero";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.NotNull(vm.MensajeError);
        svc.Verify(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()), Times.Never);
    }

    [Fact]
    public async Task RegistrarPago_ReglaDeNegocio_MuestraMensaje()
    {
        var (vm, svc, _, _) = Crear();
        svc.Setup(s => s.RegistrarPagoAsync(It.IsAny<PagoGasto>()))
            .ThrowsAsync(new ReglaDeNegocioException("El pago supera el saldo pendiente."));
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();
        vm.MontoTexto = "9999";

        await vm.RegistrarPagoCommand.ExecuteAsync(null);

        Assert.Equal("El pago supera el saldo pendiente.", vm.MensajeError);
    }

    [Fact]
    public async Task AnularPago_ConConfirmacion_AnulaYRefresca()
    {
        var (vm, svc, _, _) = Crear();
        vm.CargarParaGasto(GastoConPago());
        await vm.InicializarAsync();

        await vm.AnularPagoCommand.ExecuteAsync(vm.Pagos[0]);

        svc.Verify(s => s.AnularPagoAsync(5, 21), Times.Once);
        svc.Verify(s => s.ObtenerPorIdAsync(5), Times.AtLeastOnce);
    }

    [Fact]
    public async Task Volver_NavegaALaGrilla()
    {
        var (vm, _, nav, _) = Crear();
        vm.CargarParaGasto(GastoConPago());

        vm.VolverCommand.Execute(null);

        nav.Verify(n => n.Navegar<GastosViewModel>(), Times.Once);
        await Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~GastosViewModel|FullyQualifiedName~GastoFormViewModel|FullyQualifiedName~PagosGastoViewModel"`
Expected: FALLA la compilación con `CS0246` (`GastosViewModel` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar GastosViewModel (grilla + filtros + CSV)**

`src/StockApp.Presentation/ViewModels/Finanzas/GastosViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Exportacion;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Fila de solo lectura de la grilla de gastos: aplana las navs y materializa el estado
/// CALCULADO (con la fecha de referencia del momento de la carga). También define las
/// columnas del export CSV.
/// </summary>
public sealed class GastoFila
{
    public Gasto Gasto { get; }

    public GastoFila(Gasto gasto, DateTime fechaReferencia)
    {
        Gasto = gasto;
        Estado = gasto.CalcularEstado(fechaReferencia).ToString();
    }

    public int Id => Gasto.Id;
    public DateTime Fecha => Gasto.Fecha;
    public string ProveedorNombre => Gasto.Proveedor?.Nombre ?? string.Empty;
    public string NumeroFactura => Gasto.NumeroFactura ?? string.Empty;
    public string Detalle => Gasto.Detalle;
    public string FuenteNombre => Gasto.FuenteFinanciamiento?.Nombre ?? string.Empty;
    public string RubroNombre => Gasto.RubroGasto?.Nombre ?? string.Empty;
    public string LineaPoaNombre => Gasto.LineaPoa?.Nombre ?? string.Empty;
    public decimal MontoTotal => Gasto.MontoTotal;
    public decimal TotalPagado => Gasto.TotalPagado;
    public decimal Saldo => Gasto.SaldoPendiente;
    public string Estado { get; }
}

/// <summary>
/// Pantalla "Gastos y facturas" (spec §7.1): grilla con filtros combinables y acciones
/// Nuevo / Editar / Pagos / Anular + export CSV. El filtro de estado se aplica EN MEMORIA
/// (el estado es calculado, el servidor no puede filtrarlo en SQL sin materializarlo).
/// </summary>
public partial class GastosViewModel : ViewModelBase
{
    public const string EstadoTodos = "Todos";

    private readonly IGastoService                _service;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;
    private readonly ICsvExporter                 _csvExporter;
    private readonly IServicioGuardadoArchivo     _guardado;

    // ── Filtros ───────────────────────────────────────────────────────────────
    [ObservableProperty] private DateTimeOffset? _fechaDesde;
    [ObservableProperty] private DateTimeOffset? _fechaHasta;
    [ObservableProperty] private Proveedor? _proveedorSeleccionado;
    [ObservableProperty] private FuenteFinanciamiento? _fuenteSeleccionada;
    [ObservableProperty] private RubroGasto? _rubroSeleccionado;
    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;
    [ObservableProperty] private string _estadoSeleccionado = EstadoTodos;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(PagosCommand))]
    [NotifyCanExecuteChangedFor(nameof(AnularCommand))]
    private GastoFila? _filaSeleccionada;

    public ObservableCollection<GastoFila> Filas { get; } = new();
    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public IReadOnlyList<string> EstadosDisponibles { get; } =
        new[] { EstadoTodos, "Pendiente", "Parcial", "Pagada", "Vencida", "Anulada" };

    public GastosViewModel(
        IGastoService service,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion,
        ICsvExporter csvExporter,
        IServicioGuardadoArchivo guardado)
    {
        _service            = service;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
        _csvExporter        = csvExporter;
        _guardado           = guardado;
    }

    /// <summary>Carga combos de filtros + primer listado. La dispara la View (DataContextChanged).</summary>
    public async Task CargarAsync()
    {
        try
        {
            var proveedores = await _proveedoresService.ListarTodosAsync();
            ProveedoresDisponibles.Clear();
            foreach (var p in proveedores.Where(p => p.Activo))
                ProveedoresDisponibles.Add(p);

            var fuentes = await _fuentesService.ListarActivasAsync();
            FuentesDisponibles.Clear();
            foreach (var f in fuentes)
                FuentesDisponibles.Add(f);

            var rubros = await _rubrosService.ListarActivosAsync();
            RubrosDisponibles.Clear();
            foreach (var r in rubros)
                RubrosDisponibles.Add(r);

            var lineas = await _lineasService.ListarActivasAsync();
            LineasPoaDisponibles.Clear();
            foreach (var l in lineas)
                LineasPoaDisponibles.Add(l);

            await FiltrarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private GastoFiltro ArmarFiltro() => new(
        // DatePicker devuelve fecha local: se fija a medianoche UTC del día elegido
        // (mismo criterio que MovimientoHistorialViewModel).
        FechaDesde: FechaDesde is null
            ? null : DateTime.SpecifyKind(FechaDesde.Value.Date, DateTimeKind.Utc),
        FechaHasta: FechaHasta is null
            ? null : DateTime.SpecifyKind(FechaHasta.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc),
        ProveedorId: ProveedorSeleccionado?.Id,
        FuenteFinanciamientoId: FuenteSeleccionada?.Id,
        RubroGastoId: RubroSeleccionado?.Id,
        LineaPoaId: LineaPoaSeleccionada?.Id);

    [RelayCommand]
    private async Task FiltrarAsync()
    {
        try
        {
            var gastos = await _service.ListarAsync(ArmarFiltro());
            var ahora = DateTime.UtcNow;

            var filas = gastos.Select(g => new GastoFila(g, ahora));
            if (EstadoSeleccionado != EstadoTodos)
                filas = filas.Where(f => f.Estado == EstadoSeleccionado);

            Filas.Clear();
            foreach (var fila in filas)
                Filas.Add(fila);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task LimpiarFiltrosAsync()
    {
        FechaDesde = null;
        FechaHasta = null;
        ProveedorSeleccionado = null;
        FuenteSeleccionada = null;
        RubroSeleccionado = null;
        LineaPoaSeleccionada = null;
        EstadoSeleccionado = EstadoTodos;
        await FiltrarAsync();
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<GastoFormViewModel>());

    private bool TieneSeleccion() => FilaSeleccionada is not null;

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task EditarAsync()
    {
        if (FilaSeleccionada is null) return;
        var gasto = FilaSeleccionada.Gasto;
        await Task.Run(() =>
            _navigation.Navegar<GastoFormViewModel>(vm => vm.CargarParaEditar(gasto)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task PagosAsync()
    {
        if (FilaSeleccionada is null) return;
        var gasto = FilaSeleccionada.Gasto;
        await Task.Run(() =>
            _navigation.Navegar<PagosGastoViewModel>(vm => vm.CargarParaGasto(gasto)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccion))]
    private async Task AnularAsync()
    {
        if (FilaSeleccionada is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma anular el gasto \"{FilaSeleccionada.Detalle}\" " +
            $"(factura {FilaSeleccionada.NumeroFactura} — {FilaSeleccionada.MontoTotal})?");
        if (!confirmar) return;

        try
        {
            await _service.AnularAsync(FilaSeleccionada.Id);
            await FiltrarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private static readonly IReadOnlyList<string> ColumnasCsv = new[]
    {
        nameof(GastoFila.Fecha), nameof(GastoFila.ProveedorNombre), nameof(GastoFila.NumeroFactura),
        nameof(GastoFila.Detalle), nameof(GastoFila.FuenteNombre), nameof(GastoFila.RubroNombre),
        nameof(GastoFila.LineaPoaNombre), nameof(GastoFila.MontoTotal), nameof(GastoFila.TotalPagado),
        nameof(GastoFila.Saldo), nameof(GastoFila.Estado),
    };

    [RelayCommand]
    private async Task ExportarCsvAsync()
    {
        var contenido = _csvExporter.Exportar(Filas, ColumnasCsv);
        await _guardado.GuardarTextoAsync(contenido, $"gastos-{DateTime.Now:yyyyMMdd}.csv");
    }
}
```

- [ ] **Step 4: Implementar GastoFormViewModel**

`src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Formulario de alta / edición de un gasto (factura). Tres modos:
/// alta directa desde Finanzas, edición desde la grilla, y alta DESDE la entrada de stock
/// (CargarDesdeEntrada: monto precargado con cantidad × precio pero editable — la factura
/// real puede traer fletes o redondeos — y el movimiento queda vinculado al guardar; si la
/// factura ya existe para ese proveedor, ofrece asociar los movimientos a la existente).
/// </summary>
public partial class GastoFormViewModel : ViewModelBase
{
    private readonly IGastoService                _service;
    private readonly IProveedorService            _proveedoresService;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly IRubroGastoService           _rubrosService;
    private readonly ILineaPoaService             _lineasService;
    private readonly INavigationService           _navigation;
    private readonly IConfirmacionService         _confirmacion;

    private int _idEdicion;
    private Gasto? _gastoParaEditar;
    private int? _movimientoVinculado;   // modo "desde entrada de stock"

    /// <summary>Cultura FIJA es-UY (patrón MonedaConverter / LineaPoaFormViewModel).</summary>
    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private Proveedor? _proveedorSeleccionado;

    [ObservableProperty] private string? _numeroFactura;
    [ObservableProperty] private string? _numeroOrden;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _detalle = string.Empty;

    [ObservableProperty] private string? _destino;

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private RubroGasto? _rubroSeleccionado;

    [ObservableProperty] private LineaPoa? _lineaPoaSeleccionada;

    [ObservableProperty] private bool _esCredito;
    [ObservableProperty] private DateTimeOffset? _fechaVencimientoSeleccionada;

    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion
        ? "Editar gasto"
        : _movimientoVinculado is null ? "Nuevo gasto" : "Asociar factura a la entrada";

    public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();
    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
    public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
    public ObservableCollection<LineaPoa> LineasPoaDisponibles { get; } = new();

    public GastoFormViewModel(
        IGastoService service,
        IProveedorService proveedoresService,
        IFuenteFinanciamientoService fuentesService,
        IRubroGastoService rubrosService,
        ILineaPoaService lineasService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service            = service;
        _proveedoresService = proveedoresService;
        _fuentesService     = fuentesService;
        _rubrosService      = rubrosService;
        _lineasService      = lineasService;
        _navigation         = navigation;
        _confirmacion       = confirmacion;
    }

    /// <summary>Modo edición. Corre ANTES de InicializarAsync (contrato de LineaPoaFormViewModel).</summary>
    public void CargarParaEditar(Gasto gasto)
    {
        _idEdicion       = gasto.Id;
        _gastoParaEditar = gasto;
        NumeroFactura    = gasto.NumeroFactura;
        NumeroOrden      = gasto.NumeroOrden;
        Detalle          = gasto.Detalle;
        Destino          = gasto.Destino;
        FechaSeleccionada = new DateTimeOffset(DateTime.SpecifyKind(gasto.Fecha, DateTimeKind.Utc));
        MontoTexto       = gasto.MontoTotal.ToString("N2", CulturaMonto);
        EsCredito        = gasto.CondicionPago == CondicionPago.Credito;
        FechaVencimientoSeleccionada = gasto.FechaVencimiento is null
            ? null : new DateTimeOffset(DateTime.SpecifyKind(gasto.FechaVencimiento.Value, DateTimeKind.Utc));
        EsEdicion        = true;
    }

    /// <summary>
    /// Modo "desde entrada de stock" (spec §5): precarga el monto sugerido
    /// (cantidad × precio unitario) EDITABLE y recuerda el movimiento a vincular.
    /// </summary>
    public void CargarDesdeEntrada(int movimientoId, decimal montoSugerido)
    {
        _movimientoVinculado = movimientoId;
        MontoTexto = montoSugerido.ToString("N2", CulturaMonto);
    }

    /// <summary>Carga los combos. La dispara la View (DataContextChanged).</summary>
    public async Task InicializarAsync()
    {
        var proveedores = await _proveedoresService.ListarTodosAsync();
        ProveedoresDisponibles.Clear();
        foreach (var p in proveedores.Where(p => p.Activo))
            ProveedoresDisponibles.Add(p);

        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        var rubros = await _rubrosService.ListarActivosAsync();
        RubrosDisponibles.Clear();
        foreach (var r in rubros)
            RubrosDisponibles.Add(r);

        var lineas = await _lineasService.ListarActivasAsync();
        LineasPoaDisponibles.Clear();
        foreach (var l in lineas)
            LineasPoaDisponibles.Add(l);

        if (_gastoParaEditar is not null)
        {
            // Resuelve las selecciones por Id contra los combos; si un maestro fue dado
            // de baja después, cae al objeto de la nav para no perder el dato histórico.
            ProveedorSeleccionado =
                ProveedoresDisponibles.FirstOrDefault(p => p.Id == _gastoParaEditar.ProveedorId)
                ?? Agregar(ProveedoresDisponibles, _gastoParaEditar.Proveedor);
            FuenteSeleccionada =
                FuentesDisponibles.FirstOrDefault(f => f.Id == _gastoParaEditar.FuenteFinanciamientoId)
                ?? Agregar(FuentesDisponibles, _gastoParaEditar.FuenteFinanciamiento);
            RubroSeleccionado =
                RubrosDisponibles.FirstOrDefault(r => r.Id == _gastoParaEditar.RubroGastoId)
                ?? Agregar(RubrosDisponibles, _gastoParaEditar.RubroGasto);
            if (_gastoParaEditar.LineaPoaId is not null)
                LineaPoaSeleccionada =
                    LineasPoaDisponibles.FirstOrDefault(l => l.Id == _gastoParaEditar.LineaPoaId)
                    ?? Agregar(LineasPoaDisponibles, _gastoParaEditar.LineaPoa);
        }
    }

    private static T? Agregar<T>(ObservableCollection<T> coleccion, T? item) where T : class
    {
        if (item is not null)
            coleccion.Add(item);
        return item;
    }

    private bool PuedeGuardar()
        => ProveedorSeleccionado is not null
           && FuenteSeleccionada is not null
           && RubroSeleccionado is not null
           && !string.IsNullOrWhiteSpace(Detalle)
           && !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(
                MontoTexto,
                NumberStyles.Number,           // permite miles "." y decimales "," de es-UY
                CulturaMonto,
                out var monto))
        {
            MensajeError = "El monto total no es un número válido.";
            return;
        }

        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del gasto es obligatoria.";
            return;
        }
        if (EsCredito && FechaVencimientoSeleccionada is null)
        {
            MensajeError = "Un gasto a crédito exige fecha de vencimiento.";
            return;
        }

        var gasto = new Gasto
        {
            Id = EsEdicion ? _idEdicion : 0,
            ProveedorId = ProveedorSeleccionado!.Id,
            NumeroFactura = string.IsNullOrWhiteSpace(NumeroFactura) ? null : NumeroFactura!.Trim(),
            NumeroOrden = string.IsNullOrWhiteSpace(NumeroOrden) ? null : NumeroOrden!.Trim(),
            Detalle = Detalle,
            Destino = string.IsNullOrWhiteSpace(Destino) ? null : Destino,
            Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            MontoTotal = monto,
            FuenteFinanciamientoId = FuenteSeleccionada!.Id,
            RubroGastoId = RubroSeleccionado!.Id,
            LineaPoaId = LineaPoaSeleccionada?.Id,
            CondicionPago = EsCredito ? CondicionPago.Credito : CondicionPago.Contado,
            FechaVencimiento = EsCredito
                ? DateTime.SpecifyKind(FechaVencimientoSeleccionada!.Value.Date, DateTimeKind.Utc)
                : null,
        };

        try
        {
            var resultado = EsEdicion
                ? await _service.ModificarAsync(gasto)
                : await _service.AltaAsync(gasto,
                    _movimientoVinculado is null ? null : new[] { _movimientoVinculado.Value });

            if (resultado.AdvertenciaSobregiro is not null)
                await _confirmacion.InformarAsync(resultado.AdvertenciaSobregiro);

            _navigation.Navegar<GastosViewModel>();
        }
        catch (ReglaDeNegocioException ex)
            when (_movimientoVinculado is not null && gasto.NumeroFactura is not null)
        {
            // La factura ya existe (cargada antes desde Finanzas): ofrecer asociar los
            // movimientos a la existente en vez de duplicarla (spec §5.1).
            var existente = await _service.ObtenerPorProveedorYFacturaAsync(
                gasto.ProveedorId, gasto.NumeroFactura);
            if (existente is null)
            {
                MensajeError = ex.Message;
                return;
            }

            var asociar = await _confirmacion.PreguntarAsync(
                $"La factura '{gasto.NumeroFactura}' ya existe para ese proveedor " +
                $"(\"{existente.Detalle}\"). ¿Asociar la entrada de stock a esa factura?");
            if (!asociar)
            {
                MensajeError = ex.Message;
                return;
            }

            try
            {
                await _service.AsociarMovimientosAsync(existente.Id, new[] { _movimientoVinculado.Value });
                _navigation.Navegar<GastosViewModel>();
            }
            catch (Exception ex2) when (ex2 is ReglaDeNegocioException or EntidadNoEncontradaException)
            {
                MensajeError = ex2.Message;
            }
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<GastosViewModel>();
}
```

- [ ] **Step 5: Implementar PagosGastoViewModel**

`src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Globalization;
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
/// Pantalla "Pagos de la factura": lista los pagos (activos y anulados) de un gasto,
/// permite registrar un pago nuevo (sin superar el saldo — lo valida el servidor) y
/// anular pagos existentes. Refresca el gasto tras cada operación para que saldo y
/// estado calculado queden al día.
/// </summary>
public partial class PagosGastoViewModel : ViewModelBase
{
    private readonly IGastoService        _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _gastoId;

    /// <summary>Cultura FIJA es-UY (patrón MonedaConverter).</summary>
    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }

    [ObservableProperty] private string _tituloGasto = string.Empty;
    [ObservableProperty] private decimal _montoTotal;
    [ObservableProperty] private decimal _totalPagado;
    [ObservableProperty] private decimal _saldoPendiente;
    [ObservableProperty] private string _estado = string.Empty;

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RegistrarPagoCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _nota;
    [ObservableProperty] private string? _mensajeError;

    public ObservableCollection<PagoGasto> Pagos { get; } = new();

    public PagosGastoViewModel(
        IGastoService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    /// <summary>Recibe el gasto de la grilla. Corre ANTES de InicializarAsync.</summary>
    public void CargarParaGasto(Gasto gasto) => _gastoId = gasto.Id;

    /// <summary>Trae el gasto fresco del servidor. La dispara la View (DataContextChanged).</summary>
    public async Task InicializarAsync()
    {
        try
        {
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private async Task RefrescarAsync()
    {
        var gasto = await _service.ObtenerPorIdAsync(_gastoId);

        TituloGasto = $"{gasto.Detalle} — factura {gasto.NumeroFactura ?? "s/n"} " +
                      $"({gasto.Proveedor?.Nombre ?? $"proveedor {gasto.ProveedorId}"})";
        MontoTotal     = gasto.MontoTotal;
        TotalPagado    = gasto.TotalPagado;
        SaldoPendiente = gasto.SaldoPendiente;
        Estado         = gasto.CalcularEstado(DateTime.UtcNow).ToString();

        Pagos.Clear();
        foreach (var pago in gasto.Pagos)
            Pagos.Add(pago);
    }

    private bool PuedeRegistrar() => !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeRegistrar))]
    private async Task RegistrarPagoAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto del pago no es un número válido.";
            return;
        }
        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del pago es obligatoria.";
            return;
        }

        try
        {
            await _service.RegistrarPagoAsync(new PagoGasto
            {
                GastoId = _gastoId,
                Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
                Monto = monto,
                Nota = string.IsNullOrWhiteSpace(Nota) ? null : Nota,
            });

            MontoTexto = string.Empty;
            Nota = null;
            await RefrescarAsync();
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task AnularPagoAsync(PagoGasto pago)
    {
        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma anular el pago de {pago.Monto.ToString("N2", CulturaMonto)} del {pago.Fecha:dd/MM/yyyy}?");
        if (!confirmar) return;

        try
        {
            await _service.AnularPagoAsync(_gastoId, pago.Id);
            await RefrescarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<GastosViewModel>();
}
```

- [ ] **Step 6: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~GastosViewModel|FullyQualifiedName~GastoFormViewModel|FullyQualifiedName~PagosGastoViewModel"`
Expected: los 23 tests nuevos en verde.

- [ ] **Step 7: Suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: toda la suite verde.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation tests/StockApp.Presentation.Tests
git commit -m "feat(finanzas): ViewModels de gastos, formulario de factura y pagos con cultura es-UY"
```

---

### Task 9: Presentation — ViewModels de Ingresos + todas las Views XAML + sidebar + DI

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/GastosView.axaml` + `.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` + `.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` + `.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml` + `.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml` + `.axaml.cs`
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (comandos NavGastos / NavIngresos)
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml` (botones en la sección Finanzas)
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI: ApiClients + VMs nuevos)

**Interfaces:**
- Consumes: VMs de Task 8, `IIngresoCajaService`, `IFuenteFinanciamientoService`, `MonedaConverter.Instance` / `FechaUtcALocalConverter.Instance` / `ActivoOpacidadConverter.Instance` (converters existentes), `ViewLocator` (resuelve View por convención de nombre).
- Produces: `IngresosViewModel` (lista + Nuevo/Editar/Baja), `IngresoFormViewModel` (`CargarParaEditar`, `InicializarAsync`, `GuardarCommand`, `CancelarCommand`), comandos `NavGastosCommand`/`NavIngresosCommand` con `SeccionActiva` `"Gastos"`/`"Ingresos"`, registros DI transient.

- [ ] **Step 1: Escribir los tests de los VMs de ingresos que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Finanzas/IngresosViewModelTests.cs`:

```csharp
using System;
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

public class IngresosViewModelTests
{
    private static IngresoCaja Ingreso(int id, string concepto, bool activo = true) => new()
    {
        Id = id, Fecha = DateTime.UtcNow, Concepto = concepto,
        FuenteFinanciamientoId = 2,
        FuenteFinanciamiento = new FuenteFinanciamiento { Id = 2, Nombre = "Literal B" },
        Monto = 1000m, Activo = activo,
    };

    private static (IngresosViewModel vm,
                    Mock<IIngresoCajaService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(IReadOnlyList<IngresoCaja>? ingresos = null)
    {
        var svc = new Mock<IIngresoCajaService>();
        svc.Setup(s => s.ListarTodosAsync()).ReturnsAsync(ingresos ?? new List<IngresoCaja>());
        svc.Setup(s => s.BajaLogicaAsync(It.IsAny<int>())).Returns(Task.CompletedTask);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new IngresosViewModel(svc.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaItems()
    {
        var (vm, _, _, _) = Crear(new List<IngresoCaja>
        {
            Ingreso(1, "Partida FIGM"), Ingreso(2, "Multas"),
        });

        await vm.CargarAsync();

        Assert.Equal(2, vm.Items.Count);
        Assert.Equal("Partida FIGM", vm.Items[0].Concepto);
    }

    [Fact]
    public async Task NuevoCommand_NavegaAlFormulario()
    {
        var (vm, _, nav, _) = Crear();

        await vm.NuevoCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(), Times.Once);
    }

    [Fact]
    public async Task EditarCommand_ConSeleccion_NavegaEnModoEdicion()
    {
        var ingreso = Ingreso(5, "Editable");
        var (vm, _, nav, _) = Crear(new List<IngresoCaja> { ingreso });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.EditarCommand.ExecuteAsync(null);

        nav.Verify(n => n.Navegar<IngresoFormViewModel>(
            It.IsAny<Action<IngresoFormViewModel>>()), Times.Once);
    }

    [Fact]
    public async Task BajaCommand_ConConfirmacion_DaDeBajaYRecarga()
    {
        var (vm, svc, _, _) = Crear(new List<IngresoCaja> { Ingreso(1, "Para baja") });
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];

        await vm.BajaCommand.ExecuteAsync(null);

        svc.Verify(s => s.BajaLogicaAsync(1), Times.Once);
        svc.Verify(s => s.ListarTodosAsync(), Times.AtLeast(2));
    }

    [Fact]
    public async Task BajaCommand_ErrorDeRegla_SeInforma()
    {
        var (vm, svc, _, confirm) = Crear(new List<IngresoCaja> { Ingreso(1, "Ya inactivo", activo: false) });
        svc.Setup(s => s.BajaLogicaAsync(1))
            .ThrowsAsync(new ReglaDeNegocioException("Ya está dado de baja."));
        await vm.CargarAsync();
        vm.ItemSeleccionado = vm.Items[0];
        // El CanExecute exige Activo: se fuerza el caso llamando con item inactivo re-seleccionado
        vm.Items[0].Activo = true;

        await vm.BajaCommand.ExecuteAsync(null);

        confirm.Verify(c => c.InformarAsync("Ya está dado de baja."), Times.Once);
    }

    [Fact]
    public void EditarYBaja_SinSeleccion_Deshabilitados()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.EditarCommand.CanExecute(null));
        Assert.False(vm.BajaCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresosViewModel"`
Expected: FALLA la compilación con `CS0246` (`IngresosViewModel` no existe) — rojo confirmado.

- [ ] **Step 3: Implementar los VMs de ingresos**

`src/StockApp.Presentation/ViewModels/Finanzas/IngresosViewModel.cs`:

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
/// Pantalla "Ingresos de caja" (spec §7.2): ABM simple de partidas, multas, préstamos.
/// Alta/edición navegan al formulario; baja lógica con confirmación.
/// </summary>
public partial class IngresosViewModel : ViewModelBase
{
    private readonly IIngresoCajaService  _service;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditarCommand))]
    [NotifyCanExecuteChangedFor(nameof(BajaCommand))]
    private IngresoCaja? _itemSeleccionado;

    public ObservableCollection<IngresoCaja> Items { get; } = new();

    public IngresosViewModel(
        IIngresoCajaService service,
        INavigationService navigation,
        IConfirmacionService confirmacion)
    {
        _service      = service;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        try
        {
            var resultados = await _service.ListarTodosAsync();
            Items.Clear();
            foreach (var ingreso in resultados)
                Items.Add(ingreso);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private async Task NuevoAsync()
        => await Task.Run(() => _navigation.Navegar<IngresoFormViewModel>());

    private bool TieneSeleccionActiva()
        => ItemSeleccionado is not null && ItemSeleccionado.Activo;

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task EditarAsync()
    {
        if (ItemSeleccionado is null) return;
        var seleccionado = ItemSeleccionado;
        await Task.Run(() =>
            _navigation.Navegar<IngresoFormViewModel>(vm => vm.CargarParaEditar(seleccionado)));
    }

    [RelayCommand(CanExecute = nameof(TieneSeleccionActiva))]
    private async Task BajaAsync()
    {
        if (ItemSeleccionado is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma dar de baja el ingreso \"{ItemSeleccionado.Concepto}\" ({ItemSeleccionado.Monto})?");
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

`src/StockApp.Presentation/ViewModels/Finanzas/IngresoFormViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Formulario de alta / edición de un ingreso de caja. Montos con cultura FIJA es-UY.</summary>
public partial class IngresoFormViewModel : ViewModelBase
{
    private readonly IIngresoCajaService          _service;
    private readonly IFuenteFinanciamientoService _fuentesService;
    private readonly INavigationService           _navigation;

    private int _idEdicion;
    private IngresoCaja? _ingresoParaEditar;

    private static readonly IFormatProvider CulturaMonto = CrearCulturaMonto();

    private static IFormatProvider CrearCulturaMonto()
    {
        try
        {
            return CultureInfo.GetCultureInfo("es-UY");
        }
        catch (CultureNotFoundException)
        {
            return new NumberFormatInfo
            {
                NumberDecimalSeparator = ",",
                NumberGroupSeparator = ".",
            };
        }
    }

    [ObservableProperty] private DateTimeOffset? _fechaSeleccionada = DateTimeOffset.Now;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _concepto = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private FuenteFinanciamiento? _fuenteSeleccionada;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _montoTexto = string.Empty;

    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Titulo))]
    private bool _esEdicion;

    public string Titulo => EsEdicion ? "Editar ingreso" : "Nuevo ingreso";

    public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();

    public IngresoFormViewModel(
        IIngresoCajaService service,
        IFuenteFinanciamientoService fuentesService,
        INavigationService navigation)
    {
        _service        = service;
        _fuentesService = fuentesService;
        _navigation     = navigation;
    }

    public void CargarParaEditar(IngresoCaja ingreso)
    {
        _idEdicion         = ingreso.Id;
        _ingresoParaEditar = ingreso;
        FechaSeleccionada  = new DateTimeOffset(DateTime.SpecifyKind(ingreso.Fecha, DateTimeKind.Utc));
        Concepto           = ingreso.Concepto;
        MontoTexto         = ingreso.Monto.ToString("N2", CulturaMonto);
        EsEdicion          = true;
    }

    public async Task InicializarAsync()
    {
        var fuentes = await _fuentesService.ListarActivasAsync();
        FuentesDisponibles.Clear();
        foreach (var f in fuentes)
            FuentesDisponibles.Add(f);

        if (_ingresoParaEditar is not null)
        {
            FuenteSeleccionada =
                FuentesDisponibles.FirstOrDefault(f => f.Id == _ingresoParaEditar.FuenteFinanciamientoId)
                ?? _ingresoParaEditar.FuenteFinanciamiento;
            if (FuenteSeleccionada is not null
                && FuentesDisponibles.All(f => f.Id != FuenteSeleccionada.Id))
                FuentesDisponibles.Add(FuenteSeleccionada);
        }
    }

    private bool PuedeGuardar()
        => !string.IsNullOrWhiteSpace(Concepto)
           && FuenteSeleccionada is not null
           && !string.IsNullOrWhiteSpace(MontoTexto);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;

        if (!decimal.TryParse(MontoTexto, NumberStyles.Number, CulturaMonto, out var monto))
        {
            MensajeError = "El monto no es un número válido.";
            return;
        }
        if (FechaSeleccionada is null)
        {
            MensajeError = "La fecha del ingreso es obligatoria.";
            return;
        }

        var ingreso = new IngresoCaja
        {
            Id = EsEdicion ? _idEdicion : 0,
            Fecha = DateTime.SpecifyKind(FechaSeleccionada.Value.Date, DateTimeKind.Utc),
            Concepto = Concepto,
            FuenteFinanciamientoId = FuenteSeleccionada!.Id,
            Monto = monto,
        };

        try
        {
            if (EsEdicion)
                await _service.ModificarAsync(ingreso);
            else
                await _service.AltaAsync(ingreso);

            _navigation.Navegar<IngresosViewModel>();
        }
        catch (Exception ex)
            when (ex is ReglaDeNegocioException or EntidadNoEncontradaException or ArgumentException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Cancelar() => _navigation.Navegar<IngresosViewModel>();
}
```

- [ ] **Step 4: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~IngresosViewModel"`
Expected: los 6 tests nuevos en verde.

- [ ] **Step 5: Views XAML + code-behinds**

`src/StockApp.Presentation/Views/Finanzas/GastosView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1100" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.GastosView"
             x:DataType="vm:GastosViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="Gastos y facturas"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <!-- Filtros -->
        <Border DockPanel.Dock="Top" Classes="card" Margin="0,0,0,12">
            <StackPanel Spacing="8">
                <WrapPanel>
                    <StackPanel Margin="0,0,12,8">
                        <TextBlock Text="Desde" />
                        <DatePicker SelectedDate="{Binding FechaDesde}" />
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8">
                        <TextBlock Text="Hasta" />
                        <DatePicker SelectedDate="{Binding FechaHasta}" />
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="180">
                        <TextBlock Text="Proveedor" />
                        <ComboBox ItemsSource="{Binding ProveedoresDisponibles}"
                                  SelectedItem="{Binding ProveedorSeleccionado}"
                                  PlaceholderText="Todos"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="160">
                        <TextBlock Text="Fuente" />
                        <ComboBox ItemsSource="{Binding FuentesDisponibles}"
                                  SelectedItem="{Binding FuenteSeleccionada}"
                                  PlaceholderText="Todas"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="160">
                        <TextBlock Text="Rubro" />
                        <ComboBox ItemsSource="{Binding RubrosDisponibles}"
                                  SelectedItem="{Binding RubroSeleccionado}"
                                  PlaceholderText="Todos"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="160">
                        <TextBlock Text="Línea POA" />
                        <ComboBox ItemsSource="{Binding LineasPoaDisponibles}"
                                  SelectedItem="{Binding LineaPoaSeleccionada}"
                                  PlaceholderText="Todas"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="130">
                        <TextBlock Text="Estado" />
                        <ComboBox ItemsSource="{Binding EstadosDisponibles}"
                                  SelectedItem="{Binding EstadoSeleccionado}"
                                  HorizontalAlignment="Stretch" />
                    </StackPanel>
                </WrapPanel>
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Classes="primary" Content="Filtrar" Command="{Binding FiltrarCommand}" />
                    <Button Classes="secondary" Content="Limpiar" Command="{Binding LimpiarFiltrosCommand}" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Acciones -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
            <Button Classes="primary" Content="Nuevo gasto" Command="{Binding NuevoCommand}" />
            <Button Classes="secondary" Content="Editar" Command="{Binding EditarCommand}" />
            <Button Classes="secondary" Content="Pagos" Command="{Binding PagosCommand}" />
            <Button Classes="secondary" Content="Anular" Command="{Binding AnularCommand}" />
            <Button Classes="secondary" Command="{Binding ExportarCsvCommand}">
                <StackPanel Orientation="Horizontal" Spacing="6">
                    <i:Icon Value="mdi-file-export" />
                    <TextBlock Text="Exportar CSV" VerticalAlignment="Center" />
                </StackPanel>
            </Button>
        </StackPanel>

        <!-- Grilla -->
        <DataGrid ItemsSource="{Binding Filas}"
                  SelectedItem="{Binding FilaSeleccionada}"
                  IsReadOnly="True"
                  CanUserSortColumns="True"
                  AutoGenerateColumns="False">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Fecha"
                                    Binding="{Binding Fecha, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy', DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Proveedor"
                                    Binding="{Binding ProveedorNombre, DataType={x:Type vm:GastoFila}}"
                                    Width="*" />
                <DataGridTextColumn Header="Factura"
                                    Binding="{Binding NumeroFactura, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Detalle"
                                    Binding="{Binding Detalle, DataType={x:Type vm:GastoFila}}"
                                    Width="2*" />
                <DataGridTextColumn Header="Fuente"
                                    Binding="{Binding FuenteNombre, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Rubro"
                                    Binding="{Binding RubroNombre, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Línea POA"
                                    Binding="{Binding LineaPoaNombre, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Monto"
                                    Binding="{Binding MontoTotal, Converter={x:Static conv:MonedaConverter.Instance}, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Pagado"
                                    Binding="{Binding TotalPagado, Converter={x:Static conv:MonedaConverter.Instance}, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Saldo"
                                    Binding="{Binding Saldo, Converter={x:Static conv:MonedaConverter.Instance}, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Estado"
                                    Binding="{Binding Estado, DataType={x:Type vm:GastoFila}}"
                                    Width="Auto" />
            </DataGrid.Columns>
        </DataGrid>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/GastosView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastosView : UserControl
{
    public GastosView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="760" d:DesignHeight="760"
             x:Class="StockApp.Presentation.Views.Finanzas.GastoFormView"
             x:DataType="vm:GastoFormViewModel">

    <ScrollViewer>
        <DockPanel Margin="24">

            <TextBlock DockPanel.Dock="Top"
                       Text="{Binding Titulo}"
                       Classes="titulo-vista"
                       Margin="0,0,0,16" />

            <Border Classes="card" VerticalAlignment="Top">
                <StackPanel Spacing="12" MaxWidth="620" HorizontalAlignment="Left">

                    <TextBlock Text="Proveedor" />
                    <ComboBox ItemsSource="{Binding ProveedoresDisponibles}"
                              SelectedItem="{Binding ProveedorSeleccionado}"
                              PlaceholderText="Elegí el proveedor"
                              HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>

                    <Grid ColumnDefinitions="*,12,*">
                        <StackPanel Grid.Column="0" Spacing="4">
                            <TextBlock Text="Número de factura (opcional)" />
                            <TextBox Text="{Binding NumeroFactura}" Watermark="Ej.: A-0001234" />
                        </StackPanel>
                        <StackPanel Grid.Column="2" Spacing="4">
                            <TextBlock Text="Orden de compra (opcional)" />
                            <TextBox Text="{Binding NumeroOrden}" Watermark="Ej.: OC-77" />
                        </StackPanel>
                    </Grid>

                    <TextBlock Text="Detalle" />
                    <TextBox Text="{Binding Detalle}" Watermark="Ej.: Materiales para la rambla" />

                    <TextBlock Text="Destino (opcional)" />
                    <TextBox Text="{Binding Destino}" Watermark="Ej.: Corralón municipal" />

                    <Grid ColumnDefinitions="*,12,*">
                        <StackPanel Grid.Column="0" Spacing="4">
                            <TextBlock Text="Fecha" />
                            <DatePicker SelectedDate="{Binding FechaSeleccionada}" />
                        </StackPanel>
                        <StackPanel Grid.Column="2" Spacing="4">
                            <TextBlock Text="Monto total" />
                            <TextBox Text="{Binding MontoTexto}" Watermark="Ej.: 1.500,50" />
                        </StackPanel>
                    </Grid>

                    <TextBlock Text="Fuente de financiamiento" />
                    <ComboBox ItemsSource="{Binding FuentesDisponibles}"
                              SelectedItem="{Binding FuenteSeleccionada}"
                              PlaceholderText="Elegí la fuente"
                              HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>

                    <TextBlock Text="Rubro" />
                    <ComboBox ItemsSource="{Binding RubrosDisponibles}"
                              SelectedItem="{Binding RubroSeleccionado}"
                              PlaceholderText="Elegí el rubro"
                              HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>

                    <TextBlock Text="Línea POA (opcional)" />
                    <ComboBox ItemsSource="{Binding LineasPoaDisponibles}"
                              SelectedItem="{Binding LineaPoaSeleccionada}"
                              PlaceholderText="Sin línea POA"
                              HorizontalAlignment="Stretch">
                        <ComboBox.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding Nombre}" />
                            </DataTemplate>
                        </ComboBox.ItemTemplate>
                    </ComboBox>

                    <CheckBox IsChecked="{Binding EsCredito}" Content="Compra a crédito" />
                    <StackPanel Spacing="4" IsVisible="{Binding EsCredito}">
                        <TextBlock Text="Fecha de vencimiento" />
                        <DatePicker SelectedDate="{Binding FechaVencimientoSeleccionada}" />
                    </StackPanel>

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
    </ScrollViewer>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class GastoFormView : UserControl
{
    public GastoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is GastoFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="820" d:DesignHeight="640"
             x:Class="StockApp.Presentation.Views.Finanzas.PagosGastoView"
             x:DataType="vm:PagosGastoViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="Pagos de la factura"
                   Classes="titulo-vista"
                   Margin="0,0,0,8" />
        <TextBlock DockPanel.Dock="Top"
                   Text="{Binding TituloGasto}"
                   TextWrapping="Wrap"
                   Margin="0,0,0,16" />

        <!-- Resumen -->
        <Border DockPanel.Dock="Top" Classes="card" Margin="0,0,0,12">
            <StackPanel Orientation="Horizontal" Spacing="32">
                <StackPanel>
                    <TextBlock Text="Monto total" Opacity="0.7" />
                    <TextBlock Text="{Binding MontoTotal, Converter={x:Static conv:MonedaConverter.Instance}}"
                               FontWeight="SemiBold" />
                </StackPanel>
                <StackPanel>
                    <TextBlock Text="Pagado" Opacity="0.7" />
                    <TextBlock Text="{Binding TotalPagado, Converter={x:Static conv:MonedaConverter.Instance}}"
                               FontWeight="SemiBold" />
                </StackPanel>
                <StackPanel>
                    <TextBlock Text="Saldo" Opacity="0.7" />
                    <TextBlock Text="{Binding SaldoPendiente, Converter={x:Static conv:MonedaConverter.Instance}}"
                               FontWeight="SemiBold" />
                </StackPanel>
                <StackPanel>
                    <TextBlock Text="Estado" Opacity="0.7" />
                    <TextBlock Text="{Binding Estado}" FontWeight="SemiBold" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Registrar pago -->
        <Border DockPanel.Dock="Top" Classes="card" Margin="0,0,0,12">
            <StackPanel Spacing="8">
                <TextBlock Text="Registrar pago" FontWeight="SemiBold" />
                <WrapPanel>
                    <StackPanel Margin="0,0,12,8">
                        <TextBlock Text="Fecha" />
                        <DatePicker SelectedDate="{Binding FechaSeleccionada}" />
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="140">
                        <TextBlock Text="Monto" />
                        <TextBox Text="{Binding MontoTexto}" Watermark="Ej.: 500,00" />
                    </StackPanel>
                    <StackPanel Margin="0,0,12,8" MinWidth="220">
                        <TextBlock Text="Nota (opcional)" />
                        <TextBox Text="{Binding Nota}" Watermark="Ej.: recibo 123" />
                    </StackPanel>
                </WrapPanel>
                <TextBlock Text="{Binding MensajeError}"
                           Foreground="Red"
                           TextWrapping="Wrap"
                           IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <Button Classes="primary" Content="Registrar pago" Command="{Binding RegistrarPagoCommand}" />
                    <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- Lista de pagos -->
        <Border Classes="card">
            <ListBox ItemsSource="{Binding Pagos}">
                <ListBox.ItemTemplate>
                    <DataTemplate x:DataType="e:PagoGasto">
                        <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto" Margin="4">
                            <TextBlock Grid.Column="0"
                                       Text="{Binding Fecha, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy'}"
                                       Margin="0,0,16,0" />
                            <TextBlock Grid.Column="1"
                                       Text="{Binding Monto, Converter={x:Static conv:MonedaConverter.Instance}}"
                                       FontWeight="SemiBold"
                                       Margin="0,0,16,0" />
                            <TextBlock Grid.Column="2"
                                       Text="{Binding Nota}"
                                       Opacity="0.8"
                                       TextTrimming="CharacterEllipsis" />
                            <Border Grid.Column="3" Classes="badge-inactiva" IsVisible="{Binding !Activo}"
                                    Margin="0,0,8,0">
                                <TextBlock Text="Anulado" Classes="badge-inactiva-texto" />
                            </Border>
                            <Button Grid.Column="4"
                                    Classes="secondary"
                                    IsVisible="{Binding Activo}"
                                    Command="{Binding $parent[UserControl].((vm:PagosGastoViewModel)DataContext).AnularPagoCommand}"
                                    CommandParameter="{Binding}">
                                <i:Icon Value="mdi-cancel" />
                            </Button>
                        </Grid>
                    </DataTemplate>
                </ListBox.ItemTemplate>
            </ListBox>
        </Border>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class PagosGastoView : UserControl
{
    public PagosGastoView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is PagosGastoViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:e="using:StockApp.Domain.Entities"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="900" d:DesignHeight="600"
             x:Class="StockApp.Presentation.Views.Finanzas.IngresosView"
             x:DataType="vm:IngresosViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="Ingresos de caja"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
            <Button Classes="primary" Content="Nuevo ingreso" Command="{Binding NuevoCommand}" />
            <Button Classes="secondary" Content="Editar" Command="{Binding EditarCommand}" />
            <Button Classes="secondary" Content="Dar de baja" Command="{Binding BajaCommand}" />
        </StackPanel>

        <Border Classes="card">
            <DataGrid ItemsSource="{Binding Items}"
                      SelectedItem="{Binding ItemSeleccionado}"
                      IsReadOnly="True"
                      CanUserSortColumns="True"
                      AutoGenerateColumns="False">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Fecha"
                                        Binding="{Binding Fecha, Converter={x:Static conv:FechaUtcALocalConverter.Instance}, StringFormat='dd/MM/yyyy', DataType={x:Type e:IngresoCaja}}"
                                        Width="Auto" />
                    <DataGridTextColumn Header="Concepto"
                                        Binding="{Binding Concepto, DataType={x:Type e:IngresoCaja}}"
                                        Width="2*" />
                    <DataGridTextColumn Header="Fuente"
                                        Binding="{Binding FuenteFinanciamiento.Nombre, DataType={x:Type e:IngresoCaja}}"
                                        Width="*" />
                    <DataGridTextColumn Header="Monto"
                                        Binding="{Binding Monto, Converter={x:Static conv:MonedaConverter.Instance}, DataType={x:Type e:IngresoCaja}}"
                                        Width="Auto" />
                    <DataGridCheckBoxColumn Header="Activo"
                                            Binding="{Binding Activo, DataType={x:Type e:IngresoCaja}}"
                                            Width="Auto" />
                </DataGrid.Columns>
            </DataGrid>
        </Border>

    </DockPanel>

</UserControl>
```

`src/StockApp.Presentation/Views/Finanzas/IngresosView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresosView : UserControl
{
    public IngresosView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresosViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

`src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="640" d:DesignHeight="520"
             x:Class="StockApp.Presentation.Views.Finanzas.IngresoFormView"
             x:DataType="vm:IngresoFormViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="{Binding Titulo}"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <Border Classes="card" VerticalAlignment="Top">
            <StackPanel Spacing="12" MaxWidth="480" HorizontalAlignment="Left">

                <TextBlock Text="Fecha" />
                <DatePicker SelectedDate="{Binding FechaSeleccionada}" />

                <TextBlock Text="Concepto" />
                <TextBox Text="{Binding Concepto}" Watermark="Ej.: Partida mensual FIGM julio" />

                <TextBlock Text="Fuente de financiamiento" />
                <ComboBox ItemsSource="{Binding FuentesDisponibles}"
                          SelectedItem="{Binding FuenteSeleccionada}"
                          PlaceholderText="Elegí la fuente"
                          HorizontalAlignment="Stretch">
                    <ComboBox.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding Nombre}" />
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>

                <TextBlock Text="Monto" />
                <TextBox Text="{Binding MontoTexto}" Watermark="Ej.: 250.000,00" MaxWidth="200"
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

`src/StockApp.Presentation/Views/Finanzas/IngresoFormView.axaml.cs`:

```csharp
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class IngresoFormView : UserControl
{
    public IngresoFormView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is IngresoFormViewModel vm)
                await vm.InicializarAsync();
        };
    }
}
```

- [ ] **Step 6: Sidebar + comandos de navegación**

En `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, agregar dentro del bloque `// ── Finanzas — Fase 1: Admin y Operador ─...` (antes de `NavMaestrosFinanzas`):

```csharp
    [RelayCommand]
    private void NavGastos()
    {
        SeccionActiva = "Gastos";
        _navigation.Navegar<GastosViewModel>();
    }

    [RelayCommand]
    private void NavIngresos()
    {
        SeccionActiva = "Ingresos";
        _navigation.Navegar<IngresosViewModel>();
    }
```

En `src/StockApp.Presentation/Views/ShellMainView.axaml`, dentro de la sección `<!-- Finanzas: ... -->`, ANTES del botón de "Maestros de finanzas":

```xml
                <Button Command="{Binding NavGastosCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Gastos}"
                        HorizontalAlignment="Stretch">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-receipt-text" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Gastos y facturas" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>

                <Button Command="{Binding NavIngresosCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Ingresos}"
                        HorizontalAlignment="Stretch">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-cash-plus" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Ingresos de caja" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>
```

- [ ] **Step 7: Registro DI en App.axaml.cs**

En `src/StockApp.Presentation/App.axaml.cs`, después del bloque `// ── Módulo Finanzas — Fase 1: maestros ─...`:

```csharp
        // ── Módulo Finanzas — Fase 2: gastos, pagos e ingresos ────────────────
        services.AddTransient<IGastoService, GastoApiClient>();
        services.AddTransient<IIngresoCajaService, IngresoCajaApiClient>();
```

Y después del bloque `// ── Módulo Finanzas — Fase 1: VMs de maestros ─...`:

```csharp
        // ── Módulo Finanzas — Fase 2: VMs de gastos e ingresos ────────────────
        services.AddTransient<GastosViewModel>();
        services.AddTransient<GastoFormViewModel>();
        services.AddTransient<PagosGastoViewModel>();
        services.AddTransient<IngresosViewModel>();
        services.AddTransient<IngresoFormViewModel>();
```

- [ ] **Step 8: Suite completa de Presentation**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: toda la suite verde (incluye los tests de navegación del Shell existentes).

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Presentation tests/StockApp.Presentation.Tests
git commit -m "feat(ui): pantallas Gastos y facturas e Ingresos de caja con sidebar y DI"
```

---

### Task 10: Vínculo stock — paso opcional "Asociar factura" al registrar una entrada

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoRegistroViewModelBase.cs` (hook post-registro sobreescribible)
- Modify: `src/StockApp.Presentation/ViewModels/Movimientos/EntradaRegistroViewModel.cs` (override: pregunta y navega al form de gasto)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Movimientos/EntradaRegistroFacturaTests.cs`

**Interfaces:**
- Consumes: `GastoFormViewModel.CargarDesdeEntrada(int movimientoId, decimal montoSugerido)` (Task 8), `MovimientoRegistradoDto` (tiene `MovimientoId`, `Cantidad`, `PrecioUnitario`), `IConfirmacionService.PreguntarAsync`.
- Produces:
  - En la base: `protected INavigationService Navigation { get; }`, `protected IConfirmacionService Confirmacion { get; }` (antes campos privados) y `protected virtual Task AlRegistradoAsync(MovimientoRegistradoDto registrado)` que por defecto navega al historial — el comportamiento actual NO cambia para Salida ni para Ajuste.
  - En Entrada: override que, SOLO si el motivo es `Compra`, pregunta "¿Asociar una factura de proveedor a esta entrada?" — si acepta, navega a `GastoFormViewModel` precargado con el movimiento y el monto sugerido (cantidad × precio); si no, sigue al historial como siempre. El paso es OPCIONAL a propósito (spec §5.4): la operativa de stock no se bloquea por un dato financiero.

- [ ] **Step 1: Escribir los tests que fallan**

`tests/StockApp.Presentation.Tests/ViewModels/Movimientos/EntradaRegistroFacturaTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using StockApp.Presentation.ViewModels.Movimientos;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Movimientos;

/// <summary>
/// Vínculo stock ↔ finanzas (spec §5.1): al confirmar una ENTRADA por COMPRA se ofrece
/// el paso OPCIONAL "Asociar factura". Ajustes no lo ofrecen y rechazar no bloquea nada.
/// </summary>
public class EntradaRegistroFacturaTests
{
    private static MovimientoRegistradoDto Registrado(int id = 40) => new(
        MovimientoId: id, ProductoId: 1, Tipo: TipoMovimiento.Entrada,
        Motivo: MotivoMovimiento.Compra, Cantidad: 5m, PrecioUnitario: 500m,
        StockAnterior: 0m, StockNuevo: 5m, Fecha: DateTime.UtcNow);

    private static (EntradaRegistroViewModel vm,
                    Mock<IMovimientoStockService> svcMock,
                    Mock<INavigationService> navMock,
                    Mock<IConfirmacionService> confirmMock)
        Crear(bool aceptaAsociar)
    {
        var svc = new Mock<IMovimientoStockService>();
        svc.Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), It.IsAny<bool>()))
            .ReturnsAsync(Registrado());

        var productos = new Mock<IProductoService>();
        productos.Setup(p => p.BuscarAsync(null, null, null))
            .ReturnsAsync(new List<ProductoDto>
            {
                // Firma real de ProductoDto (src/StockApp.Application/Catalogo/Dtos.cs):
                // (Id, Codigo, CodigoBarras, Nombre, Descripcion, CategoriaId, CategoriaNombre,
                //  ProveedorId, UnidadMedidaId, UnidadMedidaNombre, PrecioCosto, PrecioVenta,
                //  StockActual, StockMinimo, Activo, FechaAlta)
                new(1, "COD1", null, "Prod test", null, null, null, null,
                    1, "Unidad", 100m, 200m, 10m, 0m, true, DateTime.UtcNow),
            });

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(aceptaAsociar);

        var vm = new EntradaRegistroViewModel(svc.Object, productos.Object, nav.Object, confirm.Object);
        return (vm, svc, nav, confirm);
    }

    private static async Task RegistrarCompraAsync(EntradaRegistroViewModel vm)
    {
        await vm.InicializarAsync();
        vm.ProductoSeleccionado = vm.Productos[0];
        vm.Cantidad = 5m;
        vm.Motivo = MotivoMovimiento.Compra;
        await vm.RegistrarCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task Compra_AceptaAsociar_NavegaAlFormDeGastoConElMovimiento()
    {
        var (vm, _, nav, confirm) = Crear(aceptaAsociar: true);

        await RegistrarCompraAsync(vm);

        confirm.Verify(c => c.PreguntarAsync(It.Is<string>(s => s.Contains("factura"))), Times.Once);
        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Once);
        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Never);
    }

    [Fact]
    public async Task Compra_RechazaAsociar_SigueAlHistorialComoSiempre()
    {
        var (vm, _, nav, _) = Crear(aceptaAsociar: false);

        await RegistrarCompraAsync(vm);

        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
        nav.Verify(n => n.Navegar<GastoFormViewModel>(
            It.IsAny<Action<GastoFormViewModel>>()), Times.Never);
    }

    [Fact]
    public async Task Ajuste_NoOfreceFactura()
    {
        var (vm, svc, nav, confirm) = Crear(aceptaAsociar: true);
        svc.Setup(s => s.RegistrarAsync(It.IsAny<RegistrarMovimientoDto>(), It.IsAny<bool>()))
            .ReturnsAsync(Registrado() with { Motivo = MotivoMovimiento.Ajuste });
        await vm.InicializarAsync();
        vm.ProductoSeleccionado = vm.Productos[0];
        vm.Cantidad = 5m;
        vm.Motivo = MotivoMovimiento.Ajuste;

        await vm.RegistrarCommand.ExecuteAsync(null);

        confirm.Verify(c => c.PreguntarAsync(It.IsAny<string>()), Times.Never);
        nav.Verify(n => n.Navegar<MovimientoHistorialViewModel>(), Times.Once);
    }
}
```

- [ ] **Step 2: Correr los tests y verlos fallar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~EntradaRegistroFacturaTests"`
Expected: los 3 tests FALLAN (el flujo actual navega siempre al historial y nunca pregunta) — rojo confirmado.

- [ ] **Step 3: Hook sobreescribible en la base**

En `src/StockApp.Presentation/ViewModels/Movimientos/MovimientoRegistroViewModelBase.cs`:

1. Reemplazar los campos privados de navegación/confirmación por propiedades protegidas (el resto de los campos no cambia):

```csharp
    private readonly IMovimientoStockService _service;
    private readonly IProductoService        _productoService;

    /// <summary>Expuestos a las subclases para el hook post-registro (vínculo factura de Entrada).</summary>
    protected INavigationService   Navigation   { get; }
    protected IConfirmacionService Confirmacion { get; }
```

2. En el constructor, asignar `Navigation = navigation;` y `Confirmacion = confirmacion;` en lugar de `_navigation = ...` / `_confirmacion = ...`.

3. Reemplazar el cuerpo de `RegistrarAsync` para capturar el resultado y delegar la navegación en el hook:

```csharp
    [RelayCommand(CanExecute = nameof(PuedeRegistrar))]
    private async Task RegistrarAsync()
    {
        MensajeError = null;

        var dto = new RegistrarMovimientoDto(
            ProductoId:     ProductoSeleccionado!.Id,
            Tipo:           Tipo,
            Motivo:         Motivo,
            Cantidad:       Cantidad,
            PrecioUnitario: PrecioUnitario,
            Comentario:     Comentario);

        try
        {
            var registrado = await _service.RegistrarAsync(dto, forzar: false);
            await AlRegistradoAsync(registrado);
        }
        catch (StockInsuficienteException ex)
        {
            var mensaje = $"El stock quedará en {ex.StockResultante}. ¿Confirmar la salida igual?";
            var confirmar = await Confirmacion.PreguntarAsync(mensaje);

            if (confirmar)
            {
                var registrado = await _service.RegistrarAsync(dto, forzar: true);
                await AlRegistradoAsync(registrado);
            }
            // Si rechaza, no hace nada — el usuario puede corregir la cantidad
        }
        catch (Exception ex)
        {
            MensajeError = ex.Message;
        }
    }

    /// <summary>
    /// Hook post-registro: por defecto navega al historial (comportamiento histórico).
    /// EntradaRegistroViewModel lo sobreescribe para ofrecer el paso opcional
    /// "Asociar factura" (spec Finanzas §5.1).
    /// </summary>
    protected virtual Task AlRegistradoAsync(MovimientoRegistradoDto registrado)
    {
        Navigation.Navegar<MovimientoHistorialViewModel>();
        return Task.CompletedTask;
    }
```

- [ ] **Step 4: Override en EntradaRegistroViewModel**

Reemplazar `src/StockApp.Presentation/ViewModels/Movimientos/EntradaRegistroViewModel.cs` por:

```csharp
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Movimientos;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.ViewModels.Movimientos;

/// <summary>
/// Formulario de registro de ENTRADA de stock: tipo fijo <see cref="TipoMovimiento.Entrada"/>,
/// motivos habilitados restringidos a Compra y Ajuste. Tras registrar una COMPRA ofrece el
/// paso OPCIONAL "Asociar factura" (spec Finanzas §5.1): si el usuario acepta, navega al
/// formulario de gasto precargado con el movimiento y el monto sugerido (cantidad × precio,
/// editable — la factura real puede traer fletes o redondeos). Ajustes no llevan factura.
/// </summary>
public sealed partial class EntradaRegistroViewModel : MovimientoRegistroViewModelBase
{
    private static readonly IReadOnlyList<MotivoMovimiento> _motivosDisponibles =
        new[] { MotivoMovimiento.Compra, MotivoMovimiento.Ajuste };

    public override TipoMovimiento Tipo => TipoMovimiento.Entrada;

    public override IReadOnlyList<MotivoMovimiento> MotivosDisponibles => _motivosDisponibles;

    public override string Titulo => "Registrar Entrada";

    public EntradaRegistroViewModel(
        IMovimientoStockService service,
        IProductoService productoService,
        INavigationService navigation,
        IConfirmacionService confirmacion)
        : base(service, productoService, navigation, confirmacion)
    {
        Motivo = MotivosDisponibles[0];
    }

    protected override async Task AlRegistradoAsync(MovimientoRegistradoDto registrado)
    {
        if (registrado.Motivo != MotivoMovimiento.Compra)
        {
            await base.AlRegistradoAsync(registrado);
            return;
        }

        var asociar = await Confirmacion.PreguntarAsync(
            "Entrada registrada. ¿Desea asociar una factura de proveedor a esta entrada?");
        if (!asociar)
        {
            await base.AlRegistradoAsync(registrado);
            return;
        }

        var montoSugerido = registrado.Cantidad * registrado.PrecioUnitario;
        Navigation.Navegar<GastoFormViewModel>(vm =>
            vm.CargarDesdeEntrada(registrado.MovimientoId, montoSugerido));
    }
}
```

- [ ] **Step 5: Correr los tests y ver verde**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~EntradaRegistroFacturaTests"`
Expected: los 3 tests nuevos en verde.

- [ ] **Step 6: Suite completa de Presentation (regresión del refactor de la base)**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: toda la suite verde — en particular los tests existentes de `SalidaRegistroViewModel` y del flujo "¿forzar salida?" (el hook por defecto reproduce el comportamiento anterior).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation tests/StockApp.Presentation.Tests
git commit -m "feat(finanzas): paso opcional de asociar factura al registrar entrada de stock"
```

---

### Task 11: Cierre — suite completa + verificación orgánica

**Files:**
- Ninguno nuevo (solo correcciones que surjan de la verificación).

**Interfaces:**
- Consumes: todo lo anterior; contenedor `stockapp-pg` corriendo (convención del repo); credenciales de dev (admin `test123`).

- [ ] **Step 1: Suite completa de la solución**

Run: `dotnet test`
Expected: TODA la solución verde (los 1166 tests previos + los ~105 nuevos de esta fase), con Docker corriendo para Testcontainers.

- [ ] **Step 2: Verificación orgánica (convención del repo: probar con la app real)**

1. Levantar la API: `dotnet run --project src/StockApp.Api` (aplica la migración `FinanzasGastos` sola contra `stockapp-pg`).
2. Levantar el desktop: `dotnet run --project src/StockApp.Presentation` y loguearse como admin.
3. **Precondición**: en "Maestros de finanzas" debe haber al menos 1 fuente, 1 rubro y 1 línea POA con asignación chica (para provocar el sobregiro). Crearlos si la base de dev está vacía.
4. Checklist funcional (el usuario reporta = ya probó; verificar TODO antes de dar por cerrado):
   - [ ] Sidebar muestra "Gastos y facturas" e "Ingresos de caja" en la sección Finanzas (Admin y Operador).
   - [ ] Nuevo gasto CONTADO con factura → aparece en la grilla con estado **Pagada** (pago automático) y montos formateados es-UY.
   - [ ] Nuevo gasto CRÉDITO sin vencimiento → error claro en el formulario (no crashea).
   - [ ] Nuevo gasto CRÉDITO con vencimiento pasado → estado **Vencida** en rojo conceptual (columna Estado).
   - [ ] Gasto con línea POA cuyo presupuesto se supera → se guarda IGUAL y aparece el aviso de sobregiro.
   - [ ] "Pagos": pago parcial → estado **Parcial**; pagar de más → error 409 mostrado; completar el saldo → **Pagada**; anular un pago → vuelve a **Parcial/Pendiente**.
   - [ ] Anular gasto con pagos activos → error claro; anular los pagos primero → anulación OK y estado **Anulada**.
   - [ ] Filtros de la grilla (fechas, proveedor, fuente, estado) y "Exportar CSV" (abrir el archivo y verificar separadores/columnas).
   - [ ] Ingresos: alta / edición / baja lógica, montos es-UY.
   - [ ] Entrada de stock motivo COMPRA → pregunta "¿Asociar factura?"; aceptar → formulario con monto precargado cantidad × precio (editable); guardar → el gasto queda en la grilla. Registrar otra entrada con el MISMO número de factura → ofrece asociar a la existente.
   - [ ] Entrada motivo AJUSTE → NO pregunta nada (flujo histórico intacto).
   - [ ] Auditoría: el log muestra las acciones nuevas (AltaGasto, AltaPagoGasto, etc.).
5. Dejar el contenedor `stockapp-pg` corriendo (convención del repo).

- [ ] **Step 3: Commit final (solo si la verificación orgánica exigió correcciones)**

```bash
git add -A
git commit -m "fix(finanzas): ajustes de la verificación orgánica de la fase 2"
```

---

## Cobertura del spec (self-check)

| Ítem del spec F2 | Dónde |
|---|---|
| `Gasto` cabecera única con dimensiones (§4) | Task 1 (entidad) / Task 3 (reglas) |
| `PagoGasto` + contado ⇒ pago automático (§4) | Task 1 / Task 3 |
| `IngresoCaja` (§4) | Tasks 1, 2, 4, 6, 7, 9 |
| Estado CALCULADO, nunca persistido (§4) | Task 1 (`CalcularEstado` + tests) — sin columna en la migración |
| `MovimientoStock.GastoId?` FK opcional (§4) | Task 1 (migración) |
| Paso opcional "Asociar factura" en la entrada (§5.1) | Task 10 |
| Factura existente → buscar y asociar (§5.1) | Task 3 (`AsociarMovimientosAsync`) / Task 8 (flujo en el form) |
| Monto editable precargado cantidad × precio (§5.3) | Task 8 (`CargarDesdeEntrada`) / Task 10 |
| Registrar pago desde la factura (§5.4) | Tasks 3, 5, 8 (PagosGastoViewModel) |
| Pantalla "Gastos y facturas" con filtros + export CSV (§7.1) | Tasks 8, 9 |
| Pantalla "Ingresos de caja" (§7.2) | Tasks 4, 6, 9 |
| Permisos granulares Admin+Operador (§9) | Task 3 (Permisos + AccionesOperador) |
| No pagar más que el saldo; crédito exige vencimiento; no anular con pagos; fuente compatible con la línea; sobregiro advierte sin bloquear; maestros de baja no se ofrecen (§10) | Task 3 + matriz de API Task 5 |
| Auditoría append-only 31–39 (§10) | Task 1 (enum) / Tasks 3-4 (llamadas) |
| Fuera de alcance: adjuntos, libro caja/control POA/calendario, importador | No aparecen en ninguna task (F3/F4/F5) |

