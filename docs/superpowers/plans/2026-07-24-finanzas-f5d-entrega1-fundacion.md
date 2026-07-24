# F5d Entrega 1 — Fundación · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Construir la fundación de bajo riesgo del importador de planillas (F5d, spec §8 Entrega 1): endpoint de historial + ApiClient + sidebar + pantalla contenedora con 2 tabs + tab Historial completo (grilla + Revertir por fila) + wizard de 3 pasos con el Paso 2 en modo SOLO LECTURA con color de fila por `EstadoFila`. La grilla híbrida editable del Paso 2 (Entrega 2) queda explícitamente fuera de este plan.

**Architecture:** Capas server→client de abajo hacia arriba: query de lectura sobre `LogsAuditoria` en `ImportacionRepository` → método de aplicación con gate de permiso en `ConfirmacionImportacionService` → endpoint Minimal API admin-only → `ImportacionApiClient` (4 métodos, sin remapeo — los DTOs de `StockApp.Application.Finanzas` ya son la forma de wire) → ViewModels de Avalonia (contenedor con 2 tabs, Historial, wizard de 3 pasos como una sola VM con estado `PasoActual`). Ningún paso de esta entrega escribe una migración nueva ni agrega edición de celda.

**Tech Stack:** .NET 10, ASP.NET Core Minimal APIs, EF Core 10 / Npgsql (Postgres), Avalonia 12 (`DataGridCollectionView` para sort), CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), xUnit + Moq (Application/Presentation/ApiClient) y xUnit contra Postgres real vía `PostgresRepositoryTestBase`/`ApiFactory` (Infrastructure/Api).

## Global Constraints

- Stack: .NET 10, Avalonia 12 — grillas de solo lectura envueltas en `DataGridCollectionView` (gotcha Avalonia 12/DataGrid: bindear un DataGrid directo a una `ObservableCollection` con `CanUserSortColumns="True"` no ordena, AvaloniaUI/Avalonia#21129).
- Admin-only: permiso `Permisos.ImportarPlanillas` = `"finanzas.importar"` (`RolUsuario.Admin` únicamente, verificado con `_auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas)` en el servicio de aplicación y `.RequireAuthorization(Permisos.ImportarPlanillas)` en el endpoint).
- TDD estricto en TODAS las tasks de este plan: escribir el test que falla → correr y verificar que falla con el mensaje/tipo esperado → implementación mínima → correr y verificar que pasa → commit. Nunca escribir implementación antes que su test.
- Endpoints HTTP: matriz de autorización completa (401 sin token, 403 rol sin permiso, 200/404 según corresponda) — mismo patrón que `ImportacionReversionEndpointTests`/`ImportacionEndpointTests`.
- Historial: SIN migración nueva, SIN entidad cabecera nueva — query de lectura 100% sobre `LogsAuditoria` (columna tipada `IdLote`, ya indexada). `TRUNCATE` de tests (`PostgresRepositoryTestBase`/`ApiTestBase`) ya incluye `"LogsAuditoria"` y `"Usuarios"` — no requiere tocarse.
- Paso 2 del wizard (grillas Gastos/Ingresos/Líneas POA/Maestros nuevos) es SOLO LECTURA en esta entrega — CERO edición de celda. Confirmar se deshabilita si `Resumen.Errores > 0`.
- ApiClient: reusa `ApiErrores.EnviarAsync`/`AsegurarExitoAsync` y `ApiQuery` de `StockApp.ApiClient`. Los DTOs de `StockApp.Application.Finanzas` (`ResultadoAnalisisDto`, `ConfirmarImportacionDto`, `ResultadoConfirmacionDto`, `ResultadoReversionDto`) se serializan/deserializan DIRECTO — sin registros `Wire` intermedios (mismo criterio que `FinanzasVistasApiClient`, no el de `GastoApiClient`).
- Commits: conventional commits en español, SIN `Co-Authored-By` ni atribución de IA.
- No usar `cat`/`grep`/`find`/`sed` para explorar durante la implementación — usar las herramientas dedicadas del entorno de ejecución.

## File Structure

**Server (Api / Application / Infrastructure):**
- `src/StockApp.Application/Finanzas/ImportacionHistorialDtos.cs` (crear) — `ImportacionHistorialDto`.
- `src/StockApp.Application/Interfaces/IImportacionRepository.cs` (modificar) — agrega `ListarHistorialAsync()`.
- `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs` (modificar) — implementa `ListarHistorialAsync()` (query sobre `LogsAuditoria`).
- `src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs` (modificar) — agrega `ListarHistorialAsync()`.
- `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs` (modificar) — implementa `ListarHistorialAsync()` con gate de permiso.
- `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs` (modificar) — agrega `GET /finanzas/importar/historial`.
- `tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs` (modificar) — implementa el método nuevo de la interfaz (ripple obligatorio, si no el fake deja de compilar).

**Cliente (Application/Finanzas contrato + ApiClient):**
- `src/StockApp.Application/Finanzas/IImportacionService.cs` (crear) — 4 métodos.
- `src/StockApp.ApiClient/ImportacionApiClient.cs` (crear) — implementación.
- `src/StockApp.Presentation/App.axaml.cs` (modificar) — registro DI de `IImportacionService` + de los VMs nuevos.

**Presentation (sidebar, contenedor, tabs, wizard):**
- `src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs` (modificar) — agrega `SeleccionarArchivoOdsAsync()`.
- `src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs` (modificar) — implementa el filtro `.ods`.
- `src/StockApp.Presentation/Views/ShellMainView.axaml` (modificar) — sección "Importación" admin-only.
- `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` (modificar) — `NavImportacionCommand`.
- `src/StockApp.Presentation/ViewModels/Finanzas/ImportacionViewModel.cs` (crear) — contenedor, 2 tabs.
- `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml` + `.axaml.cs` (crear).
- `src/StockApp.Presentation/ViewModels/Finanzas/HistorialImportacionesViewModel.cs` (crear).
- `src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml` + `.axaml.cs` (crear).
- `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs` (crear) — wizard completo (Pasos 1/2/3 como una sola VM con `PasoActual`).
- `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml` + `.axaml.cs` (crear).
- `src/StockApp.Presentation/Converters/EstadoFilaBrushConverter.cs` (crear) — color de fila por `EstadoFila`.

**Tests nuevos:**
- `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryHistorialTests.cs` (crear).
- `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs` (crear).
- `tests/StockApp.Api.Tests/ImportacionHistorialEndpointTests.cs` (crear).
- `tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs` (crear).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ImportacionViewModelTests.cs` (crear).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/HistorialImportacionesViewModelTests.cs` (crear).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs` (crear, crece en Tasks 7/8/9).

---

### Task 1: Historial — query de lectura en el repositorio

**Files:**
- Create: `src/StockApp.Application/Finanzas/ImportacionHistorialDtos.cs`
- Modify: `src/StockApp.Application/Interfaces/IImportacionRepository.cs`
- Modify: `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryHistorialTests.cs`

**Interfaces:**
- Consumes: `AppDbContext.LogsAuditoria` (`StockApp.Infrastructure.Persistence`), `LogAuditoria.IdLote`/`Accion`/`EntidadId`/`Fecha`/`UsuarioId`/`Usuario` (`StockApp.Domain.Entities`), `AccionAuditada.ImportacionPlanillas`/`ReversionImportacion` (`StockApp.Domain.Enums`).
- Produces: `ImportacionHistorialDto(Guid IdImportacion, DateTime Fecha, int Ejercicio, string Usuario, bool Revertida)`; `IImportacionRepository.ListarHistorialAsync(): Task<IReadOnlyList<ImportacionHistorialDto>>` — usado por Task 2.

- [ ] **Step 1: Escribir el DTO (sin test — es un record sin comportamiento)**

```csharp
// src/StockApp.Application/Finanzas/ImportacionHistorialDtos.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Fila del historial de importaciones (F5d §3). Se deriva ENTERAMENTE de LogsAuditoria — sin
/// entidad cabecera ni migración nueva. Revertida se calcula comparando IdLote contra los logs
/// de AccionAuditada.ReversionImportacion (mismo patrón que
/// ImportacionRepository.BuscarImportacionNoRevertidaAsync).
/// </summary>
public sealed record ImportacionHistorialDto(
    Guid IdImportacion, DateTime Fecha, int Ejercicio, string Usuario, bool Revertida);
```

- [ ] **Step 2: Escribir el test que falla (repo test contra Postgres real)**

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryHistorialTests.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

/// <summary>F5d Task 1: ListarHistorialAsync lee ÚNICAMENTE de LogsAuditoria, sin entidad
/// cabecera. Reusa el mismo seed de usuario que ImportacionRepositoryTests.</summary>
public class ImportacionRepositoryHistorialTests : PostgresRepositoryTestBase
{
    private const int Ejercicio = 2026;
    private readonly ImportacionRepository _repo;
    private readonly int _usuarioId;

    public ImportacionRepositoryHistorialTests(PostgresFixture fixture) : base(fixture)
    {
        var usuarioSemilla = new Usuario
        {
            NombreUsuario = "historial-tests",
            HashContrasena = "hash",
            Rol = RolUsuario.Admin,
            Activo = true,
            FechaAlta = DateTime.UtcNow,
        };
        Context.Usuarios.Add(usuarioSemilla);
        Context.SaveChanges();
        _usuarioId = usuarioSemilla.Id;
        Context.ChangeTracker.Clear();

        _repo = new ImportacionRepository(Context);
    }

    private static ConfirmarImportacionDto PayloadMinimo(int ejercicio, bool forzar = false) => new(
        Ejercicio: ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ListarHistorialAsync_ImportacionSinRevertir_LaListaComoActiva()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadMinimo(Ejercicio), usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        var fila = Assert.Single(historial);
        Assert.Equal(resultado.IdImportacion, fila.IdImportacion);
        Assert.Equal(Ejercicio, fila.Ejercicio);
        Assert.Equal("historial-tests", fila.Usuario);
        Assert.False(fila.Revertida);
    }

    [Fact]
    public async Task ListarHistorialAsync_ImportacionRevertida_LaMarcaComoRevertida()
    {
        var resultado = await _repo.ConfirmarAsync(PayloadMinimo(Ejercicio), usuarioId: _usuarioId);
        await _repo.RevertirAsync(resultado.IdImportacion, usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        var fila = Assert.Single(historial);
        Assert.True(fila.Revertida);
    }

    [Fact]
    public async Task ListarHistorialAsync_VariasImportaciones_OrdenaPorFechaDescendente()
    {
        var primero = await _repo.ConfirmarAsync(PayloadMinimo(2024), usuarioId: _usuarioId);
        var segundo = await _repo.ConfirmarAsync(PayloadMinimo(2025), usuarioId: _usuarioId);

        var historial = await _repo.ListarHistorialAsync();

        Assert.Equal(2, historial.Count);
        Assert.Equal(segundo.IdImportacion, historial[0].IdImportacion);
        Assert.Equal(primero.IdImportacion, historial[1].IdImportacion);
    }
}
```

- [ ] **Step 3: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryHistorialTests"`
Expected: FAIL con error de compilación — `IImportacionRepository` no declara `ListarHistorialAsync` (`'ImportacionRepository' does not implement interface member` / `'ListarHistorialAsync' does not exist in the current context`).

- [ ] **Step 4: Declarar el método en la interfaz**

```csharp
// src/StockApp.Application/Interfaces/IImportacionRepository.cs
using StockApp.Application.Finanzas;

namespace StockApp.Application.Interfaces;

public interface IImportacionRepository
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto, int usuarioId);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion, int usuarioId);

    /// <summary>F5d §3: historial derivado de LogsAuditoria, sin entidad cabecera ni migración.</summary>
    Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync();
}
```

- [ ] **Step 5: Implementar en ImportacionRepository**

Agregar al final de la clase `ImportacionRepository` (después de `RevertirAsync`/antes de los métodos privados), en `src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs`:

```csharp
    /// <summary>
    /// F5d §3: historial de importaciones derivado ENTERAMENTE de LogsAuditoria — sin entidad
    /// cabecera ni migración. Dos queries (mismo criterio que BuscarImportacionNoRevertidaAsync):
    /// el set de IdLote revertidos se trae una sola vez y se compara en memoria contra cada
    /// confirmación, evitando un N+1 de AnyAsync por fila. l.Usuario!.NombreUsuario genera el
    /// JOIN a Usuarios en SQL (mismo patrón que AuditoriaQueryRepository.ObtenerLogAsync) — sin
    /// Include explícito.
    /// </summary>
    public async Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        var revertidos = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ReversionImportacion && l.IdLote != null)
            .Select(l => l.IdLote!.Value)
            .ToHashSetAsync();

        var confirmaciones = await _ctx.LogsAuditoria
            .Where(l => l.Accion == AccionAuditada.ImportacionPlanillas && l.IdLote != null)
            .OrderByDescending(l => l.Fecha)
            .Select(l => new
            {
                IdImportacion = l.IdLote!.Value,
                l.Fecha,
                Ejercicio = l.EntidadId,
                Usuario = l.Usuario!.NombreUsuario,
            })
            .ToListAsync();

        return confirmaciones
            .Select(c => new ImportacionHistorialDto(
                c.IdImportacion, c.Fecha, c.Ejercicio, c.Usuario, revertidos.Contains(c.IdImportacion)))
            .ToList();
    }
```

- [ ] **Step 6: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~ImportacionRepositoryHistorialTests"`
Expected: PASS (3/3).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Finanzas/ImportacionHistorialDtos.cs \
        src/StockApp.Application/Interfaces/IImportacionRepository.cs \
        src/StockApp.Infrastructure/Repositories/ImportacionRepository.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/ImportacionRepositoryHistorialTests.cs
git commit -m "feat(finanzas): historial de importaciones — query de lectura sobre LogsAuditoria"
```

---

### Task 2: Historial — servicio de aplicación con gate de permiso

**Files:**
- Modify: `src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs`
- Modify: `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs`
- Test: `tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs`

**Interfaces:**
- Consumes: `IImportacionRepository.ListarHistorialAsync()` (Task 1); `IAuthorizationService.Verificar(RolUsuario?, string)`; `ICurrentSession.RolActual`; `Permisos.ImportarPlanillas`.
- Produces: `IConfirmacionImportacionService.ListarHistorialAsync(): Task<IReadOnlyList<ImportacionHistorialDto>>` — usado por Task 3 (endpoint).

- [ ] **Step 1: Actualizar el Fake para que siga compilando (ripple obligatorio de Task 1)**

```csharp
// tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;

namespace StockApp.Application.Tests.Finanzas.Fakes;

public sealed class ImportacionRepositoryFake : IImportacionRepository
{
    private readonly ResultadoConfirmacionDto _resultadoConfirmar;
    private readonly ResultadoReversionDto _resultadoRevertir;
    private readonly IReadOnlyList<ImportacionHistorialDto> _historial;

    public ConfirmarImportacionDto? DtoRecibido { get; private set; }
    public int? UsuarioIdRecibido { get; private set; }
    public Guid? IdImportacionRevertidaRecibida { get; private set; }
    public int VecesConfirmarLlamado { get; private set; }
    public int VecesRevertirLlamado { get; private set; }
    public int VecesListarHistorialLlamado { get; private set; }

    public ImportacionRepositoryFake(
        ResultadoConfirmacionDto? resultadoConfirmar = null,
        ResultadoReversionDto? resultadoRevertir = null,
        IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        _resultadoConfirmar = resultadoConfirmar
            ?? new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        _resultadoRevertir = resultadoRevertir
            ?? new ResultadoReversionDto(Guid.NewGuid(), 0, 0, 0, 0, 0);
        _historial = historial ?? new List<ImportacionHistorialDto>();
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

    public Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        VecesListarHistorialLlamado++;
        return Task.FromResult(_historial);
    }
}
```

- [ ] **Step 2: Escribir el test que falla**

```csharp
// tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Finanzas;
using StockApp.Application.Interfaces;
using StockApp.Application.Tests.Finanzas.Fakes;
using StockApp.Domain.Enums;
using Xunit;
using IAuthSvc = StockApp.Application.Authorization.IAuthorizationService;

namespace StockApp.Application.Tests.Finanzas;

public class ConfirmacionImportacionServiceHistorialTests
{
    private static (ConfirmacionImportacionService Svc, ImportacionRepositoryFake Repo) Crear(
        RolUsuario rol, IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        var proveedoresRepo = new ProveedorRepositoryFake(new List<StockApp.Domain.Entities.Proveedor>());
        var rubrosRepo = new RubroGastoRepositoryFake(new List<StockApp.Domain.Entities.RubroGasto>());
        var fuentesRepo = new FuenteFinanciamientoRepositoryFake(new List<StockApp.Domain.Entities.FuenteFinanciamiento>());
        var lineasPoaRepo = new LineaPoaRepositoryStubFake(new List<StockApp.Domain.Entities.LineaPoa>());
        var importacionRepo = new ImportacionRepositoryFake(historial: historial);

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var auth = new Mock<IAuthSvc>();
        auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.ImportarPlanillas))
            .Throws<UnauthorizedAccessException>();

        var svc = new ConfirmacionImportacionService(
            importacionRepo, proveedoresRepo, rubrosRepo, fuentesRepo, lineasPoaRepo, session.Object, auth.Object);

        return (svc, importacionRepo);
    }

    [Fact]
    public async Task ListarHistorialAsync_Operador_LanzaUnauthorized()
    {
        var (svc, repo) = Crear(RolUsuario.Operador);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => svc.ListarHistorialAsync());
        Assert.Equal(0, repo.VecesListarHistorialLlamado);
    }

    [Fact]
    public async Task ListarHistorialAsync_Admin_DelegaEnElRepositorioYDevuelveSuResultado()
    {
        var historial = new List<ImportacionHistorialDto>
        {
            new(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
        };
        var (svc, repo) = Crear(RolUsuario.Admin, historial);

        var resultado = await svc.ListarHistorialAsync();

        Assert.Equal(1, repo.VecesListarHistorialLlamado);
        Assert.Same(historial, resultado);
    }
}
```

- [ ] **Step 3: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ConfirmacionImportacionServiceHistorialTests"`
Expected: FAIL con error de compilación — `'IConfirmacionImportacionService' does not contain a definition for 'ListarHistorialAsync'`.

- [ ] **Step 4: Declarar el método en la interfaz**

```csharp
// src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs
namespace StockApp.Application.Finanzas;

public interface IConfirmacionImportacionService
{
    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto);
    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion);

    /// <summary>F5d §3: historial admin-only, mismo permiso que confirmar/revertir.</summary>
    Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync();
}
```

- [ ] **Step 5: Implementar en ConfirmacionImportacionService**

Agregar dentro de la clase, después de `RevertirAsync` y antes de `ValidarAsync`, en `src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs`:

```csharp
    public async Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.ImportarPlanillas);

        return await _importacionRepo.ListarHistorialAsync();
    }
```

- [ ] **Step 6: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ConfirmacionImportacionServiceHistorialTests"`
Expected: PASS (2/2). También correr `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~ConfirmacionImportacionServiceTests"` para confirmar que el ripple del Fake no rompió los tests existentes.
Expected: PASS (todos los existentes siguen verdes).

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Application/Finanzas/IConfirmacionImportacionService.cs \
        src/StockApp.Application/Finanzas/ConfirmacionImportacionService.cs \
        tests/StockApp.Application.Tests/Finanzas/Fakes/ImportacionRepositoryFake.cs \
        tests/StockApp.Application.Tests/Finanzas/ConfirmacionImportacionServiceHistorialTests.cs
git commit -m "feat(finanzas): ListarHistorialAsync en el servicio de aplicación del importador"
```

---

### Task 3: Endpoint `GET /finanzas/importar/historial`

**Files:**
- Modify: `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs`
- Test: `tests/StockApp.Api.Tests/ImportacionHistorialEndpointTests.cs`

**Interfaces:**
- Consumes: `IConfirmacionImportacionService.ListarHistorialAsync()` (Task 2); `Permisos.ImportarPlanillas`.
- Produces: `GET /finanzas/importar/historial` → `200 OK` con `IReadOnlyList<ImportacionHistorialDto>` — usado por Task 4 (`ImportacionApiClient.ListarHistorialAsync`).

- [ ] **Step 1: Escribir el test que falla (matriz 401/403/200)**

```csharp
// tests/StockApp.Api.Tests/ImportacionHistorialEndpointTests.cs
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

public class ImportacionHistorialEndpointTests : ApiTestBase
{
    private const int Ejercicio = 2026;

    public ImportacionHistorialEndpointTests(ApiFactory factory) : base(factory) { }

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

    private static ConfirmarImportacionDto PayloadMinimo(bool forzar = false) => new(
        Ejercicio: Ejercicio,
        Forzar: forzar,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task GetHistorial_SinToken_Devuelve401()
    {
        var client = Factory.CreateClient();

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ComoOperador_Devuelve403()
    {
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistorial_ComoAdmin_SinImportaciones_Devuelve200YListaVacia()
    {
        var client = ClienteAutenticado(TokenAdmin());

        var response = await client.GetAsync("/finanzas/importar/historial");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var historial = await response.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        Assert.NotNull(historial);
        Assert.Empty(historial!);
    }

    [Fact]
    public async Task GetHistorial_ComoAdmin_ConImportacionConfirmadaYRevertida_ReflejaElEstado()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);

        var client = ClienteAutenticado(TokenAdmin());
        var confirmacion = await client.PostAsJsonAsync("/finanzas/importar/confirmar", PayloadMinimo());
        var resultado = await confirmacion.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>();

        var historialActivo = await client.GetAsync("/finanzas/importar/historial");
        var listaActiva = await historialActivo.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        var filaActiva = Assert.Single(listaActiva!);
        Assert.Equal(resultado!.IdImportacion, filaActiva.IdImportacion);
        Assert.False(filaActiva.Revertida);

        await client.PostAsync($"/finanzas/importar/revertir/{resultado.IdImportacion}", null);

        var historialRevertido = await client.GetAsync("/finanzas/importar/historial");
        var listaRevertida = await historialRevertido.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>();
        var filaRevertida = Assert.Single(listaRevertida!);
        Assert.True(filaRevertida.Revertida);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionHistorialEndpointTests"`
Expected: FAIL — `404 Not Found` en vez de `401/403/200` (la ruta todavía no existe).

- [ ] **Step 3: Mapear el endpoint**

Agregar dentro de `MapImportacionEndpoints`, después del `MapPost` de `/revertir/{id:guid}` y antes del `return app;`, en `src/StockApp.Api/Endpoints/ImportacionEndpoints.cs`:

```csharp
        app.MapGet("/finanzas/importar/historial", async (IConfirmacionImportacionService confirmacion) =>
        {
            var resultado = await confirmacion.ListarHistorialAsync();
            return Results.Ok(resultado);
        })
        .RequireAuthorization(Permisos.ImportarPlanillas);
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~ImportacionHistorialEndpointTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Api/Endpoints/ImportacionEndpoints.cs \
        tests/StockApp.Api.Tests/ImportacionHistorialEndpointTests.cs
git commit -m "feat(finanzas): endpoint GET /finanzas/importar/historial"
```

---

### Task 4: ApiClient — `IImportacionService` / `ImportacionApiClient`

**Files:**
- Create: `src/StockApp.Application/Finanzas/IImportacionService.cs`
- Create: `src/StockApp.ApiClient/ImportacionApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs`

**Interfaces:**
- Consumes: endpoints de Tasks 3 (F5c: `POST /finanzas/importar/analizar|confirmar|revertir/{id}`, F5d: `GET /finanzas/importar/historial`); `ApiErrores.EnviarAsync`/`AsegurarExitoAsync`; `ResultadoAnalisisDto`/`ConfirmarImportacionDto`/`ResultadoConfirmacionDto`/`ResultadoReversionDto`/`ImportacionHistorialDto`.
- Produces: `IImportacionService` con `AnalizarAsync`, `ConfirmarAsync`, `RevertirAsync`, `ListarHistorialAsync` — usado por Tasks 5-9 (VMs del wizard/historial).

Este task hace 4 ciclos TDD cortos (uno por método), en orden de complejidad creciente, cada uno con su propio commit. La interfaz completa se declara una sola vez al principio (Step 1 — un contrato no es testeable por sí mismo) y la clase queda SIEMPRE compilando: los métodos que todavía no llegaron a su ciclo usan `throw new NotImplementedException()` temporalmente, reemplazado en el ciclo correspondiente — al final del Task no queda ningún `NotImplementedException`.

- [ ] **Step 1: Declarar la interfaz completa**

```csharp
// src/StockApp.Application/Finanzas/IImportacionService.cs
namespace StockApp.Application.Finanzas;

/// <summary>
/// Contrato único del cliente de escritorio contra los 4 endpoints del importador (F5b/F5c
/// análisis+confirmación+reversa, F5d historial). A diferencia del servidor (IAnalisisImportacionService
/// + IConfirmacionImportacionService separados), acá se unifica en UNA interfaz porque el
/// wizard de la UI consume las 4 operaciones desde el mismo flujo.
/// </summary>
public interface IImportacionService
{
    Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio);

    Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto);

    Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion);

    Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync();
}
```

- [ ] **Step 2: Escribir el test que falla para `ListarHistorialAsync` (el más simple: GET sin params)**

```csharp
// tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class ImportacionApiClientTests
{
    [Fact]
    public async Task ListarHistorialAsync_GETParseaListaJson()
    {
        var dtos = new[]
        {
            new ImportacionHistorialDto(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
        };
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("finanzas/importar/historial", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dtos);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ListarHistorialAsync();

        Assert.Single(resultado);
        Assert.Equal("admin", resultado[0].Usuario);
    }
}
```

- [ ] **Step 3: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ImportacionApiClientTests"`
Expected: FAIL — `'ImportacionApiClient' could not be found` (la clase no existe todavía).

- [ ] **Step 4: Crear la clase con `ListarHistorialAsync` real y el resto stub**

```csharp
// src/StockApp.ApiClient/ImportacionApiClient.cs
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using StockApp.Application.Finanzas;

namespace StockApp.ApiClient;

/// <summary>
/// IImportacionService contra /finanzas/importar/*. Sin registros Wire: los DTOs de
/// StockApp.Application.Finanzas ya son la forma de wire (mismo criterio que
/// FinanzasVistasApiClient) — no hace falta remapear.
/// </summary>
public sealed class ImportacionApiClient : IImportacionService
{
    private readonly HttpClient _http;

    public ImportacionApiClient(HttpClient http) => _http = http;

    public Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio) => throw new NotImplementedException();

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
        => throw new NotImplementedException();

    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
        => throw new NotImplementedException();

    public async Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("finanzas/importar/historial"));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<List<ImportacionHistorialDto>>() ?? new();
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ImportacionApiClientTests"`
Expected: PASS (1/1).

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Application/Finanzas/IImportacionService.cs \
        src/StockApp.ApiClient/ImportacionApiClient.cs \
        tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs
git commit -m "feat(finanzas): IImportacionService + ImportacionApiClient.ListarHistorialAsync"
```

- [ ] **Step 7: Escribir el test que falla para `RevertirAsync` (POST sin body, con Guid en la ruta)**

Agregar a `tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs`:

```csharp
    [Fact]
    public async Task RevertirAsync_POSTConIdEnLaRuta_ParseaResultado()
    {
        var id = Guid.NewGuid();
        var dto = new ResultadoReversionDto(id, 2, 1, 1, 0, 0);
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal($"finanzas/importar/revertir/{id}", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.RevertirAsync(id);

        Assert.Equal(2, resultado.GastosRevertidos);
    }
```

- [ ] **Step 8: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~RevertirAsync_POSTConIdEnLaRuta"`
Expected: FAIL — `System.NotImplementedException`.

- [ ] **Step 9: Implementar `RevertirAsync`**

Reemplazar el stub en `src/StockApp.ApiClient/ImportacionApiClient.cs`:

```csharp
    public async Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync($"finanzas/importar/revertir/{idImportacion}", null));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoReversionDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al revertir la importación.");
    }
```

- [ ] **Step 10: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ImportacionApiClientTests"`
Expected: PASS (2/2).

- [ ] **Step 11: Commit**

```bash
git add src/StockApp.ApiClient/ImportacionApiClient.cs tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs
git commit -m "feat(finanzas): ImportacionApiClient.RevertirAsync"
```

- [ ] **Step 12: Escribir el test que falla para `ConfirmarAsync` (POST con JSON body)**

Agregar a `tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs`:

```csharp
    private static ConfirmarImportacionDto PayloadMinimo() => new(
        Ejercicio: 2026,
        Forzar: false,
        MaestrosNuevos: new MaestrosNuevosConfirmarDto(
            new List<string>(), new List<string>(), new List<RubroNuevoConfirmarDto>()),
        Ingresos: new List<IngresoConfirmarDto>(),
        Gastos: new List<GastoConfirmarDto>(),
        LineasPoa: new List<LineaPoaConfirmarDto>());

    [Fact]
    public async Task ConfirmarAsync_POSTConJson_ParseaResultado()
    {
        var idImportacion = Guid.NewGuid();
        var dto = new ResultadoConfirmacionDto(
            idImportacion, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/importar/confirmar", request.RequestUri!.PathAndQuery.TrimStart('/'));
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.ConfirmarAsync(PayloadMinimo());

        Assert.Equal(idImportacion, resultado.IdImportacion);
        Assert.Equal(1, resultado.ProveedoresCreados);
    }
```

- [ ] **Step 13: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ConfirmarAsync_POSTConJson"`
Expected: FAIL — `System.NotImplementedException`.

- [ ] **Step 14: Implementar `ConfirmarAsync`**

Reemplazar el stub en `src/StockApp.ApiClient/ImportacionApiClient.cs`:

```csharp
    public async Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync("finanzas/importar/confirmar", dto));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoConfirmacionDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al confirmar la importación.");
    }
```

- [ ] **Step 15: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ImportacionApiClientTests"`
Expected: PASS (3/3).

- [ ] **Step 16: Commit**

```bash
git add src/StockApp.ApiClient/ImportacionApiClient.cs tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs
git commit -m "feat(finanzas): ImportacionApiClient.ConfirmarAsync"
```

- [ ] **Step 17: Escribir el test que falla para `AnalizarAsync` (multipart con 2 archivos + ejercicio)**

Agregar a `tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs`:

```csharp
    [Fact]
    public async Task AnalizarAsync_EnviaMultipartConDosArchivosYEjercicio_ParseaResultado()
    {
        var dto = new ResultadoAnalisisDto(
            Ingresos: new List<IngresoAnalizadoDto>(),
            Gastos: new List<GastoAnalizadoDto>(),
            LineasPoa: new List<LineaPoaAnalizadaDto>(),
            MaestrosNuevos: new MaestrosNuevosDto(
                new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            Resumen: new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            SaldosPoa: new SaldosTotalesPoaOds(0m, 0m));
        var fake = new FakeHttpHandler(request =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("finanzas/importar/analizar", request.RequestUri!.PathAndQuery.TrimStart('/'));
            Assert.IsType<MultipartFormDataContent>(request.Content);
            return TestHttp.Json(dto);
        });
        var client = new ImportacionApiClient(TestHttp.CrearCliente(fake));

        var resultado = await client.AnalizarAsync(
            "gastos.ods", new byte[] { 1, 2, 3 }, "poa.ods", new byte[] { 4, 5, 6 }, 2026);

        Assert.Equal(0, resultado.Resumen.TotalFilas);
    }
```

- [ ] **Step 18: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~AnalizarAsync_EnviaMultipart"`
Expected: FAIL — `System.NotImplementedException`. (`SaldosTotalesPoaOds(decimal SaldoLiteralB, decimal SaldoLiteralC)` está definido en `src/StockApp.Application/Finanzas/PlanillaOdsDtos.cs:66` — verificado contra el código real; `new SaldosTotalesPoaOds(0m, 0m)` en el test de arriba es la forma posicional correcta.)

- [ ] **Step 19: Implementar `AnalizarAsync`**

Reemplazar el stub en `src/StockApp.ApiClient/ImportacionApiClient.cs`:

```csharp
    public async Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds,
        string nombreArchivoPoa, byte[] poaOds,
        int ejercicio)
    {
        using var multipart = new MultipartFormDataContent();

        using var archivoGastos = new ByteArrayContent(gastosOds);
        archivoGastos.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoGastos, "gastos", nombreArchivoGastos);

        using var archivoPoa = new ByteArrayContent(poaOds);
        archivoPoa.Headers.ContentType =
            new MediaTypeHeaderValue("application/vnd.oasis.opendocument.spreadsheet");
        multipart.Add(archivoPoa, "poa", nombreArchivoPoa);

        multipart.Add(
            new StringContent(ejercicio.ToString(CultureInfo.InvariantCulture)), "ejercicio");

        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsync("finanzas/importar/analizar", multipart));
        await ApiErrores.AsegurarExitoAsync(response);

        return await response.Content.ReadFromJsonAsync<ResultadoAnalisisDto>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al analizar la importación.");
    }
```

- [ ] **Step 20: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~ImportacionApiClientTests"`
Expected: PASS (4/4).

- [ ] **Step 21: Registrar en DI**

Agregar en `src/StockApp.Presentation/App.axaml.cs`, junto al bloque `// ── Módulo Finanzas — Fase 2: gastos e ingresos de caja ──` (después de `services.AddTransient<IAdjuntoService, AdjuntoApiClient>();`):

```csharp
        // ── Módulo Finanzas — F5d: importador de planillas (historial + análisis/confirmación/reversa) ──
        services.AddTransient<IImportacionService, ImportacionApiClient>();
```

- [ ] **Step 22: Commit**

```bash
git add src/StockApp.ApiClient/ImportacionApiClient.cs \
        tests/StockApp.ApiClient.Tests/ImportacionApiClientTests.cs \
        src/StockApp.Presentation/App.axaml.cs
git commit -m "feat(finanzas): ImportacionApiClient.AnalizarAsync + registro DI de IImportacionService"
```

---

### Task 5: Selector de archivo `.ods`

**Files:**
- Modify: `src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs`
- Modify: `src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs`

**Interfaces:**
- Produces: `IServicioSeleccionArchivo.SeleccionarArchivoOdsAsync(): Task<(string NombreArchivo, byte[] Contenido)?>` — usado por Task 7 (Paso 1 del wizard).

Sin test propio: `ServicioSeleccionArchivo` es UI real (`IStorageProvider`) y el propio comentario de la interfaz ya documenta que no se testea unitariamente ("es UI"; en headless devuelve null de forma segura) — mismo criterio que `SeleccionarArchivoAsync()` existente. Se agrega un método NUEVO (no se toca la firma de `SeleccionarArchivoAsync()`) para no romper `AdjuntosPanelViewModel` ni sus tests.

- [ ] **Step 1: Agregar el método a la interfaz**

```csharp
// src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs
using System.Threading.Tasks;

namespace StockApp.Presentation.Services;

public interface IServicioSeleccionArchivo
{
    /// <summary>
    /// Muestra el selector de archivo filtrando por PDF/JPG/PNG. Devuelve el nombre y los
    /// bytes leídos, o null si el usuario canceló.
    /// </summary>
    Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync();

    /// <summary>
    /// F5d: selector de archivo filtrando por .ods (OpenDocument Spreadsheet), para elegir las
    /// planillas de Gastos/POA del importador. Mismo contrato que SeleccionarArchivoAsync().
    /// </summary>
    Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoOdsAsync();
}
```

- [ ] **Step 2: Implementar en ServicioSeleccionArchivo**

```csharp
// src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Services;

public class ServicioSeleccionArchivo : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<(string, byte[])?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => SeleccionarInternoAsync(FiltroDocumentosEImagenes()));
    }

    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoOdsAsync()
    {
        if (AvaloniaApp.Current is null)
            return Task.FromResult<(string, byte[])?>(null);

        return Dispatcher.UIThread.InvokeAsync(() => SeleccionarInternoAsync(FiltroOds()));
    }

    private static FilePickerFileType FiltroDocumentosEImagenes() => new("Documentos e imágenes")
    {
        Patterns = new[] { "*.pdf", "*.jpg", "*.jpeg", "*.png" },
        MimeTypes = new[] { "application/pdf", "image/jpeg", "image/png" },
    };

    private static FilePickerFileType FiltroOds() => new("Planillas OpenDocument")
    {
        Patterns = new[] { "*.ods" },
        MimeTypes = new[] { "application/vnd.oasis.opendocument.spreadsheet" },
    };

    private static async Task<(string, byte[])?> SeleccionarInternoAsync(FilePickerFileType filtro)
    {
        var lifetime = AvaloniaApp.Current?.ApplicationLifetime
            as IClassicDesktopStyleApplicationLifetime;

        var storageProvider = lifetime?.MainWindow?.StorageProvider;
        if (storageProvider is null)
            return null;

        var archivos = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType> { filtro },
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

- [ ] **Step 3: Verificar que el proyecto sigue compilando**

Run: `dotnet build src/StockApp.Presentation`
Expected: Build succeeded (0 errores). `AdjuntosPanelViewModel` sigue usando `SeleccionarArchivoAsync()` sin cambios de firma.

- [ ] **Step 4: Commit**

```bash
git add src/StockApp.Presentation/Services/IServicioSeleccionArchivo.cs \
        src/StockApp.Presentation/Services/ServicioSeleccionArchivo.cs
git commit -m "feat(finanzas): selector de archivo .ods para el importador de planillas"
```

---

### Task 6: Sidebar + pantalla contenedora con 2 tabs

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/ImportacionViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ImportacionViewModelTests.cs`
- Test: Modify `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs`

**Interfaces:**
- Consumes: `INavigationService.Navegar<TVm>()`; `HistorialImportacionesViewModel`/`NuevaImportacionViewModel` (Tasks 7-9, se resuelven por DI — este task solo referencia los TIPOS, ambos VMs se registran en Tasks 7/8/9).
- Produces: `ImportacionViewModel(HistorialVm, NuevaVm)` con propiedades `HistorialVm`/`NuevaVm` — consumido por `ImportacionView.axaml` (tabs).

- [ ] **Step 1: Escribir el test que falla para la navegación admin (`ShellMainViewModel`)**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs`, junto a los tests de `NavMaestrosFinanzasCommand`:

```csharp
    [Fact]
    public void NavImportacion_LlamaNavegar_AImportacionViewModel()
    {
        var (vm, _, navMock, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        navMock.Verify(n => n.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>(), Times.Once);
    }

    [Fact]
    public void NavImportacion_EstableceSeccionActiva_Importacion()
    {
        var (vm, _, _, _) = Crear(RolUsuario.Admin);

        vm.NavImportacionCommand.Execute(null);

        Assert.Equal("Importacion", vm.SeccionActiva);
    }
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NavImportacion"`
Expected: FAIL — `'ShellMainViewModel' does not contain a definition for 'NavImportacionCommand'` (error de compilación).

- [ ] **Step 3: Agregar el comando de navegación**

Agregar en `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`, después de `NavCalendarioPagos` (antes de la región "Cerrar sesión"):

```csharp
    [RelayCommand]
    private void NavImportacion()
    {
        SeccionActiva = "Importacion";
        _navigation.Navegar<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>();
    }
```

- [ ] **Step 4: Escribir el ViewModel contenedor (sin test unitario propio — solo expone 2 propiedades resueltas por constructor, cubierto por Task 5's DI wiring y la aserción de tipo del Step 1)**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/ImportacionViewModel.cs
namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Pantalla "Importar planillas" (F5d): hostea las 2 sub-listas (Nueva importación, Historial)
/// en tabs, mismo patrón que MaestrosFinanzasViewModel.
/// </summary>
public partial class ImportacionViewModel : ViewModelBase
{
    public NuevaImportacionViewModel NuevaVm { get; }
    public HistorialImportacionesViewModel HistorialVm { get; }

    public ImportacionViewModel(NuevaImportacionViewModel nuevaVm, HistorialImportacionesViewModel historialVm)
    {
        NuevaVm = nuevaVm;
        HistorialVm = historialVm;
    }
}
```

- [ ] **Step 5: Escribir el test del contenedor**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ImportacionViewModelTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class ImportacionViewModelTests
{
    [Fact]
    public void Constructor_ExponeAmbosSubVms()
    {
        var servicio = Mock.Of<IImportacionService>();
        var seleccion = Mock.Of<IServicioSeleccionArchivo>();
        var confirmacion = Mock.Of<IConfirmacionService>();

        var nuevaVm = new NuevaImportacionViewModel(servicio, seleccion, confirmacion);
        var historialVm = new HistorialImportacionesViewModel(servicio, confirmacion);

        var vm = new ImportacionViewModel(nuevaVm, historialVm);

        Assert.Same(nuevaVm, vm.NuevaVm);
        Assert.Same(historialVm, vm.HistorialVm);
    }
}
```

Nota: este test compila recién cuando `NuevaImportacionViewModel` (Task 7) y `HistorialImportacionesViewModel` (Task 8... revisar: en este plan `HistorialImportacionesViewModel` es Task 7 y el wizard es Tasks 8/9/10 — ver numeración real de tasks más abajo) existan con esos constructores. Este Step queda escrito ahora como parte de la interfaz consumida por Task 6, pero el archivo de test se completa recién al final de Task 8 (cuando ambos VMs referenciados ya compilan). Ver Step 6.

- [ ] **Step 6: Diferir la corrida de este test hasta que compile**

No correr `ImportacionViewModelTests` todavía — `NuevaImportacionViewModel`/`HistorialImportacionesViewModel` (Tasks 7/8) no existen aún. Verificar en cambio que el resto del proyecto Presentation compila con el `ImportacionViewModel` recién creado usando una compilación aislada:

Run: `dotnet build src/StockApp.Presentation/StockApp.Presentation.csproj 2>&1 | grep -i "ImportacionViewModel"`
Expected: error esperado y ACEPTADO en este punto — `CS0246: The type or namespace name 'NuevaImportacionViewModel' could not be found` / `'HistorialImportacionesViewModel' could not be found`. Este es el "red" intencional que Task 7 resuelve.

- [ ] **Step 7: Commit del contenedor + sidebar + test de ShellMainViewModel (que SÍ compila y pasa ya)**

XAML del sidebar — agregar en `src/StockApp.Presentation/Views/ShellMainView.axaml`, después del botón `NavCalendarioPagosCommand` y antes de la sección "Tablas maestras: solo Admin":

```xml
                <!-- Importación: solo Admin -->
                <TextBlock Text="Importación"
                           Classes="caption"
                           Foreground="{DynamicResource SidebarTextoBrush}"
                           FontWeight="SemiBold"
                           Margin="8,8,0,4"
                           IsVisible="{Binding EsAdmin}"
                           Opacity="0.6" />

                <Button Command="{Binding NavImportacionCommand}"
                        Classes="ghost"
                        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Importacion}"
                        HorizontalAlignment="Stretch"
                        IsVisible="{Binding EsAdmin}">
                    <Grid ColumnDefinitions="Auto,*">
                        <i:Icon Grid.Column="0" Value="mdi-file-upload" Foreground="{DynamicResource SidebarTextoBrush}" />
                        <TextBlock Grid.Column="1" Text="Importar planillas" VerticalAlignment="Center"
                                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
                    </Grid>
                </Button>

```

Registrar en DI (`src/StockApp.Presentation/App.axaml.cs`), junto a `services.AddTransient<CalendarioPagosViewModel>();`:

```csharp
        services.AddTransient<StockApp.Presentation.ViewModels.Finanzas.ImportacionViewModel>();
```

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NavImportacion"`
Expected: PASS (2/2) — estos SÍ compilan porque no dependen de `ImportacionViewModelTests.cs` (archivo distinto).

```bash
git add src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml \
        src/StockApp.Presentation/ViewModels/Finanzas/ImportacionViewModel.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/ShellMainViewModelTests.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/ImportacionViewModelTests.cs
git commit -m "feat(finanzas): sidebar + pantalla contenedora de Importar planillas (2 tabs)"
```

(`ImportacionView.axaml`/`.axaml.cs` se escriben en Task 9, cuando las 2 sub-vistas ya existen y hay algo real que hostear en el `TabControl`.)

---

### Task 7: Tab Historial — `HistorialImportacionesViewModel`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/HistorialImportacionesViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/HistorialImportacionesViewModelTests.cs`

**Interfaces:**
- Consumes: `IImportacionService.ListarHistorialAsync()`/`RevertirAsync(Guid)` (Task 4); `IConfirmacionService.PreguntarAsync`/`InformarAsync`.
- Produces: `HistorialImportacionesViewModel(IImportacionService, IConfirmacionService)` con `Filas`/`FilasView`/`CargarAsync()`/`RevertirCommand` — consumido por Task 6's `ImportacionViewModel` y por `HistorialImportacionesView.axaml.cs` (`DataContextChanged`).

- [ ] **Step 1: Escribir el test que falla (carga + Revertir habilitado solo en Activas)**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/HistorialImportacionesViewModelTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class HistorialImportacionesViewModelTests
{
    private static (HistorialImportacionesViewModel vm, Mock<IImportacionService> svc, Mock<IConfirmacionService> confirm)
        Crear(IReadOnlyList<ImportacionHistorialDto>? historial = null)
    {
        var svc = new Mock<IImportacionService>();
        svc.Setup(s => s.ListarHistorialAsync()).ReturnsAsync(historial ?? new List<ImportacionHistorialDto>());

        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new HistorialImportacionesViewModel(svc.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_PopulaFilasDesdeElServicio()
    {
        var historial = new List<ImportacionHistorialDto>
        {
            new(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", false),
            new(Guid.NewGuid(), DateTime.UtcNow.AddDays(-1), 2025, "admin", true),
        };
        var (vm, _, _) = Crear(historial);

        await vm.CargarAsync();

        Assert.Equal(2, vm.Filas.Count);
    }

    [Fact]
    public async Task RevertirAsync_FilaActiva_LlamaAlServicioYRecarga()
    {
        var id = Guid.NewGuid();
        var historial = new List<ImportacionHistorialDto> { new(id, DateTime.UtcNow, 2026, "admin", false) };
        var (vm, svc, _) = Crear(historial);
        svc.Setup(s => s.RevertirAsync(id))
            .ReturnsAsync(new ResultadoReversionDto(id, 1, 0, 0, 0, 0));
        await vm.CargarAsync();
        vm.FilaSeleccionada = vm.Filas[0];

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(id), Times.Once);
        svc.Verify(s => s.ListarHistorialAsync(), Times.Exactly(2)); // carga inicial + refresco post-revertir
    }

    [Fact]
    public void PuedeRevertir_FilaYaRevertida_False()
    {
        var (vm, _, _) = Crear();
        vm.FilaSeleccionada = new ImportacionHistorialDto(Guid.NewGuid(), DateTime.UtcNow, 2026, "admin", true);

        Assert.False(vm.RevertirCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~HistorialImportacionesViewModelTests"`
Expected: FAIL — `'HistorialImportacionesViewModel' could not be found` (error de compilación).

- [ ] **Step 3: Implementar el ViewModel**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/HistorialImportacionesViewModel.cs
using System.Collections.ObjectModel;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Tab "Historial" (F5d §5): grilla read-only clásica + Revertir por fila, habilitado solo en
/// filas Activas. DataContextChanged de la View dispara CargarAsync() (mismo patrón que
/// GastosViewModel/AuditoriaLogViewModel).
/// </summary>
public partial class HistorialImportacionesViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IConfirmacionService _confirmacion;

    public ObservableCollection<ImportacionHistorialDto> Filas { get; } = new();

    /// <summary>Envuelve Filas para el ordenamiento por click en encabezados (gotcha Avalonia 12,
    /// mismo criterio que GastosViewModel.FilasView).</summary>
    public DataGridCollectionView FilasView { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertirCommand))]
    private ImportacionHistorialDto? _filaSeleccionada;

    public HistorialImportacionesViewModel(IImportacionService service, IConfirmacionService confirmacion)
    {
        _service = service;
        _confirmacion = confirmacion;

        FilasView = new DataGridCollectionView(Filas);
    }

    public async Task CargarAsync()
    {
        try
        {
            var historial = await _service.ListarHistorialAsync();
            Filas.Clear();
            foreach (var fila in historial)
                Filas.Add(fila);
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private bool PuedeRevertir() => FilaSeleccionada is { Revertida: false };

    [RelayCommand(CanExecute = nameof(PuedeRevertir))]
    private async Task RevertirAsync()
    {
        if (FilaSeleccionada is not { Revertida: false } fila) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma revertir la importación del ejercicio {fila.Ejercicio} " +
            $"({fila.IdImportacion})? Se darán de baja todos los gastos, ingresos y líneas POA que creó.");
        if (!confirmar) return;

        try
        {
            await _service.RevertirAsync(fila.IdImportacion);
            await CargarAsync();
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
}
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~HistorialImportacionesViewModelTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Vista + code-behind**

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="900" d:DesignHeight="600"
             x:Class="StockApp.Presentation.Views.Finanzas.HistorialImportacionesView"
             x:DataType="vm:HistorialImportacionesViewModel">

    <DockPanel Margin="24">

        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,12">
            <Button Classes="secondary" Content="Revertir" Command="{Binding RevertirCommand}" />
        </StackPanel>

        <DataGrid ItemsSource="{Binding FilasView}"
                  SelectedItem="{Binding FilaSeleccionada}"
                  IsReadOnly="True"
                  CanUserSortColumns="True"
                  AutoGenerateColumns="False">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Fecha"
                                    Binding="{Binding Fecha, StringFormat='dd/MM/yyyy HH:mm', DataType={x:Type vm:ImportacionHistorialDto}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Ejercicio"
                                    Binding="{Binding Ejercicio, DataType={x:Type vm:ImportacionHistorialDto}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Usuario"
                                    Binding="{Binding Usuario, DataType={x:Type vm:ImportacionHistorialDto}}"
                                    Width="Auto" />
                <DataGridTextColumn Header="Estado"
                                    Binding="{Binding Revertida, Converter={x:Static BoolConverters.Not}, DataType={x:Type vm:ImportacionHistorialDto}}"
                                    Width="Auto" />
            </DataGrid.Columns>
        </DataGrid>

    </DockPanel>

</UserControl>
```

Nota sobre la columna "Estado": `BoolConverters.Not` invierte `Revertida` (True→False, False→True) para que el `DataGridTextColumn` muestre el booleano invertido; como una lectura textual "Activa"/"Revertida" requiere un converter dedicado, se reemplaza el binding de esa columna por un `DataGridTemplateColumn` con texto condicional:

```xml
                <DataGridTemplateColumn Header="Estado" Width="Auto">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate x:DataType="vm:ImportacionHistorialDto">
                            <TextBlock Text="{Binding Revertida, Converter={x:Static vm:EstadoRevertidaConverter.Instance}}"
                                       VerticalAlignment="Center" Margin="4,0" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
```

Con este converter chico agregado en `HistorialImportacionesViewModel.cs` (mismo archivo, junto a la clase — mismo criterio que `GastoFila` viviendo en `GastosViewModel.cs`):

```csharp
/// <summary>Texto de la columna Estado del historial: "Activa"/"Revertida".</summary>
public sealed class EstadoRevertidaConverter : Avalonia.Data.Converters.IValueConverter
{
    public static readonly EstadoRevertidaConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => value is true ? "Revertida" : "Activa";

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
```

Reemplazar la columna `DataGridTextColumn Header="Estado"` de arriba por el `DataGridTemplateColumn` de esta nota (usar SOLO uno de los dos — el `DataGridTemplateColumn`).

```csharp
// src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class HistorialImportacionesView : UserControl
{
    public HistorialImportacionesView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is HistorialImportacionesViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] **Step 6: Registrar en DI**

Agregar en `src/StockApp.Presentation/App.axaml.cs`, junto al registro de `ImportacionViewModel` (Task 6):

```csharp
        services.AddTransient<HistorialImportacionesViewModel>();
```

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/HistorialImportacionesViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml \
        src/StockApp.Presentation/Views/Finanzas/HistorialImportacionesView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/HistorialImportacionesViewModelTests.cs
git commit -m "feat(finanzas): tab Historial del importador (grilla + Revertir por fila)"
```

---

### Task 8: Wizard — Paso 1 (Cargar) + esqueleto del wizard

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Create: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs`
- Create: `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml.cs` (code-behind faltante de Task 6)
- Modify: `src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml` (crear el XAML real, Task 6 solo dejó el VM)
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`

**Interfaces:**
- Consumes: `IImportacionService.AnalizarAsync(...)` (Task 4); `IServicioSeleccionArchivo.SeleccionarArchivoOdsAsync()` (Task 5); `IConfirmacionService.InformarAsync`.
- Produces: `NuevaImportacionViewModel` con `PasoActual` (`PasoWizardImportacion`), `GastosNombreArchivo`/`PoaNombreArchivo`/`Ejercicio`/`Forzar`, `SeleccionarGastosCommand`/`SeleccionarPoaCommand`/`AnalizarCommand` — consumido por Task 9 (Paso 2) y Task 10 (Paso 3), que agregan miembros a esta MISMA clase.

- [ ] **Step 1: Escribir el test que falla (selección de archivos habilita Analizar)**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
using Moq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class NuevaImportacionViewModelTests
{
    private static ResultadoAnalisisDto ResultadoAnalisisVacio() => new(
        Ingresos: new List<IngresoAnalizadoDto>(),
        Gastos: new List<GastoAnalizadoDto>(),
        LineasPoa: new List<LineaPoaAnalizadaDto>(),
        MaestrosNuevos: new MaestrosNuevosDto(
            new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
        Resumen: new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        SaldosPoa: new SaldosTotalesPoaOds(0m, 0m));

    private static (NuevaImportacionViewModel vm, Mock<IImportacionService> svc,
                    Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm)
        Crear()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new NuevaImportacionViewModel(svc.Object, seleccion.Object, confirm.Object);
        return (vm, svc, seleccion, confirm);
    }

    [Fact]
    public void EstadoInicial_PasoActualEsCargar()
    {
        var (vm, _, _, _) = Crear();

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
    }

    [Fact]
    public void AnalizarCommand_SinArchivosSeleccionados_NoPuedeEjecutar()
    {
        var (vm, _, _, _) = Crear();

        Assert.False(vm.AnalizarCommand.CanExecute(null));
    }

    [Fact]
    public async Task SeleccionarGastosYPoa_HabilitaAnalizar()
    {
        var (vm, _, seleccion, _) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));

        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        Assert.True(vm.AnalizarCommand.CanExecute(null));
        Assert.Equal("gastos.ods", vm.GastosNombreArchivo);
        Assert.Equal("poa.ods", vm.PoaNombreArchivo);
    }

    [Fact]
    public async Task AnalizarAsync_ConExito_AvanzaAPasoRevisar()
    {
        var (vm, svc, seleccion, _) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                "gastos.ods", It.IsAny<byte[]>(), "poa.ods", It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(ResultadoAnalisisVacio());
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
    }

    [Fact]
    public async Task AnalizarAsync_ElServidorFalla_InformaYNoAvanzaDePaso()
    {
        var (vm, svc, seleccion, confirm) = Crear();
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ThrowsAsync(new ArgumentException("El archivo no es un .ods válido."));
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);

        await vm.AnalizarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
        confirm.Verify(c => c.InformarAsync("El archivo no es un .ods válido."), Times.Once);
    }
}
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `'NuevaImportacionViewModel' could not be found` (error de compilación).

- [ ] **Step 3: Implementar el ViewModel (esqueleto del wizard + Paso 1 completo)**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Paso actual del wizard de importación (F5d §5).</summary>
public enum PasoWizardImportacion { Cargar, Revisar, Resultado }

/// <summary>
/// Tab "Nueva importación" (F5d §5): wizard de 3 pasos como UNA sola VM con estado PasoActual —
/// las 3 vistas de paso comparten DataContext con esta VM y alternan visibilidad por PasoActual.
/// Este task cubre el esqueleto + Paso 1 (Cargar); Paso 2/3 se agregan en Tasks 9/10 sobre esta
/// MISMA clase (misma convención que GastoFila embebido en GastosViewModel.cs).
/// </summary>
public partial class NuevaImportacionViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    private PasoWizardImportacion _pasoActual = PasoWizardImportacion.Cargar;

    // ── Paso 1: Cargar ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _gastosNombreArchivo;
    private byte[]? _gastosContenido;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _poaNombreArchivo;
    private byte[]? _poaContenido;

    [ObservableProperty]
    private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    private bool _forzar;

    public NuevaImportacionViewModel(
        IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion)
    {
        _service = service;
        _seleccion = seleccion;
        _confirmacion = confirmacion;
    }

    [RelayCommand]
    private async Task SeleccionarGastosAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (GastosNombreArchivo, _gastosContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SeleccionarPoaAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (PoaNombreArchivo, _poaContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeAnalizar() => _gastosContenido is not null && _poaContenido is not null;

    [RelayCommand(CanExecute = nameof(PuedeAnalizar))]
    private async Task AnalizarAsync()
    {
        try
        {
            var analisis = await _service.AnalizarAsync(
                GastosNombreArchivo!, _gastosContenido!, PoaNombreArchivo!, _poaContenido!, Ejercicio);

            CargarAnalisis(analisis);
            PasoActual = PasoWizardImportacion.Revisar;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>Placeholder de Task 9: puebla las colecciones del Paso 2. Se reemplaza por la
    /// implementación real en Task 9 — DEBE quedar reemplazado antes de cerrar ese task, no es
    /// un placeholder permanente de este plan.</summary>
    partial void CargarAnalisisPaso2(ResultadoAnalisisDto analisis);

    private void CargarAnalisis(ResultadoAnalisisDto analisis) => CargarAnalisisPaso2(analisis);
}
```

Nota de diseño: `CargarAnalisisPaso2` se declara como `partial void` sin cuerpo (válido en C#: una `partial void` sin implementación se compila como no-op) ÚNICAMENTE para que este Step 3 compile de forma autocontenida sin adelantar código del Paso 2. Task 9 la reemplaza por un método completo con cuerpo real (a partir de ahí deja de ser `partial`) — al cierre de Task 9 no queda ningún `partial void` vacío en el archivo.

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Vista del wizard (Paso 1 visible, Pasos 2/3 se agregan en Tasks 9/10 al mismo archivo)**

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.NuevaImportacionView"
             x:DataType="vm:NuevaImportacionViewModel">

    <Grid Margin="24">

        <!-- Paso 1: Cargar -->
        <StackPanel Spacing="12"
                    IsVisible="{Binding PasoActual, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static vm:PasoWizardImportacion.Cargar}}">
            <TextBlock Text="Paso 1 · Cargar planillas" Classes="titulo-vista" />

            <Border Classes="card">
                <StackPanel Spacing="12">
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Classes="secondary" Content="Elegir planilla de Gastos..." Command="{Binding SeleccionarGastosCommand}" />
                        <TextBlock Text="{Binding GastosNombreArchivo}" VerticalAlignment="Center" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <Button Classes="secondary" Content="Elegir planilla POA..." Command="{Binding SeleccionarPoaCommand}" />
                        <TextBlock Text="{Binding PoaNombreArchivo}" VerticalAlignment="Center" />
                    </StackPanel>
                    <StackPanel Orientation="Horizontal" Spacing="8">
                        <TextBlock Text="Ejercicio" VerticalAlignment="Center" />
                        <NumericUpDown Value="{Binding Ejercicio}" Minimum="2000" Maximum="2100" FormatString="0" Width="140" />
                    </StackPanel>
                    <CheckBox Content="Forzar (re-importar un ejercicio ya importado)" IsChecked="{Binding Forzar}" />
                    <Button Classes="primary" Content="Analizar" Command="{Binding AnalizarCommand}" />
                </StackPanel>
            </Border>
        </StackPanel>

    </Grid>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class NuevaImportacionView : UserControl
{
    public NuevaImportacionView() => InitializeComponent();
}
```

Nota: `NuevaImportacionView` NO necesita `DataContextChanged` (a diferencia de `GastosView`/`HistorialImportacionesView`) porque el Paso 1 no carga datos del servidor al entrar — la primera llamada real (`AnalizarAsync`) la dispara el usuario con el botón "Analizar".

- [ ] **Step 6: Completar `ImportacionView.axaml`/`.axaml.cs` (Task 6 dejó solo el VM contenedor)**

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:views="using:StockApp.Presentation.Views.Finanzas"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.ImportacionView"
             x:DataType="vm:ImportacionViewModel">

    <DockPanel Margin="24">

        <TextBlock DockPanel.Dock="Top"
                   Text="Importar planillas"
                   Classes="titulo-vista"
                   Margin="0,0,0,16" />

        <TabControl>
            <TabItem Header="Nueva importación">
                <views:NuevaImportacionView DataContext="{Binding NuevaVm}" />
            </TabItem>
            <TabItem Header="Historial">
                <views:HistorialImportacionesView DataContext="{Binding HistorialVm}" />
            </TabItem>
        </TabControl>

    </DockPanel>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml.cs
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Finanzas;

public partial class ImportacionView : UserControl
{
    public ImportacionView() => InitializeComponent();
}
```

- [ ] **Step 7: Registrar en DI**

Agregar en `src/StockApp.Presentation/App.axaml.cs`, junto al registro de `HistorialImportacionesViewModel` (Task 7):

```csharp
        services.AddTransient<NuevaImportacionViewModel>();
```

- [ ] **Step 8: Correr el test diferido de Task 6 (ahora SÍ compila: ambos sub-VMs existen)**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~ImportacionViewModelTests"`
Expected: PASS (1/1).

- [ ] **Step 9: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs \
        src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml \
        src/StockApp.Presentation/Views/Finanzas/ImportacionView.axaml.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): wizard de importación — esqueleto + Paso 1 (Cargar)"
```

---

### Task 9: Wizard — Paso 2 (Revisar, solo lectura con color) + Confirmar

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Create: `src/StockApp.Presentation/Converters/EstadoFilaBrushConverter.cs`
- Test: Modify `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`

**Interfaces:**
- Consumes: `ResultadoAnalisisDto` (Gastos/Ingresos/LineasPoa/MaestrosNuevos/Resumen, Task 4); `IImportacionService.ConfirmarAsync(ConfirmarImportacionDto)`.
- Produces: `NuevaImportacionViewModel.GastosAnalizados`/`IngresosAnalizados`/`LineasPoaAnalizadas`/`ProveedoresNuevos`/`FuentesNuevas`/`RubrosNuevos`/`Resumen`/`PuedeConfirmar`/`ConfirmarCommand` — consumido por Task 10 (Paso 3, que lee `ResultadoConfirmacion` producido por `ConfirmarAsync`).

- [ ] **Step 1: Escribir el converter de color de fila (sin test unitario — mismo criterio documentado que SignoNegativoBrushConverter: sin Application.Current disponible en Presentation.Tests, se prueba indirectamente por el fallback)**

```csharp
// src/StockApp.Presentation/Converters/EstadoFilaBrushConverter.cs
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using StockApp.Application.Finanzas;
using AvaloniaApp = Avalonia.Application;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Colorea la fila de las grillas del Paso 2 del wizard (F5d §5) por EstadoFila: rojo Error,
/// amarillo Advertencia, sin color (hereda el fondo) para Ok. Mismo criterio de fallback que
/// SignoNegativoBrushConverter: sin Application.Current (StockApp.Presentation.Tests no tiene
/// infraestructura Avalonia Headless) se usa el espejo hardcodeado del token.
/// </summary>
public sealed class EstadoFilaBrushConverter : IValueConverter
{
    public static readonly EstadoFilaBrushConverter Instance = new();

    private const string TokenError = "DangerBrush";
    private const string TokenAdvertencia = "WarningBrush";

    private static readonly IBrush FallbackError = new ImmutableSolidColorBrush(Color.Parse("#DC2626"));
    private static readonly IBrush FallbackAdvertencia = new ImmutableSolidColorBrush(Color.Parse("#D97706"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not EstadoFila estado || estado == EstadoFila.Ok)
            return AvaloniaProperty.UnsetValue;

        var token = estado == EstadoFila.Error ? TokenError : TokenAdvertencia;
        var fallback = estado == EstadoFila.Error ? FallbackError : FallbackAdvertencia;

        if (AvaloniaApp.Current is { } app && app.TryFindResource(token, out var recurso) && recurso is IBrush brush)
            return brush;

        return fallback;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
```

- [ ] **Step 2: Escribir los tests que fallan (mapeo del análisis a filas + gating de Confirmar)**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`:

```csharp
    private static async Task<NuevaImportacionViewModel> CrearEnPasoRevisarAsync(
        Mock<IImportacionService> svc, Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
        ResultadoAnalisisDto analisis)
    {
        seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
            .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
            .ReturnsAsync(("poa.ods", new byte[] { 2 }));
        svc.Setup(s => s.AnalizarAsync(
                It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
            .ReturnsAsync(analisis);

        var vm = new NuevaImportacionViewModel(svc.Object, seleccion.Object, confirm.Object);
        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        await vm.AnalizarCommand.ExecuteAsync(null);
        return vm;
    }

    [Fact]
    public async Task Analizar_PopulaLasGrillasDelPaso2()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, "Literal A", false, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.Single(vm.GastosAnalizados);
        Assert.Equal("ACME SA", vm.GastosAnalizados[0].Proveedor);
    }

    [Fact]
    public async Task Resumen_ConErrores_ConfirmarQuedaDeshabilitado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(1, 0, 0, 1, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.False(vm.PuedeConfirmar);
        Assert.False(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task Resumen_SoloAdvertencias_ConfirmarQuedaHabilitado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
        };

        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);

        Assert.True(vm.PuedeConfirmar);
        Assert.True(vm.ConfirmarCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConfirmarAsync_AnalisisLimpio_MapeaGastoContadoYAvanzaAResultado()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compra de insumos", null, "Literal A", false, 1, "Materiales", false, null),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        var resultadoConfirmacion = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 1, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        ConfirmarImportacionDto? dtoCapturado = null;
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .Callback<ConfirmarImportacionDto>(dto => dtoCapturado = dto)
            .ReturnsAsync(resultadoConfirmacion);

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Resultado, vm.PasoActual);
        Assert.NotNull(dtoCapturado);
        var gasto = Assert.Single(dtoCapturado!.Gastos);
        Assert.Equal(CondicionPago.Contado, gasto.Condicion);
        Assert.Null(gasto.FechaVencimiento);
    }

    [Fact]
    public async Task ConfirmarAsync_GastoConLineaPoaAsignada_MapeaCredito()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Gastos = new List<GastoAnalizadoDto>
            {
                new("ENERO", 3, EstadoFila.Ok, new List<MotivoEstado>(),
                    new DateOnly(2026, 1, 15), 500m, "ACME SA", false, "F-1", "O-1",
                    "Compromiso POA", null, "Literal A", false, 1, "Materiales", false, "COMPOSTERAS"),
            },
            Resumen = new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        ConfirmarImportacionDto? dtoCapturado = null;
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .Callback<ConfirmarImportacionDto>(dto => dtoCapturado = dto)
            .ReturnsAsync(new ResultadoConfirmacionDto(
                Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));

        await vm.ConfirmarCommand.ExecuteAsync(null);

        var gasto = Assert.Single(dtoCapturado!.Gastos);
        Assert.Equal(CondicionPago.Credito, gasto.Condicion);
        Assert.Equal(new DateOnly(2026, 1, 15), gasto.FechaVencimiento);
        Assert.Empty(dtoCapturado.LineasPoa); // gap documentado: Entrega 1 nunca declara LineaPoa nueva
    }

    [Fact]
    public async Task ConfirmarAsync_ElServidorRechaza400_InformaYNoAvanzaDePaso()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var analisis = ResultadoAnalisisVacio() with
        {
            Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
        };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
            .ThrowsAsync(new ArgumentException("MaestrosNuevos.Rubros[0].Nombre: Requerido"));

        await vm.ConfirmarCommand.ExecuteAsync(null);

        Assert.Equal(PasoWizardImportacion.Revisar, vm.PasoActual);
        confirm.Verify(c => c.InformarAsync("MaestrosNuevos.Rubros[0].Nombre: Requerido"), Times.Once);
    }
```

Y agregar el helper `ResultadoAnalisisVacio()` como método de instancia de la clase de test (si `Task 8` ya lo dejó como `private static`, reusarlo tal cual — está declarado una sola vez en el archivo).

- [ ] **Step 3: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `'NuevaImportacionViewModel' does not contain a definition for 'GastosAnalizados'` (error de compilación).

- [ ] **Step 4: Reemplazar el placeholder `CargarAnalisisPaso2` por la implementación real + agregar `ConfirmarCommand`**

Reemplazar TODO el archivo `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs` por (agrega Paso 2 sobre el Paso 1 de Task 8; el método `partial void CargarAnalisisPaso2` y su llamado indirecto se eliminan — `CargarAnalisis` ahora hace el trabajo directo):

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Collections;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Paso actual del wizard de importación (F5d §5).</summary>
public enum PasoWizardImportacion { Cargar, Revisar, Resultado }

/// <summary>
/// Tab "Nueva importación" (F5d §5): wizard de 3 pasos como UNA sola VM con estado PasoActual.
/// Paso 2 (Revisar) es SOLO LECTURA en esta entrega — Entrega 2 agrega la edición de celda.
/// </summary>
public partial class NuevaImportacionViewModel : ViewModelBase
{
    private readonly IImportacionService _service;
    private readonly IServicioSeleccionArchivo _seleccion;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty]
    private PasoWizardImportacion _pasoActual = PasoWizardImportacion.Cargar;

    // ── Paso 1: Cargar ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _gastosNombreArchivo;
    private byte[]? _gastosContenido;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AnalizarCommand))]
    private string? _poaNombreArchivo;
    private byte[]? _poaContenido;

    [ObservableProperty]
    private int _ejercicio = DateTime.UtcNow.Year;

    [ObservableProperty]
    private bool _forzar;

    // ── Paso 2: Revisar (solo lectura, Entrega 1) ───────────────────────────
    private ResultadoAnalisisDto? _analisis;

    public ObservableCollection<GastoAnalizadoDto> GastosAnalizados { get; } = new();
    public DataGridCollectionView GastosAnalizadosView { get; }

    public ObservableCollection<IngresoAnalizadoDto> IngresosAnalizados { get; } = new();
    public DataGridCollectionView IngresosAnalizadosView { get; }

    public ObservableCollection<LineaPoaAnalizadaDto> LineasPoaAnalizadas { get; } = new();
    public DataGridCollectionView LineasPoaAnalizadasView { get; }

    public ObservableCollection<string> ProveedoresNuevos { get; } = new();
    public ObservableCollection<string> FuentesNuevas { get; } = new();
    public ObservableCollection<CodigoRubroNuevoDto> RubrosNuevos { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuedeConfirmar))]
    [NotifyCanExecuteChangedFor(nameof(ConfirmarCommand))]
    private ResumenAnalisisDto? _resumen;

    public bool PuedeConfirmar => Resumen is { Errores: 0 };

    // ── Paso 3: Resultado ────────────────────────────────────────────────────
    [ObservableProperty]
    private ResultadoConfirmacionDto? _resultadoConfirmacion;

    public NuevaImportacionViewModel(
        IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion)
    {
        _service = service;
        _seleccion = seleccion;
        _confirmacion = confirmacion;

        GastosAnalizadosView = new DataGridCollectionView(GastosAnalizados);
        IngresosAnalizadosView = new DataGridCollectionView(IngresosAnalizados);
        LineasPoaAnalizadasView = new DataGridCollectionView(LineasPoaAnalizadas);
    }

    [RelayCommand]
    private async Task SeleccionarGastosAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (GastosNombreArchivo, _gastosContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task SeleccionarPoaAsync()
    {
        var seleccionado = await _seleccion.SeleccionarArchivoOdsAsync();
        if (seleccionado is null) return;
        (PoaNombreArchivo, _poaContenido) = seleccionado.Value;
        AnalizarCommand.NotifyCanExecuteChanged();
    }

    private bool PuedeAnalizar() => _gastosContenido is not null && _poaContenido is not null;

    [RelayCommand(CanExecute = nameof(PuedeAnalizar))]
    private async Task AnalizarAsync()
    {
        try
        {
            _analisis = await _service.AnalizarAsync(
                GastosNombreArchivo!, _gastosContenido!, PoaNombreArchivo!, _poaContenido!, Ejercicio);

            GastosAnalizados.Clear();
            foreach (var g in _analisis.Gastos) GastosAnalizados.Add(g);
            IngresosAnalizados.Clear();
            foreach (var i in _analisis.Ingresos) IngresosAnalizados.Add(i);
            LineasPoaAnalizadas.Clear();
            foreach (var l in _analisis.LineasPoa) LineasPoaAnalizadas.Add(l);
            ProveedoresNuevos.Clear();
            foreach (var p in _analisis.MaestrosNuevos.Proveedores) ProveedoresNuevos.Add(p);
            FuentesNuevas.Clear();
            foreach (var f in _analisis.MaestrosNuevos.Fuentes) FuentesNuevas.Add(f);
            RubrosNuevos.Clear();
            foreach (var r in _analisis.MaestrosNuevos.Rubros) RubrosNuevos.Add(r);

            Resumen = _analisis.Resumen;
            PasoActual = PasoWizardImportacion.Revisar;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand(CanExecute = nameof(PuedeConfirmar))]
    private async Task ConfirmarAsync()
    {
        if (_analisis is null) return;

        var dto = MapearAConfirmacion(_analisis, Ejercicio, Forzar);

        try
        {
            ResultadoConfirmacion = await _service.ConfirmarAsync(dto);
            PasoActual = PasoWizardImportacion.Resultado;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    /// <summary>
    /// Mapeo directo (sin edición) análisis→confirmación, válido SOLO cuando Resumen.Errores == 0
    /// (único caso en que ConfirmarCommand se puede ejecutar) — todo campo requerido de las filas
    /// mapeadas ya viene no-nulo del análisis en ese caso.
    ///
    /// Gap documentado (Entrega 1, contradice el diseño F5d §8 en la letra chica): GastoConfirmarDto
    /// exige CondicionPago no-nullable, pero GastoAnalizadoDto NO expone ese campo — el análisis
    /// (F5b/F5c) nunca lo calculó. Se infiere con el mismo criterio que ya usa el backend para los
    /// compromisos POA (ImportacionRepository, "Los compromisos POA importados van Credito SIN
    /// pago"): LineaPoaAsignada != null ⇒ Credito con FechaVencimiento = la misma Fecha del gasto
    /// (no hay otra fecha disponible sin editar); si no, Contado sin vencimiento. Es una heurística,
    /// no una elección del usuario — Entrega 2 debería exponer Condicion/FechaVencimiento como
    /// celda editable si este supuesto no alcanza en la práctica.
    ///
    /// Segundo gap documentado: LineaPoaConfirmarDto exige Nombre+Programa, que
    /// LineaPoaAnalizadaDto NO expone (solo Hoja/Literal/Presupuesto/SaldoPlanilla — Literal es
    /// el nombre de la FUENTE de esa línea, no el nombre de la línea). Declarar una LineaPoa
    /// NUEVA es, en los hechos, una operación de edición — se difiere a Entrega 2. Acá SIEMPRE se
    /// manda vacía: si algún Gasto referencia una LineaPoa que todavía no existe en la base para
    /// este Ejercicio, el 400 estructurado del servidor se muestra tal cual (catch de
    /// ConfirmarAsync), nunca se inventa un Nombre/Programa.
    /// </summary>
    private static ConfirmarImportacionDto MapearAConfirmacion(
        ResultadoAnalisisDto analisis, int ejercicio, bool forzar)
    {
        var ingresos = analisis.Ingresos
            .Select(i => new IngresoConfirmarDto(i.Fecha!.Value, i.Concepto ?? string.Empty, i.Monto!.Value, i.Fuente!))
            .ToList();

        var gastos = analisis.Gastos.Select(g =>
        {
            var esCompromisoPoa = g.LineaPoaAsignada is not null;
            return new GastoConfirmarDto(
                Proveedor: g.Proveedor!,
                NumeroFactura: g.NumeroFactura,
                NumeroOrden: g.NumeroOrden,
                Detalle: g.Detalle ?? string.Empty,
                Destino: g.Destino,
                Fecha: g.Fecha!.Value,
                MontoTotal: g.Monto!.Value,
                Fuente: g.Fuente!,
                CodigoRubro: g.CodigoRubro!.Value,
                LineaPoa: g.LineaPoaAsignada,
                Condicion: esCompromisoPoa ? CondicionPago.Credito : CondicionPago.Contado,
                FechaVencimiento: esCompromisoPoa ? g.Fecha!.Value : null);
        }).ToList();

        var maestrosNuevos = new MaestrosNuevosConfirmarDto(
            analisis.MaestrosNuevos.Proveedores,
            analisis.MaestrosNuevos.Fuentes,
            analisis.MaestrosNuevos.Rubros
                .Select(r => new RubroNuevoConfirmarDto(r.Codigo, r.NombreSugerido ?? string.Empty))
                .ToList());

        return new ConfirmarImportacionDto(
            ejercicio, forzar, maestrosNuevos, ingresos, gastos, new List<LineaPoaConfirmarDto>());
    }
}
```

- [ ] **Step 5: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS (todos los tests de Task 8 + los 6 nuevos de este task, 11/11).

- [ ] **Step 6: Agregar la grilla del Paso 2 a la vista**

Agregar a `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`, dentro del `Grid` raíz, después del bloque "Paso 1" (agregar `xmlns:conv="using:StockApp.Presentation.Converters"` al `UserControl` de arriba):

```xml
        <!-- Paso 2: Revisar (solo lectura, con color por EstadoFila) -->
        <DockPanel IsVisible="{Binding PasoActual, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static vm:PasoWizardImportacion.Revisar}}">
            <TextBlock DockPanel.Dock="Top" Text="Paso 2 · Revisar" Classes="titulo-vista" Margin="0,0,0,8" />

            <Border DockPanel.Dock="Top" Classes="card" Margin="0,0,0,12">
                <StackPanel Orientation="Horizontal" Spacing="16">
                    <TextBlock Text="{Binding Resumen.Ok, StringFormat='Ok: {0}'}" />
                    <TextBlock Text="{Binding Resumen.Advertencias, StringFormat='Advertencias: {0}'}" />
                    <TextBlock Text="{Binding Resumen.Errores, StringFormat='Errores: {0}'}" />
                    <Button Classes="primary" Content="Confirmar" Command="{Binding ConfirmarCommand}" />
                </StackPanel>
            </Border>

            <TabControl>
                <TabItem Header="Gastos">
                    <DataGrid ItemsSource="{Binding GastosAnalizadosView}" IsReadOnly="True"
                              CanUserSortColumns="True" AutoGenerateColumns="False">
                        <DataGrid.Styles>
                            <Style Selector="DataGridRow">
                                <Setter Property="Background" Value="{Binding Estado, Converter={x:Static conv:EstadoFilaBrushConverter.Instance}}" />
                            </Style>
                        </DataGrid.Styles>
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Proveedor" Binding="{Binding Proveedor, DataType={x:Type vm:GastoAnalizadoDto}}" Width="*" />
                            <DataGridTextColumn Header="Factura" Binding="{Binding NumeroFactura, DataType={x:Type vm:GastoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Detalle" Binding="{Binding Detalle, DataType={x:Type vm:GastoAnalizadoDto}}" Width="2*" />
                            <DataGridTextColumn Header="Monto" Binding="{Binding Monto, DataType={x:Type vm:GastoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Fuente" Binding="{Binding Fuente, DataType={x:Type vm:GastoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Rubro" Binding="{Binding Rubro, DataType={x:Type vm:GastoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:GastoAnalizadoDto}}" Width="Auto" />
                        </DataGrid.Columns>
                    </DataGrid>
                </TabItem>
                <TabItem Header="Ingresos">
                    <DataGrid ItemsSource="{Binding IngresosAnalizadosView}" IsReadOnly="True"
                              CanUserSortColumns="True" AutoGenerateColumns="False">
                        <DataGrid.Styles>
                            <Style Selector="DataGridRow">
                                <Setter Property="Background" Value="{Binding Estado, Converter={x:Static conv:EstadoFilaBrushConverter.Instance}}" />
                            </Style>
                        </DataGrid.Styles>
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Concepto" Binding="{Binding Concepto, DataType={x:Type vm:IngresoAnalizadoDto}}" Width="*" />
                            <DataGridTextColumn Header="Monto" Binding="{Binding Monto, DataType={x:Type vm:IngresoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Fuente" Binding="{Binding Fuente, DataType={x:Type vm:IngresoAnalizadoDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:IngresoAnalizadoDto}}" Width="Auto" />
                        </DataGrid.Columns>
                    </DataGrid>
                </TabItem>
                <TabItem Header="Líneas POA">
                    <DataGrid ItemsSource="{Binding LineasPoaAnalizadasView}" IsReadOnly="True"
                              CanUserSortColumns="True" AutoGenerateColumns="False">
                        <DataGrid.Styles>
                            <Style Selector="DataGridRow">
                                <Setter Property="Background" Value="{Binding Estado, Converter={x:Static conv:EstadoFilaBrushConverter.Instance}}" />
                            </Style>
                        </DataGrid.Styles>
                        <DataGrid.Columns>
                            <DataGridTextColumn Header="Hoja" Binding="{Binding Hoja, DataType={x:Type vm:LineaPoaAnalizadaDto}}" Width="*" />
                            <DataGridTextColumn Header="Literal" Binding="{Binding Literal, DataType={x:Type vm:LineaPoaAnalizadaDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Presupuesto" Binding="{Binding Presupuesto, DataType={x:Type vm:LineaPoaAnalizadaDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Saldo" Binding="{Binding SaldoPlanilla, DataType={x:Type vm:LineaPoaAnalizadaDto}}" Width="Auto" />
                            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:LineaPoaAnalizadaDto}}" Width="Auto" />
                        </DataGrid.Columns>
                    </DataGrid>
                </TabItem>
                <TabItem Header="Maestros nuevos">
                    <StackPanel Orientation="Horizontal" Spacing="24" Margin="12">
                        <StackPanel Spacing="4">
                            <TextBlock Text="Proveedores nuevos" FontWeight="SemiBold" />
                            <ItemsControl ItemsSource="{Binding ProveedoresNuevos}" />
                        </StackPanel>
                        <StackPanel Spacing="4">
                            <TextBlock Text="Fuentes nuevas" FontWeight="SemiBold" />
                            <ItemsControl ItemsSource="{Binding FuentesNuevas}" />
                        </StackPanel>
                        <StackPanel Spacing="4">
                            <TextBlock Text="Rubros nuevos" FontWeight="SemiBold" />
                            <ItemsControl ItemsSource="{Binding RubrosNuevos}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate x:DataType="vm:CodigoRubroNuevoDto">
                                        <TextBlock Text="{Binding Codigo, StringFormat='Código {0}'}" />
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </StackPanel>
                    </StackPanel>
                </TabItem>
            </TabControl>
        </DockPanel>
```

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        src/StockApp.Presentation/Converters/EstadoFilaBrushConverter.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): wizard Paso 2 (Revisar solo lectura, color por EstadoFila) + Confirmar"
```

---

### Task 10: Wizard — Paso 3 (Resultado) + Revertir

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Test: Modify `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`

**Interfaces:**
- Consumes: `ResultadoConfirmacionDto` (`IdImportacion`/contadores/`Conflictos`, Task 4); `IImportacionService.RevertirAsync(Guid)`; `IConfirmacionService.PreguntarAsync`.
- Produces: `NuevaImportacionViewModel.Conflictos`/`RevertirCommand` — cierra el wizard de Entrega 1 (nada más lo consume dentro de este plan).

- [ ] **Step 1: Escribir los tests que fallan (conflictos + revertir + reinicio del wizard)**

Agregar a `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`:

```csharp
    private static async Task<(NuevaImportacionViewModel vm, ResultadoConfirmacionDto resultado)>
        CrearEnPasoResultadoAsync(
            Mock<IImportacionService> svc, Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
            ResultadoConfirmacionDto resultado)
    {
        var analisis = ResultadoAnalisisVacio() with { Resumen = new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0) };
        var vm = await CrearEnPasoRevisarAsync(svc, seleccion, confirm, analisis);
        svc.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>())).ReturnsAsync(resultado);

        await vm.ConfirmarCommand.ExecuteAsync(null);
        return (vm, resultado);
    }

    [Fact]
    public async Task Confirmar_ConConflictos_PopulaLaGrillaDeConflictos()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        var resultado = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            new List<ConflictoGastoDto>
            {
                new("ACME SA", "F-1",
                    new List<CampoDivergenteDto> { new("MontoTotal", "500", "550") }, 0),
            });

        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, resultado);

        var conflicto = Assert.Single(vm.Conflictos);
        Assert.Equal("ACME SA", conflicto.Proveedor);
        Assert.Equal("MontoTotal: 500 → 550", conflicto.CamposTexto);
    }

    [Fact]
    public async Task RevertirAsync_ConfirmaYLlamaAlServicio_ReiniciaElWizard()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        var idImportacion = Guid.NewGuid();
        var resultado = new ResultadoConfirmacionDto(
            idImportacion, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, resultado);
        svc.Setup(s => s.RevertirAsync(idImportacion))
            .ReturnsAsync(new ResultadoReversionDto(idImportacion, 0, 0, 0, 0, 0));

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(idImportacion), Times.Once);
        Assert.Equal(PasoWizardImportacion.Cargar, vm.PasoActual);
        Assert.Null(vm.ResultadoConfirmacion);
    }

    [Fact]
    public async Task RevertirAsync_UsuarioCancelaConfirmacion_NoLlamaAlServicio()
    {
        var svc = new Mock<IImportacionService>();
        var seleccion = new Mock<IServicioSeleccionArchivo>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        var resultado = new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>());
        var (vm, _) = await CrearEnPasoResultadoAsync(svc, seleccion, confirm, resultado);

        await vm.RevertirCommand.ExecuteAsync(null);

        svc.Verify(s => s.RevertirAsync(It.IsAny<Guid>()), Times.Never);
        Assert.Equal(PasoWizardImportacion.Resultado, vm.PasoActual);
    }
```

- [ ] **Step 2: Correr y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `'NuevaImportacionViewModel' does not contain a definition for 'Conflictos'` / `'RevertirCommand'` (error de compilación).

- [ ] **Step 3: Agregar el Paso 3 al ViewModel**

Agregar a `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`:

1) El record de fila de conflicto, antes de la declaración de la clase `NuevaImportacionViewModel` (mismo criterio que `GastoFila` en `GastosViewModel.cs` — vive junto a la VM que lo produce):

```csharp
/// <summary>Fila de solo lectura de la grilla de conflictos del Paso 3 (F5d §5): aplana
/// ConflictoGastoDto.CamposDivergentes a una sola línea legible.</summary>
public sealed record ConflictoGastoFila(string Proveedor, string NumeroFactura, string CamposTexto)
{
    public static ConflictoGastoFila Desde(ConflictoGastoDto dto) => new(
        dto.Proveedor, dto.NumeroFactura,
        string.Join("; ", dto.CamposDivergentes.Select(c => $"{c.Campo}: {c.ValorAnterior} → {c.ValorNuevo}")));
}
```

2) Dentro de la clase, la colección y el comando de Revertir (después del bloque `// ── Paso 3: Resultado ──`):

```csharp
    public ObservableCollection<ConflictoGastoFila> Conflictos { get; } = new();
```

3) Reemplazar el cuerpo de `ConfirmarAsync` para poblar `Conflictos` tras un `ConfirmarAsync` exitoso:

```csharp
    [RelayCommand(CanExecute = nameof(PuedeConfirmar))]
    private async Task ConfirmarAsync()
    {
        if (_analisis is null) return;

        var dto = MapearAConfirmacion(_analisis, Ejercicio, Forzar);

        try
        {
            ResultadoConfirmacion = await _service.ConfirmarAsync(dto);
            Conflictos.Clear();
            foreach (var c in ResultadoConfirmacion.Conflictos)
                Conflictos.Add(ConflictoGastoFila.Desde(c));
            PasoActual = PasoWizardImportacion.Resultado;
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }
```

4) `RevertirCommand` + `ReiniciarWizard`, al final de la clase:

```csharp
    private bool PuedeRevertir() => ResultadoConfirmacion is not null;

    [RelayCommand(CanExecute = nameof(PuedeRevertir))]
    private async Task RevertirAsync()
    {
        if (ResultadoConfirmacion is null) return;

        var confirmar = await _confirmacion.PreguntarAsync(
            $"¿Confirma revertir la importación {ResultadoConfirmacion.IdImportacion}? " +
            "Se darán de baja todos los gastos, ingresos y líneas POA que creó.");
        if (!confirmar) return;

        try
        {
            await _service.RevertirAsync(ResultadoConfirmacion.IdImportacion);
            await _confirmacion.InformarAsync("Importación revertida correctamente.");
            ReiniciarWizard();
        }
        catch (Exception ex)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    private void ReiniciarWizard()
    {
        PasoActual = PasoWizardImportacion.Cargar;
        GastosNombreArchivo = null;
        _gastosContenido = null;
        PoaNombreArchivo = null;
        _poaContenido = null;
        Forzar = false;
        GastosAnalizados.Clear();
        IngresosAnalizados.Clear();
        LineasPoaAnalizadas.Clear();
        ProveedoresNuevos.Clear();
        FuentesNuevas.Clear();
        RubrosNuevos.Clear();
        Conflictos.Clear();
        Resumen = null;
        ResultadoConfirmacion = null;
        _analisis = null;
        AnalizarCommand.NotifyCanExecuteChanged();
    }
```

5) Agregar `[NotifyCanExecuteChangedFor(nameof(RevertirCommand))]` a la declaración de `_resultadoConfirmacion`:

```csharp
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RevertirCommand))]
    private ResultadoConfirmacionDto? _resultadoConfirmacion;
```

- [ ] **Step 4: Correr y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS (todos los tests acumulados de Tasks 8+9+10, 14/14).

- [ ] **Step 5: Agregar el Paso 3 a la vista**

Agregar a `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`, dentro del `Grid` raíz, después del bloque "Paso 2":

```xml
        <!-- Paso 3: Resultado -->
        <StackPanel Spacing="12"
                    IsVisible="{Binding PasoActual, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static vm:PasoWizardImportacion.Resultado}}">
            <TextBlock Text="Paso 3 · Resultado" Classes="titulo-vista" />

            <Border Classes="card">
                <StackPanel Spacing="8">
                    <TextBlock Text="{Binding ResultadoConfirmacion.GastosCreados, StringFormat='Gastos creados: {0}'}" />
                    <TextBlock Text="{Binding ResultadoConfirmacion.IngresosCreados, StringFormat='Ingresos creados: {0}'}" />
                    <TextBlock Text="{Binding ResultadoConfirmacion.ProveedoresCreados, StringFormat='Proveedores creados: {0}'}" />
                    <TextBlock Text="{Binding ResultadoConfirmacion.FuentesCreadas, StringFormat='Fuentes creadas: {0}'}" />
                    <TextBlock Text="{Binding ResultadoConfirmacion.RubrosCreados, StringFormat='Rubros creados: {0}'}" />
                    <Button Classes="secondary" Content="Revertir esta importación" Command="{Binding RevertirCommand}" />
                </StackPanel>
            </Border>

            <TextBlock Text="Conflictos (no se escribieron — resolvé a mano)" FontWeight="SemiBold" />
            <DataGrid ItemsSource="{Binding Conflictos}" IsReadOnly="True" AutoGenerateColumns="False">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Proveedor" Binding="{Binding Proveedor, DataType={x:Type vm:ConflictoGastoFila}}" Width="Auto" />
                    <DataGridTextColumn Header="Factura" Binding="{Binding NumeroFactura, DataType={x:Type vm:ConflictoGastoFila}}" Width="Auto" />
                    <DataGridTextColumn Header="Campos divergentes" Binding="{Binding CamposTexto, DataType={x:Type vm:ConflictoGastoFila}}" Width="*" />
                </DataGrid.Columns>
            </DataGrid>
        </StackPanel>
```

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): wizard Paso 3 (Resultado + conflictos + Revertir)"
```

---

## Self-Review

**1. Cobertura del spec §8 (Entrega 1):**
- Backend `GET /finanzas/importar/historial` + `ImportacionHistorialDto` + query sobre `LogsAuditoria`, sin migración → Tasks 1-3. ✅
- ApiClient `IImportacionService`/`ImportacionApiClient` (4 métodos) + DI → Task 4. ✅
- Sidebar admin-only + navegación → Task 6. ✅
- Pantalla contenedora con 2 tabs → Tasks 6 (VM) + 8 (View real). ✅
- Tab Historial completo (grilla read-only + Revertir por fila, solo Activas) → Task 7. ✅
- Wizard Paso 1 (Cargar: 2 archivos + ejercicio + Forzar → Analizar) → Task 8. ✅
- Wizard Paso 2 solo lectura con color por `EstadoFila`, Confirmar deshabilitado con Errores → Task 9. ✅
- Wizard Paso 3 (contadores + conflictos + Revertir) → Task 10. ✅
- Selector de archivo `.ods` (no existía, la interfaz vieja solo filtraba PDF/JPG/PNG) → Task 5, agregado como método nuevo sin romper el uso existente en `AdjuntosPanelViewModel`.

**2. Gaps reales encontrados contra el diseño (documentados, no inventados):**
- `GastoAnalizadoDto` (F5b/F5c) NO tiene campo `CondicionPago`/`FechaVencimiento`, pero `GastoConfirmarDto` los exige (uno obligatorio, el otro condicional). El diseño F5d dice "confirmar si el análisis vino limpio" sin mencionar este vacío. Resuelto con una heurística grounded en el propio backend (los compromisos POA ya van `Credito` por convención de `ImportacionRepository`): `LineaPoaAsignada != null ⇒ Credito` con `FechaVencimiento = Fecha`; si no, `Contado` sin vencimiento. Documentado en el XML comment de `MapearAConfirmacion` y cubierto por 2 tests (`ConfirmarAsync_AnalisisLimpio_MapeaGastoContadoYAvanzaAResultado`, `ConfirmarAsync_GastoConLineaPoaAsignada_MapeaCredito`). Riesgo real: si esta heurística no cubre el caso de negocio, Entrega 2 necesita exponer `Condicion`/`FechaVencimiento` como celda editable (no está en la lista del §6 del diseño).
- `LineaPoaAnalizadaDto` NO tiene `Nombre` ni `Programa` (solo `Hoja`/`Literal`/`Presupuesto`/`SaldoPlanilla`), pero `LineaPoaConfirmarDto` los exige. Declarar una LineaPoa nueva es, en los hechos, una operación de edición inalcanzable sin UI de edición. Resuelto enviando SIEMPRE `LineasPoa: []` en Entrega 1 — un Gasto que referencia una LineaPoa nueva recibe el 400 estructurado del servidor tal cual, sin inventar datos. Cubierto por el assert `Assert.Empty(dtoCapturado.LineasPoa)` en el test de mapeo Credito.
- Ambos gaps recortan el alcance real de "confirmar si vino limpio": solo es 100% mecánico cuando la planilla no declara LineasPoa nuevas. Para el caso más común (gastos del libro caja sin proyecto POA nuevo) el flujo cierra igual.

**3. Scan de placeholders:** sin `TODO`/`"implementar después"`/`"similar a Task N"` en ningún step. El único artefacto temporal es el `partial void CargarAnalisisPaso2` de Task 8 (Step 3) y los `throw new NotImplementedException()` de Task 4 (Step 4) — ambos EXPLÍCITAMENTE reemplazados dentro del mismo plan (Task 9 Step 4 y Task 4 Steps 9/14/19 respectivamente) antes de que el plan termine; no queda ninguno en el estado final. Se dejó una nota inline en cada uno aclarando que es temporal.

**4. Consistencia de tipos entre tasks:**
- `IImportacionRepository.ListarHistorialAsync()` (Task 1) ↔ `ImportacionRepositoryFake.ListarHistorialAsync()` (Task 2) ↔ `IConfirmacionImportacionService.ListarHistorialAsync()` (Task 2) ↔ endpoint (Task 3) ↔ `IImportacionService.ListarHistorialAsync()`/`ImportacionApiClient` (Task 4) ↔ `HistorialImportacionesViewModel` (Task 7): mismo tipo `Task<IReadOnlyList<ImportacionHistorialDto>>` en las 6 capas.
- `PasoWizardImportacion` se declara una sola vez (Task 8) y se referencia igual en Tasks 9/10 y en el XAML (`{x:Static vm:PasoWizardImportacion.Revisar}` / `.Resultado`).
- `GastosAnalizados`/`IngresosAnalizados`/`LineasPoaAnalizadas` y sus `*View` (Task 9) se nombran igual en el ViewModel y en `NuevaImportacionView.axaml`.
- `ConflictoGastoFila.CamposTexto` (Task 10) es el único nombre usado tanto en el test como en el XAML de la grilla de conflictos.
- Corregido inline durante esta revisión: la primera versión de Task 8 dejaba `CargarAnalisisPaso2` como método privado normal (rompía la regla de "sin placeholders no reemplazados") — se ajustó a `partial void` con nota explícita de que Task 9 la reemplaza por completo, dejando claro que no es una implementación final.

**5. `SaldosTotalesPoaOds` verificado contra el código real:** `src/StockApp.Application/Finanzas/PlanillaOdsDtos.cs:66` define `SaldosTotalesPoaOds(decimal SaldoLiteralB, decimal SaldoLiteralC)` — coincide exactamente con `new SaldosTotalesPoaOds(0m, 0m)` usado en los tests de Tasks 4/8, sin necesidad de ajuste.
