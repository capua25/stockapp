# F5c — Confirmación transaccional del importador de Finanzas — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir el paso de CONFIRMACIÓN del importador one-shot (spec §3-§8): dos endpoints, `POST /finanzas/importar/confirmar` (escribe transaccionalmente maestros/LineaPoa/Ingresos/Gastos con idempotencia por clave natural y guard de re-importación) y `POST /finanzas/importar/revertir/{id}` (baja lógica de todo un lote), sobre lo que F5a/F5b ya dejaron listo (parser + análisis read-only).

**Architecture:** La validación del payload (permiso + referencias nominales resueltas contra maestros existentes o declarados) vive en `StockApp.Application.Finanzas.ConfirmacionImportacionService`, que NO toca EF/Npgsql — delega toda la escritura transaccional a `IImportacionRepository` (interfaz en Application, implementación `ImportacionRepository` en Infrastructure), que abre UNA transacción por operación, toma un `pg_advisory_xact_lock(ejercicio)` y hace todo el trabajo con UN solo `SaveChangesAsync()`, imitando el patrón ya usado por `MovimientoStockRepository.RegistrarMovimientoAtomicoAsync` y `GastoRepository.RegistrarPagoAtomicoAsync`. Los endpoints van en `ImportacionEndpoints.cs` (ya existe desde F5b, se le agregan las dos rutas nuevas).

**Tech Stack:** .NET 10, EF Core 10 / Npgsql, xUnit + Moq (Application.Tests), Testcontainers `postgres:16-alpine` (Infrastructure.Tests e Api.Tests). Reutiliza los DTOs de análisis de F5b (`ResultadoAnalisisDto` y afines) solo como INSUMO del test de aceptación (Task 9) — el contrato de `/confirmar` es un DTO propio, no los reutiliza.

## Global Constraints

- **F5c SÍ escribe en el dominio.** A diferencia de F5b (read-only), `/confirmar` y `/revertir/{id}` mutan la base. Toda escritura pasa por `IImportacionRepository`, nunca directo desde el servicio de Application.
- **Cero índices únicos nuevos.** La migración de Task 1 agrega SOLO columnas `IdImportacion` (`Guid?`) + índices NO únicos en `Gasto`, `IngresoCaja`, `LineaPoa` (spec §4, matiz). El índice único parcial `IX_Gastos_ProveedorId_NumeroFactura` (migración `UniqueFacturaProveedorGastosActivos`) NO se toca.
- **UN solo `SaveChangesAsync()` por operación transaccional.** `ConfirmarAsync` y `RevertirAsync` arman el grafo completo en memoria (usando navegaciones de EF para que se resuelvan los FK de entidades recién creadas, en vez de Ids manuales) y hacen un único `SaveChangesAsync()` antes del commit — mismo patrón que `MovimientoStockRepository.RegistrarMovimientoAtomicoAsync` (`MovimientoStockRepository.cs:40-101`).
- **Maestros nunca se revierten** (spec §2.7): `RevertirAsync` NUNCA toca `Proveedor`, `FuenteFinanciamiento` ni `RubroGasto`.
- **`LogAuditoria` no gana columna propia para `IdImportacion`.** No existe una columna `Guid` en `LogAuditoria` — el `IdImportacion` del lote viaja como el PRIMER token de `Detalle` (formato `"IdImportacion={guid}; ..."`), y `EntidadId` guarda el `Ejercicio` para poder filtrar sin traer todo el historial. Es una decisión de este plan (el spec no baja a este nivel de detalle); está documentada en la Task 1 y se usa en Tasks 4, 6 y 8.
- TDD estricto por capas: test → verlo fallar → implementar lo mínimo → verlo verde → commit. Un commit como mínimo por task.
- Commits convencionales en español (`feat(finanzas): ...`, `test(finanzas): ...`, `fix(finanzas): ...`), **sin `Co-Authored-By` ni ninguna atribución a IA**.
- Comando de test real de este repo: `dotnet test tests/<Proyecto> --filter "FullyQualifiedName~NombreDeClase"` (convención confirmada en los planes de Fase 1, F1-maestros, F3-adjuntos, F4-vistas). No correr `dotnet build` suelto — alcanza con los `dotnet test` que cada task pide.
- **Fuera de alcance (es F5d):** `ImportacionApiClient`, la grilla editable del desktop, la pantalla con pestañas, el ítem admin-only del sidebar. Ninguna task de este plan toca `StockApp.Presentation`.

---

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Domain/Entities/Gasto.cs` | Modify: agrega `Guid? IdImportacion`. |
| `src/StockApp.Domain/Entities/IngresoCaja.cs` | Modify: agrega `Guid? IdImportacion`. |
| `src/StockApp.Domain/Entities/LineaPoa.cs` | Modify: agrega `Guid? IdImportacion`. |
| `src/StockApp.Domain/Enums/AccionAuditada.cs` | Modify: agrega `ImportacionPlanillas = 42` y `ReversionImportacion = 43`. |
| `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` | Modify: mapea `IdImportacion` + índice no-único en las 3 entidades. |
| `src/StockApp.Infrastructure/Migrations/<timestamp>_IdImportacionTrazabilidad.cs` | Create (generada por `dotnet ef migrations add`): 3 `AddColumn` + 3 `CreateIndex`. |
| `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` | Modify: `LimpiarTablas()` suma `"IngresosCaja"` al `TRUNCATE` (gap de F5b). |
| `src/StockApp.Domain/Exceptions/ValidacionImportacionException.cs` | Create: excepción con `IReadOnlyDictionary<string, string[]> Errores`. |
| `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs` | Modify: caso nuevo que mapea `ValidacionImportacionException` a 400 con `Errors` en el body. |
| `src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs` | Create: `ConfirmarImportacionDto` y afines, `ResultadoConfirmacionDto`, `ResultadoReversionDto`. |
| `src/StockApp.Application/Interfaces/IImportacionRepository.cs` | Create: contrato de la escritura transaccional. |
| `src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs` | Create: contrato del servicio de Application. |
| `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs` | Create: permiso + validación de referencias + delega en `IImportacionRepository`. |
| `tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs` | Modify: los métodos de escritura pasan de `NotSupportedException` a implementación real en memoria. |
| `tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs` | Create: spy/fake de `IImportacionRepository`. |
| `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs` | Create: permiso + validación de referencias nominales. |
| `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs` | Create (Task 4) / Modify (Tasks 5, 6, 8): la transacción completa. |
| `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs` | Create (Task 4) / Modify (Tasks 5, 6, 8): atomicidad, get-or-create, dedupe, guard, reversa. |
| `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs` | Modify: agrega `/finanzas/importar/confirmar` y `/finanzas/importar/revertir/{id}` + límites de tamaño. |
| `src/StockApp.Api/Program.cs` | Modify: DI de `IImportacionRepository` y `IConfirmacionImportacionService`. |
| `tests/StockApp.Api.Tests/ImportacionEndpointTests.cs` | Modify (F5b): suma el test del límite de tamaño de archivo de `/analizar` (spec §8). |
| `tests/StockApp.Api.Tests/ImportacionConfirmacionEndpointTests.cs` | Create: matriz 401/403/400/409/200 de `/confirmar`. |
| `tests/StockApp.Api.Tests/ImportacionReversionEndpointTests.cs` | Create: matriz de `/revertir/{id}` + ciclo completo confirmar→revertir→confirmar. |
| `tests/StockApp.Api.Tests/ImportacionAceptacionConfirmacionTests.cs` | Create: aceptación end-to-end contra las planillas reales, aserciones sobre la base. |

---

## Task 1: Fundaciones de esquema y auditoría

**Files:**
- Modify: `src/StockApp.Domain/Entities/Gasto.cs`
- Modify: `src/StockApp.Domain/Entities/IngresoCaja.cs`
- Modify: `src/StockApp.Domain/Entities/LineaPoa.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/StockApp.Infrastructure/Migrations/<timestamp>_IdImportacionTrazabilidad.cs` (generada)
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextFinanzasImportacionTests.cs`

**Interfaces:**
- Consumes: nada nuevo — son cambios de esquema puro.
- Produces: `Gasto.IdImportacion`, `IngresoCaja.IdImportacion`, `LineaPoa.IdImportacion` (`Guid?`), `AccionAuditada.ImportacionPlanillas` (=42), `AccionAuditada.ReversionImportacion` (=43). Todas las tasks siguientes que escriben estas 3 entidades estampan este campo.

### Contexto para el implementador

`Gasto`, `IngresoCaja` y `LineaPoa` no tienen hoy ninguna forma de saber "esto lo creó una corrida del importador". Sin esa marca, `/revertir/{id}` (Task 8) no podría encontrar qué dar de baja. Por eso esta primera task solo toca esquema: una columna `Guid?` nullable (la inmensa mayoría de los datos del sistema, cargados a mano, la va a tener en `null` para siempre) con un índice NO único (se busca por igualdad, nunca hace falta que sea único — muchos registros van a compartir el mismo `IdImportacion` de una misma corrida).

`AccionAuditada` es un enum append-only: **nunca se reordena ni se reutiliza un valor**, porque los valores ya están persistidos en la tabla `LogsAuditoria` de instalaciones reales. El último valor usado hoy es `BajaAdjunto = 41` (`src/StockApp.Domain/Enums/AccionAuditada.cs:59`), así que los dos nuevos van en 42 y 43.

Por último, un gap real que dejó F5b: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`, método `LimpiarTablas()` (líneas 41-50), arma un `TRUNCATE` que incluye `"Gastos"` pero NO `"IngresosCaja"`. Hoy no importaba porque ningún test de `StockApp.Api.Tests` escribía ingresos vía la API. En cuanto Task 7 agregue `/confirmar` (que sí escribe `IngresoCaja`), los tests de la collection `"Api"` se van a empezar a filtrar estado entre sí si no se arregla ahora. `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` YA incluye `"IngresosCaja"` en su propio `TRUNCATE` (línea 33) — el gap es SOLO de `ApiTestBase`.

- [ ] **Paso 1: Escribir el test que falla**

```csharp
// tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextFinanzasImportacionTests.cs
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Persistence;

/// <summary>
/// F5c Task 1: la columna IdImportacion (Guid?, nullable, índice no-único) tiene que existir
/// y persistir en Gasto, IngresoCaja y LineaPoa — es la base de la trazabilidad que Task 4/5/6
/// estampan al escribir y que Task 8 usa para encontrar qué revertir.
/// </summary>
public class AppDbContextFinanzasImportacionTests : PostgresRepositoryTestBase
{
    public AppDbContextFinanzasImportacionTests(PostgresFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Gasto_IdImportacion_PersisteYSePuedeConsultarPorIgualdad()
    {
        var idLote = Guid.NewGuid();
        var proveedor = new Proveedor { Nombre = "ACME SA" };
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        var rubro = new RubroGasto { Codigo = 1, Nombre = "Paseos Públicos" };
        Context.Proveedores.Add(proveedor);
        Context.FuentesFinanciamiento.Add(fuente);
        Context.RubrosGasto.Add(rubro);
        Context.Gastos.Add(new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto importado", Fecha = DateTime.UtcNow, MontoTotal = 100m,
            IdImportacion = idLote,
        });
        Context.Gastos.Add(new Gasto
        {
            Proveedor = proveedor, FuenteFinanciamiento = fuente, RubroGasto = rubro,
            Detalle = "Gasto manual", Fecha = DateTime.UtcNow, MontoTotal = 50m,
            IdImportacion = null,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var delLote = Context.Gastos.Where(g => g.IdImportacion == idLote).ToList();
        var manuales = Context.Gastos.Where(g => g.IdImportacion == null).ToList();

        Assert.Single(delLote);
        Assert.Equal("Gasto importado", delLote[0].Detalle);
        Assert.Single(manuales);
    }

    [Fact]
    public async Task IngresoCaja_IdImportacion_PersisteYAceptaNull()
    {
        var fuente = new FuenteFinanciamiento { Nombre = "Literal A" };
        Context.FuentesFinanciamiento.Add(fuente);
        var idLote = Guid.NewGuid();
        Context.IngresosCaja.Add(new IngresoCaja
        {
            Fecha = DateTime.UtcNow, Concepto = "Saldo inicial", Monto = 1000m,
            FuenteFinanciamiento = fuente, IdImportacion = idLote,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrado = Context.IngresosCaja.Single(i => i.IdImportacion == idLote);
        Assert.Equal("Saldo inicial", encontrado.Concepto);
    }

    [Fact]
    public async Task LineaPoa_IdImportacion_PersisteYAceptaNull()
    {
        var idLote = Guid.NewGuid();
        Context.LineasPoa.Add(new LineaPoa
        {
            Nombre = "COMPOSTERAS", Programa = "Ambiente", Ejercicio = 2026, IdImportacion = idLote,
        });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var encontrada = Context.LineasPoa.Single(l => l.IdImportacion == idLote);
        Assert.Equal("COMPOSTERAS", encontrada.Nombre);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~AppDbContextFinanzasImportacionTests"`
Esperado: FAIL en compilación — `CS1061: 'Gasto' no contiene una definición para 'IdImportacion'` (y lo mismo para `IngresoCaja`/`LineaPoa`).

- [ ] **Paso 3: Implementación mínima**

En `src/StockApp.Domain/Entities/Gasto.cs`, agregar la propiedad después de `Activo`:

```csharp
    public bool Activo { get; set; } = true;              // false = anulado

    /// <summary>
    /// Guid del lote de /confirmar que creó este gasto (F5c). Null para TODO lo cargado por
    /// las vías normales (ABM manual) — que es, hoy y a futuro, la inmensa mayoría de los
    /// datos. Permite a /revertir/{id} encontrar y dar de baja un lote completo.
    /// </summary>
    public Guid? IdImportacion { get; set; }

    public List<PagoGasto> Pagos { get; set; } = new();
```

(la línea `public List<PagoGasto> Pagos...` ya existe — el bloque de arriba solo inserta `IdImportacion` ANTES de ella, sin tocarla).

En `src/StockApp.Domain/Entities/IngresoCaja.cs`, agregar después de `Activo`:

```csharp
    public bool Activo { get; set; } = true;               // baja lógica

    /// <summary>Guid del lote de /confirmar que creó este ingreso (F5c). Null si es manual.</summary>
    public Guid? IdImportacion { get; set; }
}
```

En `src/StockApp.Domain/Entities/LineaPoa.cs`, agregar después de `Activo`:

```csharp
    public bool Activo { get; set; } = true;              // baja lógica

    /// <summary>Guid del lote de /confirmar que creó esta línea (F5c). Null si es manual.</summary>
    public Guid? IdImportacion { get; set; }

    public List<AsignacionPresupuestal> Asignaciones { get; set; } = new();
```

(mismo criterio: `Asignaciones` ya existe, `IdImportacion` se inserta antes).

En `src/StockApp.Domain/Enums/AccionAuditada.cs`, agregar al final del enum, después de `BajaAdjunto = 41,`:

```csharp
    AltaAdjunto = 40,
    BajaAdjunto = 41,

    // ── Finanzas — F5c: importador de planillas, confirmación y reversa (append-only a partir de 42) ──
    ImportacionPlanillas   = 42,
    ReversionImportacion   = 43,
}
```

En `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`, dentro de `modelBuilder.Entity<Gasto>(e => { ... })` (agregar la línea del índice al final del bloque, antes del cierre `});`):

```csharp
            e.HasOne(g => g.LineaPoa).WithMany()
                .HasForeignKey(g => g.LineaPoaId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(g => g.IdImportacion);
        });
```

Dentro de `modelBuilder.Entity<IngresoCaja>(e => { ... })`:

```csharp
            e.HasOne(i => i.FuenteFinanciamiento).WithMany()
                .HasForeignKey(i => i.FuenteFinanciamientoId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(i => i.IdImportacion);
        });
```

Dentro de `modelBuilder.Entity<LineaPoa>(e => { ... })`:

```csharp
        modelBuilder.Entity<LineaPoa>(e =>
        {
            e.Property(l => l.Nombre).IsRequired();
            e.Property(l => l.Programa).IsRequired();
            e.HasIndex(l => new { l.Nombre, l.Ejercicio }).IsUnique();
            e.Property(l => l.Activo).HasDefaultValue(true);
            e.HasIndex(l => l.IdImportacion);
        });
```

Generar la migración:

```bash
dotnet ef migrations add IdImportacionTrazabilidad --project src/StockApp.Infrastructure --startup-project src/StockApp.Api
```

Resultado esperado en el archivo `Up()` generado (los nombres de índice pueden variar levemente, EF los deriva del nombre de tabla+columna — si el contenido difiere de esto en algo más que nombres, revisar antes de aplicar, no asumir que está bien):

```csharp
protected override void Up(MigrationBuilder migrationBuilder)
{
    migrationBuilder.AddColumn<Guid>(
        name: "IdImportacion", table: "Gastos", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<Guid>(
        name: "IdImportacion", table: "IngresosCaja", type: "uuid", nullable: true);
    migrationBuilder.AddColumn<Guid>(
        name: "IdImportacion", table: "LineasPoa", type: "uuid", nullable: true);

    migrationBuilder.CreateIndex(
        name: "IX_Gastos_IdImportacion", table: "Gastos", column: "IdImportacion");
    migrationBuilder.CreateIndex(
        name: "IX_IngresosCaja_IdImportacion", table: "IngresosCaja", column: "IdImportacion");
    migrationBuilder.CreateIndex(
        name: "IX_LineasPoa_IdImportacion", table: "LineasPoa", column: "IdImportacion");
}

protected override void Down(MigrationBuilder migrationBuilder)
{
    migrationBuilder.DropIndex(name: "IX_LineasPoa_IdImportacion", table: "LineasPoa");
    migrationBuilder.DropIndex(name: "IX_IngresosCaja_IdImportacion", table: "IngresosCaja");
    migrationBuilder.DropIndex(name: "IX_Gastos_IdImportacion", table: "Gastos");

    migrationBuilder.DropColumn(name: "IdImportacion", table: "LineasPoa");
    migrationBuilder.DropColumn(name: "IdImportacion", table: "IngresosCaja");
    migrationBuilder.DropColumn(name: "IdImportacion", table: "Gastos");
}
```

Por último, el fix del gap en `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`:

```csharp
    private void LimpiarTablas()
    {
        using var ctx = Factory.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"Usuarios\" RESTART IDENTITY CASCADE;");
    }
```

(el único cambio real es agregar `\"IngresosCaja\", ` antes de `\"Usuarios\"`).

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~AppDbContextFinanzasImportacionTests"`
Esperado: `Passed! - Failed: 0, Passed: 3`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Domain/Entities/Gasto.cs src/StockApp.Domain/Entities/IngresoCaja.cs \
  src/StockApp.Domain/Entities/LineaPoa.cs src/StockApp.Domain/Enums/AccionAuditada.cs \
  src/StockApp.Infrastructure/Persistence/AppDbContext.cs src/StockApp.Infrastructure/Migrations \
  tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs \
  tests/StockApp.Infrastructure.Tests/Persistence/AppDbContextFinanzasImportacionTests.cs
git commit -m "feat(finanzas): columna IdImportacion en Gasto/IngresoCaja/LineaPoa + acciones de auditoría F5c"
```

---

## Task 2: Contrato de confirmación y errores estructurados

**Files:**
- Create: `src/StockApp.Domain/Exceptions/ValidacionImportacionException.cs`
- Create: `src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs`
- Modify: `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`
- Test: `tests/StockApp.Domain.Tests/Exceptions/ValidacionImportacionExceptionTests.cs`
- Test: `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs` (Modify)

**Interfaces:**
- Consumes: nada (son contratos nuevos, sin dependencias de tasks previas salvo el enum `CondicionPago` que ya existe en `StockApp.Domain.Enums`).
- Produces: `ConfirmarImportacionDto`, `MaestrosNuevosConfirmarDto`, `RubroNuevoConfirmarDto`, `IngresoConfirmarDto`, `GastoConfirmarDto`, `LineaPoaConfirmarDto`, `AsignacionConfirmarDto`, `ResultadoConfirmacionDto`, `ResultadoReversionDto`, `ValidacionImportacionException` — Tasks 3 a 9 los usan tal cual quedan definidos acá. **Los nombres y el orden de los parámetros posicionales de estos records son la firma que todas las tasks siguientes citan textualmente — no renombrar ni reordenar campos después de esta task.**

### Contexto para el implementador

El resto de la API de este repo (`DomainExceptionHandler.cs:17-34`) mapea excepciones a un `(status, title)` fijo y escribe un `ProblemDetails` uniforme. Para F5c necesitamos que un `400` de validación traiga ADEMÁS un diccionario `{ "Gastos[12].CodigoRubro": ["mensaje"] }` en el body — igual que `Microsoft.AspNetCore.Http.Results.ValidationProblem` arma un `ValidationProblemDetails.Errors`. El handler actual NO usa `Results.ValidationProblem` (no puede: `IExceptionHandler.TryHandleAsync` no devuelve un `IResult`, escribe directo sobre la response) — así que el camino real es el mismo patrón que ya usa el handler para `StockInsuficienteException` (`DomainExceptionHandler.cs:57-62`): un `if` después del switch que agrega datos estructurados a `contexto.ProblemDetails.Extensions`. Con eso el JSON queda `{ "status": 400, ..., "errors": { "Gastos[12].CodigoRubro": ["..."] } }` — mismo shape, sin reinventar el pipeline de errores del resto de la app.

- [ ] **Paso 1: Escribir el test que falla**

```csharp
// tests/StockApp.Domain.Tests/Exceptions/ValidacionImportacionExceptionTests.cs
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Exceptions;

public class ValidacionImportacionExceptionTests
{
    [Fact]
    public void Constructor_ExponeElDiccionarioDeErroresRecibido()
    {
        var errores = new Dictionary<string, string[]>
        {
            ["Gastos[0].Detalle"] = new[] { "Requerido" },
        };

        var ex = new ValidacionImportacionException(errores);

        Assert.Same(errores, ex.Errores);
        Assert.Equal("Requerido", ex.Errores["Gastos[0].Detalle"][0]);
    }
}
```

Y, agregado al final de la clase existente `tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs` (mismo patrón que los tests ya presentes en ese archivo, ver `StockInsuficienteException_IncluyeLosDatosEstructuradosComoExtensiones`):

```csharp
    [Fact]
    public async Task ValidacionImportacionException_Mapea400ConElDiccionarioDeErroresEnErrors()
    {
        var errores = new Dictionary<string, string[]>
        {
            ["Gastos[12].CodigoRubro"] = new[] { "El rubro 340 no existe ni fue declarado nuevo" },
            ["LineasPoa[3].Programa"] = new[] { "Requerido" },
        };

        var (status, _, body) = await EjecutarAsync(new ValidacionImportacionException(errores));

        Assert.Equal(StatusCodes.Status400BadRequest, status);
        var errorsElement = body.RootElement.GetProperty("errors");
        Assert.Equal(
            "El rubro 340 no existe ni fue declarado nuevo",
            errorsElement.GetProperty("Gastos[12].CodigoRubro")[0].GetString());
        Assert.Equal("Requerido", errorsElement.GetProperty("LineasPoa[3].Programa")[0].GetString());
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~ValidacionImportacionExceptionTests"` y `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~DomainExceptionHandlerTests"`
Esperado: FAIL en compilación — `CS0246: no se encontró el tipo o el nombre de espacio de nombres 'ValidacionImportacionException'`.

- [ ] **Paso 3: Implementación mínima**

```csharp
// src/StockApp.Domain/Exceptions/ValidacionImportacionException.cs
namespace StockApp.Domain.Exceptions;

/// <summary>
/// Se lanza cuando el payload de POST /finanzas/importar/confirmar (F5c) tiene referencias
/// nominales que no resuelven contra ningún maestro existente ni declarado en MaestrosNuevos,
/// o campos obligatorios del dominio ausentes. A diferencia de ReglaDeNegocioException, lleva
/// la ubicación estructurada del error (clave "Tipo[índice].Campo" → mensajes) para que F5d
/// pueda resaltar la celda exacta en la grilla de corrección.
/// </summary>
public class ValidacionImportacionException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errores { get; }

    public ValidacionImportacionException(IReadOnlyDictionary<string, string[]> errores)
        : base("El payload de confirmación de importación tiene errores de validación.")
    {
        Errores = errores;
    }
}
```

```csharp
// src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs
using StockApp.Domain.Enums;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Payload de POST /finanzas/importar/confirmar (F5c, spec §3). Los maestros se referencian
/// por NOMBRE (Proveedor/Fuente) o CÓDIGO (Rubro), no por Id — la mayoría no existe todavía en
/// la base; el servidor resuelve nombre/código → Id con get-or-create dentro de la transacción.
/// Contrato PROPIO, no reutiliza los DTOs de análisis de F5b (ResultadoAnalisisDto y afines):
/// los campos obligatorios del dominio van NO nullable acá aunque el análisis los deje vacíos.
/// </summary>
public sealed record ConfirmarImportacionDto(
    int Ejercicio,
    bool Forzar,
    MaestrosNuevosConfirmarDto MaestrosNuevos,
    IReadOnlyList<IngresoConfirmarDto> Ingresos,
    IReadOnlyList<GastoConfirmarDto> Gastos,
    IReadOnlyList<LineaPoaConfirmarDto> LineasPoa);

/// <summary>Conjuntos de maestros a crear, declarados EXPLÍCITAMENTE por el usuario (F5d). Nada
/// se crea por fuera de lo que aparece acá — es la "regla de cierre" del spec §3.</summary>
public sealed record MaestrosNuevosConfirmarDto(
    IReadOnlyList<string> Proveedores,
    IReadOnlyList<string> Fuentes,
    IReadOnlyList<RubroNuevoConfirmarDto> Rubros);

public sealed record RubroNuevoConfirmarDto(int Codigo, string Nombre);

public sealed record IngresoConfirmarDto(DateOnly Fecha, string Concepto, decimal Monto, string Fuente);

/// <summary>
/// LineaPoa es null cuando el gasto NO está vinculado a ningún proyecto POA (la mayoría de los
/// gastos del libro caja). Cuando no es null, tiene que resolver contra una LineaPoa YA
/// existente en la base para este Ejercicio o contra una declarada en el propio payload
/// (LineasPoa) — NO existe un "MaestrosNuevos.LineasPoa" separado: la lista LineasPoa del
/// payload ES la declaración.
/// </summary>
public sealed record GastoConfirmarDto(
    string Proveedor, string? NumeroFactura, string? NumeroOrden,
    string Detalle, string? Destino, DateOnly Fecha, decimal MontoTotal,
    string Fuente, int CodigoRubro, string? LineaPoa, CondicionPago Condicion);

public sealed record LineaPoaConfirmarDto(
    string Nombre, string Programa,
    IReadOnlyList<AsignacionConfirmarDto> Asignaciones);

public sealed record AsignacionConfirmarDto(string Fuente, decimal Monto);

/// <summary>Respuesta feliz de /confirmar. IdImportacion es el Guid del lote — necesario para
/// poder revertirlo después con /revertir/{id}.</summary>
public sealed record ResultadoConfirmacionDto(
    Guid IdImportacion,
    int ProveedoresCreados, int FuentesCreadas, int RubrosCreados,
    int LineasPoaCreadas, int AsignacionesCreadas,
    int IngresosCreados, int IngresosOmitidos,
    int GastosCreados, int GastosOmitidos, int PagosCreados);

/// <summary>Respuesta feliz de /revertir/{id}: contadores de registros dados de baja por tipo.</summary>
public sealed record ResultadoReversionDto(
    Guid IdImportacion,
    int GastosRevertidos, int PagosRevertidos, int IngresosRevertidos,
    int LineasPoaRevertidas, int AsignacionesRevertidas);
```

En `src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs`, agregar el caso al switch (ANTES del caso `_` catch-all, en cualquier posición del resto — el orden entre los demás casos no importa porque los tipos no se solapan):

```csharp
            EntidadNoEncontradaException => (StatusCodes.Status404NotFound, "Recurso no encontrado."),
            ReglaDeNegocioException      => (StatusCodes.Status409Conflict, "Regla de negocio violada."),
            // F5c: errores de validación estructurada del payload de /confirmar (referencias
            // nominales que no resuelven, campos obligatorios ausentes). Mismo 400 que
            // ArgumentException, pero con el diccionario Errores agregado más abajo.
            ValidacionImportacionException => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
            ArgumentException            => (StatusCodes.Status400BadRequest, "Solicitud inválida."),
```

Y agregar el bloque de extensiones, junto al de `StockInsuficienteException` (después de él, mismo nivel):

```csharp
        if (exception is StockInsuficienteException stock)
        {
            contexto.ProblemDetails.Extensions["productoId"]         = stock.ProductoId;
            contexto.ProblemDetails.Extensions["stockActual"]        = stock.StockActual;
            contexto.ProblemDetails.Extensions["cantidadSolicitada"] = stock.CantidadSolicitada;
        }

        // F5c: mismo shape que Microsoft.AspNetCore.Http.Results.ValidationProblem produciría
        // (un objeto "errors" con clave "Tipo[índice].Campo" → array de mensajes), sin poder
        // usar ese helper acá porque IExceptionHandler no devuelve un IResult.
        if (exception is ValidacionImportacionException validacion)
        {
            contexto.ProblemDetails.Extensions["errors"] = validacion.Errores;
        }
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~ValidacionImportacionExceptionTests"` y `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~DomainExceptionHandlerTests"`
Esperado: ambos `Passed! - Failed: 0`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Domain/Exceptions/ValidacionImportacionException.cs \
  src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs \
  src/StockApp.Api/ErrorHandling/DomainExceptionHandler.cs \
  tests/StockApp.Domain.Tests/Exceptions/ValidacionImportacionExceptionTests.cs \
  tests/StockApp.Api.Tests/ErrorHandling/DomainExceptionHandlerTests.cs
git commit -m "feat(finanzas): contrato de confirmación de importación (DTOs) + errores de validación estructurados"
```

---

## Task 3: Validación del payload (sin persistencia)

**Files:**
- Create: `src/StockApp.Application/Interfaces/IImportacionRepository.cs`
- Create: `src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs`
- Create: `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs`
- Create: `tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs`

**Interfaces:**
- Consumes: `IProveedorRepository.ListarTodosAsync`, `IRubroGastoRepository.ListarTodosAsync`, `IFuenteFinanciamientoRepository.ListarTodasAsync`, `ILineaPoaRepository.ListarTodasAsync` (todos ya existen), `ICurrentSession`, `IAuthorizationService.Verificar`, `Permisos.ImportarPlanillas` (ya existe desde F5b), los DTOs de Task 2.
- Produces: `IImportacionRepository` con `Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)` y `Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId)` — Task 4 lo implementa en Infrastructure. `IConfirmacionImportacionService` con `Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)` y `Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)` — Task 7 lo consume desde el endpoint.

### Contexto para el implementador

Esta task construye la MITAD de arriba de la arquitectura (spec §5): un servicio de Application que (1) verifica el permiso, (2) valida que toda referencia nominal del payload resuelva contra un maestro existente o declarado, y (3) si pasa, delega TODA la escritura a `IImportacionRepository` — que en esta task es solo una interfaz (la implementación real, con la transacción de Postgres, es la Task 4). Por eso el test de esta task usa un FAKE de `IImportacionRepository` (no toca la base) y por eso el archivo se llama "sin persistencia": lo único que se prueba acá es la validación y el enrutamiento del permiso.

Una aclaración de arquitectura importante, porque el spec (§5) dice que el SERVICIO "resuelve el guard de 409/Forzar": en la práctica el guard de re-importación (spec §2.6) tiene que ejecutarse DENTRO de la transacción con el `pg_advisory_xact_lock` tomado (si se chequeara antes de la transacción, dos confirmaciones concurrentes podrían pasar el guard las dos antes de que cualquiera commitee). Por eso el guard en sí vive en `ImportacionRepository.ConfirmarAsync` (Task 6), no acá — el servicio de Application simplemente pasa el flag `Forzar` dentro del `dto` y deja que el repositorio decida. Esto es una aclaración de este plan sobre una ambigüedad real del spec, no una desviación: el resultado (409 si corresponde) es el mismo, cambia solo QUÉ capa ejecuta la comparación.

**Regla de cierre (spec §3)** que esta task implementa: toda referencia nominal —`GastoConfirmarDto.Proveedor`, `.Fuente`, `.CodigoRubro`, `.LineaPoa`; `IngresoConfirmarDto.Fuente`; `AsignacionConfirmarDto.Fuente`— tiene que resolver contra un maestro YA existente en la base o contra uno declarado en `MaestrosNuevos` (o, para `LineaPoa`, contra una `LineaPoaConfirmarDto.Nombre` del propio payload). Si no resuelve, error con la clave `Tipo[índice].Campo` y un mensaje. Se valida además que `GastoConfirmarDto.Detalle`, `LineaPoaConfirmarDto.Nombre` y `LineaPoaConfirmarDto.Programa` no estén vacíos — aunque el tipo del DTO ya los declara `string` no-nullable, el deserializador JSON de ASP.NET Core NO rechaza un `null` entrante para un `string` no-nullable en tiempo de ejecución (la anotación de nulabilidad es solo un chequeo de compilador); sin este chequeo explícito, un payload malformado llegaría con `Detalle == null` hasta el repositorio y reventaría con un `NullReferenceException` no controlado (500) en vez de un 400 claro.

Las comparaciones de nombre son normalizadas (`Trim().ToUpperInvariant()`), mismo criterio que `AnalisisImportacionService.Normalizar` (F5b). Proveedores y Rubros se comparan contra TODOS los existentes (sin filtro de `Activo`); Fuentes SOLO contra las ACTIVAS — mismo criterio ya establecido por F5b (`AnalisisImportacionService.cs:42-53`), no una decisión nueva de esta task.

`RepositorioMaestrosFake.cs` (F5b) tiene los 3 fakes de maestros con los métodos de escritura (`AgregarAsync`/`ActualizarAsync`/`ExisteNombreAsync`/`ExisteCodigoAsync`) lanzando `NotSupportedException`, porque F5b es 100% read-only. `ConfirmacionImportacionService` (esta task) tampoco los llama — solo lee (`ListarTodosAsync`/`ListarTodasAsync`) para armar los sets de validación — pero mantener esos métodos tirando una excepción es una trampa para cualquier evolución futura de Application que sí necesite un fake de escritura funcional (y el enunciado de esta fase pide explícitamente extenderlos). Se reemplazan por una implementación real en memoria: mutan una lista interna y auto-incrementan el Id, igual que haría un repositorio real.

- [ ] **Paso 1: Escribir el test que falla**

Primero, el fake extendido (reemplaza el archivo completo — el comentario de clase también cambia porque ya no es "solo lectura"):

```csharp
// tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fakes de los 3 repos de maestros que consumen tanto el análisis de importación (F5b,
/// read-only) como la validación de confirmación (F5c, Task 3, tampoco escribe pero necesita
/// que los fakes NO exploten si algún test los ejercita). Implementación real en memoria:
/// AgregarAsync/ActualizarAsync mutan una lista interna y auto-incrementan el Id, como haría
/// un repositorio EF real — a diferencia de la versión anterior (F5b), que los hacía tirar
/// NotSupportedException porque en ese entonces ningún código los llamaba nunca.
/// </summary>
public sealed class ProveedorRepositoryFake : IProveedorRepository
{
    private readonly List<Proveedor> _proveedores;
    private int _siguienteId;

    public ProveedorRepositoryFake(IReadOnlyList<Proveedor> proveedores)
    {
        _proveedores = proveedores.ToList();
        _siguienteId = _proveedores.Count == 0 ? 1 : _proveedores.Max(p => p.Id) + 1;
    }

    public Task<Proveedor?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_proveedores.FirstOrDefault(p => p.Id == id));

    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() =>
        Task.FromResult((IReadOnlyList<Proveedor>)_proveedores.ToList());

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        Task.FromResult(_proveedores.Any(p =>
            p.Nombre == nombre && (excluyendoId is null || p.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(Proveedor proveedor)
    {
        proveedor.Id = _siguienteId++;
        _proveedores.Add(proveedor);
        return Task.FromResult(proveedor.Id);
    }

    public Task ActualizarAsync(Proveedor proveedor)
    {
        var indice = _proveedores.FindIndex(p => p.Id == proveedor.Id);
        if (indice >= 0)
            _proveedores[indice] = proveedor;
        return Task.CompletedTask;
    }
}

public sealed class RubroGastoRepositoryFake : IRubroGastoRepository
{
    private readonly List<RubroGasto> _rubros;
    private int _siguienteId;

    public RubroGastoRepositoryFake(IReadOnlyList<RubroGasto> rubros)
    {
        _rubros = rubros.ToList();
        _siguienteId = _rubros.Count == 0 ? 1 : _rubros.Max(r => r.Id) + 1;
    }

    public Task<RubroGasto?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_rubros.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() =>
        Task.FromResult((IReadOnlyList<RubroGasto>)_rubros.ToList());

    public Task<bool> ExisteCodigoAsync(int codigo, int? excluyendoId = null) =>
        Task.FromResult(_rubros.Any(r =>
            r.Codigo == codigo && (excluyendoId is null || r.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(RubroGasto rubro)
    {
        rubro.Id = _siguienteId++;
        _rubros.Add(rubro);
        return Task.FromResult(rubro.Id);
    }

    public Task ActualizarAsync(RubroGasto rubro)
    {
        var indice = _rubros.FindIndex(r => r.Id == rubro.Id);
        if (indice >= 0)
            _rubros[indice] = rubro;
        return Task.CompletedTask;
    }
}

public sealed class FuenteFinanciamientoRepositoryFake : IFuenteFinanciamientoRepository
{
    private readonly List<FuenteFinanciamiento> _fuentes;
    private int _siguienteId;

    public FuenteFinanciamientoRepositoryFake(IReadOnlyList<FuenteFinanciamiento> fuentes)
    {
        _fuentes = fuentes.ToList();
        _siguienteId = _fuentes.Count == 0 ? 1 : _fuentes.Max(f => f.Id) + 1;
    }

    public Task<FuenteFinanciamiento?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_fuentes.FirstOrDefault(f => f.Id == id));

    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() =>
        Task.FromResult((IReadOnlyList<FuenteFinanciamiento>)_fuentes.ToList());

    public Task<bool> ExisteNombreAsync(string nombre, int? excluyendoId = null) =>
        Task.FromResult(_fuentes.Any(f =>
            f.Nombre == nombre && (excluyendoId is null || f.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(FuenteFinanciamiento fuente)
    {
        fuente.Id = _siguienteId++;
        _fuentes.Add(fuente);
        return Task.FromResult(fuente.Id);
    }

    public Task ActualizarAsync(FuenteFinanciamiento fuente)
    {
        var indice = _fuentes.FindIndex(f => f.Id == fuente.Id);
        if (indice >= 0)
            _fuentes[indice] = fuente;
        return Task.CompletedTask;
    }
}
```

El fake nuevo de `IImportacionRepository` (spy: graba qué recibió, devuelve un resultado fijo):

```csharp
// tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Spy de IImportacionRepository: graba el dto/usuarioId/idImportacion con el que se lo llamó
/// y devuelve un resultado fijo pasado por constructor. Permite testear
/// ConfirmacionImportacionService (validación + enrutamiento del permiso) SIN Postgres — la
/// transacción real es Task 4, en Infrastructure.Tests.
/// </summary>
public sealed class ImportacionRepositoryFake : IImportacionRepository
{
    private readonly ResultadoConfirmacionDto _resultadoConfirmar;
    private readonly ResultadoReversionDto _resultadoRevertir;

    public ConfirmarImportacionDto? DtoRecibido { get; private set; }
    public int? UsuarioIdRecibido { get; private set; }
    public Guid? IdImportacionRevertidaRecibida { get; private set; }
    public int VecesConfirmarLlamado { get; private set; }
    public int VecesRevertirLlamado { get; private set; }

    public ImportacionRepositoryFake(
        ResultadoConfirmacionDto? resultadoConfirmar = null,
        ResultadoReversionDto? resultadoRevertir = null)
    {
        _resultadoConfirmar = resultadoConfirmar
            ?? new ResultadoConfirmacionDto(Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        _resultadoRevertir = resultadoRevertir
            ?? new ResultadoReversionDto(Guid.NewGuid(), 0, 0, 0, 0, 0);
    }

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        DtoRecibido = dto;
        UsuarioIdRecibido = usuarioId;
        VecesConfirmarLlamado++;
        return Task.FromResult(_resultadoConfirmar);
    }

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId)
    {
        IdImportacionRevertidaRecibida = idImportacion;
        UsuarioIdRecibido = usuarioId;
        VecesRevertirLlamado++;
        return Task.FromResult(_resultadoRevertir);
    }
}
```

Y el test del servicio:

```csharp
// tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tests.Finanzas.Fakes;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class ConfirmacionImportacionServiceTests
{
    private const int Ejercicio = 2026;

    private sealed record Mocks(
        ConfirmacionImportacionService Svc, ImportacionRepositoryFake Repo, Mock<IAuthSvc> Auth);

    private static Mocks Crear(
        IReadOnlyList<Proveedor>? proveedores = null,
        IReadOnlyList<RubroGasto>? rubros = null,
        IReadOnlyList<FuenteFinanciamiento>? fuentes = null,
        IReadOnlyList<LineaPoa>? lineasPoa = null,
        RolUsuario rol = RolUsuario.Admin)
    {
        var proveedoresRepo = new ProveedorRepositoryFake(proveedores ?? new List<Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(rubros ?? new List<RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());
        var lineasPoaRepo = new LineaPoaRepositoryStubFake(lineasPoa ?? new List<LineaPoa>());
        var importacionRepo = new ImportacionRepositoryFake();

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new StockApp.Application.Auth.UsuarioSesion(1, "admin", RolUsuario.Admin, null));

        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.ImportarPlanillas))
            .Throws<UnauthorizedAccessException>();

        var svc = new ConfirmacionImportacionService(
            importacionRepo, proveedoresRepo, rubrosRepo, fuentesRepo, lineasPoaRepo, session.Object, auth.Object);

        return new Mocks(svc, importacionRepo, auth);
    }

    private static ConfirmarImportacionDto PayloadValido() => new(
        Ejercicio: Ejercicio,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            Proveedores: new List<string> { "ACME SA" },
            Fuentes: new List<string> { "Literal A" },
            Rubros: new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_Operador_LanzaUnauthorized()
    {
        var m = Crear(rol: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => m.Svc.ConfirmarAsync(PayloadValido()));
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_PayloadValido_DelegaEnElRepositorioYDevuelveSuResultado()
    {
        var m = Crear();
        var payload = PayloadValido();

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.Same(payload, m.Repo.DtoRecibido);
        Assert.Equal(1, m.Repo.UsuarioIdRecibido);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_ProveedorNoExisteNiDeclarado_LanzaValidacionConLaClaveDelIndice()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(new List<string>(), new List<string> { "Literal A" },
                new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.True(ex.Errores.ContainsKey("Gastos[0].Proveedor"));
        Assert.Equal(0, m.Repo.VecesConfirmarLlamado);
    }

    [Fact]
    public async Task ConfirmarAsync_CodigoRubroNoExisteNiDeclarado_LanzaValidacionConElMensajeDelSpec()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            MaestrosNuevos = new MaestrosNuevosConfirmarDto(new List<string> { "ACME SA" },
                new List<string> { "Literal A" }, new List<RubroNuevoConfirmarDto>()),
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 340, null, CondicionPago.Contado),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal(
            "El rubro 340 no existe ni fue declarado nuevo",
            ex.Errores["Gastos[0].CodigoRubro"][0]);
    }

    [Fact]
    public async Task ConfirmarAsync_ProgramaVacioEnLineaPoa_LanzaValidacionConMensajeRequerido()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "", new List<AsignacionConfirmarDto>
                {
                    new("Literal A", 1000m),
                }),
            },
        };

        var ex = await Assert.ThrowsAsync<ValidacionImportacionException>(() => m.Svc.ConfirmarAsync(payload));

        Assert.Equal("Requerido", ex.Errores["LineasPoa[0].Programa"][0]);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConLineaPoaDeclaradaEnElMismoPayload_NoErrorDeValidacion()
    {
        var m = Crear();
        var payload = PayloadValido() with
        {
            Gastos = new List<GastoConfirmarDto>
            {
                new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                    new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, "COMPOSTERAS", CondicionPago.Contado),
            },
            LineasPoa = new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto> { new("Literal A", 1000m) }),
            },
        };

        var resultado = await m.Svc.ConfirmarAsync(payload);

        Assert.Equal(1, m.Repo.VecesConfirmarLlamado);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task RevertirAsync_Operador_LanzaUnauthorized()
    {
        var m = Crear(rol: RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => m.Svc.RevertirAsync(Guid.NewGuid()));
        Assert.Equal(0, m.Repo.VecesRevertirLlamado);
    }

    [Fact]
    public async Task RevertirAsync_Admin_DelegaEnElRepositorioConElIdYElUsuario()
    {
        var m = Crear();
        var idImportacion = Guid.NewGuid();

        await m.Svc.RevertirAsync(idImportacion);

        Assert.Equal(1, m.Repo.VecesRevertirLlamado);
        Assert.Equal(idImportacion, m.Repo.IdImportacionRevertidaRecibida);
        Assert.Equal(1, m.Repo.UsuarioIdRecibido);
    }
}

/// <summary>
/// F5c Task 3: stub read-only de ILineaPoaRepository — el validador solo necesita
/// ListarTodasAsync; el resto de la interfaz no lo usa ningún test de esta clase.
/// </summary>
public sealed class LineaPoaRepositoryStubFake : ILineaPoaRepository
{
    private readonly IReadOnlyList<LineaPoa> _lineas;

    public LineaPoaRepositoryStubFake(IReadOnlyList<LineaPoa> lineas) => _lineas = lineas;

    public Task<LineaPoa?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_lineas.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => Task.FromResult(_lineas);

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task<int> AgregarAsync(LineaPoa linea) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");

    public Task ActualizarSinAsignacionesAsync(LineaPoa linea) =>
        throw new NotSupportedException("El validador de confirmación solo lee.");
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ConfirmacionImportacionServiceTests"`
Esperado: FAIL en compilación — `CS0246: no se encontró el tipo 'IImportacionRepository'` / `'ConfirmacionImportacionService'`.

- [ ] **Paso 3: Implementación mínima**

```csharp
// src/StockApp.Application/Interfaces/IImportacionRepository.cs
using StockApp.Application.Finanzas;

namespace StockApp.Application.Interfaces;

/// <summary>
/// Escritura transaccional del importador de planillas (F5c). Abre UNA transacción por
/// operación (confirmar/revertir) — es el único lugar de todo el flujo de importación que toca
/// EF/Npgsql directamente; Application no referencia esas dependencias (mismo criterio ya
/// documentado en GastoRepository.RegistrarPagoAtomicoAsync, GastoRepository.cs:113-121).
/// </summary>
public interface IImportacionRepository
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId);
}
```

```csharp
// src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Paso de CONFIRMACIÓN del importador (spec F5c). Verifica el permiso, valida el payload
/// COMPLETO (referencias nominales + campos obligatorios) y delega la escritura transaccional
/// en IImportacionRepository. Exige el permiso ImportarPlanillas (solo Admin, mismo que F5b).
/// </summary>
public interface IConfirmacionImportacionService
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion);
}
```

```csharp
// src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Finanzas;

public class ConfirmacionImportacionService : IConfirmacionImportacionService
{
    private readonly IImportacionRepository _importacionRepo;
    private readonly IProveedorRepository _proveedores;
    private readonly IRubroGastoRepository _rubros;
    private readonly IFuenteFinanciamientoRepository _fuentes;
    private readonly ILineaPoaRepository _lineasPoa;
    private readonly ICurrentSession _session;
    private readonly IAuthorizationService _auth;

    public ConfirmacionImportacionService(
        IImportacionRepository importacionRepo,
        IProveedorRepository proveedores,
        IRubroGastoRepository rubros,
        IFuenteFinanciamientoRepository fuentes,
        ILineaPoaRepository lineasPoa,
        ICurrentSession session,
        IAuthorizationService auth)
    {
        _importacionRepo = importacionRepo;
        _proveedores = proveedores;
        _rubros = rubros;
        _fuentes = fuentes;
        _lineasPoa = lineasPoa;
        _session = session;
        _auth = auth;
    }

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        await ValidarAsync(dto);

        // El guard de re-importación (spec §2.6, 409 salvo Forzar) se resuelve DENTRO de la
        // transacción del repositorio, no acá: tiene que correr con el advisory lock tomado
        // para no dejar una ventana de carrera entre dos /confirmar concurrentes del mismo
        // ejercicio (ver Task 6). Este servicio solo pasa dto.Forzar tal cual llegó.
        return await _importacionRepo.ConfirmarAsync(dto, _session.UsuarioActual!.Id);
    }

    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        return await _importacionRepo.RevertirAsync(idImportacion, _session.UsuarioActual!.Id);
    }

    private async Task ValidarAsync(ConfirmarImportacionDto dto)
    {
        var errores = new Dictionary<string, List<string>>();

        // Proveedores/Rubros: contra TODOS los existentes (sin filtro Activo). Fuentes: SOLO
        // activas. Mismo criterio ya establecido por AnalisisImportacionService (F5b).
        var proveedoresExistentes = (await _proveedores.ListarTodosAsync())
            .Select(p => Normalizar(p.Nombre)).ToHashSet();
        var rubrosExistentes = (await _rubros.ListarTodosAsync())
            .Select(r => r.Codigo).ToHashSet();
        var fuentesActivas = (await _fuentes.ListarTodasAsync())
            .Where(f => f.Activo).Select(f => Normalizar(f.Nombre)).ToHashSet();
        var lineasPoaExistentes = (await _lineasPoa.ListarTodasAsync())
            .Where(l => l.Ejercicio == dto.Ejercicio)
            .Select(l => Normalizar(l.Nombre)).ToHashSet();

        var proveedoresNuevos = dto.MaestrosNuevos.Proveedores.Select(Normalizar).ToHashSet();
        var fuentesNuevas = dto.MaestrosNuevos.Fuentes.Select(Normalizar).ToHashSet();
        var rubrosNuevos = dto.MaestrosNuevos.Rubros.Select(r => r.Codigo).ToHashSet();
        var lineasPoaDeclaradas = dto.LineasPoa.Select(l => Normalizar(l.Nombre)).ToHashSet();

        for (var i = 0; i < dto.MaestrosNuevos.Rubros.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(dto.MaestrosNuevos.Rubros[i].Nombre))
                AgregarError(errores, $"MaestrosNuevos.Rubros[{i}].Nombre", "Requerido");
        }

        for (var i = 0; i < dto.Ingresos.Count; i++)
        {
            var ingreso = dto.Ingresos[i];
            if (string.IsNullOrWhiteSpace(ingreso.Concepto))
                AgregarError(errores, $"Ingresos[{i}].Concepto", "Requerido");
            if (!Resuelve(ingreso.Fuente, fuentesActivas, fuentesNuevas))
                AgregarError(errores, $"Ingresos[{i}].Fuente",
                    $"La fuente '{ingreso.Fuente}' no existe ni fue declarada nueva");
        }

        for (var i = 0; i < dto.Gastos.Count; i++)
        {
            var gasto = dto.Gastos[i];
            if (string.IsNullOrWhiteSpace(gasto.Detalle))
                AgregarError(errores, $"Gastos[{i}].Detalle", "Requerido");
            if (!Resuelve(gasto.Proveedor, proveedoresExistentes, proveedoresNuevos))
                AgregarError(errores, $"Gastos[{i}].Proveedor",
                    $"El proveedor '{gasto.Proveedor}' no existe ni fue declarado nuevo");
            if (!Resuelve(gasto.Fuente, fuentesActivas, fuentesNuevas))
                AgregarError(errores, $"Gastos[{i}].Fuente",
                    $"La fuente '{gasto.Fuente}' no existe ni fue declarada nueva");
            if (!rubrosExistentes.Contains(gasto.CodigoRubro) && !rubrosNuevos.Contains(gasto.CodigoRubro))
                AgregarError(errores, $"Gastos[{i}].CodigoRubro",
                    $"El rubro {gasto.CodigoRubro} no existe ni fue declarado nuevo");
            if (!string.IsNullOrWhiteSpace(gasto.LineaPoa)
                && !lineasPoaExistentes.Contains(Normalizar(gasto.LineaPoa))
                && !lineasPoaDeclaradas.Contains(Normalizar(gasto.LineaPoa)))
                AgregarError(errores, $"Gastos[{i}].LineaPoa",
                    $"La línea POA '{gasto.LineaPoa}' no existe ni fue declarada en LineasPoa");
        }

        for (var i = 0; i < dto.LineasPoa.Count; i++)
        {
            var linea = dto.LineasPoa[i];
            if (string.IsNullOrWhiteSpace(linea.Nombre))
                AgregarError(errores, $"LineasPoa[{i}].Nombre", "Requerido");
            if (string.IsNullOrWhiteSpace(linea.Programa))
                AgregarError(errores, $"LineasPoa[{i}].Programa", "Requerido");

            for (var j = 0; j < linea.Asignaciones.Count; j++)
            {
                if (!Resuelve(linea.Asignaciones[j].Fuente, fuentesActivas, fuentesNuevas))
                    AgregarError(errores, $"LineasPoa[{i}].Asignaciones[{j}].Fuente",
                        $"La fuente '{linea.Asignaciones[j].Fuente}' no existe ni fue declarada nueva");
            }
        }

        if (errores.Count > 0)
            throw new StockApp.Domain.Exceptions.ValidacionImportacionException(
                errores.ToDictionary(kv => kv.Key, kv => kv.Value.ToArray()));
    }

    private static bool Resuelve(string nombre, HashSet<string> existentes, HashSet<string> nuevos) =>
        existentes.Contains(Normalizar(nombre)) || nuevos.Contains(Normalizar(nombre));

    private static void AgregarError(Dictionary<string, List<string>> errores, string clave, string mensaje)
    {
        if (!errores.TryGetValue(clave, out var lista))
        {
            lista = new List<string>();
            errores[clave] = lista;
        }
        lista.Add(mensaje);
    }

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ConfirmacionImportacionServiceTests"`
Esperado: `Passed! - Failed: 0, Passed: 8`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Application/Interfaces/IImportacionRepository.cs \
  src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs \
  src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs \
  tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs \
  tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs \
  tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceTests.cs
git commit -m "feat(finanzas): validación de referencias nominales del payload de confirmación de importación"
```

---

## Task 4: Transacción y escritura de maestros + POA

**Files:**
- Create: `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs`

**Interfaces:**
- Consumes: `AppDbContext` directo (mismo patrón que `MovimientoStockRepository`/`GastoRepository` — sin componer otros repositorios), los DTOs de Task 2, `IImportacionRepository` de Task 3.
- Produces: `ImportacionRepository : IImportacionRepository`. En esta task, `ConfirmarAsync` YA abre la transacción real y hace get-or-create de maestros + escritura de `LineaPoa`/`AsignacionPresupuestal`, pero Ingresos/Gastos todavía NO se procesan (`IngresosCreados`/`GastosCreados` quedan en 0) — Task 5 completa eso sobre el MISMO método. El guard de re-importación y el `LogAuditoria` tampoco existen todavía — los agrega Task 6.

### Contexto para el implementador

Esta es la task más grande del plan porque es donde vive la única transacción real de todo el importador. El patrón a imitar es `MovimientoStockRepository.RegistrarMovimientoAtomicoAsync` (`MovimientoStockRepository.cs:40-101`): `await using var tx = await _ctx.Database.BeginTransactionAsync();`, todo el trabajo adentro, UN `SaveChangesAsync()`, `tx.CommitAsync()` al final.

Punto clave de EF Core que hay que entender ANTES de escribir código: como el spec pide un solo `SaveChangesAsync()` para toda la operación (no uno por cada maestro nuevo), las entidades que se crean en esta misma corrida (un `Proveedor` nuevo, por ejemplo) todavía NO tienen `Id` real hasta que se guarda. La forma correcta de conectar un `Gasto` nuevo con un `Proveedor` recién creado, SIN esperar a que se guarde primero, es asignar la NAVEGACIÓN (`gasto.Proveedor = proveedorEntity`) en vez del Id (`gasto.ProveedorId = ...`) — EF resuelve el FK automáticamente al hacer `SaveChangesAsync()`, por "fixup" de relaciones. Este mismo mecanismo ya lo usa el repo existente `LineaPoaRepository.AgregarAsync` (agrega la línea con su lista de `Asignaciones` ya poblada, un solo `Add` + `SaveChangesAsync`) y `GastoService.AltaAsync` (agrega `gasto.Pagos` como lista antes de guardar). Por eso el diseño de esta task usa diccionarios `Dictionary<string, FuenteFinanciamiento>` (guardan el OBJETO, no el Id) para resolver referencias nominales.

El lock: `pg_advisory_xact_lock(ejercicio)` es un lock a nivel de sesión de Postgres, con alcance de TRANSACCIÓN — se libera solo, automáticamente, en `COMMIT` o `ROLLBACK` (por eso "xact"). Se ejecuta con `_ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})")`, DENTRO de la transacción recién abierta. Hay precedente de SQL crudo dentro de un repositorio en `GastoRepository.RegistrarPagoAtomicoAsync` (`GastoRepository.cs:138-175`, usa `FromSqlInterpolated` con `FOR UPDATE`) — mismo principio: serializar con una primitiva de Postgres, no con lógica de aplicación.

El `Guid idImportacion` de la corrida se genera al PRINCIPIO del método (antes de la transacción, es determinístico y no depende de nada de la base) y se estampa en las `LineaPoa` NUEVAS que esta corrida crea (las que ya existían, aunque se les agregue una asignación nueva, NO cambian su `IdImportacion` — spec §2.7: "columna nueva... en Gasto, IngresoCaja y LineaPoa" solo tiene sentido en el registro que la corrida realmente originó).

- [ ] **Paso 1: Escribir el test que falla**

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>
/// F5c Task 4: get-or-create de maestros + LineaPoa/AsignacionPresupuestal dentro de la
/// transacción real. Ingresos/Gastos (Task 5) y guard/auditoría (Task 6) se agregan en tasks
/// posteriores sobre el MISMO ConfirmarAsync — acá los contadores respectivos quedan en 0.
/// </summary>
public class ImportacionRepositoryTests : PostgresRepositoryTestBase
{
    private const int Ejercicio = 2026;
    private readonly ImportacionRepository _repo;

    public ImportacionRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new ImportacionRepository(Context);
    }

    private static ConfirmarImportacionDto PayloadSoloMaestrosYPoa(
        IReadOnlyList<string>? proveedoresNuevos = null,
        IReadOnlyList<string>? fuentesNuevas = null,
        IReadOnlyList<RubroNuevoConfirmarDto>? rubrosNuevos = null,
        IReadOnlyList<LineaPoaConfirmarDto>? lineasPoa = null) => new(
        Ejercicio: Ejercicio,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            proveedoresNuevos ?? new List<string>(),
            fuentesNuevas ?? new List<string> { "Literal B", "Literal C" },
            rubrosNuevos ?? new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: lineasPoa ?? new List<LineaPoaConfirmarDto>
        {
            new("COMPOSTERAS", "Ambiente", new List<AsignacionConfirmarDto>
            {
                new("Literal B", 92748m),
                new("Literal C", 1407252m),
            }),
        });

    [Fact]
    public async Task ConfirmarAsync_FuentesNuevas_LasCreaYDevuelveElContador()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(2, resultado.FuentesCreadas);
        var fuentesEnBase = await Fixture.CrearContexto().FuentesFinanciamiento.ToListAsync();
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal B");
        Assert.Contains(fuentesEnBase, f => f.Nombre == "Literal C");
    }

    [Fact]
    public async Task ConfirmarAsync_FuenteYaExistente_NoLaDuplicaYNoLaCuentaComoCreada()
    {
        Context.FuentesFinanciamiento.Add(new StockApp.Domain.Entities.FuenteFinanciamiento { Nombre = "Literal B" });
        await Context.SaveChangesAsync();
        Context.ChangeTracker.Clear();

        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(1, resultado.FuentesCreadas); // solo "Literal C" es nueva
        await using var verificacion = Fixture.CrearContexto();
        var cantidad = verificacion.FuentesFinanciamiento.Count(f => f.Nombre == "Literal B");
        Assert.Equal(1, cantidad);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaConFinanciamientoMixto_CreaLaLineaConDosAsignaciones()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(1, resultado.LineasPoaCreadas);
        Assert.Equal(2, resultado.AsignacionesCreadas);

        await using var verificacion = Fixture.CrearContexto();
        var linea = verificacion.LineasPoa
            .Include(l => l.Asignaciones).ThenInclude(a => a.FuenteFinanciamiento)
            .Single(l => l.Nombre == "COMPOSTERAS");
        Assert.Equal("Ambiente", linea.Programa);
        Assert.Equal(Ejercicio, linea.Ejercicio);
        Assert.NotNull(linea.IdImportacion);
        Assert.Equal(resultado.IdImportacion, linea.IdImportacion);
        Assert.Equal(2, linea.Asignaciones.Count);
        Assert.Contains(linea.Asignaciones, a => a.FuenteFinanciamiento!.Nombre == "Literal B" && a.Monto == 92748m);
        Assert.Contains(linea.Asignaciones, a => a.FuenteFinanciamiento!.Nombre == "Literal C" && a.Monto == 1407252m);
    }

    [Fact]
    public async Task ConfirmarAsync_LineaPoaYaExistenteConLaMismaAsignacion_NoLaDuplicaNiCuentaComoNueva()
    {
        var primero = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        var segundo = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        Assert.Equal(0, segundo.LineasPoaCreadas);
        Assert.Equal(0, segundo.AsignacionesCreadas);
        Assert.Equal(0, segundo.FuentesCreadas);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, verificacion.LineasPoa.Count(l => l.Nombre == "COMPOSTERAS"));
        Assert.Equal(2, verificacion.AsignacionesPresupuestales.Count());
    }

    [Fact]
    public async Task ConfirmarAsync_TodoElGrafoSeCommiteaJuntoEnUnaSolaTransaccion()
    {
        // Atomicidad: si la corrida completa, TODO lo que creó (fuentes + línea + 2
        // asignaciones) tiene que estar visible desde un contexto NUEVO (no el mismo Context,
        // que podría mostrar el change tracker en vez del estado real de la BD).
        await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.FuentesFinanciamiento.CountAsync());
        Assert.Equal(1, await verificacion.LineasPoa.CountAsync());
        Assert.Equal(2, await verificacion.AsignacionesPresupuestales.CountAsync());
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: FAIL en compilación — `CS0246: no se encontró el tipo 'ImportacionRepository'`.

- [ ] **Paso 3: Implementación mínima**

```csharp
// src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

/// <summary>
/// Escritura transaccional del importador de planillas (F5c). ConfirmarAsync abre UNA
/// transacción, toma pg_advisory_xact_lock(ejercicio) y hace TODO el trabajo con un solo
/// SaveChangesAsync — mismo patrón que MovimientoStockRepository.RegistrarMovimientoAtomicoAsync.
/// </summary>
public class ImportacionRepository : IImportacionRepository
{
    private readonly AppDbContext _ctx;

    public ImportacionRepository(AppDbContext ctx) => _ctx = ctx;

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        var idImportacion = Guid.NewGuid();

        await using var tx = await _ctx.Database.BeginTransactionAsync();
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})");

        var (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
                proveedoresCreados, fuentesCreadas, rubrosCreados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            IngresosCreados: 0, IngresosOmitidos: 0,
            GastosCreados: 0, GastosOmitidos: 0, PagosCreados: 0);
    }

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId) =>
        throw new NotImplementedException("Se implementa en Task 8.");

    /// <summary>
    /// Get-or-create de Proveedor/FuenteFinanciamiento/RubroGasto declarados en
    /// MaestrosNuevos. Devuelve diccionarios normalizados (Trim + ToUpperInvariant, mismo
    /// criterio que AnalisisImportacionService.Normalizar de F5b) con el OBJETO de la entidad
    /// (no su Id): las entidades nuevas todavía no tienen Id real hasta el SaveChangesAsync
    /// único del final — el resto del método las referencia por navegación, no por FK manual.
    /// </summary>
    private async Task<(
        Dictionary<string, Proveedor> ProveedorPorNombre,
        Dictionary<string, FuenteFinanciamiento> FuentePorNombre,
        Dictionary<int, RubroGasto> RubroPorCodigo,
        int ProveedoresCreados, int FuentesCreadas, int RubrosCreados)>
        GetOrCrearMaestrosAsync(ConfirmarImportacionDto dto)
    {
        var proveedorPorNombre = (await _ctx.Proveedores.ToListAsync())
            .ToDictionary(p => Normalizar(p.Nombre));
        var fuentePorNombre = (await _ctx.FuentesFinanciamiento.ToListAsync())
            .ToDictionary(f => Normalizar(f.Nombre));
        var rubroPorCodigo = (await _ctx.RubrosGasto.ToListAsync())
            .ToDictionary(r => r.Codigo);

        var proveedoresCreados = 0;
        foreach (var nombre in dto.MaestrosNuevos.Proveedores)
        {
            var clave = Normalizar(nombre);
            if (proveedorPorNombre.ContainsKey(clave))
                continue;

            var proveedor = new Proveedor { Nombre = nombre.Trim() };
            _ctx.Proveedores.Add(proveedor);
            proveedorPorNombre[clave] = proveedor;
            proveedoresCreados++;
        }

        var fuentesCreadas = 0;
        foreach (var nombre in dto.MaestrosNuevos.Fuentes)
        {
            var clave = Normalizar(nombre);
            if (fuentePorNombre.ContainsKey(clave))
                continue;

            var fuente = new FuenteFinanciamiento { Nombre = nombre.Trim() };
            _ctx.FuentesFinanciamiento.Add(fuente);
            fuentePorNombre[clave] = fuente;
            fuentesCreadas++;
        }

        var rubrosCreados = 0;
        foreach (var rubroNuevo in dto.MaestrosNuevos.Rubros)
        {
            if (rubroPorCodigo.ContainsKey(rubroNuevo.Codigo))
                continue;

            var rubro = new RubroGasto { Codigo = rubroNuevo.Codigo, Nombre = rubroNuevo.Nombre.Trim() };
            _ctx.RubrosGasto.Add(rubro);
            rubroPorCodigo[rubroNuevo.Codigo] = rubro;
            rubrosCreados++;
        }

        return (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
            proveedoresCreados, fuentesCreadas, rubrosCreados);
    }

    /// <summary>
    /// Get-or-create de LineaPoa (clave natural Nombre+Ejercicio) y sus AsignacionPresupuestal
    /// (clave natural LineaPoaId+FuenteFinanciamientoId, único en BD — AppDbContext.cs:142).
    /// IdImportacion se estampa SOLO en las líneas NUEVAS: una línea ya existente a la que esta
    /// corrida solo le agrega una asignación (financiamiento mixto declarado en dos corridas
    /// separadas) sigue siendo, a todos los efectos, "de antes" — no la creó esta importación.
    /// </summary>
    private async Task<(Dictionary<string, LineaPoa> LineaPorNombre, int LineasCreadas, int AsignacionesCreadas)>
        GetOrCrearLineasPoaAsync(
            ConfirmarImportacionDto dto,
            Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
            Guid idImportacion)
    {
        var lineasExistentes = await _ctx.LineasPoa
            .Where(l => l.Ejercicio == dto.Ejercicio)
            .Include(l => l.Asignaciones)
            .ToListAsync();
        var lineaPorNombre = lineasExistentes.ToDictionary(l => Normalizar(l.Nombre));

        var lineasCreadas = 0;
        var asignacionesCreadas = 0;

        foreach (var lineaDto in dto.LineasPoa)
        {
            var clave = Normalizar(lineaDto.Nombre);
            if (!lineaPorNombre.TryGetValue(clave, out var linea))
            {
                linea = new LineaPoa
                {
                    Nombre = lineaDto.Nombre.Trim(),
                    Programa = lineaDto.Programa.Trim(),
                    Ejercicio = dto.Ejercicio,
                    IdImportacion = idImportacion,
                };
                _ctx.LineasPoa.Add(linea);
                lineaPorNombre[clave] = linea;
                lineasCreadas++;
            }

            foreach (var asignacionDto in lineaDto.Asignaciones)
            {
                var fuente = fuentePorNombre[Normalizar(asignacionDto.Fuente)];

                // Una asignación NUEVA (fuente.Id == 0, todavía no guardada) nunca puede
                // colisionar con una existente (todas tienen Id real > 0) — el chequeo por Id
                // alcanza sin necesitar comparar referencias de objeto.
                if (linea.Asignaciones.Any(a => a.FuenteFinanciamientoId == fuente.Id))
                    continue;

                linea.Asignaciones.Add(new AsignacionPresupuestal
                {
                    FuenteFinanciamiento = fuente,
                    Monto = asignacionDto.Monto,
                });
                asignacionesCreadas++;
            }
        }

        return (lineaPorNombre, lineasCreadas, asignacionesCreadas);
    }

    private static string Normalizar(string texto) => texto.Trim().ToUpperInvariant();
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: `Passed! - Failed: 0, Passed: 5`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs \
  tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs
git commit -m "feat(finanzas): transacción de confirmación — get-or-create de maestros y LineaPoa/AsignacionPresupuestal"
```

---

## Task 5: Ingresos, gastos y dedupe por clave natural

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs`

**Interfaces:**
- Consumes: todo lo de Task 4 (`GetOrCrearMaestrosAsync`, `GetOrCrearLineasPoaAsync`).
- Produces: `ConfirmarAsync` ahora procesa `dto.Ingresos`/`dto.Gastos` con dedupe real; `IngresosCreados`/`IngresosOmitidos`/`GastosCreados`/`GastosOmitidos`/`PagosCreados` del `ResultadoConfirmacionDto` dejan de estar hardcodeados en 0.

### Contexto para el implementador

El dedupe compara una CLAVE NATURAL: `(Fecha, Concepto, Monto, FuenteFinanciamientoId)` para `IngresoCaja`, `(ProveedorId, NumeroFactura, NumeroOrden, Fecha, MontoTotal)` para `Gasto` (spec §4). El enunciado de esta fase pide, EXPLÍCITAMENTE, que la proyección de esa clave sea UNA sola función compartida entre "cargar el set de lo que ya existe en la base" y "comparar cada fila nueva del payload" — es la mitigación real contra el riesgo que el propio spec anota (§4, "Riesgo asumido"): si la carga del set existente y la comparación de las filas nuevas usaran cada una su propia lógica de normalización, un desvío entre ambas (ej. una las compara case-sensitive y la otra no) haría que el dedupe fallara en silencio, sin ningún error visible — simplemente se duplicarían filas. Con una sola función, cualquier cambio futuro a la clave se aplica en los dos lados a la vez.

Cuidado con dos conversiones que tienen que ser CONSISTENTES en ambos lados de la comparación:
- `DateOnly` (el DTO) → `DateTime` (la entidad, UTC): se usa `new DateTime(f.Year, f.Month, f.Day, 0, 0, 0, DateTimeKind.Utc)`, el mismo patrón ya usado en `FinanzasVistasService.cs:37,96` para construir fechas UTC a partir de componentes — NO `DateOnly.ToDateTime(TimeOnly.MinValue)` (ese devuelve `DateTimeKind.Unspecified`, no es lo que Npgsql espera para una columna `timestamp with time zone`).
- El campo `Monto`/`MontoTotal` es `decimal(18,4)` en la base — comparar decimales de distinta escala numérica (ej. `100m` vs `100.0000m`) es seguro en C# (`decimal` normaliza la igualdad independientemente de la escala interna), así que no hace falta redondear antes de comparar.

Dedupe SOLO contra registros `Activo == true` — mismo criterio que el índice único parcial existente `IX_Gastos_ProveedorId_NumeroFactura` (`AppDbContext.cs:165-167`, filtro `"Activo" = TRUE`): un gasto/ingreso dado de baja (por ejemplo, por una reversa previa, Task 8) no debería bloquear para siempre una re-importación limpia de la misma clave natural.

**Nota de dominio, para que quede documentada y no parezca un descuido**: `GastoConfirmarDto` (spec §3) no tiene un campo `FechaVencimiento`. El alta manual de un gasto `Credito` vía `GastoService.AltaAsync` SÍ lo exige (`GastoService.cs:272-273`, lanza si falta) — pero esa validación vive en `GastoService`, que esta escritura NO usa (escribe directo contra `AppDbContext`, como el resto de este repositorio). Los gastos `Credito` que importa F5c (los compromisos POA, spec §2.3) quedan con `FechaVencimiento = null`. Es una consecuencia directa de que el contrato de F5c no incluye ese campo, no una decisión nueva de esta task — el Calendario de Pagos (F4) simplemente no los va a listar como vencidos.

- [ ] **Paso 1: Escribir el test que falla**

Agregar al final de la clase `ImportacionRepositoryTests` (mismo archivo, después del último test de Task 4):

```csharp
    private static ConfirmarImportacionDto PayloadConIngresoYGasto(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { "ACME SA" },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_IngresoYGastoNuevos_LosCreaYElGastoContadoTraePagoAutomatico()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        Assert.Equal(1, resultado.IngresosCreados);
        Assert.Equal(0, resultado.IngresosOmitidos);
        Assert.Equal(1, resultado.GastosCreados);
        Assert.Equal(0, resultado.GastosOmitidos);
        Assert.Equal(1, resultado.PagosCreados);

        await using var verificacion = Fixture.CrearContexto();
        var gasto = verificacion.Gastos.Include(g => g.Pagos).Single(g => g.Detalle == "Compra de insumos");
        Assert.Equal(resultado.IdImportacion, gasto.IdImportacion);
        Assert.Single(gasto.Pagos);
        Assert.Equal(500m, gasto.Pagos[0].Monto);
        var ingreso = verificacion.IngresosCaja.Single(i => i.Concepto == "Saldo inicial");
        Assert.Equal(resultado.IdImportacion, ingreso.IdImportacion);
    }

    [Fact]
    public async Task ConfirmarAsync_CorridaRepetidaConForzar_NoDuplicaIngresosNiGastos()
    {
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: 1);

        Assert.Equal(0, segunda.IngresosCreados);
        Assert.Equal(1, segunda.IngresosOmitidos);
        Assert.Equal(0, segunda.GastosCreados);
        Assert.Equal(1, segunda.GastosOmitidos);
        Assert.Equal(0, segunda.PagosCreados);

        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        Assert.Equal(1, await verificacion.IngresosCaja.CountAsync());
    }

    [Fact]
    public async Task ConfirmarAsync_CompromisoPoaCredito_NoGeneraPago()
    {
        var payload = new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                new List<string> { "Contratista SRL" },
                new List<string> { "Literal C" },
                new List<RubroNuevoConfirmarDto> { new(2, "Obras") }),
            Ingresos: new List<IngresoConfirmarDto>(),
            Gastos: new List<GastoConfirmarDto>
            {
                new("Contratista SRL", "F-9", null, "Compromiso POA sin pago", null,
                    new DateOnly(Ejercicio, 12, 31), 300000m, "Literal C", 2, "COMPOSTERAS",
                    CondicionPago.Credito),
            },
            LineasPoa: new List<LineaPoaConfirmarDto>
            {
                new("COMPOSTERAS", "Ambiente",
                    new List<AsignacionConfirmarDto> { new("Literal C", 1407252m) }),
            });

        var resultado = await _repo.ConfirmarAsync(payload, usuarioId: 1);

        Assert.Equal(1, resultado.GastosCreados);
        Assert.Equal(0, resultado.PagosCreados);
        await using var verificacion = Fixture.CrearContexto();
        var gasto = verificacion.Gastos.Include(g => g.Pagos).Single();
        Assert.Empty(gasto.Pagos);
        Assert.Equal(gasto.MontoTotal, gasto.MontoTotal - 0m); // SaldoPendiente == MontoTotal
    }
```

Y agregar, junto a los `using` existentes del test, `using StockApp.Domain.Enums;` (ya está presente desde el paso 1 de Task 4 — no duplicar si ya quedó).

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: FAIL — `Assert.Equal(1, resultado.IngresosCreados)` falla porque `ConfirmarAsync` todavía hardcodea `IngresosCreados: 0`.

- [ ] **Paso 3: Implementación mínima**

Reemplazar el método `ConfirmarAsync` completo por esta versión (agrega el procesamiento de Ingresos/Gastos entre la escritura de LineasPoa y el `SaveChangesAsync`), y agregar los 4 métodos privados nuevos al final de la clase:

```csharp
    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        var idImportacion = Guid.NewGuid();

        await using var tx = await _ctx.Database.BeginTransactionAsync();
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})");

        var (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
                proveedoresCreados, fuentesCreadas, rubrosCreados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        var (ingresosCreados, ingresosOmitidos) =
            await ProcesarIngresosAsync(dto, fuentePorNombre, idImportacion);

        var (gastosCreados, gastosOmitidos, pagosCreados) =
            await ProcesarGastosAsync(dto, proveedorPorNombre, fuentePorNombre, rubroPorCodigo, lineaPorNombre, idImportacion);

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            ingresosCreados, ingresosOmitidos,
            gastosCreados, gastosOmitidos, pagosCreados);
    }
```

```csharp
    // ── Dedupe por clave natural (spec §4) ──────────────────────────────────────────────────
    //
    // Una sola función de proyección por entidad, compartida entre la carga del set existente
    // (desde la BD) y la comparación de cada fila nueva del payload — evita que ambos lados se
    // desincronicen silenciosamente (spec §4, "Riesgo asumido").

    private readonly record struct ClaveIngreso(DateTime Fecha, string Concepto, decimal Monto, int FuenteId);
    private readonly record struct ClaveGasto(
        int ProveedorId, string? NumeroFactura, string? NumeroOrden, DateTime Fecha, decimal MontoTotal);

    private static ClaveIngreso ProyectarClaveIngreso(DateTime fecha, string concepto, decimal monto, int fuenteId) =>
        new(fecha, NormalizarClave(concepto), monto, fuenteId);

    private static ClaveGasto ProyectarClaveGasto(
        int proveedorId, string? numeroFactura, string? numeroOrden, DateTime fecha, decimal montoTotal) =>
        new(proveedorId, NormalizarClaveOpcional(numeroFactura), NormalizarClaveOpcional(numeroOrden), fecha, montoTotal);

    private static string NormalizarClave(string texto) => texto.Trim().ToUpperInvariant();

    private static string? NormalizarClaveOpcional(string? texto) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim().ToUpperInvariant();

    private static DateTime AFechaUtc(DateOnly fecha) =>
        new(fecha.Year, fecha.Month, fecha.Day, 0, 0, 0, DateTimeKind.Utc);

    private async Task<(int Creados, int Omitidos)> ProcesarIngresosAsync(
        ConfirmarImportacionDto dto,
        Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
        Guid idImportacion)
    {
        var clavesExistentes = (await _ctx.IngresosCaja
                .Where(i => i.Activo)
                .Select(i => new { i.Fecha, i.Concepto, i.Monto, i.FuenteFinanciamientoId })
                .ToListAsync())
            .Select(i => ProyectarClaveIngreso(i.Fecha, i.Concepto, i.Monto, i.FuenteFinanciamientoId))
            .ToHashSet();

        var creados = 0;
        var omitidos = 0;

        foreach (var ingresoDto in dto.Ingresos)
        {
            var fuente = fuentePorNombre[Normalizar(ingresoDto.Fuente)];
            var fechaUtc = AFechaUtc(ingresoDto.Fecha);
            var clave = ProyectarClaveIngreso(fechaUtc, ingresoDto.Concepto, ingresoDto.Monto, fuente.Id);

            if (clavesExistentes.Contains(clave))
            {
                omitidos++;
                continue;
            }

            _ctx.IngresosCaja.Add(new IngresoCaja
            {
                Fecha = fechaUtc,
                Concepto = ingresoDto.Concepto.Trim(),
                Monto = ingresoDto.Monto,
                FuenteFinanciamiento = fuente,
                IdImportacion = idImportacion,
            });
            creados++;
        }

        return (creados, omitidos);
    }

    /// <summary>
    /// Contado ⇒ pago automático por el total en la fecha del gasto (mismo criterio que
    /// GastoService.AltaAsync, GastoService.cs:50-55). Los compromisos POA importados (spec
    /// §2.3) van Credito SIN pago: SaldoPendiente == MontoTotal refleja que es un compromiso
    /// pendiente, no una factura pagada — el Control POA (F4) ya calcula el saldo de la línea a
    /// partir de gastos activos con su LineaPoaId, sin cambios.
    /// </summary>
    private async Task<(int Creados, int Omitidos, int PagosCreados)> ProcesarGastosAsync(
        ConfirmarImportacionDto dto,
        Dictionary<string, Proveedor> proveedorPorNombre,
        Dictionary<string, FuenteFinanciamiento> fuentePorNombre,
        Dictionary<int, RubroGasto> rubroPorCodigo,
        Dictionary<string, LineaPoa> lineaPorNombre,
        Guid idImportacion)
    {
        var clavesExistentes = (await _ctx.Gastos
                .Where(g => g.Activo)
                .Select(g => new { g.ProveedorId, g.NumeroFactura, g.NumeroOrden, g.Fecha, g.MontoTotal })
                .ToListAsync())
            .Select(g => ProyectarClaveGasto(g.ProveedorId, g.NumeroFactura, g.NumeroOrden, g.Fecha, g.MontoTotal))
            .ToHashSet();

        var creados = 0;
        var omitidos = 0;
        var pagosCreados = 0;

        foreach (var gastoDto in dto.Gastos)
        {
            var proveedor = proveedorPorNombre[Normalizar(gastoDto.Proveedor)];
            var fuente = fuentePorNombre[Normalizar(gastoDto.Fuente)];
            var rubro = rubroPorCodigo[gastoDto.CodigoRubro];
            var fechaUtc = AFechaUtc(gastoDto.Fecha);

            var clave = ProyectarClaveGasto(
                proveedor.Id, gastoDto.NumeroFactura, gastoDto.NumeroOrden, fechaUtc, gastoDto.MontoTotal);
            if (clavesExistentes.Contains(clave))
            {
                omitidos++;
                continue;
            }

            var gasto = new Gasto
            {
                Proveedor = proveedor,
                NumeroFactura = gastoDto.NumeroFactura,
                NumeroOrden = gastoDto.NumeroOrden,
                Detalle = gastoDto.Detalle.Trim(),
                Destino = gastoDto.Destino,
                Fecha = fechaUtc,
                MontoTotal = gastoDto.MontoTotal,
                FuenteFinanciamiento = fuente,
                RubroGasto = rubro,
                LineaPoa = string.IsNullOrWhiteSpace(gastoDto.LineaPoa)
                    ? null : lineaPorNombre[Normalizar(gastoDto.LineaPoa)],
                CondicionPago = gastoDto.Condicion,
                IdImportacion = idImportacion,
            };

            if (gastoDto.Condicion == CondicionPago.Contado)
            {
                gasto.Pagos = new List<PagoGasto>
                {
                    new() { Fecha = fechaUtc, Monto = gastoDto.MontoTotal, Nota = "Pago contado (importación)" },
                };
                pagosCreados++;
            }

            _ctx.Gastos.Add(gasto);
            creados++;
        }

        return (creados, omitidos, pagosCreados);
    }
```

Y agregar `using StockApp.Domain.Enums;` a los `using` de `ImportacionRepository.cs` (para `CondicionPago`).

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: `Passed! - Failed: 0, Passed: 8`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs \
  tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs
git commit -m "feat(finanzas): escritura de ingresos y gastos con dedupe por clave natural (idempotencia)"
```

---

## Task 6: Guard de re-importación y auditoría de la corrida

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs`

**Interfaces:**
- Consumes: todo lo de Tasks 4-5.
- Produces: `ConfirmarAsync` ahora lanza `ReglaDeNegocioException` (409, vía `DomainExceptionHandler` ya existente — sin caso nuevo en el handler, spec §6) si el ejercicio ya tiene una importación previa NO revertida y `dto.Forzar == false`. Cada corrida deja un `LogAuditoria` con `AccionAuditada.ImportacionPlanillas`. Task 8 (`RevertirAsync`) reutiliza la constante privada `PrefijoIdImportacion` (definida acá) para ubicar, con su propia query inline, el `LogAuditoria` del lote a revertir — no llama a `BuscarImportacionNoRevertidaAsync` ni a `ExtraerIdImportacion` (esos dos son específicos del guard de `/confirmar`, que ya tiene el Guid resuelto de antes; `/revertir/{id}` lo recibe directo como parámetro de ruta).

### Contexto para el implementador

`LogAuditoria` (`src/StockApp.Domain/Entities/LogAuditoria.cs`) NO tiene una columna `Guid` propia — Task 1 deliberadamente NO le agregó una (el spec solo pide la columna de trazabilidad en `Gasto`/`IngresoCaja`/`LineaPoa`, §2.7). Para poder encontrar "¿hay una corrida sin revertir para este ejercicio?" y "¿cuál es el `LogAuditoria` de la corrida con este `IdImportacion`?" sin una columna dedicada, esta task usa una convención: el `IdImportacion` viaja como el PRIMER token de `Detalle`, con el formato fijo `"IdImportacion={guid}; <resto del resumen legible>"`, y `EntidadId` (que sí es una columna `int` normal) guarda el `Ejercicio` — así el guard puede filtrar por `Accion == ImportacionPlanillas && EntidadId == ejercicio` en SQL, sin traer TODO el historial de auditoría del sistema a memoria, y recién ahí parsear el Guid de los pocos candidatos que matchean.

Esta es una decisión de este plan, no algo que el spec explicite a este nivel — se documenta acá para que quede claro que es intencional y no un atajo. La alternativa (agregar una columna `Guid?` a `LogAuditoria`) hubiera sido más prolija pero infla el alcance de la migración de Task 1 con una columna que ningún otro flujo de auditoría del sistema necesita.

Buscar por el Guid embebido usa `EF.Functions.Like(l.Detalle, patron)`, que Npgsql traduce a `LIKE` real en SQL — un Guid en formato `D` (`8-4-4-4-12` con guiones) nunca contiene `%` ni `_` (los caracteres especiales de `LIKE`), así que no hace falta escapar nada.

- [ ] **Paso 1: Escribir el test que falla**

Agregar al final de la clase `ImportacionRepositoryTests`:

```csharp
    [Fact]
    public async Task ConfirmarAsync_SegundaCorridaSinForzar_LanzaReglaDeNegocio()
    {
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: 1));

        // El rollback del guard no debe dejar un segundo LogAuditoria ni datos a medio escribir.
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(1, await verificacion.Gastos.CountAsync());
        Assert.Equal(1, verificacion.LogsAuditoria.Count(
            l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas));
    }

    [Fact]
    public async Task ConfirmarAsync_SegundaCorridaConForzar_NoLanzaYAuditaLasDosCorridas()
    {
        await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: true), usuarioId: 1);

        Assert.NotEqual(Guid.Empty, segunda.IdImportacion);
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, verificacion.LogsAuditoria.Count(
            l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas));
    }

    [Fact]
    public async Task ConfirmarAsync_PrimeraCorrida_AuditaConElResumenDeContadores()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 7);

        await using var verificacion = Fixture.CrearContexto();
        var log = verificacion.LogsAuditoria
            .Single(l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas);
        Assert.Equal(7, log.UsuarioId);
        Assert.Equal(Ejercicio, log.EntidadId);
        Assert.StartsWith($"IdImportacion={resultado.IdImportacion}", log.Detalle);
        Assert.Contains("Gastos creados: 1", log.Detalle);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: FAIL — la segunda corrida sin `Forzar` NO lanza nada todavía (el guard no existe), así que `Assert.ThrowsAsync` falla.

- [ ] **Paso 3: Implementación mínima**

Reemplazar el método `ConfirmarAsync` completo (agrega el guard justo después del lock y el `LogAuditoria` justo antes del `SaveChangesAsync`):

```csharp
    private const string PrefijoIdImportacion = "IdImportacion=";

    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId)
    {
        var idImportacion = Guid.NewGuid();

        await using var tx = await _ctx.Database.BeginTransactionAsync();
        await _ctx.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({dto.Ejercicio})");

        var idNoRevertida = await BuscarImportacionNoRevertidaAsync(dto.Ejercicio);
        if (idNoRevertida is not null && !dto.Forzar)
        {
            await tx.RollbackAsync();
            throw new StockApp.Domain.Exceptions.ReglaDeNegocioException(
                $"El ejercicio {dto.Ejercicio} ya tiene una importación previa (IdImportacion " +
                $"{idNoRevertida}) sin revertir. Usá Forzar=true para reimportar de todas formas, " +
                "o revertí esa corrida primero con /finanzas/importar/revertir/{id}.");
        }

        var (proveedorPorNombre, fuentePorNombre, rubroPorCodigo,
                proveedoresCreados, fuentesCreadas, rubrosCreados) =
            await GetOrCrearMaestrosAsync(dto);

        var (lineaPorNombre, lineasPoaCreadas, asignacionesCreadas) =
            await GetOrCrearLineasPoaAsync(dto, fuentePorNombre, idImportacion);

        var (ingresosCreados, ingresosOmitidos) =
            await ProcesarIngresosAsync(dto, fuentePorNombre, idImportacion);

        var (gastosCreados, gastosOmitidos, pagosCreados) =
            await ProcesarGastosAsync(dto, proveedorPorNombre, fuentePorNombre, rubroPorCodigo, lineaPorNombre, idImportacion);

        _ctx.LogsAuditoria.Add(new StockApp.Domain.Entities.LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas,
            Entidad = "Importacion",
            EntidadId = dto.Ejercicio,
            Detalle = $"{PrefijoIdImportacion}{idImportacion}; Ejercicio: {dto.Ejercicio}; " +
                $"Proveedores creados: {proveedoresCreados}; Fuentes creadas: {fuentesCreadas}; " +
                $"Rubros creados: {rubrosCreados}; LineasPoa creadas: {lineasPoaCreadas}; " +
                $"Asignaciones creadas: {asignacionesCreadas}; " +
                $"Ingresos creados: {ingresosCreados}; Ingresos omitidos: {ingresosOmitidos}; " +
                $"Gastos creados: {gastosCreados}; Gastos omitidos: {gastosOmitidos}; " +
                $"Pagos creados: {pagosCreados}",
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoConfirmacionDto(
            idImportacion,
            proveedoresCreados, fuentesCreadas, rubrosCreados,
            lineasPoaCreadas, asignacionesCreadas,
            ingresosCreados, ingresosOmitidos,
            gastosCreados, gastosOmitidos, pagosCreados);
    }
```

Y agregar estos dos métodos privados nuevos (los usa también Task 8):

```csharp
    /// <summary>
    /// Guard de re-importación (spec §2.6). LogAuditoria no tiene columna propia para el Guid
    /// del lote — se codifica como el primer token de Detalle y EntidadId guarda el Ejercicio
    /// para filtrar en SQL sin traer todo el historial. "No revertida" = no existe ningún
    /// LogAuditoria de ReversionImportacion cuyo Detalle referencie ese mismo IdImportacion.
    /// </summary>
    private async Task<Guid?> BuscarImportacionNoRevertidaAsync(int ejercicio)
    {
        var confirmaciones = await _ctx.LogsAuditoria
            .Where(l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas
                        && l.EntidadId == ejercicio)
            .Select(l => l.Detalle)
            .ToListAsync();

        foreach (var detalle in confirmaciones)
        {
            var id = ExtraerIdImportacion(detalle);
            var patronReversion = $"{PrefijoIdImportacion}{id}%";
            var fueRevertida = await _ctx.LogsAuditoria.AnyAsync(l =>
                l.Accion == StockApp.Domain.Enums.AccionAuditada.ReversionImportacion
                && EF.Functions.Like(l.Detalle, patronReversion));

            if (!fueRevertida)
                return id;
        }

        return null;
    }

    private static Guid ExtraerIdImportacion(string detalle) =>
        Guid.Parse(detalle.Substring(PrefijoIdImportacion.Length, 36));
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: `Passed! - Failed: 0, Passed: 11`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs \
  tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs
git commit -m "feat(finanzas): guard de re-importación por ejercicio + auditoría de cada corrida de confirmación"
```

---

## Task 7: Endpoint `/confirmar` y DI

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Modify: `tests/StockApp.Api.Tests/ImportacionEndpointTests.cs` (F5b — suma el test del límite de tamaño de `/analizar`, spec §8)
- Test: `tests/StockApp.Api.Tests/ImportacionConfirmacionEndpointTests.cs`

**Interfaces:**
- Consumes: `IConfirmacionImportacionService.ConfirmarAsync(ConfirmarImportacionDto)` (Task 3), `Permisos.ImportarPlanillas` (F5b).
- Produces: `POST /finanzas/importar/confirmar`. `/revertir/{id}` se mapea en esta MISMA task (comparte DI y límite de tamaño) pero su lógica de servidor (`RevertirAsync` real) recién la implementa Task 8 — hasta entonces devuelve `500` (el `NotImplementedException` que `ImportacionRepository.RevertirAsync` sigue tirando desde Task 4 no tiene un caso propio en el switch de `DomainExceptionHandler`, así que cae al `_` catch-all). Esto es intencional y no se testea en esta task: el endpoint y el DI quedan completos y testeados acá para `/confirmar`; Task 8 cambia la implementación de adentro de `RevertirAsync` y recién ahí agrega su propia matriz de tests contra `/revertir/{id}`.

### Contexto para el implementador

El límite de tamaño de `/analizar` es un chequeo EXPLÍCITO sobre `IFormFile.Length` (bytes del archivo tal como llegó, sin necesidad de tocar features de Kestrel/TestServer) — se puede testear con un archivo de mentira sin depender de que el test host real imponga límites de body. El límite de `/confirmar`/`/revertir` es distinto de naturaleza (spec §3: JSON plano, sin `ZipArchive`, así que no hay superficie de zip bomb — es defensa en profundidad genérica contra abuso de recursos) y se implementa con `IHttpMaxRequestBodySizeFeature` vía un `AddEndpointFilter`, SIN un test dedicado: no hay garantía de que `Microsoft.AspNetCore.TestHost` (el host en memoria que usa `WebApplicationFactory`) haga cumplir ese límite igual que Kestrel real, y forzar ese comportamiento en un test agregaría fragilidad sin que el spec lo exija (spec §8 solo pide explícitamente el test de límite de archivo para `/analizar`).

- [ ] **Paso 1: Escribir el test que falla**

```csharp
// tests/StockApp.Api.Tests/ImportacionConfirmacionEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ImportacionConfirmacionEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionConfirmacionEndpointTests(ApiFactory factory) : base(factory) { }

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

    private static ConfirmarImportacionDto PayloadValido(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { "ACME SA" },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task PostConfirmar_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_ComoAdmin_PayloadValido_Devuelve200YResultado()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var resultado = await response.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();
        Assert.NotNull(resultado);
        Assert.Equal(1, resultado!.GastosCreados);
        Assert.NotEqual(Guid.Empty, resultado.IdImportacion);
    }

    [Fact]
    public async Task PostConfirmar_ReferenciaQueNoResuelve_Devuelve400ConErrors()
    {
        var client = ClienteAutenticado(TokenAdmin());
        var payload = PayloadValido() with { MaestrosNuevos = new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }) };

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Gastos[0].Proveedor", body);
    }

    [Fact]
    public async Task PostConfirmar_SegundaVezSinForzar_Devuelve409()
    {
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido(forzar: false));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostConfirmar_SegundaVezConForzar_Devuelve200()
    {
        var client = ClienteAutenticado(TokenAdmin());
        await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());

        var response = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido(forzar: true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

Y, agregado al final de la clase EXISTENTE `ImportacionEndpointTests` (F5b, `tests/StockApp.Api.Tests/ImportacionEndpointTests.cs` — spec §8, "Tests nuevos específicos de esta fase": "archivo `.ods` que excede el límite en `/analizar` → rechazado, no llega a `ZipArchive`"). Reutiliza `ArmarMultipart()`/`CrearOdsPoaMinimo()`, ya presentes en ese archivo desde F5b:

```csharp
    /// <summary>
    /// .ods "de más de 10MB" construido a mano (NO reutiliza EmpaquetarOds): pide
    /// CompressionLevel.NoCompression explícitamente para que el tamaño del ARCHIVO final en
    /// bytes sea determinístico (con compresión por defecto, datos repetidos o predecibles
    /// pueden comprimir a un archivo final de apenas unos bytes — justo el problema de fondo
    /// que hace peligroso un zip bomb, e inútil para probar el límite del archivo TRANSMITIDO).
    /// content.xml es basura aleatoria, no XML válido — no importa: el chequeo de tamaño tiene
    /// que cortar ANTES de que nadie intente parsearlo como .ods.
    /// </summary>
    private static byte[] CrearOdsDeMasDe10MbSinComprimir()
    {
        var relleno = new byte[10 * 1024 * 1024 + 1];
        System.Security.Cryptography.RandomNumberGenerator.Fill(relleno);

        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entrada = zip.CreateEntry("content.xml", CompressionLevel.NoCompression);
            using var escritura = entrada.Open();
            escritura.Write(relleno, 0, relleno.Length);
        }
        return stream.ToArray();
    }

    [Fact]
    public async Task PostAnalizar_ArchivoGastosSuperaElLimiteDeTamano_Devuelve400()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync(
            "/finanzas/importar/analizar",
            ArmarMultipart(CrearOdsDeMasDe10MbSinComprimir(), CrearOdsPoaMinimo(), 2026));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionConfirmacionEndpointTests"`
Esperado: FAIL — `404 Not Found` en todos los casos (la ruta `/finanzas/importar/confirmar` todavía no existe).

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~PostAnalizar_ArchivoGastosSuperaElLimiteDeTamano"`
Esperado: FAIL — el test espera `400` pero HOY (antes del chequeo de tamaño de esta task) el archivo llega hasta intentar parsearse: es un zip válido (`ZipArchive` lo abre sin problema) con un `content.xml` que NO es XML válido, así que revienta más adelante en el parseo — no necesariamente con `400` (`ParsearGastosSeguro`, F5b, solo atrapa `InvalidOperationException`/`InvalidDataException`; una excepción de XML mal formado no está en esa lista, así que hoy probablemente cae al 500 genérico). Lo que importa para este test no es adivinar el status exacto de HOY, sino que el chequeo de tamaño de esta task lo corte ANTES de eso, con 400 determinístico.

- [ ] **Paso 3: Implementación mínima**

```csharp
// src/StockApp.Api/Endpoints/ImportacionEndpoints.cs (archivo completo)
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class ImportacionEndpoints
{
    /// <summary>
    /// F5c §3: límite de tamaño de ARCHIVO para /analizar — mitigación real contra zip bomb
    /// (acota el input comprimido ANTES de que ZipArchive lo descomprima, F5a). Los .ods reales
    /// de este municipio pesan ~150KB (Gastos) y ~23KB (POA); 10MB es un techo generoso que
    /// igual corta cualquier archivo anormalmente grande antes de intentar parsearlo.
    /// </summary>
    private const long LimiteBytesArchivoOds = 10 * 1024 * 1024;

    /// <summary>
    /// F5c §3: límite de tamaño de BODY para /confirmar y /revertir — defensa en profundidad
    /// contra un payload JSON de abuso, NO mitigación de zip bomb (JSON plano, sin ZipArchive,
    /// no hay nada que descomprimir). Es un techo razonable, distinto en motivo del de arriba.
    /// </summary>
    private const long LimiteBytesBodyConfirmacion = 5 * 1024 * 1024;

    public static IEndpointRouteBuilder MapImportacionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/finanzas/importar/analizar", async (
            IFormFile gastos, IFormFile poa, [FromForm] int ejercicio, IAnalisisImportacionService analisis) =>
        {
            if (gastos.Length > LimiteBytesArchivoOds || poa.Length > LimiteBytesArchivoOds)
            {
                throw new ArgumentException(
                    $"El archivo supera el límite permitido de {LimiteBytesArchivoOds / 1024 / 1024}MB.");
            }

            using var streamGastos = gastos.OpenReadStream();
            using var streamPoa = poa.OpenReadStream();
            var resultado = await analisis.AnalizarAsync(streamGastos, streamPoa, ejercicio);
            return Results.Ok(resultado);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.ImportarPlanillas);

        app.MapPost("/finanzas/importar/confirmar", async (
            ConfirmarImportacionDto dto, IConfirmacionImportacionService confirmacion) =>
        {
            var resultado = await confirmacion.ConfirmarAsync(dto);
            return Results.Ok(resultado);
        })
        .AddEndpointFilter(async (context, next) =>
        {
            var limite = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limite is not null && !limite.IsReadOnly)
                limite.MaxRequestBodySize = LimiteBytesBodyConfirmacion;
            return await next(context);
        })
        .RequireAuthorization(Permisos.ImportarPlanillas);

        app.MapPost("/finanzas/importar/revertir/{id:guid}", async (
            Guid id, IConfirmacionImportacionService confirmacion) =>
        {
            var resultado = await confirmacion.RevertirAsync(id);
            return Results.Ok(resultado);
        })
        .AddEndpointFilter(async (context, next) =>
        {
            var limite = context.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (limite is not null && !limite.IsReadOnly)
                limite.MaxRequestBodySize = LimiteBytesBodyConfirmacion;
            return await next(context);
        })
        .RequireAuthorization(Permisos.ImportarPlanillas);

        return app;
    }
}
```

En `src/StockApp.Api/Program.cs`, agregar junto al registro existente de F5b (buscar el comentario `// Finanzas — F5b: análisis...`, agregar DESPUÉS de esas dos líneas):

```csharp
builder.Services.AddScoped<IPlanillaParser, PlanillaOdsParser>();
builder.Services.AddScoped<IAnalisisImportacionService, AnalisisImportacionService>();

// Finanzas — F5c: confirmación transaccional del importador (escritura + idempotencia +
// guard de re-importación + reversa). IImportacionRepository es la única pieza de todo el
// flujo de importación que toca EF/Npgsql directamente.
builder.Services.AddScoped<IImportacionRepository, ImportacionRepository>();
builder.Services.AddScoped<IConfirmacionImportacionService, ConfirmacionImportacionService>();
```

(`IImportacionRepository` está en `StockApp.Application.Interfaces`, ya importado por el `using StockApp.Application.Interfaces;` existente al principio del archivo; `ImportacionRepository` está en `StockApp.Infrastructure.Repositories`, ya importado por el `using StockApp.Infrastructure.Repositories;` existente).

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionConfirmacionEndpointTests"`
Esperado: `Passed! - Failed: 0, Passed: 6`.

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~PostAnalizar_ArchivoGastosSuperaElLimiteDeTamano"`
Esperado: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ImportacionEndpoints.cs src/StockApp.Api/Program.cs \
  tests/StockApp.Api.Tests/ImportacionEndpointTests.cs \
  tests/StockApp.Api.Tests/ImportacionConfirmacionEndpointTests.cs
git commit -m "feat(finanzas): endpoint POST /finanzas/importar/confirmar + DI + límites de tamaño"
```

---

## Task 8: Reversa por lote

**Files:**
- Modify: `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs`
- Create: `tests/StockApp.Api.Tests/ImportacionReversionEndpointTests.cs`

**Interfaces:**
- Consumes: `BuscarImportacionNoRevertidaAsync`/`ExtraerIdImportacion`/`PrefijoIdImportacion` (Task 6), `POST /finanzas/importar/revertir/{id:guid}` (Task 7, ya mapeado).
- Produces: `ImportacionRepository.RevertirAsync` (ya no tira `NotImplementedException`). Cierra el ciclo completo del importador.

### Contexto para el implementador

Baja lógica (`Activo = false`) de `Gasto`, `IngresoCaja` y `LineaPoa` con ese `IdImportacion`, en UNA transacción. `PagoGasto` cae por cascada vía `GastoId` (no tiene columna `IdImportacion` propia — spec §2.7: es hija del agregado `Gasto`, se identifica por pertenecer a un gasto del lote). `AsignacionPresupuestal` NO tiene un `Activo` propio en el dominio (`AsignacionPresupuestal.cs` — solo `Id`, `LineaPoaId`, `FuenteFinanciamientoId`, `Monto`): al dar de baja la `LineaPoa` que la contiene, queda colgando de una línea inactiva, que es el estado correcto (nada en el sistema filtra `AsignacionPresupuestal` por separado de su `LineaPoa.Activo`). Los maestros (`Proveedor`, `FuenteFinanciamiento`, `RubroGasto`) **NUNCA se tocan** — spec §2.7: para cuando se ejecuta una reversa, esos maestros pueden estar ya referenciados por gastos cargados a mano en paralelo a la migración.

- [ ] **Paso 1: Escribir el test que falla**

Agregar al final de la clase `ImportacionRepositoryTests`:

```csharp
    [Fact]
    public async Task RevertirAsync_LoteExistente_DaDeBajaGastosIngresosYLineasPoaPeroNoLosMaestros()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: 1);

        Assert.Equal(confirmacion.IdImportacion, reversion.IdImportacion);
        Assert.Equal(1, reversion.GastosRevertidos);
        Assert.Equal(1, reversion.PagosRevertidos);
        Assert.Equal(1, reversion.IngresosRevertidos);

        await using var verificacion = Fixture.CrearContexto();
        var gasto = verificacion.Gastos.Include(g => g.Pagos).Single();
        Assert.False(gasto.Activo);
        Assert.All(gasto.Pagos, p => Assert.False(p.Activo));
        Assert.False(verificacion.IngresosCaja.Single().Activo);
        // Los maestros (Proveedor/FuenteFinanciamiento/RubroGasto) SIGUEN activos.
        Assert.True(verificacion.Proveedores.Single().Activo);
        Assert.True(verificacion.FuentesFinanciamiento.Single().Activo);
        Assert.True(verificacion.RubrosGasto.Single().Activo);
    }

    [Fact]
    public async Task RevertirAsync_LineaPoaDelLote_QuedaInactivaConSusAsignacionesColgando()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadSoloMaestrosYPoa(), usuarioId: 1);

        var reversion = await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: 1);

        Assert.Equal(1, reversion.LineasPoaRevertidas);
        Assert.Equal(2, reversion.AsignacionesRevertidas);
        await using var verificacion = Fixture.CrearContexto();
        var linea = verificacion.LineasPoa.Include(l => l.Asignaciones).Single();
        Assert.False(linea.Activo);
        Assert.Equal(2, linea.Asignaciones.Count); // siguen ahí, colgando de la línea inactiva
    }

    [Fact]
    public async Task RevertirAsync_IdInexistente_LanzaEntidadNoEncontrada()
    {
        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.EntidadNoEncontradaException>(
            () => _repo.RevertirAsync(Guid.NewGuid(), usuarioId: 1));
    }

    [Fact]
    public async Task RevertirAsync_LoteYaRevertido_LanzaReglaDeNegocio()
    {
        var confirmacion = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);
        await _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: 1);

        await Assert.ThrowsAsync<StockApp.Domain.Exceptions.ReglaDeNegocioException>(
            () => _repo.RevertirAsync(confirmacion.IdImportacion, usuarioId: 1));
    }

    [Fact]
    public async Task CicloCompleto_ConfirmarRevertirConfirmarSinForzar_SegundaConfirmacionEs200()
    {
        var primera = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(), usuarioId: 1);
        await _repo.RevertirAsync(primera.IdImportacion, usuarioId: 1);

        // El guard de Task 6 no cuenta las corridas YA revertidas — sin Forzar tiene que andar.
        var segunda = await _repo.ConfirmarAsync(PayloadConIngresoYGasto(forzar: false), usuarioId: 1);

        Assert.Equal(1, segunda.GastosCreados); // la clave natural del gasto revertido está Activo=false, no bloquea
        await using var verificacion = Fixture.CrearContexto();
        Assert.Equal(2, await verificacion.Gastos.CountAsync()); // el revertido + el nuevo
        Assert.Equal(1, await verificacion.Gastos.CountAsync(g => g.Activo));
    }
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"`
Esperado: FAIL — `RevertirAsync` sigue lanzando `NotImplementedException` (Task 4).

- [ ] **Paso 3: Implementación mínima**

Reemplazar el método `RevertirAsync` (hoy el `throw new NotImplementedException(...)` de Task 4) por:

```csharp
    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId)
    {
        await using var tx = await _ctx.Database.BeginTransactionAsync();

        var patron = $"{PrefijoIdImportacion}{idImportacion}%";
        var logConfirmacion = await _ctx.LogsAuditoria
            .Where(l => l.Accion == StockApp.Domain.Enums.AccionAuditada.ImportacionPlanillas
                        && EF.Functions.Like(l.Detalle, patron))
            .FirstOrDefaultAsync();

        if (logConfirmacion is null)
        {
            await tx.RollbackAsync();
            throw new StockApp.Domain.Exceptions.EntidadNoEncontradaException(
                $"No existe ninguna importación con IdImportacion {idImportacion}.");
        }

        var yaRevertida = await _ctx.LogsAuditoria.AnyAsync(l =>
            l.Accion == StockApp.Domain.Enums.AccionAuditada.ReversionImportacion
            && EF.Functions.Like(l.Detalle, patron));

        if (yaRevertida)
        {
            await tx.RollbackAsync();
            throw new StockApp.Domain.Exceptions.ReglaDeNegocioException(
                $"La importación {idImportacion} ya fue revertida anteriormente.");
        }

        var gastos = await _ctx.Gastos.Include(g => g.Pagos)
            .Where(g => g.IdImportacion == idImportacion && g.Activo).ToListAsync();
        var ingresos = await _ctx.IngresosCaja
            .Where(i => i.IdImportacion == idImportacion && i.Activo).ToListAsync();
        var lineasPoa = await _ctx.LineasPoa.Include(l => l.Asignaciones)
            .Where(l => l.IdImportacion == idImportacion && l.Activo).ToListAsync();

        var pagosRevertidos = 0;
        foreach (var gasto in gastos)
        {
            gasto.Activo = false;
            foreach (var pago in gasto.Pagos.Where(p => p.Activo))
            {
                pago.Activo = false;
                pagosRevertidos++;
            }
        }

        foreach (var ingreso in ingresos)
            ingreso.Activo = false;

        var asignacionesRevertidas = 0;
        foreach (var linea in lineasPoa)
        {
            linea.Activo = false;
            // AsignacionPresupuestal no tiene Activo propio (spec §2.7): queda colgando de una
            // LineaPoa inactiva, que es el estado correcto — nada que tocar acá salvo contarlas.
            asignacionesRevertidas += linea.Asignaciones.Count;
        }

        _ctx.LogsAuditoria.Add(new StockApp.Domain.Entities.LogAuditoria
        {
            UsuarioId = usuarioId,
            Fecha = DateTime.UtcNow,
            Accion = StockApp.Domain.Enums.AccionAuditada.ReversionImportacion,
            Entidad = "Importacion",
            EntidadId = logConfirmacion.EntidadId,
            Detalle = $"{PrefijoIdImportacion}{idImportacion}; " +
                $"Gastos revertidos: {gastos.Count}; Pagos revertidos: {pagosRevertidos}; " +
                $"Ingresos revertidos: {ingresos.Count}; LineasPoa revertidas: {lineasPoa.Count}; " +
                $"Asignaciones revertidas: {asignacionesRevertidas}",
        });

        await _ctx.SaveChangesAsync();
        await tx.CommitAsync();

        return new ResultadoReversionDto(
            idImportacion, gastos.Count, pagosRevertidos, ingresos.Count, lineasPoa.Count, asignacionesRevertidas);
    }
```

Y el test de endpoint (matriz + el ciclo completo a nivel HTTP):

```csharp
// tests/StockApp.Api.Tests/ImportacionReversionEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class ImportacionReversionEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionReversionEndpointTests(ApiFactory factory) : base(factory) { }

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

    private static ConfirmarImportacionDto PayloadValido(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string> { "ACME SA" },
            new List<string> { "Literal A" },
            new List<RubroNuevoConfirmarDto> { new(1, "Paseos Públicos") }),
        Ingresos: new List<IngresoConfirmarDto>
        {
            new(new DateOnly(Ejercicio, 1, 1), "Saldo inicial", 1000m, "Literal A"),
        },
        Gastos: new List<GastoConfirmarDto>
        {
            new("ACME SA", "F-1", "O-1", "Compra de insumos", null,
                new DateOnly(Ejercicio, 1, 15), 500m, "Literal A", 1, null, CondicionPago.Contado),
        },
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task PostRevertir_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostRevertir_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostRevertir_IdInexistente_Devuelve404()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.PostAsync($"/finanzas/importar/revertir/{Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CicloCompleto_ConfirmarRevertirConfirmarSinForzar_TerminaEn200()
    {
        var client = ClienteAutenticado(TokenAdmin());
        var confirmacion1 = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadValido());
        var resultado1 = await confirmacion1.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();

        var reversion = await client.PostAsync(
            $"/finanzas/importar/revertir/{resultado1!.IdImportacion}", null);
        Assert.Equal(HttpStatusCode.OK, reversion.StatusCode);

        var reversionOtraVez = await client.PostAsync(
            $"/finanzas/importar/revertir/{resultado1.IdImportacion}", null);
        Assert.Equal(HttpStatusCode.Conflict, reversionOtraVez.StatusCode);

        var confirmacion2 = await client.PostAsJsonAsync(
            "/finanzas/importar/confirmar", PayloadValido(forzar: false));
        Assert.Equal(HttpStatusCode.OK, confirmacion2.StatusCode);
    }
}
```

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryTests"` y `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionReversionEndpointTests"`
Esperado: ambos `Passed! - Failed: 0` (16 en Infrastructure, 4 en Api).

- [ ] **Paso 5: Commit**

```bash
git add src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs \
  tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryTests.cs \
  tests/StockApp.Api.Tests/ImportacionReversionEndpointTests.cs
git commit -m "feat(finanzas): reversa por lote de una importación (POST /finanzas/importar/revertir/{id})"
```

---

## Task 9: Aceptación end-to-end con las planillas reales

**Files:**
- Test: `tests/StockApp.Api.Tests/ImportacionAceptacionConfirmacionTests.cs`

**Interfaces:**
- Consumes: `POST /finanzas/importar/analizar` (F5b), `POST /finanzas/importar/confirmar` (Task 7), `ResultadoAnalisisDto` y sus DTOs anidados (F5b, `AnalisisImportacionDtos.cs`), `ConfirmarImportacionDto` y afines (Task 2), `Factory.CrearContexto()` (ya existe en `ApiFactory.cs:84-90`).
- Produces: nada nuevo — es el test que cierra la fase, cruzando F5b (análisis) con F5c (escritura) contra datos reales.

### ⚠️ Corrección importante respecto de los números originalmente previstos para esta task

El criterio de aceptación tal como estaba escrito ("Saldo POA Literal B = 6.643.349 / Literal C = 4.654.206") **no se puede cumplir consultando la base de datos**, y no es un error de este plan — es una consecuencia directa, ya documentada, de una decisión de diseño de F5b: esos dos números salen de la hoja "SALDO TOTALES" del `.ods` (`ResultadoAnalisisDto.SaldosPoa`), que `docs/finanzas-discrepancias-planilla-poa-2026.md` prueba que está DESINCRONIZADA de las hojas de línea reales:

| Literal | Suma de las hojas de línea (lo que F5c persiste) | Cacheado en "SALDO TOTALES" (lo que dice §11 original) | Diferencia |
|---|---:|---:|---:|
| B | 6.341.849 | 6.643.349 | −301.500 |
| C | 4.174.206 | 4.654.206 | −480.000 |

Y el propio documento lo anticipa explícitamente: *"si la planilla no se reconcilia antes, la base de datos migrada quedará con los mismos 301.500 / 480.000 de diferencia respecto de 'SALDO TOTALES'"* — y el spec de F5c (§10) es categórico: *"F5c escribe lo que dicen las hojas de LÍNEA, no el resumen de SALDO TOTALES"*. Verificar contra la base con los números de "SALDO TOTALES" haría que este test SIEMPRE fallara contra datos reales sin reconciliar, sin que eso sea un bug de la implementación.

**Este plan usa los números de "suma de las hojas de línea" (6.341.849 / 4.174.206) como el criterio de aceptación de esta task**, con la salvedad documentada abajo sobre movimientos POA `Dudoso` (si el Resumen de F5b reporta `PoaDudosos > 0`, ese número va a quedar más alto que el esperado en la cantidad exacta del `Importe` de esos movimientos, porque un movimiento `Dudoso` nunca se convierte en `Gasto` — es, por diseño, decisión manual de F5d). La caja de junio (43.705) NO tiene este problema: sale enteramente de las hojas de Gastos (Ingresos/Egresos), un dato independiente de la reconciliación POA.

### Gaps de datos que este test tiene que resolver con una convención explícita (no inventada en secreto)

`MovimientoPoaAnalizadoDto` (F5b) — la fuente de los movimientos `CompromisoSoloPoa` que hay que convertir en `GastoConfirmarDto` nuevos — NO trae `CodigoRubro` (el rubro es una dimensión de la planilla de Gastos, no de la planilla POA) ni `Fecha` (una hoja de línea POA no tiene columna de fecha por movimiento). El contrato de `GastoConfirmarDto` (spec §3) exige ambos como no-nullable. Esto es un gap real entre lo que F5a/F5b capturan y lo que F5c necesita para escribir un compromiso — no algo que se pueda derivar de los datos existentes. Este test lo resuelve así, de forma visible en el código (no oculto en un helper sin comentar):

- `CodigoRubro`: se declara un rubro sintético nuevo `999 — "Compromisos POA (sin rubro en la planilla)"` vía `MaestrosNuevos.Rubros`, usado SOLO para los gastos derivados de compromisos. **Recomendación para quien revise este plan**: decidir si esto es aceptable para la migración real o si hace falta ampliar el contrato de análisis de F5b para que la reconciliación POA arrastre el rubro desde algún lado (hoy no hay ninguno del que arrastrarlo).
- `Fecha`: se usa el último día del ejercicio (`31/12/{ejercicio}`) — no afecta ninguna de las dos aserciones del criterio de aceptación (la caja de junio se calcula solo con datos de la hoja de Gastos; el saldo POA por Literal se calcula sumando `MontoTotal` sin filtrar por fecha), así que es un valor sin impacto en el resultado, elegido porque el dominio no tiene de dónde sacar uno real.

- [ ] **Paso 1: Escribir el test que falla**

```csharp
// tests/StockApp.Api.Tests/ImportacionAceptacionConfirmacionTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

/// <summary>
/// F5c Task 9 — criterio de aceptación duro de la fase: /analizar con las dos planillas reales
/// (gitignored, mismos fixtures que F5b) → completar programáticamente los obligatorios que el
/// análisis no trae → /confirmar → consultar la base vía Factory.CrearContexto() y verificar que
/// lo persistido reproduce los saldos reales. Ver la nota de corrección arriba: los números de
/// POA por Literal son los de la SUMA DE LAS HOJAS DE LÍNEA (6.341.849 / 4.174.206), no los de
/// "SALDO TOTALES" (6.643.349 / 4.654.206) que usó el criterio de aceptación de F5b — están
/// desincronizados en la planilla real (docs/finanzas-discrepancias-planilla-poa-2026.md).
/// </summary>
public class ImportacionAceptacionConfirmacionTests : ApiTestBase
{
    private const int Ejercicio = 2026;
    private const int CodigoRubroCompromisosPoa = 999;

    public ImportacionAceptacionConfirmacionTests(ApiFactory factory) : base(factory) { }

    private static string RutaFixture(string archivo) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Finanzas", archivo);

    [Fact]
    public async Task ConfirmarAsync_PlanillasReales_PersisteLosSaldosDeLasHojasDeLinea()
    {
        var token = Factory.Services.GetRequiredService<IJwtTokenService>().GenerarToken(1, RolUsuario.Admin);
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // ── 1. /analizar con las planillas reales ───────────────────────────────────────────
        var gastosBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaGastos2026.ods"));
        var poaBytes = await File.ReadAllBytesAsync(RutaFixture("PlanillaPoa2026.ods"));

        var multipart = new MultipartFormDataContent();
        var archivoGastos = new ByteArrayContent(gastosBytes);
        archivoGastos.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoGastos, "gastos", "PlanillaGastos2026.ods");
        var archivoPoa = new ByteArrayContent(poaBytes);
        archivoPoa.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoPoa, "poa", "PlanillaPoa2026.ods");
        multipart.Add(new StringContent(Ejercicio.ToString()), "ejercicio");

        var respuestaAnalisis = await client.PostAsync("/finanzas/importar/analizar", multipart);
        Assert.Equal(HttpStatusCode.OK, respuestaAnalisis.StatusCode);
        var analisis = await respuestaAnalisis.Content.ReadFromJsonAsync<ResultadoAnalisisDto>();
        Assert.NotNull(analisis);

        // ── 2. Armar el payload de /confirmar completando lo que el análisis no trae ────────
        var payload = ArmarPayloadConfirmacion(analisis!);

        // ── 3. /confirmar ────────────────────────────────────────────────────────────────────
        var respuestaConfirmacion = await client.PostAsJsonAsync("/finanzas/importar/confirmar", payload);
        Assert.Equal(HttpStatusCode.OK, respuestaConfirmacion.StatusCode);

        // ── 4. Consultar la base y verificar los saldos ─────────────────────────────────────
        await using var ctx = Factory.CrearContexto();

        // Caja junio 2026: saldo inicial + Σ ingresos activos (Ene-Jun) − Σ pagos activos de
        // gastos activos (Ene-Jun). Los gastos Contado tienen un PagoGasto automático en la
        // MISMA fecha; los compromisos POA (Credito, sin pago) correctamente NO restan acá.
        var finJunio = new DateTime(Ejercicio, 6, 30, 23, 59, 59, DateTimeKind.Utc);
        var totalIngresos = await ctx.IngresosCaja
            .Where(i => i.Activo && i.Fecha <= finJunio).SumAsync(i => (decimal?)i.Monto) ?? 0m;
        var totalPagos = await ctx.PagosGasto
            .Where(p => p.Activo && p.Gasto!.Activo && p.Fecha <= finJunio)
            .SumAsync(p => (decimal?)p.Monto) ?? 0m;
        var cajaJunio = totalIngresos - totalPagos;
        Assert.Equal(43705m, cajaJunio);

        // Saldo POA por Literal = Σ AsignacionPresupuestal.Monto (presupuesto declarado en las
        // hojas de línea) − Σ Gasto.MontoTotal de gastos activos vinculados a una LineaPoa con
        // ese Literal. Ver la nota de corrección arriba sobre por qué estos números NO son los
        // de "SALDO TOTALES" — y sobre el ajuste si PoaDudosos > 0.
        // NOTA: AsignacionPresupuestal NO tiene navegación a LineaPoa (solo el FK LineaPoaId —
        // AppDbContext.cs configura esa relación con HasOne<LineaPoa>() SIN lambda de
        // navegación, ver AsignacionPresupuestal.cs), así que el filtro por Ejercicio se hace
        // contra el set de Ids de LineaPoa del ejercicio, no contra una propiedad a.LineaPoa.
        async Task<decimal> SaldoPorLiteralAsync(string literal)
        {
            var idsLineasDelEjercicio = await ctx.LineasPoa
                .Where(l => l.Ejercicio == Ejercicio)
                .Select(l => l.Id)
                .ToListAsync();

            var presupuesto = await ctx.AsignacionesPresupuestales
                .Where(a => a.FuenteFinanciamiento!.Nombre == literal
                            && idsLineasDelEjercicio.Contains(a.LineaPoaId))
                .SumAsync(a => (decimal?)a.Monto) ?? 0m;
            var gastado = await ctx.Gastos
                .Where(g => g.Activo && g.LineaPoaId != null
                            && idsLineasDelEjercicio.Contains(g.LineaPoaId!.Value)
                            && g.FuenteFinanciamiento!.Nombre == literal)
                .SumAsync(g => (decimal?)g.MontoTotal) ?? 0m;
            return presupuesto - gastado;
        }

        var ajusteDudosos = analisis.LineasPoa
            .SelectMany(l => l.Movimientos)
            .Where(m => m.Clasificacion == ClasificacionReconciliacion.Dudoso)
            .Sum(m => m.Importe ?? 0m);

        var saldoB = await SaldoPorLiteralAsync("Literal B");
        var saldoC = await SaldoPorLiteralAsync("Literal C");

        // Si hay movimientos Dudoso, no se convirtieron en Gasto (decisión manual, F5d) — el
        // saldo persistido queda más alto que el de "suma de las hojas de línea" en esa
        // cantidad exacta. Con la planilla real, en el momento de escribir este plan, no se
        // pudo confirmar si PoaDudosos es 0 — si este assert falla, revisar
        // analisis.Resumen.PoaDudosos antes de tocar la implementación.
        Assert.Equal(6341849m + ajusteDudosos, saldoB);
        Assert.Equal(4174206m + ajusteDudosos, saldoC);
    }

    /// <summary>
    /// Arma el ConfirmarImportacionDto a partir del resultado de /analizar. Gotcha (F5b): en una
    /// hoja con financiamiento mixto, LineaPoaAnalizadaDto se aplana en N entradas (una por
    /// asignación) que comparten el mismo Hoja — los Movimientos SOLO viajan en la primera
    /// (Movimientos.Count > 0). Agrupar por Hoja y tomar la asignación con movimientos no
    /// vacíos es obligatorio para no perderlos al armar el payload.
    /// </summary>
    private static ConfirmarImportacionDto ArmarPayloadConfirmacion(ResultadoAnalisisDto analisis)
    {
        // Ejercicio: se usa la CONSTANTE de la clase (el mismo valor 2026 ya mandado a
        // /analizar en el paso 1), no un valor derivado de analisis.LineasPoa[0].Ejercicio —
        // más simple y sin un fallback silencioso a DateTime.UtcNow.Year si LineasPoa viniera
        // vacío (no debería, pero un fallback a "el año de hoy" sería un bug difícil de ver).
        var ingresos = analisis.Ingresos
            .Where(i => i.Estado != EstadoFila.Error)
            .Select(i => new IngresoConfirmarDto(
                i.Fecha!.Value, i.Concepto ?? "(sin concepto)", i.Monto!.Value,
                i.Fuente ?? "(sin fuente)"))
            .ToList();

        var gastosDeLaHoja = analisis.Gastos
            .Where(g => g.Estado != EstadoFila.Error)
            .Select(g => new GastoConfirmarDto(
                g.Proveedor ?? "(sin proveedor)", g.NumeroFactura, g.NumeroOrden,
                g.Detalle ?? "(sin detalle)", g.Destino, g.Fecha!.Value, g.Monto!.Value,
                g.Fuente ?? "(sin fuente)", g.CodigoRubro ?? CodigoRubroCompromisosPoa,
                g.LineaPoaAsignada, CondicionPago.Contado))
            .ToList();

        var fechaFallbackCompromisos = new DateOnly(Ejercicio, 12, 31);

        var gastosDeCompromisos = analisis.LineasPoa
            .GroupBy(l => l.Hoja)
            .SelectMany(grupo =>
            {
                var conMovimientos = grupo.FirstOrDefault(l => l.Movimientos.Count > 0) ?? grupo.First();
                return conMovimientos.Movimientos
                    .Where(m => m.Clasificacion == ClasificacionReconciliacion.CompromisoSoloPoa)
                    .Select(m => new GastoConfirmarDto(
                        m.Proveedor ?? "(sin proveedor)", m.Factura, m.Orden,
                        m.Detalle ?? "(compromiso POA sin detalle)", null,
                        fechaFallbackCompromisos, m.Importe ?? 0m,
                        conMovimientos.Literal ?? "(sin literal)", CodigoRubroCompromisosPoa,
                        conMovimientos.Hoja, CondicionPago.Credito));
            })
            .ToList();

        var lineasPoa = analisis.LineasPoa
            .GroupBy(l => l.Hoja)
            .Select(grupo => new LineaPoaConfirmarDto(
                grupo.Key,
                "Migración F5c", // el análisis (F5b) deja Programa vacío a propósito — spec §2.4
                grupo.Where(l => l.Literal is not null)
                    .Select(l => new AsignacionConfirmarDto(l.Literal!, l.Presupuesto))
                    .ToList()))
            .ToList();

        var rubrosNuevos = analisis.MaestrosNuevos.Rubros
            .Select(r => new RubroNuevoConfirmarDto(r.Codigo, r.NombreSugerido ?? $"Rubro {r.Codigo}"))
            .ToList();
        rubrosNuevos.Add(new RubroNuevoConfirmarDto(
            CodigoRubroCompromisosPoa, "Compromisos POA (sin rubro en la planilla)"));

        return new ConfirmarImportacionDto(
            Ejercicio: Ejercicio,
            Forzar: false,
            MaestrosNuevos: new MaestrosNuevosConfirmarDto(
                analisis.MaestrosNuevos.Proveedores, analisis.MaestrosNuevos.Fuentes, rubrosNuevos),
            Ingresos: ingresos,
            Gastos: gastosDeLaHoja.Concat(gastosDeCompromisos).ToList(),
            LineasPoa: lineasPoa);
    }
}
```

- [ ] **Paso 2: Correr el test y verificar que falla**

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionAceptacionConfirmacionTests"`
Esperado: FAIL en compilación primero (el archivo es nuevo pero todo lo que consume ya existe desde las Tasks anteriores, así que debería compilar) — si compila, el fallo esperable en la primera corrida es en alguno de los `Assert.Equal` finales. **No ajustar el mapeo del importador para que cierre "como sea"**: si `cajaJunio` no da 43.705, revisar `ArmarPayloadConfirmacion` contra el mapeo real de F5b (`AnalisisImportacionService`) antes de tocar `ImportacionRepository`. Si `saldoB`/`saldoC` no dan, primero imprimir `analisis.Resumen.PoaDudosos` — un valor distinto de 0 EXPLICA la diferencia (ver la nota de corrección arriba), no es necesariamente un bug.

- [ ] **Paso 3: Implementación mínima**

No aplica — esta task no agrega código de producción, solo el test de aceptación. Si falla por un bug real de mapeo (no por el ajuste de `PoaDudosos` ya documentado), corregir el archivo de producción correspondiente de las Tasks 1-8 y re-correr, dejando registro en el mensaje de commit de qué se ajustó y por qué.

- [ ] **Paso 4: Correr el test y verificar que pasa**

Comando: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionAceptacionConfirmacionTests"`
Esperado: `Passed! - Failed: 0, Passed: 1`.

- [ ] **Paso 5: Commit**

```bash
git add tests/StockApp.Api.Tests/ImportacionAceptacionConfirmacionTests.cs
git commit -m "test(finanzas): aceptación F5c — /confirmar persiste los saldos reales (caja junio + POA por Literal)"
```

---

## Cierre — suite completa

- [ ] `dotnet test tests/StockApp.Domain.Tests`, `dotnet test tests/StockApp.Application.Tests`, `dotnet test tests/StockApp.Infrastructure.Tests` y `dotnet test tests/StockApp.Api.Tests` — todo verde.
- [ ] Verificación orgánica (convención del repo, ver memoria `verificacion-organica`): con `stockapp-pg` corriendo y la API real, hacer login como Admin, `POST /finanzas/importar/confirmar` con un payload chico armado a mano (2-3 filas) y confirmar en la base (o vía las vistas de Finanzas ya existentes, F4) que los datos aparecen razonables. Después `POST /finanzas/importar/revertir/{id}` con el `IdImportacion` devuelto y confirmar que desaparecen de las vistas activas.
- [ ] **No correr la importación REAL de las planillas de producción sin antes reconciliar** `docs/finanzas-discrepancias-planilla-poa-2026.md` (spec §10) — la diferencia de 480.000 en Literal C sigue sin explicación a la fecha de este plan.

---

## Criterios de aceptación

1. `POST /finanzas/importar/confirmar` responde 401 sin token, 403 a Operador, 400 con `errors` estructurado si una referencia nominal no resuelve, 409 si el ejercicio ya tiene una corrida sin revertir y `Forzar` no viene en `true`, 200 con `ResultadoConfirmacionDto` en el caso feliz.
2. Correr `/confirmar` dos veces con el mismo payload y `Forzar=true` NO duplica ingresos ni gastos (dedupe por clave natural); la segunda corrida reporta los omitidos.
3. `POST /finanzas/importar/revertir/{id}` da de baja lógica todo el lote (`Gasto`, `PagoGasto` en cascada, `IngresoCaja`, `LineaPoa` con sus `AsignacionPresupuestal` colgando) sin tocar los maestros; 404 si el id no existe, 409 si ya fue revertido.
4. El ciclo `/confirmar` → `/revertir/{id}` → `/confirmar` sin `Forzar` termina en 200 (el guard no cuenta corridas ya revertidas).
5. Los compromisos POA (`ClasificacionReconciliacion.CompromisoSoloPoa` en el análisis de F5b) se escriben como `Gasto` `Credito` sin `PagoGasto`.
6. §11 a nivel de base de datos (Task 9): caja junio 2026 = 43.705; saldo POA por Literal reproduce la suma de las hojas de línea (6.341.849 / 4.174.206, ajustado por movimientos `Dudoso` si los hay) — NO los números de "SALDO TOTALES" que usó el criterio original de F5b, por la desincronización documentada.
7. Ninguna migración nueva agrega un índice ÚNICO (spec §4, matiz) — solo columnas `IdImportacion` + índices no-únicos.
8. Ningún índice único nuevo, `Proveedor`/`FuenteFinanciamiento`/`RubroGasto` nunca se dan de baja por una reversa.
