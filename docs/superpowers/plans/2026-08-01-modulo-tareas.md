# Módulo de tareas para operarios — Plan de implementación

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar a Admin y Operador una lista común de tareas operativas (crear, tomar, soltar, terminar, cancelar, repriorizar, comentar) con una máquina de estados explícita en el dominio, trazabilidad de quién tomó y quién cerró cada una, notas append-only, y una pantalla que agrupa por estado con las canceladas detrás de un filtro.

**Architecture:** Módulo independiente de punta a punta (`Tarea` + `NotaTarea`, sin FK a otras entidades del dominio — decisión 1 del spec). La máquina de estados vive en `Tarea.CambiarEstado(EstadoTarea)` (dominio, sin infraestructura). `TareaService` orquesta auth → validación → `CambiarEstado` → trazabilidad (quién/cuándo) → notas automáticas → persistencia → auditoría, siguiendo el mismo patrón que `GastoService`. `ITareaService` expone entidades de dominio directamente (no DTOs de Application), igual que `IGastoService`; los DTOs de transporte viven solo en el borde HTTP (`TareasEndpoints.cs`) y en el `ApiClient` (wire records), mapeando a entidades a cada lado — mismo patrón que `GastoApiClient`/`GastoDto`.

**Tech Stack:** .NET, EF Core + Npgsql, Avalonia, xUnit, Moq, Testcontainers

## Global Constraints

- Tests: xUnit + Moq, aserciones `Assert.*` (nunca FluentAssertions).
- Comando de tests: `dotnet test StockApp.sln`; por proyecto `dotnet test tests/<Proyecto>`; filtrado `dotnet test tests/<Proyecto> --filter "FullyQualifiedName~Nombre"`.
- Nunca correr `dotnet build` como paso separado — los tests compilan.
- **ImplicitUsings — verificado leyendo los `.csproj`, no asumido:** TODOS los proyectos de este repo tienen `<ImplicitUsings>enable</ImplicitUsings>` **excepto `StockApp.Presentation`** (el único que no lo declara). Esto corrige una generalización imprecisa de la feature anterior ("el proyecto NO usa ImplicitUsings"): es una excepción de un solo proyecto, no una regla global. En consecuencia:
  - Archivos nuevos en `StockApp.Domain`, `StockApp.Application`, `StockApp.Infrastructure`, `StockApp.Api`, `StockApp.ApiClient` y sus `*.Tests`: **no** hace falta `using System;`, `using System.Linq;`, `using System.Collections.Generic;`, `using System.Threading.Tasks;` — vienen implícitos. Sí hacen falta los usings de namespaces propios del proyecto (`StockApp.Domain.Entities`, `StockApp.Application.Interfaces`, etc.) y de paquetes de terceros (`Microsoft.EntityFrameworkCore`, `Moq`, etc.).
  - Archivos nuevos en `StockApp.Presentation` (ViewModels y code-behind de Views): **sí** necesitan los `using` explícitos de `System`, `System.Collections.Generic`, `System.Collections.ObjectModel`, `System.Linq`, `System.Threading.Tasks`, etc., como ya hace `GastosViewModel.cs`.
- `AccionAuditada` es append-only: los cinco valores de este módulo (`AltaTarea = 46`, `CambioEstadoTarea = 47`, `CambioPrioridadTarea = 48`, `CancelacionTarea = 49`, `AltaNotaTarea = 50`) se agregan al final, después de `AnulacionIngresoPorFactura = 45` — nunca se reordenan valores existentes.
- Fechas del request se normalizan a UTC en el borde JSON (converter ya existente, commit 2239734) — no se reimplementa.
- `ITareaService` expone `Tarea`/`NotaTarea` (entidades de dominio), no DTOs de Application — mismo criterio que `IGastoService` con `Gasto`/`PagoGasto`. Los DTOs de transporte (`TareaDto` en el endpoint, `TareaWire` en el ApiClient) son un detalle de cada borde, no del contrato de servicio.
- `NotaTarea` **no tiene** navegación de vuelta a `Tarea` (sin propiedad `Tarea? Tarea`): la relación se configura solo desde el lado padre en `AppDbContext` (`HasOne<Tarea>().WithMany(t => t.Notas)`), mismo criterio que `AsignacionPresupuestal` → `LineaPoa`. Evita ciclos de navegación y mantiene la entidad hija mínima.
- `Tarea` sí tiene `Usuario? TomadaPor` (nav de solo lectura sobre `TomadaPorUsuarioId`) porque la grilla necesita mostrar el nombre del responsable (decisión 10 del spec) — mismo criterio que `Gasto.Proveedor` para mostrar `ProveedorNombre` sin otra llamada.
- `GET /tareas` **no filtra por estado en el servidor.** El diseño técnico menciona "filtro opcional por estado" pero el resto del diseño (Presentation: "agrupadas por estado y las canceladas detrás de un filtro") y el propio dataset (chico, todo visible para todos — decisión 10) apuntan a filtrar/agrupar en el cliente, mismo patrón ya establecido por `GastosViewModel.EstadoSeleccionado` (filtro en memoria). `ITareaService.ListarAsync()` no toma parámetros; se documenta acá para no dejarlo como una contradicción silenciosa entre el spec y el plan.
- Clases de estilo AXAML verificadas contra `Themes/Controls.axaml` y `Themes/Typography.axaml`: `Button.primary`, `Button.secondary`, `Button.ghost`, `Button.danger`, `Border.card`, `TextBlock.titulo-vista`, `TextBlock.seccion` (headers de sección — **no** `subtitulo`, que no existe en este repo), `TextBlock.caption`. `Button.ghost.active` se activa vía `Classes.active="{Binding ..., Converter={x:Static ObjectConverters.Equal}, ConverterParameter=...}"`.
- Comandos por fila en `ItemsControl`/`ListBox` dentro de un `UserControl` con `x:DataType`: `Command="{Binding $parent[UserControl].((vm:XViewModel)DataContext).YCommand}"` + `CommandParameter="{Binding}"` — patrón verificado en `PagosGastoView.axaml`, `AdjuntosPanelView.axaml`, `CalendarioPagosView.axaml`.
- Resaltado de vencidas: se reusa `SignoNegativoBrushConverter` (decimal negativo → `DangerBrush`) bindeando el `Foreground` del título a una propiedad decimal auxiliar (`TareaFila.DiasParaVencer`, negativa cuando venció y sigue abierta, `0` en cualquier otro caso) — no se crea un converter nuevo.
- `CalendarDatePicker` (no `DatePicker`, migración ya hecha en commit ccd4bd7): `SelectedDate` es `DateTime?`, con `beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True"`.
- Commits: conventional commits, sin atribución de IA, uno por ciclo test→implementación completado.

---

## Estructura de archivos

| Archivo | Responsabilidad |
|---|---|
| `src/StockApp.Domain/Entities/Tarea.cs` | (Create, T1) Entidad + máquina de estados `CambiarEstado`. |
| `src/StockApp.Domain/Entities/NotaTarea.cs` | (Create, T1) Nota append-only. |
| `src/StockApp.Domain/Enums/EstadoTarea.cs` | (Create, T1) `Pendiente/EnCurso/Terminada/Cancelada`. |
| `src/StockApp.Domain/Enums/PrioridadTarea.cs` | (Create, T1) `Baja/Media/Alta`. |
| `tests/StockApp.Domain.Tests/Entities/TareaTests.cs` | (Create, T1) Transiciones válidas/inválidas, terminalidad. |
| `src/StockApp.Application/Interfaces/ITareaRepository.cs` | (Create, T2) Contrato de persistencia. |
| `src/StockApp.Infrastructure/Repositories/TareaRepository.cs` | (Create, T2) Implementación EF Core. |
| `src/StockApp.Infrastructure/Persistence/AppDbContext.cs` | (Modify, T2) DbSets + mapeo. |
| `src/StockApp.Infrastructure/Migrations/*_AgregaTareas.cs` | (Create, T2, generado) Migración `Tareas` + `NotasTarea`. |
| `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs` | (Modify, T2) TRUNCATE de las tablas nuevas. |
| `tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs` | (Create, T2) Alta con notas, listado, orden del hilo. |
| `src/StockApp.Application/Tareas/ITareaService.cs` | (Create, T3) Contrato completo (8 métodos). |
| `src/StockApp.Application/Tareas/TareaService.cs` | (Create T3; Modify T4, T5, T6) Servicio. |
| `src/StockApp.Application/Authorization/Permisos.cs` | (Modify, T3 y T5) `GestionarTareas`, `AdministrarTareas`. |
| `src/StockApp.Application/Authorization/AuthorizationService.cs` | (Modify, T3) `GestionarTareas` al `HashSet` de Operador. |
| `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs` | (Create T3; Modify T4, T5, T6) Tests del servicio. |
| `src/StockApp.Domain/Enums/AccionAuditada.cs` | (Modify, T6) 5 valores nuevos (46-50). |
| `src/StockApp.Api/Endpoints/TareasEndpoints.cs` | (Create, T7) Grupo `/tareas`. |
| `src/StockApp.Api/Program.cs` | (Modify, T7) DI + mapeo del endpoint. |
| `tests/StockApp.Api.Tests/TareasEndpointTests.cs` | (Create, T7) Matriz HTTP. |
| `src/StockApp.ApiClient/TareaApiClient.cs` | (Create, T8) Cliente HTTP. |
| `tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs` | (Create, T8) Serialización + mapeo de errores. |
| `src/StockApp.Presentation/App.axaml.cs` | (Modify, T8, T9, T10) DI de ApiClient + VMs. |
| `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs` | (Create, T9) Listado agrupado. |
| `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs` | (Create, T9) Agrupación, visibilidad por rol. |
| `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs` | (Create, T10) Alta + detalle + notas + prioridad. |
| `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs` | (Create, T10) Validaciones, hilo de notas. |
| `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml(.cs)` | (Create, T11) Vista de listado. |
| `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml(.cs)` | (Create, T11) Vista de alta/detalle. |
| `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs` | (Modify, T11) `NavTareasCommand`. |
| `src/StockApp.Presentation/Views/ShellMainView.axaml` | (Modify, T11) Botón del sidebar. |

---

## Task 1: Dominio — entidades, enums y máquina de estados

**Files:**
- Create: `src/StockApp.Domain/Entities/Tarea.cs`
- Create: `src/StockApp.Domain/Entities/NotaTarea.cs`
- Create: `src/StockApp.Domain/Enums/EstadoTarea.cs`
- Create: `src/StockApp.Domain/Enums/PrioridadTarea.cs`
- Test: `tests/StockApp.Domain.Tests/Entities/TareaTests.cs`

**Interfaces:**
- Consumes: nada (Domain no depende de otras capas).
- Produces: `Tarea`, `NotaTarea`, `EstadoTarea`, `PrioridadTarea`, `Tarea.CambiarEstado(EstadoTarea)` — consumidos por Task 2 (persistencia) y Task 3 (servicio).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Domain.Tests/Entities/TareaTests.cs
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.Domain.Tests.Entities;

public class TareaTests
{
    private static Tarea NuevaTarea(EstadoTarea estado = EstadoTarea.Pendiente) => new()
    {
        Titulo = "Reparar bache en calle Rivera",
        CreadaPorUsuarioId = 1,
        FechaCreacion = DateTime.UtcNow,
        Estado = estado,
    };

    // ── Transiciones válidas (decisión 5 del spec) ────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteAEnCurso_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.CambiarEstado(EstadoTarea.EnCurso);
        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoAPendiente_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Pendiente);
        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoATerminada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Terminada);
        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_PendienteACancelada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        tarea.CambiarEstado(EstadoTarea.Cancelada);
        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
    }

    [Fact]
    public void CambiarEstado_EnCursoACancelada_Permitido()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        tarea.CambiarEstado(EstadoTarea.Cancelada);
        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
    }

    // ── Transiciones inválidas ────────────────────────────────────────────────

    [Fact]
    public void CambiarEstado_PendienteATerminada_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.Terminada));
    }

    [Fact]
    public void CambiarEstado_PendienteAPendiente_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.Pendiente);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.Pendiente));
    }

    [Fact]
    public void CambiarEstado_EnCursoAEnCurso_LanzaReglaDeNegocio()
    {
        var tarea = NuevaTarea(EstadoTarea.EnCurso);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(EstadoTarea.EnCurso));
    }

    // ── Terminalidad: Terminada y Cancelada no tienen salida ──────────────────

    [Theory]
    [InlineData(EstadoTarea.Pendiente)]
    [InlineData(EstadoTarea.EnCurso)]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void CambiarEstado_DesdeTerminada_SiempreLanzaReglaDeNegocio(EstadoTarea destino)
    {
        var tarea = NuevaTarea(EstadoTarea.Terminada);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(destino));
    }

    [Theory]
    [InlineData(EstadoTarea.Pendiente)]
    [InlineData(EstadoTarea.EnCurso)]
    [InlineData(EstadoTarea.Terminada)]
    [InlineData(EstadoTarea.Cancelada)]
    public void CambiarEstado_DesdeCancelada_SiempreLanzaReglaDeNegocio(EstadoTarea destino)
    {
        var tarea = NuevaTarea(EstadoTarea.Cancelada);
        Assert.Throws<ReglaDeNegocioException>(() => tarea.CambiarEstado(destino));
    }

    [Fact]
    public void Tarea_Nueva_PrioridadPorDefectoEsMedia()
    {
        var tarea = new Tarea { Titulo = "x", CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow };
        Assert.Equal(PrioridadTarea.Media, tarea.Prioridad);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~TareaTests"`
Expected: FAIL — no compila (`Tarea`, `NotaTarea`, `EstadoTarea`, `PrioridadTarea` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Domain/Enums/EstadoTarea.cs
namespace StockApp.Domain.Enums;

public enum EstadoTarea
{
    Pendiente = 0,
    EnCurso   = 1,
    Terminada = 2,
    Cancelada = 3,
}
```

```csharp
// src/StockApp.Domain/Enums/PrioridadTarea.cs
namespace StockApp.Domain.Enums;

public enum PrioridadTarea
{
    Baja  = 0,
    Media = 1,
    Alta  = 2,
}
```

```csharp
// src/StockApp.Domain/Entities/NotaTarea.cs
namespace StockApp.Domain.Entities;

/// <summary>
/// Nota de una tarea, append-only (decisión 12 del spec): se agregan, nunca se editan ni
/// se borran — no hay métodos para eso ni en el dominio ni en el servicio. Sin nav de vuelta
/// a Tarea (mismo criterio que AsignacionPresupuestal → LineaPoa): la relación se configura
/// solo del lado padre en AppDbContext.
/// </summary>
public class NotaTarea
{
    public int Id { get; set; }
    public int TareaId { get; set; }
    public int UsuarioId { get; set; }
    public DateTime Fecha { get; set; }
    public string Texto { get; set; } = string.Empty;

    /// <summary>true si la generó el sistema (cambio de prioridad, acción sobre tarea ajena).</summary>
    public bool EsAutomatica { get; set; }
}
```

```csharp
// src/StockApp.Domain/Entities/Tarea.cs
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;

namespace StockApp.Domain.Entities;

/// <summary>
/// Tarea operativa del equipo (spec 2026-08-01). Módulo independiente: sin FK a otras
/// entidades del dominio (decisión 1). Lista común: se crea sin responsable, cualquiera
/// la toma (decisión 3). Sin baja lógica: Cancelada es un estado del ciclo de vida, no un
/// Activo=false (decisión 6). Guarda dos pares de trazabilidad independientes —
/// TomadaPor+FechaInicio (quién trabajó) y CerradaPor+FechaFin (quién cerró) — porque
/// cualquiera puede terminar o soltar una tarea ajena (decisión 11).
/// </summary>
public class Tarea
{
    public int Id { get; set; }
    public string Titulo { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public EstadoTarea Estado { get; set; } = EstadoTarea.Pendiente;
    public PrioridadTarea Prioridad { get; set; } = PrioridadTarea.Media;
    public DateTime? FechaLimite { get; set; }

    public int CreadaPorUsuarioId { get; set; }
    public DateTime FechaCreacion { get; set; }

    public int? TomadaPorUsuarioId { get; set; }

    /// <summary>Nav de solo lectura sobre TomadaPorUsuarioId: la grilla necesita el nombre
    /// del responsable actual (decisión 10) sin otra llamada — mismo criterio que
    /// Gasto.Proveedor para ProveedorNombre.</summary>
    public Usuario? TomadaPor { get; set; }
    public DateTime? FechaInicio { get; set; }

    public int? CerradaPorUsuarioId { get; set; }
    public DateTime? FechaFin { get; set; }

    public List<NotaTarea> Notas { get; set; } = new();

    private static readonly Dictionary<EstadoTarea, EstadoTarea[]> TransicionesValidas = new()
    {
        [EstadoTarea.Pendiente] = new[] { EstadoTarea.EnCurso, EstadoTarea.Cancelada },
        [EstadoTarea.EnCurso]   = new[] { EstadoTarea.Pendiente, EstadoTarea.Terminada, EstadoTarea.Cancelada },
        [EstadoTarea.Terminada] = Array.Empty<EstadoTarea>(),
        [EstadoTarea.Cancelada] = Array.Empty<EstadoTarea>(),
    };

    /// <summary>
    /// Valida y aplica la transición de estado (decisión 5 del spec). Rechaza cualquier
    /// combinación no listada en TransicionesValidas, incluida la identidad (ej.
    /// Pendiente→Pendiente): no existe una transición "sin cambios". Terminada y Cancelada
    /// no tienen salidas listadas: son terminales por construcción, no por un chequeo aparte.
    /// </summary>
    public void CambiarEstado(EstadoTarea destino)
    {
        if (!TransicionesValidas[Estado].Contains(destino))
            throw new ReglaDeNegocioException(
                $"No se puede pasar la tarea de '{Estado}' a '{destino}'.");
        Estado = destino;
    }
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Domain.Tests --filter "FullyQualifiedName~TareaTests"`
Expected: PASS — 17 tests verdes (5 transiciones válidas + 3 inválidas puntuales + 4+4 de terminalidad por `[Theory]` + 1 de prioridad por defecto).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Domain/Entities/Tarea.cs \
        src/StockApp.Domain/Entities/NotaTarea.cs \
        src/StockApp.Domain/Enums/EstadoTarea.cs \
        src/StockApp.Domain/Enums/PrioridadTarea.cs \
        tests/StockApp.Domain.Tests/Entities/TareaTests.cs
git commit -m "feat(tareas): agrega entidades Tarea/NotaTarea y maquina de estados"
```

---

## Task 2: Persistencia — mapeo, migración y repositorio

**Files:**
- Create: `src/StockApp.Application/Interfaces/ITareaRepository.cs`
- Create: `src/StockApp.Infrastructure/Repositories/TareaRepository.cs`
- Modify: `src/StockApp.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/StockApp.Infrastructure/Migrations/*_AgregaTareas.cs` (generado)
- Modify: `tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs`
- Test: `tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs`

**Interfaces:**
- Consumes: `Tarea`, `NotaTarea` (Task 1).
- Produces: `ITareaRepository.AgregarAsync/ObtenerPorIdAsync/ListarAsync/ActualizarAsync`, `TareaRepository` — consumidos por Task 3 (`TareaService`).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Infrastructure.Repositories;
using StockApp.Infrastructure.Tests.Fixtures;
using Xunit;

namespace StockApp.Infrastructure.Tests.Repositories;

public class TareaRepositoryTests : PostgresRepositoryTestBase
{
    private readonly TareaRepository _repo;

    public TareaRepositoryTests(PostgresFixture fixture) : base(fixture)
    {
        _repo = new TareaRepository(Context);
    }

    private static Tarea NuevaTarea(string titulo = "Reparar bache") => new()
    {
        Titulo = titulo,
        CreadaPorUsuarioId = 1,
        FechaCreacion = DateTime.UtcNow,
    };

    [Fact]
    public async Task AgregarAsync_ConNotas_Y_ObtenerPorId_TraeElHiloCompleto()
    {
        var tarea = NuevaTarea();
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow, Texto = "primera nota", EsAutomatica = false,
        });

        var id = await _repo.AgregarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);

        Assert.NotNull(encontrada);
        Assert.Equal("Reparar bache", encontrada!.Titulo);
        Assert.Equal(EstadoTarea.Pendiente, encontrada.Estado);
        Assert.Equal(PrioridadTarea.Media, encontrada.Prioridad);
        var nota = Assert.Single(encontrada.Notas);
        Assert.Equal("primera nota", nota.Texto);
    }

    [Fact]
    public async Task ListarAsync_DevuelveTodasLasTareas_SinFiltrarPorUsuario()
    {
        await _repo.AgregarAsync(NuevaTarea("Tarea A"));
        await _repo.AgregarAsync(NuevaTarea("Tarea B"));
        Context.ChangeTracker.Clear();

        var todas = await _repo.ListarAsync();

        Assert.Equal(2, todas.Count);
    }

    [Fact]
    public async Task ObtenerPorId_NotasOrdenadasPorFecha()
    {
        var tarea = NuevaTarea();
        var id = await _repo.AgregarAsync(tarea);

        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow.AddMinutes(-10), Texto = "vieja", EsAutomatica = false,
        });
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId = 1, Fecha = DateTime.UtcNow, Texto = "nueva", EsAutomatica = false,
        });
        await _repo.ActualizarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);

        Assert.Equal(2, encontrada!.Notas.Count);
        Assert.Equal("vieja", encontrada.Notas[0].Texto);
        Assert.Equal("nueva", encontrada.Notas[1].Texto);
    }

    [Fact]
    public async Task ActualizarAsync_PersisteElNuevoEstado()
    {
        var tarea = NuevaTarea();
        var id = await _repo.AgregarAsync(tarea);

        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = 1;
        tarea.FechaInicio = DateTime.UtcNow;
        await _repo.ActualizarAsync(tarea);
        Context.ChangeTracker.Clear();

        var encontrada = await _repo.ObtenerPorIdAsync(id);
        Assert.Equal(EstadoTarea.EnCurso, encontrada!.Estado);
        Assert.Equal(1, encontrada.TomadaPorUsuarioId);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~TareaRepositoryTests"`
Expected: FAIL — no compila (`TareaRepository`, `AppDbContext.Tareas`/`NotasTarea` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Interfaces/ITareaRepository.cs
using StockApp.Domain.Entities;

namespace StockApp.Application.Interfaces;

public interface ITareaRepository
{
    Task<int> AgregarAsync(Tarea tarea);
    Task<Tarea?> ObtenerPorIdAsync(int id);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary><paramref name="tarea"/> debe ser la instancia tracked de ObtenerPorIdAsync.</summary>
    Task ActualizarAsync(Tarea tarea);
}
```

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs
// Agregar el DbSet debajo de "public DbSet<CorridaBackup> CorridasBackup => Set<CorridaBackup>();":

    public DbSet<Tarea> Tareas => Set<Tarea>();
    public DbSet<NotaTarea> NotasTarea => Set<NotaTarea>();
```

```csharp
// src/StockApp.Infrastructure/Persistence/AppDbContext.cs
// Agregar DEBAJO del bloque "── Backups programados (Entrega 1) ──" en OnModelCreating:

        // ── Tareas (módulo independiente, spec 2026-08-01) ────────────────────
        modelBuilder.Entity<Tarea>(e =>
        {
            e.Property(t => t.Titulo).IsRequired();
            e.HasIndex(t => t.Estado);
            e.HasOne(t => t.TomadaPor).WithMany()
                .HasForeignKey(t => t.TomadaPorUsuarioId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotaTarea>(e =>
        {
            e.Property(n => n.Texto).IsRequired();
            e.HasIndex(n => n.TareaId);
            // Sin nav Tarea en NotaTarea (mismo criterio que AsignacionPresupuestal → LineaPoa):
            // la relación se configura solo del lado padre.
            e.HasOne<Tarea>().WithMany(t => t.Notas)
                .HasForeignKey(n => n.TareaId).OnDelete(DeleteBehavior.Restrict);
        });
```

```csharp
// src/StockApp.Infrastructure/Repositories/TareaRepository.cs
using Microsoft.EntityFrameworkCore;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Infrastructure.Persistence;

namespace StockApp.Infrastructure.Repositories;

public class TareaRepository : ITareaRepository
{
    private readonly AppDbContext _ctx;

    public TareaRepository(AppDbContext ctx) => _ctx = ctx;

    private IQueryable<Tarea> ConIncludes() =>
        _ctx.Tareas
            .Include(t => t.TomadaPor)
            .Include(t => t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id));

    public async Task<int> AgregarAsync(Tarea tarea)
    {
        _ctx.Tareas.Add(tarea);
        await _ctx.SaveChangesAsync();
        return tarea.Id;
    }

    public Task<Tarea?> ObtenerPorIdAsync(int id)
        => ConIncludes().FirstOrDefaultAsync(t => t.Id == id);

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
        => await ConIncludes().OrderByDescending(t => t.FechaCreacion).ToListAsync();

    /// <summary>
    /// Notas nuevas (Id == 0, agregadas por el servicio a la colección de una Tarea ya
    /// tracked): se agregan EXPLÍCITAMENTE al DbSet en vez de confiar en el fixup automático
    /// del change tracker sobre una colección modificada a mano — mismo criterio explícito que
    /// GastoRepository.AsignarGastoAMovimientosAsync (loop + asignación de FK + SaveChanges).
    /// </summary>
    public async Task ActualizarAsync(Tarea tarea)
    {
        foreach (var nota in tarea.Notas.Where(n => n.Id == 0))
        {
            nota.TareaId = tarea.Id;
            _ctx.NotasTarea.Add(nota);
        }

        _ctx.Tareas.Update(tarea);
        await _ctx.SaveChangesAsync();
    }
}
```

```csharp
// tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs
// Reemplazar el TRUNCATE por (agrega "NotasTarea", "Tareas" al final, hija antes que padre):

    private void LimpiarTablas()
    {
        using var ctx = Fixture.CrearContexto();
        ctx.Database.ExecuteSqlRaw(
            "TRUNCATE TABLE \"LogsAuditoria\", \"MovimientosStock\", \"Productos\", " +
            "\"Categorias\", \"Proveedores\", \"UnidadesMedida\", \"Usuarios\", " +
            "\"AsignacionesPresupuestales\", \"LineasPoa\", \"RubrosGasto\", \"FuentesFinanciamiento\", " +
            "\"AdjuntosContenido\", \"Adjuntos\", \"PagosGasto\", \"Gastos\", \"IngresosCaja\", " +
            "\"CorridasBackup\", \"NotasTarea\", \"Tareas\" RESTART IDENTITY CASCADE;");
    }
```

Run: `dotnet ef migrations add AgregaTareas --project src/StockApp.Infrastructure --startup-project src/StockApp.Api`
Expected: genera `Migrations/<timestamp>_AgregaTareas.cs` + `.Designer.cs` y actualiza `AppDbContextModelSnapshot.cs`, con dos tablas nuevas: `Tareas` (Id, Titulo, Descripcion, Estado, Prioridad, FechaLimite, CreadaPorUsuarioId, FechaCreacion, TomadaPorUsuarioId, FechaInicio, CerradaPorUsuarioId, FechaFin, FK `TomadaPorUsuarioId`→`Usuarios`, índice por `Estado`) y `NotasTarea` (Id, TareaId, UsuarioId, Fecha, Texto, EsAutomatica, FK `TareaId`→`Tareas`, índice por `TareaId`). El contenido generado NO se transcribe a mano en este plan — se genera con el comando y se inspecciona (mismo criterio que la migración `AgregaCorridaBackup`).

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Infrastructure.Tests --filter "FullyQualifiedName~TareaRepositoryTests"`
Expected: PASS — 4 tests verdes. `PostgresFixture` aplica la migración nueva automáticamente (`ctx.Database.MigrateAsync()` en `InitializeAsync`), no hace falta ningún paso manual adicional.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Interfaces/ITareaRepository.cs \
        src/StockApp.Infrastructure/Repositories/TareaRepository.cs \
        src/StockApp.Infrastructure/Persistence/AppDbContext.cs \
        src/StockApp.Infrastructure/Migrations/ \
        tests/StockApp.Infrastructure.Tests/Fixtures/PostgresRepositoryTestBase.cs \
        tests/StockApp.Infrastructure.Tests/Repositories/TareaRepositoryTests.cs
git commit -m "feat(tareas): agrega persistencia con migracion Tareas/NotasTarea"
```

---

## Task 3: Servicio — alta y listado, con permisos

**Files:**
- Create: `src/StockApp.Application/Tareas/ITareaService.cs`
- Create: `src/StockApp.Application/Tareas/TareaService.cs`
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `src/StockApp.Application/Authorization/AuthorizationService.cs`
- Test: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: `ITareaRepository` (Task 2); `ICurrentSession`, `IAuthorizationService` (ya existen).
- Produces: `ITareaService` (8 métodos: `CrearAsync`, `ListarAsync`, `TomarAsync`, `SoltarAsync`, `TerminarAsync`, `CancelarAsync`, `CambiarPrioridadAsync`, `AgregarNotaAsync` — los últimos 6 se implementan en Tasks 4-6), `Permisos.GestionarTareas` — consumidos por Task 4-11.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
using Moq;
using StockApp.Application.Authorization;
using StockApp.Application.Auth;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Application.Tests.Tareas;

public class TareaServiceTests
{
    private static (TareaService Svc, Mock<ITareaRepository> Repo,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo    = new Mock<ITareaRepository>();
        var session = new Mock<ICurrentSession>();
        var auth    = new Mock<IAuthorizationService>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, session.Object, auth.Object);
        return (svc, repo, session, auth);
    }

    // ── CrearAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrearAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" }));

        ctx.Repo.Verify(r => r.AgregarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task CrearAsync_TituloVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.CrearAsync(new Tarea { Titulo = "  " }));
    }

    [Fact]
    public async Task CrearAsync_PrioridadSiempreMedia_AunSiLlegaOtraEnLaEntidad()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(1);
        var tarea = new Tarea { Titulo = "Reparar bache", Prioridad = PrioridadTarea.Alta };

        await ctx.Svc.CrearAsync(tarea);

        ctx.Repo.Verify(r => r.AgregarAsync(
            It.Is<Tarea>(t => t.Prioridad == PrioridadTarea.Media)), Times.Once);
    }

    [Fact]
    public async Task CrearAsync_DatosValidos_DelegaAlRepoYDevuelveId()
    {
        var ctx = Crear(idSesion: 7);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(42);

        var id = await ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" });

        Assert.Equal(42, id);
        ctx.Repo.Verify(r => r.AgregarAsync(It.Is<Tarea>(t =>
            t.Titulo == "Reparar bache" && t.CreadaPorUsuarioId == 7 && t.Estado == EstadoTarea.Pendiente)),
            Times.Once);
    }

    // ── ListarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListarAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.ListarAsync());
    }

    [Fact]
    public async Task ListarAsync_DelegaAlRepo()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ListarAsync())
            .ReturnsAsync(new List<Tarea> { new() { Id = 1, Titulo = "x" } });

        var tareas = await ctx.Svc.ListarAsync();

        Assert.Single(tareas);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL — no compila (`TareaService`, `ITareaService`, `Permisos.GestionarTareas` no existen).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar la constante DEBAJO de "GestionarDiagnostico" y agregarla también a "Todos":

    // Tareas (spec 2026-08-01) — módulo independiente. GestionarTareas: crear, tomar,
    // soltar, terminar, comentar — Admin Y Operador. AdministrarTareas (Task 5): cancelar
    // y cambiar prioridad — solo Admin.
    public const string GestionarTareas = "tareas.gestionar";
```

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar "GestionarTareas," a la lista "Todos" (junto a GestionarDiagnostico):

    public static readonly IReadOnlyList<string> Todos =
    [
        GestionarUsuarios,
        VerReportes,
        GestionarProductos,
        GestionarTablasMaestras,
        RegistrarMovimientos,
        RecalcularStock,
        VerFinanzas,
        GestionarMaestrosFinanzas,
        RegistrarGastos,
        RegistrarPagos,
        RegistrarIngresos,
        ImportarPlanillas,
        GestionarDiagnostico,
        GestionarTareas,
    ];
```

```csharp
// src/StockApp.Application/Authorization/AuthorizationService.cs
// Agregar "Permisos.GestionarTareas," al HashSet AccionesOperador:

    private static readonly HashSet<string> AccionesOperador =
    [
        Permisos.GestionarProductos,
        Permisos.RegistrarMovimientos,
        Permisos.RecalcularStock,
        Permisos.VerFinanzas,
        Permisos.GestionarMaestrosFinanzas,
        Permisos.RegistrarGastos,
        Permisos.RegistrarPagos,
        Permisos.RegistrarIngresos,
        Permisos.GestionarTareas,
    ];
```

```csharp
// src/StockApp.Application/Tareas/ITareaService.cs
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Tareas;

/// <summary>
/// Tareas operativas del equipo (spec 2026-08-01): lista común sin asignación previa,
/// máquina de estados en el dominio (Tarea.CambiarEstado), notas append-only.
/// </summary>
public interface ITareaService
{
    /// <summary>Alta. La prioridad SIEMPRE se fuerza a Media, sin importar lo que traiga <paramref name="tarea"/>.</summary>
    Task<int> CrearAsync(Tarea tarea);

    /// <summary>Todas las tareas, sin filtrar por usuario (decisión 10 del spec).</summary>
    Task<IReadOnlyList<Tarea>> ListarAsync();

    /// <summary>Pendiente → EnCurso. Registra quién la tomó. Implementado en Task 4.</summary>
    Task TomarAsync(int id);

    /// <summary>EnCurso → Pendiente. Limpia el responsable. Implementado en Task 4.</summary>
    Task SoltarAsync(int id);

    /// <summary>EnCurso → Terminada. Registra quién la cerró. Implementado en Task 4.</summary>
    Task TerminarAsync(int id);

    /// <summary>Pendiente/EnCurso → Cancelada. Solo Admin. Implementado en Task 5.</summary>
    Task CancelarAsync(int id);

    /// <summary>Cambia la prioridad. Solo Admin. Genera nota automática. Implementado en Task 5.</summary>
    Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad);

    /// <summary>Nota manual. Las notas son append-only: no hay método para editarlas ni
    /// borrarlas. Implementado en Task 6.</summary>
    Task AgregarNotaAsync(int id, string texto);
}
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Application.Tareas;

/// <summary>
/// Servicio de tareas. Patrón: auth → validación → mutación de la entidad (la máquina de
/// estados vive en Tarea.CambiarEstado) → persistencia. Tomar/Soltar/Terminar se
/// implementan en Task 4 (agrega IUsuarioRepository al constructor, para resolver nombres
/// en las notas automáticas); Cancelar/CambiarPrioridad en Task 5; AgregarNota + auditoría
/// en Task 6 (agrega IAuditLogger).
/// </summary>
public class TareaService : ITareaService
{
    private readonly ITareaRepository      _repo;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;

    public TareaService(ITareaRepository repo, ICurrentSession session, IAuthorizationService auth)
    {
        _repo    = repo;
        _session = session;
        _auth    = auth;
    }

    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(tarea.Titulo))
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(tarea.Titulo));

        // Decisión 8 del spec: la prioridad nace SIEMPRE en Media, incluso si el llamador
        // (Admin incluido) trae otra cosa en la entidad.
        tarea.Estado              = EstadoTarea.Pendiente;
        tarea.Prioridad           = PrioridadTarea.Media;
        tarea.CreadaPorUsuarioId  = _session.UsuarioActual!.Id;
        tarea.FechaCreacion       = DateTime.UtcNow;
        tarea.TomadaPorUsuarioId  = null;
        tarea.FechaInicio         = null;
        tarea.CerradaPorUsuarioId = null;
        tarea.FechaFin            = null;

        return await _repo.AgregarAsync(tarea);
    }

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);
        return await _repo.ListarAsync();
    }

    public Task TomarAsync(int id) => throw new NotImplementedException();                                     // Task 4
    public Task SoltarAsync(int id) => throw new NotImplementedException();                                    // Task 4
    public Task TerminarAsync(int id) => throw new NotImplementedException();                                  // Task 4
    public Task CancelarAsync(int id) => throw new NotImplementedException();                                  // Task 5
    public Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad) => throw new NotImplementedException(); // Task 5
    public Task AgregarNotaAsync(int id, string texto) => throw new NotImplementedException();                 // Task 6
}
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS — 6 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/ITareaService.cs \
        src/StockApp.Application/Tareas/TareaService.cs \
        src/StockApp.Application/Authorization/Permisos.cs \
        src/StockApp.Application/Authorization/AuthorizationService.cs \
        tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): agrega servicio con alta y listado"
```

---

## Task 4: Transiciones — tomar, soltar, terminar

**Files:**
- Modify: `src/StockApp.Application/Tareas/TareaService.cs`
- Modify: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: `IUsuarioRepository.ObtenerPorIdAsync(int)` (ya existe, `src/StockApp.Application/Interfaces/IUsuarioRepository.cs`) — para resolver el nombre del usuario ajeno en las notas automáticas (decisión 11: "García terminó una tarea tomada por Juan").
- Produces: `TareaService.TomarAsync/SoltarAsync/TerminarAsync` implementados — consumidos por Task 7 (endpoint) y Task 9 (ViewModel de listado).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`, dentro de la clase (después de los tests de `ListarAsync`):

```csharp
    // ── TomarAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task TomarAsync_SinPermiso_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.TomarAsync(1));

        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }

    [Fact]
    public async Task TomarAsync_TareaInexistente_LanzaEntidadNoEncontrada()
    {
        var ctx = Crear();
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync((Tarea?)null);

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => ctx.Svc.TomarAsync(1));
    }

    [Fact]
    public async Task TomarAsync_DesdePendiente_CambiaAEnCursoYRegistraResponsable()
    {
        var ctx = Crear(idSesion: 3);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.TomarAsync(1);

        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
        Assert.Equal(3, tarea.TomadaPorUsuarioId);
        Assert.NotNull(tarea.FechaInicio);
        ctx.Repo.Verify(r => r.ActualizarAsync(tarea), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_DesdeEnCurso_LanzaReglaDeNegocio()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.EnCurso };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => ctx.Svc.TomarAsync(1));
    }

    // ── SoltarAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SoltarAsync_DesdeEnCurso_DejaLaTareaPendienteYLimpiaResponsable()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea
        {
            Id = 1, Titulo = "x", Estado = EstadoTarea.EnCurso,
            TomadaPorUsuarioId = 1, FechaInicio = DateTime.UtcNow,
        };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.SoltarAsync(1);

        Assert.Equal(EstadoTarea.Pendiente, tarea.Estado);
        Assert.Null(tarea.TomadaPorUsuarioId);
        Assert.Null(tarea.FechaInicio);
    }

    [Fact]
    public async Task SoltarAsync_TareaAjena_GeneraNotaAutomaticaConNombres()
    {
        var ctx = Crear(idSesion: 1, nombreUsuario: "garcia");
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 99 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Usuarios.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync(new Usuario { Id = 99, NombreUsuario = "juan" });

        await ctx.Svc.SoltarAsync(5);

        var nota = Assert.Single(tarea.Notas);
        Assert.True(nota.EsAutomatica);
        Assert.Equal("garcia soltó una tarea tomada por juan.", nota.Texto);
    }

    [Fact]
    public async Task SoltarAsync_TareaPropia_NoGeneraNotaAutomatica()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.SoltarAsync(5);

        Assert.Empty(tarea.Notas);
    }

    // ── TerminarAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task TerminarAsync_DesdeEnCurso_CambiaATerminadaYRegistraCierre()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 1 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TerminarAsync(5);

        Assert.Equal(EstadoTarea.Terminada, tarea.Estado);
        Assert.Equal(1, tarea.CerradaPorUsuarioId);
        Assert.NotNull(tarea.FechaFin);
    }

    [Fact]
    public async Task TerminarAsync_TareaAjena_GeneraNotaAutomaticaConNombres()
    {
        var ctx = Crear(idSesion: 1, nombreUsuario: "garcia");
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.EnCurso, TomadaPorUsuarioId = 99 };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);
        ctx.Usuarios.Setup(u => u.ObtenerPorIdAsync(99)).ReturnsAsync(new Usuario { Id = 99, NombreUsuario = "juan" });

        await ctx.Svc.TerminarAsync(5);

        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("garcia terminó una tarea tomada por juan.", nota.Texto);
    }
```

Agregar los usings que hacen falta arriba del archivo (`StockApp.Domain.Exceptions` para `EntidadNoEncontradaException`/`ReglaDeNegocioException`):

```csharp
using StockApp.Domain.Exceptions;
```

Y reemplazar el helper `Crear()` por la versión que agrega `Mock<IUsuarioRepository>` (nombre nuevo `Usuarios`, agregado AL FINAL de la tupla — el resto de los tests ya escritos en Task 3 siguen compilando sin tocarlos, porque acceden a los campos por nombre (`ctx.Repo`, `ctx.Svc`, etc.), no por posición):

```csharp
    private static (TareaService Svc, Mock<ITareaRepository> Repo, Mock<IUsuarioRepository> Usuarios,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo     = new Mock<ITareaRepository>();
        var usuarios = new Mock<IUsuarioRepository>();
        var session  = new Mock<ICurrentSession>();
        var auth     = new Mock<IAuthorizationService>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, usuarios.Object, session.Object, auth.Object);
        return (svc, repo, usuarios, session, auth);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL — no compila (`TareaService` todavía tiene el constructor de 3 parámetros, `TomarAsync`/`SoltarAsync`/`TerminarAsync` lanzan `NotImplementedException`).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el using StockApp.Domain.Enums; por el bloque completo de usings:

using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el constructor y los campos por:

    private readonly ITareaRepository      _repo;
    private readonly IUsuarioRepository    _usuarios;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;

    public TareaService(
        ITareaRepository repo, IUsuarioRepository usuarios,
        ICurrentSession session, IAuthorizationService auth)
    {
        _repo     = repo;
        _usuarios = usuarios;
        _session  = session;
        _auth     = auth;
    }
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar las tres líneas "=> throw new NotImplementedException();  // Task 4" por:

    public async Task TomarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaInicio        = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);
    }

    public async Task SoltarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorAnteriorId = tarea.TomadaPorUsuarioId;

        tarea.CambiarEstado(EstadoTarea.Pendiente);
        tarea.TomadaPorUsuarioId = null;
        tarea.FechaInicio        = null;

        // Decisión 11 del spec: toda acción sobre una tarea ajena genera nota automática.
        if (tomadorAnteriorId is int tomadorId && tomadorId != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, tomadorId, "soltó"));

        await _repo.ActualizarAsync(tarea);
    }

    public async Task TerminarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorId = tarea.TomadaPorUsuarioId;

        tarea.CambiarEstado(EstadoTarea.Terminada);
        tarea.CerradaPorUsuarioId = actorId;
        tarea.FechaFin            = DateTime.UtcNow;

        if (tomadorId is int idTomador && idTomador != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, idTomador, "terminó"));

        await _repo.ActualizarAsync(tarea);
    }

    /// <summary>Formato exacto de la decisión 11 del spec: "García terminó una tarea tomada por Juan".</summary>
    private async Task<NotaTarea> NotaAjenaAsync(int actorId, int tomadorId, string verbo)
    {
        var actorNombre   = _session.UsuarioActual!.NombreUsuario;
        var tomador       = await _usuarios.ObtenerPorIdAsync(tomadorId);
        var tomadorNombre = tomador?.NombreUsuario ?? $"usuario {tomadorId}";

        return new NotaTarea
        {
            UsuarioId    = actorId,
            Fecha        = DateTime.UtcNow,
            Texto        = $"{actorNombre} {verbo} una tarea tomada por {tomadorNombre}.",
            EsAutomatica = true,
        };
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS — 15 tests verdes (6 de Task 3 + 9 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/TareaService.cs \
        tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): implementa tomar, soltar y terminar con notas automaticas"
```

---

## Task 5: Cancelar y cambiar prioridad (solo Admin)

**Files:**
- Modify: `src/StockApp.Application/Tareas/TareaService.cs`
- Modify: `src/StockApp.Application/Authorization/Permisos.cs`
- Modify: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: nada nuevo (usa `_repo`, `_session`, `_auth` ya inyectados en Task 3-4).
- Produces: `TareaService.CancelarAsync/CambiarPrioridadAsync` implementados, `Permisos.AdministrarTareas` — consumidos por Task 7 (endpoint, con `RequireAuthorization` distinto de `GestionarTareas`) y Task 10 (ViewModel de detalle).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`, dentro de la clase (después de los tests de `TerminarAsync`):

```csharp
    // ── CancelarAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CancelarAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.CancelarAsync(1));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CancelarAsync_ComoAdmin_CambiaACanceladaYRegistraCierre()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CancelarAsync(1);

        Assert.Equal(EstadoTarea.Cancelada, tarea.Estado);
        Assert.Equal(1, tarea.CerradaPorUsuarioId);
    }

    // ── CambiarPrioridadAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CambiarPrioridadAsync_ComoOperador_LanzaExcepcionSinTocarElRepo()
    {
        var ctx = Crear(rol: RolUsuario.Operador);
        ctx.Auth.Setup(a => a.Verificar(RolUsuario.Operador, Permisos.AdministrarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Alta));

        ctx.Repo.Verify(r => r.ObtenerPorIdAsync(It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_ComoAdmin_CambiaLaPrioridadYGeneraNotaAutomatica()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 1, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Alta);

        Assert.Equal(PrioridadTarea.Alta, tarea.Prioridad);
        var nota = Assert.Single(tarea.Notas);
        Assert.Equal("Prioridad: Media → Alta", nota.Texto);
        Assert.True(nota.EsAutomatica);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_MismaPrioridad_NoHaceNadaYNoGeneraNota()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 1, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(1, PrioridadTarea.Media);

        Assert.Empty(tarea.Notas);
        ctx.Repo.Verify(r => r.ActualizarAsync(It.IsAny<Tarea>()), Times.Never);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL — no compila (`Permisos.AdministrarTareas` no existe; `CancelarAsync`/`CambiarPrioridadAsync` lanzan `NotImplementedException`).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar DEBAJO de GestionarTareas (NO se agrega al HashSet AccionesOperador de
// AuthorizationService — es la primera vez que GestionarTareas y AdministrarTareas
// distinguen Admin de Operador dentro del mismo módulo funcional):

    /// <summary>Cancelar y cambiar prioridad: decide sobre trabajo que otro cargó — solo Admin.</summary>
    public const string AdministrarTareas = "tareas.administrar";
```

```csharp
// src/StockApp.Application/Authorization/Permisos.cs
// Agregar "AdministrarTareas," a la lista "Todos" (junto a GestionarTareas):

        GestionarTareas,
        AdministrarTareas,
    ];
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Agregar los dos métodos DEBAJO de TerminarAsync (reemplaza los dos
// "=> throw new NotImplementedException();  // Task 5"):

    public async Task CancelarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        tarea.CambiarEstado(EstadoTarea.Cancelada);
        tarea.CerradaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaFin            = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);
    }

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        if (tarea.Prioridad == prioridad)
            return;   // sin cambios: no hay nada que registrar

        var anterior = tarea.Prioridad;
        tarea.Prioridad = prioridad;
        // Decisión 9 del spec: cada cambio de prioridad genera nota automática.
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"Prioridad: {anterior} → {prioridad}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS — 20 tests verdes (15 de Tasks 3-4 + 5 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/TareaService.cs \
        src/StockApp.Application/Authorization/Permisos.cs \
        tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): agrega cancelar y cambiar prioridad, solo admin"
```

---

## Task 6: Notas manuales y auditoría

**Files:**
- Modify: `src/StockApp.Application/Tareas/TareaService.cs`
- Modify: `src/StockApp.Domain/Enums/AccionAuditada.cs`
- Modify: `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`

**Interfaces:**
- Consumes: `IAuditLogger.RegistrarAsync(int, AccionAuditada, string, int, string)` (ya existe, `src/StockApp.Application/Interfaces/IAuditLogger.cs`).
- Produces: `TareaService.AgregarNotaAsync` implementado; auditoría retrofitteada en `CrearAsync/TomarAsync/SoltarAsync/TerminarAsync/CancelarAsync/CambiarPrioridadAsync`; `AccionAuditada.AltaTarea/CambioEstadoTarea/CambioPrioridadTarea/CancelacionTarea/AltaNotaTarea` — consumidos por Task 7 (endpoint) y por el módulo de auditoría existente (`AuditoriaLogViewModel`, sin cambios: ya lista genéricamente por `AccionAuditada`).

- [ ] **Step 1: Escribir el test que falla**

Agregar a `tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs`, dentro de la clase (después de los tests de `CambiarPrioridadAsync`):

```csharp
    // ── AgregarNotaAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task AgregarNotaAsync_SinPermiso_LanzaExcepcion()
    {
        var ctx = Crear();
        ctx.Auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), Permisos.GestionarTareas))
            .Throws<UnauthorizedAccessException>();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ctx.Svc.AgregarNotaAsync(1, "avance"));
    }

    [Fact]
    public async Task AgregarNotaAsync_TextoVacio_LanzaArgumentException()
    {
        var ctx = Crear();
        await Assert.ThrowsAsync<ArgumentException>(() => ctx.Svc.AgregarNotaAsync(1, "   "));
    }

    [Fact]
    public async Task AgregarNotaAsync_GuardaLaNotaConSuAutorYRegistraAuditoria()
    {
        var ctx = Crear(idSesion: 3);
        var tarea = new Tarea { Id = 1, Titulo = "x" };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(1)).ReturnsAsync(tarea);

        await ctx.Svc.AgregarNotaAsync(1, "avance del trabajo");

        var nota = Assert.Single(tarea.Notas);
        Assert.Equal(3, nota.UsuarioId);
        Assert.Equal("avance del trabajo", nota.Texto);
        Assert.False(nota.EsAutomatica);
        ctx.Audit.Verify(a => a.RegistrarAsync(3, AccionAuditada.AltaNotaTarea, "Tarea", 1, It.IsAny<string>()), Times.Once);
    }

    // ── Notas append-only: sin métodos para editar ni borrar ─────────────────

    [Fact]
    public void ITareaService_NoExponeMetodosParaEditarOBorrarNotas()
    {
        var metodos = typeof(ITareaService).GetMethods().Select(m => m.Name).ToList();

        Assert.DoesNotContain(metodos, n =>
            n.Contains("Editar", StringComparison.OrdinalIgnoreCase) && n.Contains("Nota", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metodos, n =>
            n.Contains("Borrar", StringComparison.OrdinalIgnoreCase) && n.Contains("Nota", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(metodos, n =>
            n.Contains("Eliminar", StringComparison.OrdinalIgnoreCase) && n.Contains("Nota", StringComparison.OrdinalIgnoreCase));
    }

    // ── Auditoría en el resto de las acciones ─────────────────────────────────

    [Fact]
    public async Task CrearAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        ctx.Repo.Setup(r => r.AgregarAsync(It.IsAny<Tarea>())).ReturnsAsync(9);

        await ctx.Svc.CrearAsync(new Tarea { Titulo = "Reparar bache" });

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.AltaTarea, "Tarea", 9, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task TomarAsync_RegistraAuditoria()
    {
        var ctx = Crear(idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.TomarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CambioEstadoTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.CancelarAsync(5);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CancelacionTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_RegistraAuditoria()
    {
        var ctx = Crear(rol: RolUsuario.Admin, idSesion: 1);
        var tarea = new Tarea { Id = 5, Titulo = "x", Prioridad = PrioridadTarea.Media };
        ctx.Repo.Setup(r => r.ObtenerPorIdAsync(5)).ReturnsAsync(tarea);

        await ctx.Svc.CambiarPrioridadAsync(5, PrioridadTarea.Alta);

        ctx.Audit.Verify(a => a.RegistrarAsync(1, AccionAuditada.CambioPrioridadTarea, "Tarea", 5, It.IsAny<string>()), Times.Once);
    }
```

Reemplazar el helper `Crear()` por la versión que agrega `Mock<IAuditLogger>` (`Audit`, al final de la tupla — de nuevo, los tests ya escritos acceden por nombre y no necesitan tocarse):

```csharp
    private static (TareaService Svc, Mock<ITareaRepository> Repo, Mock<IUsuarioRepository> Usuarios,
                     Mock<ICurrentSession> Session, Mock<IAuthorizationService> Auth, Mock<IAuditLogger> Audit)
        Crear(RolUsuario rol = RolUsuario.Admin, int idSesion = 1, string nombreUsuario = "admin.test")
    {
        var repo     = new Mock<ITareaRepository>();
        var usuarios = new Mock<IUsuarioRepository>();
        var session  = new Mock<ICurrentSession>();
        var auth     = new Mock<IAuthorizationService>();
        var audit    = new Mock<IAuditLogger>();

        session.Setup(s => s.RolActual).Returns(rol);
        session.Setup(s => s.UsuarioActual).Returns(new UsuarioSesion(idSesion, nombreUsuario, rol, null));
        auth.Setup(a => a.Verificar(It.IsAny<RolUsuario?>(), It.IsAny<string>()));

        var svc = new TareaService(repo.Object, usuarios.Object, session.Object, auth.Object, audit.Object);
        return (svc, repo, usuarios, session, auth, audit);
    }
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: FAIL — no compila (`AccionAuditada.AltaTarea` etc. no existen; el constructor de `TareaService` todavía tiene 4 parámetros; `AgregarNotaAsync` lanza `NotImplementedException`).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Domain/Enums/AccionAuditada.cs
// Agregar DEBAJO de "AnulacionIngresoPorFactura = 45,", respetando append-only:

    // ── Tareas (append-only a partir de 46) ───────────────────────────────────
    AltaTarea            = 46,
    CambioEstadoTarea    = 47,
    CambioPrioridadTarea = 48,
    CancelacionTarea     = 49,
    AltaNotaTarea        = 50,
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el bloque de usings del principio del archivo por:

using StockApp.Application.Authorization;
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el constructor y los campos por (agrega IAuditLogger):

    private readonly ITareaRepository      _repo;
    private readonly IUsuarioRepository    _usuarios;
    private readonly ICurrentSession       _session;
    private readonly IAuthorizationService _auth;
    private readonly IAuditLogger          _audit;

    public TareaService(
        ITareaRepository repo, IUsuarioRepository usuarios, ICurrentSession session,
        IAuthorizationService auth, IAuditLogger audit)
    {
        _repo     = repo;
        _usuarios = usuarios;
        _session  = session;
        _auth     = auth;
        _audit    = audit;
    }
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el cuerpo COMPLETO de CrearAsync por (agrega el bloque de auditoría al final):

    public async Task<int> CrearAsync(Tarea tarea)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(tarea.Titulo))
            throw new ArgumentException("El título de la tarea es obligatorio.", nameof(tarea.Titulo));

        tarea.Estado              = EstadoTarea.Pendiente;
        tarea.Prioridad           = PrioridadTarea.Media;
        tarea.CreadaPorUsuarioId  = _session.UsuarioActual!.Id;
        tarea.FechaCreacion       = DateTime.UtcNow;
        tarea.TomadaPorUsuarioId  = null;
        tarea.FechaInicio         = null;
        tarea.CerradaPorUsuarioId = null;
        tarea.FechaFin            = null;

        var id = await _repo.AgregarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaTarea, "Tarea", id,
            $"Título: {tarea.Titulo}" +
            (tarea.FechaLimite is not null ? $"; Vence: {tarea.FechaLimite:yyyy-MM-dd}" : string.Empty));

        return id;
    }
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar el cuerpo COMPLETO de TomarAsync/SoltarAsync/TerminarAsync/CancelarAsync/
// CambiarPrioridadAsync por (mismo cuerpo de Tasks 4-5 + una línea de auditoría antes del
// return; NotaAjenaAsync queda sin cambios):

    public async Task TomarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var estadoAnterior = tarea.Estado;
        tarea.CambiarEstado(EstadoTarea.EnCurso);
        tarea.TomadaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaInicio        = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado} (tomada)");
    }

    public async Task SoltarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorAnteriorId = tarea.TomadaPorUsuarioId;
        var estadoAnterior = tarea.Estado;

        tarea.CambiarEstado(EstadoTarea.Pendiente);
        tarea.TomadaPorUsuarioId = null;
        tarea.FechaInicio        = null;

        if (tomadorAnteriorId is int tomadorId && tomadorId != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, tomadorId, "soltó"));

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            actorId, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado} (soltada)");
    }

    public async Task TerminarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var actorId = _session.UsuarioActual!.Id;
        var tomadorId = tarea.TomadaPorUsuarioId;
        var estadoAnterior = tarea.Estado;

        tarea.CambiarEstado(EstadoTarea.Terminada);
        tarea.CerradaPorUsuarioId = actorId;
        tarea.FechaFin            = DateTime.UtcNow;

        if (tomadorId is int idTomador && idTomador != actorId)
            tarea.Notas.Add(await NotaAjenaAsync(actorId, idTomador, "terminó"));

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            actorId, AccionAuditada.CambioEstadoTarea, "Tarea", id,
            $"{estadoAnterior} → {tarea.Estado}");
    }

    public async Task CancelarAsync(int id)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        tarea.CambiarEstado(EstadoTarea.Cancelada);
        tarea.CerradaPorUsuarioId = _session.UsuarioActual!.Id;
        tarea.FechaFin            = DateTime.UtcNow;

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CancelacionTarea, "Tarea", id,
            $"Cancelación de '{tarea.Titulo}'");
    }

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        _auth.Verificar(_session.RolActual, Permisos.AdministrarTareas);

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        if (tarea.Prioridad == prioridad)
            return;

        var anterior = tarea.Prioridad;
        tarea.Prioridad = prioridad;
        tarea.Notas.Add(new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = $"Prioridad: {anterior} → {prioridad}",
            EsAutomatica = true,
        });

        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.CambioPrioridadTarea, "Tarea", id,
            $"{anterior} → {prioridad}");
    }
```

```csharp
// src/StockApp.Application/Tareas/TareaService.cs
// Reemplazar "public Task AgregarNotaAsync(int id, string texto) => throw new
// NotImplementedException();  // Task 6" por:

    public async Task AgregarNotaAsync(int id, string texto)
    {
        _auth.Verificar(_session.RolActual, Permisos.GestionarTareas);

        if (string.IsNullOrWhiteSpace(texto))
            throw new ArgumentException("El texto de la nota no puede estar vacío.", nameof(texto));

        var tarea = await _repo.ObtenerPorIdAsync(id)
            ?? throw new EntidadNoEncontradaException($"Tarea {id} no encontrada.");

        var nota = new NotaTarea
        {
            UsuarioId    = _session.UsuarioActual!.Id,
            Fecha        = DateTime.UtcNow,
            Texto        = texto.Trim(),
            EsAutomatica = false,
        };
        tarea.Notas.Add(nota);
        await _repo.ActualizarAsync(tarea);

        await _audit.RegistrarAsync(
            _session.UsuarioActual!.Id, AccionAuditada.AltaNotaTarea, "Tarea", id,
            $"Nota: {nota.Texto}");
    }
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~TareaServiceTests"`
Expected: PASS — 28 tests verdes (20 de Tasks 3-5 + 8 nuevos).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Application/Tareas/TareaService.cs \
        src/StockApp.Domain/Enums/AccionAuditada.cs \
        tests/StockApp.Application.Tests/Tareas/TareaServiceTests.cs
git commit -m "feat(tareas): agrega notas manuales y auditoria en todas las acciones"
```

---

## Task 7: Endpoints y DI

**Files:**
- Create: `src/StockApp.Api/Endpoints/TareasEndpoints.cs`
- Modify: `src/StockApp.Api/Program.cs`
- Test: `tests/StockApp.Api.Tests/TareasEndpointTests.cs`

**Interfaces:**
- Consumes: `ITareaService` (Tasks 3-6); `Permisos.GestionarTareas`/`AdministrarTareas`; `DomainExceptionHandler` (ya existente, mapea `ArgumentException`→400, `EntidadNoEncontradaException`→404, `ReglaDeNegocioException`→409, `UnauthorizedAccessException`→403 — no se duplica acá).
- Produces: `POST /tareas`, `GET /tareas`, `POST /tareas/{id}/tomar|soltar|terminar|cancelar|prioridad|notas`, `TareaDto`, `TareaCreadaResponse` — consumidos por Task 8 (`TareaApiClient`).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Api.Tests/TareasEndpointTests.cs
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using StockApp.Api.Auth;
using StockApp.Api.Endpoints;
using StockApp.Api.Tests.Fixtures;
using StockApp.Domain.Enums;
using Xunit;

namespace StockApp.Api.Tests;

public class TareasEndpointTests : ApiTestBase
{
    public TareasEndpointTests(ApiFactory factory) : base(factory) { }

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

    private async Task SeedUsuariosAsync()
    {
        await using var ctx = Factory.CrearContexto();
        await DatosDePrueba.SeedUsuarioAsync(ctx, "admin.test", "Secreta123!", RolUsuario.Admin);
        await DatosDePrueba.SeedUsuarioAsync(ctx, "operador.test", "Secreta123!", RolUsuario.Operador);
    }

    private async Task<int> CrearTareaAsync(HttpClient client, string titulo = "Reparar bache")
    {
        var response = await client.PostAsJsonAsync("/tareas", new CrearTareaRequest(titulo, null, null));
        var creada = await response.Content.ReadFromJsonAsync<TareaCreadaResponse>();
        return creada!.Id;
    }

    [Fact]
    public async Task PostTareas_SinToken_Devuelve401()
    {
        var response = await Factory.CreateClient()
            .PostAsJsonAsync("/tareas", new CrearTareaRequest("Reparar bache", null, null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostTareas_TituloVacio_Devuelve400()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync("/tareas", new CrearTareaRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostTareas_ConTokenOperador_Crea201()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsJsonAsync(
            "/tareas", new CrearTareaRequest("Reparar bache", "en calle Rivera", null));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var creada = await response.Content.ReadFromJsonAsync<TareaCreadaResponse>();
        Assert.True(creada!.Id > 0);
    }

    [Fact]
    public async Task GetTareas_ConTokenOperador_Devuelve200ConLaLista()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        await CrearTareaAsync(client);

        var response = await client.GetAsync("/tareas");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var tareas = await response.Content.ReadFromJsonAsync<List<TareaDto>>();
        Assert.Single(tareas!);
        Assert.Equal(PrioridadTarea.Media, tareas![0].Prioridad);
    }

    [Fact]
    public async Task PostTomar_TareaInexistente_Devuelve404()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());

        var response = await client.PostAsync("/tareas/9999/tomar", content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostTerminar_DesdePendiente_Devuelve409()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsync($"/tareas/{id}/terminar", content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task PostCancelar_ConTokenOperador_Devuelve403()
    {
        await SeedUsuariosAsync();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(clienteAdmin);
        var clienteOperador = ClienteAutenticado(TokenOperador());

        var response = await clienteOperador.PostAsync($"/tareas/{id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostCancelar_ConTokenAdmin_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsync($"/tareas/{id}/cancelar", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostPrioridad_ConTokenOperador_Devuelve403()
    {
        await SeedUsuariosAsync();
        var clienteAdmin = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(clienteAdmin);
        var clienteOperador = ClienteAutenticado(TokenOperador());

        var response = await clienteOperador.PostAsJsonAsync(
            $"/tareas/{id}/prioridad", new CambiarPrioridadRequest(PrioridadTarea.Alta));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostPrioridad_ConTokenAdmin_Devuelve200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenAdmin());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsJsonAsync(
            $"/tareas/{id}/prioridad", new CambiarPrioridadRequest(PrioridadTarea.Alta));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PostNotas_ConTokenOperador_Crea200()
    {
        await SeedUsuariosAsync();
        var client = ClienteAutenticado(TokenOperador());
        var id = await CrearTareaAsync(client);

        var response = await client.PostAsJsonAsync($"/tareas/{id}/notas", new AgregarNotaRequest("avance del día"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~TareasEndpointTests"`
Expected: FAIL — no compila (`TareasEndpoints`, `CrearTareaRequest`, `TareaDto`, etc. no existen; `/tareas` no está mapeado).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Api/Endpoints/TareasEndpoints.cs
using System.Linq;
using StockApp.Application.Authorization;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.Api.Endpoints;

public record NotaTareaDto(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

public record TareaDto(
    int Id, string Titulo, string? Descripcion,
    EstadoTarea Estado, PrioridadTarea Prioridad, DateTime? FechaLimite,
    int CreadaPorUsuarioId, DateTime FechaCreacion,
    int? TomadaPorUsuarioId, string? TomadaPorNombre, DateTime? FechaInicio,
    int? CerradaPorUsuarioId, DateTime? FechaFin,
    List<NotaTareaDto> Notas);

public record CrearTareaRequest(string Titulo, string? Descripcion, DateTime? FechaLimite);
public record CambiarPrioridadRequest(PrioridadTarea Prioridad);
public record AgregarNotaRequest(string Texto);
public record TareaCreadaResponse(int Id);

public static class TareasEndpoints
{
    public static IEndpointRouteBuilder MapTareasEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/tareas");

        group.MapPost("/", async (CrearTareaRequest request, ITareaService service) =>
        {
            var tarea = new Tarea
            {
                Titulo      = request.Titulo,
                Descripcion = request.Descripcion,
                FechaLimite = request.FechaLimite,
            };
            var id = await service.CrearAsync(tarea);
            return Results.Created((string?)null, new TareaCreadaResponse(id));
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapGet("/", async (ITareaService service) =>
            Results.Ok((await service.ListarAsync()).Select(ADto)))
            .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/tomar", async (int id, ITareaService service) =>
        {
            await service.TomarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/soltar", async (int id, ITareaService service) =>
        {
            await service.SoltarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/terminar", async (int id, ITareaService service) =>
        {
            await service.TerminarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        group.MapPost("/{id:int}/cancelar", async (int id, ITareaService service) =>
        {
            await service.CancelarAsync(id);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);

        group.MapPost("/{id:int}/prioridad", async (int id, CambiarPrioridadRequest request, ITareaService service) =>
        {
            await service.CambiarPrioridadAsync(id, request.Prioridad);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.AdministrarTareas);

        group.MapPost("/{id:int}/notas", async (int id, AgregarNotaRequest request, ITareaService service) =>
        {
            await service.AgregarNotaAsync(id, request.Texto);
            return Results.Ok();
        })
        .RequireAuthorization(Permisos.GestionarTareas);

        return app;
    }

    private static TareaDto ADto(Tarea t) => new(
        t.Id, t.Titulo, t.Descripcion,
        t.Estado, t.Prioridad, t.FechaLimite,
        t.CreadaPorUsuarioId, t.FechaCreacion,
        t.TomadaPorUsuarioId, t.TomadaPor?.NombreUsuario, t.FechaInicio,
        t.CerradaPorUsuarioId, t.FechaFin,
        t.Notas.OrderBy(n => n.Fecha).ThenBy(n => n.Id)
            .Select(n => new NotaTareaDto(n.Id, n.UsuarioId, n.Fecha, n.Texto, n.EsAutomatica))
            .ToList());
}
```

```csharp
// src/StockApp.Api/Program.cs
// Agregar el using junto al resto de StockApp.Application.*:

using StockApp.Application.Tareas;
```

```csharp
// src/StockApp.Api/Program.cs
// Agregar la DI DEBAJO del bloque "Finanzas — F5b: análisis..." (junto a los otros
// AddScoped de repos/servicios propios del módulo):

// Tareas — módulo independiente (spec 2026-08-01)
builder.Services.AddScoped<ITareaRepository, TareaRepository>();
builder.Services.AddScoped<ITareaService, TareaService>();
```

```csharp
// src/StockApp.Api/Program.cs
// Agregar el mapeo del endpoint junto al resto de app.MapXxxEndpoints():

app.MapTareasEndpoints();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Api.Tests --filter "FullyQualifiedName~TareasEndpointTests"`
Expected: PASS — 11 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Api/Endpoints/TareasEndpoints.cs \
        src/StockApp.Api/Program.cs \
        tests/StockApp.Api.Tests/TareasEndpointTests.cs
git commit -m "feat(tareas): agrega endpoints HTTP del modulo de tareas"
```

---

## Task 8: ApiClient

**Files:**
- Create: `src/StockApp.ApiClient/TareaApiClient.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs`

**Interfaces:**
- Consumes: `ITareaService` (Task 3-6, el mismo contrato que consumen los ViewModels); `ApiErrores.EnviarAsync/AsegurarExitoAsync` (ya existentes); `IdCreado` (record `internal` ya definido en `ApiErrores.cs`, reutilizado).
- Produces: `TareaApiClient` (implementa `ITareaService` contra `/tareas`) — consumido por Task 9-10 (ViewModels, vía DI).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs
using System.Net;
using StockApp.ApiClient;
using StockApp.ApiClient.Tests.TestInfra;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using Xunit;

namespace StockApp.ApiClient.Tests;

public class TareaApiClientTests
{
    [Fact]
    public async Task CrearAsync_POSTTareas_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new { id = 7 }, HttpStatusCode.Created));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var id = await client.CrearAsync(new Tarea { Titulo = "Reparar bache", Descripcion = "en calle Rivera" });

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas", fake.UltimaRequest.RequestUri!.AbsolutePath);
        Assert.Contains("\"titulo\":\"Reparar bache\"", fake.UltimoBody);
        Assert.Equal(7, id);
    }

    [Fact]
    public async Task CrearAsync_400_LanzaArgumentException()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.BadRequest, "El título de la tarea es obligatorio."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => client.CrearAsync(new Tarea { Titulo = "" }));
        Assert.Equal("El título de la tarea es obligatorio.", ex.Message);
    }

    [Fact]
    public async Task ListarAsync_GETTareas_DeserializaLaListaConTomadaPor()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Json(new[]
        {
            new
            {
                id = 1, titulo = "Reparar bache", descripcion = (string?)null,
                estado = EstadoTarea.EnCurso, prioridad = PrioridadTarea.Media, fechaLimite = (DateTime?)null,
                creadaPorUsuarioId = 1, fechaCreacion = new DateTime(2026, 8, 1),
                tomadaPorUsuarioId = 2, tomadaPorNombre = "juan", fechaInicio = new DateTime(2026, 8, 1),
                cerradaPorUsuarioId = (int?)null, fechaFin = (DateTime?)null,
                notas = Array.Empty<object>(),
            },
        }));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        var tareas = await client.ListarAsync();

        var tarea = Assert.Single(tareas);
        Assert.Equal(EstadoTarea.EnCurso, tarea.Estado);
        Assert.Equal("juan", tarea.TomadaPor!.NombreUsuario);
    }

    [Fact]
    public async Task TomarAsync_POSTTomar_ConLaRutaCorrecta()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.TomarAsync(5);

        Assert.Equal(HttpMethod.Post, fake.UltimaRequest!.Method);
        Assert.Equal("/tareas/5/tomar", fake.UltimaRequest.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task TomarAsync_404_LanzaEntidadNoEncontrada()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(HttpStatusCode.NotFound, "Tarea 5 no encontrada."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<EntidadNoEncontradaException>(() => client.TomarAsync(5));
    }

    [Fact]
    public async Task TerminarAsync_409_LanzaReglaDeNegocio()
    {
        var fake = new FakeHttpHandler(_ => TestHttp.Problema(
            HttpStatusCode.Conflict, "No se puede pasar la tarea de 'Pendiente' a 'Terminada'."));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await Assert.ThrowsAsync<ReglaDeNegocioException>(() => client.TerminarAsync(5));
    }

    [Fact]
    public async Task CambiarPrioridadAsync_POSTPrioridad_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.CambiarPrioridadAsync(5, PrioridadTarea.Alta);

        Assert.Equal("/tareas/5/prioridad", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"prioridad\":", fake.UltimoBody);
    }

    [Fact]
    public async Task AgregarNotaAsync_POSTNotas_SerializaElBody()
    {
        var fake = new FakeHttpHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var client = new TareaApiClient(TestHttp.CrearCliente(fake));

        await client.AgregarNotaAsync(5, "avance registrado");

        Assert.Equal("/tareas/5/notas", fake.UltimaRequest!.RequestUri!.AbsolutePath);
        Assert.Contains("\"texto\":\"avance registrado\"", fake.UltimoBody);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~TareaApiClientTests"`
Expected: FAIL — no compila (`TareaApiClient` no existe).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.ApiClient/TareaApiClient.cs
using System.Net.Http.Json;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;

namespace StockApp.ApiClient;

internal sealed record NotaTareaWire(int Id, int UsuarioId, DateTime Fecha, string Texto, bool EsAutomatica);

internal sealed record TareaWire(
    int Id, string Titulo, string? Descripcion,
    EstadoTarea Estado, PrioridadTarea Prioridad, DateTime? FechaLimite,
    int CreadaPorUsuarioId, DateTime FechaCreacion,
    int? TomadaPorUsuarioId, string? TomadaPorNombre, DateTime? FechaInicio,
    int? CerradaPorUsuarioId, DateTime? FechaFin,
    List<NotaTareaWire> Notas);

internal sealed record CrearTareaBody(string Titulo, string? Descripcion, DateTime? FechaLimite);
internal sealed record CambiarPrioridadBody(PrioridadTarea Prioridad);
internal sealed record AgregarNotaBody(string Texto);

/// <summary>ITareaService contra /tareas.</summary>
public sealed class TareaApiClient : ITareaService
{
    private readonly HttpClient _http;

    public TareaApiClient(HttpClient http) => _http = http;

    public async Task<int> CrearAsync(Tarea tarea)
    {
        var body = new CrearTareaBody(tarea.Titulo, tarea.Descripcion, tarea.FechaLimite);
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsJsonAsync("tareas", body));
        await ApiErrores.AsegurarExitoAsync(response);

        var creada = await response.Content.ReadFromJsonAsync<IdCreado>()
            ?? throw new InvalidOperationException("Respuesta vacía del servidor al crear la tarea.");
        return creada.Id;
    }

    public async Task<IReadOnlyList<Tarea>> ListarAsync()
    {
        var response = await ApiErrores.EnviarAsync(() => _http.GetAsync("tareas"));
        await ApiErrores.AsegurarExitoAsync(response);

        var dtos = await response.Content.ReadFromJsonAsync<List<TareaWire>>() ?? new();
        return dtos.Select(AEntidad).ToList();
    }

    public Task TomarAsync(int id) => PostSinBodyAsync($"tareas/{id}/tomar");
    public Task SoltarAsync(int id) => PostSinBodyAsync($"tareas/{id}/soltar");
    public Task TerminarAsync(int id) => PostSinBodyAsync($"tareas/{id}/terminar");
    public Task CancelarAsync(int id) => PostSinBodyAsync($"tareas/{id}/cancelar");

    public async Task CambiarPrioridadAsync(int id, PrioridadTarea prioridad)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"tareas/{id}/prioridad", new CambiarPrioridadBody(prioridad)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    public async Task AgregarNotaAsync(int id, string texto)
    {
        var response = await ApiErrores.EnviarAsync(() =>
            _http.PostAsJsonAsync($"tareas/{id}/notas", new AgregarNotaBody(texto)));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private async Task PostSinBodyAsync(string ruta)
    {
        var response = await ApiErrores.EnviarAsync(() => _http.PostAsync(ruta, content: null));
        await ApiErrores.AsegurarExitoAsync(response);
    }

    private static Tarea AEntidad(TareaWire dto) => new()
    {
        Id = dto.Id,
        Titulo = dto.Titulo,
        Descripcion = dto.Descripcion,
        Estado = dto.Estado,
        Prioridad = dto.Prioridad,
        FechaLimite = dto.FechaLimite,
        CreadaPorUsuarioId = dto.CreadaPorUsuarioId,
        FechaCreacion = dto.FechaCreacion,
        TomadaPorUsuarioId = dto.TomadaPorUsuarioId,
        TomadaPor = dto.TomadaPorNombre is null
            ? null : new Usuario { Id = dto.TomadaPorUsuarioId!.Value, NombreUsuario = dto.TomadaPorNombre },
        FechaInicio = dto.FechaInicio,
        CerradaPorUsuarioId = dto.CerradaPorUsuarioId,
        FechaFin = dto.FechaFin,
        Notas = dto.Notas.Select(n => new NotaTarea
        {
            Id = n.Id, TareaId = dto.Id, UsuarioId = n.UsuarioId, Fecha = n.Fecha,
            Texto = n.Texto, EsAutomatica = n.EsAutomatica,
        }).ToList(),
    };
}
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar el using junto al resto de StockApp.Application.*:

using StockApp.Application.Tareas;
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<IIngresoPorFacturaService, IngresoPorFacturaApiClient>();":

        // ── Módulo Tareas (independiente de Finanzas, spec 2026-08-01) ────────
        services.AddTransient<ITareaService, TareaApiClient>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.ApiClient.Tests --filter "FullyQualifiedName~TareaApiClientTests"`
Expected: PASS — 8 tests verdes.

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.ApiClient/TareaApiClient.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.ApiClient.Tests/TareaApiClientTests.cs
git commit -m "feat(tareas): agrega TareaApiClient"
```

---

## Task 9: ViewModel de listado

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs`

**Interfaces:**
- Consumes: `ITareaService` (Task 3-8); `ICurrentSession`, `INavigationService`, `IConfirmacionService` (ya existen).
- Produces: `TareaListViewModel` (con `TareaFila` anidada: `Titulo`, `PrioridadTexto`, `FechaLimite`, `TomadaPorNombre`, `DiasParaVencer`, `PuedeTomar/Soltar/Terminar/Cancelar`), `MostrarCanceladas`, `Pendientes/EnCurso/Terminadas/Canceladas`, `NuevaCommand`, `VerDetalleCommand`, `TomarCommand/SoltarCommand/TerminarCommand/CancelarCommand`, `CargarAsync()` — consumidos por Task 11 (`TareaListView`).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class TareaListViewModelTests
{
    private static Tarea TareaDe(int id, EstadoTarea estado, int? tomadaPorId = null) => new()
    {
        Id = id, Titulo = $"Tarea {id}", Estado = estado, Prioridad = PrioridadTarea.Media,
        CreadaPorUsuarioId = 1, FechaCreacion = DateTime.UtcNow, TomadaPorUsuarioId = tomadaPorId,
    };

    private static (TareaListViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(IReadOnlyList<Tarea>? tareas = null, RolUsuario rol = RolUsuario.Operador)
    {
        var svc = new Mock<ITareaService>();
        svc.Setup(s => s.ListarAsync()).ReturnsAsync(tareas ?? new List<Tarea>());

        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);

        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(true);
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new TareaListViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public async Task CargarAsync_AgrupaTareasPorEstado()
    {
        var ctx = Crear(new List<Tarea>
        {
            TareaDe(1, EstadoTarea.Pendiente),
            TareaDe(2, EstadoTarea.EnCurso),
            TareaDe(3, EstadoTarea.Terminada),
            TareaDe(4, EstadoTarea.Cancelada),
        });

        await ctx.Vm.CargarAsync();

        Assert.Single(ctx.Vm.Pendientes);
        Assert.Single(ctx.Vm.EnCurso);
        Assert.Single(ctx.Vm.Terminadas);
        Assert.Single(ctx.Vm.Canceladas);
        Assert.Equal(1, ctx.Vm.Pendientes[0].Id);
        Assert.Equal(4, ctx.Vm.Canceladas[0].Id);
    }

    [Fact]
    public async Task CargarAsync_ConRolOperador_FilaNoPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Operador);

        await ctx.Vm.CargarAsync();

        Assert.False(ctx.Vm.Pendientes[0].PuedeCancelar);
        Assert.True(ctx.Vm.Pendientes[0].PuedeTomar);
    }

    [Fact]
    public async Task CargarAsync_ConRolAdmin_FilaPuedeCancelar()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);

        await ctx.Vm.CargarAsync();

        Assert.True(ctx.Vm.Pendientes[0].PuedeCancelar);
    }

    [Fact]
    public async Task TomarAsync_LlamaAlServicioYRecarga()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) });
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Pendientes[0];

        await ctx.Vm.TomarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.TomarAsync(1), Times.Once);
    }

    [Fact]
    public async Task CancelarAsync_SinConfirmar_NoLlamaAlServicio()
    {
        var ctx = Crear(new List<Tarea> { TareaDe(1, EstadoTarea.Pendiente) }, rol: RolUsuario.Admin);
        ctx.Confirm.Setup(c => c.PreguntarAsync(It.IsAny<string>())).ReturnsAsync(false);
        await ctx.Vm.CargarAsync();
        var fila = ctx.Vm.Pendientes[0];

        await ctx.Vm.CancelarCommand.ExecuteAsync(fila);

        ctx.Svc.Verify(s => s.CancelarAsync(It.IsAny<int>()), Times.Never);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaListViewModelTests"`
Expected: FAIL — no compila (`TareaListViewModel` no existe).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Fila de solo lectura de la lista de tareas: aplana la entidad y agrega la visibilidad de
/// acciones según estado de la fila y rol del usuario logueado (spec: "un Operador no ve las
/// acciones de Admin"). DiasParaVencer alimenta SignoNegativoBrushConverter para el resaltado
/// de vencidas: negativo cuando la fecha límite pasó y el estado sigue abierto, 0 en cualquier
/// otro caso (sin fecha límite, o estado terminal).
/// </summary>
public sealed class TareaFila
{
    public Tarea Tarea { get; }
    private readonly RolUsuario _rol;

    public TareaFila(Tarea tarea, RolUsuario rol)
    {
        Tarea = tarea;
        _rol = rol;
    }

    public int Id => Tarea.Id;
    public string Titulo => Tarea.Titulo;
    public string PrioridadTexto => Tarea.Prioridad.ToString();
    public DateTime? FechaLimite => Tarea.FechaLimite;
    public string? TomadaPorNombre => Tarea.TomadaPor?.NombreUsuario;

    public decimal DiasParaVencer =>
        Tarea.FechaLimite is null || Tarea.Estado is EstadoTarea.Terminada or EstadoTarea.Cancelada
            ? 0m
            : (decimal)(Tarea.FechaLimite.Value.Date - DateTime.UtcNow.Date).TotalDays;

    public bool PuedeTomar    => Tarea.Estado == EstadoTarea.Pendiente;
    public bool PuedeSoltar   => Tarea.Estado == EstadoTarea.EnCurso;
    public bool PuedeTerminar => Tarea.Estado == EstadoTarea.EnCurso;

    public bool PuedeCancelar =>
        _rol == RolUsuario.Admin && Tarea.Estado is EstadoTarea.Pendiente or EstadoTarea.EnCurso;
}

/// <summary>
/// Pantalla "Tareas": lista agrupada por estado, canceladas detrás de un filtro (spec).
/// La vista dispara CargarAsync() vía DataContextChanged (convención del proyecto).
/// </summary>
public partial class TareaListViewModel : ViewModelBase
{
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    [ObservableProperty] private bool _mostrarCanceladas;

    public ObservableCollection<TareaFila> Pendientes { get; } = new();
    public ObservableCollection<TareaFila> EnCurso { get; } = new();
    public ObservableCollection<TareaFila> Terminadas { get; } = new();
    public ObservableCollection<TareaFila> Canceladas { get; } = new();

    public TareaListViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public async Task CargarAsync()
    {
        try
        {
            var tareas = await _service.ListarAsync();
            var rol = _session.RolActual ?? RolUsuario.Operador;

            Pendientes.Clear();
            EnCurso.Clear();
            Terminadas.Clear();
            Canceladas.Clear();

            foreach (var tarea in tareas)
            {
                var fila = new TareaFila(tarea, rol);
                switch (tarea.Estado)
                {
                    case EstadoTarea.Pendiente: Pendientes.Add(fila); break;
                    case EstadoTarea.EnCurso:   EnCurso.Add(fila); break;
                    case EstadoTarea.Terminada: Terminadas.Add(fila); break;
                    case EstadoTarea.Cancelada: Canceladas.Add(fila); break;
                }
            }
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            await _confirmacion.InformarAsync(ex.Message);
        }
    }

    [RelayCommand]
    private void Nueva() => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaCrear());

    [RelayCommand]
    private void VerDetalle(TareaFila fila)
        => _navigation.Navegar<TareaFormViewModel>(vm => vm.CargarParaVer(fila.Tarea));

    [RelayCommand]
    private async Task TomarAsync(TareaFila fila)
    {
        try { await _service.TomarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        { await _confirmacion.InformarAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task SoltarAsync(TareaFila fila)
    {
        try { await _service.SoltarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        { await _confirmacion.InformarAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task TerminarAsync(TareaFila fila)
    {
        try { await _service.TerminarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        { await _confirmacion.InformarAsync(ex.Message); }
    }

    [RelayCommand]
    private async Task CancelarAsync(TareaFila fila)
    {
        var confirmar = await _confirmacion.PreguntarAsync($"¿Confirma cancelar la tarea \"{fila.Titulo}\"?");
        if (!confirmar) return;

        try { await _service.CancelarAsync(fila.Id); await CargarAsync(); }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        { await _confirmacion.InformarAsync(ex.Message); }
    }
}
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar el using junto al resto de ViewModels.*:

using StockApp.Presentation.ViewModels.Tareas;
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO del bloque "── Módulo Finanzas — F5d: importador de planillas ──":

        // ── Módulo Tareas (spec 2026-08-01) ───────────────────────────────────
        services.AddTransient<TareaListViewModel>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaListViewModelTests"`
Expected: PASS — 5 tests verdes. (`TareaFormViewModel`, referenciado por `Nueva`/`VerDetalle`, todavía no existe — se crea en Task 10; hasta entonces este archivo no compila solo: correr Task 9 y Task 10 en el mismo ciclo antes de testear, o dejar un stub vacío de `TareaFormViewModel` si se ejecutan por separado.)

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Tareas/TareaListViewModel.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaListViewModelTests.cs
git commit -m "feat(tareas): agrega TareaListViewModel con agrupacion por estado"
```

---

## Task 10: ViewModel de alta, detalle y notas

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs`
- Modify: `src/StockApp.Presentation/App.axaml.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs`

**Interfaces:**
- Consumes: `ITareaService` (Task 3-8); `ICurrentSession`, `INavigationService`, `IConfirmacionService`.
- Produces: `TareaFormViewModel` — doble uso: alta (`CargarParaCrear`, `Titulo`/`Descripcion`/`FechaLimiteSeleccionada`/`GuardarCommand`) y detalle de una tarea existente (`CargarParaVer`, `EstadoTexto`/`TomadaPorNombre`/hilo de `Notas`/`AgregarNotaCommand`/`CambiarPrioridadCommand` solo Admin) — consumido por Task 9 (`Nueva`/`VerDetalle`) y Task 11 (`TareaFormView`).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs
using System;
using System.Threading.Tasks;
using Moq;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;
using StockApp.Presentation.ViewModels.Tareas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Tareas;

public class TareaFormViewModelTests
{
    private static (TareaFormViewModel Vm, Mock<ITareaService> Svc, Mock<IConfirmacionService> Confirm)
        Crear(RolUsuario rol = RolUsuario.Admin)
    {
        var svc = new Mock<ITareaService>();
        var session = new Mock<ICurrentSession>();
        session.Setup(s => s.RolActual).Returns(rol);
        var nav = new Mock<INavigationService>();
        var confirm = new Mock<IConfirmacionService>();
        confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var vm = new TareaFormViewModel(svc.Object, session.Object, nav.Object, confirm.Object);
        return (vm, svc, confirm);
    }

    [Fact]
    public void CargarParaCrear_DejaLosCamposVaciosYModoAlta()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();

        Assert.True(ctx.Vm.EsNuevaTarea);
        Assert.Equal(string.Empty, ctx.Vm.Titulo);
        Assert.Empty(ctx.Vm.Notas);
    }

    [Fact]
    public void CargarParaVer_PopulaCamposDeSoloLecturaYElHiloDeNotas()
    {
        var ctx = Crear();
        var tarea = new Tarea
        {
            Id = 5, Titulo = "Reparar bache", Descripcion = "En calle Rivera",
            Estado = EstadoTarea.EnCurso, TomadaPor = new Usuario { NombreUsuario = "juan" },
        };
        tarea.Notas.Add(new NotaTarea { Texto = "primera nota", Fecha = DateTime.UtcNow });

        ctx.Vm.CargarParaVer(tarea);

        Assert.False(ctx.Vm.EsNuevaTarea);
        Assert.Equal("Reparar bache", ctx.Vm.Titulo);
        Assert.Single(ctx.Vm.Notas);
        Assert.Equal("juan", ctx.Vm.TomadaPorNombre);
    }

    [Fact]
    public void GuardarCommand_TituloVacio_NoSePuedeEjecutar()
    {
        var ctx = Crear();
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "   ";

        Assert.False(ctx.Vm.GuardarCommand.CanExecute(null));
    }

    [Fact]
    public async Task GuardarAsync_ConTitulo_CreaLaTareaYVuelveAlListado()
    {
        var ctx = Crear();
        ctx.Svc.Setup(s => s.CrearAsync(It.IsAny<Tarea>())).ReturnsAsync(9);
        ctx.Vm.CargarParaCrear();
        ctx.Vm.Titulo = "Reparar bache";

        await ctx.Vm.GuardarCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CrearAsync(It.Is<Tarea>(t => t.Titulo == "Reparar bache")), Times.Once);
    }

    [Fact]
    public async Task AgregarNotaAsync_SumaLaNotaAlHiloSinRecargarTodo()
    {
        var ctx = Crear();
        var tarea = new Tarea { Id = 5, Titulo = "Reparar bache" };
        ctx.Vm.CargarParaVer(tarea);
        ctx.Vm.NuevaNotaTexto = "avance del día";

        await ctx.Vm.AgregarNotaCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.AgregarNotaAsync(5, "avance del día"), Times.Once);
        Assert.Single(ctx.Vm.Notas);
        Assert.Equal("avance del día", ctx.Vm.Notas[0].Texto);
        Assert.Equal(string.Empty, ctx.Vm.NuevaNotaTexto);
        // La nota se suma localmente al hilo: no hace falta releer toda la tarea.
        ctx.Svc.Verify(s => s.ListarAsync(), Times.Never);
    }

    [Fact]
    public async Task CambiarPrioridadAsync_ComoAdmin_LlamaAlServicio()
    {
        var ctx = Crear(rol: RolUsuario.Admin);
        var tarea = new Tarea { Id = 5, Titulo = "x", Estado = EstadoTarea.Pendiente, Prioridad = PrioridadTarea.Media };
        ctx.Vm.CargarParaVer(tarea);
        ctx.Vm.PrioridadSeleccionada = PrioridadTarea.Alta;

        await ctx.Vm.CambiarPrioridadCommand.ExecuteAsync(null);

        ctx.Svc.Verify(s => s.CambiarPrioridadAsync(5, PrioridadTarea.Alta), Times.Once);
    }
}
```

- [ ] **Step 2: Correr el test y verificar que falla**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFormViewModelTests"`
Expected: FAIL — no compila (`TareaFormViewModel` no existe).

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Interfaces;
using StockApp.Application.Tareas;
using StockApp.Domain.Entities;
using StockApp.Domain.Enums;
using StockApp.Domain.Exceptions;
using StockApp.Presentation.Navigation;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.ViewModels.Tareas;

/// <summary>
/// Doble uso (spec: "el panel de detalle muestra la descripción y el hilo de notas"): modo
/// alta (EsNuevaTarea = true) para crear una tarea nueva, y modo detalle (EsNuevaTarea =
/// false) para ver una tarea existente, su hilo de notas y —solo Admin— cambiarle la
/// prioridad. Título/descripción/fecha límite NO se editan después de creada: ninguna
/// acción del módulo lo permite (fuera de alcance del spec: "reasignación explícita").
/// Cargar* son síncronos (sin combos que precargar), por eso TareaFormView.axaml.cs no
/// necesita wiring de DataContextChanged.
/// </summary>
public partial class TareaFormViewModel : ViewModelBase
{
    private readonly ITareaService        _service;
    private readonly ICurrentSession      _session;
    private readonly INavigationService   _navigation;
    private readonly IConfirmacionService _confirmacion;

    private int _idTarea;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(GuardarCommand))]
    private string _titulo = string.Empty;

    [ObservableProperty] private string? _descripcion;
    [ObservableProperty] private DateTime? _fechaLimiteSeleccionada;
    [ObservableProperty] private string _estadoTexto = string.Empty;
    [ObservableProperty] private string? _tomadaPorNombre;
    [ObservableProperty] private string? _mensajeError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MuestraCambioPrioridad))]
    private bool _esNuevaTarea = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AgregarNotaCommand))]
    private string _nuevaNotaTexto = string.Empty;

    [ObservableProperty] private PrioridadTarea _prioridadSeleccionada;

    public ObservableCollection<NotaTarea> Notas { get; } = new();
    public IReadOnlyList<PrioridadTarea> PrioridadesDisponibles { get; } =
        new[] { PrioridadTarea.Baja, PrioridadTarea.Media, PrioridadTarea.Alta };

    public bool EsAdmin => _session.RolActual == RolUsuario.Admin;
    public bool MuestraCambioPrioridad => EsAdmin && !EsNuevaTarea;

    public TareaFormViewModel(
        ITareaService service, ICurrentSession session,
        INavigationService navigation, IConfirmacionService confirmacion)
    {
        _service      = service;
        _session      = session;
        _navigation   = navigation;
        _confirmacion = confirmacion;
    }

    public void CargarParaCrear()
    {
        _idTarea = 0;
        EsNuevaTarea = true;
        Titulo = string.Empty;
        Descripcion = null;
        FechaLimiteSeleccionada = null;
        EstadoTexto = string.Empty;
        TomadaPorNombre = null;
        MensajeError = null;
        Notas.Clear();
    }

    public void CargarParaVer(Tarea tarea)
    {
        _idTarea = tarea.Id;
        EsNuevaTarea = false;
        Titulo = tarea.Titulo;
        Descripcion = tarea.Descripcion;
        FechaLimiteSeleccionada = tarea.FechaLimite;
        EstadoTexto = tarea.Estado.ToString();
        TomadaPorNombre = tarea.TomadaPor?.NombreUsuario;
        PrioridadSeleccionada = tarea.Prioridad;
        MensajeError = null;

        Notas.Clear();
        foreach (var nota in tarea.Notas)
            Notas.Add(nota);
    }

    private bool PuedeGuardar() => !string.IsNullOrWhiteSpace(Titulo);

    [RelayCommand(CanExecute = nameof(PuedeGuardar))]
    private async Task GuardarAsync()
    {
        MensajeError = null;
        try
        {
            await _service.CrearAsync(new Tarea
            {
                Titulo = Titulo,
                Descripcion = string.IsNullOrWhiteSpace(Descripcion) ? null : Descripcion,
                FechaLimite = FechaLimiteSeleccionada,
            });
            _navigation.Navegar<TareaListViewModel>();
        }
        catch (Exception ex) when (ex is ArgumentException or ReglaDeNegocioException)
        {
            MensajeError = ex.Message;
        }
    }

    private bool PuedeAgregarNota() => !string.IsNullOrWhiteSpace(NuevaNotaTexto);

    [RelayCommand(CanExecute = nameof(PuedeAgregarNota))]
    private async Task AgregarNotaAsync()
    {
        MensajeError = null;
        var texto = NuevaNotaTexto;
        try
        {
            await _service.AgregarNotaAsync(_idTarea, texto);
            Notas.Add(new NotaTarea { TareaId = _idTarea, Texto = texto, Fecha = DateTime.UtcNow, EsAutomatica = false });
            NuevaNotaTexto = string.Empty;
        }
        catch (Exception ex) when (ex is ArgumentException or ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private async Task CambiarPrioridadAsync()
    {
        MensajeError = null;
        try
        {
            await _service.CambiarPrioridadAsync(_idTarea, PrioridadSeleccionada);
            await _confirmacion.InformarAsync($"Prioridad actualizada a {PrioridadSeleccionada}.");
        }
        catch (Exception ex) when (ex is ReglaDeNegocioException or EntidadNoEncontradaException)
        {
            MensajeError = ex.Message;
        }
    }

    [RelayCommand]
    private void Volver() => _navigation.Navegar<TareaListViewModel>();
}
```

```csharp
// src/StockApp.Presentation/App.axaml.cs
// Agregar DEBAJO de "services.AddTransient<TareaListViewModel>();":

        services.AddTransient<TareaFormViewModel>();
```

- [ ] **Step 4: Correr el test y verificar que pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~TareaFormViewModelTests"`
Expected: PASS — 6 tests verdes.

Correr también los de Task 9, ahora que `TareaFormViewModel` existe:
Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~Tareas"`
Expected: PASS — 11 tests verdes (5 de `TareaListViewModelTests` + 6 de `TareaFormViewModelTests`).

- [ ] **Step 5: Commit**
```bash
git add src/StockApp.Presentation/ViewModels/Tareas/TareaFormViewModel.cs \
        src/StockApp.Presentation/App.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Tareas/TareaFormViewModelTests.cs
git commit -m "feat(tareas): agrega TareaFormViewModel con alta, detalle y notas"
```

---

## Task 11: Vistas AXAML, navegación y resaltado de vencidas

**Files:**
- Create: `src/StockApp.Presentation/Views/Tareas/TareaListView.axaml` (+ `.axaml.cs`)
- Create: `src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml` (+ `.axaml.cs`)
- Modify: `src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/ShellMainView.axaml`

**Interfaces:**
- Consumes: `TareaListViewModel`, `TareaFormViewModel` (Task 9-10); `SignoNegativoBrushConverter` (ya existe); `CalendarDatePickerFechaBehavior` (ya existe); `ObjectConverters`, `BoolConverters` (Avalonia core, sin xmlns extra).
- Produces: `TareaListView`, `TareaFormView`, `ShellMainViewModel.NavTareasCommand` — pantalla completa navegable desde el sidebar.

> **Instrucción especial de esta tarea (lección de la feature anterior):** en `2026-07-31-ingreso-por-factura.md` la suite quedó entera en verde con una pantalla inutilizable porque la vista no exponía controles para varias propiedades del ViewModel — los tests no lo detectaron porque asignaban esas propiedades por código, no por UI. Por eso el Step 4 de esta tarea es una **verificación inversa de cobertura**, obligatoria y verificable: cada propiedad pública editable y cada comando de `TareaListViewModel`/`TareaFormViewModel` tiene que tener un control en el AXAML, documentado en una tabla. Una propiedad sin control es un defecto, aunque compile y aunque los tests pasen. El Step 5 (validación visual en la app real) es un requisito, no una sugerencia.

- [ ] **Step 1: Crear `TareaListView`**

```xml
<!-- src/StockApp.Presentation/Views/Tareas/TareaListView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Tareas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Tareas.TareaListView"
             x:DataType="vm:TareaListViewModel">

    <DockPanel Margin="24">

        <Grid DockPanel.Dock="Top" ColumnDefinitions="*,Auto" Margin="0,0,0,16">
            <TextBlock Grid.Column="0" Text="Tareas" Classes="titulo-vista" />
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="12" VerticalAlignment="Center">
                <CheckBox Content="Mostrar canceladas" IsChecked="{Binding MostrarCanceladas}" />
                <Button Classes="primary" Content="Nueva tarea" Command="{Binding NuevaCommand}" />
            </StackPanel>
        </Grid>

        <ScrollViewer>
            <StackPanel Spacing="24">

                <StackPanel Spacing="8">
                    <TextBlock Text="Pendientes" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding Pendientes}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TareaFila">
                                <Border Classes="card" Margin="0,0,0,8">
                                    <Grid ColumnDefinitions="*,Auto,Auto,Auto">
                                        <StackPanel Grid.Column="0" Spacing="2">
                                            <TextBlock Text="{Binding Titulo}" FontWeight="SemiBold"
                                                       Foreground="{Binding DiasParaVencer, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />
                                            <TextBlock Text="{Binding PrioridadTexto}" Classes="caption" Opacity="0.7" />
                                            <TextBlock Text="{Binding FechaLimite, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                                       Classes="caption" Opacity="0.7"
                                                       IsVisible="{Binding FechaLimite, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="2" Classes="secondary" Content="Tomar"
                                                IsVisible="{Binding PuedeTomar}"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).TomarCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="3" Classes="danger" Content="Cancelar"
                                                IsVisible="{Binding PuedeCancelar}"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).CancelarCommand}"
                                                CommandParameter="{Binding}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="En curso" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding EnCurso}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TareaFila">
                                <Border Classes="card" Margin="0,0,0,8">
                                    <Grid ColumnDefinitions="*,Auto,Auto,Auto,Auto">
                                        <StackPanel Grid.Column="0" Spacing="2">
                                            <TextBlock Text="{Binding Titulo}" FontWeight="SemiBold"
                                                       Foreground="{Binding DiasParaVencer, Converter={x:Static conv:SignoNegativoBrushConverter.Instance}}" />
                                            <TextBlock Text="{Binding PrioridadTexto}" Classes="caption" Opacity="0.7" />
                                            <TextBlock Text="{Binding FechaLimite, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                                       Classes="caption" Opacity="0.7"
                                                       IsVisible="{Binding FechaLimite, Converter={x:Static ObjectConverters.IsNotNull}}" />
                                            <TextBlock Text="{Binding TomadaPorNombre, StringFormat='Tomada por {0}'}"
                                                       Classes="caption" Opacity="0.7" />
                                        </StackPanel>
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="2" Classes="secondary" Content="Soltar"
                                                IsVisible="{Binding PuedeSoltar}"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).SoltarCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="3" Classes="primary" Content="Terminar"
                                                IsVisible="{Binding PuedeTerminar}"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).TerminarCommand}"
                                                CommandParameter="{Binding}" />
                                        <Button Grid.Column="4" Classes="danger" Content="Cancelar"
                                                IsVisible="{Binding PuedeCancelar}"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).CancelarCommand}"
                                                CommandParameter="{Binding}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <StackPanel Spacing="8">
                    <TextBlock Text="Terminadas" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding Terminadas}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TareaFila">
                                <Border Classes="card" Margin="0,0,0,8">
                                    <Grid ColumnDefinitions="*,Auto">
                                        <TextBlock Grid.Column="0" Text="{Binding Titulo}" Opacity="0.8" />
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

                <StackPanel Spacing="8" IsVisible="{Binding MostrarCanceladas}">
                    <TextBlock Text="Canceladas" Classes="seccion" />
                    <ItemsControl ItemsSource="{Binding Canceladas}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate x:DataType="vm:TareaFila">
                                <Border Classes="card" Margin="0,0,0,8">
                                    <Grid ColumnDefinitions="*,Auto">
                                        <TextBlock Grid.Column="0" Text="{Binding Titulo}" Opacity="0.6"
                                                   TextDecorations="Strikethrough" />
                                        <Button Grid.Column="1" Classes="ghost" Content="Ver"
                                                Command="{Binding $parent[UserControl].((vm:TareaListViewModel)DataContext).VerDetalleCommand}"
                                                CommandParameter="{Binding}" />
                                    </Grid>
                                </Border>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </StackPanel>

            </StackPanel>
        </ScrollViewer>

    </DockPanel>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Tareas/TareaListView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Tareas;

namespace StockApp.Presentation.Views.Tareas;

public partial class TareaListView : UserControl
{
    public TareaListView()
    {
        InitializeComponent();

        // Las vistas no se auto-inicializan (gotcha del repo): la carga se dispara
        // cuando la navegación asigna el DataContext.
        DataContextChanged += async (_, _) =>
        {
            if (DataContext is TareaListViewModel vm)
                await vm.CargarAsync();
        };
    }
}
```

- [ ] **Step 2: Crear `TareaFormView`**

```xml
<!-- src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Tareas"
             xmlns:dom="using:StockApp.Domain.Entities"
             xmlns:beh="using:StockApp.Presentation.Behaviors"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="700" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Tareas.TareaFormView"
             x:DataType="vm:TareaFormViewModel">

    <ScrollViewer>
        <DockPanel Margin="24">

            <Border Classes="card" VerticalAlignment="Top">
                <StackPanel Spacing="12" MaxWidth="620" HorizontalAlignment="Left">

                    <TextBlock Text="Nueva tarea" Classes="titulo-vista" IsVisible="{Binding EsNuevaTarea}" />
                    <TextBlock Text="Detalle de la tarea" Classes="titulo-vista" IsVisible="{Binding !EsNuevaTarea}" />

                    <!-- Alta: título/descripción/fecha límite editables -->
                    <StackPanel Spacing="12" IsVisible="{Binding EsNuevaTarea}">
                        <TextBlock Text="Título" />
                        <TextBox Text="{Binding Titulo}" Watermark="Ej.: Reparar bache en calle Rivera" />

                        <TextBlock Text="Descripción (opcional)" />
                        <TextBox Text="{Binding Descripcion}" AcceptsReturn="True" Height="80"
                                  Watermark="Detalle del trabajo a realizar" />

                        <TextBlock Text="Fecha límite (opcional)" />
                        <CalendarDatePicker SelectedDate="{Binding FechaLimiteSeleccionada}"
                                            PlaceholderText="dd/mm/aaaa"
                                            SelectedDateFormat="Custom"
                                            CustomDateFormatString="dd/MM/yyyy"
                                            beh:CalendarDatePickerFechaBehavior.NormalizarFechaTipeada="True" />

                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <Button Classes="primary" Content="Guardar" Command="{Binding GuardarCommand}" />
                            <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}" />
                        </StackPanel>
                    </StackPanel>

                    <!-- Detalle de solo lectura: título/descripción/fecha límite/estado/tomada por -->
                    <StackPanel Spacing="6" IsVisible="{Binding !EsNuevaTarea}">
                        <TextBlock Text="{Binding Titulo}" FontWeight="SemiBold" FontSize="16" />
                        <TextBlock Text="{Binding Descripcion}" TextWrapping="Wrap" Opacity="0.85"
                                   IsVisible="{Binding Descripcion, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding FechaLimiteSeleccionada, StringFormat='Vence: {0:dd/MM/yyyy}'}"
                                   Classes="caption"
                                   IsVisible="{Binding FechaLimiteSeleccionada, Converter={x:Static ObjectConverters.IsNotNull}}" />
                        <TextBlock Text="{Binding EstadoTexto, StringFormat='Estado: {0}'}" Classes="caption" />
                        <TextBlock Text="{Binding TomadaPorNombre, StringFormat='Tomada por: {0}'}" Classes="caption"
                                   IsVisible="{Binding TomadaPorNombre, Converter={x:Static ObjectConverters.IsNotNull}}" />

                        <Button Classes="secondary" Content="Volver" Command="{Binding VolverCommand}"
                                HorizontalAlignment="Left" Margin="0,8,0,0" />
                    </StackPanel>

                    <!-- Cambio de prioridad: solo Admin, solo tarea existente -->
                    <StackPanel Spacing="6" IsVisible="{Binding MuestraCambioPrioridad}" Margin="0,8,0,0">
                        <TextBlock Text="Prioridad" Classes="seccion" />
                        <StackPanel Orientation="Horizontal" Spacing="8">
                            <ComboBox ItemsSource="{Binding PrioridadesDisponibles}"
                                      SelectedItem="{Binding PrioridadSeleccionada}"
                                      Width="160" />
                            <Button Classes="secondary" Content="Actualizar prioridad" Command="{Binding CambiarPrioridadCommand}" />
                        </StackPanel>
                    </StackPanel>

                    <!-- Hilo de notas: solo tarea existente -->
                    <StackPanel Spacing="6" IsVisible="{Binding !EsNuevaTarea}" Margin="0,8,0,0">
                        <TextBlock Text="Notas" Classes="seccion" />
                        <ItemsControl ItemsSource="{Binding Notas}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="dom:NotaTarea">
                                    <Border Classes="card" Margin="0,0,0,6" Padding="8">
                                        <StackPanel Spacing="2">
                                            <TextBlock Text="{Binding Texto}" TextWrapping="Wrap" />
                                            <TextBlock Text="{Binding Fecha, StringFormat='{}{0:dd/MM/yyyy HH:mm}'}"
                                                       Classes="caption" Opacity="0.7" />
                                        </StackPanel>
                                    </Border>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>

                        <TextBlock Text="Nueva nota" />
                        <TextBox Text="{Binding NuevaNotaTexto}" AcceptsReturn="True" Height="60"
                                  Watermark="Agregar una nota al hilo" />
                        <Button Classes="secondary" Content="Agregar nota" Command="{Binding AgregarNotaCommand}" />
                    </StackPanel>

                    <TextBlock Text="{Binding MensajeError}"
                               Foreground="Red"
                               TextWrapping="Wrap"
                               IsVisible="{Binding MensajeError, Converter={x:Static ObjectConverters.IsNotNull}}" />

                </StackPanel>
            </Border>

        </DockPanel>
    </ScrollViewer>

</UserControl>
```

```csharp
// src/StockApp.Presentation/Views/Tareas/TareaFormView.axaml.cs
using Avalonia.Controls;

namespace StockApp.Presentation.Views.Tareas;

/// <summary>
/// Sin DataContextChanged: CargarParaCrear/CargarParaVer son síncronos y ya corren ANTES
/// de que INavigationService.Navegar&lt;TVm&gt;(Action&lt;TVm&gt;) publique el VM como
/// DataContext — a diferencia de GastoFormView, acá no hay combos que precargar async.
/// </summary>
public partial class TareaFormView : UserControl
{
    public TareaFormView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Alta en la navegación del shell**

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar el using junto al resto de ViewModels.*:

using StockApp.Presentation.ViewModels.Tareas;
```

```csharp
// src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs
// Agregar el comando DEBAJO de "NavHistorialMovimientos" (Admin y Operador, sin
// restricción de rol — decisión 7 del spec: tareas.gestionar lo tienen los dos):

    // ── Tareas (spec 2026-08-01): Admin y Operador ────────────────────────────

    [RelayCommand]
    private void NavTareas()
    {
        SeccionActiva = "Tareas";
        _navigation.Navegar<TareaListViewModel>();
    }
```

```xml
<!-- src/StockApp.Presentation/Views/ShellMainView.axaml -->
<!-- Agregar DEBAJO del botón "Historial de movimientos" y ANTES de la sección "Finanzas" -->

<!-- Tareas: visible para Admin y Operador -->
<TextBlock Text="Tareas"
           Classes="caption"
           Foreground="{DynamicResource SidebarTextoBrush}"
           FontWeight="SemiBold"
           Margin="8,8,0,4"
           Opacity="0.6" />

<Button Command="{Binding NavTareasCommand}"
        Classes="ghost"
        Classes.active="{Binding SeccionActiva, Converter={x:Static ObjectConverters.Equal}, ConverterParameter=Tareas}"
        HorizontalAlignment="Stretch">
    <Grid ColumnDefinitions="Auto,*">
        <i:Icon Grid.Column="0" Value="mdi-checkbox-marked-outline" Foreground="{DynamicResource SidebarTextoBrush}" />
        <TextBlock Grid.Column="1" Text="Tareas" VerticalAlignment="Center"
                   Margin="10,0,0,0" TextTrimming="CharacterEllipsis" />
    </Grid>
</Button>
```

- [ ] **Step 4: Verificación inversa de cobertura (obligatoria)**

Recorrer TODA propiedad pública editable y TODO comando de `TareaListViewModel`/`TareaFila`/`TareaFormViewModel` y confirmar que tiene un control en el AXAML que lo enlaza. Resultado de esa verificación:

**`TareaListViewModel`**

| Miembro | Control en `TareaListView.axaml` |
|---|---|
| `MostrarCanceladas` | `CheckBox IsChecked` |
| `Pendientes` / `EnCurso` / `Terminadas` / `Canceladas` | 4 `ItemsControl.ItemsSource` |
| `NuevaCommand` | Botón "Nueva tarea" |
| `VerDetalleCommand` | Botón "Ver" en las 4 secciones |
| `TomarCommand` | Botón "Tomar" (Pendientes) |
| `SoltarCommand` | Botón "Soltar" (En curso) |
| `TerminarCommand` | Botón "Terminar" (En curso) |
| `CancelarCommand` | Botón "Cancelar" (Pendientes y En curso) |

**`TareaFila`** (item de las 4 secciones)

| Miembro | Control |
|---|---|
| `Titulo` | `TextBlock` en las 4 secciones |
| `PrioridadTexto` | `TextBlock` (Pendientes, En curso) |
| `FechaLimite` | `TextBlock "Vence: ..."` (Pendientes, En curso) |
| `TomadaPorNombre` | `TextBlock "Tomada por ..."` (En curso) |
| `DiasParaVencer` | `Foreground` del título vía `SignoNegativoBrushConverter` (Pendientes, En curso) |
| `PuedeTomar` / `PuedeSoltar` / `PuedeTerminar` / `PuedeCancelar` | `IsVisible` de cada botón de acción |
| `Id` | Sin control propio — se usa como `CommandParameter="{Binding}"` (la fila completa) de cada botón de acción, nunca se muestra aislado. |

**`TareaFormViewModel`**

| Miembro | Control en `TareaFormView.axaml` |
|---|---|
| `Titulo` | `TextBox` (alta) + `TextBlock` (detalle) |
| `Descripcion` | `TextBox` (alta) + `TextBlock` (detalle) |
| `FechaLimiteSeleccionada` | `CalendarDatePicker` (alta) + `TextBlock "Vence: ..."` (detalle) |
| `EstadoTexto` | `TextBlock "Estado: ..."` (detalle) |
| `TomadaPorNombre` | `TextBlock "Tomada por: ..."` (detalle) |
| `MensajeError` | `TextBlock` rojo al pie |
| `EsNuevaTarea` | `IsVisible` de las secciones de alta vs. detalle |
| `EsAdmin` | Consumido indirectamente vía `MuestraCambioPrioridad` (no hay control propio: no hace falta mostrar el rol, solo condicionar el panel) |
| `MuestraCambioPrioridad` | `IsVisible` del panel "Prioridad" |
| `PrioridadSeleccionada` / `PrioridadesDisponibles` | `ComboBox` del panel "Prioridad" |
| `NuevaNotaTexto` | `TextBox` "Nueva nota" |
| `Notas` | `ItemsControl` del hilo |
| `GuardarCommand` | Botón "Guardar" (alta) |
| `AgregarNotaCommand` | Botón "Agregar nota" |
| `CambiarPrioridadCommand` | Botón "Actualizar prioridad" |
| `VolverCommand` | Botón "Volver" (alta y detalle) |

No queda ninguna propiedad editable ni ningún comando sin control. `Id` y `EsAdmin` son las únicas excepciones, ambas justificadas arriba (no son datos que el usuario edite ni deban mostrarse por sí solos).

- [ ] **Step 5: Validación visual en la app real (requisito, no sugerencia)**

Con la API y el desktop corriendo (Postgres real, `stockapp-pg` levantado — convención del repo):

1. Login como Operador: navegar a "Tareas" desde el sidebar, crear una tarea con y sin fecha límite, confirmar que aparece en "Pendientes".
2. Tomarla: confirmar que pasa a "En curso" y muestra "Tomada por" con el nombre del Operador.
3. Soltarla y volver a tomarla desde otra sesión (Admin) para generar la nota automática de tarea ajena: abrir el detalle y confirmar que la nota aparece en el hilo con el texto exacto.
4. Terminarla: confirmar que pasa a "Terminada" y desaparece de las acciones disponibles.
5. Crear una segunda tarea con fecha límite en el pasado: confirmar que el título aparece en rojo (resaltado de vencida) mientras esté Pendiente o En curso, y que deja de estar en rojo si se cancela.
6. Login como Admin: confirmar que en Operador NO aparecían los botones "Cancelar" ni el panel "Prioridad", y que en Admin sí aparecen. Cancelar una tarea y cambiarle la prioridad a otra, confirmando la nota automática `Prioridad: Media → Alta`.
7. Confirmar que el filtro "Mostrar canceladas" oculta/muestra la sección correctamente.

Si cualquiera de estos pasos falla o un control no aparece donde el diseño lo pide, es un defecto de esta tarea — no se cierra hasta corregirlo, aunque los tests automatizados ya estén en verde.

- [ ] **Step 6: Commit**
```bash
git add src/StockApp.Presentation/Views/Tareas/ \
        src/StockApp.Presentation/ViewModels/ShellMainViewModel.cs \
        src/StockApp.Presentation/Views/ShellMainView.axaml
git commit -m "feat(tareas): agrega vistas, navegacion y resaltado de vencidas"
```
