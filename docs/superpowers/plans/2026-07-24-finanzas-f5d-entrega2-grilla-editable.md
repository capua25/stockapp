# F5d Entrega 2 — Grilla híbrida editable · Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convertir el Paso 2 del wizard de importación (hoy read-only con color, Entrega 1) en la primera grilla editable del repo: completar celdas faltantes (fuente/rubro/proveedor/fecha/monto), corregir condición de pago/vencimiento, declarar maestros nuevos y líneas POA nuevas, y descomponer visualmente el error 400 estructurado — para que la planilla real del municipio se cargue de punta a punta sin salir de la grilla.

**Architecture:** Capas de abajo hacia arriba: (1) `AnalisisImportacionService` gana un flag `EsNueva` por línea POA (comparando Hoja contra `ILineaPoaRepository`); (2) tres VMs de fila nuevas (`FilaGastoEditableVm`/`FilaIngresoEditableVm`/`FilaLineaPoaEditableVm`), heredando de `ObservableValidator` (CommunityToolkit.Mvvm), reemplazan el binding directo de los DTOs de análisis; (3) `NuevaImportacionViewModel` proyecta `ResultadoAnalisisDto` → colecciones de filas VM, agrega `HasErrors` de todas las filas para el gating de Confirmar, y mapea filas VM → `ConfirmarImportacionDto` (reemplazando el `MapearAConfirmacion` estático de Entrega 1); (4) `NuevaImportacionView.axaml` gana `DataGridTemplateColumn` con `CellTemplate`/`CellEditingTemplate` por celda editable, combos `IsEditable`, date-pickers con converter nuevo, y candado visual con desbloqueo por fila; (5) el catch de `ValidacionImportacionException` se descompone celda por celda en vez de mostrarse como texto plano.

**Tech Stack:** .NET 10, Avalonia 12.0.5/12.0.1 (`Avalonia.Controls.DataGrid` 12.0.1), CommunityToolkit.Mvvm 8.4.1 (`ObservableValidator`, `[ObservableProperty]`, `[NotifyDataErrorInfo]`, `[RelayCommand]`), `System.ComponentModel.DataAnnotations`, xUnit + Moq (Application/Presentation).

## Global Constraints

- Stack: .NET 10, Avalonia 12.0.5 (core) / `Avalonia.Controls.DataGrid` 12.0.1. `CommunityToolkit.Mvvm` 8.4.1 ya referenciado en `StockApp.Presentation.csproj:48` — soporta `ObservableValidator`.
- **`Avalonia.AppBuilder.WithDataAnnotationsValidation()` es OBLIGATORIO** (verificado: existe en `Avalonia.Controls.dll` 12.0.1, extension method sobre `AppBuilder`) — Avalonia 12 NO valida `DataAnnotations` por defecto. Sin esto, todos los `[Required]`/atributos custom de los VMs de fila quedan mudos (no error, simplemente nunca se llaman). Se agrega en `Program.cs:54-60`, en el chain de `BuildAvaloniaApp()`.
- `DataGrid.IsReadOnly="False"` explícito en el `<DataGrid>` Y en cada `DataGridTemplateColumn` editable — NO confiar en cascada (gotcha Avalonia 12, `DataGridCollectionView`).
- Date-pickers (`CalendarDatePicker`) bindean `DateTimeOffset?`, no `DateOnly?` (tipo real de `Fecha`/`FechaVencimiento` en los DTOs) → usar el converter `DateOnlyOffsetConverter` (Task 2), TwoWay explícito, manejo defensivo de null.
- Los VMs de fila (`FilaGastoEditableVm`/`FilaIngresoEditableVm`/`FilaLineaPoaEditableVm`) heredan de `ObservableValidator` — es el PRIMER uso de `ObservableValidator` en el repo (confirmado: cero hits de `ObservableValidator`/`DataAnnotations` en `src/` o `tests/` antes de este plan). No hay un VM existente del cual calcar el patrón de test; Task 3 lo establece desde cero.
- Combos "elegí existente o escribí nuevo" (Fuente/Rubro/Proveedor en Gastos e Ingresos) → `ComboBox IsEditable="True"` con `Text` bindeado TwoWay (no sólo `SelectedItem`). El repo NO usa `AutoCompleteBox` hoy; se introduce `ComboBox IsEditable` por primera vez.
- Celdas ya completas (no-null en el DTO de análisis) quedan bloqueadas por defecto; se desbloquean por fila completa con una acción `✎` explícita (`Desbloqueada`, toggle, sin confirmación). Celdas null/faltantes son editables SIEMPRE, sin importar `Desbloqueada`.
- DTOs de `StockApp.Application.Finanzas` se referencian en XAML con `xmlns:dto="using:StockApp.Application.Finanzas"` (namespace real, NO hay un namespace `.Dto` separado — confirmado en `NuevaImportacionView.axaml:4`).
- TDD estricto en TODAS las tasks: test que falla → correr y verificar el mensaje/tipo de fallo esperado → implementación mínima → correr y verificar que pasa → commit. Nunca escribir implementación antes que su test. Comando: `dotnet test tests/<Proyecto> --filter "FullyQualifiedName~NombreDeLaClase"`.
- Conventional commits en español, SIN `Co-Authored-By` ni atribución de IA.
- No usar `cat`/`grep`/`find`/`sed` para explorar durante la implementación — usar las herramientas dedicadas del entorno de ejecución.
- `StockApp.Presentation.Tests` NO tiene infraestructura Avalonia Headless (confirmado por comentario en `EstadoFilaBrushConverter.cs:16-17` y ausencia de paquete `Avalonia.Headless.XUnit` referenciado en ese proyecto). PERO el repo SÍ tiene un proyecto headless activo y con precedente real: `tests/StockApp.Presentation.UiTests` (`Avalonia.Headless.XUnit` + `Avalonia.Controls.DataGrid` 12.0.1 + xunit.v3, patrón `[AvaloniaFact]`), con dos precedentes exactos a calcar: `DataGridSortClickTests.cs` (click real de puntero sobre un `DataGrid` con `DataGridCollectionView`, reproduce/verifica el bug AvaloniaUI/Avalonia.Controls.DataGrid#232) y `MovimientoFormControlValidacionTests.cs` (monta la View real con un VM real + fakes hechos a mano — este proyecto NO referencia Moq, a diferencia de `StockApp.Presentation.Tests` — y valida bindings TwoWay reales). Por eso los puntos riesgosos de XAML puro de Tasks 7/8 (candado por celda, `ComboBox IsEditable`, regresión del bug #232 con edición inline sobre `DataGridCollectionView`) llevan un Step de test headless en `StockApp.Presentation.UiTests`, calcado de esos dos precedentes (detalle en el Step correspondiente de cada Task) — reemplaza lo que en una primera pasada de este plan había quedado sólo como verificación orgánica manual. Queda como verificación orgánica manual SOLO lo que genuinamente no es automatizable headless (layout/estética fina, flujo end-to-end contra las planillas reales del municipio). La lógica de negocio detrás de cada celda (validación, gating, mapeo) SÍ está 100% cubierta por tests de VM en Tasks 3-6, 9, 10, 11. Task 9 no suma un Step headless propio: su UI es `ItemsControl` (Nombre editable vía `TextBox` simple), no `DataGridTemplateColumn`, así que no atraviesa el mecanismo de `DataGridCollectionView`/candado-por-celda que motiva estos tests — su cobertura ya es 100% de VM (Steps 1-8 del propio Task).
- **Regresión del bug AvaloniaUI/Avalonia.Controls.DataGrid#232** (mencionado en `StockApp.Presentation.csproj:33`, fixeado en 12.0.1): editar una celda vía `DataGridCollectionView` y confirmar que commitea sin perder ni duplicar la fila. Cubierto por el Step de test headless de Task 7 (grilla de Gastos) y Task 8 (grilla de Líneas POA) en `StockApp.Presentation.UiTests` — la verificación orgánica manual en la app real (WSLg) queda como chequeo final de UX, no como el único gate.

## File Structure

**Backend (Application/Domain):**
- `src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs` (modificar) — agrega `bool EsNueva` a `LineaPoaAnalizadaDto`.
- `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs` (modificar) — inyecta `ILineaPoaRepository`, computa `EsNueva`.
- `tests/StockApp.Application.Tests/Finanzas/Fakes/LineaPoaRepositoryFake.cs` (crear) — fake in-memory de `ILineaPoaRepository`, mismo patrón que `RepositorioMaestrosFake.cs`.
- `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs` (modificar) — helper `Crear()` gana parámetro `lineasPoaExistentes`; tests nuevos de `EsNueva`.

**Presentation — infraestructura (converter, validación global):**
- `src/StockApp.Presentation/Converters/DateOnlyOffsetConverter.cs` (crear).
- `tests/StockApp.Presentation.Tests/Converters/DateOnlyOffsetConverterTests.cs` (crear).
- `src/StockApp.Presentation/Program.cs` (modificar) — `.WithDataAnnotationsValidation()`.

**Presentation — VMs de fila (el corazón del cambio):**
- `src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs` (crear).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaGastoEditableVmTests.cs` (crear).
- `src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs` (crear).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaIngresoEditableVmTests.cs` (crear).
- `src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs` (crear) — incluye `AsignacionLineaPoaVm`.
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaLineaPoaEditableVmTests.cs` (crear).

**Presentation — contenedor (proyección, gating, mapeo, maestros, errores):**
- `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs` (modificar en Tasks 6, 9, 10, 11).
- `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs` (modificar/crece en Tasks 6, 9, 10, 11).

**Presentation — XAML (grillas editables):**
- `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml` (modificar en Tasks 7, 8, 9).

---

### Task 1: Backend — flag `EsNueva` en `LineaPoaAnalizadaDto`

**Files:**
- Modify: `src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs`
- Modify: `src/StockApp.Application/Finanzas/AnalisisImportacionService.cs`
- Create: `tests/StockApp.Application.Tests/Finanzas/Fakes/LineaPoaRepositoryFake.cs`
- Modify: `tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs`

**Interfaces:**
- Consumes: `ILineaPoaRepository.ListarTodasAsync(): Task<IReadOnlyList<LineaPoa>>` (`src/StockApp.Application/Interfaces/ILineaPoaRepository.cs`, ya existe — NO tiene filtro por ejercicio, trae TODOS); `LineaPoa.Nombre`/`LineaPoa.Ejercicio` (`src/StockApp.Domain/Entities/LineaPoa.cs`).
- Produces: `LineaPoaAnalizadaDto` gana el campo posicional `bool EsNueva` (entre `Ejercicio` y `Estado`) — usado por Task 5 (`FilaLineaPoaEditableVm`) y Task 6 (proyección/agrupado).

**Decisión de diseño (el spec no lo especifica, se documenta acá):** `EsNueva` es puramente informativo — NO genera un `MotivoEstado` ni cambia `EstadoFila` (a diferencia de `ProveedorNuevo`/`FuenteDesconocida`/`RubroDesconocido`, que sí lo hacen). Razón: los tests existentes de `AnalisisImportacionServicePoaTests` (p. ej. `AnalizarAsync_LineaPoaConLiteralExistente_MapeaOk`) corren SIN ningún repo de líneas POA existentes, así que TODA línea de esos tests sería `EsNueva = true` — si eso disparara `EstadoFila.Advertencia`, esos tests (que assertan `EstadoFila.Ok` y `Motivos` vacío) se romperían sin que el diseño lo haya pedido explícitamente. Mantenerlo sin efecto en `Estado`/`Motivos` es no-breaking y suficiente: la UI (Task 5) sólo necesita el booleano para decidir si mostrar el campo `Programa`.

- [ ] **Step 1: Agregar el campo al DTO**

```csharp
// src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs
// Reemplazar la definición de LineaPoaAnalizadaDto (antes en líneas 62-67):

/// <summary>
/// Línea POA candidata (una hoja de la planilla POA) + su asignación presupuestal. EsNueva
/// (F5d Entrega 2 Task 1) indica que Hoja no matchea ninguna LineaPoa existente del Ejercicio —
/// puramente informativo para la UI (NO genera MotivoEstado ni afecta Estado): a diferencia de
/// ProveedorNuevo/FuenteDesconocida/RubroDesconocido, una línea POA nueva no es una anomalía a
/// resaltar en rojo/amarillo, es el flujo normal cuando el municipio agrega un proyecto.
/// </summary>
public sealed record LineaPoaAnalizadaDto(
    string Hoja, int Ejercicio, bool EsNueva,
    EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos,
    string? Literal, bool FuenteDesconocida,
    decimal Presupuesto, decimal SaldoPlanilla,
    IReadOnlyList<MovimientoPoaAnalizadoDto> Movimientos);
```

- [ ] **Step 2: Escribir el fake de `ILineaPoaRepository` (sin test propio — es infraestructura de test)**

```csharp
// tests/StockApp.Application.Tests/Finanzas/Fakes/LineaPoaRepositoryFake.cs
using StockApp.Application.Interfaces;
using StockApp.Domain.Entities;

namespace StockApp.Application.Tests.Finanzas.Fakes;

/// <summary>
/// Fake in-memory de ILineaPoaRepository para AnalisisImportacionServicePoaTests (F5d Entrega 2
/// Task 1: cómputo de EsNueva). Mismo patrón que ProveedorRepositoryFake/RubroGastoRepositoryFake/
/// FuenteFinanciamientoRepositoryFake en RepositorioMaestrosFake.cs. AgregarAsync/ActualizarAsync/
/// ActualizarSinAsignacionesAsync no los ejercita ningún test de este módulo todavía — implementados
/// de forma mínima pero funcional (no NotSupportedException) para no sorprender a un test futuro.
/// </summary>
public sealed class LineaPoaRepositoryFake : ILineaPoaRepository
{
    private readonly List<LineaPoa> _lineas;
    private int _siguienteId;

    public LineaPoaRepositoryFake(IReadOnlyList<LineaPoa> lineas)
    {
        _lineas = lineas.ToList();
        _siguienteId = _lineas.Count == 0 ? 1 : _lineas.Max(l => l.Id) + 1;
    }

    public Task<LineaPoa?> ObtenerPorIdAsync(int id) =>
        Task.FromResult(_lineas.FirstOrDefault(l => l.Id == id));

    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() =>
        Task.FromResult((IReadOnlyList<LineaPoa>)_lineas.ToList());

    public Task<bool> ExisteNombreEjercicioAsync(string nombre, int ejercicio, int? excluyendoId = null) =>
        Task.FromResult(_lineas.Any(l =>
            l.Nombre == nombre && l.Ejercicio == ejercicio && (excluyendoId is null || l.Id != excluyendoId.Value)));

    public Task<int> AgregarAsync(LineaPoa linea)
    {
        linea.Id = _siguienteId++;
        _lineas.Add(linea);
        return Task.FromResult(linea.Id);
    }

    public Task ActualizarAsync(LineaPoa linea, IReadOnlyList<AsignacionPresupuestal> nuevasAsignaciones)
    {
        var indice = _lineas.FindIndex(l => l.Id == linea.Id);
        if (indice >= 0)
        {
            linea.Asignaciones = nuevasAsignaciones.ToList();
            _lineas[indice] = linea;
        }
        return Task.CompletedTask;
    }

    public Task ActualizarSinAsignacionesAsync(LineaPoa linea)
    {
        var indice = _lineas.FindIndex(l => l.Id == linea.Id);
        if (indice >= 0)
            _lineas[indice] = linea;
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Extender el helper `Crear()` del test file existente (compila roto a propósito: el constructor de `AnalisisImportacionService` todavía no acepta `ILineaPoaRepository`)**

```csharp
// tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs
// Reemplazar el método Crear() (líneas 44-62 actuales):
using StockApp.Application.Tests.Finanzas.Fakes;
// (agregar el using si no está — ya debería estar por ProveedorRepositoryFake etc.)

private static Mocks Crear(
    PlanillaPoaOds poa,
    IReadOnlyList<FuenteFinanciamiento>? fuentes = null,
    IReadOnlyList<LineaPoa>? lineasPoaExistentes = null)
{
    var parser = new PlanillaParserFake(PlanillaGastosVacia(), poa);
    var proveedoresRepo = new ProveedorRepositoryFake(new List<Proveedor>());
    var rubrosRepo = new RubroGastoRepositoryFake(new List<RubroGasto>());
    var fuentesRepo = new FuenteFinanciamientoRepositoryFake(fuentes ?? new List<FuenteFinanciamiento>());
    var lineasPoaRepo = new LineaPoaRepositoryFake(lineasPoaExistentes ?? new List<LineaPoa>());

    var session = new Mock<ICurrentSession>();
    session.Setup(s => s.RolActual).Returns(RolUsuario.Admin);

    var auth = new Mock<IAuthSvc>();

    var svc = new AnalisisImportacionService(
        parser, proveedoresRepo, rubrosRepo, fuentesRepo, lineasPoaRepo, session.Object, auth.Object);

    return new Mocks(svc);
}
```

- [ ] **Step 4: Correr los tests existentes de este archivo — deben fallar en compilación**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~AnalisisImportacionServicePoaTests"`
Expected: FAIL — error de compilación `CS7036: no argument given that corresponds to the required parameter 'lineasPoaRepo'` (o similar) en la construcción de `AnalisisImportacionService`, y `CS7036`/`CS8852` en cada `new LineaPoaAnalizadaDto(...)` sin `EsNueva`.

- [ ] **Step 5: Escribir el test nuevo específico de `EsNueva` (agregarlo al mismo archivo, junto a los demás `[Fact]`)**

```csharp
[Fact]
public async Task AnalizarAsync_HojaNoExisteEnLineasPoaDelEjercicio_EsNuevaTrue()
{
    var linea = new LineaPoaResumenOds(
        Hoja: "PROYECTO NUEVO", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
        Movimientos: new List<FilaPoaOds>());
    var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
    var m = Crear(poa,
        fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } },
        lineasPoaExistentes: new List<LineaPoa>());

    var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

    var lineaPoa = Assert.Single(resultado.LineasPoa);
    Assert.True(lineaPoa.EsNueva);
    Assert.Equal(EstadoFila.Ok, lineaPoa.Estado);
    Assert.Empty(lineaPoa.Motivos);
}

[Fact]
public async Task AnalizarAsync_HojaYaExisteEnElMismoEjercicio_EsNuevaFalse()
{
    var linea = new LineaPoaResumenOds(
        Hoja: "RAMBLA", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
        Movimientos: new List<FilaPoaOds>());
    var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
    var lineaExistente = new LineaPoa { Id = 1, Nombre = "RAMBLA", Programa = "Obras", Ejercicio = Ejercicio, Activo = true };
    var m = Crear(poa,
        fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } },
        lineasPoaExistentes: new List<LineaPoa> { lineaExistente });

    var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

    Assert.False(Assert.Single(resultado.LineasPoa).EsNueva);
}

[Fact]
public async Task AnalizarAsync_HojaExisteEnOtroEjercicio_EsNuevaTrue()
{
    // Unicidad de LineaPoa es Nombre+Ejercicio (ExisteNombreEjercicioAsync) — una hoja con el
    // mismo nombre en OTRO ejercicio no cuenta como "ya existe" para este ejercicio.
    var linea = new LineaPoaResumenOds(
        Hoja: "RAMBLA", Asignaciones: new List<AsignacionPoaOds> { new("B", 1000m, 500m) },
        Movimientos: new List<FilaPoaOds>());
    var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(500m, 0m));
    var lineaOtroEjercicio = new LineaPoa { Id = 1, Nombre = "RAMBLA", Programa = "Obras", Ejercicio = Ejercicio - 1, Activo = true };
    var m = Crear(poa,
        fuentes: new List<FuenteFinanciamiento> { new() { Id = 1, Nombre = "B", Activo = true } },
        lineasPoaExistentes: new List<LineaPoa> { lineaOtroEjercicio });

    var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

    Assert.True(Assert.Single(resultado.LineasPoa).EsNueva);
}

[Fact]
public async Task AnalizarAsync_HojaConDosAsignaciones_EsNuevaConstanteEnAmbasFilas()
{
    // Financiamiento mixto: EsNueva se computa UNA vez por Hoja (antes del for de Asignaciones,
    // igual que movimientosReconciliados) — debe ser el mismo valor en las N filas aplanadas.
    var linea = new LineaPoaResumenOds(
        Hoja: "COMPOSTERAS Y COMPACTADORAS",
        Asignaciones: new List<AsignacionPoaOds> { new("C", 1407252m, 1407252m), new("B", 92748m, 92748m) },
        Movimientos: new List<FilaPoaOds>());
    var poa = new PlanillaPoaOds(new List<LineaPoaResumenOds> { linea }, new SaldosTotalesPoaOds(92748m, 1407252m));
    var m = Crear(poa, fuentes: new List<FuenteFinanciamiento>
    {
        new() { Id = 1, Nombre = "B", Activo = true },
        new() { Id = 2, Nombre = "C", Activo = true },
    });

    var resultado = await m.Svc.AnalizarAsync(Stream.Null, Stream.Null, Ejercicio);

    Assert.Equal(2, resultado.LineasPoa.Count);
    Assert.All(resultado.LineasPoa, l => Assert.True(l.EsNueva));
}
```

- [ ] **Step 6: Correr los tests nuevos — deben fallar (implementación todavía no existe)**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~AnalisisImportacionServicePoaTests"`
Expected: FAIL en compilación (todavía, `AnalisisImportacionService` no tiene el constructor de 7 parámetros ni `EsNueva`).

- [ ] **Step 7: Implementar — inyectar `ILineaPoaRepository` y computar `EsNueva`**

```csharp
// src/StockApp.Application/Finanzas/AnalisisImportacionService.cs
// Reemplazar el bloque de campos/constructor (líneas 15-36):
using StockApp.Domain.Entities;   // agregar si falta, para LineaPoa

private readonly IPlanillaParser _parser;
private readonly IProveedorRepository _proveedores;
private readonly IRubroGastoRepository _rubros;
private readonly IFuenteFinanciamientoRepository _fuentes;
private readonly ILineaPoaRepository _lineasPoa;
private readonly ICurrentSession _session;
private readonly IAuthorizationService _auth;

public AnalisisImportacionService(
    IPlanillaParser parser,
    IProveedorRepository proveedores,
    IRubroGastoRepository rubros,
    IFuenteFinanciamientoRepository fuentes,
    ILineaPoaRepository lineasPoa,
    ICurrentSession session,
    IAuthorizationService auth)
{
    _parser = parser;
    _proveedores = proveedores;
    _rubros = rubros;
    _fuentes = fuentes;
    _lineasPoa = lineasPoa;
    _session = session;
    _auth = auth;
}
```

```csharp
// Dentro de AnalizarAsync, junto a la precarga de maestros (después de la línea de fuentesActivas,
// antes de `var gastosOds = ParsearGastosSeguro(planillaGastos);`):

// EsNueva (F5d Entrega 2 Task 1): mismo criterio de normalización que proveedores/fuentes,
// filtrado por Ejercicio porque ListarTodasAsync trae TODOS los ejercicios.
var lineasPoaExistentes = (await _lineasPoa.ListarTodasAsync())
    .Where(l => l.Ejercicio == ejercicio)
    .Select(l => Normalizar(l.Nombre))
    .ToHashSet();
```

```csharp
// Dentro del foreach (var lineaOds in poaOds.Lineas), ANTES del for de Asignaciones (antes de la
// línea 186 `for (var i = 0; i < lineaOds.Asignaciones.Count; i++)`), constante para toda la Hoja:

var esNueva = !lineasPoaExistentes.Contains(Normalizar(lineaOds.Hoja));
```

```csharp
// Dentro del for de Asignaciones, en la construcción de LineaPoaAnalizadaDto (reemplaza las
// líneas 195-200):
lineasPoa.Add(new LineaPoaAnalizadaDto(
    Hoja: lineaOds.Hoja, Ejercicio: ejercicio, EsNueva: esNueva,
    Estado: EstadoMasSevero(motivosLinea), Motivos: motivosLinea,
    Literal: literal, FuenteDesconocida: fuenteDesconocida,
    Presupuesto: asignacion.Presupuesto, SaldoPlanilla: asignacion.Saldo,
    Movimientos: i == 0 ? movimientosReconciliados : new List<MovimientoPoaAnalizadoDto>()));
```

- [ ] **Step 8: Correr TODOS los tests de este archivo — deben pasar**

Run: `dotnet test tests/StockApp.Application.Tests --filter "FullyQualifiedName~AnalisisImportacionServicePoaTests"`
Expected: PASS (8/8 — los 4 tests originales sin ripple de comportamiento + los 4 nuevos de `EsNueva`).

- [ ] **Step 9: Correr la suite completa de Application.Tests — el `EsNueva` posicional puede haber roto otros archivos que instancien `LineaPoaAnalizadaDto` o `AnalisisImportacionService`**

Run: `dotnet test tests/StockApp.Application.Tests`
Expected: PASS. Si falla por otro archivo (`AnalisisImportacionServiceGastosTests.cs`, `AnalisisImportacionServiceReconciliacionTests.cs`, `AnalisisImportacionServiceMaestrosNuevosPoaTests.cs` también usan `Crear()`/construyen el servicio con los mismos 6 argumentos antiguos), aplicar el MISMO cambio de Step 3 a esos archivos antes de continuar — no es opcional, son ripples obligatorios del cambio de firma del constructor. **`AnalisisImportacionServiceMaestrosNuevosPoaTests.cs` en particular tiene DOS call-sites** (mismo criterio que Task 7 Step 2 documenta para los "DOS call-sites" de `NuevaImportacionViewModelTests.cs`): el helper `Crear()` (~línea 79) Y una construcción INLINE dentro de un `[Fact]` (~línea 170) que NO pasa por el helper — actualizar AMBOS, no sólo el helper.

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Application/Finanzas/AnalisisImportacionDtos.cs \
        src/StockApp.Application/Finanzas/AnalisisImportacionService.cs \
        tests/StockApp.Application.Tests/Finanzas/Fakes/LineaPoaRepositoryFake.cs \
        tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServicePoaTests.cs \
        tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceGastosTests.cs \
        tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceReconciliacionTests.cs \
        tests/StockApp.Application.Tests/Finanzas/AnalisisImportacionServiceMaestrosNuevosPoaTests.cs
git commit -m "feat(finanzas): agrega el flag EsNueva a LineaPoaAnalizadaDto (F5d Entrega 2)"
```

---

### Task 2: Converter `DateOnly? ↔ DateTimeOffset?` + activar `WithDataAnnotationsValidation`

**Files:**
- Create: `src/StockApp.Presentation/Converters/DateOnlyOffsetConverter.cs`
- Test: `tests/StockApp.Presentation.Tests/Converters/DateOnlyOffsetConverterTests.cs`
- Modify: `src/StockApp.Presentation/Program.cs`

**Interfaces:**
- Produces: `DateOnlyOffsetConverter.Instance : IValueConverter` — usado por Task 7/8 en cada `CalendarDatePicker`/`DatePicker` de Fecha/Vencimiento. `Program.cs` gana `.WithDataAnnotationsValidation()` — precondición silenciosa de Tasks 3-5 (sin esto, `[Required]`/atributos custom en los VMs de fila jamás se ejecutan, pero tampoco fallan: simplemente `HasErrors` queda siempre `false`).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/Converters/DateOnlyOffsetConverterTests.cs
using System;
using System.Globalization;
using StockApp.Presentation.Converters;
using Xunit;

namespace StockApp.Presentation.Tests.Converters;

/// <summary>
/// F5d Entrega 2 Task 2: CalendarDatePicker/DatePicker de Avalonia bindean DateTimeOffset?, no
/// DateOnly? (tipo real de GastoAnalizadoDto.Fecha/FechaVencimiento) — este converter tapa esa
/// brecha. Sin componente de hora/zona en ningún lado: Convert usa 00:00 UTC-offset 0 fijo,
/// ConvertBack descarta hora/offset y se queda con la fecha calendario tal cual la eligió el
/// usuario (evita el bug clásico de "se guardó un día antes/después" por zona horaria).
/// </summary>
public class DateOnlyOffsetConverterTests
{
    private static readonly DateOnlyOffsetConverter Sut = DateOnlyOffsetConverter.Instance;

    [Fact]
    public void Convert_Null_DevuelveNull()
    {
        var resultado = Sut.Convert(null, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void Convert_DateOnly_DevuelveDateTimeOffsetConLaMismaFechaYOffsetCero()
    {
        var fecha = new DateOnly(2026, 3, 15);

        var resultado = Sut.Convert(fecha, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);

        var dto = Assert.IsType<DateTimeOffset>(resultado);
        Assert.Equal(2026, dto.Year);
        Assert.Equal(3, dto.Month);
        Assert.Equal(15, dto.Day);
        Assert.Equal(TimeSpan.Zero, dto.Offset);
    }

    [Fact]
    public void ConvertBack_Null_DevuelveNull()
    {
        var resultado = Sut.ConvertBack(null, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Null(resultado);
    }

    [Fact]
    public void ConvertBack_DateTimeOffset_DevuelveDateOnlyConLaMismaFechaCalendario()
    {
        var dto = new DateTimeOffset(2026, 3, 15, 14, 30, 0, TimeSpan.FromHours(-3));

        var resultado = Sut.ConvertBack(dto, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2026, 3, 15), resultado);
    }

    [Fact]
    public void RoundTrip_FechaFija_CierraAlConvertirYVolverAParsear()
    {
        var original = new DateOnly(2026, 12, 31);

        var offset = Sut.Convert(original, typeof(DateTimeOffset?), null, CultureInfo.InvariantCulture);
        var vuelta = Sut.ConvertBack(offset, typeof(DateOnly?), null, CultureInfo.InvariantCulture);

        Assert.Equal(original, vuelta);
    }
}
```

- [ ] **Step 2: Correr el test — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DateOnlyOffsetConverterTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'DateOnlyOffsetConverter' no se encontró`.

- [ ] **Step 3: Implementación mínima**

```csharp
// src/StockApp.Presentation/Converters/DateOnlyOffsetConverter.cs
using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace StockApp.Presentation.Converters;

/// <summary>
/// Convierte entre <c>DateOnly?</c> (tipo real de Fecha/FechaVencimiento en los DTOs de análisis
/// de Finanzas) y <c>DateTimeOffset?</c> (tipo que bindean CalendarDatePicker/DatePicker de
/// Avalonia 12 — no soportan DateOnly? nativo). Offset SIEMPRE cero: no hay componente de hora
/// en ningún lado del dominio de Finanzas, así que fijar TimeSpan.Zero evita que el offset local
/// de la máquina corra la fecha calendario un día para adelante/atrás al ida-y-vuelta.
/// </summary>
public sealed class DateOnlyOffsetConverter : IValueConverter
{
    public static readonly DateOnlyOffsetConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateOnly fecha
            ? new DateTimeOffset(fecha.Year, fecha.Month, fecha.Day, 0, 0, 0, TimeSpan.Zero)
            : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset dto
            ? DateOnly.FromDateTime(dto.DateTime)
            : null;
}
```

- [ ] **Step 4: Correr el test — debe pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~DateOnlyOffsetConverterTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Activar `WithDataAnnotationsValidation` (sin test propio — es configuración de infraestructura de UI, no hay forma de testearla sin Avalonia Headless; se verifica indirectamente en Task 3 cuando el primer VM con `[Required]` exista)**

```csharp
// src/StockApp.Presentation/Program.cs
// Reemplazar el bloque de BuildAvaloniaApp() (líneas 54-60 actuales):
public static AppBuilder BuildAvaloniaApp()
{
    IconProvider.Current.Register<MaterialDesignIconProvider>();

    return AppBuilder.Configure<App>()
        .UsePlatformDetect()
#if DEBUG
        .WithDeveloperTools()
#endif
        .WithInterFont()
        .WithDataAnnotationsValidation()
        .LogToTrace();
}
```

- [ ] **Step 6: Confirmar que el proyecto compila**

Run: `dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 7: Commit**

```bash
git add src/StockApp.Presentation/Converters/DateOnlyOffsetConverter.cs \
        tests/StockApp.Presentation.Tests/Converters/DateOnlyOffsetConverterTests.cs \
        src/StockApp.Presentation/Program.cs
git commit -m "feat(finanzas): converter DateOnly<->DateTimeOffset y activa DataAnnotations (F5d Entrega 2)"
```

---

### Task 3: `FilaGastoEditableVm` (primer `ObservableValidator` del repo)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaGastoEditableVmTests.cs`

**Interfaces:**
- Consumes: `GastoAnalizadoDto` (`StockApp.Application.Finanzas`, 17 campos posicionales — ver Task de investigación), `CondicionPago` (`StockApp.Domain.Enums`, `Contado = 0, Credito = 1`).
- Produces: `FilaGastoEditableVm : ObservableValidator` con propiedades editables TwoWay (`Proveedor`, `NumeroFactura`, `NumeroOrden`, `Detalle`, `Destino`, `Fecha: DateOnly?`, `Monto: decimal?`, `Fuente`, `CodigoRubro: int?`, `Rubro`, `Condicion: CondicionPago`, `FechaVencimiento: DateOnly?`), propiedades read-only del análisis (`HojaOrigen`, `NumeroFila`, `Estado`, `Motivos`, `ProveedorNuevo`, `FuenteDesconocida`, `RubroDesconocido`, `LineaPoaAsignada`), `bool Desbloqueada` + `DesbloquearCommand`, computadas `EsEditableProveedor`/`EsEditableFuente`/`EsEditableRubro`/`EsEditableFecha`/`EsEditableMonto`/`EsEditableDetalle`/`EsEditableDestino`/`EsEditableNumeroFactura`/`EsEditableNumeroOrden` (`bool`, `= <Campo> is null || Desbloqueada`), static factory `FilaGastoEditableVm.Desde(GastoAnalizadoDto dto)`. Usado por Task 6 (proyección), Task 7 (XAML), Task 10 (mapeo a confirmación).

**Decisión de diseño (el spec no especifica el mecanismo, se documenta acá):** `Condicion`/`FechaVencimiento` NO tienen `EsEditable*` — están SIEMPRE habilitadas (design §2: "queda como VALOR INICIAL sugerido, corregible"), a diferencia de las demás celdas que se bloquean si ya tienen valor. `Rubro` (nombre visible, editable vía combo) es distinto de `CodigoRubro` (código numérico que viaja a confirmación) — la fila expone ambos porque `RubroGasto` tiene Código+Nombre separados (a diferencia de Proveedor/Fuente, que son sólo nombre); la resolución de cuál `CodigoRubro` corresponde a un `Rubro` elegido de un combo la hace la View (Task 7) al setear ambas propiedades juntas cuando el usuario selecciona un `RubroGasto` existente.

- [ ] **Step 1: Escribir el test que falla — mapeo básico + validación de campo requerido**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaGastoEditableVmTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaGastoEditableVmTests
{
    private static GastoAnalizadoDto DtoCompleto() => new(
        HojaOrigen: "MARZO", NumeroFila: 5,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 10), Monto: 1500m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-100", NumeroOrden: "O-1",
        Detalle: "Compra de insumos", Destino: "Depósito central",
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 12, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);

    [Fact]
    public void Desde_MapeaTodosLosCamposDelDto()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.Equal("MARZO", fila.HojaOrigen);
        Assert.Equal(5, fila.NumeroFila);
        Assert.Equal(EstadoFila.Ok, fila.Estado);
        Assert.Equal(new DateOnly(2026, 3, 10), fila.Fecha);
        Assert.Equal(1500m, fila.Monto);
        Assert.Equal("ACME SA", fila.Proveedor);
        Assert.Equal("F-100", fila.NumeroFactura);
        Assert.Equal("O-1", fila.NumeroOrden);
        Assert.Equal("Compra de insumos", fila.Detalle);
        Assert.Equal("Depósito central", fila.Destino);
        Assert.Equal("Rentas Generales", fila.Fuente);
        Assert.Equal(12, fila.CodigoRubro);
        Assert.Equal("Materiales", fila.Rubro);
        Assert.False(fila.Desbloqueada);
    }

    [Fact]
    public void Desde_SinCompromisoPoa_CondicionInicialEsContadoSinVencimiento()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.Equal(CondicionPago.Contado, fila.Condicion);
        Assert.Null(fila.FechaVencimiento);
    }

    [Fact]
    public void Desde_ConCompromisoPoa_CondicionInicialEsCreditoConVencimientoIgualAFecha()
    {
        var dto = DtoCompleto() with { LineaPoaAsignada = "RAMBLA" };

        var fila = FilaGastoEditableVm.Desde(dto);

        Assert.Equal(CondicionPago.Credito, fila.Condicion);
        Assert.Equal(fila.Fecha, fila.FechaVencimiento);
    }

    [Fact]
    public void Desde_DtoCompleto_NoTieneErroresDeValidacion()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());

        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_ProveedorNulo_TieneErrorDeValidacionEnProveedor()
    {
        var dto = DtoCompleto() with { Proveedor = null };

        var fila = FilaGastoEditableVm.Desde(dto);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Proveedor)).Cast<object>());
    }

    [Fact]
    public void Proveedor_SeSeteaAVacio_GeneraErrorDeValidacionEnCaliente()
    {
        var fila = FilaGastoEditableVm.Desde(DtoCompleto());
        Assert.False(fila.HasErrors);

        fila.Proveedor = null;

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Proveedor)).Cast<object>());
    }
}
```

- [ ] **Step 2: Correr el test — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaGastoEditableVmTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'FilaGastoEditableVm' no se encontró`.

- [ ] **Step 3: Implementación mínima — propiedades editables + validación simple + `Desde`**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// VM de fila editable para la grilla de Gastos del Paso 2 (F5d Entrega 2). Reemplaza el binding
/// directo de GastoAnalizadoDto (record inmutable, Entrega 1) — necesario para two-way binding de
/// celda, validación por campo (ObservableValidator, PRIMER uso en el repo) y CancelEdit del
/// DataGrid. Condicion/FechaVencimiento parten de la heurística de Entrega 1 (compromiso POA =>
/// Crédito con vencimiento = Fecha) como valor SUGERIDO, siempre editable — a diferencia de las
/// demás celdas, que se bloquean si ya tienen valor (ver EsEditable* más abajo).
/// </summary>
public partial class FilaGastoEditableVm : ObservableValidator
{
    [ObservableProperty] private string _hojaOrigen = string.Empty;
    [ObservableProperty] private int _numeroFila;
    [ObservableProperty] private EstadoFila _estado;
    [ObservableProperty] private IReadOnlyList<MotivoEstado> _motivos = new List<MotivoEstado>();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    private DateOnly? _fecha;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El monto es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    private decimal? _monto;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El proveedor es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableProveedor))]
    private string? _proveedor;

    [ObservableProperty] private bool _proveedorNuevo;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroFactura))]
    private string? _numeroFactura;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroOrden))]
    private string? _numeroOrden;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El detalle es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableDetalle))]
    private string? _detalle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableDestino))]
    private string? _destino;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fuente de financiamiento es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private string? _fuente;

    [ObservableProperty] private bool _fuenteDesconocida;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El rubro es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableRubro))]
    private int? _codigoRubro;

    [ObservableProperty] private string? _rubro;
    [ObservableProperty] private bool _rubroDesconocido;
    [ObservableProperty] private string? _lineaPoaAsignada;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    private CondicionPago _condicion;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [VencimientoCondicional]
    private DateOnly? _fechaVencimiento;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableProveedor))]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    [NotifyPropertyChangedFor(nameof(EsEditableRubro))]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    [NotifyPropertyChangedFor(nameof(EsEditableDetalle))]
    [NotifyPropertyChangedFor(nameof(EsEditableDestino))]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroFactura))]
    [NotifyPropertyChangedFor(nameof(EsEditableNumeroOrden))]
    private bool _desbloqueada;

    public bool EsEditableProveedor => Proveedor is null || Desbloqueada;
    public bool EsEditableFuente => Fuente is null || Desbloqueada;
    public bool EsEditableRubro => CodigoRubro is null || Desbloqueada;
    public bool EsEditableFecha => Fecha is null || Desbloqueada;
    public bool EsEditableMonto => Monto is null || Desbloqueada;
    public bool EsEditableDetalle => Detalle is null || Desbloqueada;
    public bool EsEditableDestino => Destino is null || Desbloqueada;
    public bool EsEditableNumeroFactura => NumeroFactura is null || Desbloqueada;
    public bool EsEditableNumeroOrden => NumeroOrden is null || Desbloqueada;

    /// <summary>Re-valida FechaVencimiento cuando cambia Condicion (VencimientoCondicional lee
    /// Condicion desde ValidationContext.ObjectInstance, pero sólo se dispara al setear
    /// FechaVencimiento — este hook cubre el caso de cambiar Condicion primero).</summary>
    partial void OnCondicionChanged(CondicionPago value) => ValidateProperty(FechaVencimiento, nameof(FechaVencimiento));

    [RelayCommand]
    private void Desbloquear() => Desbloqueada = true;

    public static FilaGastoEditableVm Desde(GastoAnalizadoDto dto)
    {
        var esCompromisoPoa = dto.LineaPoaAsignada is not null;
        var fila = new FilaGastoEditableVm
        {
            HojaOrigen = dto.HojaOrigen,
            NumeroFila = dto.NumeroFila,
            Estado = dto.Estado,
            Motivos = dto.Motivos,
            Fecha = dto.Fecha,
            Monto = dto.Monto,
            Proveedor = dto.Proveedor,
            ProveedorNuevo = dto.ProveedorNuevo,
            NumeroFactura = dto.NumeroFactura,
            NumeroOrden = dto.NumeroOrden,
            Detalle = dto.Detalle,
            Destino = dto.Destino,
            Fuente = dto.Fuente,
            FuenteDesconocida = dto.FuenteDesconocida,
            CodigoRubro = dto.CodigoRubro,
            Rubro = dto.Rubro,
            RubroDesconocido = dto.RubroDesconocido,
            LineaPoaAsignada = dto.LineaPoaAsignada,
            Condicion = esCompromisoPoa ? CondicionPago.Credito : CondicionPago.Contado,
            FechaVencimiento = esCompromisoPoa ? dto.Fecha : null,
        };
        fila.ValidateAllProperties();
        return fila;
    }
}

/// <summary>
/// Vencimiento obligatorio si Condicion==Credito, prohibido si Condicion==Contado — espejo
/// exacto de la regla del backend (GastoConfirmarDto.FechaVencimiento condicional, ver
/// ConfirmacionImportacionService). Cruza contra Condicion vía ValidationContext.ObjectInstance
/// porque DataAnnotations no tiene un atributo condicional de dos campos nativo.
/// </summary>
public sealed class VencimientoCondicionalAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var fila = (FilaGastoEditableVm)validationContext.ObjectInstance;
        if (fila.Condicion == CondicionPago.Credito && value is null)
            return new ValidationResult("El vencimiento es obligatorio para un gasto a crédito.");
        if (fila.Condicion == CondicionPago.Contado && value is not null)
            return new ValidationResult("Un gasto contado no debe tener fecha de vencimiento.");
        return ValidationResult.Success;
    }
}
```

- [ ] **Step 4: Correr el test — debe pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaGastoEditableVmTests"`
Expected: PASS (6/6).

- [ ] **Step 5: Escribir el test que falla — celdas bloqueadas/desbloqueadas y vencimiento condicional**

```csharp
// Agregar a FilaGastoEditableVmTests.cs:

[Fact]
public void EsEditableProveedor_ProveedorNoNuloYFilaNoDesbloqueada_EsFalse()
{
    var fila = FilaGastoEditableVm.Desde(DtoCompleto());

    Assert.False(fila.EsEditableProveedor);
}

[Fact]
public void EsEditableProveedor_ProveedorNulo_EsTrueAunqueNoEsteDesbloqueada()
{
    var dto = DtoCompleto() with { Proveedor = null };

    var fila = FilaGastoEditableVm.Desde(dto);

    Assert.True(fila.EsEditableProveedor);
}

[Fact]
public void Desbloquear_HabilitaLaEdicionDeTodasLasCeldasCompletas()
{
    var fila = FilaGastoEditableVm.Desde(DtoCompleto());
    Assert.False(fila.EsEditableProveedor);

    fila.DesbloquearCommand.Execute(null);

    Assert.True(fila.Desbloqueada);
    Assert.True(fila.EsEditableProveedor);
    Assert.True(fila.EsEditableFuente);
    Assert.True(fila.EsEditableRubro);
}

[Fact]
public void FechaVencimiento_CreditoSinVencimiento_GeneraErrorDeValidacion()
{
    var fila = FilaGastoEditableVm.Desde(DtoCompleto());

    fila.Condicion = CondicionPago.Credito;
    fila.FechaVencimiento = null;

    Assert.NotEmpty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
}

[Fact]
public void FechaVencimiento_ContadoConVencimiento_GeneraErrorDeValidacion()
{
    var fila = FilaGastoEditableVm.Desde(DtoCompleto());

    fila.Condicion = CondicionPago.Contado;
    fila.FechaVencimiento = new DateOnly(2026, 4, 1);

    Assert.NotEmpty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
}

[Fact]
public void FechaVencimiento_CreditoConVencimiento_NoGeneraError()
{
    var fila = FilaGastoEditableVm.Desde(DtoCompleto());

    fila.FechaVencimiento = new DateOnly(2026, 4, 1);
    fila.Condicion = CondicionPago.Credito;

    Assert.Empty(fila.GetErrors(nameof(fila.FechaVencimiento)).Cast<object>());
}
```

- [ ] **Step 6: Correr los tests nuevos — deben fallar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaGastoEditableVmTests"`
Expected: FAIL — `Assert.NotEmpty` recibe una colección vacía en los tests de `FechaVencimiento` (la implementación de Step 3 ya incluye `VencimientoCondicionalAttribute`, así que el fallo esperado acá es SOLO si algún detalle de wiring quedó mal; si Step 3 se copió tal cual, este step puede pasar directo — correrlo igual para confirmarlo explícitamente antes de continuar).

- [ ] **Step 7: Ajustar si hace falta y confirmar que TODO pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaGastoEditableVmTests"`
Expected: PASS (12/12).

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaGastoEditableVmTests.cs
git commit -m "feat(finanzas): FilaGastoEditableVm con validacion por campo (F5d Entrega 2)"
```

---

### Task 4: `FilaIngresoEditableVm`

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaIngresoEditableVmTests.cs`

**Interfaces:**
- Consumes: `IngresoAnalizadoDto(string HojaOrigen, int NumeroFila, EstadoFila Estado, IReadOnlyList<MotivoEstado> Motivos, DateOnly? Fecha, decimal? Monto, string? Concepto, string? Fuente, bool FuenteDesconocida)`.
- Produces: `FilaIngresoEditableVm : ObservableValidator` con `Fecha`/`Monto`/`Concepto`/`Fuente` editables (`[Required]`), `EsEditableFecha`/`EsEditableMonto`/`EsEditableConcepto`/`EsEditableFuente`, `Desbloqueada`/`DesbloquearCommand`, static factory `Desde(IngresoAnalizadoDto)`. Usado por Task 6/8/10.

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaIngresoEditableVmTests.cs
using System;
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaIngresoEditableVmTests
{
    private static IngresoAnalizadoDto DtoCompleto() => new(
        HojaOrigen: "ENERO", NumeroFila: 3,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 1, 15), Monto: 5000m,
        Concepto: "Venta de entradas",
        Fuente: "Rentas Generales", FuenteDesconocida: false);

    [Fact]
    public void Desde_MapeaTodosLosCamposDelDto()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        Assert.Equal("ENERO", fila.HojaOrigen);
        Assert.Equal(3, fila.NumeroFila);
        Assert.Equal(new DateOnly(2026, 1, 15), fila.Fecha);
        Assert.Equal(5000m, fila.Monto);
        Assert.Equal("Venta de entradas", fila.Concepto);
        Assert.Equal("Rentas Generales", fila.Fuente);
        Assert.False(fila.Desbloqueada);
        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_ConceptoNulo_TieneErrorDeValidacion()
    {
        var dto = DtoCompleto() with { Concepto = null };

        var fila = FilaIngresoEditableVm.Desde(dto);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Concepto)).Cast<object>());
    }

    [Fact]
    public void EsEditableConcepto_ConceptoCompletoYNoDesbloqueada_EsFalse()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        Assert.False(fila.EsEditableConcepto);
    }

    [Fact]
    public void Desbloquear_HabilitaLaEdicionDeTodasLasCeldasCompletas()
    {
        var fila = FilaIngresoEditableVm.Desde(DtoCompleto());

        fila.DesbloquearCommand.Execute(null);

        Assert.True(fila.Desbloqueada);
        Assert.True(fila.EsEditableConcepto);
        Assert.True(fila.EsEditableFuente);
        Assert.True(fila.EsEditableFecha);
        Assert.True(fila.EsEditableMonto);
    }
}
```

- [ ] **Step 2: Correr el test — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaIngresoEditableVmTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'FilaIngresoEditableVm' no se encontró`.

- [ ] **Step 3: Implementación**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>VM de fila editable para la grilla de Ingresos del Paso 2 (F5d Entrega 2). Mismo
/// patrón que FilaGastoEditableVm, con menos campos (IngresoAnalizadoDto es más chico que
/// GastoAnalizadoDto — sin condición de pago, sin rubro, sin reconciliación POA).</summary>
public partial class FilaIngresoEditableVm : ObservableValidator
{
    [ObservableProperty] private string _hojaOrigen = string.Empty;
    [ObservableProperty] private int _numeroFila;
    [ObservableProperty] private EstadoFila _estado;
    [ObservableProperty] private IReadOnlyList<MotivoEstado> _motivos = new List<MotivoEstado>();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fecha es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    private DateOnly? _fecha;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El monto es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    private decimal? _monto;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El concepto es obligatorio.")]
    [NotifyPropertyChangedFor(nameof(EsEditableConcepto))]
    private string? _concepto;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "La fuente de financiamiento es obligatoria.")]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private string? _fuente;

    [ObservableProperty] private bool _fuenteDesconocida;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EsEditableFecha))]
    [NotifyPropertyChangedFor(nameof(EsEditableMonto))]
    [NotifyPropertyChangedFor(nameof(EsEditableConcepto))]
    [NotifyPropertyChangedFor(nameof(EsEditableFuente))]
    private bool _desbloqueada;

    public bool EsEditableFecha => Fecha is null || Desbloqueada;
    public bool EsEditableMonto => Monto is null || Desbloqueada;
    public bool EsEditableConcepto => Concepto is null || Desbloqueada;
    public bool EsEditableFuente => Fuente is null || Desbloqueada;

    [RelayCommand]
    private void Desbloquear() => Desbloqueada = true;

    public static FilaIngresoEditableVm Desde(IngresoAnalizadoDto dto)
    {
        var fila = new FilaIngresoEditableVm
        {
            HojaOrigen = dto.HojaOrigen,
            NumeroFila = dto.NumeroFila,
            Estado = dto.Estado,
            Motivos = dto.Motivos,
            Fecha = dto.Fecha,
            Monto = dto.Monto,
            Concepto = dto.Concepto,
            Fuente = dto.Fuente,
            FuenteDesconocida = dto.FuenteDesconocida,
        };
        fila.ValidateAllProperties();
        return fila;
    }
}
```

- [ ] **Step 4: Correr el test — debe pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaIngresoEditableVmTests"`
Expected: PASS (4/4).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaIngresoEditableVmTests.cs
git commit -m "feat(finanzas): FilaIngresoEditableVm con validacion por campo (F5d Entrega 2)"
```

---

### Task 5: `FilaLineaPoaEditableVm` (agrupada por Hoja)

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaLineaPoaEditableVmTests.cs`

**Interfaces:**
- Consumes: `LineaPoaAnalizadaDto` (con `EsNueva`, Task 1) — bajo financiamiento mixto llegan VARIAS filas con el mismo `Hoja` (una por asignación), hay que agruparlas.
- Produces: `AsignacionLineaPoaVm(string? Fuente, bool FuenteDesconocida, decimal Presupuesto, decimal SaldoPlanilla)` (record, read-only — Entrega 2 NO edita asignaciones individuales de la línea POA, sólo declara la línea nueva con su Programa; fuera de alcance según diseño §11). `FilaLineaPoaEditableVm : ObservableValidator` con `Hoja`/`Ejercicio`/`EsNueva`/`Estado`/`Motivos` read-only, `Asignaciones: IReadOnlyList<AsignacionLineaPoaVm>` read-only, `Programa: string?` editable (`[Required]` SÓLO si `EsNueva`), static factory `FilaLineaPoaEditableVm.DesdeGrupo(IGrouping<string, LineaPoaAnalizadaDto> grupo)`. Usado por Task 6 (proyección con `GroupBy(l => l.Hoja)`), Task 8 (XAML), Task 10 (mapeo).

**Decisión de diseño:** a diferencia de `FilaGastoEditableVm`/`FilaIngresoEditableVm`, esta fila NO tiene `Desbloqueada`/candado — `Programa` nunca viene pre-cargado desde el análisis (no existe en la planilla, diseño §6), así que no hay un estado "ya completo, bloqueado" que desbloquear: sólo hay "aplica" (`EsNueva == true`, editable y obligatorio) o "no aplica" (`EsNueva == false`, el campo ni se muestra en el XAML de Task 8).

- [ ] **Step 1: Escribir el test que falla**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaLineaPoaEditableVmTests.cs
using System.Collections.Generic;
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaLineaPoaEditableVmTests
{
    private static LineaPoaAnalizadaDto Dto(string fuente, decimal presupuesto, decimal saldo, bool esNueva = true) =>
        new(Hoja: "RAMBLA", Ejercicio: 2026, EsNueva: esNueva,
            Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
            Literal: fuente, FuenteDesconocida: false,
            Presupuesto: presupuesto, SaldoPlanilla: saldo,
            Movimientos: new List<MovimientoPoaAnalizadoDto>());

    [Fact]
    public void DesdeGrupo_UnaSolaAsignacion_MapeaHojaYAsignacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("Rentas Generales", 100000m, 50000m) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.Equal("RAMBLA", fila.Hoja);
        Assert.Equal(2026, fila.Ejercicio);
        Assert.True(fila.EsNueva);
        var asignacion = Assert.Single(fila.Asignaciones);
        Assert.Equal("Rentas Generales", asignacion.Fuente);
        Assert.Equal(100000m, asignacion.Presupuesto);
        Assert.Equal(50000m, asignacion.SaldoPlanilla);
    }

    [Fact]
    public void DesdeGrupo_FinanciamientoMixto_AgrupaLasDosAsignacionesEnUnaSolaFila()
    {
        var lineas = new List<LineaPoaAnalizadaDto>
        {
            Dto("C", 1407252m, 1407252m),
            Dto("B", 92748m, 92748m),
        };
        var grupo = lineas.GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.Equal(2, fila.Asignaciones.Count);
        Assert.Equal(1407252m, fila.Asignaciones[0].Presupuesto);
        Assert.Equal(92748m, fila.Asignaciones[1].Presupuesto);
    }

    [Fact]
    public void DesdeGrupo_EsNueva_ProgramaVacio_TieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: true) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Programa)).Cast<object>());
    }

    [Fact]
    public void DesdeGrupo_NoEsNueva_ProgramaVacio_NoTieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: false) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);

        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void DesdeGrupo_EsNueva_ProgramaCompleto_NoTieneErrorDeValidacion()
    {
        var grupo = new List<LineaPoaAnalizadaDto> { Dto("B", 1000m, 500m, esNueva: true) }
            .GroupBy(l => l.Hoja).Single();

        var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);
        fila.Programa = "Obras públicas";

        Assert.False(fila.HasErrors);
    }
}
```

- [ ] **Step 2: Correr el test — debe fallar en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaLineaPoaEditableVmTests"`
Expected: FAIL — `CS0246: El tipo o el nombre del espacio de nombres 'FilaLineaPoaEditableVm' no se encontró`.

- [ ] **Step 3: Implementación**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Finanzas;
using StockApp.Domain.Enums;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>Una asignación (Fuente + Presupuesto/Saldo) dentro de una línea POA. Read-only en
/// Entrega 2 — editar asignaciones individuales está fuera de alcance (diseño F5d Entrega 2
/// §11).</summary>
public sealed record AsignacionLineaPoaVm(string? Fuente, bool FuenteDesconocida, decimal Presupuesto, decimal SaldoPlanilla);

/// <summary>
/// VM de fila editable para la grilla de Líneas POA del Paso 2 (F5d Entrega 2). A diferencia de
/// LineaPoaAnalizadaDto (una fila POR ASIGNACIÓN bajo financiamiento mixto), esta fila representa
/// UNA HOJA completa — DesdeGrupo agrupa las N LineaPoaAnalizadaDto que comparten Hoja (mismo
/// criterio de aplanado/agrupado que el diseño §6 pide para el mapeo a confirmación en Task 10).
/// Nombre de la línea = Hoja (siempre read-only, no se edita: viene de la planilla). Programa es
/// el ÚNICO campo editable, y sólo aplica/es obligatorio si EsNueva (si la línea ya existe en la
/// base, Programa ni se manda a confirmar — ver Task 10).
/// </summary>
public partial class FilaLineaPoaEditableVm : ObservableValidator
{
    [ObservableProperty] private string _hoja = string.Empty;
    [ObservableProperty] private int _ejercicio;
    [ObservableProperty] private bool _esNueva;
    [ObservableProperty] private EstadoFila _estado;
    [ObservableProperty] private IReadOnlyList<MotivoEstado> _motivos = new List<MotivoEstado>();
    [ObservableProperty] private IReadOnlyList<AsignacionLineaPoaVm> _asignaciones = new List<AsignacionLineaPoaVm>();

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [ProgramaObligatorioSiNueva]
    private string? _programa;

    public static FilaLineaPoaEditableVm DesdeGrupo(IGrouping<string, LineaPoaAnalizadaDto> grupo)
    {
        var lista = grupo.ToList();
        var primera = lista[0];
        var fila = new FilaLineaPoaEditableVm
        {
            Hoja = grupo.Key,
            Ejercicio = primera.Ejercicio,
            EsNueva = primera.EsNueva,
            Estado = lista.Max(l => l.Estado),
            Motivos = lista.SelectMany(l => l.Motivos).ToList(),
            Asignaciones = lista
                .Select(l => new AsignacionLineaPoaVm(l.Literal, l.FuenteDesconocida, l.Presupuesto, l.SaldoPlanilla))
                .ToList(),
        };
        fila.ValidateAllProperties();
        return fila;
    }
}

/// <summary>Programa es obligatorio SÓLO si la línea es nueva (EsNueva) — una línea existente no
/// manda Programa a confirmar (Task 10), así que no tiene sentido exigirlo acá.</summary>
public sealed class ProgramaObligatorioSiNuevaAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var fila = (FilaLineaPoaEditableVm)validationContext.ObjectInstance;
        if (fila.EsNueva && string.IsNullOrWhiteSpace(value as string))
            return new ValidationResult("El programa es obligatorio para una línea POA nueva.");
        return ValidationResult.Success;
    }
}
```

- [ ] **Step 4: Correr el test — debe pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaLineaPoaEditableVmTests"`
Expected: PASS (5/5).

- [ ] **Step 5: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaLineaPoaEditableVmTests.cs
git commit -m "feat(finanzas): FilaLineaPoaEditableVm agrupada por Hoja (F5d Entrega 2)"
```

---

### Task 6: Proyección en `NuevaImportacionViewModel` — colecciones de filas VM + gating relajado

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`

**Interfaces:**
- Consumes: `FilaGastoEditableVm.Desde`, `FilaIngresoEditableVm.Desde`, `FilaLineaPoaEditableVm.DesdeGrupo` (Tasks 3-5); `ObservableValidator.ErrorsChanged` (evento heredado, `EventHandler<DataErrorsChangedEventArgs>`).
- Produces: `NuevaImportacionViewModel.FilasGasto/FilasIngreso/FilasLineaPoa: ObservableCollection<Fila*EditableVm>` + sus `*View: DataGridCollectionView` (reemplazan `GastosAnalizados`/`IngresosAnalizados`/`LineasPoaAnalizadas` y sus Views de Entrega 1). `PuedeConfirmar` reescrito. Usado por Task 7/8 (XAML), Task 9 (maestros nuevos), Task 10 (mapeo).

**Nota importante:** este Task deja `MapearAConfirmacion`/`ConfirmarAsync` SIN TOCAR salvo por lo estrictamente necesario para compilar (el mapeo real fila→DTO es Task 10, a propósito, para no mezclar dos responsabilidades en un mismo Task). Como `MapearAConfirmacion` lee `_analisis` (el `ResultadoAnalisisDto` crudo, que se sigue guardando en `AnalizarAsync`), sigue compilando sin cambios en este Task.

- [ ] **Step 1: Escribir el test que falla — las nuevas colecciones existen y se populan con filas VM, no DTOs**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
// Agregar estos tests al archivo existente (usa el mismo helper Crear()/mocks que ya tiene el
// archivo — ImportacionApiClient mockeado vía IImportacionService, ver tests existentes de
// AnalizarAsync en este mismo archivo para el patrón exacto de mock de _service.AnalizarAsync).

[Fact]
public async Task AnalizarAsync_PopulaFilasGastoComoVmEditables()
{
    var (vm, service, _, _) = Crear();
    var gastoDto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Advertencia, Motivos: new List<MotivoEstado>(),
        Fecha: null, Monto: 1000m,
        Proveedor: null, ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";

    await vm.AnalizarCommand.ExecuteAsync(null);

    var fila = Assert.Single(vm.FilasGasto);
    Assert.IsType<FilaGastoEditableVm>(fila);
    Assert.Equal("MARZO", fila.HojaOrigen);
    Assert.True(fila.HasErrors); // Proveedor null => [Required] falla
}

[Fact]
public async Task AnalizarAsync_PopulaFilasLineaPoaAgrupadasPorHoja()
{
    var (vm, service, _, _) = Crear();
    var lineaC = new LineaPoaAnalizadaDto(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 100m, SaldoPlanilla: 100m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());
    var lineaB = lineaC with { Literal = "B", Presupuesto = 50m, SaldoPlanilla = 50m };
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto> { lineaC, lineaB },
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";

    await vm.AnalizarCommand.ExecuteAsync(null);

    var fila = Assert.Single(vm.FilasLineaPoa);
    Assert.Equal("COMPOSTERAS", fila.Hoja);
    Assert.Equal(2, fila.Asignaciones.Count);
}

[Fact]
public async Task PuedeConfirmar_FilaConErrorDeValidacion_EsFalse()
{
    var (vm, service, _, _) = Crear();
    var gastoIncompleto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: null, ProveedorNuevo: false,
        NumeroFactura: null, NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoIncompleto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";

    await vm.AnalizarCommand.ExecuteAsync(null);

    Assert.False(vm.PuedeConfirmar);
    Assert.NotNull(vm.MensajeConfirmarBloqueado);
    Assert.Contains("1", vm.MensajeConfirmarBloqueado);
}

[Fact]
public async Task PuedeConfirmar_TodasLasFilasCompletas_EsTrue()
{
    var (vm, service, _, _) = Crear();
    var gastoCompleto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoCompleto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";

    await vm.AnalizarCommand.ExecuteAsync(null);

    Assert.True(vm.PuedeConfirmar);
    Assert.Null(vm.MensajeConfirmarBloqueado);
}
```

- [ ] **Step 2: Correr los tests — deben fallar en compilación** (`FilasGasto`/`FilasLineaPoa` no existen todavía)

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `CS1061: 'NuevaImportacionViewModel' no contiene una definición para 'FilasGasto'` (y similares).

- [ ] **Step 3: Reemplazar las colecciones de DTOs por colecciones de filas VM**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// Reemplazar el bloque de líneas 61-68 (colecciones GastosAnalizados/IngresosAnalizados/LineasPoaAnalizadas):

public ObservableCollection<FilaGastoEditableVm> FilasGasto { get; } = new();
public DataGridCollectionView FilasGastoView { get; }

public ObservableCollection<FilaIngresoEditableVm> FilasIngreso { get; } = new();
public DataGridCollectionView FilasIngresoView { get; }

public ObservableCollection<FilaLineaPoaEditableVm> FilasLineaPoa { get; } = new();
public DataGridCollectionView FilasLineaPoaView { get; }

public ObservableCollection<string> ProveedoresNuevos { get; } = new();
public ObservableCollection<string> FuentesNuevas { get; } = new();
public ObservableCollection<CodigoRubroNuevoDto> RubrosNuevos { get; } = new();
```

```csharp
// Constructor (reemplaza líneas 125-127, la construcción de las DataGridCollectionView):
FilasGastoView = new DataGridCollectionView(FilasGasto);
FilasIngresoView = new DataGridCollectionView(FilasIngreso);
FilasLineaPoaView = new DataGridCollectionView(FilasLineaPoa);
```

- [ ] **Step 4: Reescribir `AnalizarAsync` para proyectar a filas VM y suscribir `ErrorsChanged`**

```csharp
// Reemplaza el bloque de población dentro de AnalizarAsync (líneas 158-169 actuales):
FilasGasto.Clear();
foreach (var g in _analisis.Gastos)
{
    var fila = FilaGastoEditableVm.Desde(g);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    FilasGasto.Add(fila);
}

FilasIngreso.Clear();
foreach (var i in _analisis.Ingresos)
{
    var fila = FilaIngresoEditableVm.Desde(i);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    FilasIngreso.Add(fila);
}

FilasLineaPoa.Clear();
foreach (var grupo in _analisis.LineasPoa.GroupBy(l => l.Hoja))
{
    var fila = FilaLineaPoaEditableVm.DesdeGrupo(grupo);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    FilasLineaPoa.Add(fila);
}

ProveedoresNuevos.Clear();
foreach (var p in _analisis.MaestrosNuevos.Proveedores) ProveedoresNuevos.Add(p);
FuentesNuevas.Clear();
foreach (var f in _analisis.MaestrosNuevos.Fuentes) FuentesNuevas.Add(f);
RubrosNuevos.Clear();
foreach (var r in _analisis.MaestrosNuevos.Rubros) RubrosNuevos.Add(r);
```

```csharp
// Nuevo método privado, junto a PuedeConfirmar:

/// <summary>Se dispara cuando cualquier fila (Gasto/Ingreso/LineaPoa) cambia su estado de
/// validación — el gating de Confirmar depende de HasErrors de TODAS las filas, no sólo de la
/// que cambió, así que se recalculan las dos propiedades computadas completas.</summary>
private void NotificarGatingCambio()
{
    OnPropertyChanged(nameof(PuedeConfirmar));
    OnPropertyChanged(nameof(MensajeConfirmarBloqueado));
    ConfirmarCommand.NotifyCanExecuteChanged();
}
```

- [ ] **Step 5: Reescribir el gating — `PuedeConfirmar`/`MensajeConfirmarBloqueado`, eliminar `ContarFilasIncompletas`**

```csharp
// Reemplaza PuedeConfirmar (líneas 80-86) — el viejo gate Resumen.Errores==0 queda REDUNDANTE:
// FechaIlegible/MontoIlegible (las únicas causas de EstadoFila.Error) dejan la Fecha/Monto del
// DTO en null, y el [Required] de FilaGastoEditableVm.Fecha/Monto captura EXACTAMENTE ese caso
// vía HasErrors — ahora que la celda es editable, el usuario lo corrige ahí, no hace falta un
// gate aparte para "errores" vs "incompletas".
/// <summary>Confirmar sólo puede ejecutarse si NINGUNA fila (Gasto/Ingreso/LineaPoa) tiene
/// errores de validación pendientes (F5d Entrega 2 §7) — reemplaza el gate de Entrega 1
/// (Resumen.Errores==0 && ContarFilasIncompletas()==0), ahora redundante: los campos que antes
/// dejaban a una fila "incompleta" o en EstadoFila.Error son exactamente los que [Required]
/// valida en las filas VM.</summary>
public bool PuedeConfirmar => !HayFilasConErrores();

private bool HayFilasConErrores() =>
    FilasGasto.Any(f => f.HasErrors) || FilasIngreso.Any(f => f.HasErrors) || FilasLineaPoa.Any(f => f.HasErrors);
```

```csharp
// Reemplaza MensajeConfirmarBloqueado (líneas 88-105):
/// <summary>Cuenta de filas con errores de validación pendientes — null/vacío si Confirmar está
/// habilitado.</summary>
public string? MensajeConfirmarBloqueado
{
    get
    {
        var conErrores = FilasGasto.Count(f => f.HasErrors)
            + FilasIngreso.Count(f => f.HasErrors)
            + FilasLineaPoa.Count(f => f.HasErrors);
        return conErrores == 0
            ? null
            : $"Hay {conErrores} fila(s) con errores de validación pendientes.";
    }
}
```

```csharp
// Eliminar por completo el método ContarFilasIncompletas() (líneas 107-109) — ya no se usa.
```

- [ ] **Step 6: Actualizar `ReiniciarWizard` para limpiar las colecciones nuevas**

```csharp
// Reemplaza, dentro de ReiniciarWizard() (líneas 330-335 actuales):
FilasGasto.Clear();
FilasIngreso.Clear();
FilasLineaPoa.Clear();
ProveedoresNuevos.Clear();
FuentesNuevas.Clear();
RubrosNuevos.Clear();
```

- [ ] **Step 7: Agregar el `using` que falta**

```csharp
// Al tope del archivo, junto a los using existentes (línea 1-13):
using System.Linq; // ya debería estar (se usa en ConflictoGastoFila.Desde) — confirmar antes de duplicar
```

- [ ] **Step 8: Correr los tests nuevos y los existentes del archivo — el archivo entero debe compilar y pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL todavía en esta corrida SI algún test existente de Entrega 1 referencia `GastosAnalizados`/`ContarFilasIncompletas` directamente — si es así, actualizar esos tests puntuales para usar `FilasGasto`/el nuevo gating (son ripples esperados de este Task, no un bug). Después de ajustar: PASS completo.

- [ ] **Step 9: Confirmar que el resto del archivo (XAML todavía sin tocar, Task 7/8) sigue compilando** — `NuevaImportacionView.axaml` referencia `GastosAnalizadosView`/`IngresosAnalizadosView`/`LineasPoaAnalizadasView`, que ya no existen. Esto es un ROTO INTENCIONAL de este Task — se resuelve recién en Task 7/8. Documentarlo y seguir: no revertir.

Run: `dotnet build src/StockApp.Presentation`
Expected: FAIL — errores de binding en tiempo de compilación de XAML NO ocurren en Avalonia (los bindings son runtime, no compile-time salvo con `x:DataType` y CompiledBindings activos). Si el proyecto compila igual (los bindings rotos sólo fallan en runtime), está bien: seguir a Task 7 para dejarlo consistente antes de cualquier verificación manual.

- [ ] **Step 10: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): proyecta el analisis a filas VM editables y relaja el gating de Confirmar (F5d Entrega 2)"
```

---

### Task 7: Grilla de Gastos editable (XAML) + carga de maestros existentes

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`
- Create: `tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs` — fakes hechos a mano (sin Moq) de las dependencias de `NuevaImportacionViewModel`, mismo criterio que `MovimientoRegistroFakes.cs`.
- Create: `tests/StockApp.Presentation.UiTests/NuevaImportacionGastosGridTests.cs` — tests headless de la grilla de Gastos (candado por celda, `ComboBox IsEditable`, regresión del bug #232).

**Interfaces:**
- Consumes: `IFuenteFinanciamientoService.ListarActivasAsync()`, `IRubroGastoService.ListarActivosAsync()`, `IProveedorService.ListarTodosAsync()` (filtrado `.Where(p => p.Activo)` — `IProveedorService` NO tiene `ListarActivosAsync`, mismo patrón que `GastoFormViewModel.InicializarAsync`, `src/StockApp.Presentation/ViewModels/Finanzas/GastoFormViewModel.cs:163-176`). `DateOnlyOffsetConverter.Instance` (Task 2), `DecimalOpcionalConverter.Instance` (ya existe).
- Produces: `NuevaImportacionViewModel.FuentesDisponibles/RubrosDisponibles/ProveedoresDisponibles: ObservableCollection<T>` + `InicializarMaestrosAsync(): Task` (la dispara la View vía `DataContextChanged`, mismo contrato que `GastoFormViewModel.InicializarAsync`). Consumido también por Task 8 (Ingresos reusa `FuentesDisponibles`) y Task 9 (declaración automática de maestros nuevos).

**Decisión de diseño — binding cross-DataContext dentro de `DataGridTemplateColumn`:** los `ComboBox` de Proveedor/Fuente/Rubro viven dentro de `CellEditingTemplate`, cuyo `DataContext` es la FILA (`FilaGastoEditableVm`), no el VM contenedor — pero `ProveedoresDisponibles`/`FuentesDisponibles`/`RubrosDisponibles` viven en el VM contenedor. Mismo problema exacto que ya resolvió Entrega 1 para el `Style` de color de fila (`NuevaImportacionView.axaml:16`, comentario: "esta fila es compartida por 3 DataGrids con item type distinto... no hay un x:DataType único que tipe-chequee"): se usa `x:CompileBindings="False"` en esos `DataTemplate` puntuales + `{Binding #Root.DataContext.ProveedoresDisponibles}` (binding por `ElementName`, clásico no compilado) en vez de intentar un cast compilado `$parent[UserControl].((vm:...)DataContext)`. El `UserControl` raíz gana `x:Name="Root"` en este Task.

- [ ] **Step 1: Inyectar los 3 servicios de maestros y agregar `InicializarMaestrosAsync`**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// using nuevos (junto a los existentes, líneas 1-13):
using System.Collections.ObjectModel;
using StockApp.Application.Catalogo;
using StockApp.Domain.Entities;
```

```csharp
// Campos + constructor (reemplaza líneas 34-36 y 118-128):
private readonly IImportacionService _service;
private readonly IServicioSeleccionArchivo _seleccion;
private readonly IConfirmacionService _confirmacion;
private readonly IFuenteFinanciamientoService _fuentesService;
private readonly IRubroGastoService _rubrosService;
private readonly IProveedorService _proveedoresService;

public ObservableCollection<FuenteFinanciamiento> FuentesDisponibles { get; } = new();
public ObservableCollection<RubroGasto> RubrosDisponibles { get; } = new();
public ObservableCollection<Proveedor> ProveedoresDisponibles { get; } = new();

public NuevaImportacionViewModel(
    IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion,
    IFuenteFinanciamientoService fuentesService, IRubroGastoService rubrosService, IProveedorService proveedoresService)
{
    _service = service;
    _seleccion = seleccion;
    _confirmacion = confirmacion;
    _fuentesService = fuentesService;
    _rubrosService = rubrosService;
    _proveedoresService = proveedoresService;

    FilasGastoView = new DataGridCollectionView(FilasGasto);
    FilasIngresoView = new DataGridCollectionView(FilasIngreso);
    FilasLineaPoaView = new DataGridCollectionView(FilasLineaPoa);
}

/// <summary>Carga los combos de maestros existentes. La dispara la View (DataContextChanged),
/// mismo contrato que GastoFormViewModel.InicializarAsync.</summary>
public async Task InicializarMaestrosAsync()
{
    var fuentes = await _fuentesService.ListarActivasAsync();
    FuentesDisponibles.Clear();
    foreach (var f in fuentes) FuentesDisponibles.Add(f);

    var rubros = await _rubrosService.ListarActivosAsync();
    RubrosDisponibles.Clear();
    foreach (var r in rubros) RubrosDisponibles.Add(r);

    var proveedores = await _proveedoresService.ListarTodosAsync();
    ProveedoresDisponibles.Clear();
    foreach (var p in proveedores.Where(p => p.Activo)) ProveedoresDisponibles.Add(p);
}
```

**Nota de DI:** `App.axaml.cs:281` registra `services.AddTransient<NuevaImportacionViewModel>()` sin factory lambda — la resolución de constructor es automática por el contenedor DI, así que agregar 3 parámetros NO requiere tocar el registro (`IFuenteFinanciamientoService`/`IRubroGastoService`/`IProveedorService` ya están registrados, los usa `GastoFormViewModel`).

- [ ] **Step 2: Actualizar el helper de tests `Crear()` — ripple OBLIGATORIO, sin esto TODO el archivo de tests deja de compilar** (`NuevaImportacionViewModelTests.cs` ya existe desde Entrega 1 con `Crear()` construyendo `new NuevaImportacionViewModel(svc.Object, seleccion.Object, confirm.Object)` en DOS lugares: el helper `Crear()` en sí — líneas 22-33 — y el helper `CrearEnPasoRevisarAsync` — líneas 104-120. El constructor de Step 1 pasa de 3 a 6 parámetros: los tests existentes de Tasks 6 y anteriores de este mismo archivo NO compilan hasta este Step.)

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
// Reemplaza Crear() (líneas 22-33 actuales):
private static (NuevaImportacionViewModel vm, Mock<IImportacionService> svc,
                Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
                Mock<IFuenteFinanciamientoService> fuentes, Mock<IRubroGastoService> rubros,
                Mock<IProveedorService> proveedores)
    Crear()
{
    var svc = new Mock<IImportacionService>();
    var seleccion = new Mock<IServicioSeleccionArchivo>();
    var confirm = new Mock<IConfirmacionService>();
    confirm.Setup(c => c.InformarAsync(It.IsAny<string>())).Returns(Task.CompletedTask);
    var fuentes = new Mock<IFuenteFinanciamientoService>();
    fuentes.Setup(f => f.ListarActivasAsync()).ReturnsAsync(new List<FuenteFinanciamiento>());
    var rubros = new Mock<IRubroGastoService>();
    rubros.Setup(r => r.ListarActivosAsync()).ReturnsAsync(new List<RubroGasto>());
    var proveedores = new Mock<IProveedorService>();
    proveedores.Setup(p => p.ListarTodosAsync()).ReturnsAsync(new List<Proveedor>());

    var vm = new NuevaImportacionViewModel(
        svc.Object, seleccion.Object, confirm.Object, fuentes.Object, rubros.Object, proveedores.Object);
    return (vm, svc, seleccion, confirm, fuentes, rubros, proveedores);
}
```

```csharp
// Reemplaza CrearEnPasoRevisarAsync (líneas 104-120 actuales) — agrega los 3 mocks nuevos como
// parámetros, mismo criterio que svc/seleccion/confirm:
private static async Task<NuevaImportacionViewModel> CrearEnPasoRevisarAsync(
    Mock<IImportacionService> svc, Mock<IServicioSeleccionArchivo> seleccion, Mock<IConfirmacionService> confirm,
    Mock<IFuenteFinanciamientoService> fuentes, Mock<IRubroGastoService> rubros, Mock<IProveedorService> proveedores,
    ResultadoAnalisisDto analisis)
{
    seleccion.SetupSequence(s => s.SeleccionarArchivoOdsAsync())
        .ReturnsAsync(("gastos.ods", new byte[] { 1 }))
        .ReturnsAsync(("poa.ods", new byte[] { 2 }));
    svc.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(analisis);

    var vm = new NuevaImportacionViewModel(
        svc.Object, seleccion.Object, confirm.Object, fuentes.Object, rubros.Object, proveedores.Object);
    await vm.SeleccionarGastosCommand.ExecuteAsync(null);
    await vm.SeleccionarPoaCommand.ExecuteAsync(null);
    await vm.AnalizarCommand.ExecuteAsync(null);
    return vm;
}
```

**Nota — corrección de convención detectada al escribir este plan:** los pasos de Tasks 6/9/10/11 de este documento describen los tests nuevos con la forma simplificada `vm.GastosNombreArchivo = "..."; vm.PoaNombreArchivo = "..."; await vm.AnalizarCommand.ExecuteAsync(null);` para mantener los snippets legibles. Esa forma COMPILA y PASA (Moq matchea `It.IsAny<byte[]>()` incluso con `_gastosContenido`/`_poaContenido` en null, porque `AnalizarCommand.ExecuteAsync` invocado directo no chequea `CanExecute`), pero NO seguía el patrón idiomático que YA usa este archivo (`CrearEnPasoRevisarAsync` + `SeleccionarGastosCommand`/`SeleccionarPoaCommand` reales). Al implementar Tasks 6/9/10/11, preferir `CrearEnPasoRevisarAsync(svc, seleccion, confirm, fuentes, rubros, proveedores, resultadoAnalisisDto)` en vez del atajo — mismo resultado, consistente con el resto del archivo.

- [ ] **Step 3: Confirmar que el proyecto y los tests compilan**

Run: `dotnet build src/StockApp.Presentation && dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: `Build succeeded.` y PASS de toda la suite del archivo (sin regresión — el cambio de Step 2 es mecánico, no cambia comportamiento).

- [ ] **Step 4: Wiring `DataContextChanged` en la View (code-behind)**

```csharp
// src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs
using Avalonia.Controls;
using StockApp.Presentation.ViewModels.Finanzas;

namespace StockApp.Presentation.Views.Finanzas;

public partial class NuevaImportacionView : UserControl
{
    public NuevaImportacionView()
    {
        InitializeComponent();

        DataContextChanged += async (_, _) =>
        {
            if (DataContext is NuevaImportacionViewModel vm)
                await vm.InicializarMaestrosAsync();
        };
    }
}
```

- [ ] **Step 5: Nombrar el `UserControl` raíz y agregar los `xmlns` que faltan**

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml -->
<!-- Reemplaza el encabezado (líneas 1-10) -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:StockApp.Presentation.ViewModels.Finanzas"
             xmlns:dto="using:StockApp.Application.Finanzas"
             xmlns:conv="using:StockApp.Presentation.Converters"
             xmlns:sys="using:System"
             xmlns:enums="using:StockApp.Domain.Enums"
             xmlns:i="https://github.com/projektanker/icons.avalonia"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="700"
             x:Class="StockApp.Presentation.Views.Finanzas.NuevaImportacionView"
             x:Name="Root"
             x:DataType="vm:NuevaImportacionViewModel">
```

- [ ] **Step 6: Reemplazar el `<DataGrid>` de Gastos (líneas 67-80 actuales) por la versión editable**

```xml
<TabItem Header="Gastos">
    <DataGrid x:Name="GridGastos" ItemsSource="{Binding FilasGastoView}" IsReadOnly="False"
              CanUserSortColumns="True" AutoGenerateColumns="False">
        <DataGrid.Columns>

            <DataGridTemplateColumn Header="Fecha" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Fecha, StringFormat='{}{0:dd/MM/yyyy}'}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableFecha}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <CalendarDatePicker
                            SelectedDate="{Binding Fecha, Converter={x:Static conv:DateOnlyOffsetConverter.Instance}, Mode=TwoWay}"
                            IsEnabled="{Binding EsEditableFecha}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Proveedor" Width="*" IsReadOnly="False" x:CompileBindings="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Proveedor}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableProveedor}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate>
                        <ComboBox IsEditable="True"
                                  Text="{Binding Proveedor, Mode=TwoWay}"
                                  ItemsSource="{Binding #Root.DataContext.ProveedoresDisponibles}"
                                  IsEnabled="{Binding EsEditableProveedor}"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Factura" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBlock Text="{Binding NumeroFactura}" VerticalAlignment="Center" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBox Text="{Binding NumeroFactura, Mode=TwoWay}" IsEnabled="{Binding EsEditableNumeroFactura}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Detalle" Width="2*" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Detalle}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableDetalle}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBox Text="{Binding Detalle, Mode=TwoWay}" IsEnabled="{Binding EsEditableDetalle}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Monto" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Monto}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableMonto}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBox Text="{Binding Monto, Converter={x:Static conv:DecimalOpcionalConverter.Instance}, Mode=TwoWay}"
                                  IsEnabled="{Binding EsEditableMonto}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Fuente" Width="Auto" IsReadOnly="False" x:CompileBindings="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Fuente}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableFuente}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate>
                        <ComboBox IsEditable="True"
                                  Text="{Binding Fuente, Mode=TwoWay}"
                                  ItemsSource="{Binding #Root.DataContext.FuentesDisponibles}"
                                  IsEnabled="{Binding EsEditableFuente}"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Rubro" Width="Auto" IsReadOnly="False" x:CompileBindings="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Rubro}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableRubro}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate>
                        <!-- A diferencia de Proveedor/Fuente (sólo Nombre), RubroGasto tiene
                             Codigo+Nombre separados: elegir un item existente setea AMBAS
                             propiedades de la fila (Rubro=Nombre, CodigoRubro=Codigo) — escribir
                             texto libre que no matchea ningún RubroGasto existente deja
                             CodigoRubro sin resolver (la fila queda con error de validación,
                             [Required] en CodigoRubro la marca como Advertencia: los rubros
                             nuevos SÓLO se declaran vía la pestaña Maestros nuevos — Task 9 — con
                             el Código que ya viene del análisis, un combo de fila no puede
                             inventar un Código nuevo). -->
                        <ComboBox IsEditable="True"
                                  Text="{Binding Rubro, Mode=TwoWay}"
                                  ItemsSource="{Binding #Root.DataContext.RubrosDisponibles}"
                                  SelectionChanged="RubroComboBox_SelectionChanged"
                                  IsEnabled="{Binding EsEditableRubro}"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Condición" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBlock Text="{Binding Condicion}" VerticalAlignment="Center" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <ComboBox ItemsSource="{Binding CondicionesDisponibles}" SelectedItem="{Binding Condicion, Mode=TwoWay}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Vencimiento" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <TextBlock Text="{Binding FechaVencimiento, StringFormat='{}{0:dd/MM/yyyy}'}" VerticalAlignment="Center" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <!-- IsEnabled sólo si Condicion == CondicionPago.Credito. x:Static NO invoca
                             métodos (Enum.Parse no es sintaxis válida acá) — se referencia el valor
                             de enum directo, mismo patrón que ya usa este archivo para
                             PasoWizardImportacion (NuevaImportacionView.axaml:25). Requiere
                             xmlns:enums="using:StockApp.Domain.Enums" en el encabezado (Step 5). -->
                        <CalendarDatePicker
                            SelectedDate="{Binding FechaVencimiento, Converter={x:Static conv:DateOnlyOffsetConverter.Instance}, Mode=TwoWay}"
                            IsEnabled="{Binding Condicion, Converter={x:Static ObjectConverters.Equal}, ConverterParameter={x:Static enums:CondicionPago.Credito}}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:FilaGastoEditableVm}}" Width="Auto" IsReadOnly="True" />

            <DataGridTemplateColumn Header="" Width="40" IsReadOnly="True">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaGastoEditableVm">
                        <Button Classes="ghost" Command="{Binding DesbloquearCommand}"
                                IsVisible="{Binding !Desbloqueada}" ToolTip.Tip="Desbloquear fila para corregir">
                            <i:Icon Value="mdi-pencil" />
                        </Button>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

        </DataGrid.Columns>
    </DataGrid>
</TabItem>
```

- [ ] **Step 7: Agregar `CondicionesDisponibles` a `FilaGastoEditableVm` (falta del Task 3 — se detecta acá al escribir el binding del combo; agregarlo ahí, no como parche suelto)**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs
// Agregar dentro de la clase, junto a las demás propiedades:
using System;
// ...
public IReadOnlyList<CondicionPago> CondicionesDisponibles { get; } = Enum.GetValues<CondicionPago>();
```

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaGastoEditableVmTests"`
Expected: PASS (12/12, sin regresión — es una propiedad nueva, no cambia comportamiento existente).

- [ ] **Step 8: Code-behind del handler `RubroComboBox_SelectionChanged`**

```csharp
// src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs
// Agregar el using y el método:
using System.Linq;
using StockApp.Domain.Entities;

private void RubroComboBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
{
    if (sender is ComboBox { SelectedItem: RubroGasto rubro } combo
        && combo.DataContext is ViewModels.Finanzas.FilaGastoEditableVm fila)
    {
        fila.CodigoRubro = rubro.Codigo;
        fila.Rubro = rubro.Nombre;
    }
}
```

- [ ] **Step 9: Compilar y verificar**

Run: `dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 10: Test headless — candado por celda, `ComboBox IsEditable` y regresión del bug #232, en `StockApp.Presentation.UiTests`** (calcado de `MovimientoFormControlValidacionTests.cs`: monta la View real — `NuevaImportacionView` — con un `NuevaImportacionViewModel` real y fakes hechos a mano, ya que este proyecto no referencia Moq. A diferencia de `DataGridSortClickTests.cs`, que necesitaba un click de puntero real porque el bug de sort era de ruteo de evento en el header, acá el mecanismo bajo prueba — begin/commit de edición contra un `ItemsSource` respaldado por `DataGridCollectionView`, que es exactamente donde vivió el bug #232 — se dirige con la API pública de `DataGrid` (`SelectedItem`/`CurrentColumn`/`BeginEdit()`/`CommitEdit()`/`CancelEdit()`; `DataGridCell`/`DataGridRow` no exponen `Column`/`Index`/etc. como público, confirmado contra `Avalonia.Controls.DataGrid.dll` 12.0.1, así que no son usables desde un proyecto de test externo). El control de edición vivo (`ComboBox`/`TextBox`) se ubica luego con `GetVisualDescendants()`, igual que los dos precedentes.)

```csharp
// tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using StockApp.Application.Catalogo;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.Services;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Fakes minimos de las dependencias de NuevaImportacionViewModel (F5d Entrega 2), mismo criterio
/// que MovimientoRegistroFakes.cs: este proyecto no referencia Moq, asi que se escriben a mano y
/// lanzan NotSupportedException en los miembros no ejercitados por los tests headless de la
/// grilla. Reutiliza ConfirmacionServiceFake (ya existe en MovimientoRegistroFakes.cs, mismo
/// namespace, misma interfaz IConfirmacionService).
/// </summary>
internal sealed class ImportacionServiceFake : IImportacionService
{
    private readonly ResultadoAnalisisDto _resultado;

    public ImportacionServiceFake(ResultadoAnalisisDto resultado) => _resultado = resultado;

    public Task<ResultadoAnalisisDto> AnalizarAsync(
        string nombreArchivoGastos, byte[] gastosOds, string nombreArchivoPoa, byte[] poaOds, int ejercicio)
        => Task.FromResult(_resultado);

    public Task<ResultadoConfirmacionDto> ConfirmarAsync(ConfirmarImportacionDto dto)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<ResultadoReversionDto> RevertirAsync(Guid idImportacion)
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<IReadOnlyList<ImportacionHistorialDto>> ListarHistorialAsync()
        => throw new NotSupportedException("No usado en este banco de pruebas.");
}

internal sealed class ServicioSeleccionArchivoFake : IServicioSeleccionArchivo
{
    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoAsync()
        => throw new NotSupportedException("No usado en este banco de pruebas.");

    public Task<(string NombreArchivo, byte[] Contenido)?> SeleccionarArchivoOdsAsync()
        => Task.FromResult<(string NombreArchivo, byte[] Contenido)?>(("archivo.ods", new byte[] { 1 }));
}

internal sealed class FuenteFinanciamientoServiceFake : IFuenteFinanciamientoService
{
    private readonly IReadOnlyList<FuenteFinanciamiento> _fuentes;

    public FuenteFinanciamientoServiceFake(IReadOnlyList<FuenteFinanciamiento> fuentes) => _fuentes = fuentes;

    public Task<int> AltaAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(FuenteFinanciamiento fuente) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarTodasAsync() => Task.FromResult(_fuentes);
    public Task<IReadOnlyList<FuenteFinanciamiento>> ListarActivasAsync() => Task.FromResult(_fuentes);
}

internal sealed class RubroGastoServiceFake : IRubroGastoService
{
    private readonly IReadOnlyList<RubroGasto> _rubros;

    public RubroGastoServiceFake(IReadOnlyList<RubroGasto> rubros) => _rubros = rubros;

    public Task<int> AltaAsync(RubroGasto rubro) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(RubroGasto rubro) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<RubroGasto>> ListarTodosAsync() => Task.FromResult(_rubros);
    public Task<IReadOnlyList<RubroGasto>> ListarActivosAsync() => Task.FromResult(_rubros);
}

internal sealed class ProveedorServiceFake : IProveedorService
{
    private readonly IReadOnlyList<Proveedor> _proveedores;

    public ProveedorServiceFake(IReadOnlyList<Proveedor> proveedores) => _proveedores = proveedores;

    public Task<int> AltaAsync(Proveedor proveedor) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(Proveedor proveedor) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<Proveedor>> ListarTodosAsync() => Task.FromResult(_proveedores);
}
```

```csharp
// tests/StockApp.Presentation.UiTests/NuevaImportacionGastosGridTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Tests headless de la grilla editable de Gastos (F5d Entrega 2 Task 7), calcados de
/// DataGridSortClickTests.cs y MovimientoFormControlValidacionTests.cs. Cubren automatizado lo
/// que una primera pasada de este plan dejaba solo como verificación orgánica manual: candado
/// por celda, ComboBox IsEditable con texto libre, y la regresión del bug
/// AvaloniaUI/Avalonia.Controls.DataGrid#232 (edición inline con DataGridCollectionView).
/// </summary>
public class NuevaImportacionGastosGridTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(GastoAnalizadoDto gasto)
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
        var seleccion = new ServicioSeleccionArchivoFake();
        var fuentes = new FuenteFinanciamientoServiceFake(new List<FuenteFinanciamiento>());
        var rubros = new RubroGastoServiceFake(new List<RubroGasto>());
        var proveedores = new ProveedorServiceFake(new List<Proveedor>());

        var vm = new NuevaImportacionViewModel(
            service, seleccion, new ConfirmacionServiceFake(), fuentes, rubros, proveedores);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        await vm.AnalizarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "GridGastos");
        return (window, grid, vm);
    }

    private static GastoAnalizadoDto GastoBase(string? proveedor, string? numeroFactura, string? fuente) => new(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: proveedor, ProveedorNuevo: false,
        NumeroFactura: numeroFactura, NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: fuente, FuenteDesconocida: fuente is null,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);

    [AvaloniaFact]
    public async Task CeldaProveedorConValorCargado_QuedaBloqueada_CeldaFuenteFaltante_EsEditable()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: null);
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Proveedor"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        var comboProveedor = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.False(comboProveedor.IsEnabled);
        grid.CancelEdit();
        Dispatcher.UIThread.RunJobs();

        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Fuente"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();
        var comboFuente = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.True(comboFuente.IsEnabled);
    }

    [AvaloniaFact]
    public async Task ComboBoxDeFuente_EsEditable_AceptaTextoLibre()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: "F-1", fuente: null);
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Fuente"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        combo.Text = "Fuente Municipal Nueva";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        Assert.Equal("Fuente Municipal Nueva", fila.Fuente);
    }

    [AvaloniaFact]
    public async Task EditarFactura_Commitea_SinPerderNiDuplicarLaFila()
    {
        var gasto = GastoBase(proveedor: "ACME SA", numeroFactura: null, fuente: "Rentas Generales");
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(gasto);
        Assert.Single(vm.FilasGasto);
        var fila = vm.FilasGasto[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Factura"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var texto = window.GetVisualDescendants().OfType<TextBox>().First();
        texto.Text = "F-2026-001";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        // Regresión AvaloniaUI/Avalonia.Controls.DataGrid#232: el commit vía DataGridCollectionView
        // no debe perder ni duplicar la fila.
        Assert.Single(vm.FilasGasto);
        Assert.Same(fila, vm.FilasGasto[0]);
        Assert.Equal("F-2026-001", vm.FilasGasto[0].NumeroFactura);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~NuevaImportacionGastosGridTests"`
Expected: PASS (3/3).

- [ ] **Step 11: Verificación orgánica (chequeo final de UX, no el único gate — lo automatizable ya lo cubre el Step 10)** — levantar la app real (WSLg, patrón ya establecido en el proyecto), ir a Finanzas → Importar → cargar `PlanillaGastos2026.ods`/`PlanillaPoa2026.ods` (fixtures en `tests/StockApp.Infrastructure.Tests/Fixtures/Finanzas/`), y en el Paso 2 confirmar visualmente: (a) el ícono `mdi-lock` aparece en las celdas ya cargadas; (b) el botón ✎ desbloquea una fila completa; (c) el combo de Proveedor/Fuente/Rubro muestra los maestros existentes junto con la opción de texto libre.

- [ ] **Step 12: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs \
        tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs \
        tests/StockApp.Presentation.UiTests/NuevaImportacionGastosGridTests.cs
git commit -m "feat(finanzas): grilla de Gastos editable con combos, date-pickers y candado por celda (F5d Entrega 2)"
```

---

### Task 8: Grillas de Ingresos y Líneas POA editables (XAML)

**Files:**
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Modify: `tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs` — agrega el fake de `ILineaPoaService` (ripple del Step 3 de este Task) y actualiza `MontarEnPasoRevisarAsync` de Task 7 para pasar el 7mo argumento del constructor.
- Create: `tests/StockApp.Presentation.UiTests/NuevaImportacionLineasPoaGridTests.cs` — test headless de la grilla de Líneas POA (`ComboBox IsEditable` de Programa gateado por `EsNueva`, regresión del bug #232).

**Interfaces:**
- Consumes: `FuentesDisponibles` (Task 7, reusado para el combo de Fuente de Ingresos). `FilaLineaPoaEditableVm.Asignaciones`/`EsNueva`/`Programa` (Task 5).

- [ ] **Step 1: Reemplazar el `<DataGrid>` de Ingresos (líneas 81-91 actuales) por la versión editable**

```xml
<TabItem Header="Ingresos">
    <DataGrid ItemsSource="{Binding FilasIngresoView}" IsReadOnly="False"
              CanUserSortColumns="True" AutoGenerateColumns="False">
        <DataGrid.Columns>

            <DataGridTemplateColumn Header="Fecha" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Fecha, StringFormat='{}{0:dd/MM/yyyy}'}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableFecha}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <CalendarDatePicker
                            SelectedDate="{Binding Fecha, Converter={x:Static conv:DateOnlyOffsetConverter.Instance}, Mode=TwoWay}"
                            IsEnabled="{Binding EsEditableFecha}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Concepto" Width="*" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Concepto}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableConcepto}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <TextBox Text="{Binding Concepto, Mode=TwoWay}" IsEnabled="{Binding EsEditableConcepto}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Monto" Width="Auto" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Monto}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableMonto}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <TextBox Text="{Binding Monto, Converter={x:Static conv:DecimalOpcionalConverter.Instance}, Mode=TwoWay}"
                                  IsEnabled="{Binding EsEditableMonto}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Fuente" Width="Auto" IsReadOnly="False" x:CompileBindings="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate>
                        <StackPanel Orientation="Horizontal" Spacing="4">
                            <TextBlock Text="{Binding Fuente}" VerticalAlignment="Center" />
                            <i:Icon Value="mdi-lock" IsVisible="{Binding !EsEditableFuente}" FontSize="12" Opacity="0.5" />
                        </StackPanel>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate>
                        <ComboBox IsEditable="True"
                                  Text="{Binding Fuente, Mode=TwoWay}"
                                  ItemsSource="{Binding #Root.DataContext.FuentesDisponibles}"
                                  IsEnabled="{Binding EsEditableFuente}"
                                  HorizontalAlignment="Stretch">
                            <ComboBox.ItemTemplate>
                                <DataTemplate>
                                    <TextBlock Text="{Binding Nombre}" />
                                </DataTemplate>
                            </ComboBox.ItemTemplate>
                        </ComboBox>
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:FilaIngresoEditableVm}}" Width="Auto" IsReadOnly="True" />

            <DataGridTemplateColumn Header="" Width="40" IsReadOnly="True">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaIngresoEditableVm">
                        <Button Classes="ghost" Command="{Binding DesbloquearCommand}"
                                IsVisible="{Binding !Desbloqueada}" ToolTip.Tip="Desbloquear fila para corregir">
                            <i:Icon Value="mdi-pencil" />
                        </Button>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

        </DataGrid.Columns>
    </DataGrid>
</TabItem>
```

- [ ] **Step 2: Reemplazar el `<DataGrid>` de Líneas POA (líneas 92-103 actuales) por la versión editable**

`Asignaciones` es de sólo lectura (Task 5, fuera de alcance editarlas en Entrega 2) — se muestra como texto plano concatenado vía un `IValueConverter` chico inline-friendly: en vez de agregar OTRO converter nuevo (fuera del alcance pedido — el spec sólo pide el converter de fecha, Task 2), se resuelve con un `MultiBinding`/`StringFormat` simple NO: Avalonia no soporta agregación de listas en `StringFormat` directo, así que se usa un `ItemsControl` anidado (sin converter nuevo) mostrando cada asignación como `Fuente: Monto`.

```xml
<TabItem Header="Líneas POA">
    <DataGrid x:Name="GridLineasPoa" ItemsSource="{Binding FilasLineaPoaView}" IsReadOnly="False"
              CanUserSortColumns="True" AutoGenerateColumns="False">
        <DataGrid.Columns>

            <DataGridTextColumn Header="Hoja" Binding="{Binding Hoja, DataType={x:Type vm:FilaLineaPoaEditableVm}}" Width="*" IsReadOnly="True" />

            <DataGridTemplateColumn Header="Asignaciones" Width="2*" IsReadOnly="True">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaLineaPoaEditableVm">
                        <ItemsControl ItemsSource="{Binding Asignaciones}">
                            <ItemsControl.ItemTemplate>
                                <DataTemplate x:DataType="vm:AsignacionLineaPoaVm">
                                    <!-- Fuente Y Monto (Presupuesto) — no sólo la fuente: el resultado en
                                         pantalla debe leerse "Rentas Generales: $ 6.341.849", no
                                         "Rentas Generales:" a secas. MonedaConverter porque Presupuesto
                                         es decimal NO nullable (DecimalOpcionalConverter es para decimal?). -->
                                    <StackPanel Orientation="Horizontal" Spacing="4">
                                        <TextBlock Text="{Binding Fuente, StringFormat='{}{0}: '}" />
                                        <TextBlock Text="{Binding Presupuesto, Converter={x:Static conv:MonedaConverter.Instance}}" />
                                    </StackPanel>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
            </DataGridTemplateColumn>

            <DataGridTemplateColumn Header="Programa" Width="*" IsReadOnly="False">
                <DataGridTemplateColumn.CellTemplate>
                    <DataTemplate x:DataType="vm:FilaLineaPoaEditableVm">
                        <TextBlock Text="{Binding Programa}" VerticalAlignment="Center" IsVisible="{Binding EsNueva}" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellTemplate>
                <DataGridTemplateColumn.CellEditingTemplate>
                    <DataTemplate x:DataType="vm:FilaLineaPoaEditableVm">
                        <ComboBox IsEditable="True"
                                  Text="{Binding Programa, Mode=TwoWay}"
                                  ItemsSource="{Binding #Root.DataContext.ProgramasExistentes}"
                                  IsEnabled="{Binding EsNueva}"
                                  IsVisible="{Binding EsNueva}"
                                  HorizontalAlignment="Stretch" />
                    </DataTemplate>
                </DataGridTemplateColumn.CellEditingTemplate>
            </DataGridTemplateColumn>

            <DataGridTextColumn Header="Nueva" Binding="{Binding EsNueva, DataType={x:Type vm:FilaLineaPoaEditableVm}}" Width="Auto" IsReadOnly="True" />

            <DataGridTextColumn Header="Estado" Binding="{Binding Estado, DataType={x:Type vm:FilaLineaPoaEditableVm}}" Width="Auto" IsReadOnly="True" />

        </DataGrid.Columns>
    </DataGrid>
</TabItem>
```

**Corrección detectada al escribir este step:** la columna "Programa" referencia `#Root.DataContext.ProgramasExistentes`, que todavía no existe en `NuevaImportacionViewModel` (el diseño §6 pide "autocompletar de los programas ya usados en líneas existentes" pero ningún Task anterior lo agregó — se agrega ACÁ, no en Task 6, porque recién ahora se vuelve necesario y así queda junto al lugar que lo consume). Agregar antes de continuar:

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// Campo + carga en InicializarMaestrosAsync (agregar ILineaPoaService al constructor, Step 1 de Task 7 queda incompleto sin esto):
private readonly ILineaPoaService _lineasPoaService;

public ObservableCollection<string> ProgramasExistentes { get; } = new();

// Constructor: agregar el parámetro ILineaPoaService lineasPoaService y asignar _lineasPoaService = lineasPoaService;

// Dentro de InicializarMaestrosAsync, al final:
var lineas = await _lineasPoaService.ListarTodasAsync();
ProgramasExistentes.Clear();
foreach (var programa in lineas.Select(l => l.Programa).Distinct().OrderBy(p => p))
    ProgramasExistentes.Add(programa);
```

- [ ] **Step 3: Volver a Task 7 Step 1 y agregar el parámetro `ILineaPoaService lineasPoaService` al constructor de `NuevaImportacionViewModel`** (ripple documentado arriba — se hace acá para no reabrir el commit de Task 7)

```csharp
public NuevaImportacionViewModel(
    IImportacionService service, IServicioSeleccionArchivo seleccion, IConfirmacionService confirmacion,
    IFuenteFinanciamientoService fuentesService, IRubroGastoService rubrosService, IProveedorService proveedoresService,
    ILineaPoaService lineasPoaService)
{
    _service = service;
    _seleccion = seleccion;
    _confirmacion = confirmacion;
    _fuentesService = fuentesService;
    _rubrosService = rubrosService;
    _proveedoresService = proveedoresService;
    _lineasPoaService = lineasPoaService;

    FilasGastoView = new DataGridCollectionView(FilasGasto);
    FilasIngresoView = new DataGridCollectionView(FilasIngreso);
    FilasLineaPoaView = new DataGridCollectionView(FilasLineaPoa);
}
```

- [ ] **Step 4: Actualizar los tests de `NuevaImportacionViewModelTests.Crear()` con el mock de `ILineaPoaService`** (ripple obligatorio — el helper del archivo de test ya mockea `IFuenteFinanciamientoService`/`IRubroGastoService`/`IProveedorService` desde Task 7; agregar el 4to mock con el mismo patrón)

```csharp
var lineasPoaService = new Mock<ILineaPoaService>();
lineasPoaService.Setup(s => s.ListarTodasAsync()).ReturnsAsync(new List<LineaPoa>());
// ... pasar lineasPoaService.Object como último argumento del constructor del VM.
```

**Ripple adicional — `tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs` (Task 7 Step 10) también construye `NuevaImportacionViewModel` con el constructor viejo de 6 argumentos y deja de compilar con el cambio de Step 3.** Ese proyecto no referencia Moq, así que el fake se agrega a mano, mismo criterio que los demás:

```csharp
// tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs — agregar:
using StockApp.Application.Finanzas;

internal sealed class LineaPoaServiceFake : ILineaPoaService
{
    private readonly IReadOnlyList<LineaPoa> _lineas;

    public LineaPoaServiceFake(IReadOnlyList<LineaPoa> lineas) => _lineas = lineas;

    public Task<int> AltaAsync(LineaPoa linea) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task ModificarAsync(LineaPoa linea) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task BajaLogicaAsync(int id) => throw new NotSupportedException("No usado en este banco de pruebas.");
    public Task<IReadOnlyList<LineaPoa>> ListarTodasAsync() => Task.FromResult(_lineas);
    public Task<IReadOnlyList<LineaPoa>> ListarActivasAsync() => Task.FromResult(_lineas);
}
```

```csharp
// tests/StockApp.Presentation.UiTests/NuevaImportacionGastosGridTests.cs (Task 7 Step 10)
// MontarEnPasoRevisarAsync — agregar el 7mo argumento:
var lineasPoa = new LineaPoaServiceFake(new List<LineaPoa>());
var vm = new NuevaImportacionViewModel(
    service, seleccion, new ConfirmacionServiceFake(), fuentes, rubros, proveedores, lineasPoa);
```

- [ ] **Step 5: Compilar**

Run: `dotnet build src/StockApp.Presentation && dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: `Build succeeded.` y PASS de toda la suite del archivo.

- [ ] **Step 6: Test headless — `ComboBox IsEditable` de Programa gateado por `EsNueva`, en `StockApp.Presentation.UiTests`** (mismo criterio que Task 7 Step 10: monta `NuevaImportacionView` real con `NuevaImportacionViewModel` real y los fakes hechos a mano; el `ItemsSource` de esta grilla también es `DataGridCollectionView`, así que el commit vía `BeginEdit`/`CommitEdit` ejercita la misma superficie del bug #232)

```csharp
// tests/StockApp.Presentation.UiTests/NuevaImportacionLineasPoaGridTests.cs
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using StockApp.Application.Finanzas;
using StockApp.Domain.Entities;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.UiTests;

/// <summary>
/// Tests headless de la grilla editable de Líneas POA (F5d Entrega 2 Task 8), mismo criterio que
/// NuevaImportacionGastosGridTests.cs (Task 7): cubren automatizado el combo de Programa gateado
/// por EsNueva y la regresión del bug AvaloniaUI/Avalonia.Controls.DataGrid#232 sobre esta grilla.
/// </summary>
public class NuevaImportacionLineasPoaGridTests
{
    private const string Xaml = """
        <Window xmlns="https://github.com/avaloniaui"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                xmlns:fin="clr-namespace:StockApp.Presentation.Views.Finanzas;assembly=StockApp.Presentation"
                Width="1000" Height="700">
            <fin:NuevaImportacionView />
        </Window>
        """;

    private static async Task<(Window Window, DataGrid Grid, NuevaImportacionViewModel Vm)> MontarEnPasoRevisarAsync(LineaPoaAnalizadaDto linea)
    {
        var service = new ImportacionServiceFake(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto> { linea },
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
        var seleccion = new ServicioSeleccionArchivoFake();
        var fuentes = new FuenteFinanciamientoServiceFake(new List<FuenteFinanciamiento>());
        var rubros = new RubroGastoServiceFake(new List<RubroGasto>());
        var proveedores = new ProveedorServiceFake(new List<Proveedor>());
        var lineasPoa = new LineaPoaServiceFake(new List<LineaPoa>());

        var vm = new NuevaImportacionViewModel(
            service, seleccion, new ConfirmacionServiceFake(), fuentes, rubros, proveedores, lineasPoa);

        var window = AvaloniaRuntimeXamlLoader.Parse<Window>(Xaml, typeof(TestApp).Assembly);
        window.DataContext = vm;
        window.Show();
        Dispatcher.UIThread.RunJobs();

        await vm.SeleccionarGastosCommand.ExecuteAsync(null);
        await vm.SeleccionarPoaCommand.ExecuteAsync(null);
        await vm.AnalizarCommand.ExecuteAsync(null);
        Dispatcher.UIThread.RunJobs();

        var grid = window.GetVisualDescendants().OfType<DataGrid>().First(g => g.Name == "GridLineasPoa");
        return (window, grid, vm);
    }

    private static LineaPoaAnalizadaDto LineaBase(bool esNueva) => new(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: esNueva,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());

    [AvaloniaFact]
    public async Task LineaExistente_ProgramaNoEsVisible_LineaNueva_ProgramaEsEditable()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(LineaBase(esNueva: false));
        var fila = vm.FilasLineaPoa[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Programa"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        // Línea existente: EsNueva=false, el combo de Programa no debe quedar visible/editable.
        Assert.Empty(window.GetVisualDescendants().OfType<ComboBox>());
    }

    [AvaloniaFact]
    public async Task LineaNueva_ProgramaEsEditable_CommiteaSinPerderLaFila()
    {
        var (window, grid, vm) = await MontarEnPasoRevisarAsync(LineaBase(esNueva: true));
        Assert.Single(vm.FilasLineaPoa);
        var fila = vm.FilasLineaPoa[0];

        grid.SelectedItem = fila;
        grid.CurrentColumn = grid.Columns.First(c => Equals(c.Header, "Programa"));
        Dispatcher.UIThread.RunJobs();
        grid.BeginEdit();
        Dispatcher.UIThread.RunJobs();

        var combo = window.GetVisualDescendants().OfType<ComboBox>().First();
        Assert.True(combo.IsEnabled);
        combo.Text = "Compostaje comunitario";
        Dispatcher.UIThread.RunJobs();
        grid.CommitEdit();
        Dispatcher.UIThread.RunJobs();

        // Regresión AvaloniaUI/Avalonia.Controls.DataGrid#232: el commit vía DataGridCollectionView
        // no debe perder ni duplicar la fila.
        Assert.Single(vm.FilasLineaPoa);
        Assert.Equal("Compostaje comunitario", vm.FilasLineaPoa[0].Programa);
    }
}
```

Run: `dotnet test tests/StockApp.Presentation.UiTests --filter "FullyQualifiedName~NuevaImportacionLineasPoaGridTests"`
Expected: PASS (2/2).

- [ ] **Step 7: Verificación orgánica (chequeo final de UX, no el único gate)** — Paso 2, pestañas Ingresos y Líneas POA: confirmar candado/edición igual que Task 7 Step 11; confirmar visualmente que una línea con `EsNueva` muestra el campo Programa con el ícono de autocompletar, y una línea existente NO lo muestra.

- [ ] **Step 8: Commit**

```bash
git add src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs \
        tests/StockApp.Presentation.UiTests/NuevaImportacionFakes.cs \
        tests/StockApp.Presentation.UiTests/NuevaImportacionGastosGridTests.cs \
        tests/StockApp.Presentation.UiTests/NuevaImportacionLineasPoaGridTests.cs
git commit -m "feat(finanzas): grillas de Ingresos y Lineas POA editables + Programa para lineas nuevas (F5d Entrega 2)"
```

---

### Task 9: Maestros nuevos — auto-declaración desde combo + tablero de rubros con Nombre obligatorio

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FilaRubroNuevoVm.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaRubroNuevoVmTests.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`

**Interfaces:**
- Consumes: `CodigoRubroNuevoDto(int Codigo, string? NombreSugerido)` (ya existe).
- Produces: `FilaRubroNuevoVm : ObservableValidator` con `Codigo` read-only + `Nombre` editable (`[Required]`), factory `Desde(CodigoRubroNuevoDto)`. `NuevaImportacionViewModel.RubrosNuevos` cambia de tipo (`ObservableCollection<CodigoRubroNuevoDto>` → `ObservableCollection<FilaRubroNuevoVm>`) — ripple hacia Task 10 (mapeo) y hacia el binding XAML de la pestaña Maestros nuevos.

**Decisión de diseño — alcance de "auto-declaración":** el diseño (§5) menciona "Proveedores / Fuentes / Rubros" juntos, pero SÓLO Proveedor y Fuente son nombres puros sin código — auto-declarar "escribí texto que no matchea nada → se agrega a la lista de nuevos" tiene sentido ahí. Rubro es distinto (Task 7 ya lo estableció): `RubroGasto` tiene `Código` numérico + `Nombre`, y un rubro nuevo SIEMPRE se origina en un código que YA viene de la planilla (`RubroDesconocido`/`CodigoRubroNuevoDto`, calculado por el backend en `AnalisisImportacionService`) — no hay forma de que el combo de una fila "invente" un código nuevo. Por eso la auto-declaración de este Task aplica sólo a Proveedor/Fuente; Rubro se resuelve completando `Nombre` en la pestaña Maestros nuevos (el tablero de este Task).

**Por qué este Task NO suma un Step de test headless (a diferencia de Tasks 7/8):** el tablero "Maestros nuevos" se arma con `ItemsControl` (`Nombre` de `FilaRubroNuevoVm` editado con un `TextBox` simple dentro del `ItemTemplate`), no con `DataGridTemplateColumn`. No hay candado por celda, no hay `ComboBox IsEditable`, y el `ItemsSource` no es un `DataGridCollectionView` — así que no atraviesa el mecanismo del bug AvaloniaUI/Avalonia.Controls.DataGrid#232 que motiva los tests headless de Tasks 7/8. La cobertura de este Task ya es 100% de VM (Steps 1-8, `FilaRubroNuevoVmTests` + los tests de auto-declaración agregados a `NuevaImportacionViewModelTests`).

- [ ] **Step 1: Escribir el test que falla — `FilaRubroNuevoVm`**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaRubroNuevoVmTests.cs
using System.Linq;
using StockApp.Application.Finanzas;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaRubroNuevoVmTests
{
    [Fact]
    public void Desde_MapeaCodigoYNombreSugerido()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, "Materiales"));

        Assert.Equal(42, fila.Codigo);
        Assert.Equal("Materiales", fila.Nombre);
        Assert.False(fila.HasErrors);
    }

    [Fact]
    public void Desde_SinNombreSugerido_TieneErrorDeValidacion()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, null));

        Assert.True(fila.HasErrors);
        Assert.NotEmpty(fila.GetErrors(nameof(fila.Nombre)).Cast<object>());
    }

    [Fact]
    public void Nombre_SeCompleta_LimpiaElErrorDeValidacion()
    {
        var fila = FilaRubroNuevoVm.Desde(new CodigoRubroNuevoDto(42, null));

        fila.Nombre = "Materiales de obra";

        Assert.False(fila.HasErrors);
    }
}
```

- [ ] **Step 2: Correr — falla en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaRubroNuevoVmTests"`
Expected: FAIL — `CS0246`.

- [ ] **Step 3: Implementación**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaRubroNuevoVm.cs
using System.ComponentModel.DataAnnotations;
using CommunityToolkit.Mvvm.ComponentModel;
using StockApp.Application.Finanzas;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Fila del tablero "Maestros nuevos" para un rubro nuevo (F5d Entrega 2 Task 9). El análisis
/// deja NombreSugerido en null (el Código sí lo conoce, viene de la planilla) — Entrega 1 lo
/// mandaba como "" a confirmar, violando RubroNuevoConfirmarDto.Nombre no-vacío (bug documentado
/// en el diseño §5); esta fila exige el nombre acá, antes de llegar a Confirmar.
/// </summary>
public partial class FilaRubroNuevoVm : ObservableValidator
{
    [ObservableProperty] private int _codigo;

    [ObservableProperty]
    [NotifyDataErrorInfo]
    [Required(ErrorMessage = "El nombre del rubro nuevo es obligatorio.")]
    private string? _nombre;

    public static FilaRubroNuevoVm Desde(CodigoRubroNuevoDto dto)
    {
        var fila = new FilaRubroNuevoVm { Codigo = dto.Codigo, Nombre = dto.NombreSugerido };
        fila.ValidateAllProperties();
        return fila;
    }
}
```

- [ ] **Step 4: Correr — pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaRubroNuevoVmTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Commit parcial**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/FilaRubroNuevoVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaRubroNuevoVmTests.cs
git commit -m "feat(finanzas): FilaRubroNuevoVm con Nombre obligatorio (F5d Entrega 2)"
```

- [ ] **Step 6: Escribir el test que falla — auto-declaración de Proveedor/Fuente nuevos al editar una celda**

```csharp
// Agregar a NuevaImportacionViewModelTests.cs:

[Fact]
public async Task EditarProveedorDeUnaFilaGasto_ConTextoQueNoMatcheaNingunProveedorExistente_LoAgregaAProveedoresNuevos()
{
    var (vm, service, _, _) = Crear();
    var gastoDto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: null, ProveedorNuevo: false,
        NumeroFactura: null, NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);

    vm.FilasGasto[0].Proveedor = "Nuevo Proveedor SRL";

    Assert.Contains("Nuevo Proveedor SRL", vm.ProveedoresNuevos);
}

[Fact]
public async Task EditarProveedorDeUnaFilaGasto_ConTextoQueYaExisteEnProveedoresDisponibles_NoLoAgrega()
{
    var (vm, service, _, _) = Crear();
    var gastoDto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: null, ProveedorNuevo: false,
        NumeroFactura: null, NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoDto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 0, 1, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    vm.ProveedoresDisponibles.Add(new Proveedor { Id = 1, Nombre = "ACME SA", Activo = true });

    vm.FilasGasto[0].Proveedor = "ACME SA";

    Assert.DoesNotContain("ACME SA", vm.ProveedoresNuevos);
}
```

- [ ] **Step 7: Correr — falla (comportamiento todavía no implementado)**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL en los 2 tests nuevos — `Assert.Contains` no encuentra el string (la lista `ProveedoresNuevos` no se actualiza todavía al editar `Proveedor`).

- [ ] **Step 8: Implementar la auto-declaración**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// Dentro de AnalizarAsync, en el foreach de FilasGasto (extiende el bloque de Task 6 Step 4):
foreach (var g in _analisis.Gastos)
{
    var fila = FilaGastoEditableVm.Desde(g);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    fila.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(fila.Proveedor)) RegistrarSiEsNuevo(ProveedoresDisponibles.Select(p => p.Nombre), ProveedoresNuevos, fila.Proveedor);
        if (e.PropertyName == nameof(fila.Fuente)) RegistrarSiEsNuevo(FuentesDisponibles.Select(f => f.Nombre), FuentesNuevas, fila.Fuente);
    };
    FilasGasto.Add(fila);
}

// Mismo wiring en el foreach de FilasIngreso, sólo Fuente:
foreach (var i in _analisis.Ingresos)
{
    var fila = FilaIngresoEditableVm.Desde(i);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    fila.PropertyChanged += (_, e) =>
    {
        if (e.PropertyName == nameof(fila.Fuente)) RegistrarSiEsNuevo(FuentesDisponibles.Select(f => f.Nombre), FuentesNuevas, fila.Fuente);
    };
    FilasIngreso.Add(fila);
}
```

```csharp
// Nuevo método privado, junto a NotificarGatingCambio:

/// <summary>Auto-declaración de maestro nuevo (F5d Entrega 2 Task 9): si el texto no matchea
/// (case-insensitive) ningún nombre existente Y todavía no está declarado, se agrega a la lista
/// de nuevos. Sin remoción: si el usuario corrige el typo después, el nombre viejo queda
/// declarado (aceptable para Entrega 2 — el usuario revisa la pestaña Maestros nuevos antes de
/// Confirmar).</summary>
private static void RegistrarSiEsNuevo(IEnumerable<string> existentes, ObservableCollection<string> nuevos, string? texto)
{
    if (string.IsNullOrWhiteSpace(texto)) return;
    var normalizado = texto.Trim();
    if (existentes.Any(e => string.Equals(e, normalizado, StringComparison.OrdinalIgnoreCase))) return;
    if (!nuevos.Any(n => string.Equals(n, normalizado, StringComparison.OrdinalIgnoreCase)))
        nuevos.Add(normalizado);
}
```

- [ ] **Step 9: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS.

- [ ] **Step 10: Escribir el test que falla — `RubrosNuevos` pasa a `FilaRubroNuevoVm` y entra en el gating**

```csharp
[Fact]
public async Task AnalizarAsync_PopulaRubrosNuevosComoFilasEditables()
{
    var (vm, service, _, _) = Crear();
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto> { new(42, null) }),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";

    await vm.AnalizarCommand.ExecuteAsync(null);

    var rubro = Assert.Single(vm.RubrosNuevos);
    Assert.Equal(42, rubro.Codigo);
    Assert.True(rubro.HasErrors); // NombreSugerido null => [Required] falla
    Assert.False(vm.PuedeConfirmar); // el gating agregado incluye RubrosNuevos
}
```

- [ ] **Step 11: Correr — falla en compilación** (`RubrosNuevos` sigue siendo `ObservableCollection<CodigoRubroNuevoDto>`, `.HasErrors` no existe en ese tipo)

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `CS1061: 'CodigoRubroNuevoDto' no contiene una definición para 'HasErrors'`.

- [ ] **Step 12: Cambiar el tipo de `RubrosNuevos` e incluirlo en el gating**

```csharp
// Reemplaza la declaración de la propiedad (Task 6 Step 3):
public ObservableCollection<FilaRubroNuevoVm> RubrosNuevos { get; } = new();
```

```csharp
// Reemplaza el bloque de población en AnalizarAsync (Task 6 Step 4):
RubrosNuevos.Clear();
foreach (var r in _analisis.MaestrosNuevos.Rubros)
{
    var fila = FilaRubroNuevoVm.Desde(r);
    fila.ErrorsChanged += (_, _) => NotificarGatingCambio();
    RubrosNuevos.Add(fila);
}
```

```csharp
// Reemplaza HayFilasConErrores (Task 6 Step 5):
private bool HayFilasConErrores() =>
    FilasGasto.Any(f => f.HasErrors) || FilasIngreso.Any(f => f.HasErrors) || FilasLineaPoa.Any(f => f.HasErrors)
    || RubrosNuevos.Any(r => r.HasErrors);
```

```csharp
// Reemplaza el conteo dentro de MensajeConfirmarBloqueado (Task 6 Step 5):
var conErrores = FilasGasto.Count(f => f.HasErrors)
    + FilasIngreso.Count(f => f.HasErrors)
    + FilasLineaPoa.Count(f => f.HasErrors)
    + RubrosNuevos.Count(r => r.HasErrors);
```

- [ ] **Step 13: Correr TODA la suite de `NuevaImportacionViewModelTests`**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS completo (sin regresión en los tests de Tasks 6/7/8 de este mismo archivo).

- [ ] **Step 14: Actualizar el XAML de la pestaña "Maestros nuevos" (líneas 104-125 actuales) — tablero editable con badges**

```xml
<TabItem>
    <TabItem.Header>
        <TextBlock Text="{Binding ProveedoresNuevos.Count, StringFormat='Maestros nuevos ({0})'}" />
    </TabItem.Header>
    <StackPanel Orientation="Horizontal" Spacing="24" Margin="12">
        <StackPanel Spacing="4">
            <TextBlock Text="{Binding ProveedoresNuevos.Count, StringFormat='Proveedores nuevos ({0})'}" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding ProveedoresNuevos}" />
        </StackPanel>
        <StackPanel Spacing="4">
            <TextBlock Text="{Binding FuentesNuevas.Count, StringFormat='Fuentes nuevas ({0})'}" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding FuentesNuevas}" />
        </StackPanel>
        <StackPanel Spacing="4">
            <TextBlock Text="{Binding RubrosNuevos.Count, StringFormat='Rubros nuevos ({0})'}" FontWeight="SemiBold" />
            <ItemsControl ItemsSource="{Binding RubrosNuevos}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate x:DataType="vm:FilaRubroNuevoVm">
                        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,4">
                            <TextBlock Text="{Binding Codigo, StringFormat='Código {0}'}" VerticalAlignment="Center" Width="90" />
                            <TextBox Text="{Binding Nombre, Mode=TwoWay}" Watermark="Nombre del rubro (obligatorio)" Width="220" />
                        </StackPanel>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </StackPanel>
</TabItem>
```

- [ ] **Step 15: Compilar**

Run: `dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 16: Verificación orgánica** — escribir un proveedor/fuente nuevo en una celda del Paso 2 y confirmar que aparece en la pestaña "Maestros nuevos"; confirmar que un rubro nuevo sin Nombre bloquea Confirmar y `MensajeConfirmarBloqueado` lo refleja; completar el Nombre y confirmar que se desbloquea.

- [ ] **Step 17: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): auto-declaracion de maestros nuevos y tablero de rubros con Nombre obligatorio (F5d Entrega 2)"
```

---

### Task 10: Mapeo filas VM → `ConfirmarImportacionDto` (reemplaza `MapearAConfirmacion`)

**Files:**
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`

**Interfaces:**
- Consumes: `FilasGasto`/`FilasIngreso`/`FilasLineaPoa`/`RubrosNuevos`/`ProveedoresNuevos`/`FuentesNuevas` (Tasks 6-9). `GastoConfirmarDto`/`IngresoConfirmarDto`/`LineaPoaConfirmarDto`/`AsignacionConfirmarDto`/`RubroNuevoConfirmarDto`/`MaestrosNuevosConfirmarDto`/`ConfirmarImportacionDto` (`src/StockApp.Application/Finanzas/ConfirmacionImportacionDtos.cs`, ya existen, sin cambios).
- Produces: `MapearAConfirmacion` reescrito (misma firma de propósito, distinta fuente de datos) — usado por `ConfirmarAsync`.

- [ ] **Step 1: Escribir el test que falla — Condicion/FechaVencimiento corregidos por el usuario viajan tal cual (no la heurística de Entrega 1)**

```csharp
// Agregar a NuevaImportacionViewModelTests.cs:

[Fact]
public async Task ConfirmarAsync_UsuarioCorrigioCondicionYVencimiento_MapeaLosValoresDeLaFilaNoLaHeuristica()
{
    var (vm, service, _, _) = Crear();
    var gastoConPoa = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: "RAMBLA"); // heurística de Entrega 1 lo sugeriría Credito, vto=Fecha
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gastoConPoa },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ReturnsAsync(new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);

    // El usuario corrige: es Contado, no Crédito (el reconciliador se equivocó de heurística).
    vm.FilasGasto[0].Condicion = CondicionPago.Contado;
    vm.FilasGasto[0].FechaVencimiento = null;

    await vm.ConfirmarCommand.ExecuteAsync(null);

    service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
        dto.Gastos[0].Condicion == CondicionPago.Contado && dto.Gastos[0].FechaVencimiento == null)));
}

[Fact]
public async Task ConfirmarAsync_LineaPoaNueva_MandaNombreProgramaYAsignacionesAgrupadas()
{
    var (vm, service, _, _) = Crear();
    var lineaC = new LineaPoaAnalizadaDto(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());
    var lineaB = lineaC with { Literal = "B", Presupuesto = 500m, SaldoPlanilla = 500m };
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto> { lineaC, lineaB },
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ReturnsAsync(new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    vm.FilasLineaPoa[0].Programa = "Obras públicas";

    await vm.ConfirmarCommand.ExecuteAsync(null);

    service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
        dto.LineasPoa.Count == 1
        && dto.LineasPoa[0].Nombre == "COMPOSTERAS"
        && dto.LineasPoa[0].Programa == "Obras públicas"
        && dto.LineasPoa[0].Asignaciones.Count == 2)));
}

[Fact]
public async Task ConfirmarAsync_RubroNuevoConNombreCompletado_LoMandaEnMaestrosNuevos()
{
    var (vm, service, _, _) = Crear();
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto> { new(42, null) }),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ReturnsAsync(new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    vm.RubrosNuevos[0].Nombre = "Materiales de obra";

    await vm.ConfirmarCommand.ExecuteAsync(null);

    service.Verify(s => s.ConfirmarAsync(It.Is<ConfirmarImportacionDto>(dto =>
        dto.MaestrosNuevos.Rubros.Count == 1
        && dto.MaestrosNuevos.Rubros[0].Codigo == 42
        && dto.MaestrosNuevos.Rubros[0].Nombre == "Materiales de obra")));
}
```

- [ ] **Step 2: Correr — deben fallar** (el mapeo actual sigue leyendo `_analisis`/la heurística vieja, `LineasPoa` sigue fijo en `[]`)

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL en los 3 tests nuevos.

- [ ] **Step 3: Reescribir `MapearAConfirmacion` y `ConfirmarAsync`**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// Reemplaza ConfirmarAsync (líneas 180-203 actuales) — sólo cambia el call site de MapearAConfirmacion:
[RelayCommand(CanExecute = nameof(PuedeConfirmar))]
private async Task ConfirmarAsync()
{
    if (_analisis is null) return;

    var dto = MapearAConfirmacion(FilasGasto, FilasIngreso, FilasLineaPoa, ProveedoresNuevos, FuentesNuevas, RubrosNuevos, Ejercicio, Forzar);

    try
    {
        ResultadoConfirmacion = await _service.ConfirmarAsync(dto);
        Conflictos.Clear();
        foreach (var c in ResultadoConfirmacion.Conflictos)
            Conflictos.Add(ConflictoGastoFila.Desde(c));
        PasoActual = PasoWizardImportacion.Resultado;
    }
    catch (ValidacionImportacionException vex)
    {
        await _confirmacion.InformarAsync(FormatearErroresValidacion(vex));
    }
    catch (Exception ex)
    {
        await _confirmacion.InformarAsync(ex.Message);
    }
}
```

```csharp
// Reemplaza MapearAConfirmacion COMPLETO (líneas 215-280 actuales de Entrega 1):
/// <summary>
/// Mapeo filas VM editables → confirmación (F5d Entrega 2, reemplaza el mapeo directo
/// análisis→confirmación de Entrega 1). Precondición garantizada por PuedeConfirmar: ninguna
/// fila tiene HasErrors (los [Required]/atributos custom de las filas VM ya cubren exactamente
/// los mismos campos que RequeridoNoNulo defiende acá como cinturón de seguridad extra). A
/// diferencia de Entrega 1, Condicion/FechaVencimiento vienen DIRECTO de la fila (el usuario
/// pudo haber corregido la heurística inicial, ver FilaGastoEditableVm.Desde) y LineasPoa ya NO
/// se manda vacía: las filas con EsNueva==true se mapean con Nombre=Hoja, Programa editado por
/// el usuario y Asignaciones agrupadas (FilaLineaPoaEditableVm.DesdeGrupo, Task 5/6).
/// </summary>
private static ConfirmarImportacionDto MapearAConfirmacion(
    IReadOnlyList<FilaGastoEditableVm> filasGasto,
    IReadOnlyList<FilaIngresoEditableVm> filasIngreso,
    IReadOnlyList<FilaLineaPoaEditableVm> filasLineaPoa,
    IReadOnlyList<string> proveedoresNuevos,
    IReadOnlyList<string> fuentesNuevas,
    IReadOnlyList<FilaRubroNuevoVm> rubrosNuevos,
    int ejercicio, bool forzar)
{
    var ingresos = filasIngreso
        .Select(i => new IngresoConfirmarDto(
            RequeridoNoNulo(i.Fecha, "Ingreso.Fecha"),
            i.Concepto ?? string.Empty,
            RequeridoNoNulo(i.Monto, "Ingreso.Monto"),
            RequeridoNoNulo(i.Fuente, "Ingreso.Fuente")))
        .ToList();

    var gastos = filasGasto
        .Select(g => new GastoConfirmarDto(
            Proveedor: RequeridoNoNulo(g.Proveedor, "Gasto.Proveedor"),
            NumeroFactura: g.NumeroFactura,
            NumeroOrden: g.NumeroOrden,
            Detalle: g.Detalle ?? string.Empty,
            Destino: g.Destino,
            Fecha: RequeridoNoNulo(g.Fecha, "Gasto.Fecha"),
            MontoTotal: RequeridoNoNulo(g.Monto, "Gasto.MontoTotal"),
            Fuente: RequeridoNoNulo(g.Fuente, "Gasto.Fuente"),
            CodigoRubro: RequeridoNoNulo(g.CodigoRubro, "Gasto.CodigoRubro"),
            LineaPoa: g.LineaPoaAsignada,
            Condicion: g.Condicion,
            FechaVencimiento: g.FechaVencimiento))
        .ToList();

    var lineasPoaNuevas = filasLineaPoa
        .Where(f => f.EsNueva)
        .Select(f => new LineaPoaConfirmarDto(
            Nombre: f.Hoja,
            Programa: RequeridoNoNulo(f.Programa, "LineaPoa.Programa"),
            Asignaciones: f.Asignaciones
                .Select(a => new AsignacionConfirmarDto(
                    RequeridoNoNulo(a.Fuente, "LineaPoa.Asignacion.Fuente"), a.Presupuesto))
                .ToList()))
        .ToList();

    var maestrosNuevos = new MaestrosNuevosConfirmarDto(
        proveedoresNuevos.ToList(),
        fuentesNuevas.ToList(),
        rubrosNuevos
            .Select(r => new RubroNuevoConfirmarDto(r.Codigo, RequeridoNoNulo(r.Nombre, "RubroNuevo.Nombre")))
            .ToList());

    return new ConfirmarImportacionDto(ejercicio, forzar, maestrosNuevos, ingresos, gastos, lineasPoaNuevas);
}
```

```csharp
// RequeridoNoNulo (líneas 282-290 actuales) queda IGUAL — sin cambios, se sigue usando tal cual.
```

- [ ] **Step 4: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS (los 3 nuevos + sin regresión en el resto del archivo).

- [ ] **Step 5: Correr la suite completa de Presentation.Tests**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): mapea filas VM a ConfirmarImportacionDto, incluye lineas POA nuevas (F5d Entrega 2)"
```

---

### Task 11: Descomposición visual del error 400 estructurado

**Files:**
- Create: `src/StockApp.Presentation/ViewModels/Finanzas/FilaImportacionEditableVmBase.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs`
- Modify: `src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs`
- Modify: `src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml`
- Modify: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs`
- Test: `tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaImportacionEditableVmBaseTests.cs`

**Interfaces:**
- Consumes: `ValidacionImportacionException.Errores: IReadOnlyDictionary<string, string[]>` con claves `"Tipo[i].Campo"` (`src/StockApp.Domain/Exceptions/ValidacionImportacionException.cs`, ya existe, sin cambios — el ApiClient ya lo reconstruye, `src/StockApp.ApiClient/ApiErrores.cs:107-113`).
- Produces: `FilaImportacionEditableVmBase.TieneErrorServidor: bool` / `MensajeErrorServidor: string?` / `AgregarErrorServidor(string)` / `LimpiarErrorServidor()` — heredado por las 3 filas editables (refactor DRY: Tasks 3-5 dejaban esta lógica repetida 3 veces si se agregaba directo). `NuevaImportacionViewModel.PestanaSeleccionada: int` (nuevo, TwoWay con el `TabControl` del Paso 2).

**Decisión de diseño — precisión por fila, no por celda exacta:** `CommunityToolkit.Mvvm.ObservableValidator` no expone una API pública para inyectar un error EXTERNO (no producido por un `ValidationAttribute`) en el mismo `INotifyDataErrorInfo` que ya usan `[Required]`/`VencimientoCondicional`/`ProgramaObligatorioSiNueva` — inventar una llamada a un método interno no documentado sería fabricar una API que puede no existir en 8.4.1 y romper la compilación. Se resuelve con un mecanismo PARALELO y liviano (`TieneErrorServidor`/`MensajeErrorServidor`, propiedades planas sin DataAnnotations) que marca la FILA completa (no la celda individual) con el mensaje agregado de todos sus campos con error. Es menos preciso que "resaltar la celda exacta" (diseño §8, letra estricta), pero cumple el objetivo real ("el usuario ve exactamente DÓNDE" — ahora sabe la fila Y el campo, por texto, en vez de un diccionario técnico plano) sin arriesgar una implementación rota. Documentado también en el Self-Review de este plan como alcance reducido consciente.

- [ ] **Step 1: Escribir el test que falla — la base class**

```csharp
// tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaImportacionEditableVmBaseTests.cs
using System.Linq;
using StockApp.Presentation.ViewModels.Finanzas;
using Xunit;

namespace StockApp.Presentation.Tests.ViewModels.Finanzas;

public class FilaImportacionEditableVmBaseTests
{
    private sealed class FilaDePrueba : FilaImportacionEditableVmBase { }

    [Fact]
    public void Nueva_NoTieneErrorServidor()
    {
        var fila = new FilaDePrueba();

        Assert.False(fila.TieneErrorServidor);
        Assert.Null(fila.MensajeErrorServidor);
    }

    [Fact]
    public void AgregarErrorServidor_MarcaLaFilaYAcumulaElMensaje()
    {
        var fila = new FilaDePrueba();

        fila.AgregarErrorServidor("Fecha: la fecha es obligatoria.");
        fila.AgregarErrorServidor("Fuente: la fuente es obligatoria.");

        Assert.True(fila.TieneErrorServidor);
        Assert.Contains("Fecha:", fila.MensajeErrorServidor);
        Assert.Contains("Fuente:", fila.MensajeErrorServidor);
    }

    [Fact]
    public void LimpiarErrorServidor_QuitaElMarcadoYElMensaje()
    {
        var fila = new FilaDePrueba();
        fila.AgregarErrorServidor("Fecha: la fecha es obligatoria.");

        fila.LimpiarErrorServidor();

        Assert.False(fila.TieneErrorServidor);
        Assert.Null(fila.MensajeErrorServidor);
    }
}
```

- [ ] **Step 2: Correr — falla en compilación**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaImportacionEditableVmBaseTests"`
Expected: FAIL — `CS0246`.

- [ ] **Step 3: Implementar la base y hacer que las 3 filas hereden de ella**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaImportacionEditableVmBase.cs
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace StockApp.Presentation.ViewModels.Finanzas;

/// <summary>
/// Base común de FilaGastoEditableVm/FilaIngresoEditableVm/FilaLineaPoaEditableVm (F5d Entrega 2
/// Task 11): mecanismo de error de servidor, PARALELO a la validación por DataAnnotations de
/// ObservableValidator (ver decisión de diseño en el plan — ObservableValidator no expone API
/// pública para inyectar errores externos al pipeline de ValidationAttribute).
/// </summary>
public abstract partial class FilaImportacionEditableVmBase : ObservableValidator
{
    [ObservableProperty] private bool _tieneErrorServidor;
    [ObservableProperty] private string? _mensajeErrorServidor;

    private readonly List<string> _mensajesServidor = new();

    public void AgregarErrorServidor(string mensaje)
    {
        _mensajesServidor.Add(mensaje);
        MensajeErrorServidor = string.Join(" | ", _mensajesServidor);
        TieneErrorServidor = true;
    }

    public void LimpiarErrorServidor()
    {
        _mensajesServidor.Clear();
        MensajeErrorServidor = null;
        TieneErrorServidor = false;
    }
}
```

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs
// Cambia SOLO la firma de la clase (línea "public partial class FilaGastoEditableVm : ObservableValidator"):
public partial class FilaGastoEditableVm : FilaImportacionEditableVmBase
```

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs
// Mismo cambio:
public partial class FilaIngresoEditableVm : FilaImportacionEditableVmBase
```

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs
// Mismo cambio:
public partial class FilaLineaPoaEditableVm : FilaImportacionEditableVmBase
```

- [ ] **Step 4: Correr — pasa**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~FilaImportacionEditableVmBaseTests"`
Expected: PASS (3/3).

- [ ] **Step 5: Confirmar que el cambio de jerarquía NO rompió los tests de las 3 filas ni de `NuevaImportacionViewModelTests`**

Run: `dotnet test tests/StockApp.Presentation.Tests`
Expected: PASS completo.

- [ ] **Step 6: Commit parcial**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/FilaImportacionEditableVmBase.cs \
        src/StockApp.Presentation/ViewModels/Finanzas/FilaGastoEditableVm.cs \
        src/StockApp.Presentation/ViewModels/Finanzas/FilaIngresoEditableVm.cs \
        src/StockApp.Presentation/ViewModels/Finanzas/FilaLineaPoaEditableVm.cs \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/FilaImportacionEditableVmBaseTests.cs
git commit -m "refactor(finanzas): extrae FilaImportacionEditableVmBase con el mecanismo de error de servidor (F5d Entrega 2)"
```

- [ ] **Step 7: Escribir el test que falla — descomposición del 400 y salto de pestaña**

```csharp
// Agregar a NuevaImportacionViewModelTests.cs:

[Fact]
public async Task ConfirmarAsync_Error400EnGastos_MarcaLaFilaYSaltaALaPestanaDeGastos()
{
    var (vm, service, _, _) = Crear();
    var gasto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
        {
            ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
        }));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);

    await vm.ConfirmarCommand.ExecuteAsync(null);

    Assert.True(vm.FilasGasto[0].TieneErrorServidor);
    Assert.Contains("Fuente", vm.FilasGasto[0].MensajeErrorServidor);
    Assert.Equal(0, vm.PestanaSeleccionada);
}

[Fact]
public async Task ConfirmarAsync_Error400EnLineasPoa_SaltaALaPestanaDeLineasPoa()
{
    var (vm, service, _, _) = Crear();
    var linea = new LineaPoaAnalizadaDto(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto>(),
            new List<LineaPoaAnalizadaDto> { linea },
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(0, 0, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
        {
            ["LineasPoa[0].Programa"] = new[] { "El programa es obligatorio." },
        }));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    vm.FilasLineaPoa[0].Programa = "Obras"; // pasa la validación CLIENTE, el 400 simula un error de SERVIDOR (p.ej. carrera con otro import)

    await vm.ConfirmarCommand.ExecuteAsync(null);

    Assert.True(vm.FilasLineaPoa[0].TieneErrorServidor);
    Assert.Equal(2, vm.PestanaSeleccionada);
}

[Fact]
public async Task ConfirmarAsync_ReintentoDespuesDeCorregir_LimpiaElErrorDeServidorAnterior()
{
    var (vm, service, _, _) = Crear();
    var gasto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto>(),
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.SetupSequence(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
        {
            ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
        }))
        .ReturnsAsync(new ResultadoConfirmacionDto(
            Guid.NewGuid(), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, new List<ConflictoGastoDto>()));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    await vm.ConfirmarCommand.ExecuteAsync(null);
    Assert.True(vm.FilasGasto[0].TieneErrorServidor);

    await vm.ConfirmarCommand.ExecuteAsync(null);

    Assert.False(vm.FilasGasto[0].TieneErrorServidor);
}

/// <summary>
/// El orden de enumeración de IReadOnlyDictionary (Dictionary por debajo) NO está garantizado por
/// .NET — no se puede usar "la primera clave del diccionario" para decidir la pestaña. Este test
/// inserta la clave de LineasPoa ANTES que la de Gastos en el diccionario literal (a propósito,
/// para que una implementación ingenua basada en orden de enumeración falle) y verifica que la
/// pestaña resultante es igual siempre: Gastos (orden fijo Gastos→Ingresos→LineasPoa, índice menor
/// dentro del mismo tipo), sin importar el orden de inserción.
/// </summary>
[Fact]
public async Task ConfirmarAsync_Error400ConClavesDeVariasPestanas_SaltaALaPestanaDeMenorOrden()
{
    var (vm, service, _, _) = Crear();
    var gasto = new GastoAnalizadoDto(
        HojaOrigen: "MARZO", NumeroFila: 1,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Fecha: new DateOnly(2026, 3, 1), Monto: 1000m,
        Proveedor: "ACME SA", ProveedorNuevo: false,
        NumeroFactura: "F-1", NumeroOrden: null,
        Detalle: "Compra", Destino: null,
        Fuente: "Rentas Generales", FuenteDesconocida: false,
        CodigoRubro: 10, Rubro: "Materiales", RubroDesconocido: false,
        LineaPoaAsignada: null);
    var linea = new LineaPoaAnalizadaDto(
        Hoja: "COMPOSTERAS", Ejercicio: 2026, EsNueva: true,
        Estado: EstadoFila.Ok, Motivos: new List<MotivoEstado>(),
        Literal: "C", FuenteDesconocida: false, Presupuesto: 1000m, SaldoPlanilla: 1000m,
        Movimientos: new List<MovimientoPoaAnalizadoDto>());
    service.Setup(s => s.AnalizarAsync(
            It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<int>()))
        .ReturnsAsync(new ResultadoAnalisisDto(
            new List<IngresoAnalizadoDto>(), new List<GastoAnalizadoDto> { gasto },
            new List<LineaPoaAnalizadaDto> { linea },
            new MaestrosNuevosDto(new List<string>(), new List<string>(), new List<CodigoRubroNuevoDto>()),
            new ResumenAnalisisDto(1, 1, 0, 0, 0, 0, 0),
            new SaldosTotalesPoaOds(0m, 0m)));
    service.Setup(s => s.ConfirmarAsync(It.IsAny<ConfirmarImportacionDto>()))
        .ThrowsAsync(new ValidacionImportacionException(new Dictionary<string, string[]>
        {
            // A propósito en este orden: LineasPoa insertada ANTES que Gastos.
            ["LineasPoa[0].Programa"] = new[] { "El programa es obligatorio." },
            ["Gastos[0].Fuente"] = new[] { "La fuente no existe en el catálogo." },
        }));
    vm.GastosNombreArchivo = "gastos.ods";
    vm.PoaNombreArchivo = "poa.ods";
    await vm.AnalizarCommand.ExecuteAsync(null);
    vm.FilasLineaPoa[0].Programa = "Obras"; // pasa la validación cliente

    await vm.ConfirmarCommand.ExecuteAsync(null);

    Assert.True(vm.FilasGasto[0].TieneErrorServidor);
    Assert.True(vm.FilasLineaPoa[0].TieneErrorServidor);
    Assert.Equal(0, vm.PestanaSeleccionada); // Gastos (orden fijo), NO LineasPoa (que apareció primero en el diccionario)
}
```

- [ ] **Step 8: Correr — deben fallar en compilación** (`PestanaSeleccionada` no existe todavía en el VM)

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: FAIL — `CS1061: 'NuevaImportacionViewModel' no contiene una definición para 'PestanaSeleccionada'`.

- [ ] **Step 9: Implementar la descomposición en el VM**

```csharp
// src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs
// using nuevo:
using System.Text.RegularExpressions;
```

```csharp
// Nueva propiedad, junto a PasoActual:
[ObservableProperty] private int _pestanaSeleccionada;
```

```csharp
// Reemplaza el catch de ValidacionImportacionException dentro de ConfirmarAsync
// (dentro del bloque reescrito en Task 10 Step 3):
[RelayCommand(CanExecute = nameof(PuedeConfirmar))]
private async Task ConfirmarAsync()
{
    if (_analisis is null) return;

    LimpiarErroresServidor();
    var dto = MapearAConfirmacion(FilasGasto, FilasIngreso, FilasLineaPoa, ProveedoresNuevos, FuentesNuevas, RubrosNuevos, Ejercicio, Forzar);

    try
    {
        ResultadoConfirmacion = await _service.ConfirmarAsync(dto);
        Conflictos.Clear();
        foreach (var c in ResultadoConfirmacion.Conflictos)
            Conflictos.Add(ConflictoGastoFila.Desde(c));
        PasoActual = PasoWizardImportacion.Resultado;
    }
    catch (ValidacionImportacionException vex)
    {
        DecomponerErroresServidor(vex);
        await _confirmacion.InformarAsync("El servidor encontró errores de validación — revisá las celdas resaltadas.");
    }
    catch (Exception ex)
    {
        await _confirmacion.InformarAsync(ex.Message);
    }
}
```

```csharp
// Nuevos métodos privados, junto a FormatearErroresValidacion (que queda SIN uso — Step 10 lo elimina):
private static readonly Regex PatronErrorCampo = new(@"^(Gastos|Ingresos|LineasPoa)\[(\d+)\]\.(.+)$");

// Orden fijo de pestañas — usado para decidir determinísticamente a cuál saltar cuando el 400
// trae errores de varios tipos a la vez (ver nota abajo, IReadOnlyDictionary no garantiza orden).
private static readonly Dictionary<string, int> OrdenPestana = new()
{
    ["Gastos"] = 0,
    ["Ingresos"] = 1,
    ["LineasPoa"] = 2,
};

/// <summary>Descompone ValidacionImportacionException.Errores ("Tipo[i].Campo" -> mensajes) en
/// errores por fila (F5d Entrega 2 Task 11) y salta a la pestaña de la clave "menor". IMPORTANTE:
/// IReadOnlyDictionary (Dictionary por debajo) NO garantiza orden de enumeración en .NET — "la
/// primera clave del diccionario" NO es determinístico, así que en vez de usar el orden de
/// enumeración se ordenan las claves válidas por tipo (Gastos→Ingresos→LineasPoa, vía
/// OrdenPestana) y, dentro del mismo tipo, por índice ascendente; se salta a la pestaña de la
/// clave resultante más chica.</summary>
private void DecomponerErroresServidor(ValidacionImportacionException vex)
{
    foreach (var (clave, mensajes) in vex.Errores)
    {
        var match = PatronErrorCampo.Match(clave);
        if (!match.Success) continue;

        var indice = int.Parse(match.Groups[2].Value);
        var mensaje = $"{match.Groups[3].Value}: {string.Join("; ", mensajes)}";

        switch (match.Groups[1].Value)
        {
            case "Gastos" when indice < FilasGasto.Count:
                FilasGasto[indice].AgregarErrorServidor(mensaje);
                break;
            case "Ingresos" when indice < FilasIngreso.Count:
                FilasIngreso[indice].AgregarErrorServidor(mensaje);
                break;
            case "LineasPoa" when indice < FilasLineaPoa.Count:
                FilasLineaPoa[indice].AgregarErrorServidor(mensaje);
                break;
        }
    }

    var claveMenor = vex.Errores.Keys
        .Select(clave => PatronErrorCampo.Match(clave))
        .Where(match => match.Success && OrdenPestana.ContainsKey(match.Groups[1].Value))
        .OrderBy(match => OrdenPestana[match.Groups[1].Value])
        .ThenBy(match => int.Parse(match.Groups[2].Value))
        .FirstOrDefault();

    if (claveMenor is not null)
    {
        PestanaSeleccionada = OrdenPestana[claveMenor.Groups[1].Value];
    }
}

private void LimpiarErroresServidor()
{
    foreach (var f in FilasGasto) f.LimpiarErrorServidor();
    foreach (var f in FilasIngreso) f.LimpiarErrorServidor();
    foreach (var f in FilasLineaPoa) f.LimpiarErrorServidor();
}
```

- [ ] **Step 10: Eliminar `FormatearErroresValidacion`** (líneas 205-213 de Entrega 1, ahora sin uso — el mensaje genérico de Step 9 lo reemplaza)

```csharp
// Borrar el método completo:
// private static string FormatearErroresValidacion(ValidacionImportacionException vex) => ...
```

- [ ] **Step 11: Correr — deben pasar**

Run: `dotnet test tests/StockApp.Presentation.Tests --filter "FullyQualifiedName~NuevaImportacionViewModelTests"`
Expected: PASS.

- [ ] **Step 12: Wiring del `TabControl` en el XAML — `SelectedIndex` TwoWay + candado visual del error de servidor**

```xml
<!-- src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml -->
<!-- Reemplaza la apertura del TabControl del Paso 2 (original línea 66, sin tocar hasta ahora): -->
<TabControl SelectedIndex="{Binding PestanaSeleccionada, Mode=TwoWay}">
```

```xml
<!-- Agregar, en el Style compartido de DataGridRow (UserControl.Styles, líneas 12-19 originales) un
     segundo Setter para el tooltip del error de servidor — mismo x:CompileBindings="False" que ya
     tiene el Style (ítem heterogéneo entre los 3 grids): -->
<Style Selector="DataGridRow" x:CompileBindings="False">
    <Setter Property="Background" Value="{Binding Estado, Converter={x:Static conv:EstadoFilaBrushConverter.Instance}}" />
    <Setter Property="ToolTip.Tip" Value="{Binding MensajeErrorServidor}" />
</Style>
```

- [ ] **Step 13: Compilar**

Run: `dotnet build src/StockApp.Presentation`
Expected: `Build succeeded.`

- [ ] **Step 14: Verificación orgánica** — provocar un 400 real (p. ej. confirmar dos veces la misma importación sin `Forzar`, o desconectar el server justo después de pasar la validación cliente) y confirmar: el diálogo muestra el mensaje genérico, la pestaña salta a la que corresponde, y al pasar el mouse sobre la fila marcada aparece el tooltip con el detalle por campo.

- [ ] **Step 15: Commit**

```bash
git add src/StockApp.Presentation/ViewModels/Finanzas/NuevaImportacionViewModel.cs \
        src/StockApp.Presentation/Views/Finanzas/NuevaImportacionView.axaml \
        tests/StockApp.Presentation.Tests/ViewModels/Finanzas/NuevaImportacionViewModelTests.cs
git commit -m "feat(finanzas): descompone el error 400 por fila y salta a la pestana correspondiente (F5d Entrega 2)"
```

---

## Self-Review

**1. Cobertura del spec** (`docs/superpowers/specs/2026-07-24-finanzas-f5d-entrega2-grilla-editable-design.md`), sección por sección:

- §2.1 Scope completo (edición + maestros + líneas POA + validación + error 400) → cubierto por Tasks 1-11 en conjunto.
- §2.2 Condición/Vencimiento editables con heurística como sugerencia → Task 3 (`Desde` parte de la heurística, `Condicion`/`FechaVencimiento` sin `EsEditable*`, siempre habilitadas), Task 7 (combo/date-picker), Task 10 (mapeo usa el valor de la fila, no la heurística).
- §2.3 Celdas Ok bloqueadas + desbloqueo por fila ✎ → Tasks 3/4 (`EsEditable*`, `Desbloqueada`, `DesbloquearCommand`), Task 7/8 (candado `mdi-lock` + botón `mdi-pencil`). `FilaLineaPoaEditableVm` deliberadamente NO tiene desbloqueo (Task 5, justificado: `Programa` no viene precargado nunca).
- §2.4 Líneas POA nuevas, flag `EsNueva` → Task 1 (backend) + Task 5/6 (agrupado) + Task 8 (XAML) + Task 10 (mapeo a confirmación).
- §3 Modelo VM por fila → Tasks 3, 4, 5 (+ Task 11 extrae la base común `FilaImportacionEditableVmBase`).
- §4 Grilla híbrida, patrón Avalonia 12 → Task 7 (Gastos, con TODOS los controles por tipo de celda: date-picker+converter, ComboBox IsEditable, TextBox+DecimalOpcionalConverter, combo enum), Task 8 (Ingresos/LineasPoa).
- §5 Maestros nuevos automáticos + tablero con Nombre obligatorio → Task 9.
- §6 Líneas POA nuevas (backend + frontend) → Task 1 + Task 5/6/8/10, con el gap real detectado durante la investigación (`ILineaPoaRepository` sin filtro por ejercicio, resuelto con `.Where(l => l.Ejercicio == ejercicio)` client-side, mismo criterio que fuentes/proveedores/rubros).
- §7 Validación por celda, `WithDataAnnotationsValidation` obligatorio, gating relajado → Task 2 (activación) + Tasks 3-5 (DataAnnotations por fila) + Task 6 (gating: `!HayFilasConErrores()`) + Task 9 (extiende el gating a `RubrosNuevos`).
- §8 Error 400 — descomposición visual → Task 11, con alcance reducido CONSCIENTE (fila, no celda individual — documentado como decisión de diseño en el propio Task, motivo técnico real: `ObservableValidator` no expone API pública para inyectar errores externos).
- §9 Gotchas Avalonia 12: `WithDataAnnotationsValidation` (Task 2), `IsReadOnly` explícito por columna (Task 7/8, en el `<DataGrid>` Y en cada columna), converter `DateOnly?↔DateTimeOffset?` (Task 2), regresión del bug #232 cubierta por test headless en `StockApp.Presentation.UiTests` (Task 7 Step 10, Task 8 Step 6) — la verificación orgánica en la app real queda como chequeo final de UX (Task 7 Step 11, Task 8 Step 7), no como el único gate. **GAP no cubierto**: "congelar el sort durante la edición" (mencionado como "considerar", no como requisito duro) — NO se agregó ninguna task para esto; si la verificación orgánica de Task 7/8 revela que el salto de fila al ordenar durante edición molesta en la práctica, es la única deuda pendiente de §9 y amerita un plan chico aparte.
- §10 Contrato relevante → verificado contra el código real en cada Task (DTOs con nombres/orden de campos exactos, ver punto 3 abajo).
- §11 Fuera de alcance → respetado: ningún Task edita movimientos POA a nivel submovimiento, ningún Task toca conflictos/contadores de historial/backup.

**2. Barrido de placeholders**: sin resultados — el único patrón que matcheó el grep (`TODO`) son usos legítimos de la palabra española "todo/TODOS" (p. ej. "TODOS los tests"), no el marcador en inglés. Cada Task tiene código C#/XAML real y completo, sin "similar a la Task N" ni fragmentos truncados.

**3. Consistencia de tipos — verificado contra el código real, no contra memoria**: se confirmaron uno por uno los campos/orden exacto de `GastoAnalizadoDto`, `IngresoAnalizadoDto`, `LineaPoaAnalizadaDto`, `ResultadoAnalisisDto`, `ResumenAnalisisDto`, `MaestrosNuevosDto`, `GastoConfirmarDto`, `IngresoConfirmarDto`, `LineaPoaConfirmarDto`, `AsignacionConfirmarDto`, `RubroNuevoConfirmarDto`, `MaestrosNuevosConfirmarDto`, `ConfirmarImportacionDto`, `ResultadoConfirmacionDto` (16 parámetros: `Guid` + 14 `int` + `IReadOnlyList<ConflictoGastoDto>`) contra `src/StockApp.Application/Finanzas/*.cs` — sin desajustes. Se detectaron y corrigieron DOS bugs reales durante la escritura de este plan (no simulados):
   - **Task 7**: el constructor de `NuevaImportacionViewModel` ganaba 3 parámetros nuevos pero el helper `Crear()` de `NuevaImportacionViewModelTests.cs` (existente desde Entrega 1, con DOS call-sites: `Crear()` y `CrearEnPasoRevisarAsync`) seguía llamando al constructor de 3 argumentos — hubiera roto la compilación de TODO el archivo de tests, incluidos los tests de Tasks 6 y anteriores. Se agregó el Step 2 de Task 7 con el fix completo de ambos call-sites.
   - **Task 7 (XAML)**: un intento inicial de comparar `Condicion == CondicionPago.Credito` en el binding de `IsEnabled` del date-picker de Vencimiento usaba `{x:Static sys:Enum.Parse(...)}}`, sintaxis inválida en Avalonia (`x:Static` no invoca métodos). Corregido en el mismo Step, referenciando el valor de enum directo (`{x:Static enums:CondicionPago.Credito}`), mismo patrón ya usado por el propio archivo para `PasoWizardImportacion`.
   - Numeración de Steps: la inserción del fix de `Crear()` en Task 7 dejó dos "Step 4" duplicados en la primera pasada de escritura — renumerados 5-11 y las dos referencias cruzadas ("Task 7 Step 9") corregidas a "Task 7 Step 10" en Task 8.

**4. Decisiones no especificadas por el spec, documentadas explícitamente en el plan** (para que el implementador no las reinterprete): `EsNueva` no genera `MotivoEstado`/`Advertencia` (Task 1); auto-declaración de maestro nuevo aplica sólo a Proveedor/Fuente, no a Rubro (Task 9, por la separación Código/Nombre de `RubroGasto`); descomposición del 400 es por FILA, no por celda individual (Task 11).

---
