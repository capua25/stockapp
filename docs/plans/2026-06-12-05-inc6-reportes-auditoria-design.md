# Incremento 6: Reportes + Auditoría — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) o superpowers:executing-plans para implementar este plan tarea por tarea. Los pasos usan checkbox (`- [ ]`) para tracking.

> **Estado del plan:** Propuesta + Diseño aprobados por el usuario. Sección **## Tareas** a completar en la fase `sdd-tasks`.

**Goal:** Cinco reportes de solo lectura para Admin, todos exportables a CSV, con generación del CSV
en la capa Application sin dependencias de UI. Los reportes cubren valorización de stock, agrupación
por categoría, historial de movimientos por producto (reutilizando Inc 5), productos más movidos y
visor del log de auditoría.

**Tech Stack:** .NET 10, C#, Clean Architecture, Avalonia MVVM (CommunityToolkit.Mvvm 8.4),
EF Core 10 + SQLite, xUnit + Moq. TDD estricto.

---

## Propuesta

### Contexto y motivación

El Inc 5 (Movimientos de Stock) dejó las siguientes bases sobre las que se construye este incremento:

- `Producto.StockActual`, `Producto.PrecioCosto`, `Producto.PrecioVenta` — persistidos y actualizados
  por los movimientos.
- `LogAuditoria` — poblado con todas las acciones de sistema; disponible para consulta.
- `Permisos.VerReportes` (`"reportes.ver"`) — ya definido y **denegado a Operador** (solo Admin).
- `ObtenerHistorialAsync` en `MovimientoStockService` — ya filtra por producto + rango de fechas,
  con el fix de `FechaHasta` como fin de día (23:59:59.999).

El Inc 6 construye los cinco reportes de negocio sobre esta infraestructura existente. No introduce
movimientos nuevos ni modifica entidades de dominio: es un incremento de **solo lectura** puro.

### Objetivos medibles

1. Cinco reportes accesibles solo a Admin (verificación `Permisos.VerReportes` en capa Application).
2. Exportación CSV de cualquier reporte, con escaping RFC 4180 y BOM UTF-8 (compatibilidad Excel).
3. El CSV se genera en Application; el guardado a disco lo hace Presentation con el file picker de
   Avalonia (`IStorageProvider`).
4. `dotnet build StockApp.sln` y `dotnet test` pasan con 0 errores y sin regresiones del Inc 5.

### Alcance

#### Entra (In Scope)

- **Application:** `IReporteStockService` + `ReporteStockService` (4 reportes de stock);
  `IAuditoriaQueryService` + `AuditoriaQueryService` (visor de log); `ICsvExporter` + `CsvExporter`
  (exportador genérico, usado por los 5 reportes); DTOs como records en sus respectivos namespaces.
- **Infrastructure:** `ReporteStockRepository` con consultas GroupBy/Sum/Count en EF; consultas de
  auditoría filtradas por usuario + rango de fechas.
- **Presentation:** 5 ViewModels + 5 Views con `Avalonia.Controls.DataGrid`; navegación vía
  `INavigationService`; entradas en `ShellMainViewModel` visibles solo para Admin.
- Tests: unit (cálculos, agrupaciones, escaping CSV, autorización fail-closed) + integración (queries
  GroupBy/Sum/Count sobre SQLite in-memory).

#### Fuera (Out of Scope)

- Modificación de movimientos, entidades de dominio o lógica de auditoría existente.
- Exportación a formatos distintos de CSV (PDF, Excel nativo, etc.).
- Roles distintos de Admin para consultar reportes.
- Reportes de stock bajo / alertas automáticas — no planificados en este incremento.

### Decisiones firmes (tomadas por el usuario — no se re-discuten)

**D1 — Productos sin CategoriaId → grupo "Sin categoría".**
En el reporte de stock por categoría, los productos con `CategoriaId null` se agrupan bajo el
literal `"Sin categoría"`. No se excluyen ni se tratan como error.

**D2 — Historial por producto reutiliza el servicio del Inc 5.**
El reporte de historial de movimientos por producto **no reimplementa** la lógica: llama a
`MovimientoStockService.ObtenerHistorialAsync` existente (que ya tiene el fix de `FechaHasta`
como fin de día). Solo se le agrega la capacidad de exportar el resultado a CSV.

**D3 — Productos más movidos: ordenado por volumen total (Σ Cantidad), Top 20 default.**
El ranking agrupa por producto en el rango de fechas dado, calcula `CantidadMovimientos (COUNT)` y
`VolumenTotal (Σ Cantidad)`, y ordena descendente por `VolumenTotal`. El `TopN` es configurable,
con default 20.

**D4 — Consultar reportes NO genera auditoría.**
Los reportes son operaciones de lectura pura: ningún método de reporte llama a `IAuditLogger`.

---

## Especificación

### Los cinco reportes

#### R1 — Valorización de stock

Fila por cada producto **activo**. DTOs como record.

| Campo | Fuente |
|-------|--------|
| `ProductoId` | `Producto.Id` |
| `Codigo` | `Producto.Codigo` |
| `Nombre` | `Producto.Nombre` |
| `Categoria` | `Categoria.Nombre` (null → `"Sin categoría"`) |
| `StockActual` | `Producto.StockActual` |
| `PrecioCosto` | `Producto.PrecioCosto` |
| `PrecioVenta` | `Producto.PrecioVenta` |
| `ValorCosto` | `StockActual × PrecioCosto` |
| `ValorVenta` | `StockActual × PrecioVenta` |

Resultado: lista de `ValorizacionItemDto` + `ValorizacionTotalesDto` (suma de `ValorCosto` y
`ValorVenta`).

Autorización: `Permisos.VerReportes`. Operador → `UnauthorizedAccessException`.

Escenarios clave:
- Sin productos activos → lista vacía + totales en cero.
- Producto sin categoría → campo `Categoria = "Sin categoría"`.
- Operador → `UnauthorizedAccessException` antes de ejecutar la query.

---

#### R2 — Stock por categoría

`GROUP BY CategoriaId` sobre productos activos.

| Campo | Fuente |
|-------|--------|
| `Categoria` | `Categoria.Nombre` (null → `"Sin categoría"`) |
| `CantidadProductos` | `COUNT(ProductoId)` |
| `StockTotal` | `Σ StockActual` |
| `ValorCosto` | `Σ (StockActual × PrecioCosto)` |
| `ValorVenta` | `Σ (StockActual × PrecioVenta)` |

Resultado: lista de `StockCategoriaDto`.

Autorización: `Permisos.VerReportes`.

Escenarios clave:
- Categorías sin productos activos no aparecen en el resultado.
- Productos con `CategoriaId null` → grupo `"Sin categoría"`.

---

#### R3 — Historial de movimientos por producto

**Reutiliza** `MovimientoStockService.ObtenerHistorialAsync(productoId, fechaDesde, fechaHasta)`.
No duplica lógica. El reporte solo agrega la capa de exportación CSV sobre el resultado existente
(`IReadOnlyList<MovimientoHistorialDto>`).

Parámetros: `ProductoId` (requerido), `FechaDesde` (opcional), `FechaHasta` (opcional, aplicado
como fin de día internamente por el servicio del Inc 5).

Autorización: `Permisos.VerReportes` en el servicio de reporte (adicional al guard del Inc 5).

Escenarios clave:
- Sin movimientos en el rango → lista vacía.
- `FechaHasta` se aplica como fin de día (23:59:59.999) — comportamiento heredado del Inc 5.

---

#### R4 — Productos más movidos

`GROUP BY ProductoId` en `MovimientosStock` para el rango de fechas dado.

| Campo | Fuente |
|-------|--------|
| `ProductoId` | `MovimientoStock.ProductoId` |
| `Codigo` | `Producto.Codigo` |
| `Nombre` | `Producto.Nombre` |
| `CantidadMovimientos` | `COUNT(MovimientoId)` |
| `VolumenTotal` | `Σ Cantidad` |

Ordenado por `VolumenTotal` descendente. `TopN` configurable, default 20.

Parámetros: `FechaDesde` (opcional), `FechaHasta` (opcional), `TopN` (default 20).

Autorización: `Permisos.VerReportes`.

Escenarios clave:
- Sin movimientos en el rango → lista vacía.
- `TopN = 5` → retorna máximo 5 resultados.
- El mismo producto con múltiples tipos de movimiento se agrega en una sola fila.

---

#### R5 — Visor del log de auditoría

Lectura de `LogAuditoria` filtrada por `UsuarioId` (opcional) + rango de fechas (mismo fix
`FechaHasta` como fin de día).

| Campo | Fuente |
|-------|--------|
| `Fecha` | `LogAuditoria.Fecha` |
| `NombreUsuario` | `Usuario.NombreUsuario` (join) |
| `Accion` | `LogAuditoria.Accion` (`AccionAuditada`) |
| `Entidad` | `LogAuditoria.Entidad` |
| `EntidadId` | `LogAuditoria.EntidadId` |
| `Detalle` | `LogAuditoria.Detalle` |

Resultado: lista de `AuditoriaItemDto`, ordenada por `Fecha` descendente.

Autorización: `Permisos.VerReportes`.

Escenarios clave:
- Sin filtro de usuario → todos los registros en el rango.
- Sin registros en el rango → lista vacía.
- `FechaHasta` aplicada como fin de día.

---

### Exportación CSV

`ICsvExporter` genera el contenido CSV como `string` (con BOM UTF-8 incluido) en la capa
Application, sin dependencias de UI.

**Requisitos RFC 4180:**
- Campos que contienen coma, comilla doble o salto de línea van entre comillas dobles.
- Las comillas dobles internas se duplican (`"` → `""`).
- Separador: coma (`,`). Fin de línea: CRLF (`\r\n`).

**BOM UTF-8:** el string resultante incluye BOM (`﻿` al inicio) para compatibilidad con Excel
al abrir directamente archivos con acentos.

**Responsabilidades por capa:**
- Application (`CsvExporter`): genera el string CSV.
- Presentation: usa `IStorageProvider` de Avalonia para mostrar el file picker y escribir el archivo
  a disco.

Escenarios clave de escaping (casos borde):
- Campo con coma → `"valor,con,comas"`
- Campo con comilla doble → `"valor ""con"" comillas"`
- Campo con salto de línea → `"valor\ncon\nsaltos"`
- Campo con acentos → BOM garantiza que Excel lo abra sin corrupción.
- Campo vacío → celda vacía (sin comillas).

---

## Diseño técnico

### Estructura de carpetas Application

```
Application/
  Reportes/
    IReporteStockService.cs
    ReporteStockService.cs
    Dtos.cs          ← ValorizacionItemDto, ValorizacionTotalesDto,
                        StockCategoriaDto, MasMovidoDto
  Auditoria/
    IAuditoriaQueryService.cs
    AuditoriaQueryService.cs
    Dtos.cs          ← AuditoriaItemDto
  Exportacion/
    ICsvExporter.cs
    CsvExporter.cs
```

### Contratos de interfaz

```csharp
// IReporteStockService
public interface IReporteStockService
{
    Task<(IReadOnlyList<ValorizacionItemDto> Items, ValorizacionTotalesDto Totales)>
        ObtenerValorizacionAsync();

    Task<IReadOnlyList<StockCategoriaDto>>
        ObtenerStockPorCategoriaAsync();

    Task<IReadOnlyList<MovimientoHistorialDto>>
        ObtenerHistorialPorProductoAsync(int productoId, DateTime? fechaDesde, DateTime? fechaHasta);

    Task<IReadOnlyList<MasMovidoDto>>
        ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN = 20);
}

// IAuditoriaQueryService
public interface IAuditoriaQueryService
{
    Task<IReadOnlyList<AuditoriaItemDto>>
        ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta);
}

// ICsvExporter
public interface ICsvExporter
{
    string Exportar<T>(IEnumerable<T> items);
}
```

Cada método de servicio verifica **primero**: `_auth.Verificar(_session.RolActual, Permisos.VerReportes)`.
Fail-closed: si el rol no tiene el permiso, lanza `UnauthorizedAccessException` antes de ejecutar
la query.

### DTOs (records)

```csharp
// Application/Reportes/Dtos.cs
public record ValorizacionItemDto(
    int ProductoId, string Codigo, string Nombre, string Categoria,
    decimal StockActual, decimal PrecioCosto, decimal PrecioVenta,
    decimal ValorCosto, decimal ValorVenta);

public record ValorizacionTotalesDto(decimal TotalValorCosto, decimal TotalValorVenta);

public record StockCategoriaDto(
    string Categoria, int CantidadProductos,
    decimal StockTotal, decimal ValorCosto, decimal ValorVenta);

public record MasMovidoDto(
    int ProductoId, string Codigo, string Nombre,
    int CantidadMovimientos, decimal VolumenTotal);

// Application/Auditoria/Dtos.cs
public record AuditoriaItemDto(
    DateTime Fecha, string NombreUsuario, AccionAuditada Accion,
    string Entidad, int EntidadId, string Detalle);
```

### Infrastructure: ReporteStockRepository

```
Infrastructure/Repositories/ReporteStockRepository.cs
```

Consultas EF Core con LINQ:

```csharp
// Valorización: productos activos con LEFT JOIN a Categorias
_ctx.Productos
    .Where(p => p.Activo)
    .Include(p => p.Categoria)
    .Select(p => new ValorizacionItemDto(
        p.Id, p.Codigo, p.Nombre,
        p.Categoria != null ? p.Categoria.Nombre : "Sin categoría",
        p.StockActual, p.PrecioCosto, p.PrecioVenta,
        p.StockActual * p.PrecioCosto,
        p.StockActual * p.PrecioVenta))
    .OrderBy(p => p.Nombre)
    .ToListAsync()

// Stock por categoría: GROUP BY con null coalescido
_ctx.Productos
    .Where(p => p.Activo)
    .GroupBy(p => p.Categoria != null ? p.Categoria.Nombre : "Sin categoría")
    .Select(g => new StockCategoriaDto(
        g.Key,
        g.Count(),
        g.Sum(p => p.StockActual),
        g.Sum(p => p.StockActual * p.PrecioCosto),
        g.Sum(p => p.StockActual * p.PrecioVenta)))
    .ToListAsync()

// Más movidos: GROUP BY ProductoId en MovimientosStock
_ctx.MovimientosStock
    .Where(m => (fechaDesde == null || m.Fecha >= fechaDesde)
             && (fechaHasta == null || m.Fecha <= fechaHasta.Value.Date.AddDays(1).AddTicks(-1)))
    .GroupBy(m => m.ProductoId)
    .Select(g => new MasMovidoDto(
        g.Key,
        g.First().Producto.Codigo,
        g.First().Producto.Nombre,
        g.Count(),
        g.Sum(m => m.Cantidad)))
    .OrderByDescending(x => x.VolumenTotal)
    .Take(topN)
    .ToListAsync()

// Auditoría: LogAuditoria con JOIN a Usuarios
_ctx.LogsAuditoria
    .Where(l => (usuarioId == null || l.UsuarioId == usuarioId)
             && (fechaDesde == null || l.Fecha >= fechaDesde)
             && (fechaHasta == null || l.Fecha <= fechaHasta.Value.Date.AddDays(1).AddTicks(-1)))
    .Include(l => l.Usuario)
    .OrderByDescending(l => l.Fecha)
    .Select(l => new AuditoriaItemDto(
        l.Fecha, l.Usuario.NombreUsuario, l.Accion,
        l.Entidad, l.EntidadId, l.Detalle))
    .ToListAsync()
```

### CsvExporter

```csharp
// Application/Exportacion/CsvExporter.cs
public class CsvExporter : ICsvExporter
{
    public string Exportar<T>(IEnumerable<T> items)
    {
        var sb = new StringBuilder();
        sb.Append('﻿'); // BOM UTF-8
        var props = typeof(T).GetProperties();
        // Header
        sb.AppendLine(string.Join(",", props.Select(p => Escapar(p.Name))));
        // Filas
        foreach (var item in items)
            sb.AppendLine(string.Join(",", props.Select(p => Escapar(p.GetValue(item)?.ToString() ?? ""))));
        return sb.ToString();
    }

    private static string Escapar(string valor)
    {
        if (valor.Contains(',') || valor.Contains('"') || valor.Contains('\n') || valor.Contains('\r'))
            return $"\"{valor.Replace("\"", "\"\"")}\"";
        return valor;
    }
}
```

### Presentation

**5 ViewModels** — patrón calcado de `MovimientoHistorialViewModel`:
- `ValorizacionViewModel`
- `StockCategoriaViewModel`
- `HistorialPorProductoViewModel`
- `MasMovidosViewModel`
- `AuditoriaLogViewModel`

Cada uno tiene:
- `[ObservableProperty]` para los filtros (fechas, productoId, topN, usuarioId).
- `[RelayCommand] BuscarAsync()` — llama al servicio correspondiente y popula la colección.
- `[RelayCommand] ExportarAsync()` — llama a `ICsvExporter.Exportar(Items)` y usa `IStorageProvider`
  de Avalonia para el file picker + escritura a disco.

**5 Views** — `Avalonia.Controls.DataGrid` (paquete `Avalonia.Controls.DataGrid` a referenciar en
el proyecto Presentation):
- `ValorizacionView.axaml`
- `StockCategoriaView.axaml`
- `HistorialPorProductoView.axaml`
- `MasMovidosView.axaml`
- `AuditoriaLogView.axaml`

Montos numéricos alineados a la derecha en el DataGrid.

**Navegación:** 5 entradas nuevas en `ShellMainViewModel` bajo el grupo "Reportes", visibles solo
para Admin (misma lógica de `EsAdmin` ya existente).

### Gotchas y notas de implementación

**Avalonia.Controls.DataGrid:** es un paquete NuGet separado (`Avalonia.Controls.DataGrid`), no
incluido en el meta-paquete `Avalonia` base. Debe referenciarse explícitamente en el proyecto
Presentation y registrarse en `App.axaml` con `<DataGrid.Styles>`.

**BOM UTF-8:** imprescindible para que Excel no corrompa acentos al abrir el CSV directamente.
El `﻿` debe estar al inicio del string, antes de cualquier contenido.

**R3 — Historial por producto:** al implementar, verificar la firma actual de
`MovimientoStockService.ObtenerHistorialAsync` y el DTO de retorno `MovimientoHistorialDto` para
asegurar compatibilidad. No reimplementar la lógica.

**FechaHasta como fin de día:** el fix ya está en `ObtenerHistorialAsync` del Inc 5. Para R4 y R5
(queries directas en Infrastructure), aplicar el mismo patrón:
`fechaHasta.Value.Date.AddDays(1).AddTicks(-1)`.

---

## Testing

### Unit tests

| Módulo | Tests |
|--------|-------|
| `ReporteStockService` | Valorización: cálculo correcto de ValorCosto/ValorVenta; producto sin categoría → "Sin categoría"; totales correctos; Operador → `UnauthorizedAccessException`. |
| `ReporteStockService` | StockPorCategoria: agrupación correcta; null-categoría en grupo "Sin categoría". |
| `ReporteStockService` | MasMovidos: orden por VolumenTotal desc; TopN respetado; sin movimientos → lista vacía. |
| `AuditoriaQueryService` | Filtro por usuario; filtro por fechas; FechaHasta como fin de día; Operador → `UnauthorizedAccessException`. |
| `CsvExporter` | Campo sin caracteres especiales; campo con coma; campo con comilla doble; campo con salto de línea; campo vacío; BOM presente al inicio; acentos en campos no corrompen el string. |

### Integration tests

| Módulo | Tests |
|--------|-------|
| `ReporteStockRepository` | `ObtenerValorizacionAsync`: productos activos con y sin categoría; totales correctos. |
| `ReporteStockRepository` | `ObtenerStockPorCategoriaAsync`: GroupBy sobre SQLite in-memory; grupo "Sin categoría" presente. |
| `ReporteStockRepository` | `ObtenerMasMovidosAsync`: GroupBy + Sum + COUNT; TopN; rango de fechas; FechaHasta fin de día. |
| `ReporteStockRepository` | Auditoría: filtro por usuarioId; filtro por fechas; FechaHasta fin de día; orden descendente. |

---

## Riesgos / Mitigaciones

| Riesgo | Probabilidad | Mitigación |
|--------|--------------|------------|
| `Avalonia.Controls.DataGrid` no incluido en Avalonia base | Alta (es paquete separado) | Agregar NuGet `Avalonia.Controls.DataGrid` al proyecto Presentation y registrar styles en `App.axaml` como primer paso. |
| BOM UTF-8 ausente → acentos corruptos en Excel | Alta si se olvida | Test explícito: verificar `resultado[0] == '﻿'`. |
| R3 firma de `ObtenerHistorialAsync` cambia en el futuro | Baja | Verificar firma al implementar; no duplicar lógica. |
| GroupBy sobre SQLite con null coalescido no traduce correctamente a SQL | Media | Test de integración con SQLite in-memory confirma traducción EF→SQL antes de merge. |
| FechaHasta fin-de-día no aplicado en R4/R5 (queries directas) | Media | Patrón explicitado en diseño; test de integración lo valida. |

---

## Criterios de aceptación de alto nivel

- [ ] `dotnet build StockApp.sln` y `dotnet test` pasan con 0 errores y sin regresiones del Inc 5.
- [ ] Los 5 reportes solo son accesibles para Admin; un Operador recibe `UnauthorizedAccessException`.
- [ ] R1 (Valorización): cálculo ValorCosto/ValorVenta correcto; totales correctos; productos sin
      categoría aparecen como "Sin categoría".
- [ ] R2 (Por categoría): agrupación correcta; grupo "Sin categoría" presente cuando corresponde.
- [ ] R3 (Historial por producto): reutiliza `ObtenerHistorialAsync` del Inc 5 sin duplicar lógica.
- [ ] R4 (Más movidos): ordenado por VolumenTotal desc; TopN respetado; FechaHasta como fin de día.
- [ ] R5 (Log auditoría): filtro por usuario + fechas; FechaHasta como fin de día; orden descendente.
- [ ] CSV: escaping RFC 4180 correcto (coma, comilla doble, salto de línea); BOM UTF-8 presente.
- [ ] Exportar desde Presentation abre el file picker de Avalonia y escribe el archivo a disco.
- [ ] `Avalonia.Controls.DataGrid` referenciado y registrado; DataGrids muestran datos correctamente.

---

## Tareas

> A completar en la fase `sdd-tasks`. TDD estricto: test rojo → impl mínima → verde → commit.
> Un commit por task, conventional commits.

> TDD estricto: test rojo → impl mínima → verde → commit. Un commit por task, conventional commits.
> Total: 19 tasks, ~75 tests nuevos. Orden: A1→A2→B1→B2→B3→B4→B5→C1→C2→C3→C4→C5→D1→D2→D3→D4→D5→D6→E1→E2

### Bloque A — Application: Exportación CSV (transversal, va primero)

#### A1 — Application: ICsvExporter + CsvExporter

**Archivos:** `Application/Exportacion/ICsvExporter.cs`, `Application/Exportacion/CsvExporter.cs`, `Application.Tests/Exportacion/CsvExporterTests.cs` — **Dep:** ninguna

- [ ] A1.1 Escribir 9 tests que fallan: `Exportar_CampoSimple_SinComillas`, `Exportar_CampoConComa_EntreComillas`, `Exportar_CampoConComillaDoble_Duplicada`, `Exportar_CampoConSaltoDeLinea_EntreComillas`, `Exportar_CampoVacio_SinComillas`, `Exportar_BomUtf8_AlInicio` (verifica `resultado[0] == '﻿'`), `Exportar_Acentos_NoCorrompen`, `Exportar_FinDeLinea_EsCRLF` (verifica que las filas terminan en `\r\n`, NO en solo `\n`), `Exportar_OrdenColumnas_Deterministico` (fija el header exacto esperado; falla si el orden varía)
- [ ] A1.2 Definir `ICsvExporter` con `string Exportar<T>(IEnumerable<T> items, IReadOnlyList<string> columnOrder)`; el parámetro `columnOrder` recibe la lista de nombres de propiedades en el orden deseado, resolviendo el no-determinismo de `Type.GetProperties()`
- [ ] A1.3 Implementar `CsvExporter`: BOM `﻿` al inicio; CRLF explícito (`\r\n`) por fila — NO usar `sb.AppendLine` (en Linux emite solo `\n`); escapado RFC 4180 (campo con `,`, `"` o salto de línea va entre comillas dobles; `"` interno se duplica)
- [ ] A1.4 `dotnet test Application.Tests/Exportacion` verde → commit: `feat(app): ICsvExporter + CsvExporter RFC 4180 con BOM UTF-8 y CRLF explícito`

#### A2 — Application: DTOs de Reportes y Auditoría

**Archivos:** `Application/Reportes/Dtos.cs`, `Application/Auditoria/Dtos.cs` — **Dep:** A1

- [ ] A2.1 Crear `Application/Reportes/Dtos.cs` con records: `ValorizacionItemDto`, `ValorizacionTotalesDto`, `StockCategoriaDto`, `MasMovidoDto` (firmas según diseño)
- [ ] A2.2 Crear `Application/Auditoria/Dtos.cs` con record: `AuditoriaItemDto`
- [ ] A2.3 `dotnet build StockApp.sln` → commit: `feat(app): DTOs de Reportes y Auditoría como records`

---

### Bloque B — Application: Interfaces + Servicios

#### B1 — Application: IReporteStockRepository + IReporteStockService + ReporteStockService.ObtenerValorizacionAsync

**Archivos:** `Application/Interfaces/IReporteStockRepository.cs`, `Application/Reportes/IReporteStockService.cs`, `Application/Reportes/ReporteStockService.cs`, `Application.Tests/Reportes/ReporteStockServiceValorizacionTests.cs` — **Dep:** A2

- [ ] B1.1 Escribir 4 tests: `ObtenerValorizacionAsync_CalculaValorCostoYValorVenta_Correcto`, `ObtenerValorizacionAsync_ProductoSinCategoria_Retorna_SinCategoria`, `ObtenerValorizacionAsync_CalculaTotalesCorrectamente`, `ObtenerValorizacionAsync_Operador_LanzaUnauthorizedAccessException`
- [ ] B1.2 Definir `IReporteStockRepository` con `ObtenerValorizacionAsync()`; definir `IReporteStockService` con `ObtenerValorizacionAsync()`
- [ ] B1.3 Implementar `ReporteStockService.ObtenerValorizacionAsync`: guard `_auth.Verificar(_session.RolActual, Permisos.VerReportes)` fail-closed antes de ejecutar la query; delega a `IReporteStockRepository`
- [ ] B1.4 `dotnet test Application.Tests/Reportes` verde → commit: `feat(app): ReporteStockService.ObtenerValorizacionAsync TDD — cálculo y autorización`

#### B2 — Application: ReporteStockService.ObtenerStockPorCategoriaAsync

**Archivos:** `Application/Reportes/ReporteStockService.cs` (mod), `Application.Tests/Reportes/ReporteStockServiceCategoriaTests.cs` — **Dep:** B1

- [ ] B2.1 Escribir 3 tests: `ObtenerStockPorCategoriaAsync_AgrupaCorrectamente`, `ObtenerStockPorCategoriaAsync_NullCategoria_GrupoSinCategoria`, `ObtenerStockPorCategoriaAsync_Operador_LanzaUnauthorized`
- [ ] B2.2 Agregar `ObtenerStockPorCategoriaAsync()` a `IReporteStockRepository` e implementar en `ReporteStockService` con guard
- [ ] B2.3 `dotnet test Application.Tests/Reportes` verde → commit: `feat(app): ReporteStockService.ObtenerStockPorCategoriaAsync TDD`

#### B3 — Application: ReporteStockService.ObtenerMasMovidosAsync

**Archivos:** `Application/Reportes/ReporteStockService.cs` (mod), `Application.Tests/Reportes/ReporteStockServiceMasMovidosTests.cs` — **Dep:** B1

- [ ] B3.1 Escribir 4 tests: `ObtenerMasMovidosAsync_OrdenadoPorVolumenTotalDesc`, `ObtenerMasMovidosAsync_TopNRespetado`, `ObtenerMasMovidosAsync_SinMovimientos_ListaVacia`, `ObtenerMasMovidosAsync_Operador_LanzaUnauthorized`
- [ ] B3.2 Agregar `ObtenerMasMovidosAsync(DateTime? fechaDesde, DateTime? fechaHasta, int topN)` a `IReporteStockRepository` e implementar en `ReporteStockService` con guard; `FechaHasta` se pasa cruda al repositorio (el ajuste fin de día lo aplica Infrastructure)
- [ ] B3.3 `dotnet test Application.Tests/Reportes` verde → commit: `feat(app): ReporteStockService.ObtenerMasMovidosAsync TDD`

#### B4 — Application: ReporteStockService.ObtenerHistorialPorProductoAsync (D2 — delegación pura)

**Archivos:** `Application/Reportes/ReporteStockService.cs` (mod), `Application.Tests/Reportes/ReporteStockServiceHistorialTests.cs` — **Dep:** B1

- [ ] B4.1 Escribir 2 tests: `ObtenerHistorialPorProductoAsync_DelegaAMovimientoStockService` (verifica que llama a `MovimientoStockService.ObtenerHistorialAsync` con los mismos parámetros), `ObtenerHistorialPorProductoAsync_Operador_LanzaUnauthorized`
- [ ] B4.2 Implementar `ObtenerHistorialPorProductoAsync`: guard primero; luego delega a `IMovimientoStockService.ObtenerHistorialAsync` — NO reimplementar lógica (decisión D2)
- [ ] B4.3 `dotnet test Application.Tests/Reportes` verde → commit: `feat(app): ReporteStockService.ObtenerHistorialPorProductoAsync — delegación a Inc5 + guard`

#### B5 — Application: IAuditoriaQueryService + AuditoriaQueryService

**Archivos:** `Application/Auditoria/IAuditoriaQueryService.cs`, `Application/Auditoria/AuditoriaQueryService.cs`, `Application/Interfaces/IAuditoriaQueryRepository.cs`, `Application.Tests/Auditoria/AuditoriaQueryServiceTests.cs` — **Dep:** A2

- [ ] B5.1 Escribir 5 tests: `ObtenerLogAsync_FiltraPorUsuario`, `ObtenerLogAsync_FiltraPorFechas`, `ObtenerLogAsync_FechaHasta_AplicadaComoFinDeDia`, `ObtenerLogAsync_OrdenadoPorFechaDesc`, `ObtenerLogAsync_Operador_LanzaUnauthorized`
- [ ] B5.2 Definir `IAuditoriaQueryRepository` con `ObtenerLogAsync(int? usuarioId, DateTime? fechaDesde, DateTime? fechaHasta)`; definir `IAuditoriaQueryService`
- [ ] B5.3 Implementar `AuditoriaQueryService`: guard `Permisos.VerReportes` fail-closed; delega a `IAuditoriaQueryRepository`
- [ ] B5.4 `dotnet test Application.Tests/Auditoria` verde → commit: `feat(app): AuditoriaQueryService TDD — filtros + autorización`

---

### Bloque C — Infrastructure: Repositorios EF

#### C1 — Infrastructure: ReporteStockRepository.ObtenerValorizacionAsync

**Archivos:** `Infrastructure/Repositories/ReporteStockRepository.cs`, `Infrastructure.Tests/Repositories/ReporteStockRepositoryValorizacionTests.cs` — **Dep:** B1

- [ ] C1.1 Escribir 3 tests integration (SQLite in-memory + EnsureCreated): `ObtenerValorizacionAsync_RetornaProductosActivos`, `ObtenerValorizacionAsync_ProductoSinCategoria_GrupoSinCategoria`, `ObtenerValorizacionAsync_TotalesCorrectos`
- [ ] C1.2 Implementar `ReporteStockRepository.ObtenerValorizacionAsync` con `Include(p => p.Categoria)`, null-coalescido `"Sin categoría"`, `OrderBy(p => p.Nombre)`
- [ ] C1.3 `dotnet test Infrastructure.Tests/Repositories` verde → commit: `feat(infra): ReporteStockRepository.ObtenerValorizacionAsync + integration tests`

#### C2 — Infrastructure: ReporteStockRepository.ObtenerStockPorCategoriaAsync

**Archivos:** `Infrastructure/Repositories/ReporteStockRepository.cs` (mod), `Infrastructure.Tests/Repositories/ReporteStockRepositoryCategoriaTests.cs` — **Dep:** C1, B2

- [ ] C2.1 Escribir 3 tests: `ObtenerStockPorCategoriaAsync_AgrupaCorrectamente`, `ObtenerStockPorCategoriaAsync_GrupoSinCategoria_Presente`, `ObtenerStockPorCategoriaAsync_CategoriasSinProductosActivos_NoAparecen`
- [ ] C2.2 Implementar `ObtenerStockPorCategoriaAsync`: `GroupBy` con null-coalescido; `Sum(StockActual)`, `Sum(StockActual * PrecioCosto)`, `Sum(StockActual * PrecioVenta)`
- [ ] C2.3 `dotnet test Infrastructure.Tests/Repositories` verde → commit: `feat(infra): ReporteStockRepository.ObtenerStockPorCategoriaAsync + integration tests`

#### C3 — Infrastructure: ReporteStockRepository.ObtenerMasMovidosAsync

**Archivos:** `Infrastructure/Repositories/ReporteStockRepository.cs` (mod), `Infrastructure.Tests/Repositories/ReporteStockRepositoryMasMovidosTests.cs` — **Dep:** C1, B3

- [ ] C3.1 Escribir 4 tests: `ObtenerMasMovidosAsync_OrdenadoPorVolumenDesc`, `ObtenerMasMovidosAsync_TopNRespetado`, `ObtenerMasMovidosAsync_FechaHastaFinDeDia`, `ObtenerMasMovidosAsync_SinMovimientos_ListaVacia`
- [ ] C3.2 Implementar `ObtenerMasMovidosAsync`: `GroupBy(m => m.ProductoId)`; `FechaHasta` ajustada con `fechaHasta.Value.Date.AddDays(1).AddTicks(-1)`; `OrderByDescending(x => x.VolumenTotal).Take(topN)`
- [ ] C3.3 `dotnet test Infrastructure.Tests/Repositories` verde → commit: `feat(infra): ReporteStockRepository.ObtenerMasMovidosAsync + integration tests`

#### C4 — Infrastructure: AuditoriaQueryRepository

**Archivos:** `Infrastructure/Repositories/AuditoriaQueryRepository.cs`, `Infrastructure.Tests/Repositories/AuditoriaQueryRepositoryTests.cs` — **Dep:** B5

- [ ] C4.1 Escribir 4 tests: `ObtenerLogAsync_FiltraPorUsuarioId`, `ObtenerLogAsync_FiltraPorFechas_FechaHastaFinDeDia`, `ObtenerLogAsync_SinFiltros_RetornaAll`, `ObtenerLogAsync_OrdenadoPorFechaDesc`
- [ ] C4.2 Implementar `AuditoriaQueryRepository`: `FechaHasta` ajustada con `fechaHasta.Value.Date.AddDays(1).AddTicks(-1)`; `Include(l => l.Usuario)`; `OrderByDescending(l => l.Fecha)`
- [ ] C4.3 `dotnet test Infrastructure.Tests/Repositories` verde → commit: `feat(infra): AuditoriaQueryRepository EF + integration tests`

#### C5 — Infrastructure: registro DI

**Archivos:** `Infrastructure/DependencyInjection.cs` (o donde se registran los servicios del Inc 5) — **Dep:** C1–C4, B1–B5

- [ ] C5.1 Registrar en el contenedor DI: `IReporteStockRepository` → `ReporteStockRepository`; `IAuditoriaQueryRepository` → `AuditoriaQueryRepository`; `IReporteStockService` → `ReporteStockService`; `IAuditoriaQueryService` → `AuditoriaQueryService`; `ICsvExporter` → `CsvExporter`
- [ ] C5.2 `dotnet build StockApp.sln` → commit: `feat(infra): registro DI de repositorios y servicios de reportes y auditoría`

---

### Bloque D — Presentation: ViewModels + Views

#### D1 — Presentation: paquete Avalonia.Controls.DataGrid

**Archivos:** `src/StockApp.Presentation/StockApp.Presentation.csproj`, `src/StockApp.Presentation/App.axaml` — **Dep:** C5

- [ ] D1.1 Agregar referencia NuGet `Avalonia.Controls.DataGrid` al proyecto Presentation
- [ ] D1.2 Registrar styles del DataGrid en `App.axaml`: `<StyleInclude Source="avares://Avalonia.Controls.DataGrid/Themes/Fluent.xaml"/>` (o equivalente para la versión instalada)
- [ ] D1.3 `dotnet build StockApp.sln` → commit: `feat(presentation): agregar Avalonia.Controls.DataGrid al proyecto`

#### D2 — Presentation: ValorizacionViewModel + ValorizacionView

**Archivos:** `Presentation/ViewModels/Reportes/ValorizacionViewModel.cs`, `Presentation/Views/Reportes/ValorizacionView.axaml` — **Dep:** D1

- [ ] D2.1 Escribir 2 tests: `BuscarCommand_LlamaObtenerValorizacionAsync_YPopulaItems`, `ExportarCommand_LlamaExportarConOrdenColumnasFijo`
- [ ] D2.2 Implementar `ValorizacionViewModel`: `[ObservableProperty] IReadOnlyList<ValorizacionItemDto> Items`; `[RelayCommand] BuscarAsync()`; `[RelayCommand] ExportarAsync()` — usa `ICsvExporter.Exportar(Items, columnOrder)` + `IStorageProvider` para file picker; montos (`ValorCosto`, `ValorVenta`) alineados a la derecha en el DataGrid
- [ ] D2.3 Crear `ValorizacionView.axaml` con `DataGrid` vinculado a `Items`
- [ ] D2.4 `dotnet test` verde → commit: `feat(presentation): ValorizacionViewModel + ValorizacionView`

#### D3 — Presentation: StockCategoriaViewModel + StockCategoriaView

**Archivos:** `Presentation/ViewModels/Reportes/StockCategoriaViewModel.cs`, `Presentation/Views/Reportes/StockCategoriaView.axaml` — **Dep:** D1

- [ ] D3.1 Escribir 2 tests: `BuscarCommand_LlamaObtenerStockPorCategoriaAsync_YPopulaItems`, `ExportarCommand_LlamaExportarConItems`
- [ ] D3.2 Implementar `StockCategoriaViewModel` + View con DataGrid; columnas de montos alineadas a la derecha
- [ ] D3.3 `dotnet test` verde → commit: `feat(presentation): StockCategoriaViewModel + StockCategoriaView`

#### D4 — Presentation: HistorialPorProductoViewModel + HistorialPorProductoView

**Archivos:** `Presentation/ViewModels/Reportes/HistorialPorProductoViewModel.cs`, `Presentation/Views/Reportes/HistorialPorProductoView.axaml` — **Dep:** D1

- [ ] D4.1 Escribir 2 tests: `BuscarCommand_LlamaObtenerHistorialPorProductoAsync_ConParametros`, `ExportarCommand_LlamaExportarConItems`
- [ ] D4.2 Implementar `HistorialPorProductoViewModel`: `[ObservableProperty]` para `ProductoId`, `FechaDesde`, `FechaHasta` + View con DataGrid
- [ ] D4.3 `dotnet test` verde → commit: `feat(presentation): HistorialPorProductoViewModel + HistorialPorProductoView`

#### D5 — Presentation: MasMovidosViewModel + MasMovidosView

**Archivos:** `Presentation/ViewModels/Reportes/MasMovidosViewModel.cs`, `Presentation/Views/Reportes/MasMovidosView.axaml` — **Dep:** D1

- [ ] D5.1 Escribir 2 tests: `BuscarCommand_LlamaObtenerMasMovidosAsync_ConTopN`, `ExportarCommand_LlamaExportarConItems`
- [ ] D5.2 Implementar `MasMovidosViewModel`: `[ObservableProperty]` para `FechaDesde`, `FechaHasta`, `TopN` (default 20) + View con DataGrid; columna `VolumenTotal` alineada a la derecha
- [ ] D5.3 `dotnet test` verde → commit: `feat(presentation): MasMovidosViewModel + MasMovidosView`

#### D6 — Presentation: AuditoriaLogViewModel + AuditoriaLogView

**Archivos:** `Presentation/ViewModels/Reportes/AuditoriaLogViewModel.cs`, `Presentation/Views/Reportes/AuditoriaLogView.axaml` — **Dep:** D1

- [ ] D6.1 Escribir 2 tests: `BuscarCommand_LlamaObtenerLogAsync_ConFiltros`, `ExportarCommand_LlamaExportarConItems`
- [ ] D6.2 Implementar `AuditoriaLogViewModel`: `[ObservableProperty]` para `UsuarioId?`, `FechaDesde`, `FechaHasta` + View con DataGrid
- [ ] D6.3 `dotnet test` verde → commit: `feat(presentation): AuditoriaLogViewModel + AuditoriaLogView`

---

### Bloque E — Navegación + Cierre

#### E1 — Presentation: navegación grupo "Reportes" en ShellMainViewModel

**Archivos:** `Presentation/ViewModels/ShellMainViewModel.cs` (mod), `Presentation.Tests/ViewModels/ShellMainViewModelReportesTests.cs` — **Dep:** D2–D6

- [ ] E1.1 Escribir 3 tests: `Admin_VeEntradasGrupoReportes`, `Operador_NoVeEntradasGrupoReportes`, `NavReportes_LlamaNavegar_ConViewModelCorrecto`
- [ ] E1.2 Agregar en `ShellMainViewModel` 5 commands de navegación bajo el grupo "Reportes" (`NavValorizacion`, `NavStockCategoria`, `NavHistorialPorProducto`, `NavMasMovidos`, `NavAuditoriaLog`), visibles solo cuando `EsAdmin`; registrar los 5 ViewModels en DI como `Transient`
- [ ] E1.3 `dotnet test` verde → commit: `feat(presentation): navegación grupo Reportes en ShellMainViewModel — solo Admin`

#### E2 — Cierre del Incremento 6

**Archivos:** `docs/plans/2026-06-08-00-roadmap.md` — **Dep:** E1

- [ ] E2.1 Ejecutar `dotnet build StockApp.sln` y `dotnet test` — confirmar 0 errores y sin regresiones del Inc 5
- [ ] E2.2 Marcar Incremento 6 como completado en `docs/plans/2026-06-08-00-roadmap.md`
- [ ] E2.3 Commit: `docs(plans): marcar Incremento 6 (Reportes + Auditoría) como completado`

---

**Total tasks: 19 | Tests nuevos estimados: ~75 | Commits: 19 (uno por task)**
