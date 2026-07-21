# F5b — Análisis backend del importador de planillas .ods — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recomendado) o superpowers:executing-plans para implementar este plan task-by-task. Los pasos usan checkbox (`- [ ]`) para tracking.

**Goal:** Construir el paso de ANÁLISIS del importador one-shot (spec §8, primer bullet de "dos pasos: análisis → confirmación"): un endpoint `POST /finanzas/importar/analizar` que recibe las dos planillas .ods por multipart, las parsea con el `IPlanillaParser` de F5a, mapea cada fila a "la fila-candidata tal cual iría a la base" (modelo de dominio de Finanzas), le asigna un estado (OK / advertencia / error) con su motivo, y concilia las facturas del POA contra las del libro caja. Todo READ-ONLY respecto del dominio: puede LEER la base para reconciliar maestros existentes, pero NO escribe nada. La escritura transaccional, la idempotencia y la grilla de corrección son F5c.

**Architecture:** El análisis es orquestación + reglas de negocio puras, así que vive en `StockApp.Application.Finanzas` como `AnalisisImportacionService : IAnalisisImportacionService` (mismo patrón que `GastoService`/`IngresoCajaService`: `_auth.Verificar` al entrar, repos por constructor). Consume `IPlanillaParser` (interfaz ya en Application; impl `PlanillaOdsParser` en Infrastructure) para transformar los `Stream` en DTOs de F5a, y los repositorios de maestros (`IProveedorRepository`, `IRubroGastoRepository`, `IFuenteFinanciamientoRepository`) para decidir qué es "nuevo a crear". El endpoint `ImportacionEndpoints` vive en `StockApp.Api/Endpoints/`, recibe multipart, abre los streams y delega. F5b es la primera fase que registra `IPlanillaParser` → `PlanillaOdsParser` en el DI de `StockApp.Api`, y la primera que agrega un permiso Finanzas Admin-only (`ImportarPlanillas`).

**Tech Stack:** .NET 10. Reutiliza `IPlanillaParser`/DTOs de F5a (sin tocar Infrastructure salvo el registro DI). xUnit. Tests de análisis puros en `StockApp.Application.Tests` con fakes de repos + fake de `IPlanillaParser` (sin Postgres). Test de aceptación §11 en `StockApp.Api.Tests` (integración full-stack contra Postgres real vía `ApiTestBase`, con las planillas reales como fixtures).

## Global Constraints

- **F5b NO escribe en el dominio.** `/analizar` es idempotente por naturaleza (no muta): puede LEER maestros y gastos existentes para clasificar, nunca inserta/actualiza. No hay auditoría en `/analizar` (la auditoría de importación —spec §10, `AccionAuditada` nueva— es de F5c, cuando se confirma y escribe).
- **La reconciliación Gastos↔POA de F5b es cálculo/marcado en memoria**, dentro del mismo request: se cruzan las filas EGRESO parseadas de la planilla de Gastos contra los movimientos parseados de la planilla POA. NO se decide nada (eso es la grilla del desktop, F5c); solo se clasifica cada movimiento POA (Conciliado / Dudoso / CompromisoSoloPoa) y se anota en el resultado.
- **Fuera de alcance de F5b (es F5c):** endpoint `/finanzas/importar/confirmar`, escritura transaccional, idempotencia por factura+orden+fecha+monto, y toda la UI/grilla del desktop (Avalonia) + ApiClient. Ninguna task de este plan los toca.
- TDD estricto por capas: test → verlo fallar → implementar lo mínimo → verlo verde → commit.
- Commits frecuentes, conventional commits en español (`feat(finanzas): ...`, `test(finanzas): ...`), **sin `Co-Authored-By` ni atribución a IA**.
- No correr `dotnet build` intermedio — solo los `dotnet test` que cada task pide.

## Decisión registrada: el servicio de análisis vive en Application, no en Infrastructure

`AnalisisImportacionService` es lógica de negocio (mapeo dominio + reglas de estado + reconciliación), no acceso a datos. Va en `StockApp.Application.Finanzas` igual que `GastoService`. La ÚNICA pieza de Infrastructure involucrada es `PlanillaOdsParser` (F5a), que se inyecta detrás de `IPlanillaParser` (interfaz de Application). Así el servicio se testea con fakes puros, sin Postgres ni zip real.

## Decisión registrada: un solo endpoint recibe ambas planillas

`POST /finanzas/importar/analizar` recibe Gastos **y** POA en el mismo request multipart (dos `IFormFile`). La reconciliación Gastos↔POA necesita las dos planillas juntas; partirlas en dos endpoints obligaría a mantener estado entre requests (contra "sin sobre-ingeniería" para un one-shot).

## Decisión registrada: `/analizar` es async

`AnalisisImportacionService.AnalizarAsync` es `async`: LEE la base (repos async de maestros). El parseo interno (F5a) es síncrono y CPU-bound, pero se llama dentro del método async sin envolverlo en `Task.Run` (es rápido, decenas de KB). Coherente con el resto de servicios de Finanzas.

## Decisión registrada: contrato del resultado — anidado por tipo

El DTO de resultado se agrupa por tipo (`Ingresos`, `Gastos`, `LineasPoa`) más un bloque `MaestrosNuevos` con los conjuntos distintos a crear y un `Resumen` con conteos. Razón: la UI de F5c muestra pestañas por tipo (spec §8), y los maestros nuevos se crean una sola vez en el confirm, no fila por fila.

## Pre-flight (resoluciones adoptadas)

1. Un solo endpoint recibe Gastos + POA en el mismo multipart (la reconciliación necesita ambas juntas).
2. Híbrido: reconciliación Gastos↔POA en memoria; la base se lee SOLO para clasificar maestros existentes vs nuevos. Detección de duplicados contra gastos ya cargados = F5c (idempotencia), NO F5b.
3. Maestros nuevos con doble representación: flag por fila (para resaltar celda en F5c) + bloque top-level `MaestrosNuevos` con conjuntos distintos.
4. DTO de resultado anidado/agrupado por tipo, con movimientos POA anidados dentro de cada línea.
5. Servicio async, en Application.
6. Saldo inicial de enero: buscar la fila etiquetada "SALDO INICIAL/ANTERIOR"; si no existe, tomar el primer `Saldo` de ENERO antes del primer movimiento. Se confirma inspeccionando la hoja ENERO real al ejecutar Task 3.
7. Movimiento POA sin factura → `CompromisoSoloPoa` (no hay clave de match; es compromiso, no ambigüedad).
8. `LineaPoa` candidata: `Ejercicio` = parámetro del request; `Programa` = vacío + advertencia (no se inventa dato; se completa en la grilla de F5c).
9. ~~Línea POA con financiamiento mixto B+C: marca-y-sigue (advertencia "revisar financiamiento mixto"); el split real en dos `AsignacionPresupuestal` excede F5a/F5b. El oráculo fuerte de §11 sigue siendo el `SaldosTotales` del parser F5a.~~ **Actualizado al cierre de F5b:** el split SÍ se implementó. `LineaPoaResumenOds` pasó a tener una lista de `AsignacionPoaOds` (Literal, Presupuesto, Saldo) en vez de un escalar; el parser detecta financiamiento mixto guiándose por las celdas "LITERAL X" de la zona de encabezado (caso real: COMPOSTERAS Y COMPACTADORAS, que se lee como 2 asignaciones — C=1.407.252, B=92.748 — en vez del bloque agregado 1.500.000). El servicio aplana N asignaciones en N `LineaPoaAnalizadaDto` por hoja (movimientos solo en la primera). El oráculo fuerte de §11 sigue siendo `SaldosTotales` del parser F5a (ahora expuesto como `ResultadoAnalisisDto.SaldosPoa`) — sumar `SaldoPlanilla` por Literal a través de `LineasPoa` NO cuadra contra ese oráculo por inconsistencias reales de la planilla, ver `docs/finanzas-discrepancias-planilla-poa-2026.md`.
10. Archivo .ods inválido / hoja faltante: el servicio envuelve las llamadas al parser y re-lanza `InvalidOperationException` como `ArgumentException` → 400 vía el `DomainExceptionHandler` existente (sin tocar el handler).

---

## File Structure

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Application/Authorization/Permisos.cs` | Modify: agrega `ImportarPlanillas` a las constantes y a `Todos`. |
| `src/StockApp.Application/Authorization/AuthorizationService.cs` | Modify: `ImportarPlanillas` NO entra en `AccionesOperador` (solo Admin lo tiene). |
| `src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs` | Create: DTOs del resultado del análisis (estados, motivos, filas candidatas, reconciliación, resumen). |
| `src/StockApp.Application/Finanzas/IAnalisisImportacionService.cs` | Create: interfaz del servicio de análisis. |
| `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs` | Create: impl (Gastos → Ingresos/Gastos/saldo inicial; POA → líneas; reconciliación). |
| `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs` | Create: `POST /finanzas/importar/analizar` (multipart 2 archivos), `RequireAuthorization(Permisos.ImportarPlanillas)`. |
| `src/StockApp.Api/Program.cs` | Modify: registra `IPlanillaParser` → `PlanillaOdsParser`, `IAnalisisImportacionService` → `AnalisisImportacionService`, y `app.MapImportacionEndpoints()`. |
| `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs` | Modify: cuenta 11 → 12, incluye `ImportarPlanillas`. |
| `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs` | Modify: Operador NIEGA `ImportarPlanillas`; Admin lo tiene. |
| `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs` | Create: mapeo + estados de la planilla de Gastos (fake parser + fake repos). |
| `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs` | Create: mapeo + estados de la planilla POA. |
| `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceReconciliacionTests.cs` | Create: reconciliación Gastos↔POA (Conciliado/Dudoso/CompromisoSoloPoa). |
| `tests/StockApp.Application.Tests/Finanzas/Fakes/` | Create: fakes de `IPlanillaParser` y de los 3 repos de maestros. |
| `tests/StockApp.Api.Tests/ImportacionEndpointTests.cs` | Create: matriz 401/403/200 + validación de archivo inválido → 400. |
| `tests/StockApp.Api.Tests/ImportacionAceptacionTests.cs` | Create: §11 contra las planillas reales, a nivel del resultado del análisis. |
| `tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj` | Modify: `CopyToOutputDirectory` de los 2 `.ods` (gitignored, igual que F5a). |

---

## Contrato del resultado del análisis (DTOs)

```csharp
// src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs
namespace StockApp.Application.Finanzas;

/// <summary>Estado de una fila candidata del análisis (spec §8): OK importa directo;
/// Advertencia importa pero necesita atención (literal vacío, rubro/proveedor nuevo);
/// Error NO se puede importar hasta corregir (fecha/monto ilegible).</summary>
public enum EstadoFila { Ok, Advertencia, Error }

/// <summary>Tipo tipado del motivo, para que la UI de F5c resalte la celda correcta.</summary>
public enum TipoMotivo
{
    LiteralVacio,          // advertencia: fuente sin identificar
    FuenteDesconocida,     // advertencia: literal no matchea ninguna FuenteFinanciamiento
    RubroDesconocido,      // advertencia: código no matchea ningún RubroGasto
    ProveedorNuevo,        // advertencia: nombre no existe en Proveedores (se crearía)
    FechaIlegible,         // error: movimiento sin fecha parseable
    MontoIlegible,         // error: movimiento sin monto (ni ingreso ni egreso) parseable
    ReconciliacionDudosa,  // advertencia: match parcial POA↔Gastos, decisión manual
}

public sealed record MotivoEstado(TipoMotivo Tipo, string Mensaje);

/// <summary>Clasificación de un movimiento POA frente al libro caja (planilla de Gastos).</summary>
public enum ClasificacionReconciliacion
{
    Conciliado,        // matchea 1 gasto por factura+orden: NO se duplica, se le asigna la línea POA
    Dudoso,            // match parcial o múltiple: decisión manual en F5c
    CompromisoSoloPoa, // no matchea ningún gasto: compromiso → gasto pendiente a crear
}

/// <summary>Fila candidata de Ingreso de caja (saldo inicial + filas INGRESO de Gastos).</summary>
public sealed record IngresoAnalizadoDto(
    string HojaOrigen, int NumeroFila,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    DateOnly? Fecha, decimal? Monto,
    string? Concepto,
    string? Fuente, bool FuenteDesconocida);

/// <summary>Fila candidata de Gasto (filas EGRESO de la planilla de Gastos).</summary>
public sealed record GastoAnalizadoDto(
    string HojaOrigen, int NumeroFila,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    DateOnly? Fecha, decimal? Monto,
    string? Proveedor, bool ProveedorNuevo,
    string? NumeroFactura, string? NumeroOrden,
    string? Detalle, string? Destino,
    string? Fuente, bool FuenteDesconocida,
    int? CodigoRubro, string? Rubro, bool RubroDesconocido,
    // Reconciliación: si un movimiento POA matcheó este gasto, acá viaja la línea POA
    // que se le asignaría (en vez de duplicar el gasto). Null si no hay match.
    string? LineaPoaAsignada);

/// <summary>Movimiento (factura imputada) de una línea POA, ya clasificado.</summary>
public sealed record MovimientoPoaAnalizadoDto(
    int NumeroFila,
    string? Factura, string? Orden, string? Proveedor, string? Detalle, decimal? Importe,
    ClasificacionReconciliacion Clasificacion,
    // Índice (en la lista Gastos del resultado) del gasto conciliado, o null.
    int? IndiceGastoConciliado,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos);

/// <summary>Línea POA candidata (una hoja de la planilla POA) + su asignación presupuestal.</summary>
public sealed record LineaPoaAnalizadaDto(
    string Hoja, int Ejercicio,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    string? Literal, bool FuenteDesconocida,
    decimal Presupuesto, decimal SaldoPlanilla,
    IReadOnlyList<MovimientoPoaAnalizadoDto> Movimientos);

/// <summary>Conjuntos DISTINTOS de maestros que la importación crearía (spec §8: "se crean
/// los faltantes"). Se materializan una sola vez en el confirm de F5c, no fila por fila.</summary>
public sealed record MaestrosNuevosDto(
    IReadOnlyList<string> Proveedores,
    IReadOnlyList<string> Fuentes,
    IReadOnlyList<CodigoRubroNuevoDto> Rubros);

public sealed record CodigoRubroNuevoDto(int Codigo, string? NombreSugerido);

public sealed record ResumenAnalisisDto(
    int TotalFilas, int Ok, int Advertencias, int Errores,
    int PoaConciliados, int PoaDudosos, int PoaCompromisos);

/// <summary>Resultado completo del análisis (READ-ONLY): nada de esto está en la base todavía.</summary>
public sealed record ResultadoAnalisisDto(
    IReadOnlyList<IngresoAnalizadoDto> Ingresos,
    IReadOnlyList<GastoAnalizadoDto> Gastos,
    IReadOnlyList<LineaPoaAnalizadaDto> LineasPoa,
    MaestrosNuevosDto MaestrosNuevos,
    ResumenAnalisisDto Resumen);
```

```csharp
// src/StockApp.Application/Finanzas/IAnalisisImportacionService.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Paso de ANÁLISIS del importador (spec §8). Parsea las dos planillas, mapea cada fila a su
/// candidata de dominio con estado OK/advertencia/error, y concilia POA↔Gastos. READ-ONLY:
/// lee maestros para clasificar, NUNCA escribe. Exige el permiso ImportarPlanillas (solo Admin).
/// </summary>
public interface IAnalisisImportacionService
{
    Task<ResultadoAnalisisDto> AnalizarAsync(Stream planillaGastos, Stream planillaPoa, int ejercicio);
}
```

---

## Mapeo de dominio (detallado)

### Planilla de Gastos → Ingresos / Gastos

Por cada `FilaGastoOds` de cada hoja mensual (F5a `PlanillaGastosOds.FilasPorMes`):

- **Saldo inicial de enero** (spec §8) → un único `IngresoAnalizadoDto` con `Concepto = "Saldo inicial {ejercicio}"`, `Monto = <saldo de apertura>`, `Fuente = null` (o la que se decida). Detección: fila etiquetada "SALDO INICIAL/ANTERIOR"; fallback al primer `Saldo` de ENERO antes del primer movimiento.
- **Fila con `Ingreso != null`** → `IngresoAnalizadoDto`: `Fecha`, `Monto = Ingreso`, `Concepto` = primer texto no vacío entre `Proveedor`/`Destino`/`Gasto`, `Fuente = Literal`.
- **Fila con `Egreso != null`** → `GastoAnalizadoDto` (contado, con pago automático a la fecha en F5c): `Fecha`, `Monto = Egreso`, `Proveedor`, `NumeroFactura = Factura`, `NumeroOrden = Orden`, `Detalle = Gasto` (o `Destino` si `Gasto` vacío), `Destino`, `Fuente = Literal`, `CodigoRubro = Codigo`, `Rubro`.
- **Fila con `Ingreso` y `Egreso` ambos null** (solo arrastra saldo) → el parser F5a ya la descarta; no genera candidata.

**Estados (Gastos):**
- `Error` + `MontoIlegible`: fila-movimiento sin `Ingreso` ni `Egreso` parseable (defensivo).
- `Error` + `FechaIlegible`: `Fecha == null` en una fila que sí es movimiento.
- `Advertencia` + `LiteralVacio` (o `FuenteDesconocida`): `Literal` null/vacío, o `Literal` no matchea ninguna `FuenteFinanciamiento` activa de la base.
- `Advertencia` + `RubroDesconocido`: `Codigo` no matchea ningún `RubroGasto` de la base (solo EGRESO).
- `Advertencia` + `ProveedorNuevo`: `Proveedor` no vacío pero no existe en `Proveedores` (comparación normalizada, trim + case-insensitive).
- `Ok`: todo resuelve contra maestros y fecha+monto legibles.
- El estado final de la fila es el MÁS severo de sus motivos (Error > Advertencia > Ok); los motivos se acumulan todos.

### Planilla POA → LineasPoa

Por cada `LineaPoaResumenOds` (F5a):
- `LineaPoaAnalizadaDto`: `Hoja`, `Ejercicio` (parámetro), `Literal`, `Presupuesto`, `SaldoPlanilla = Saldo`. La `AsignacionPresupuestal` candidata es `{ Fuente = Literal, Monto = Presupuesto }`.
- `Advertencia` + `FuenteDesconocida`: `Literal` no matchea ninguna `FuenteFinanciamiento`.
- Movimientos → clasificados por reconciliación (abajo).

### Reglas de reconciliación Gastos↔POA (explícitas)

Se cruza cada `MovimientoPoaAnalizadoDto` contra la lista de `GastoAnalizadoDto` (filas EGRESO). Clave de match: `(NumeroFactura, NumeroOrden)` normalizados.

1. **Conciliado**: existe EXACTAMENTE un gasto con misma `Factura` **y** misma `Orden`, ambas no vacías. → `Clasificacion = Conciliado`, `IndiceGastoConciliado = <i>`. Ese gasto recibe `LineaPoaAsignada = <hoja POA>`. El movimiento POA NO genera un gasto nuevo (evita la doble carga).
2. **Dudoso**: matchea por `Factura` pero difiere/falta `Orden`, o hay >1 candidato con esa factura, o el movimiento tiene factura pero cero match exacto. → `Clasificacion = Dudoso`, `Estado = Advertencia`, motivo `ReconciliacionDudosa`.
3. **CompromisoSoloPoa**: el movimiento no tiene `Factura`, o su factura no matchea ninguna fila de Gastos. → `Clasificacion = CompromisoSoloPoa`. Estado `Ok`.

Nota: F5b NO recalcula sobregiro POA (eso es la vista `control-poa`, F4). Solo mapea presupuesto y saldo de la planilla.

---

## Task 1: Permiso `ImportarPlanillas` (solo Admin)

**Files:**
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/PermisosTests.cs`
- Test: `tests/StockApp.Application.Tests/Authorization/AuthorizationServiceTests.cs`

### Steps

- [ ] Actualizar `PermisosTests`: subir el conteo esperado y agregar `Permisos.ImportarPlanillas`. Correr y ver fallar.
- [ ] Agregar la constante `public const string ImportarPlanillas = "finanzas.importar";` y sumarla a `Todos` (append-only).
- [ ] Agregar a `AuthorizationServiceTests`: `TienePermiso(Admin, ImportarPlanillas) == true`; `TienePermiso(Operador, ImportarPlanillas) == false`; `Verificar(Operador, ImportarPlanillas)` lanza `UnauthorizedAccessException`. Correr y ver fallar.
- [ ] En `AuthorizationService`: NO agregar `ImportarPlanillas` a `AccionesOperador`. Comentar por qué queda afuera a propósito.
- [ ] Correr y ver verde.
- [ ] Commit: `feat(finanzas): permiso ImportarPlanillas otorgado solo a Admin`

---

## Task 2: DTOs del resultado + `IAnalisisImportacionService` (contratos)

**Files:**
- Create: `src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs` (el bloque completo de "Contrato del resultado del análisis").
- Create: `src/StockApp.Application/Finanzas/IAnalisisImportacionService.cs`.

### Steps

- [ ] Crear ambos archivos tal cual el contrato de arriba.
- [ ] Correr la suite de Application para confirmar que compila.
- [ ] Commit: `feat(finanzas): contrato del resultado del análisis de importación (DTOs + interfaz)`

---

## Task 3: `AnalisisImportacionService` — mapeo de la planilla de Gastos con estados

**Files:**
- Create: `tests/StockApp.Application.Tests/Finanzas/Fakes/PlanillaParserFake.cs`.
- Create: `tests/StockApp.Application.Tests/Finanzas/Fakes/RepositorioMaestrosFake.cs`.
- Create: `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs` (Gastos completo; POA/reconciliación en stubs que Task 4/5 completan).
- Test: `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs`.

### Steps

- [ ] Escribir los fakes de repos (retornan listas fijas; métodos de escritura lanzan `NotSupportedException`). Fake de `IPlanillaParser` devuelve DTOs pasados por constructor.
- [ ] Escribir los tests que fallan: EstadoOk con maestros existentes; fila INGRESO → IngresoAnalizado; LiteralVacio → advertencia; CodigoRubro inexistente → RubroDesconocido + aparece en MaestrosNuevos.Rubros; Proveedor inexistente → ProveedorNuevo + distinct en MaestrosNuevos.Proveedores; Fecha nula → Error FechaIlegible; sesión Operador → Unauthorized.
- [ ] Correr y ver fallar.
- [ ] Implementar `AnalisisImportacionService`: (1) `_auth.Verificar`, (2) cargar maestros normalizados, (3) parsear Gastos (envuelto, ver resolución pre-flight 10), (4) mapear cada fila, (5) detectar saldo inicial (confirmar layout ENERO real), (6) POA/reconciliación stub, armar Resumen.
- [ ] Correr y ver verde.
- [ ] Commit: `feat(finanzas): análisis de la planilla de Gastos (mapeo dominio + estados)`

---

## Task 4: `AnalisisImportacionService` — mapeo de la planilla POA con estados

**Files:**
- Modify: `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs`.
- Test: `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs`.

### Steps

- [ ] Tests que fallan: línea POA con literal existente → mapea presupuesto/saldo (Ok); literal desconocido → FuenteDesconocida + en MaestrosNuevos.Fuentes; movimiento sin factura → CompromisoSoloPoa.
- [ ] Correr y ver fallar.
- [ ] Completar el mapeo POA (líneas + asignación candidata + movimientos con clasificación provisional; reconciliación real es Task 5).
- [ ] Correr y ver verde.
- [ ] Commit: `feat(finanzas): análisis de la planilla POA (líneas, asignaciones, estados)`

---

## Task 5: `AnalisisImportacionService` — reconciliación Gastos↔POA

**Files:**
- Modify: `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs`.
- Test: `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceReconciliacionTests.cs`.

### Steps

- [ ] Tests que fallan: factura+orden matchean un gasto → Conciliado + LineaPoaAsignada en el gasto; factura matchea pero orden difiere → Dudoso; múltiple gasto misma factura → Dudoso; sin factura → CompromisoSoloPoa; factura inexistente en Gastos → CompromisoSoloPoa; Resumen cuenta conciliados/dudosos/compromisos.
- [ ] Correr y ver fallar.
- [ ] Implementar: indexar Gastos por `(Factura,Orden)` y por `Factura`; aplicar reglas 1-3; mutar `LineaPoaAsignada`; recomputar `Resumen`.
- [ ] Correr y ver verde.
- [ ] Commit: `feat(finanzas): reconciliación Gastos↔POA en el análisis de importación`

---

## Task 6: Endpoint `POST /finanzas/importar/analizar` + DI

**Files:**
- Create: `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs`.
- Modify: `src/StockApp.Api/Program.cs`.
- Test: `tests/StockApp.Api.Tests/ImportacionEndpointTests.cs`.

### Steps

- [ ] Tests que fallan (patrón `AdjuntosEndpointTests`, `ApiTestBase`, multipart 2 archivos): sin token → 401; Operador → 403; Admin con 2 .ods → 200 + resultado; archivo no-ods → 400.
- [ ] Correr y ver fallar.
- [ ] Implementar `ImportacionEndpoints` (MapPost, OpenReadStream de ambos, delega en `IAnalisisImportacionService`, `.DisableAntiforgery()`, `.RequireAuthorization(Permisos.ImportarPlanillas)`).
- [ ] En `Program.cs`: `AddScoped<IPlanillaParser, PlanillaOdsParser>()`, `AddScoped<IAnalisisImportacionService, AnalisisImportacionService>()`, `app.MapImportacionEndpoints()`.
- [ ] Correr y ver verde.
- [ ] Commit: `feat(finanzas): endpoint POST /finanzas/importar/analizar + DI del parser y el análisis`

---

## Task 7: Test de aceptación §11 — a nivel del resultado del análisis

**Files:**
- Modify: `tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj` (`CopyToOutputDirectory` de los 2 `.ods`, gitignored).
- Test: `tests/StockApp.Api.Tests/ImportacionAceptacionTests.cs`.

### Steps

- [ ] Copiar los 2 fixtures reales al output de `StockApp.Api.Tests` (mismo par gitignored que F5a).
- [ ] Test (Admin, POST ambos .ods, `ejercicio=2026`): Caja junio = 43.705 = (saldo inicial) + Σ(Ingresos ENERO..JUNIO) − Σ(Gastos ENERO..JUNIO); POA Literal B = 6.643.349 y C = 4.654.206 agrupando `LineasPoa` por `Literal` sumando `SaldoPlanilla`.
- [ ] Correr. Si falla, NO ajustar los esperados: ajustar el mapeo.
- [ ] Commit: `test(finanzas): aceptación F5b — el análisis reproduce los saldos §11 contra las planillas reales`

---

## Cierre — suite completa

- [ ] `dotnet test tests/StockApp.Application.Tests` y `dotnet test tests/StockApp.Api.Tests` — todo verde.
- [ ] Los `.ods` reales están gitignored; NO se commitean. MSBuild los copia al output.
- [ ] Verificación orgánica (spec §11): con `stockapp-pg` y la API real, POST las dos planillas a `/analizar` como Admin y confirmar que el resumen y los 3 saldos cierran. Pendiente para el cierre de F5b, antes de F5c.

---

## Criterios de aceptación

1. `POST /finanzas/importar/analizar` responde 401 sin token, 403 a Operador, 200 a Admin.
2. El resultado clasifica cada fila con estado OK/advertencia/error y motivo tipado, y expone `MaestrosNuevos` distintos.
3. La reconciliación clasifica cada movimiento POA como Conciliado / Dudoso / CompromisoSoloPoa y anota la línea POA en el gasto conciliado sin duplicarlo.
4. §11 a nivel del análisis (READ-ONLY): caja junio = 43.705; POA Literal B = 6.643.349; POA Literal C = 4.654.206.
5. `IPlanillaParser` queda registrado en el DI de `StockApp.Api`; el permiso `ImportarPlanillas` existe, es solo-Admin y protege el endpoint.
6. Nada de F5b escribe en el dominio.
