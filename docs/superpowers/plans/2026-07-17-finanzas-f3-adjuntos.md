# Finanzas F3 — Adjuntos — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir adjuntar archivos (PDF/JPG/PNG, máx 10MB) a Gastos y Pagos de Gasto, almacenados en Postgres como `bytea`, con ABM completo (agregar/listar/descargar/quitar) en API y desktop.

**Architecture:** Dos entidades de dominio (`Adjunto` metadatos + `AdjuntoContenido` bytes, 1:1) para que los listados nunca arrastren bytes. `AdjuntoService` en Application valida MIME por magic bytes y aplica la doble barrera de permisos (reusa `RegistrarGastos`/`RegistrarPagos`/`VerFinanzas`, sin permisos nuevos). API expone 5 endpoints multipart/JSON bajo `/finanzas/gastos/{id}/adjuntos`, `/finanzas/pagos/{id}/adjuntos` y `/finanzas/adjuntos/{id}`. Desktop agrega un panel de adjuntos reutilizable (`AdjuntosPanelViewModel`) embebido en `GastoFormViewModel` y `PagosGastoViewModel`, con servicios de plataforma nuevos para seleccionar y abrir archivos.

**Tech Stack:** .NET 10, Avalonia 12 (CommunityToolkit.Mvvm), EF Core + Npgsql (Postgres `bytea`), Minimal APIs (multipart/form-data), xUnit + Testcontainers Postgres (postgres:16-alpine).

## Global Constraints

- .NET 10, C# — nombres de dominio y mensajes de usuario en español.
- Baja lógica con `Activo` (nunca DELETE físico), también para `Adjunto`.
- Fechas en UTC (`FechaAltaUtc`).
- Conventional commits en español, SIN "Co-Authored-By".
- Tests de Infrastructure y Api contra Postgres real vía Testcontainers — NO hay InMemory.
- Doble barrera de permisos: `.RequireAuthorization(...)` en el endpoint Y `_auth.Verificar(_session.RolActual, Permisos.X)` al inicio del método de servicio.
- Permisos reusados, NO nuevos: `RegistrarGastos` (adjuntos de gasto), `RegistrarPagos` (adjuntos de pago), `VerFinanzas` (ver/listar/descargar).
- Invariante XOR: exactamente uno de `GastoId`/`PagoGastoId` no nulo — validado en `AdjuntoService`, no confiado a la BD (además de un `CHECK` en la migración como defensa en profundidad).
- Validación MIME por magic bytes en una única constante compartida (`Application`), NO duplicada entre API y UI.
- Tope de 10 MB por archivo, validado en el `service` (409 con mensaje claro vía `ReglaDeNegocioException`), no solo por el límite crudo de ASP.NET (que daría 413).
- Multipart: `DisableAntiforgery()` en los POST + límite de body configurado (`FormOptions.MultipartBodyLengthLimit` / `MaxRequestBodySize`).
- Auditoría de alta/baja de adjuntos vía `IAuditLogger` (valores nuevos y append-only en `AccionAuditada`, sin reordenar los existentes).
- NO cascada: anular un Gasto o PagoGasto NO toca sus adjuntos (quedan `Activo=true`).
- YAGNI: sin `ModificarAsync`/`PUT` para adjuntos (se quita y se sube otro) — desviación justificada de la "regla de oro" de servicios completos, documentada en Task 6.
- No hay `UsuarioId` por fila de adjunto (trazabilidad vía `IAuditLogger`, igual que el resto de las entidades de Finanzas).

---

## File Structure

### Domain
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Domain/Entities/Adjunto.cs` | Entidad de metadatos: Id, NombreArchivo, ContentType, TamanoBytes, GastoId?, PagoGastoId?, Activo, FechaAltaUtc. |
| `src/StockApp.Domain/Entities/AdjuntoContenido.cs` | Entidad 1:1 con `Adjunto` (Id = AdjuntoId): `byte[] Contenido`. |
| `src/StockApp.Domain/Enums/AccionAuditada.cs` (modify) | Agrega `AltaAdjunto = 40`, `BajaAdjunto = 41` (append-only). |

### Application
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Application/Finanzas/AdjuntoValidador.cs` | Constante única de validación MIME por magic bytes (PDF/JPG/PNG) + tope de 10 MB. Fuente compartida API/UI. |
| `src/StockApp.Application/Interfaces/IAdjuntoRepository.cs` | Contrato de persistencia. |
| `src/StockApp.Application/Finanzas/IAdjuntoService.cs` | Contrato de servicio (ABM sin Modificar). |
| `src/StockApp.Application/Finanzas/AdjuntoService.cs` | Implementación: doble barrera, validación XOR/MIME/tamaño, auditoría. |
| `src/StockApp.Application/Finanzas/AdjuntoDto.cs` | DTOs de metadatos (`AdjuntoDto`) y contenido (`AdjuntoContenidoDto`) devueltos por el servicio, consumidos por Api/ApiClient. |

### Infrastructure
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (modify) | `DbSet<Adjunto> Adjuntos`, `DbSet<AdjuntoContenido> AdjuntosContenido` + Fluent config en `OnModelCreating` (bytea, 1:1, CHECK XOR, índices). |
| `src/StockApp.Infrastructure/Repositories/AdjuntoRepository.cs` | Implementa `IAdjuntoRepository`. |
| `src/StockApp.Infrastructure/Migrations/<timestamp>_FinanzasAdjuntos.cs` | Migración EF generada. |
| `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` (modify) | Agrega `"AdjuntosContenido", "Adjuntos"` al `TRUNCATE`. |

### Api
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Api/Endpoints/AdjuntosEndpoints.cs` | 6 endpoints multipart/JSON bajo `/finanzas/gastos/{id}/adjuntos`, `/finanzas/pagos/{id}/adjuntos`, `/finanzas/adjuntos/{id}/...`. |
| `src/StockApp.Api/Program.cs` (modify) | DI (`IAdjuntoRepository`, `IAdjuntoService`), `FormOptions.MultipartBodyLengthLimit`, `app.MapAdjuntosEndpoints()`. |
| `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` (modify) | Agrega `"AdjuntosContenido", "Adjuntos"` al `TRUNCATE`. |

### ApiClient
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.ApiClient/AdjuntoApiClient.cs` | Implementa `IAdjuntoService` contra la API (multipart upload, descarga de bytes). |
| `src/StockApp.Presentation/App.axaml.cs` (modify) | `AddTransient<IAdjuntoService, AdjuntoApiClient>()`. |

### Presentation
| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs` / `ServicioSeleccionArchivo.cs` | Abrir file picker (Open), leer bytes + nombre. Molde: `IServicioGuardadoArchivo`. |
| `src/StockApp.Presentation/Services/IServicioAperturaArchivo.cs` / `ServicioAperturaArchivo.cs` | Guardar bytes a archivo temporal y abrirlo con la app del SO (`ProcessStartInfo` con `UseShellExecute = true`). |
| `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs` | VM reusable: `InicializarAsync(int? gastoId, int? pagoGastoId)`, `AgregarAsync`, `VerAsync`, `QuitarAsync`. |
| `src/StockApp.Presentation/Views/Finanzas/AdjuntosPanelView.axaml` / `.axaml.cs` | Vista reusable (lista + botones), wiring por `DataContextChanged`. |
| `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` (modify) | Expone `AdjuntosPanelViewModel AdjuntosPanel`, lo inicializa al cargar un gasto existente. |
| `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml` (modify) | Embebe `AdjuntosPanelView`. |
| `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs` (modify) | Expone `AdjuntosPanelViewModel AdjuntosPanel` ligado al pago seleccionado (`SelectedPago`/`OnPagoSeleccionadoChanged`). |
| `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml` (modify) | Embebe `AdjuntosPanelView`. |
| `src/StockApp.Presentation/App.axaml.cs` (modify) | DI: `IServicioSeleccionArchivo`, `IServicioAperturaArchivo` (singleton), `AdjuntosPanelViewModel` (transient). |

### Tests
| Archivo | Capa |
|---|---|
| `tests/StockApp.Domain.Tests/Entities/AdjuntoTests.cs` | Domain |
| `tests/StockApp.Application.Tests/Finanzas/AdjuntoValidadorTests.cs` | Application (magic bytes) |
| `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs` | Application (permisos, XOR, tamaño, auditoría) |
| `tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoRepositoryTests.cs` | Infrastructure (round-trip bytea real) |
| `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs` | Api (401/403/multipart/404/409) |
| `tests/StockApp.ApiClient.Tests/AdjuntoApiClientTests.cs` | ApiClient |
| `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs` | Presentation |

---

## Task 1 — Domain: entidades `Adjunto` y `AdjuntoContenido`

**Files:**
- Create: `src/StockApp.Domain/Entities/Adjunto.cs`
- Create: `src/StockApp.Domain/Entities/AdjuntoContenido.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/AdjuntoTests.cs`

**Interfaces:**
- Produces: `public class Adjunto { int Id; string NombreArchivo; string ContentType; long TamanoBytes; int? GastoId; Gasto? Gasto; int? PagoGastoId; PagoGasto? PagoGasto; bool Activo; DateTime FechaAltaUtc; bool EsDeGasto => GastoId is not null; bool EsDePago => PagoGastoId is not null; }`
- Produces: `public class AdjuntoContenido { int Id; byte[] Contenido; }` (Id comparte PK con `Adjunto.Id`, sin nav — la relación se configura en `AppDbContext`).

**Steps:**

- [ ] 1.1 Escribir el test que falla en `tests/StockApp.Domain.Tests/Entities/AdjuntoTests.cs`:
```csharp
using StockApp.Domain.Entities;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class AdjuntoTests
{
    [Fact]
    public void EsDeGasto_ConGastoIdSeteado_EsTrue()
    {
        var adjunto = new Adjunto { GastoId = 5 };

        Assert.True(adjunto.EsDeGasto);
        Assert.False(adjunto.EsDePago);
    }

    [Fact]
    public void EsDePago_ConPagoGastoIdSeteado_EsTrue()
    {
        var adjunto = new Adjunto { PagoGastoId = 8 };

        Assert.True(adjunto.EsDePago);
        Assert.False(adjunto.EsDeGasto);
    }

    [Fact]
    public void Activo_PorDefecto_EsTrue()
    {
        var adjunto = new Adjunto();

        Assert.True(adjunto.Activo);
    }
}
```
- [ ] 1.2 Correr y ver que falla (el tipo `Adjunto` no existe):
  `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter AdjuntoTests`
  Salida esperada: error de compilación `CS0246: The type or namespace name 'Adjunto' could not be found`.
- [ ] 1.3 Implementación mínima. Crear `src/StockApp.Domain/Entities/Adjunto.cs`:
```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Metadatos de un archivo adjunto a un Gasto (factura) o a un PagoGasto (recibo).
/// El contenido (bytes) vive SEPARADO en <see cref="AdjuntoContenido"/> (relación 1:1,
/// Id = AdjuntoId) para que listar adjuntos nunca arrastre los bytes de la BD.
/// Baja lógica con Activo, sin cascada: anular el Gasto/PagoGasto dueño NO anula sus
/// adjuntos (spec F3, decisión de alcance).
/// </summary>
public class Adjunto
{
    public int Id { get; set; }
    public string NombreArchivo { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long TamanoBytes { get; set; }

    /// <summary>Exactamente uno de GastoId/PagoGastoId es no nulo (invariante XOR).</summary>
    public int? GastoId { get; set; }
    public Gasto? Gasto { get; set; }

    public int? PagoGastoId { get; set; }
    public PagoGasto? PagoGasto { get; set; }

    public bool Activo { get; set; } = true;
    public DateTime FechaAltaUtc { get; set; }

    public bool EsDeGasto => GastoId is not null;
    public bool EsDePago => PagoGastoId is not null;
}
```
  Crear `src/StockApp.Domain/Entities/AdjuntoContenido.cs`:
```csharp
namespace StockApp.Domain.Entities;

/// <summary>
/// Bytes del adjunto, en tabla propia (mapea a <c>bytea</c> en Postgres). Id comparte
/// valor con el <see cref="Adjunto"/> dueño (relación 1:1 configurada en AppDbContext) —
/// separarlo del registro de metadatos evita que los listados de adjuntos (grilla del
/// formulario) traigan bytes que nadie pidió.
/// </summary>
public class AdjuntoContenido
{
    public int Id { get; set; }
    public byte[] Contenido { get; set; } = Array.Empty<byte>();
}
```
- [ ] 1.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.Domain.Tests/StockApp.Domain.Tests.csproj --filter AdjuntoTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 3`.
- [ ] 1.5 Commit:
  `git commit -m "feat(finanzas): entidades Adjunto y AdjuntoContenido"`

---

## Task 2 — Domain: nuevos valores de `AccionAuditada`

**Files:**
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`

**Interfaces:**
- Produces: `AccionAuditada.AltaAdjunto = 40`, `AccionAuditada.BajaAdjunto = 41`.

**Steps:**

- [ ] 2.1 No aplica TDD (es un enum, sin comportamiento a testear). Editar `src/StockApp.Domain/Enums/AccionAuditada.cs` agregando al final, respetando "append-only, NO reordenar":
```csharp
    // ── Finanzas — Fase 3: adjuntos (append-only a partir de 40) ─────────────
    AltaAdjunto = 40,
    BajaAdjunto = 41,
}
```
  (el `}` final del enum se mueve al cierre de este bloque; el resto del archivo queda igual).
- [ ] 2.2 Correr el build de Domain para confirmar que compila:
  `dotnet build src/StockApp.Domain/StockApp.Domain.csproj`
  Salida esperada: `Build succeeded.`
- [ ] 2.3 Commit:
  `git commit -m "feat(finanzas): valores AltaAdjunto/BajaAdjunto en AccionAuditada"`

---

## Task 3 — Application: `AdjuntoValidador` (magic bytes + tamaño)

**Files:**
- Create: `src/StockApp.Application/Finanzas/AdjuntoValidador.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/AdjuntoValidadorTests.cs`

**Interfaces:**
- Produces:
```csharp
public static class AdjuntoValidador
{
    public const long TamanoMaximoBytes = 10 * 1024 * 1024; // 10 MB
    public static readonly IReadOnlyList<string> ContentTypesPermitidos; // "application/pdf", "image/jpeg", "image/png"

    public static string? DetectarContentType(byte[] contenido); // null si no matchea ningún magic bytes de la whitelist
    public static void Validar(byte[] contenido, string nombreArchivo); // throw ReglaDeNegocioException si tamaño/mime inválido
}
```

**Steps:**

- [ ] 3.1 Escribir el test que falla en `tests/StockApp.Application.Tests/Finanzas/AdjuntoValidadorTests.cs`:
```csharp
using StockApp.Application.Finanzas;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class AdjuntoValidadorTests
{
    private static readonly byte[] MagicPdf = { 0x25, 0x50, 0x44, 0x46 };
    private static readonly byte[] MagicJpg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] MagicPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Fact]
    public void DetectarContentType_Pdf_DevuelveApplicationPdf()
    {
        Assert.Equal("application/pdf", AdjuntoValidador.DetectarContentType(MagicPdf));
    }

    [Fact]
    public void DetectarContentType_Jpg_DevuelveImageJpeg()
    {
        Assert.Equal("image/jpeg", AdjuntoValidador.DetectarContentType(MagicJpg));
    }

    [Fact]
    public void DetectarContentType_Png_DevuelveImagePng()
    {
        Assert.Equal("image/png", AdjuntoValidador.DetectarContentType(MagicPng));
    }

    [Fact]
    public void DetectarContentType_BytesNoReconocidos_DevuelveNull()
    {
        Assert.Null(AdjuntoValidador.DetectarContentType(new byte[] { 0x00, 0x01, 0x02 }));
    }

    [Fact]
    public void Validar_ArchivoValido_NoLanza()
    {
        AdjuntoValidador.Validar(MagicPdf, "factura.pdf");
    }

    [Fact]
    public void Validar_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var ex = Assert.Throws<ReglaDeNegocioException>(
            () => AdjuntoValidador.Validar(new byte[] { 0x00, 0x01 }, "archivo.exe"));

        Assert.Contains("PDF, JPG o PNG", ex.Message);
    }

    [Fact]
    public void Validar_ExcedeTamanoMaximo_LanzaReglaDeNegocio()
    {
        var contenido = new byte[AdjuntoValidador.TamanoMaximoBytes + 1];
        MagicPdf.CopyTo(contenido, 0);

        var ex = Assert.Throws<ReglaDeNegocioException>(
            () => AdjuntoValidador.Validar(contenido, "factura.pdf"));

        Assert.Contains("10", ex.Message);
    }
}
```
- [ ] 3.2 Correr y ver que falla:
  `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AdjuntoValidadorTests`
  Salida esperada: error de compilación `CS0234` (namespace `StockApp.Application.Finanzas` no tiene `AdjuntoValidador`).
- [ ] 3.3 Implementación mínima. Crear `src/StockApp.Application/Finanzas/AdjuntoValidador.cs`:
```csharp
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

/// <summary>
/// Fuente ÚNICA de validación de adjuntos: whitelist de tipo por magic bytes (no por
/// extensión, que se puede falsear) y tope de tamaño. Compartida entre AdjuntoService
/// (API) y el desktop (ServicioSeleccionArchivo) — spec F3 decisión 7: no duplicar el
/// hardcode de firmas entre capas.
/// </summary>
public static class AdjuntoValidador
{
    public const long TamanoMaximoBytes = 10 * 1024 * 1024; // 10 MB

    public static readonly IReadOnlyList<string> ContentTypesPermitidos =
        new[] { "application/pdf", "image/jpeg", "image/png" };

    private static readonly byte[] MagicPdf = { 0x25, 0x50, 0x44, 0x46 };             // %PDF
    private static readonly byte[] MagicJpg = { 0xFF, 0xD8, 0xFF };
    private static readonly byte[] MagicPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>Devuelve el content-type real según los primeros bytes, o null si no matchea la whitelist.</summary>
    public static string? DetectarContentType(byte[] contenido)
    {
        if (EmpiezaCon(contenido, MagicPdf)) return "application/pdf";
        if (EmpiezaCon(contenido, MagicJpg)) return "image/jpeg";
        if (EmpiezaCon(contenido, MagicPng)) return "image/png";
        return null;
    }

    private static bool EmpiezaCon(byte[] contenido, byte[] firma)
    {
        if (contenido.Length < firma.Length)
            return false;

        for (var i = 0; i < firma.Length; i++)
            if (contenido[i] != firma[i])
                return false;

        return true;
    }

    /// <summary>Valida tamaño y MIME real (por magic bytes). Lanza ReglaDeNegocioException con mensaje claro.</summary>
    public static void Validar(byte[] contenido, string nombreArchivo)
    {
        if (contenido.LongLength > TamanoMaximoBytes)
            throw new ReglaDeNegocioException(
                $"El archivo '{nombreArchivo}' supera el tamaño máximo permitido de 10 MB.");

        if (DetectarContentType(contenido) is null)
            throw new ReglaDeNegocioException(
                $"El archivo '{nombreArchivo}' no es un tipo permitido. Solo se aceptan PDF, JPG o PNG.");
    }
}
```
- [ ] 3.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AdjuntoValidadorTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 7`.
- [ ] 3.5 Commit:
  `git commit -m "feat(finanzas): AdjuntoValidador con validacion por magic bytes"`

---

## Task 4 — Application: DTOs y `IAdjuntoRepository`

**Files:**
- Create: `src/StockApp.Application/Finanzas/AdjuntoDto.cs`
- Create: `src/StockApp.Application/Interfaces/IAdjuntoRepository.cs`

No hay test propio (son contratos/records sin lógica); se verifican indirectamente en Task 5 y 8.

**Interfaces:**
- Produces:
```csharp
namespace StockApp.Application.Finanzas;

public record AdjuntoDto(
    int Id, string NombreArchivo, string ContentType, long TamanoBytes,
    int? GastoId, int? PagoGastoId, DateTime FechaAltaUtc);

public record AdjuntoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido);
```
```csharp
namespace StockApp.Application.Interfaces;

public interface IAdjuntoRepository
{
    Task<Adjunto?> ObtenerPorIdAsync(int id);
    Task<IReadOnlyList<Adjunto>> ListarPorGastoAsync(int gastoId);
    Task<IReadOnlyList<Adjunto>> ListarPorPagoAsync(int pagoGastoId);
    Task<int> AgregarAsync(Adjunto adjunto, byte[] contenido);
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);
    Task ActualizarAsync(Adjunto adjunto); // usado SOLO para la baja lógica (Activo=false)
}
```

**Steps:**

- [ ] 4.1 Crear `src/StockApp.Application/Finanzas/AdjuntoDto.cs`:
```csharp
namespace StockApp.Application.Finanzas;

/// <summary>Metadatos de un adjunto (sin bytes) — lo que devuelven los listados.</summary>
public record AdjuntoDto(
    int Id,
    string NombreArchivo,
    string ContentType,
    long TamanoBytes,
    int? GastoId,
    int? PagoGastoId,
    DateTime FechaAltaUtc);

/// <summary>Contenido completo para descarga (Results.File en el endpoint).</summary>
public record AdjuntoContenidoDto(string NombreArchivo, string ContentType, byte[] Contenido);
```
- [ ] 4.2 Crear `src/StockApp.Application/Interfaces/IAdjuntoRepository.cs`:
```csharp
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface IAdjuntoRepository
{
    /// <summary>Metadatos únicamente (sin bytes). Null si no existe.</summary>
    Task<Adjunto?> ObtenerPorIdAsync(int id);

    /// <summary>Metadatos de adjuntos ACTIVOS del gasto, ordenados por FechaAltaUtc desc.</summary>
    Task<IReadOnlyList<Adjunto>> ListarPorGastoAsync(int gastoId);

    /// <summary>Metadatos de adjuntos ACTIVOS del pago, ordenados por FechaAltaUtc desc.</summary>
    Task<IReadOnlyList<Adjunto>> ListarPorPagoAsync(int pagoGastoId);

    /// <summary>Inserta metadatos + contenido en una sola transacción. Devuelve el Id generado.</summary>
    Task<int> AgregarAsync(Adjunto adjunto, byte[] contenido);

    /// <summary>Bytes del adjunto (tabla separada). Null si no existe el adjunto.</summary>
    Task<byte[]?> ObtenerContenidoAsync(int adjuntoId);

    /// <summary><paramref name="adjunto"/> debe ser instancia tracked de ObtenerPorIdAsync. Usado solo para baja lógica.</summary>
    Task ActualizarAsync(Adjunto adjunto);
}
```
- [ ] 4.3 Correr el build de Application para confirmar que compila:
  `dotnet build src/StockApp.Application/StockApp.Application.csproj`
  Salida esperada: `Build succeeded.`
- [ ] 4.4 Commit:
  `git commit -m "feat(finanzas): DTOs e IAdjuntoRepository de adjuntos"`

---

## Task 5 — Infrastructure: EF Core config + migración + `AdjuntoRepository`

**Files:**
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` (DbSets tras línea 22, config tras el bloque `PagoGasto` en `OnModelCreating` ~línea 183)
- Create: `src/StockApp.Infrastructure/Repositories/AdjuntoRepository.cs`
- Create (generado por EF): `src/StockApp.Infrastructure/Migrations/<timestamp>_FinanzasAdjuntos.cs`
- Modify: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoRepositoryTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoRepository` (Task 4), `AppDbContext` (`DbSet<Adjunto> Adjuntos`, `DbSet<AdjuntoContenido> AdjuntosContenido`).
- Produces: `public class AdjuntoRepository : IAdjuntoRepository` (mismas firmas que Task 4).

**Steps:**

- [ ] 5.1 Modificar `AppDbContext.cs`: agregar los DbSets justo después de la línea 22 (`public DbSet<PagoGasto> PagosGasto => Set<PagoGasto>();`):
```csharp
    public DbSet<Adjunto> Adjuntos => Set<Adjunto>();
    public DbSet<AdjuntoContenido> AdjuntosContenido => Set<AdjuntoContenido>();
```
- [ ] 5.2 Agregar la config Fluent en `OnModelCreating`, inmediatamente después del bloque `modelBuilder.Entity<PagoGasto>(e => { ... });` (antes del bloque `IngresoCaja`):
```csharp
        // ── Finanzas: adjuntos (Fase 3 módulo Finanzas) ───────────────────────
        // Contenido separado en tabla propia (bytea) para que ListarPorGasto/Pago nunca
        // traigan bytes. CHECK XOR en BD como defensa en profundidad (AdjuntoService ya
        // valida la invariante en memoria antes de llegar acá).
        modelBuilder.Entity<Adjunto>(e =>
        {
            e.Property(a => a.NombreArchivo).IsRequired();
            e.Property(a => a.ContentType).IsRequired();
            e.Property(a => a.Activo).HasDefaultValue(true);
            e.HasIndex(a => a.GastoId);
            e.HasIndex(a => a.PagoGastoId);
            e.HasCheckConstraint(
                "CK_Adjuntos_GastoOPago",
                "(\"GastoId\" IS NOT NULL AND \"PagoGastoId\" IS NULL) OR " +
                "(\"GastoId\" IS NULL AND \"PagoGastoId\" IS NOT NULL)");
            e.HasOne(a => a.Gasto).WithMany()
                .HasForeignKey(a => a.GastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(a => a.PagoGasto).WithMany()
                .HasForeignKey(a => a.PagoGastoId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AdjuntoContenido>().WithOne()
                .HasForeignKey<AdjuntoContenido>(c => c.Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AdjuntoContenido>(e =>
        {
            e.Property(c => c.Contenido).IsRequired();
        });

```
  Nota: `OnDelete(DeleteBehavior.Cascade)` aplica SOLO a la relación interna `Adjunto → AdjuntoContenido` (metadatos y bytes del mismo adjunto forman una unidad atómica); la baja lógica del `Adjunto` en sí sigue siendo `Activo=false`, nunca DELETE físico — no hay contradicción con la convención del proyecto.
- [ ] 5.3 Generar la migración:
  `dotnet ef migrations add FinanzasAdjuntos -p src/StockApp.Infrastructure -s src/StockApp.Api`
  Salida esperada: `Done.` + archivo nuevo en `src/StockApp.Infrastructure/Migrations/`.
- [ ] 5.4 Revisar el archivo de migración generado: confirmar que `AdjuntosContenido.Contenido` mapeó a `type: "bytea"` y que el `CHECK` XOR aparece en `CreateTable` de `Adjuntos`. Si EF no generó el `CHECK` inline, agregarlo manualmente en el método `Up` de la migración con `migrationBuilder.Sql(...)`.
- [ ] 5.5 Actualizar `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` — agregar `"AdjuntosContenido", "Adjuntos"` a la lista de tablas del `TRUNCATE` (gap conocido: cada migración nueva de Finanzas debe actualizar este `LimpiarTablas()` o los tests posteriores ven data residual):
```csharp
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\" " +
            "RESTART IDENTITY CASCADE;");
```
  (`AdjuntosContenido` y `Adjuntos` antes de `PagosGasto`/`Gastos` porque tienen FK hacia esas tablas.)
- [ ] 5.6 Escribir el test que falla en `tests/StockApp.Infrastructure.Tests/Repositories/AdjuntoRepositoryTests.cs`:
```csharp
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

[Collection("Postgres")]
public class AdjuntoRepositoryTests : PostgresRepositoryTestBase
{
    private readonly AdjuntoRepository _repo;

    public AdjuntoRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new AdjuntoRepository(Context);
    }

    private async Task<int> CrearGastoAsync()
    {
        var proveedor = new Proveedor { Nombre = "Proveedor Test", Activo = true };
        Context.Proveedores.Add(proveedor);
        var fuente = new FuenteFinanciamiento { Nombre = "Fuente Test", Activo = true };
        Context.FuentesFinanciamiento.Add(fuente);
        var rubro = new RubroGasto { Nombre = "Rubro Test", Activo = true };
        Context.RubrosGasto.Add(rubro);
        await Context.SaveChangesAsync();

        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id, Detalle = "Test", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
            CondicionPago = CondicionPago.Contado,
        };
        Context.Gastos.Add(gasto);
        await Context.SaveChangesAsync();
        return gasto.Id;
    }

    [Fact]
    public async Task AgregarAsync_GuardaMetadatosYContenidoPorSeparado()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

        var adjunto = new Adjunto
        {
            NombreArchivo = "factura.pdf", ContentType = "application/pdf",
            TamanoBytes = bytes.Length, GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, bytes);

        var recuperado = await _repo.ObtenerPorIdAsync(id);
        Assert.NotNull(recuperado);
        Assert.Equal("factura.pdf", recuperado!.NombreArchivo);

        var contenido = await _repo.ObtenerContenidoAsync(id);
        Assert.Equal(bytes, contenido);
    }

    [Fact]
    public async Task ListarPorGastoAsync_SoloDevuelveActivosDelGasto()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };

        var id1 = await _repo.AgregarAsync(new Adjunto
        {
            NombreArchivo = "a.jpg", ContentType = "image/jpeg", TamanoBytes = bytes.Length,
            GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        }, bytes);

        var lista = await _repo.ListarPorGastoAsync(gastoId);

        Assert.Single(lista);
        Assert.Equal(id1, lista[0].Id);
    }

    [Fact]
    public async Task ActualizarAsync_BajaLogica_NoAparaceEnListado()
    {
        var gastoId = await CrearGastoAsync();
        var bytes = new byte[] { 0xFF, 0xD8, 0xFF };

        var id = await _repo.AgregarAsync(new Adjunto
        {
            NombreArchivo = "a.jpg", ContentType = "image/jpeg", TamanoBytes = bytes.Length,
            GastoId = gastoId, FechaAltaUtc = DateTime.UtcNow,
        }, bytes);

        var adjunto = await _repo.ObtenerPorIdAsync(id);
        adjunto!.Activo = false;
        await _repo.ActualizarAsync(adjunto);

        var lista = await _repo.ListarPorGastoAsync(gastoId);
        Assert.Empty(lista);
    }
}
```
- [ ] 5.7 Correr y ver que falla (falta `AdjuntoRepository`):
  `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter AdjuntoRepositoryTests`
  Salida esperada: error de compilación `CS0246: The type or namespace name 'AdjuntoRepository' could not be found`.
- [ ] 5.8 Implementación mínima. Crear `src/StockApp.Infrastructure/Repositories/AdjuntoRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class AdjuntoRepository : IAdjuntoRepository
{
    private readonly AppDbContext _ctx;

    public AdjuntoRepository(AppDbContext ctx) => _ctx = ctx;

    public Task<Adjunto?> ObtenerPorIdAsync(int id)
        => _ctx.Adjuntos.FirstOrDefaultAsync(a => a.Id == id);

    public Task<IReadOnlyList<Adjunto>> ListarPorGastoAsync(int gastoId)
        => ListarAsync(a => a.GastoId == gastoId);

    public Task<IReadOnlyList<Adjunto>> ListarPorPagoAsync(int pagoGastoId)
        => ListarAsync(a => a.PagoGastoId == pagoGastoId);

    private async Task<IReadOnlyList<Adjunto>> ListarAsync(
        System.Linq.Expressions.Expression<Func<Adjunto, bool>> filtro)
        => await _ctx.Adjuntos
            .Where(a => a.Activo)
            .Where(filtro)
            .OrderByDescending(a => a.FechaAltaUtc)
            .ToListAsync();

    public async Task<int> AgregarAsync(Adjunto adjunto, byte[] contenido)
    {
        _ctx.Adjuntos.Add(adjunto);
        await _ctx.SaveChangesAsync();

        _ctx.AdjuntosContenido.Add(new AdjuntoContenido { Id = adjunto.Id, Contenido = contenido });
        await _ctx.SaveChangesAsync();

        return adjunto.Id;
    }

    public async Task<byte[]?> ObtenerContenidoAsync(int adjuntoId)
    {
        var fila = await _ctx.AdjuntosContenido
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == adjuntoId);
        return fila?.Contenido;
    }

    public Task ActualizarAsync(Adjunto adjunto)
    {
        _ctx.Adjuntos.Update(adjunto);
        return _ctx.SaveChangesAsync();
    }
}
```
- [ ] 5.9 Correr y ver que pasa:
  `dotnet test tests/StockApp.Infrastructure.Tests/StockApp.Infrastructure.Tests.csproj --filter AdjuntoRepositoryTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 3`.
- [ ] 5.10 Commit:
  `git commit -m "feat(finanzas): AdjuntoRepository + migracion FinanzasAdjuntos"`

---

## Task 6 — Application: `IAdjuntoService` / `AdjuntoService`

**Files:**
- Create: `src/StockApp.Application/Finanzas/IAdjuntoService.cs`
- Create: `src/StockApp.Application/Finanzas/AdjuntoService.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoRepository` (Task 4), `IGastoRepository.ObtenerPorIdAsync(int)` (existente, para validar que el gasto existe), `AdjuntoValidador.Validar` (Task 3), `ICurrentSession`, `IAuthorizationService.Verificar`, `IAuditLogger.RegistrarAsync`.
- Produces:
```csharp
public interface IAdjuntoService
{
    Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido);
    Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId);
    Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId);
    Task QuitarAsync(int adjuntoId);
}
```

Nota YAGNI (constraint global 12): esta interfaz NO tiene `ModificarAsync` — un adjunto se quita (`QuitarAsync`, baja lógica) y se sube uno nuevo; editar metadatos de un archivo ya subido no tiene caso de uso en el spec. Desviación consciente de la convención "ABM completo" del resto de Finanzas.

**Steps:**

- [x] 6.1 Escribir los tests que fallan en `tests/StockApp.Application.Tests/Finanzas/AdjuntoServiceTests.cs`:
```csharp
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Application.Tests.Finanzas;

public class AdjuntoServiceTests
{
    private readonly Mock<IAdjuntoRepository> _adjuntos = new();
    private readonly Mock<IGastoRepository> _gastos = new();
    private readonly Mock<ICurrentSession> _session = new();
    private readonly Mock<IAuthorizationService> _auth = new();
    private readonly Mock<IAuditLogger> _audit = new();
    private readonly AdjuntoService _service;

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01 };

    public AdjuntoServiceTests()
    {
        _session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);
        _session.Setup(s => s.UsuarioActual).Returns(new StockApp.Application.Auth.UsuarioSesion(1, "admin", RolUsuario.Admin, null));
        _gastos.Setup(g => g.ObtenerPorIdAsync(It.IsAny<int>()))
            .ReturnsAsync(new Gasto { Id = 1, Activo = true });

        _service = new AdjuntoService(_adjuntos.Object, _gastos.Object, _session.Object, _auth.Object, _audit.Object);
    }

    [Fact]
    public async Task AgregarAGastoAsync_VerificaPermisoRegistrarGastos()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarGastos), Times.Once);
    }

    [Fact]
    public async Task AgregarAGastoAsync_GastoInexistente_LanzaEntidadNoEncontrada()
    {
        _gastos.Setup(g => g.ObtenerPorIdAsync(99)).ReturnsAsync((Gasto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.AgregarAGastoAsync(99, "factura.pdf", BytesPdf));
    }

    [Fact]
    public async Task AgregarAGastoAsync_MimeNoPermitido_LanzaReglaDeNegocio()
    {
        var bytesInvalidos = new byte[] { 0x00, 0x01, 0x02 };

        await Assert.ThrowsAsync<ReglaDeNegocioException>(
            () => _service.AgregarAGastoAsync(1, "archivo.exe", bytesInvalidos));
    }

    [Fact]
    public async Task AgregarAGastoAsync_Exitoso_RegistraAuditoria()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        _audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.AltaAdjunto, "Adjunto", 10, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AgregarAGastoAsync_SeteaGastoIdYPagoGastoIdNull()
    {
        Adjunto? capturado = null;
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>()))
            .Callback<Adjunto, byte[]>((a, _) => capturado = a)
            .ReturnsAsync(10);

        await _service.AgregarAGastoAsync(1, "factura.pdf", BytesPdf);

        Assert.NotNull(capturado);
        Assert.Equal(1, capturado!.GastoId);
        Assert.Null(capturado.PagoGastoId);
    }

    [Fact]
    public async Task AgregarAPagoAsync_VerificaPermisoRegistrarPagos()
    {
        _adjuntos.Setup(r => r.AgregarAsync(It.IsAny<Adjunto>(), It.IsAny<byte[]>())).ReturnsAsync(11);

        await _service.AgregarAPagoAsync(5, "recibo.pdf", BytesPdf);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarPagos), Times.Once);
    }

    [Fact]
    public async Task ListarPorGastoAsync_VerificaPermisoVerFinanzas()
    {
        _adjuntos.Setup(r => r.ListarPorGastoAsync(1)).ReturnsAsync(new List<Adjunto>());

        await _service.ListarPorGastoAsync(1);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.VerFinanzas), Times.Once);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_AdjuntoInexistente_LanzaEntidadNoEncontrada()
    {
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(99)).ReturnsAsync((Adjunto?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(
            () => _service.ObtenerContenidoAsync(99));
    }

    [Fact]
    public async Task QuitarAsync_HaceBajaLogicaYAuditoria()
    {
        var adjunto = new Adjunto { Id = 7, GastoId = 1, Activo = true, NombreArchivo = "a.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(7)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(7);

        Assert.False(adjunto.Activo);
        _adjuntos.Verify(r => r.ActualizarAsync(adjunto), Times.Once);
        _audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.BajaAdjunto, "Adjunto", 7, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task QuitarAsync_DeUnPago_VerificaPermisoRegistrarPagos()
    {
        var adjunto = new Adjunto { Id = 8, PagoGastoId = 5, Activo = true, NombreArchivo = "r.pdf" };
        _adjuntos.Setup(r => r.ObtenerPorIdAsync(8)).ReturnsAsync(adjunto);

        await _service.QuitarAsync(8);

        _auth.Verify(a => a.Verificar(RolUsuario.Admin, Permisos.RegistrarPagos), Times.Once);
    }
}
```
- [x] 6.2 Correr y ver que falla (falta `AdjuntoService`/`IAdjuntoService`):
  `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AdjuntoServiceTests`
  Salida esperada: error de compilación `CS0246`.
- [x] 6.3 Implementación mínima. Crear `src/StockApp.Application/Finanzas/IAdjuntoService.cs`:
```csharp
namespace StockApp.Application.Finanzas;

/// <summary>
/// ABM de Adjunto SIN Modificar (YAGNI, spec F3 decisión 8): un adjunto no se edita, se
/// quita y se sube otro. Doble barrera de permisos reusados (RegistrarGastos/RegistrarPagos/
/// VerFinanzas) — sin permisos nuevos.
/// </summary>
public interface IAdjuntoService
{
    Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido);
    Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId);
    Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId);
    Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId);
    Task QuitarAsync(int adjuntoId);
}
```
  Crear `src/StockApp.Application/Finanzas/AdjuntoService.cs`:
```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Application.Finanzas;

public class AdjuntoService : IAdjuntoService
{
    private readonly IAdjuntoRepository    _repo;
    private readonly IGastoRepository      _gastos;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public AdjuntoService(
        IAdjuntoRepository repo,
        IGastoRepository gastos,
        ICurrentSession session,
        IAuthorizationService auth,
        IAuditLogger audit)
    {
        _repo    = repo;
        _gastos  = gastos;
        _session = session;
        _auth    = auth;
        _audit   = audit;
    }

    public async Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarGastos);

        var gasto = await _gastos.ObtenerPorIdAsync(gastoId)
            ?? throw new EntidadNoEncontradaException($"No existe el gasto {gastoId}.");

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new Adjunto
        {
            NombreArchivo = nombreArchivo,
            ContentType = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes = contenido.LongLength,
            GastoId = gasto.Id,
            FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, contenido);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjunto, "Adjunto", id,
            $"Gasto {gastoId} — {nombreArchivo}");

        return ADto(adjunto with { });
    }

    public async Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido)
    {
        _auth.Verificar(_session.RolActual, Permisos.RegistrarPagos);

        AdjuntoValidador.Validar(contenido, nombreArchivo);

        var adjunto = new Adjunto
        {
            NombreArchivo = nombreArchivo,
            ContentType = AdjuntoValidador.DetectarContentType(contenido)!,
            TamanoBytes = contenido.LongLength,
            PagoGastoId = pagoGastoId,
            FechaAltaUtc = DateTime.UtcNow,
        };

        var id = await _repo.AgregarAsync(adjunto, contenido);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaAdjunto, "Adjunto", id,
            $"Pago {pagoGastoId} — {nombreArchivo}");

        return ADto(adjunto);
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return (await _repo.ListarPorGastoAsync(gastoId)).Select(ADto).ToList();
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);
        return (await _repo.ListarPorPagoAsync(pagoGastoId)).Select(ADto).ToList();
    }

    public async Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        _auth.Verificar(_session.RolActual, Permisos.VerFinanzas);

        var adjunto = await _repo.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        var contenido = await _repo.ObtenerContenidoAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el contenido del adjunto {adjuntoId}.");

        return new AdjuntoContenidoDto(adjunto.NombreArchivo, adjunto.ContentType, contenido);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var adjunto = await _repo.ObtenerPorIdAsync(adjuntoId)
            ?? throw new EntidadNoEncontradaException($"No existe el adjunto {adjuntoId}.");

        // El permiso depende de a qué pertenece el adjunto (spec F3, decisión 2).
        _auth.Verificar(_session.RolActual, adjunto.EsDePago ? Permisos.RegistrarPagos : Permisos.RegistrarGastos);

        adjunto.Activo = false;
        await _repo.ActualizarAsync(adjunto);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.BajaAdjunto, "Adjunto", adjuntoId,
            $"{adjunto.NombreArchivo}");
    }

    private static AdjuntoDto ADto(Adjunto a) => new(
        a.Id, a.NombreArchivo, a.ContentType, a.TamanoBytes, a.GastoId, a.PagoGastoId, a.FechaAltaUtc);
}
```
  Nota: `adjunto with { }` no compila sobre `class` sin `record` — corregir usando directamente `adjunto` (ya tiene `Id` seteado por `AgregarAsync` via referencia). Reemplazar `return ADto(adjunto with { });` por `return ADto(adjunto);` en `AgregarAGastoAsync` antes de compilar.
- [x] 6.4 Correr y ver que pasa:
  `dotnet test tests/StockApp.Application.Tests/StockApp.Application.Tests.csproj --filter AdjuntoServiceTests`
  Salida real: `Passed! - Failed: 0, Passed: 10` (el bloque de test de 6.1 trae 10 `[Fact]`, no 11 — corrección del plan, sin impacto).
- [x] 6.5 Commit:
  `git commit -m "feat(finanzas): AdjuntoService con doble barrera de permisos y auditoria"`

---

## Task 7 — Api: DI + `AdjuntosEndpoints` (multipart)

**Files:**
- Create: `src/StockApp.Api/Endpoints/AdjuntosEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs` (DI ~línea 116, FormOptions cerca del `builder.Services` inicial, registro de endpoint ~línea 396)
- Modify: `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs`
- Test: `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoService` (Task 6).
- Produces endpoints:
  - `POST /finanzas/gastos/{id:int}/adjuntos` (multipart, campo `archivo`) → 201 `AdjuntoDto`.
  - `POST /finanzas/pagos/{id:int}/adjuntos` (multipart, campo `archivo`) → 201 `AdjuntoDto`.
  - `GET /finanzas/gastos/{id:int}/adjuntos` → 200 `List<AdjuntoDto>`.
  - `GET /finanzas/pagos/{id:int}/adjuntos` → 200 `List<AdjuntoDto>`.
  - `GET /finanzas/adjuntos/{id:int}/contenido` → `Results.File(bytes, contentType, nombreArchivo)`.
  - `DELETE /finanzas/adjuntos/{id:int}` → 200.

**Steps:**

- [x] 7.1 Escribir los tests que fallan en `tests/StockApp.Api.Tests/AdjuntosEndpointTests.cs`. Sigue el patrón de `GastosEndpointTests` (hereda `ApiTestBase`, JWT por rol, seed vía `Factory.CrearContexto()`):
```csharp
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StockApp.Api.Tests.Fixtures;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class AdjuntosEndpointTests : ApiTestBase
{
    public AdjuntosEndpointTests(ApiFactory factory) : base(factory) { }

    private static readonly byte[] BytesPdf = { 0x25, 0x50, 0x44, 0x46, 0x01, 0x02 };

    private async Task<int> SembrarGastoAsync()
    {
        using var ctx = Factory.CrearContexto();
        var proveedor = new Proveedor { Nombre = "Prov", Activo = true };
        ctx.Proveedores.Add(proveedor);
        var fuente = new FuenteFinanciamiento { Nombre = "Fuente", Activo = true };
        ctx.FuentesFinanciamiento.Add(fuente);
        var rubro = new RubroGasto { Nombre = "Rubro", Activo = true };
        ctx.RubrosGasto.Add(rubro);
        await ctx.SaveChangesAsync();

        var gasto = new Gasto
        {
            ProveedorId = proveedor.Id, Detalle = "Test", Fecha = DateTime.UtcNow,
            MontoTotal = 100m, FuenteFinanciamientoId = fuente.Id, RubroGastoId = rubro.Id,
            CondicionPago = CondicionPago.Contado,
        };
        ctx.Gastos.Add(gasto);
        await ctx.SaveChangesAsync();
        return gasto.Id;
    }

    private static MultipartFormDataContent ArmarMultipart(byte[] bytes, string nombre)
    {
        var contenido = new MultipartFormDataContent();
        var archivo = new ByteArrayContent(bytes);
        archivo.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        contenido.Add(archivo, "archivo", nombre);
        return contenido;
    }

    [Fact]
    public async Task PostAdjuntoGasto_ComoOperador_Devuelve201()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClientAutenticado(RolUsuario.Operador);

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var dto = await response.Content.ReadFromJsonAsync<AdjuntoDto>();
        Assert.Equal("factura.pdf", dto!.NombreArchivo);
        Assert.Equal(gastoId, dto.GastoId);
    }

    [Fact]
    public async Task PostAdjuntoGasto_SinToken_Devuelve401()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClient();

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAdjuntoGasto_MimeInvalido_Devuelve409()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClientAutenticado(RolUsuario.Admin);

        var response = await client.PostAsync(
            $"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(new byte[] { 0x00, 0x01 }, "malware.exe"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetAdjuntosGasto_ListaLosActivos()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClientAutenticado(RolUsuario.Admin);
        await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));

        var response = await client.GetAsync($"/finanzas/gastos/{gastoId}/adjuntos");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lista = await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>();
        Assert.Single(lista!);
    }

    [Fact]
    public async Task GetContenido_DevuelveLosBytesOriginales()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClientAutenticado(RolUsuario.Admin);
        var creado = await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDto>();

        var response = await client.GetAsync($"/finanzas/adjuntos/{dto!.Id}/contenido");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(BytesPdf, bytes);
    }

    [Fact]
    public async Task DeleteAdjunto_ComoOperador_HaceBajaLogica()
    {
        var gastoId = await SembrarGastoAsync();
        var client = Factory.CreateClientAutenticado(RolUsuario.Operador);
        var creado = await client.PostAsync($"/finanzas/gastos/{gastoId}/adjuntos", ArmarMultipart(BytesPdf, "factura.pdf"));
        var dto = await creado.Content.ReadFromJsonAsync<AdjuntoDto>();

        var response = await client.DeleteAsync($"/finanzas/adjuntos/{dto!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listado = await client.GetAsync($"/finanzas/gastos/{gastoId}/adjuntos");
        var lista = await listado.Content.ReadFromJsonAsync<List<AdjuntoDto>>();
        Assert.Empty(lista!);
    }
}
```
  Nota: `Factory.CreateClientAutenticado(RolUsuario)` es el helper existente usado en `GastosEndpointTests` — si el nombre real difiere, ajustar al helper real de `ApiFactory` (verificar en `tests/StockApp.Api.Tests/Fixtures/ApiFactory.cs` antes de escribir el test, sin inventar un nombre nuevo).
- [x] 7.2 Actualizar `tests/StockApp.Api.Tests/Fixtures/ApiTestBase.cs` — agregar `"AdjuntosContenido", "Adjuntos"` al `TRUNCATE` (mismo gap que Task 5.5, ahora en la lista de `ApiTestBase`):
```csharp
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", " +
            "\"Usuarios\" RESTART IDENTITY CASCADE;");
```
- [x] 7.3 Correr y ver que falla (falta `AdjuntosEndpoints`/DI):
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter AdjuntosEndpointTests`
  Salida esperada: fallos de conexión/404 o error de compilación (según cuánto exista aún).
- [x] 7.4 Implementación. Crear `src/StockApp.Api/Endpoints/AdjuntosEndpoints.cs`:
```csharp
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;

namespace StockApp.Api.Endpoints;

public static class AdjuntosEndpoints
{
    public static IEndpointRouteBuilder MapAdjuntosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/finanzas/gastos/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAGastoAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.RegistrarGastos);

        app.MapPost("/finanzas/pagos/{id:int}/adjuntos", async (int id, IFormFile archivo, IAdjuntoService adjuntos) =>
        {
            using var ms = new MemoryStream();
            await archivo.CopyToAsync(ms);
            var dto = await adjuntos.AgregarAPagoAsync(id, archivo.FileName, ms.ToArray());
            return Results.Created((string?)null, dto);
        })
        .DisableAntiforgery()
        .RequireAuthorization(Permisos.RegistrarPagos);

        app.MapGet("/finanzas/gastos/{id:int}/adjuntos", async (int id, IAdjuntoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorGastoAsync(id)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/pagos/{id:int}/adjuntos", async (int id, IAdjuntoService adjuntos) =>
            Results.Ok(await adjuntos.ListarPorPagoAsync(id)))
            .RequireAuthorization(Permisos.VerFinanzas);

        app.MapGet("/finanzas/adjuntos/{id:int}/contenido", async (int id, IAdjuntoService adjuntos) =>
        {
            var contenido = await adjuntos.ObtenerContenidoAsync(id);
            return Results.File(contenido.Contenido, contenido.ContentType, contenido.NombreArchivo);
        })
        .RequireAuthorization(Permisos.VerFinanzas);

        app.MapDelete("/finanzas/adjuntos/{id:int}", async (int id, IAdjuntoService adjuntos) =>
        {
            await adjuntos.QuitarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.RegistrarGastos, Permisos.RegistrarPagos);

        return app;
    }
}
```
  Nota sobre `DELETE /finanzas/adjuntos/{id}`: `RequireAuthorization(a, b)` exige AMBOS permisos (AND), no sirve para "uno u otro" según el tipo de adjunto. El endpoint deja pasar con `RequireAuthorization()` (solo autenticado) y delega la decisión fina al `_auth.Verificar` DENTRO de `AdjuntoService.QuitarAsync` (Task 6, ya implementado con `adjunto.EsDePago ? RegistrarPagos : RegistrarGastos`) — la doble barrera igual se cumple porque la segunda barrera (el service) SIEMPRE corre y es la que tiene el permiso correcto por caso. Corregir la línea del `DELETE` a:
```csharp
        app.MapDelete("/finanzas/adjuntos/{id:int}", async (int id, IAdjuntoService adjuntos) =>
        {
            await adjuntos.QuitarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization();
```
- [x] 7.5 Modificar `src/StockApp.Api/Program.cs`:
  - Agregar el using de `IFormFile`/multipart (ya cubierto por `Microsoft.AspNetCore.Http`, implícito en Minimal APIs — no requiere using extra).
  - Tras la línea `builder.Services.AddScoped<IGastoService, GastoService>();` (línea 116), agregar:
```csharp
builder.Services.AddScoped<IAdjuntoRepository, AdjuntoRepository>();
builder.Services.AddScoped<IAdjuntoService, AdjuntoService>();
```
  - Configurar el límite de multipart. Agregar, junto a los demás `builder.Services.Configure<...>` (buscar el bloque cercano a `AddRateLimiter`, antes de `var app = builder.Build();`):
```csharp
// Límite de multipart para /finanzas/.../adjuntos (spec F3): 10MB + margen para headers,
// devuelve 400 en vez de la excepción cruda de Kestrel si se supera — el tope de negocio
// real (10MB exactos, con mensaje claro) lo valida AdjuntoValidador en el service.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 11 * 1024 * 1024;
});
```
  - Tras `app.MapGastosEndpoints();` (línea 396), agregar:
```csharp
app.MapAdjuntosEndpoints();
```
- [x] 7.6 Correr y ver que pasa:
  `dotnet test tests/StockApp.Api.Tests/StockApp.Api.Tests.csproj --filter AdjuntosEndpointTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 6`.
- [x] 7.7 Commit:
  `git commit -m "feat(finanzas): endpoints multipart de adjuntos en la API"`

---

## Task 8 — ApiClient: `AdjuntoApiClient`

**Files:**
- Create: `src/StockApp.ApiClient/AdjuntoApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI ~línea 182)
- Test: `tests/StockApp.ApiClient.Tests/AdjuntoApiClientTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoService` (Task 6, misma interfaz consumida del lado cliente), `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` (existentes).
- Produces: `public sealed class AdjuntoApiClient : IAdjuntoService`.

**Steps:**

- [ ] 8.1 Escribir los tests que fallan en `tests/StockApp.ApiClient.Tests/AdjuntoApiClientTests.cs`, siguiendo el patrón de `GastoApiClientTests` (`FakeHttpHandler`, `TestHttp`):
```csharp
using System.Net;
using System.Net.Http.Json;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class AdjuntoApiClientTests
{
    [Fact]
    public async Task AgregarAGastoAsync_EnviaMultipartYParseaRespuesta()
    {
        var dto = new AdjuntoDto(1, "factura.pdf", "application/pdf", 100, 5, null, DateTime.UtcNow);
        var handler = new FakeHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/gastos/5/adjuntos", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return Task.FromResult(TestHttp.Json(HttpStatusCode.Created, dto));
        });
        var client = new AdjuntoApiClient(TestHttp.Cliente(handler));

        var resultado = await client.AgregarAGastoAsync(5, "factura.pdf", new byte[] { 1, 2, 3 });

        Assert.Equal(1, resultado.Id);
        Assert.Equal("factura.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task ObtenerContenidoAsync_DevuelveBytesYNombreDesdeHeaders()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46 };
        var handler = new FakeHttpHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/pdf");
            response.Content.Headers.ContentDisposition =
                new System.Net.Http.Headers.ContentDispositionHeaderValue("attachment") { FileName = "factura.pdf" };
            return Task.FromResult(response);
        });
        var client = new AdjuntoApiClient(TestHttp.Cliente(handler));

        var resultado = await client.ObtenerContenidoAsync(1);

        Assert.Equal(bytes, resultado.Contenido);
        Assert.Equal("factura.pdf", resultado.NombreArchivo);
    }

    [Fact]
    public async Task QuitarAsync_EnviaDelete()
    {
        var handler = new FakeHttpHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Delete, request.Method);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        });
        var client = new AdjuntoApiClient(TestHttp.Cliente(handler));

        await client.QuitarAsync(7);
    }
}
```
  Nota: `FakeHttpHandler`/`TestHttp` son los helpers reales usados por `GastoApiClientTests` — si su firma exacta (`TestHttp.Json`, `TestHttp.Cliente`) difiere, ajustar al helper real leyendo `tests/StockApp.ApiClient.Tests/TestInfra/FakeHttpHandler.cs` y `TestHttp.cs` antes de escribir el test.
- [ ] 8.2 Correr y ver que falla (falta `AdjuntoApiClient`):
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AdjuntoApiClientTests`
  Salida esperada: error de compilación `CS0246`.
- [ ] 8.3 Implementación. Crear `src/StockApp.ApiClient/AdjuntoApiClient.cs`:
```csharp
using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IAdjuntoService contra /finanzas/.../adjuntos. Primer cliente que sube multipart/form-data
/// (upload) y descarga bytes crudos (download) — el resto de los XxxApiClient son JSON puro.
/// </summary>
public sealed class AdjuntoApiClient : IAdjuntoService
{
    private readonly HttpClient _http;

    public AdjuntoApiClient(HttpClient http) => _http = http;

    public Task<AdjuntoDto> AgregarAGastoAsync(int gastoId, string nombreArchivo, byte[] contenido)
        => SubirAsync($"finanzas/gastos/{gastoId}/adjuntos", nombreArchivo, contenido);

    public Task<AdjuntoDto> AgregarAPagoAsync(int pagoGastoId, string nombreArchivo, byte[] contenido)
        => SubirAsync($"finanzas/pagos/{pagoGastoId}/adjuntos", nombreArchivo, contenido);

    private async Task<AdjuntoDto> SubirAsync(string ruta, string nombreArchivo, byte[] contenido)
    {
        using var multipart = new MultipartFormDataContent();
        using var archivo = new ByteArrayContent(contenido);
        multipart.Add(archivo, "archivo", nombreArchivo);

        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<AdjuntoDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al subir el adjunto.");
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorGastoAsync(int gastoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/gastos/{gastoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>() ?? new List<AdjuntoDto>();
    }

    public async Task<IReadOnlyList<AdjuntoDto>> ListarPorPagoAsync(int pagoGastoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/pagos/{pagoGastoId}/adjuntos"));
        await ApiErrores.AsegurarExitoAsync(response);
        return await response.Content.ReadFromJsonAsync<List<AdjuntoDto>>() ?? new List<AdjuntoDto>();
    }

    public async Task<AdjuntoContenidoDto> ObtenerContenidoAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync($"finanzas/adjuntos/{adjuntoId}/contenido"));
        await ApiErrores.AsegurarExitoAsync(response);

        var bytes = await response.Content.ReadAsByteArrayAsync();
        var nombreArchivo = response.Content.Headers.ContentDisposition?.FileName?.Trim('"') ?? "adjunto";
        var contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";

        return new AdjuntoContenidoDto(nombreArchivo, contentType, bytes);
    }

    public async Task QuitarAsync(int adjuntoId)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.DeleteAsync($"finanzas/adjuntos/{adjuntoId}"));
        await ApiErrores.AsegurarExitoAsync(response);
    }
}
```
- [ ] 8.4 Actualizar `src/StockApp.Presentation/App.axaml.cs`, tras la línea `services.AddTransient<IFinanzasVistasService, FinanzasVistasApiClient>();` (línea 184):
```csharp
        services.AddTransient<IAdjuntoService, AdjuntoApiClient>();
```
- [ ] 8.5 Correr y ver que pasa:
  `dotnet test tests/StockApp.ApiClient.Tests/StockApp.ApiClient.Tests.csproj --filter AdjuntoApiClientTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 3`.
- [ ] 8.6 Commit:
  `git commit -m "feat(finanzas): AdjuntoApiClient con upload multipart y descarga"`

---

## Task 9 — Presentation: servicios de plataforma (seleccionar y abrir archivo)

**Files:**
- Create: `src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs`
- Create: `src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs`
- Create: `src/StockApp.Presentation/Services/IServicioAperturaArchivo.cs`
- Create: `src/StockApp.Presentation/Services/ServicioAperturaArchivo.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI, tras `IServicioGuardadoArchivo` ~línea 209)

No hay test unitario (son servicios de UI/SO, igual que `ServicioGuardadoArchivo` — sin test, comentario explícito).

**Interfaces:**
- Produces:
```csharp
public interface IServicioSeleccionArchivo
{
    Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync();
}
```
```csharp
public interface IServicioAperturaArchivo
{
    Task AbrirAsync(string nombreArchivo, byte[] contenido);
}
```

**Steps:**

- [ ] 9.1 Crear `src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs`:
```csharp
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para elegir un archivo desde disco (Agregar adjunto). Molde:
/// IServicioGuardadoArchivo (Inc 6), pero de apertura en vez de guardado.
/// </summary>
public interface IServicioSeleccionArchivo
{
    /// <summary>
    /// Muestra el selector de archivo filtrando por PDF/JPG/PNG. Devuelve el nombre y los
    /// bytes leídos, o null si el usuario canceló.
    /// </summary>
    Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync();
}
```
- [ ] 9.2 Crear `src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs`:
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioSeleccionArchivo"/>. Usa el IStorageProvider
/// de la ventana principal para elegir un archivo y lo lee a memoria. No se testea
/// unitariamente (es UI); en entornos headless devuelve null de forma segura.
/// </summary>
public class ServicioSeleccionArchivo : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<(string, byte[])?>(null);

        return Dispatcher.UIThread.InvokeAsync(SeleccionarInternoAsync);
    }

    private static async Task<(string, byte[])?> SeleccionarInternoAsync()
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var storageProvider = lifetime?.MainWindow?.StorageProvider;
        if (storageProvider is null)
            return null;

        var archivos = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("Documentos e imágenes")
                {
                    Patterns = new[] { "*.pdf", "*.jpg", "*.jpeg", "*.png" },
                    MimeTypes = new[] { "application/pdf", "image/jpeg", "image/png" },
                },
            },
        });

        if (archivos.Count == 0)
            return null;

        var archivo = archivos[0];
        await using var stream = await archivo.OpenReadAsync();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);

        return (archivo.Name, ms.ToArray());
    }
}
```
- [ ] 9.3 Crear `src/StockApp.Presentation/Services/IServicioAperturaArchivo.cs`:
```csharp
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Abstracción para "Ver" un adjunto: escribe los bytes a un archivo temporal y lo abre
/// con la aplicación por defecto del sistema operativo.
/// </summary>
public interface IServicioAperturaArchivo
{
    Task AbrirAsync(string nombreArchivo, byte[] contenido);
}
```
- [ ] 9.4 Crear `src/StockApp.Presentation/Services/ServicioAperturaArchivo.cs`:
```csharp
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

/// <summary>
/// Implementación real de <see cref="IServicioAperturaArchivo"/>: guarda a un archivo
/// temporal (carpeta temp del SO, subcarpeta "stockapp-adjuntos") y lo abre con
/// ProcessStartInfo(UseShellExecute = true) — delega en la app asociada del SO (visor de
/// PDF, imágenes). No se testea unitariamente (lanza un proceso externo real).
/// </summary>
public class ServicioAperturaArchivo : IServicioAperturaArchivo
{
    public async Task AbrirAsync(string nombreArchivo, byte[] contenido)
    {
        var carpetaTemp = Path.Combine(Path.GetTempPath(), "stockapp-adjuntos");
        Directory.CreateDirectory(carpetaTemp);

        var ruta = Path.Combine(carpetaTemp, nombreArchivo);
        await File.WriteAllBytesAsync(ruta, contenido);

        Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
    }
}
```
- [ ] 9.5 Registrar en DI. Modificar `src/StockApp.Presentation/App.axaml.cs`, tras `services.AddSingleton<IServicioGuardadoArchivo, ServicioGuardadoArchivo>();` (línea 209):
```csharp
        // ── Fase 3 módulo Finanzas: adjuntos — seleccionar/abrir archivos ──────
        services.AddSingleton<IServicioSeleccionArchivo, ServicioSeleccionArchivo>();
        services.AddSingleton<IServicioAperturaArchivo, ServicioAperturaArchivo>();
```
- [ ] 9.6 Correr el build de Presentation para confirmar que compila:
  `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj`
  Salida esperada: `Build succeeded.`
- [ ] 9.7 Commit:
  `git commit -m "feat(finanzas): servicios de seleccion y apertura de archivos para adjuntos"`

---

## Task 10 — Presentation: `AdjuntosPanelViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs` (DI, junto a los demás VMs de Finanzas ~línea 261)
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs`

**Interfaces:**
- Consumes: `IAdjuntoService` (Task 6/8), `IServicioSeleccionArchivo`, `IServicioAperturaArchivo` (Task 9), `IConfirmacionService` (existente).
- Produces:
```csharp
public partial class AdjuntosPanelViewModel : ViewModelBase
{
    public ObservableCollection<AdjuntoDto> Items { get; }
    [ObservableProperty] private bool _puedeModificar; // true si hay GastoId o PagoGastoId cargado y el usuario tiene el permiso correspondiente evaluado por el caller
    public Task InicializarAsync(int? gastoId, int? pagoGastoId);
    [RelayCommand] Task AgregarAsync();
    [RelayCommand] Task VerAsync(AdjuntoDto item);
    [RelayCommand] Task QuitarAsync(AdjuntoDto item);
}
```

**Steps:**

- [ ] 10.1 Escribir los tests que fallan en `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/AdjuntosPanelViewModelTests.cs`:
```csharp
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class AdjuntosPanelViewModelTests
{
    private readonly Mock<IAdjuntoService> _adjuntos = new();
    private readonly Mock<IServicioSeleccionArchivo> _seleccion = new();
    private readonly Mock<IServicioAperturaArchivo> _apertura = new();
    private readonly Mock<IConfirmacionService> _confirmacion = new();
    private readonly AdjuntosPanelViewModel _vm;

    public AdjuntosPanelViewModelTests()
    {
        _vm = new AdjuntosPanelViewModel(_adjuntos.Object, _seleccion.Object, _apertura.Object, _confirmacion.Object);
    }

    [Fact]
    public async Task InicializarAsync_ConGastoId_CargaListaDeGasto()
    {
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5)).ReturnsAsync(new List<AdjuntoDto>
        {
            new(1, "a.pdf", "application/pdf", 10, 5, null, DateTime.UtcNow),
        });

        await _vm.InicializarAsync(gastoId: 5, pagoGastoId: null);

        Assert.Single(_vm.Items);
        _adjuntos.Verify(a => a.ListarPorGastoAsync(5), Times.Once);
    }

    [Fact]
    public async Task InicializarAsync_ConPagoGastoId_CargaListaDePago()
    {
        _adjuntos.Setup(a => a.ListarPorPagoAsync(8)).ReturnsAsync(new List<AdjuntoDto>());

        await _vm.InicializarAsync(gastoId: null, pagoGastoId: 8);

        _adjuntos.Verify(a => a.ListarPorPagoAsync(8), Times.Once);
    }

    [Fact]
    public async Task AgregarAsync_UsuarioCancelaSeleccion_NoLlamaAlServicio()
    {
        _seleccion.Setup(s => s.SeleccionarArchivoAsync()).ReturnsAsync(((string, byte[])?)null);
        await _vm.InicializarAsync(5, null);

        await _vm.AgregarCommand.ExecuteAsync(null);

        _adjuntos.Verify(a => a.AgregarAGastoAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<byte[]>()), Times.Never);
    }

    [Fact]
    public async Task AgregarAsync_ConGastoId_SubeYRecargaLista()
    {
        _seleccion.Setup(s => s.SeleccionarArchivoAsync()).ReturnsAsync(("factura.pdf", new byte[] { 1, 2 }));
        _adjuntos.Setup(a => a.AgregarAGastoAsync(5, "factura.pdf", It.IsAny<byte[]>()))
            .ReturnsAsync(new AdjuntoDto(1, "factura.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow));
        _adjuntos.Setup(a => a.ListarPorGastoAsync(5))
            .ReturnsAsync(new List<AdjuntoDto> { new(1, "factura.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow) });
        await _vm.InicializarAsync(5, null);

        await _vm.AgregarCommand.ExecuteAsync(null);

        _adjuntos.Verify(a => a.AgregarAGastoAsync(5, "factura.pdf", It.IsAny<byte[]>()), Times.Once);
        Assert.Single(_vm.Items);
    }

    [Fact]
    public async Task QuitarAsync_LlamaAlServicioYRecarga()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.SetupSequence(a => a.ListarPorGastoAsync(5))
            .ReturnsAsync(new List<AdjuntoDto> { item })
            .ReturnsAsync(new List<AdjuntoDto>());
        await _vm.InicializarAsync(5, null);

        await _vm.QuitarCommand.ExecuteAsync(item);

        _adjuntos.Verify(a => a.QuitarAsync(1), Times.Once);
        Assert.Empty(_vm.Items);
    }

    [Fact]
    public async Task VerAsync_DescargaYAbre()
    {
        var item = new AdjuntoDto(1, "a.pdf", "application/pdf", 2, 5, null, DateTime.UtcNow);
        _adjuntos.Setup(a => a.ObtenerContenidoAsync(1))
            .ReturnsAsync(new AdjuntoContenidoDto("a.pdf", "application/pdf", new byte[] { 1, 2 }));

        await _vm.VerCommand.ExecuteAsync(item);

        _apertura.Verify(x => x.AbrirAsync("a.pdf", It.IsAny<byte[]>()), Times.Once);
    }
}
```
- [ ] 10.2 Correr y ver que falla (falta `AdjuntosPanelViewModel`):
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter AdjuntosPanelViewModelTests`
  Salida esperada: error de compilación `CS0246`.
- [ ] 10.3 Implementación. Crear `src/StockApp.Presentation/ViewModels/Finanzas/AdjuntosPanelViewModel.cs`:
```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Panel reusable de adjuntos, embebido en GastoFormViewModel (adjuntos del gasto) y
/// PagosGastoViewModel (adjuntos del pago seleccionado). Exactamente uno de GastoId/
/// PagoGastoId está seteado tras InicializarAsync — igual que la invariante XOR de Adjunto.
/// </summary>
public partial class AdjuntosPanelViewModel : ViewModelBase
{
    private readonly IAdjuntoService _adjuntos;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IServicioAperturaArchivo _apertura;
    private readonly IConfirmacionService _confirmacion;

    private int? _gastoId;
    private int? _pagoGastoId;

    public ObservableCollection<AdjuntoDto> Items { get; } = new();

    public AdjuntosPanelViewModel(
        IAdjuntoService adjuntos,
        IServicioSeleccionArchivo seleccion,
        IServicioAperturaArchivo apertura,
        IConfirmacionService confirmacion)
    {
        _adjuntos = adjuntos;
        _seleccion = seleccion;
        _apertura = apertura;
        _confirmacion = confirmacion;
    }

    public async Task InicializarAsync(int? gastoId, int? pagoGastoId)
    {
        _gastoId = gastoId;
        _pagoGastoId = pagoGastoId;
        await RecargarAsync();
    }

    private async Task RecargarAsync()
    {
        Items.Clear();
        IReadOnlyList<AdjuntoDto> lista = _gastoId is int gastoId
            ? await _adjuntos.ListarPorGastoAsync(gastoId)
            : _pagoGastoId is int pagoGastoId
                ? await _adjuntos.ListarPorPagoAsync(pagoGastoId)
                : Array.Empty<AdjuntoDto>();

        foreach (var item in lista)
            Items.Add(item);
    }

    [RelayCommand]
    private async Task AgregarAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoAsync();
        if (seleccionado is null)
            return;

        var (nombreArchivo, contenido) = seleccionado.Value;

        try
        {
            if (_gastoId is int gastoId)
                await _adjuntos.AgregarAGastoAsync(gastoId, nombreArchivo, contenido);
            else if (_pagoGastoId is int pagoGastoId)
                await _adjuntos.AgregarAPagoAsync(pagoGastoId, nombreArchivo, contenido);

            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync("No se pudo agregar el adjunto", ex.Message);
        }
    }

    [RelayCommand]
    private async Task VerAsync(AdjuntoDto item)
    {
        try
        {
            var contenido = await _adjuntos.ObtenerContenidoAsync(item.Id);
            await _apertura.AbrirAsync(contenido.NombreArchivo, contenido.Contenido);
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync("No se pudo abrir el adjunto", ex.Message);
        }
    }

    [RelayCommand]
    private async Task QuitarAsync(AdjuntoDto item)
    {
        try
        {
            await _adjuntos.QuitarAsync(item.Id);
            await RecargarAsync();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync("No se pudo quitar el adjunto", ex.Message);
        }
    }
}
```
  Nota: verificar la firma exacta de `IConfirmacionService.InformarAsync` (usada en `GastosViewModel`, ver constraint del brief "Errores → IConfirmacionService.InformarAsync") antes de compilar — si difiere de `(string titulo, string mensaje)`, ajustar al método real sin inventar overloads.
- [ ] 10.4 Registrar en DI. Modificar `src/StockApp.Presentation/App.axaml.cs`, tras `services.AddTransient<ControlPoaViewModel>();` (o el último VM de Finanzas de la lista, línea ~262):
```csharp
        services.AddTransient<AdjuntosPanelViewModel>();
```
- [ ] 10.5 Correr y ver que pasa:
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter AdjuntosPanelViewModelTests`
  Salida esperada: `Passed! - Failed: 0, Passed: 6`.
- [ ] 10.6 Commit:
  `git commit -m "feat(finanzas): AdjuntosPanelViewModel reusable"`

---

## Task 11 — Presentation: integrar el panel en `GastoFormViewModel` + View

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml`
- Test: extender `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/GastoFormViewModelTests.cs` (archivo existente — agregar casos, no reescribir).

**Interfaces:**
- Consumes: `AdjuntosPanelViewModel.InicializarAsync(int?, int?)` (Task 10).
- Produces: `GastoFormViewModel.AdjuntosPanel` (propiedad pública, `AdjuntosPanelViewModel`).

**Steps:**

- [ ] 11.1 Leer `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs` completo (constructor y el método que carga un gasto existente para edición, ej. `CargarParaEditar`) antes de editar, para no romper el constructor existente ni duplicar inyección.
- [ ] 11.2 Escribir el test que falla (agregar al archivo de test existente `GastoFormViewModelTests.cs`):
```csharp
    [Fact]
    public async Task CargarParaEditar_InicializaElPanelDeAdjuntosConElGastoId()
    {
        var gasto = new Gasto { Id = 42, /* ...resto de props obligatorias del molde existente... */ };
        // Arrange: mock de AdjuntosPanelViewModel real (no mockeable directo si no tiene interfaz) —
        // en su lugar, verificar sobre el AdjuntosPanel.Items tras CargarParaEditar usando un
        // Mock<IAdjuntoService> inyectado en el AdjuntosPanelViewModel real que arma el VM.

        vm.CargarParaEditar(gasto);

        Assert.NotNull(vm.AdjuntosPanel);
    }
```
  Nota: este test es deliberadamente liviano (`AdjuntosPanel` no es null) porque `GastoFormViewModelTests.cs` ya tiene su propio patrón de arrange con mocks — el implementador debe seguir EXACTAMENTE el patrón de constructor de mocks que ya usa ese archivo (leído en 11.1), no inventar uno nuevo. Si el constructor de `GastoFormViewModel` no recibe `AdjuntosPanelViewModel` como parámetro inyectable, agregarlo como parámetro constructor adicional al final de la lista de parámetros existente.
- [ ] 11.3 Correr y ver que falla:
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter GastoFormViewModelTests`
- [ ] 11.4 Implementación. En `GastoFormViewModel.cs`:
  - Agregar el campo y parámetro de constructor:
```csharp
    private readonly AdjuntosPanelViewModel _adjuntosPanel;
    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;
```
  (agregar `AdjuntosPanelViewModel adjuntosPanel` como último parámetro del constructor existente, asignando `_adjuntosPanel = adjuntosPanel;` en el cuerpo — sin tocar el orden de los parámetros previos).
  - En el método que carga un gasto para edición (identificado en 11.1), al final, agregar:
```csharp
        _ = _adjuntosPanel.InicializarAsync(gasto.Id, null);
```
  (fire-and-forget consciente: el panel se carga async sin bloquear la apertura del formulario, igual que otros paneles secundarios de la app; si el patrón existente en el archivo usa `await` porque el método ya es async, usar `await _adjuntosPanel.InicializarAsync(gasto.Id, null);` en su lugar — seguir la convención real del método, no forzar fire-and-forget si no aplica).
- [ ] 11.5 Modificar `src/StockApp.Presentation/Views/Finanzas/GastoFormView.axaml`: agregar, dentro del contenedor principal del formulario (después de los campos existentes, antes de los botones de Guardar/Cancelar), el embed del panel:
```xml
        <ContentControl Content="{Binding AdjuntosPanel}" Margin="0,16,0,0" />
```
  (verificar que `AdjuntosPanelView` esté registrada como `DataTemplate` global — si el proyecto usa `ViewLocator` por convención de nombres VM→View como el resto de la app, no hace falta declarar el `DataTemplate` a mano).
- [ ] 11.6 Correr y ver que pasa:
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter GastoFormViewModelTests`
- [ ] 11.7 Commit:
  `git commit -m "feat(finanzas): panel de adjuntos en el formulario de gasto"`

---

## Task 12 — Presentation: integrar el panel en `PagosGastoViewModel` + View

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml`
- Test: extender `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs` (si existe; si no existe, crear siguiendo el patrón de `GastoFormViewModelTests.cs`).

**Interfaces:**
- Consumes: `AdjuntosPanelViewModel.InicializarAsync(int?, int?)` (Task 10).
- Produces: `PagosGastoViewModel.AdjuntosPanel` (propiedad pública), `PagosGastoViewModel.PagoSeleccionado` (nueva `[ObservableProperty]` si no existe ya una selección de fila).

**Steps:**

- [ ] 12.1 Leer `src/StockApp.Presentation/ViewModels/Finanzas/PagosGastoViewModel.cs` completo antes de editar. Confirmar si ya existe una propiedad de "pago seleccionado" en la grilla (buscar `SelectedItem`/`PagoSeleccionado` en la View asociada `PagosGastoView.axaml`); si no existe, se agrega en este task.
- [ ] 12.2 Escribir el test que falla (agregar/crear en `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/PagosGastoViewModelTests.cs`), siguiendo el patrón real de arrange de ese archivo (mocks de `IGastoService`, `ICurrentSession`, etc.):
```csharp
    [Fact]
    public async Task SeleccionarPago_InicializaElPanelDeAdjuntosConElPagoId()
    {
        var pago = new PagoGasto { Id = 7, GastoId = 1, Activo = true };
        vm.Pagos.Add(pago);

        vm.PagoSeleccionado = pago;
        await Task.Delay(1); // deja correr el fire-and-forget de OnPagoSeleccionadoChanged si aplica

        Assert.NotNull(vm.AdjuntosPanel);
    }
```
- [ ] 12.3 Correr y ver que falla:
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter PagosGastoViewModelTests`
- [ ] 12.4 Implementación. En `PagosGastoViewModel.cs`:
  - Agregar el campo, parámetro de constructor y propiedad (mismo patrón que Task 11.4):
```csharp
    private readonly AdjuntosPanelViewModel _adjuntosPanel;
    public AdjuntosPanelViewModel AdjuntosPanel => _adjuntosPanel;

    [ObservableProperty] private PagoGasto? _pagoSeleccionado;

    partial void OnPagoSeleccionadoChanged(PagoGasto? value)
    {
        if (value is not null)
            _ = _adjuntosPanel.InicializarAsync(null, value.Id);
    }
```
  (si `PagoSeleccionado` ya existe con otro nombre en el VM real, reusar ESE nombre y agregar el `partial void On<Nombre>Changed` correspondiente en vez de duplicar la propiedad).
- [ ] 12.5 Modificar `src/StockApp.Presentation/Views/Finanzas/PagosGastoView.axaml`: enlazar `SelectedItem` de la grilla de pagos a `PagoSeleccionado` (si no está ya enlazado) y agregar el embed del panel:
```xml
        <ContentControl Content="{Binding AdjuntosPanel}" Margin="0,16,0,0" />
```
- [ ] 12.6 Correr y ver que pasa:
  `dotnet test tests/StockApp.Presentation.Tests/StockApp.Presentation.Tests.csproj --filter PagosGastoViewModelTests`
- [ ] 12.7 Commit:
  `git commit -m "feat(finanzas): panel de adjuntos en pagos de gasto"`

---

## Task 13 — Verificación final de la suite completa

**Files:** ninguno (solo comandos).

**Steps:**

- [ ] 13.1 Correr la suite completa de todas las capas:
  `dotnet test`
  Salida esperada: todos los proyectos en verde, sin regresiones en los tests preexistentes de Finanzas F1/F2/F4.
- [ ] 13.2 Si algún test preexistente falla por el `TRUNCATE` ampliado (Tasks 5.5/7.2), confirmar que el orden de las tablas en la lista respeta las FKs (Adjuntos/AdjuntosContenido antes de Gastos/PagosGasto).
- [ ] 13.3 Commit final si hubo ajustes:
  `git commit -m "test(finanzas): adjuntos F3 — suite completa verde"`

---

## Self-Review

Cobertura del spec §6 (spec `docs/superpowers/specs/2026-07-15-modulo-finanzas-design.md` + decisiones del brief) contra las tasks:

| Requisito del spec | Task que lo cubre |
|---|---|
| Almacenamiento bytea, contenido separado de metadatos | Task 1 (entidades), Task 5 (EF config + migración) |
| Entidad Adjunto con GastoId? XOR PagoGastoId? | Task 1 (campos), Task 5 (CHECK en BD), Task 6 (validación en memoria) |
| Límites PDF/JPG/PNG, 10MB, magic bytes | Task 3 (`AdjuntoValidador`), Task 6 (uso en `AdjuntoService`), Task 9 (filtro del file picker) |
| Endpoints multipart (2 POST, 3 GET, 1 DELETE) | Task 7 |
| JWT + doble barrera de permisos | Task 6 (`_auth.Verificar`), Task 7 (`RequireAuthorization`) |
| Desktop: lista + Agregar/Ver/Quitar en el form de gasto/pago | Task 9 (servicios de plataforma), Task 10 (VM reusable), Task 11 y 12 (integración) |
| Auditoría alta/baja vía IAuditLogger | Task 2 (enum), Task 6 (llamadas a `_audit.RegistrarAsync`) |
| ABM completo estilo CategoriaService | Task 6, con desviación YAGNI documentada (sin Modificar) |
| Baja del padre sin cascada sobre adjuntos | Task 5 (FK `OnDelete(DeleteBehavior.Restrict)` en `Adjunto→Gasto`/`Adjunto→PagoGasto`; NO se toca `Adjunto.Activo` desde `GastoService.AnularAsync`/`AnularPagoAsync` — no requiere código nuevo, es la ausencia deliberada de esa lógica) |
| Migración EF generada con comando exacto | Task 5.3 |

Scan de placeholders: no quedan "TODO"/"similar a Task N" en los steps de código — cada Task con cambio de código trae el archivo completo o el diff completo a aplicar. Las únicas notas de "verificar la firma real antes de compilar" (Tasks 7.1, 8.1, 10.3, 11.2, 11.4, 12.1, 12.4) son intencionales: apuntan a helpers y VMs YA EXISTENTES cuyo código exacto un ejecutor debe leer en el momento (mismo patrón de gap conocido documentado en discovery previo del proyecto: "task-brief VERBATIM snippets pueden no compilar" — el ejecutor corre el test/build real antes de dar el paso por cerrado).

Consistencia de tipos verificada:
- `IAdjuntoService` (Task 6) es EL MISMO contrato consumido por `AdjuntoApiClient` (Task 8) y `AdjuntosPanelViewModel` (Task 10) — mismas firmas de método en los tres.
- `AdjuntoDto`/`AdjuntoContenidoDto` (Task 4) son los tipos de retorno consistentes entre `AdjuntoService`, `AdjuntosEndpoints` (serializados a JSON), `AdjuntoApiClient` (deserializados) y `AdjuntosPanelViewModel.Items`.
- `AccionAuditada.AltaAdjunto`/`BajaAdjunto` (Task 2) son los ÚNICOS valores nuevos usados en `AdjuntoService` (Task 6) — sin duplicar ni reordenar los existentes.
- Permisos usados en Task 6 y Task 7 coinciden exactamente: `Permisos.RegistrarGastos`, `Permisos.RegistrarPagos`, `Permisos.VerFinanzas` (sin permisos nuevos, conforme a la decisión del brief).

Riesgos/huecos detectados durante la escritura (corregidos inline, no dejados como deuda):
- El `DELETE /finanzas/adjuntos/{id}` no puede usar `RequireAuthorization(permisoA, permisoB)` como OR — se corrigió en Task 7.4 a `RequireAuthorization()` (solo autenticado), delegando la decisión fina de permiso al `AdjuntoService.QuitarAsync` ya implementado en Task 6, que sí resuelve `RegistrarGastos` vs `RegistrarPagos` según `adjunto.EsDePago`.
- `adjunto with { }` en el borrador inicial de `AgregarAGastoAsync` no compila sobre una `class` no-record — corregido inline en Task 6.3 a `return ADto(adjunto);`.
- El gap de `TRUNCATE` en fixtures de test (patrón ya conocido del proyecto en Finanzas F1) se corrige explícitamente en Task 5.5 (`PostgresRepositoryTestBase`) y Task 7.2 (`ApiTestBase`), en vez de descubrirse recién al correr Task 13.
